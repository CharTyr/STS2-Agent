using System.Text.Json;
using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// A state build that freezes the game is named in the log and counted on <c>GET /health</c>.
/// </summary>
internal static class StateBuildTimingTests
{
    private static readonly DateTime Start = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    private static JsonElement Snapshot(StateBuildTiming timing) =>
        JsonSerializer.SerializeToElement(timing.Snapshot());

    public static void FastBuildsAreCountedWithoutAWarning()
    {
        var timing = new StateBuildTiming();
        Assert.True(timing.Record(12, "COMBAT", Start) == null, "A 12 ms build must not warn.");
        Assert.True(
            timing.Record(StateBuildTiming.SlowThresholdMs, "COMBAT", Start) == null,
            "A build exactly at the threshold is not over it.");

        var snapshot = Snapshot(timing);
        Assert.Equal(2L, snapshot.GetProperty("samples").GetInt64());
        Assert.Equal(0L, snapshot.GetProperty("slow_builds").GetInt64());
        Assert.Equal(100.0, snapshot.GetProperty("last_ms").GetDouble());
    }

    public static void ASlowBuildWarnsOnceAndThenSaysHowManyItHeldBack()
    {
        var timing = new StateBuildTiming();
        var first = timing.Record(640, "SHOP", Start);
        Assert.True(first != null && first.Contains("640 ms", StringComparison.Ordinal) && first.Contains("SHOP", StringComparison.Ordinal),
            "The first slow build must warn and name its duration and screen; got: " + first);

        Assert.True(timing.Record(700, "SHOP", Start.AddSeconds(5)) == null, "A second slow build 5 s later is held back.");
        Assert.True(timing.Record(710, "SHOP", Start.AddSeconds(20)) == null, "So is a third, 20 s later.");

        var next = timing.Record(720, "SHOP", Start + StateBuildTiming.WarningInterval);
        Assert.True(next != null && next.Contains("2 more slow build(s)", StringComparison.Ordinal),
            "After the interval the next warning must say how many it held back; got: " + next);

        var snapshot = Snapshot(timing);
        Assert.Equal(4L, snapshot.GetProperty("slow_builds").GetInt64());
        Assert.Equal(720.0, snapshot.GetProperty("max_ms").GetDouble());
        Assert.Equal("SHOP", snapshot.GetProperty("max_screen").GetString());
    }

    public static void TheSummaryReportsRecentPercentilesOverABoundedWindow()
    {
        var empty = Snapshot(new StateBuildTiming());
        Assert.Equal(0L, empty.GetProperty("samples").GetInt64());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("recent_p95_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("max_ms").ValueKind);

        var timing = new StateBuildTiming();
        for (var ms = 1; ms <= 100; ms++)
        {
            timing.Record(ms, "MAP", Start);
        }

        var snapshot = Snapshot(timing);
        Assert.Equal(50.0, snapshot.GetProperty("recent_p50_ms").GetDouble());
        Assert.Equal(95.0, snapshot.GetProperty("recent_p95_ms").GetDouble());

        // The window forgets: after 300 more fast builds, an early outlier no longer sets the p95.
        var windowed = new StateBuildTiming();
        windowed.Record(5000, "COMBAT", Start);
        for (var i = 0; i < 300; i++)
        {
            windowed.Record(10, "COMBAT", Start);
        }

        var later = Snapshot(windowed);
        Assert.Equal((long)StateBuildTiming.RecentWindow, later.GetProperty("recent_samples").GetInt64());
        Assert.Equal(10.0, later.GetProperty("recent_p95_ms").GetDouble());
        Assert.Equal(5000.0, later.GetProperty("max_ms").GetDouble());
    }

    public static void EveryStateBuildIsTimedAndReported()
    {
        var build = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadStateService(), "BuildStatePayload"));
        // Inside the build, not around the /state route: action responses and SSE refreshes build the
        // same payload on the same game thread, and a route timer would see none of them.
        Assert.True(
            build.Contains("StateBuildTiming.Instance.Record(buildTimer.Elapsed.TotalMilliseconds,screen,", StringComparison.Ordinal),
            "BuildStatePayload must record its own duration with StateBuildTiming.");
        Assert.True(
            build.Contains("Log.Warn($\"[STS2AIAgent]{slowBuild}\");", StringComparison.Ordinal),
            "A slow build's warning must reach the game log, which is what a player attaches.");

        var router = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs"));
        Assert.True(
            router.Contains("state_build=StateBuildTiming.Instance.Snapshot()", StringComparison.Ordinal),
            "GET /health must report the state_build summary.");
    }
}
