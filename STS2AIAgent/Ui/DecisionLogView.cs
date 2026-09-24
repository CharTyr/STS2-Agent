using System.Collections.Generic;
using STS2AIAgent.Agent;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Ui;

/// <summary>
/// Renders the recorded decisions as the compact lines the overlay's decision log shows.
/// </summary>
/// <remarks>
/// The store is <see cref="DecisionLog"/> and its owner is <c>AgentRuntime</c>; this type only turns
/// the entries it is handed into text, newest first. It reads that snapshot rather than the JSONL
/// file, so the view cannot disagree with the in-memory log the rest of the mod reads, and it stays
/// free of Godot so the executable test project can pin the line shape without a running game.
/// </remarks>
internal static class DecisionLogView
{
    /// <summary>How many of the newest decisions the log page asks for.</summary>
    public const int RecentLimit = 50;

    /// <summary>
    /// One line per decision, newest first, capped at <see cref="RecentLimit"/>. Empty when nothing
    /// has been recorded, which is how the caller tells "no decisions yet" from "no log at all".
    /// </summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<DecisionLogEntry> entries)
    {
        var count = Math.Min(entries.Count, RecentLimit);
        var lines = new List<string>(count);
        for (var offset = 0; offset < count; offset++)
        {
            lines.Add(FormatLine(entries[entries.Count - 1 - offset]));
        }

        return lines;
    }

    /// <summary>
    /// One decision: the action, its reason when the model gave one, the source that submitted it,
    /// and what that step cost. A step whose service returned no usage reads as unknown rather than
    /// as 0, because those are different facts about the same session.
    /// </summary>
    public static string FormatLine(DecisionLogEntry entry)
    {
        var parts = new List<string>(5)
        {
            $"{entry.id}. {entry.action}"
        };

        if (entry.jev_attempt == true)
        {
            // The LLM finished a turn Jev could not commit: the empty Jev receipt already spent its
            // request, and this row is where that attempt becomes visible to the player.
            parts.Add(Loc.T("Jev 未提交 → LLM 接手"));
        }

        if (!string.IsNullOrWhiteSpace(entry.reason))
        {
            parts.Add(Loc.T("理由：{0}", entry.reason));
        }

        parts.Add(Loc.T("来源：{0}", entry.source));
        parts.Add(entry.total_tokens is { } tokens
            ? Loc.T("本次 Token：{0}", tokens.ToString("N0"))
            : Loc.T("本次 Token：未知"));
        return string.Join(" · ", parts);
    }
}
