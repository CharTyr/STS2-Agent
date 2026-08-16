using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using STS2AIAgent.Config;

namespace STS2AIAgent.Llm;

internal sealed class OpenAiCompatibleClient : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly LlmEndpoint _endpoint;

    public OpenAiCompatibleClient(LlmEndpoint endpoint, HttpMessageHandler? handler = null, HttpClient? httpClient = null)
    {
        _endpoint = endpoint;
        if (httpClient != null)
        {
            _http = httpClient;
        }
        else if (handler != null)
        {
            _http = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromMinutes(3)
            };
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        }
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
        }

        return await SendCompletionAsync(body, request.Stream, cancellationToken);
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

        var completion = await SendCompletionAsync(body, stream: false, cancellationToken);
        return string.IsNullOrWhiteSpace(completion.Content) ? "ok" : completion.Content.Trim();
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
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendOnceAsync(body, stream, cancellationToken);
        }
        catch (LlmException ex) when (stream && ShouldRetryWithoutStream(ex))
        {
            body["stream"] = false;
            return await SendOnceAsync(body, stream: false, cancellationToken);
        }
    }

    private async Task<LlmCompletion> SendOnceAsync(
        Dictionary<string, object?> body,
        bool stream,
        CancellationToken cancellationToken)
    {
        var url = ResolveCompletionsUrl(_endpoint.BaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_endpoint.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _endpoint.ApiKey.Trim());
        }

        HttpResponseMessage response;
        try
        {
            var completionOption = stream
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;
            response = await _http.SendAsync(request, completionOption, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new LlmException($"LLM request failed: {ex.Message}", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmException(FormatError((int)response.StatusCode, payload));
            }

            if (stream && LooksLikeSse(payload))
            {
                return ParseSsePayload(payload);
            }

            return ParseCompletion(payload);
        }
    }

    internal static bool LooksLikeSse(string payload)
    {
        var trimmed = payload.AsSpan().TrimStart();
        return trimmed.StartsWith("data:", StringComparison.Ordinal);
    }

    internal static LlmCompletion ParseSsePayload(string payload)
    {
        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        var toolCalls = new SortedDictionary<int, SseToolCall>();

        foreach (var rawLine in payload.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line.Length <= 5 ? string.Empty : line[5..].Trim();
            if (data.Length == 0 || data == "[DONE]")
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                    choices.ValueKind != JsonValueKind.Array ||
                    choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta))
                {
                    AccumulateDelta(delta, content, reasoning, toolCalls);
                }
                else if (choice.TryGetProperty("message", out var message))
                {
                    var parsed = ParseCompletion(data);
                    if (!string.IsNullOrEmpty(parsed.Content))
                    {
                        content.Append(parsed.Content);
                    }

                    if (!string.IsNullOrEmpty(parsed.Reasoning))
                    {
                        reasoning.Append(parsed.Reasoning);
                    }

                    for (var i = 0; i < parsed.ToolCalls.Count; i++)
                    {
                        var call = parsed.ToolCalls[i];
                        toolCalls[i] = new SseToolCall
                        {
                            Id = call.Id,
                            Name = call.Name,
                            Arguments = new StringBuilder(call.ArgumentsJson)
                        };
                    }
                }
            }
        }

        return new LlmCompletion
        {
            Content = content.Length == 0 ? null : content.ToString(),
            Reasoning = reasoning.Length == 0 ? null : reasoning.ToString(),
            ToolCalls = toolCalls.Values.Select(call => new LlmToolCall
            {
                Id = call.Id ?? string.Empty,
                Name = call.Name ?? string.Empty,
                ArgumentsJson = call.Arguments.Length == 0 ? "{}" : call.Arguments.ToString()
            }).Where(call => !string.IsNullOrWhiteSpace(call.Id) && !string.IsNullOrWhiteSpace(call.Name)).ToArray()
        };
    }

    private static void AccumulateDelta(
        JsonElement delta,
        StringBuilder content,
        StringBuilder reasoning,
        SortedDictionary<int, SseToolCall> toolCalls)
    {
        if (delta.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
        {
            content.Append(contentElement.GetString());
        }

        var reasoningText = ReadOptionalString(delta, "reasoning_content") ?? ReadOptionalString(delta, "reasoning");
        if (!string.IsNullOrEmpty(reasoningText))
        {
            reasoning.Append(reasoningText);
        }

        if (!delta.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var call in calls.EnumerateArray())
        {
            var index = 0;
            if (call.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out var parsedIndex))
            {
                index = parsedIndex;
            }

            if (!toolCalls.TryGetValue(index, out var acc))
            {
                acc = new SseToolCall();
                toolCalls[index] = acc;
            }

            if (call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
            {
                acc.Id = idElement.GetString();
            }

            if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (function.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                acc.Name = nameElement.GetString();
            }

            if (function.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.String)
            {
                acc.Arguments.Append(argsElement.GetString());
            }
        }
    }

    private static bool ShouldRetryWithoutStream(LlmException ex)
    {
        var message = ex.Message;
        return message.Contains("HTTP 400", StringComparison.Ordinal) ||
               message.Contains("HTTP 415", StringComparison.Ordinal) ||
               message.Contains("HTTP 422", StringComparison.Ordinal) ||
               message.Contains("stream", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SseToolCall
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public StringBuilder Arguments { get; set; } = new();
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
        return new LlmCompletion
        {
            Content = content,
            Reasoning = reasoning,
            ToolCalls = toolCalls
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

    private static string? ReadOptionalString(JsonElement message, string name)
    {
        return message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<LlmToolCall> ReadToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LlmToolCall>();
        }

        var result = new List<LlmToolCall>();
        foreach (var call in toolCalls.EnumerateArray())
        {
            var id = call.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var function = call.TryGetProperty("function", out var functionElement) ? functionElement : default;
            var name = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var args = function.ValueKind == JsonValueKind.Object && function.TryGetProperty("arguments", out var argsElement)
                ? argsElement.GetString()
                : "{}";
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
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
