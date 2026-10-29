using STS2AIAgent.Agent;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// The four small decision tables of the 2026-10 overhaul: game speed (W1), screenshots on demand
/// (W6), stall hints (W8.3) and the SSE poll cadence (W9).
/// </summary>
internal static class OverhaulSmallPolicyTests
{
    public static void TurboFollowsTheSettingAndNeverSlowsThePlayer()
    {
        Assert.Equal(new TurboDecision(null, "off"), TurboPolicy.Decide("off", false, TurboLevel.Normal));
        Assert.Equal(new TurboDecision(TurboLevel.Instant, "engage"), TurboPolicy.Decide("instant", false, TurboLevel.Normal));
        Assert.Equal(new TurboDecision(TurboLevel.Fast, "engage"), TurboPolicy.Decide("fast", false, TurboLevel.Normal));
        Assert.Equal(new TurboDecision(TurboLevel.Instant, "engage"), TurboPolicy.Decide("instant", false, TurboLevel.None),
            "None counts as the slowest level.");
        Assert.Equal(new TurboDecision(null, "already_fast"), TurboPolicy.Decide("fast", false, TurboLevel.Instant),
            "A player already on Instant keeps it even when the setting asks for Fast.");
        Assert.Equal(new TurboDecision(null, "already_fast"), TurboPolicy.Decide("instant", false, TurboLevel.Instant));
        Assert.Equal(new TurboDecision(TurboLevel.Instant, "engage"), TurboPolicy.Decide("junk", false, TurboLevel.Normal),
            "An unknown setting means the default, instant.");
    }

    public static void TurboCapsMultiplayerAtFast()
    {
        Assert.Equal(new TurboDecision(TurboLevel.Fast, "multiplayer_cap"), TurboPolicy.Decide("instant", true, TurboLevel.Normal));
        Assert.Equal(new TurboDecision(TurboLevel.Fast, "engage"), TurboPolicy.Decide("fast", true, TurboLevel.Normal));
        Assert.Equal(new TurboDecision(null, "already_fast"), TurboPolicy.Decide("instant", true, TurboLevel.Fast));
    }

    public static void TurboRestoresOnlyItsOwnValueAndRoundTrips()
    {
        Assert.True(TurboPolicy.ShouldRestore(TurboLevel.Instant, TurboLevel.Instant));
        Assert.False(TurboPolicy.ShouldRestore(TurboLevel.Normal, TurboLevel.Instant), "The player changed it back: keep theirs.");
        foreach (var level in new[] { TurboLevel.None, TurboLevel.Normal, TurboLevel.Fast, TurboLevel.Instant })
        {
            Assert.Equal(level, TurboPolicy.Parse(TurboPolicy.Format(level)));
        }

        Assert.Null(TurboPolicy.Parse("Warp"));
        Assert.Null(TurboPolicy.Parse(null));
        Assert.Equal("off", TurboPolicy.NormalizeMode(" OFF "));
        Assert.Equal("instant", TurboPolicy.NormalizeMode(null));
    }

    public static void VisionAttachesOnlyWhenTheStateMayNotBeEnough()
    {
        Assert.False(VisionPolicy.ShouldAttach("off", "UNKNOWN", true, true, 5));
        Assert.True(VisionPolicy.ShouldAttach("every_step", "COMBAT", false, false, 0));
        Assert.False(VisionPolicy.ShouldAttach("on_demand", "COMBAT", false, false, 1), "A normal step needs no picture.");
        Assert.Equal("unknown_screen", VisionPolicy.Reason("on_demand", "UNKNOWN", false, false, 0));
        Assert.Equal("unknown_screen", VisionPolicy.Reason(null, "", false, false, 0));
        Assert.Equal("last_turn_failed", VisionPolicy.Reason("on_demand", "MAP", true, true, 3));
        Assert.Equal("no_action", VisionPolicy.Reason("on_demand", "MAP", false, true, 0));
        Assert.Equal("unsettled", VisionPolicy.Reason("on_demand", "MAP", false, false, 2));
        Assert.Equal("on_demand", VisionPolicy.NormalizeMode("whatever"));
        Assert.Equal("every_step", VisionPolicy.NormalizeMode("Every-Step"));
    }

    public static void StallHintsWaitForTheirThresholds()
    {
        var justUnder = TimeSpan.FromMilliseconds(-1);
        Assert.Equal(StallHint.None, StallHintPolicy.Decide(StallPhase.RequestingModel, TimeSpan.FromMinutes(5), "COMBAT", autoPlayRunning: false));
        Assert.Equal(StallHint.None, StallHintPolicy.Decide(StallPhase.RequestingModel, StallHintPolicy.ModelThreshold + justUnder, "COMBAT", true));
        Assert.Equal(StallHint.SlowModel, StallHintPolicy.Decide(StallPhase.RequestingModel, StallHintPolicy.ModelThreshold, "COMBAT", true));
        Assert.Equal(StallHint.WaitingForPlayer, StallHintPolicy.Decide(StallPhase.WaitingForPlayer, StallHintPolicy.PlayerThreshold, "MAP", true));
        Assert.Equal(StallHint.GameNotResponding, StallHintPolicy.Decide(StallPhase.WaitingForGame, StallHintPolicy.GameThreshold, "COMBAT", true));
        Assert.Equal(StallHint.GameNotResponding, StallHintPolicy.Decide(StallPhase.ReadingState, StallHintPolicy.GameThreshold, "COMBAT", true));
        Assert.Equal(StallHint.MainMenuIdle, StallHintPolicy.Decide(StallPhase.Other, StallHintPolicy.MainMenuThreshold, "main_menu", true));
        Assert.Equal(StallHint.None, StallHintPolicy.Decide(StallPhase.Other, TimeSpan.FromMinutes(3), "COMBAT", true));
    }

    public static void PollCadenceBacksOffAndSnapsBack()
    {
        var cadence = new EventPollingCadence();
        Assert.Equal(120, cadence.CurrentMs);
        for (var i = 0; i < 7; i++) Assert.Equal(120, cadence.Observe(false));
        Assert.Equal(250, cadence.Observe(false), "The 8th unchanged poll steps down once.");
        for (var i = 0; i < 40; i++) cadence.Observe(false);
        Assert.Equal(1000, cadence.CurrentMs, "Capped at one second.");
        Assert.Equal(120, cadence.Observe(true), "A change snaps back at once.");
        for (var i = 0; i < 20; i++) cadence.Observe(false);
        cadence.Reset();
        Assert.Equal(120, cadence.CurrentMs);
        Assert.Equal(0, cadence.UnchangedPolls);

        var slowBase = new EventPollingCadence(200);
        Assert.Equal(200, slowBase.CurrentMs);
        var verySlow = new EventPollingCadence(2000);
        for (var i = 0; i < 40; i++) verySlow.Observe(false);
        Assert.Equal(2000, verySlow.CurrentMs, "A configured base above the steps is never made faster.");
    }
}
