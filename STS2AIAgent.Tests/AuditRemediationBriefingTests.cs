using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class AuditRemediationBriefingTests
{
    public static void Build_FiltersOldRunsAndRedactsDecisionDetails()
    {
        var decisions = new DecisionLog();
        decisions.Record("jev", "play_card", "secret reasoning sk-private", runId: "RUN_A", confidence: .9);
        decisions.Record("jev", "play_card", "prev run", runId: "RUN_B", confidence: .2);
        decisions.Record("agent_loop", "end_turn", "ordinary LLM", runId: "RUN_A", confidence: .5);
        decisions.Record("jev", "end_turn", "no secret in briefing", runId: "RUN_A", confidence: .7);

        var payload = PlannerBriefingProjection.Build(
            """{"run_id":"RUN_A","screen":"COMBAT","run":{"character_id":"IRONCLAD","floor":5,"deck":[],"relics":[]}}""",
            PlayStrategy.Default, true, true, decisions);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = json.RootElement;
        Assert.Equal("balanced", root.GetProperty("strategy").GetProperty("posture").GetString());
        Assert.True(root.GetProperty("dual_layer").GetBoolean());
        Assert.True(root.GetProperty("jev_configured").GetBoolean());
        Assert.Equal("COMBAT", root.GetProperty("screen").GetString());
        Assert.Equal(5, root.GetProperty("run_summary").GetProperty("floor").GetInt32());
        var recent = root.GetProperty("recent_jev_decisions");
        Assert.Equal(2, recent.GetArrayLength());
        Assert.Equal("end_turn", recent[1].GetProperty("action").GetString());
        var trend = root.GetProperty("confidence_trend");
        Assert.Equal(2, trend.GetProperty("count").GetInt32());
        Assert.Equal("falling", trend.GetProperty("direction").GetString());
        var serialized = json.RootElement.GetRawText();
        Assert.False(serialized.Contains("secret", StringComparison.Ordinal));
        Assert.False(serialized.Contains("prev run", StringComparison.Ordinal));
        Assert.False(serialized.Contains("RUN_B", StringComparison.Ordinal));
    }

    public static void Build_UnknownRunNeverFallsBackToOldDecisions()
    {
        var decisions = new DecisionLog();
        decisions.Record("jev", "play_card", runId: "RUN_A", confidence: .9);
        var payload = PlannerBriefingProjection.Build(
            """{"run_id":"run_unknown","screen":"MAIN_MENU","run":{"floor":99}}""",
            PlayStrategy.Default, false, false, decisions);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("run_summary").ValueKind);
        Assert.Equal("MAIN_MENU", root.GetProperty("screen").GetString());
        Assert.Equal(0, root.GetProperty("recent_jev_decisions").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("confidence_trend").ValueKind);
    }

    public static void Build_BoundsDecisionsAndKeepsTheirOrder()
    {
        var decisions = new DecisionLog();
        for (var i = 0; i < 8; i++)
        {
            decisions.Record("jev", "choice_" + i, runId: "RUN_A", confidence: i / 10.0);
        }

        var payload = PlannerBriefingProjection.Build(
            """{"run_id":"RUN_A","screen":"MAP","run":{"floor":3}}""",
            PlayStrategy.Default, true, false, decisions);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var recent = json.RootElement.GetProperty("recent_jev_decisions");
        Assert.Equal(PlannerBriefingProjection.RecentDecisionLimit, recent.GetArrayLength());
        Assert.Equal("choice_3", recent[0].GetProperty("action").GetString());
        Assert.Equal("choice_7", recent[4].GetProperty("action").GetString());
        Assert.Equal("rising", json.RootElement.GetProperty("confidence_trend")
            .GetProperty("direction").GetString());
    }

    public static void Build_NoScoreDoesNotInventTrend()
    {
        var decisions = new DecisionLog();
        decisions.Record("jev", "end_turn", runId: "RUN_A");
        var payload = PlannerBriefingProjection.Build(
            """{"run_id":"RUN_A","screen":"MAP","run":{"floor":3}}""",
            PlayStrategy.Default, true, false, decisions);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("confidence_trend").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            json.RootElement.GetProperty("recent_jev_decisions")[0].GetProperty("confidence").ValueKind);
    }
}
