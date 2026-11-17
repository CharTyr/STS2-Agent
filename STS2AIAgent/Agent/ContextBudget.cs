using System.Text;
using System.Text.Json;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>Numeric-only, bounded diagnostics for one primary tool-loop request or refusal.</summary>
internal sealed record ContextRequestStats(
    int Round, bool Sent, bool Trimmed, long Window, long OutputReserve, long Margin,
    long State, long Static, long Guidance, long History, long Tools, long Images, long Other,
    int? ActualInput = null, int? ActualOutput = null, int? ActualCache = null,
    int RemovedMessages = 0, long RemovedEstimate = 0)
{
    public long Input => State + Static + Guidance + History + Tools + Images + Other;
}

/// <summary>
/// Local conservative estimates, not a tokenizer: providers tokenize text and images differently.
/// Cached input still occupies the context. Only explicitly tagged history can be removed.
/// </summary>
internal static class ContextBudget
{
    private const int DefaultOutputReserve = 8192;

    public static ContextRequestStats Prepare(List<LlmMessage> messages, IReadOnlyList<LlmTool>? tools,
        int window, int? maxOutput, int round, bool previouslyTrimmed = false, string? thinkingSuffix = null)
    {
        var reserve = maxOutput is > 0 ? maxOutput.Value : DefaultOutputReserve;
        var margin = Math.Max(128L, window / 10L);
        var trimmed = previouslyTrimmed;
        ContextRequestStats Estimate()
        {
            long state = 0, stat = 0, guidance = 0, history = 0, tool = 0, images = 0, other = 16 + Text(thinkingSuffix);
            foreach (var message in messages)
            {
                var size = 12L + Text(message.Role) + Text(message.Content) + Text(message.ToolCallId) + Text(message.Reasoning);
                if (message.ToolCalls != null)
                    foreach (var call in message.ToolCalls)
                        size += 16 + Text(call.Id) + Text(call.Name) + Text(call.ArgumentsJson);
                if (message.ImageJpeg is { Length: > 0 } jpeg)
                    images += 4096L + (jpeg.LongLength + 63) / 64;
                var part = message.Role == "tool" || message.ToolCalls is { Count: > 0 }
                    ? ContextPart.Tools : message.ContextPart;
                switch (part)
                {
                    case ContextPart.State: state += size; break;
                    case ContextPart.Static: stat += size; break;
                    case ContextPart.Guidance: guidance += size; break;
                    case ContextPart.History: history += size; break;
                    case ContextPart.Tools: tool += size; break;
                    default: other += size; break;
                }
            }
            if (tools != null)
                foreach (var schema in tools)
                    tool += 24 + Text(schema.Name) + Text(schema.Description) + Text(JsonSerializer.Serialize(schema.Parameters));
            return new ContextRequestStats(round, false, trimmed, window, reserve, margin,
                state, stat, guidance, history, tool, images, other);
        }

        var stats = Estimate();
        var removedMessages = 0;
        long removedEstimate = 0;
        while (stats.Input + reserve + margin > window)
        {
            // Never remove a tool call, tool reply, reasoning echo, current facts or player input.
            var index = messages.FindIndex(m => m.ContextPart == ContextPart.History
                && m.Role != "tool" && m.ToolCalls is not { Count: > 0 });
            if (index < 0) break;
            var before = stats.Input;
            messages.RemoveAt(index);
            trimmed = true;
            stats = Estimate();
            removedMessages++;
            removedEstimate += before - stats.Input;
        }
        return stats with { RemovedMessages = removedMessages, RemovedEstimate = removedEstimate };
    }

    public static bool Fits(ContextRequestStats stats) =>
        stats.Input + stats.OutputReserve + stats.Margin <= stats.Window;

    // UTF-8 bytes / 2 deliberately errs high for ordinary English and CJK; this is still an estimate.
    private static long Text(string? value) => string.IsNullOrEmpty(value) ? 0 : (Encoding.UTF8.GetByteCount(value) + 1L) / 2;
}

internal sealed class ContextDispatchException(string message, string kind) : Exception(message)
{
    public string Kind { get; } = kind;
}
