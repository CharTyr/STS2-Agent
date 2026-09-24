using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

/// <summary>
/// The per-turn Jev evidence the 2026-09-23 run could not answer ("was the hand playable when Jev
/// passed"): what the frame offered, what Jev chose, with what confidence, and whether the LLM
/// finished the turn after Jev could not commit. These tests pin the trace through the decider, the
/// turn result, and the decision log.
/// </summary>
internal static class JevTurnTraceTests
{
    /// <summary>The decider hands the orchestrator every option id it weighed, not only the choice.</summary>
    public static async Task DeciderCarriesTheOfferedOptionIds()
    {
        var client = new StubJev();
        var decision = await new JevExecutionDecider(client, "jev").DecideAsync(CombatFrame, PlayStrategy.Default, CancellationToken.None);

        Assert.NotNull(decision.OfferedOptionIds);
        Assert.True(decision.OfferedOptionIds!.Count > 0);
        Assert.True(decision.OfferedOptionIds.Contains("end_turn"));
        Assert.True(decision.OfferedOptionIds.Any(id => id.StartsWith("play_card:", StringComparison.Ordinal)),
            "the offered ids must include the concrete card plays the frame offered");
    }

    /// <summary>The trace reaches the decision log with the accepted Jev action.</summary>
    public static void DecisionLogCarriesTheTrace()
    {
        var log = new DecisionLog();
        var entry = log.Record(
            "jev",
            "end_turn",
            reason: "no playable cards",
            stateFingerprint: "FP",
            requestsSpent: 1,
            totalTokens: 4200,
            runId: "RUN1",
            confidence: 0.99,
            optionIds: new[] { "play_card:0->0", "play_card:1->0", "end_turn" },
            probabilities: new Dictionary<string, double> { ["end_turn"] = 0.99, ["play_card:0->0"] = 0.01 },
            danger: 0.4,
            strategyUpdatedAt: "2026-09-24T10:10:53Z");

        Assert.Equal(3, entry.option_ids!.Length);
        Assert.Equal("play_card:0->0", entry.option_ids[0]);
        Assert.Equal("end_turn", entry.option_ids[^1]);
        Assert.Equal(0.99, entry.probabilities!["end_turn"]);
        Assert.Equal(0.4, entry.danger);
        Assert.Equal("2026-09-24T10:10:53Z", entry.strategy_updated_at);
        Assert.Null(entry.jev_attempt);

        var json = log.RenderJson();
        Assert.Contains("option_ids", json);
        Assert.Contains("strategy_updated_at", json);
    }

    /// <summary>A fallback row (LLM acted after Jev could not commit) is marked as a Jev attempt.</summary>
    public static void FallbackRowsAreMarkedAsJevAttempts()
    {
        var log = new DecisionLog();
        var entry = log.Record(
            "agent_loop",
            "play_card",
            reason: "fallback took over",
            requestsSpent: 4,
            totalTokens: 30000,
            runId: "RUN1",
            jevAttempt: true);

        Assert.Equal(true, entry.jev_attempt);
        Assert.Contains("jev_attempt", log.RenderJson());
    }

    /// <summary>Existing rows without a trace keep their shape: the new fields are absent, not null spam.</summary>
    public static void RowsWithoutATraceStayLean()
    {
        var log = new DecisionLog();
        log.Record("agent_loop", "choose_map_node", requestsSpent: 1, runId: "RUN1");
        var json = log.RenderJson();
        Assert.False(json.Contains("option_ids", StringComparison.Ordinal));
        Assert.False(json.Contains("jev_attempt", StringComparison.Ordinal));
    }

    private const string CombatFrame = """
    {
      "state": {
        "screen": "COMBAT",
        "turn": 1,
        "combat": {
          "player": {"hp": "25/87", "energy": 1},
          "hand": [
            {"i": 0, "line": "Strike [1费]", "playable": true, "targets": [0, 1]},
            {"i": 1, "line": "Defend [1费]", "playable": true, "targets": []}
          ],
          "enemies": [{"i": 0, "name": "Slime"}, {"i": 1, "name": "Jaw Worm"}]
        }
      },
      "available_actions": [
        {"name": "play_card", "requires_index": true, "requires_target": true},
        {"name": "end_turn", "requires_index": false, "requires_target": false}
      ]
    }
    """;

    private sealed class StubJev : IJevClient
    {
        public Task<JevResponse> SystemOneAsync(JevRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new JevResponse
            {
                Answers = new Dictionary<string, JevAnswer>
                {
                    ["action"] = new JevAnswer { Choice = "end_turn", Confidence = 0.99 },
                    ["danger"] = new JevAnswer { Score = 0.4 }
                }
            });
        }

        public Task<string> PingAsync(CancellationToken cancellationToken) => Task.FromResult("ok");
    }
}
