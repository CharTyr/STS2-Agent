using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class DecisionLogTests
{
    public static void Record_RedactsBoundsAndKeepsNewest()
    {
        var log = new DecisionLog(capacity: 2);
        log.Record("agent_loop", "play_card", "Use sk-secret123456 now\nplease", requestsSpent: -2, totalTokens: -1);
        log.Record("agent_loop", "end_turn", "done", requestsSpent: 1, totalTokens: 9);
        log.Record("mcp", "choose_map_node", "take elite", stateFingerprint: "map:3");

        var entries = log.Snapshot(10);
        Assert.Equal(2, entries.Count);
        Assert.Equal("end_turn", entries[0].action);
        Assert.Equal(1, entries[0].requests_spent);
        Assert.Equal(9, entries[0].total_tokens);
        Assert.Equal("choose_map_node", entries[1].action);
        Assert.Equal("map:3", entries[1].state_fingerprint);

        using var json = JsonDocument.Parse(log.RenderJson());
        Assert.Equal(2, json.RootElement.GetProperty("decisions").GetArrayLength());
    }

    public static void Record_PersistsJsonlAndRotates()
    {
        var root = Path.Combine(Path.GetTempPath(), "sts2-decision-log-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "decisions.jsonl");
        try
        {
            var log = new DecisionLog(path, capacity: 3, maxFileBytes: 1);
            log.Record("agent_loop", "end_turn", "first", timestamp: DateTimeOffset.Parse("2026-09-20T00:00:00Z"));
            log.Record("agent_loop", "proceed", "second", timestamp: DateTimeOffset.Parse("2026-09-20T00:00:01Z"));

            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".previous"));
            var current = File.ReadAllText(path);
            Assert.Contains("\"action\":\"proceed\"", current);
            var previous = File.ReadAllText(path + ".previous");
            Assert.Contains("\"action\":\"end_turn\"", previous);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (Exception) { }
        }
    }

    public static void Record_UnwritablePathNeverThrows()
    {
        var root = Path.Combine(Path.GetTempPath(), "sts2-decision-log-file-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(root, "not a directory");
            var log = new DecisionLog(Path.Combine(root, "decisions.jsonl"));
            var entry = log.Record("agent_loop", "end_turn", "safe");

            Assert.Equal("end_turn", entry.action);
            Assert.Single(log.Snapshot());
        }
        finally
        {
            try { File.Delete(root); } catch (Exception) { }
        }
    }

    public static void Record_NotifiesMirrorsAfterCommitting()
    {
        var log = new DecisionLog();
        var seen = new List<DecisionLogEntry>();
        log.Recorded += entry =>
        {
            // The mirror must observe a committed entry, not a promise of one.
            seen.Add(entry);
            Assert.Contains(entry.action, string.Join(",", log.Snapshot().Select(item => item.action)));
        };
        log.Recorded += _ => throw new InvalidOperationException("mirror failed");

        var recorded = log.Record("agent_loop", "play_card", "Keep the block up.");

        Assert.Equal(1, seen.Count);
        Assert.Equal(recorded.id, seen[0].id);
        // A throwing mirror is not allowed to lose the decision or the return value.
        Assert.Equal("play_card", recorded.action);
        Assert.Single(log.Snapshot());
    }

    public static void Record_AttributesDecisionsToTheirRun()
    {
        var log = new DecisionLog();
        log.Record("agent_loop", "play_card", totalTokens: 100, runId: "RUN_A");
        log.Record("agent_loop", "end_turn", totalTokens: 50, runId: "RUN_A");
        log.Record("http_api", "play_card", totalTokens: 7, runId: "RUN_B");
        // Before a run is identified there is nothing to attribute to, and the placeholder the mod
        // uses for that must not become a bucket of its own.
        log.Record("agent_loop", "open_character_select", runId: "run_unknown");
        log.Record("native_mcp", "end_turn");

        var runA = log.Spend("RUN_A");
        Assert.Equal(2, runA.Decisions);
        Assert.Equal(150L, runA.Tokens);
        Assert.True(runA.TokensKnown);

        var runB = log.Spend("RUN_B");
        Assert.Equal(1, runB.Decisions);
        Assert.Equal(7L, runB.Tokens);
        Assert.True(runB.TokensKnown);

        Assert.Equal(0, log.Spend("RUN_MISSING").Decisions);

        // A null run id asks for the whole log, which is what the overlay needs before any run has
        // been identified: every entry counts, including the two that belong to no run.
        var all = log.Spend(null);
        Assert.Equal(5, all.Decisions);
        Assert.Equal(157L, all.Tokens);

        // Snapshot is oldest-first, so the two unattributed entries are the last two.
        var entries = log.Snapshot();
        Assert.Equal("RUN_A", entries[0].run_id);
        Assert.Equal("RUN_B", entries[2].run_id);
        Assert.Null(entries[3].run_id);
        Assert.Null(entries[4].run_id);
    }

    public static void Spend_KeepsUnknownTokensUnknown()
    {
        var log = new DecisionLog();
        log.Record("agent_loop", "play_card", totalTokens: null, runId: "RUN_A");
        log.Record("agent_loop", "end_turn", totalTokens: null, runId: "RUN_A");

        var spend = log.Spend("RUN_A");

        Assert.Equal(2, spend.Decisions);
        Assert.Equal(0L, spend.Tokens);
        // Two decisions and no usage report is "unknown", never "spent nothing".
        Assert.False(spend.TokensKnown);
    }

    public static void Spend_TracksTheEntriesTheSnapshotCanShow()
    {
        var log = new DecisionLog(capacity: 2);
        log.Record("agent_loop", "play_card", totalTokens: 10, runId: "RUN_A");
        log.Record("agent_loop", "end_turn", totalTokens: 20, runId: "RUN_A");
        log.Record("agent_loop", "proceed", totalTokens: 30, runId: "RUN_A");

        // The evicted entry is gone from both views, so the total cannot disagree with the lines.
        Assert.Equal(2, log.Snapshot().Count);
        Assert.Equal(2, log.Spend("RUN_A").Decisions);
        Assert.Equal(50L, log.Spend("RUN_A").Tokens);
    }

    public static void SseDecisionEvent_IsMirroredFromTheSameLog()
    {
        var runtime = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        Assert.Contains(
            "_decisions.Recorded += GameEventService.Instance.PublishDecision;",
            runtime,
            StringComparison.Ordinal);

        var service = AgentSourceFixture.Read("STS2AIAgent/Server/GameEventService.cs");
        var publish = AgentSourceFixture.MethodBody(service, "PublishDecision");
        // The event name is a literal so the api-facts gate can see it, and it travels the shared
        // Publish path so the repeat guard and slow-subscriber rules apply to it too.
        Assert.Contains("Publish(\"decision_made\"", publish, StringComparison.Ordinal);
        Assert.False(
            publish.Contains("_subscribers.Publish", StringComparison.Ordinal),
            "PublishDecision must go through Publish, not around the repeat guard");
    }
}
