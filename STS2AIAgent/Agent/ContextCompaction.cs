using System.Text;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>A fixed-size recent decision memory; the original decision log stays unchanged.</summary>
internal static class ContextCompaction
{
    public const int DefaultContextWindow = 256_000;
    public const int RecentDecisionsKept = 6;
    public const int MaxMemoryCharacters = 1800;

    public static int ResolveWindow(LlmModelConfig? model) =>
        model?.ContextWindow is > 0 ? model.ContextWindow.Value : DefaultContextWindow;

    public static string? Format(IReadOnlyList<DecisionLogEntry>? decisions)
    {
        if (decisions == null || decisions.Count == 0) return null;
        var builder = new StringBuilder("Recent decision memory (historical only, never reuse an index; current state is authoritative):\n");
        foreach (var entry in decisions.TakeLast(RecentDecisionsKept))
        {
            var action = Trim(entry.action, 80);
            var reason = Trim(entry.reason ?? string.Empty, 160);
            var line = "- " + entry.id + ". " + action + (reason.Length > 0 ? ": " + reason : "") + "\n";
            if (builder.Length + line.Length > MaxMemoryCharacters) break;
            builder.Append(line);
        }
        return builder.ToString().Trim();
    }

    private static string Trim(string value, int limit)
    {
        var text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= limit ? text : text[..limit] + "…";
    }
}
