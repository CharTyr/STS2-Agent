using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>A read-only, bounded briefing shared by the HTTP route and native MCP tool.</summary>
/// <remarks>
/// The current raw state alone establishes run identity. Decisions without that exact run id never
/// enter this projection, including entries arriving late from a previous run. Only selected safe
/// decision fields are copied; reasons, prompts, response bodies, settings and credentials are not.
/// </remarks>
internal static class PlannerBriefingProjection
{
    public const int RecentDecisionLimit = 5;

    internal sealed record JevDecision(long id, string timestamp, string action, double? confidence);

    internal sealed record ConfidenceTrend(int count, double average, double latest, string direction);

    internal sealed record Briefing(
        JsonElement strategy,
        bool dual_layer,
        bool jev_configured,
        object? run_summary,
        string? screen,
        IReadOnlyList<JevDecision> recent_jev_decisions,
        ConfidenceTrend? confidence_trend);

    public static Briefing Build(
        string? rawStateJson,
        PlayStrategy strategy,
        bool dualLayer,
        bool jevConfigured,
        DecisionLog? decisions)
    {
        using var state = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawStateJson) ? "{}" : rawStateJson);
        var root = state.RootElement;
        var screen = Text(root, "screen");
        var runId = Text(root, "run_id");
        if (string.IsNullOrWhiteSpace(runId) || runId == "run_unknown")
        {
            runId = null;
        }

        var recent = runId == null || decisions == null
            ? Array.Empty<JevDecision>()
            : decisions.Snapshot(200)
                .Where(entry => string.Equals(entry.run_id, runId, StringComparison.Ordinal)
                    && string.Equals(entry.source, "jev", StringComparison.Ordinal))
                .TakeLast(RecentDecisionLimit)
                .Select(entry => new JevDecision(entry.id, entry.timestamp, entry.action, entry.confidence))
                .ToArray();
        var scored = recent.Where(entry => entry.confidence.HasValue)
            .Select(entry => entry.confidence!.Value).ToArray();
        ConfidenceTrend? trend = null;
        if (scored.Length > 0)
        {
            var latest = scored[^1];
            trend = new ConfidenceTrend(
                scored.Length,
                scored.Average(),
                latest,
                scored.Length == 1 ? "insufficient_data" : latest > scored[0] ? "rising"
                    : latest < scored[0] ? "falling" : "steady");
        }

        // Use PlayStrategy's wire representation, not the PascalCase record member names.
        using var strategyDocument = JsonDocument.Parse(strategy.ToJson());
        return new Briefing(
            strategyDocument.RootElement.Clone(),
            dualLayer,
            jevConfigured,
            runId == null ? null : StateViews.BuildRunSummary(root),
            screen,
            recent,
            trend);
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
