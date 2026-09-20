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
}
