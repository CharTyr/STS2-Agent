using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class DevAuditRegressionTests
{
    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static JsonElement Diff(string before, string after, int limit = 200) =>
        Json(JsonSerializer.Serialize(StateViews.BuildStateDiff(Json(before), Json(after), limit)));

    public static void DiffExactCapIsComplete()
    {
        var diff = Diff("{\"a\":0,\"z\":3}", "{\"a\":1,\"z\":3}", 1);
        Assert.Equal(1, diff.GetProperty("change_count").GetInt32());
        Assert.False(diff.GetProperty("truncated").GetBoolean());
    }

    public static void DiffDeepComparisonReportsItsLimit()
    {
        var before = "0";
        var after = "1";
        for (var i = 0; i < 15; i++)
        {
            before = "{\"child\":" + before + "}";
            after = "{\"child\":" + after + "}";
        }
        var diff = Diff(before, after);
        Assert.True(diff.GetProperty("truncated").GetBoolean(), "Unvisited deep changes must not read as a complete equal comparison.");
    }

    public static void DiffKeepsIntegerPrecision()
    {
        var diff = Diff("{\"id\":9007199254740992}", "{\"id\":9007199254740993}");
        Assert.Equal(9007199254740993L, diff.GetProperty("changes")[0].GetProperty("after").GetInt64());
    }

    public static void DiffEquivalentNumbersAreEqual()
    {
        Assert.Equal(0, Diff("{\"n\":1}", "{\"n\":1.0}").GetProperty("change_count").GetInt32());
        Assert.Equal(0, Diff("{\"n\":0}", "{\"n\":-0e100}").GetProperty("change_count").GetInt32());
        Assert.Equal(1, Diff("{\"n\":0}", "{\"n\":1e-100}").GetProperty("change_count").GetInt32());
    }

    public static void DiffLiteralKeysDoNotCollide()
    {
        var diff = Diff("{\"a.b\":1,\"a\":{\"b\":2}}", "{\"a.b\":3,\"a\":{\"b\":2}}");
        Assert.Equal(1, diff.GetProperty("change_count").GetInt32());
        Assert.Equal("[\"a.b\"]", diff.GetProperty("changes")[0].GetProperty("path").GetString());
    }

    public static void DiffUnicodePathsMatchPython()
    {
        foreach (var (name, expected) in new[]
        {
            ("\u8336", "[\"\\u8336\"]"),
            ("\ud83d\ude42", "[\"\\ud83d\\ude42\"]"),
            ("\u2028", "[\"\\u2028\"]"),
            ("\\uABCD", "[\"\\\\uABCD\"]"),
            ("a\nb", "[\"a\\nb\"]"),
        })
        {
            var before = JsonSerializer.Serialize(new Dictionary<string, int> { [name] = 0 });
            var after = JsonSerializer.Serialize(new Dictionary<string, int> { [name] = 1 });
            Assert.Equal(expected, Diff(before, after).GetProperty("changes")[0].GetProperty("path").GetString());
        }
    }

    public static void DiffEmptyObjectIsNotString()
    {
        Assert.Equal(1, Diff("{\"a\":{}}", "{\"a\":\"{}\"}").GetProperty("change_count").GetInt32());
    }

    public static void EventOverflowDropsSnapshot()
    {
        var subscribers = new EventStreamSubscribers<string, string>(1);
        var lease = subscribers.Subscribe(null, value => value);
        subscribers.PublishSnapshot("old", "ready");
        Assert.Equal(1, subscribers.Publish("changed"));
        Assert.Equal(0, subscribers.Count);
        Assert.Null(subscribers.Snapshot);
        Assert.False(subscribers.Unsubscribe(lease.Id));
        var next = subscribers.Subscribe(subscribers.Snapshot, value => value);
        Assert.False(next.Reader.TryRead(out _));
    }

    public static void EventSnapshotOverflowDropsSnapshot()
    {
        var subscribers = new EventStreamSubscribers<string, string>(1);
        subscribers.Subscribe(null, value => value);
        subscribers.Publish("started");
        subscribers.PublishSnapshot("old", "ready");
        Assert.Equal(0, subscribers.Count);
        Assert.Null(subscribers.Snapshot);
    }

    public static void EventInFlightSampleCannotReviveIdleState()
    {
        var subscribers = new EventStreamSubscribers<string, string>(1);
        var lease = subscribers.Subscribe(null, value => value);
        subscribers.PublishSnapshot("old", "ready");
        subscribers.Unsubscribe(lease.Id);
        subscribers.RecordSnapshot("late");
        Assert.Null(subscribers.Snapshot);
        subscribers.PublishSnapshot("later", "ready");
        Assert.Null(subscribers.Snapshot);
    }

    public static void SaveRetryClearsFailureNotice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sts2-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new SettingsStore(path);
            var settings = AgentSettings.CreateDefault();
            store.Save(settings);
            Directory.CreateDirectory(path + ".tmp");
            try { store.Save(settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Assert.Equal("save_failed", store.LastNotice.Kind);
            Directory.Delete(path + ".tmp");
            store.Save(settings);
            Assert.Equal("ok", store.LastNotice.Kind);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static void ThemeSaveDoesNotHarvestOtherEdits()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.Settings.cs");
        var persist = AgentSourceFixture.MethodBody(source, "PersistThemeChoice");
        Assert.False(persist.Contains("HarvestSettings()", StringComparison.Ordinal));
        Assert.Contains("catch", persist);
        var apply = AgentSourceFixture.MethodBody(source, "ApplyThemeChoice");
        Assert.False(apply.Contains("RebuildSettingsForm", StringComparison.Ordinal));
        Assert.Contains("if (!PersistThemeChoice(themeId)) return;", apply);
        foreach (var method in new[] { "BuildEndpointCard", "BuildModelCard" })
            Assert.Contains("UiFactory.TagSurface", AgentSourceFixture.MethodBody(source, method));
    }
}
