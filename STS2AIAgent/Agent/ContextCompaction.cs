using System.Text;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>
/// Decides when a model's own context window is full enough that older decisions should be replaced
/// by one continuation summary.
/// </summary>
/// <remarks>
/// This follows the DSH compaction boundary, not its session format: the original decision log stays
/// intact, and only the prompt sent on the next turn carries the summary. A spend cap is a different
/// question and stays in <see cref="SessionBudgetGuard"/>. Filling the window must not stop the run.
/// </remarks>
internal static class ContextCompaction
{
    public const int DefaultContextWindow = 256_000;

    public const double TriggerRatio = 0.80;

    public const int RecentDecisionsKept = 6;

    public const int MinimumSummaryDecisions = 4;

    public static int ResolveWindow(LlmModelConfig? model)
    {
        return model?.ContextWindow is > 0 ? model.ContextWindow.Value : DefaultContextWindow;
    }

    public static bool ShouldCompact(int promptTokens, int contextWindow)
    {
        if (promptTokens <= 0 || contextWindow <= 0)
        {
            return false;
        }

        return promptTokens >= (long)Math.Floor(contextWindow * TriggerRatio);
    }

    public static string? Format(LlmModelConfig? model, IReadOnlyList<DecisionLogEntry>? decisions, int? promptTokens = null)
    {
        var window = ResolveWindow(model);
        // No measured prompt means the window is not known to be under pressure. Do not summarize
        // merely because the decision log is long.
        if (promptTokens is not > 0 || !ShouldCompact(promptTokens.Value, window))
        {
            return null;
        }

        if (decisions == null || decisions.Count < RecentDecisionsKept + MinimumSummaryDecisions)
        {
            return null;
        }

        var older = decisions.Take(decisions.Count - RecentDecisionsKept).ToArray();
        var recent = decisions.Skip(older.Length).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("Context compaction: older accepted decisions were summarized because the play prompt reached 80% of this model's context window.");
        builder.AppendLine("The original decision log is unchanged. Treat this summary as historical memory, never as the current legal state, and never reuse an index from it.");
        builder.AppendLine("Older decisions:");
        foreach (var entry in older.TakeLast(24))
        {
            builder.Append("- ").Append(entry.id).Append(". ").Append(entry.action);
            if (!string.IsNullOrWhiteSpace(entry.reason))
            {
                builder.Append(": ").Append(Trim(entry.reason, 160));
            }

            builder.AppendLine();
        }

        builder.AppendLine("Recent decisions, kept verbatim:");
        foreach (var entry in recent)
        {
            builder.Append("- ").Append(entry.id).Append(". ").Append(entry.action);
            if (!string.IsNullOrWhiteSpace(entry.reason))
            {
                builder.Append(": ").Append(Trim(entry.reason, 240));
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string Trim(string value, int limit)
    {
        var text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= limit ? text : text[..limit] + "...";
    }
}
