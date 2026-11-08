using System.Text.Json;

namespace STS2AIAgent.Llm;

/// <summary>
/// Turns one streamed completion into a capability verdict (W7): did the provider deliver a tool call,
/// and did that call carry every argument the play schema needs?
/// </summary>
/// <remarks>
/// Pure because the strings below are the whole contract. A truncated gateway produces syntactically
/// valid JSON (<c>{"action":"play_card"}</c>) whose missing index is only visible by comparing the
/// keys against the fields the action needs, so this is a field check, not a parse check.
/// </remarks>
internal static class ToolProbeArgs
{
    public static StreamingToolArgsProbeResult Evaluate(
        LlmCompletion completion,
        string toolName,
        IReadOnlyList<string> requiredFields)
    {
        var call = completion.ToolCalls.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (call == null)
        {
            return new StreamingToolArgsProbeResult(
                StreamingToolArgsVerdict.NoToolCall,
                Error: "the streamed reply carried no " + toolName + " call");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
        }
        catch (JsonException ex)
        {
            // An unterminated fragment is the classic truncated stream: report it as incomplete rather
            // than letting a parse exception end the model test.
            return new StreamingToolArgsProbeResult(
                StreamingToolArgsVerdict.Incomplete,
                call.ArgumentsJson,
                Error: "streamed tool arguments are not valid JSON: " + ex.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new StreamingToolArgsProbeResult(
                    StreamingToolArgsVerdict.Incomplete,
                    call.ArgumentsJson,
                    Error: "streamed tool arguments were not a JSON object");
            }

            var missing = requiredFields
                .Where(field => !HasUsableValue(document.RootElement, field))
                .ToArray();
            return missing.Length == 0
                ? new StreamingToolArgsProbeResult(StreamingToolArgsVerdict.Complete, call.ArgumentsJson)
                : new StreamingToolArgsProbeResult(
                    StreamingToolArgsVerdict.Incomplete,
                    call.ArgumentsJson,
                    Error: "streamed tool arguments are missing " + string.Join(", ", missing));
        }
    }

    private static bool HasUsableValue(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            _ => true
        };
    }
}
