using System.Text;
using System.Text.Json;

namespace STS2AIAgent.Llm;

/// <summary>
/// The state of one server-sent-events completion, fed one line at a time.
/// </summary>
/// <remarks>
/// Extracted from the client's whole-body SSE parser when a thinking model's reasoning had to reach the
/// overlay while the reply was still arriving. The parser used to be handed the complete body, so
/// nothing could be reported before the provider finished; the accumulation rules are unchanged -- the
/// same delta merging, the same tolerance for a whole <c>message</c> choice instead of a <c>delta</c>,
/// the same synthetic tool-call ids -- and the whole-body parser is now this accumulator fed in one
/// pass, so a streaming read and a buffered read cannot disagree about what the provider sent.
/// </remarks>
internal sealed class SseCompletionAccumulator
{
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly SortedDictionary<int, SseToolCall> _toolCalls = new();
    private string? _finishReason;
    private LlmUsage? _usage;

    /// <summary>
    /// The reasoning accumulated so far. A streaming caller reports this to the player as the model
    /// thinks, which is the whole point of reading the body incrementally.
    /// </summary>
    public string ReasoningSoFar => _reasoning.ToString();

    /// <summary>
    /// The first <paramref name="maxChars"/> characters of the reasoning so far, with an ellipsis when
    /// there is more. The display callback asks for this instead of <see cref="ReasoningSoFar"/>:
    /// rebuilding the whole accumulation on every delta is O(n²) copying for a long thought, while the
    /// preview costs only its own size. The final completion still reads <see cref="ReasoningSoFar"/>,
    /// so the recorded bubble and the provider echo keep the full text.
    /// </summary>
    public string ReasoningPreview(int maxChars)
    {
        if (_reasoning.Length <= maxChars)
        {
            return _reasoning.ToString();
        }

        var chars = new char[maxChars];
        _reasoning.CopyTo(0, chars, 0, maxChars);
        return new string(chars) + "…";
    }

    /// <summary>Feeds one raw line of the response body, with or without its trailing newline.</summary>
    /// <returns>
    /// True when this line added reasoning text -- the only partial a display callback cares about, so
    /// content and tool-argument chunks never wake it.
    /// </returns>
    public bool AppendLine(string rawLine)
    {
        var line = rawLine.TrimEnd('\r');
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return false;
        }

        var data = line.Length <= 5 ? string.Empty : line[5..].Trim();
        if (data.Length == 0 || data == "[DONE]")
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            // A malformed chunk is skipped, not fatal: one bad keep-alive line must not kill a turn.
            return false;
        }

        using (document)
        {
            if (OpenAiCompatibleClient.ReadUsage(document.RootElement) is { } chunkUsage)
            {
                _usage = chunkUsage;
            }

            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return false;
            }

            var choice = choices[0];
            if (string.IsNullOrEmpty(_finishReason) &&
                choice.TryGetProperty("finish_reason", out var finishElement) &&
                finishElement.ValueKind == JsonValueKind.String)
            {
                _finishReason = finishElement.GetString();
            }

            var reasoningBefore = _reasoning.Length;
            if (choice.TryGetProperty("delta", out var delta))
            {
                AccumulateDelta(delta, _content, _reasoning, _toolCalls);
            }
            else if (choice.TryGetProperty("message", out _))
            {
                var parsed = OpenAiCompatibleClient.ParseCompletion(data);
                if (!string.IsNullOrEmpty(parsed.Content))
                {
                    _content.Append(parsed.Content);
                }

                if (!string.IsNullOrEmpty(parsed.Reasoning))
                {
                    _reasoning.Append(parsed.Reasoning);
                }

                if (string.IsNullOrEmpty(_finishReason) && !string.IsNullOrEmpty(parsed.FinishReason))
                {
                    _finishReason = parsed.FinishReason;
                }

                for (var i = 0; i < parsed.ToolCalls.Count; i++)
                {
                    var call = parsed.ToolCalls[i];
                    _toolCalls[i] = new SseToolCall
                    {
                        Id = call.Id,
                        Name = call.Name,
                        Arguments = new StringBuilder(call.ArgumentsJson)
                    };
                }
            }

            return _reasoning.Length > reasoningBefore;
        }
    }

    /// <summary>The completion this stream described, in the same shape a non-streamed body produces.</summary>
    public LlmCompletion Build()
    {
        return new LlmCompletion
        {
            Content = _content.Length == 0 ? null : _content.ToString(),
            Reasoning = _reasoning.Length == 0 ? null : _reasoning.ToString(),
            // A stream that never sent an id (some local servers) still carried a real call: give it a
            // stable synthetic id instead of dropping the model's act on the floor.
            ToolCalls = _toolCalls.Select(pair => new LlmToolCall
            {
                Id = string.IsNullOrWhiteSpace(pair.Value.Id) ? $"call_{pair.Key + 1}" : pair.Value.Id!,
                Name = pair.Value.Name ?? string.Empty,
                ArgumentsJson = pair.Value.Arguments.Length == 0 ? "{}" : pair.Value.Arguments.ToString()
            }).Where(call => !string.IsNullOrWhiteSpace(call.Name)).ToArray(),
            FinishReason = _finishReason,
            Usage = _usage
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

        var reasoningText = OpenAiCompatibleClient.ReadOptionalString(delta, "reasoning_content")
            ?? OpenAiCompatibleClient.ReadOptionalString(delta, "reasoning");
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

            // Some providers repeat "id": "" on every later chunk; only a non-empty id may set it.
            if (call.TryGetProperty("id", out var idElement) &&
                OpenAiCompatibleClient.ReadLooseString(idElement) is { Length: > 0 } streamedId)
            {
                acc.Id = streamedId;
            }

            if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (function.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                acc.Name = nameElement.GetString();
            }

            if (function.TryGetProperty("arguments", out var argsElement))
            {
                if (argsElement.ValueKind == JsonValueKind.String)
                {
                    acc.Arguments.Append(argsElement.GetString());
                }
                else if (argsElement.ValueKind == JsonValueKind.Object)
                {
                    // A whole-object arguments chunk (Ollama-style) replaces rather than appends.
                    acc.Arguments.Clear().Append(argsElement.GetRawText());
                }
            }
        }
    }

    private sealed class SseToolCall
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public StringBuilder Arguments { get; set; } = new();
    }
}
