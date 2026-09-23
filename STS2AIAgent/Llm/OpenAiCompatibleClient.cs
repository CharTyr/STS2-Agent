using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using STS2AIAgent.Config;

namespace STS2AIAgent.Llm;

internal sealed class OpenAiCompatibleClient : ILlmClient
{
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly LlmEndpoint _endpoint;

    private readonly TimeSpan _requestTimeout;

    public OpenAiCompatibleClient(LlmEndpoint endpoint, HttpMessageHandler? handler = null, HttpClient? httpClient = null, TimeSpan? requestTimeout = null)
    {
        _endpoint = endpoint;
        _requestTimeout = NormalizeRequestTimeout(requestTimeout);
        if (httpClient != null)
        {
            _http = httpClient;
        }
        else if (handler != null)
        {
            _http = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = DefaultRequestTimeout + TimeSpan.FromMinutes(1)
            };
        }
        else
        {
            _http = new HttpClient { Timeout = DefaultRequestTimeout + TimeSpan.FromMinutes(1) };
        }
    }

    private static TimeSpan NormalizeRequestTimeout(TimeSpan? requestTimeout)
    {
        return requestTimeout is { } timeout && timeout > TimeSpan.Zero
            ? timeout
            : DefaultRequestTimeout;
    }

    public async Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var thinking = ThinkingRequestBuilder.Build(request.Model, request.ThinkingMode, request.Thinking);
        var messages = request.Messages.Select(message => ToMessageDto(message)).ToList();
        if (!string.IsNullOrWhiteSpace(thinking.PromptSuffix) && messages.Count > 0 && messages[0].Role == "system")
        {
            messages[0].Content = AppendPrompt(messages[0].Content, thinking.PromptSuffix);
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages
        };

        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(ToToolDto).ToArray();
        }

        if (!string.IsNullOrWhiteSpace(thinking.ReasoningEffort))
        {
            body["reasoning_effort"] = thinking.ReasoningEffort;
        }

        if (thinking.DeepSeekThinking != null)
        {
            body["thinking"] = thinking.DeepSeekThinking;
            body["extra_body"] = new Dictionary<string, object?>
            {
                ["thinking"] = thinking.DeepSeekThinking
            };
        }

        if (request.Stream)
        {
            body["stream"] = true;
            body["stream_options"] = new Dictionary<string, object?>
            {
                ["include_usage"] = true
            };
        }

        return await SendCompletionAsync(body, request.Stream, cancellationToken, request.OnReasoningDelta);
    }

    public async Task<string> PingAsync(string model, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new ChatMessageDto { Role = "user", Content = "Reply with the single word pong." }
            },
            ["max_tokens"] = 16
        };

        var completion = await SendCompletionAsync(body, stream: false, cancellationToken, onReasoningDelta: null);
        return string.IsNullOrWhiteSpace(completion.Content) ? "ok" : completion.Content.Trim();
    }

    /// <summary>
    /// Verifies the model actually calls tools, not just answers chat. The caller supplies the tool
    /// and the prompt: the per-model test passes the real <c>act</c> tool with a miniature game
    /// decision, so a provider that chokes on the actual play schema fails here rather than on a
    /// synthetic stand-in. tool_choice "required" is part of the probe: a provider that silently
    /// ignores the whole tools array fails even though its plain chat ping passed.
    /// </summary>
    public async Task<bool> ProbeToolCallingAsync(
        string model,
        LlmTool tool,
        string prompt,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new ChatMessageDto { Role = "user", Content = prompt }
            },
            ["tools"] = new[] { ToToolDto(tool) },
            ["tool_choice"] = "required",
            ["max_tokens"] = 256
        };

        try
        {
            var completion = await SendCompletionAsync(body, stream: false, cancellationToken, onReasoningDelta: null);
            return completion.ToolCalls.Count > 0;
        }
        catch (LlmException ex) when (ex.StatusCode is >= 400 and < 500 and not 408 and not 429)
        {
            // A 4xx about tools/tool_choice is the endpoint saying it cannot do tools -- a verdict,
            // not a failure. Anything transient (429, 5xx, timeout) keeps propagating so the caller
            // records an unknown capability instead of downgrading a working model on a bad day.
            throw new LlmToolProbeUnsupportedException(ex.Message, ex);
        }
    }

    public static string ResolveCompletionsUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            throw new LlmException("Endpoint base URL is empty.");
        }

        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed + "/chat/completions";
    }

    private async Task<LlmCompletion> SendCompletionAsync(
        Dictionary<string, object?> body,
        bool stream,
        CancellationToken cancellationToken,
        Action<string>? onReasoningDelta)
    {
        // Each step is a documented provider difference, applied at most once, and tried in a loop
        // rather than as nested catches. `ShouldRetryWithoutStream` accepts any HTTP 400, so with
        // two separate catches a 400 about the token-cap field would be answered by demoting the
        // stream instead of by renaming the field, and the rename would never be reached. Retrying
        // from the top means the second error is classified on its own terms.
        var streamDemotionTried = !stream;
        var tokenFieldRenameTried = false;

        while (true)
        {
            try
            {
                return await SendOnceAsync(body, stream, cancellationToken, onReasoningDelta);
            }
            catch (LlmException ex) when (!streamDemotionTried && ShouldRetryWithoutStream(ex))
            {
                body["stream"] = false;
                body.Remove("stream_options");
                stream = false;
                streamDemotionTried = true;
            }
            catch (LlmException ex) when (!tokenFieldRenameTried && MaxTokensField.IsUnsupportedParameterError(ex))
            {
                if (!MaxTokensField.RenameToCompletionTokens(body))
                {
                    // The server refused something, but this request has no max_tokens to rename,
                    // so the retry would be byte-identical. Surface the original failure.
                    throw;
                }

                tokenFieldRenameTried = true;
            }
        }
    }

    private async Task<LlmCompletion> SendOnceAsync(
        Dictionary<string, object?> body,
        bool stream,
        CancellationToken cancellationToken,
        Action<string>? onReasoningDelta)
    {
        var url = ResolveCompletionsUrl(_endpoint.BaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_endpoint.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _endpoint.ApiKey.Trim());
        }

        using var timeoutCts = new CancellationTokenSource(_requestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var linked = linkedCts.Token;
        HttpResponseMessage response;
        try
        {
            var completionOption = stream
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;
            response = await _http.SendAsync(request, completionOption, linked);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmException("LLM request timed out.", ex, 408);
        }
        catch (Exception ex) when (ex is HttpRequestException)
        {
            throw new LlmException($"LLM request failed: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorPayload = await ReadBodyAsync(response, cancellationToken, linked);
                throw new LlmException(FormatError((int)response.StatusCode, errorPayload), (int)response.StatusCode);
            }

            // A streamed reply is read as it arrives rather than buffered whole: that is what lets a
            // thinking model's reasoning reach the overlay while the turn is still in flight.
            return stream
                ? await ReadStreamedCompletionAsync(response, onReasoningDelta, cancellationToken, linked)
                : ParsePayload(await ReadBodyAsync(response, cancellationToken, linked));
        }
    }

    /// <summary>
    /// Reads a successful response body incrementally, reporting reasoning as it arrives.
    /// </summary>
    /// <remarks>
    /// The SSE/JSON decision is made on the first non-blank line and then held: a provider that ignores
    /// <c>stream: true</c> and answers with one JSON object is buffered and parsed exactly as before,
    /// while an SSE body is fed to <see cref="SseCompletionAccumulator"/> line by line. Only complete
    /// lines are consumed, so a chunk boundary in the middle of a line -- or of a UTF-8 character --
    /// cannot corrupt the parse.
    /// </remarks>
    private static async Task<LlmCompletion> ReadStreamedCompletionAsync(
        HttpResponseMessage response,
        Action<string>? onReasoningDelta,
        CancellationToken cancellationToken,
        CancellationToken linked)
    {
        Stream stream;
        try
        {
            stream = await response.Content.ReadAsStreamAsync(linked);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmException("LLM request timed out.", ex, 408);
        }

        var accumulator = new SseCompletionAccumulator();
        var buffered = new StringBuilder();
        bool? isSse = null;
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[4096];
        var chars = new char[4096];
        var line = new StringBuilder();

        void Consume(string raw)
        {
            if (isSse == null)
            {
                // Gateways open a stream with comment/keep-alive lines; blank separators carry nothing.
                var trimmed = raw.Trim();
                if (trimmed.Length == 0)
                {
                    return;
                }

                isSse = LooksLikeSseLine(trimmed);
            }

            if (isSse == true)
            {
                if (accumulator.AppendLine(raw))
                {
                    NotifyReasoningDelta(onReasoningDelta, accumulator.ReasoningSoFar);
                }

                return;
            }

            buffered.Append(raw).Append('\n');
        }

        using (stream)
        {
            while (true)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(bytes.AsMemory(0, bytes.Length), linked);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    throw new LlmException("LLM request timed out.", ex, 408);
                }

                if (read <= 0)
                {
                    break;
                }

                var decoded = decoder.GetChars(bytes, 0, read, chars, 0);
                for (var index = 0; index < decoded; index++)
                {
                    if (chars[index] != '\n')
                    {
                        line.Append(chars[index]);
                        continue;
                    }

                    Consume(line.ToString());
                    line.Clear();
                }
            }

            if (line.Length > 0)
            {
                Consume(line.ToString());
            }
        }

        return isSse == true ? accumulator.Build() : ParsePayload(buffered.ToString());
    }

    /// <summary>
    /// Hands the reasoning accumulated so far to the caller's display callback. A display callback must
    /// never be able to kill the provider stream, so a throw from it is swallowed here rather than
    /// surfacing as a failed turn.
    /// </summary>
    private static void NotifyReasoningDelta(Action<string>? callback, string reasoning)
    {
        if (callback == null || reasoning.Length == 0)
        {
            return;
        }

        try
        {
            callback(reasoning);
        }
        catch (Exception)
        {
        }
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        CancellationToken linked)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(linked);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmException("LLM request timed out.", ex, 408);
        }
    }

    private static LlmCompletion ParsePayload(string payload) =>
        LooksLikeSse(payload) ? ParseSsePayload(payload) : ParseCompletion(payload);

    internal static bool LooksLikeSse(string payload)
    {
        // Gateways (OpenRouter's ": OPENROUTER PROCESSING", retry hints) open the stream with SSE
        // comment/field lines before the first data line. Sniffing only for a leading "data:" sent
        // that body to the JSON parser and killed the turn with a raw JsonException.
        foreach (var rawLine in payload.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            return LooksLikeSseLine(line);
        }

        return false;
    }

    /// <summary>Whether one non-blank line is an SSE field, so a streaming read can decide per line.</summary>
    internal static bool LooksLikeSseLine(string line) =>
        line.StartsWith("data:", StringComparison.Ordinal)
        || line.StartsWith(':')
        || line.StartsWith("event:", StringComparison.Ordinal)
        || line.StartsWith("retry:", StringComparison.Ordinal)
        || line.StartsWith("id:", StringComparison.Ordinal);

    /// <summary>
    /// Parses a complete SSE body. Kept as the whole-body entry point -- and implemented on the same
    /// accumulator the streaming read uses, so the two cannot drift.
    /// </summary>
    internal static LlmCompletion ParseSsePayload(string payload)
    {
        var accumulator = new SseCompletionAccumulator();
        foreach (var rawLine in payload.Split('\n'))
        {
            accumulator.AppendLine(rawLine);
        }

        return accumulator.Build();
    }

    private static bool ShouldRetryWithoutStream(LlmException ex)
    {
        if (ex.StatusCode is not null and not 400 and not 415 and not 422) return false;
        var message = ex.Message;
        return message.Contains("HTTP 400", StringComparison.Ordinal) ||
               message.Contains("HTTP 415", StringComparison.Ordinal) ||
               message.Contains("HTTP 422", StringComparison.Ordinal) ||
               message.Contains("stream", StringComparison.OrdinalIgnoreCase);
    }

    internal static LlmCompletion ParseCompletion(string payload)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var errorElement))
        {
            throw new LlmException(ReadErrorMessage(errorElement, payload));
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new LlmException("LLM response did not contain choices.");
        }

        var message = choices[0].GetProperty("message");
        var content = ReadContent(message);
        var reasoning = ReadOptionalString(message, "reasoning_content") ?? ReadOptionalString(message, "reasoning");
        var toolCalls = ReadToolCalls(message);
        var finishReason = choices[0].TryGetProperty("finish_reason", out var finishElement) && finishElement.ValueKind == JsonValueKind.String
            ? finishElement.GetString()
            : null;
        var usage = ReadUsage(root);
        return new LlmCompletion
        {
            Content = content,
            Reasoning = reasoning,
            ToolCalls = toolCalls,
            FinishReason = finishReason,
            Usage = usage
        };
    }

    internal static object ToToolDto(LlmTool tool)
    {
        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters
            }
        };
    }

    private static ChatMessageDto ToMessageDto(LlmMessage message)
    {
        var dto = new ChatMessageDto
        {
            Role = message.Role,
            ToolCallId = message.ToolCallId
        };

        if (message.ToolCalls is { Count: > 0 })
        {
            dto.ToolCalls = message.ToolCalls.Select(call => new ChatToolCallDto
            {
                Id = call.Id,
                Type = "function",
                Function = new ChatToolFunctionDto
                {
                    Name = call.Name,
                    Arguments = call.ArgumentsJson
                }
            }).ToList();

            // Only on an assistant tool-call turn, and only what the provider itself returned: thinking
            // models 400 the next tool round without it, and it is never invented for anyone else.
            if (message.Role == "assistant" && !string.IsNullOrEmpty(message.Reasoning))
            {
                dto.ReasoningContent = message.Reasoning;
            }
        }

        if (message.ImageJpeg is { Length: > 0 })
        {
            var dataUrl = "data:image/jpeg;base64," + Convert.ToBase64String(message.ImageJpeg);
            dto.Content = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = message.Content ?? string.Empty
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "image_url",
                    ["image_url"] = new Dictionary<string, object?>
                    {
                        ["url"] = dataUrl
                    }
                }
            };
        }
        else
        {
            dto.Content = message.Content;
        }

        return dto;
    }

    private static object? AppendPrompt(object? content, string suffix)
    {
        if (content is string text)
        {
            return string.IsNullOrWhiteSpace(text) ? suffix : text + "\n\n" + suffix;
        }

        return content;
    }

    internal static LlmUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var prompt = ReadIntProperty(usageElement, "prompt_tokens");
        var completion = ReadIntProperty(usageElement, "completion_tokens");
        var total = ReadIntProperty(usageElement, "total_tokens");

        if (prompt == 0 && completion == 0 && total == 0)
        {
            return null;
        }

        return new LlmUsage
        {
            PromptTokens = prompt,
            CompletionTokens = completion,
            TotalTokens = total > 0 ? total : (prompt + completion)
        };
    }

    private static int ReadIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.TryGetInt32(out var val))
        {
            return val;
        }
        return 0;
    }

    private static string FormatError(int statusCode, string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return $"LLM HTTP {statusCode}: {ReadErrorMessage(error, payload)}";
            }
        }
        catch (JsonException)
        {
        }

        var snippet = payload.Length > 400 ? payload[..400] + "..." : payload;
        return $"LLM HTTP {statusCode}: {snippet}";
    }

    private static string ReadErrorMessage(JsonElement error, string fallback)
    {
        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString() ?? fallback;
        }

        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
        {
            return message.GetString() ?? fallback;
        }

        return fallback;
    }

    private static string? ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    parts.Add(part.GetString() ?? string.Empty);
                }
                else if (part.TryGetProperty("text", out var text))
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }

            return string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        return null;
    }

    internal static string? ReadOptionalString(JsonElement message, string name)
    {
        return message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    internal static string? ReadLooseString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }

    private static IReadOnlyList<LlmToolCall> ReadToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LlmToolCall>();
        }

        var result = new List<LlmToolCall>();
        var position = 0;
        foreach (var call in toolCalls.EnumerateArray())
        {
            position++;
            if (call.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Compat shims differ: a numeric id, an object-valued arguments, or no id at all. Reading
            // them with GetString() threw an unclassified InvalidOperationException that killed the
            // turn, and a missing id silently dropped the model's act call.
            var id = call.TryGetProperty("id", out var idElement) ? ReadLooseString(idElement) : null;
            var function = call.TryGetProperty("function", out var functionElement) ? functionElement : default;
            var name = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("name", out var nameElement)
                ? ReadLooseString(nameElement)
                : null;
            var args = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("arguments", out var argsElement)
                ? argsElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? argsElement.GetRawText()
                    : ReadLooseString(argsElement)
                : "{}";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                id = $"call_{position}";
            }

            result.Add(new LlmToolCall
            {
                Id = id,
                Name = name,
                ArgumentsJson = string.IsNullOrWhiteSpace(args) ? "{}" : args
            });
        }

        return result;
    }

    private sealed class ChatMessageDto
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Content { get; set; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ChatToolCallDto>? ToolCalls { get; set; }

        [JsonPropertyName("reasoning_content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningContent { get; set; }
    }

    private sealed class ChatToolCallDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public ChatToolFunctionDto Function { get; set; } = new();
    }

    private sealed class ChatToolFunctionDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; } = "{}";
    }
}
