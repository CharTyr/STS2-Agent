using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The silent-pending class of failure this file exists to kill: a precondition the player controls
/// (solo mode, autoplay running) must come back as a named reason, and only a real concurrent attempt
/// may still look busy.
/// </summary>
internal static class DualLaunchBlockReasonPolicyTests
{
    public static void SoloModeIsANamedReasonNotBusy()
    {
        Assert.Equal(DualLaunchBlockReason.SoloMode,
            DualLaunchBlockReasonRules.Evaluate(
                isCompanion: false,
                overlayPlayMode: "solo",
                playRunning: false,
                teammateStartInFlight: false,
                teammateLaunchInFlight: false,
                modeSwitchInFlight: false));
        Assert.False(DualLaunchBlockReason.SoloMode.IsBusy(),
            "Solo mode is a player-fixable precondition; telling the caller 'pending' is what hid it.");
    }

    public static void AutoplayRunningIsANamedReasonNotBusy()
    {
        Assert.Equal(DualLaunchBlockReason.AutoplayRunning,
            DualLaunchBlockReasonRules.Evaluate(
                isCompanion: false,
                overlayPlayMode: "coop",
                playRunning: true,
                teammateStartInFlight: false,
                teammateLaunchInFlight: false,
                modeSwitchInFlight: false));
    }

    /// <summary>Solo mode wins even over autoplay: it is the error the player can act on first.</summary>
    public static void SoloModeBeatsAutoplayRunning()
    {
        Assert.Equal(DualLaunchBlockReason.SoloMode,
            DualLaunchBlockReasonRules.Evaluate(
                isCompanion: false,
                overlayPlayMode: "solo",
                playRunning: true,
                teammateStartInFlight: false,
                teammateLaunchInFlight: false,
                modeSwitchInFlight: false));
    }

    public static void InFlightWorkStaysBusy()
    {
        foreach (var (start, launch, modeSwitch) in new[]
                 { (true, false, false), (false, true, false), (false, false, true), (true, true, true) })
        {
            Assert.Equal(DualLaunchBlockReason.Busy,
                DualLaunchBlockReasonRules.Evaluate(
                    isCompanion: false,
                    overlayPlayMode: "coop",
                    playRunning: false,
                    teammateStartInFlight: start,
                    teammateLaunchInFlight: launch,
                    modeSwitchInFlight: modeSwitch));
        }

        Assert.True(DualLaunchBlockReason.Busy.IsBusy());
    }

    public static void CompanionRolePassesTheModeGateAsBefore()
    {
        Assert.Equal(DualLaunchBlockReason.None,
            DualLaunchBlockReasonRules.Evaluate(
                isCompanion: true,
                overlayPlayMode: "solo",
                playRunning: false,
                teammateStartInFlight: false,
                teammateLaunchInFlight: false,
                modeSwitchInFlight: false));
        Assert.False(DualLaunchBlockReason.None.IsBusy());
    }

    /// <summary>The runtime turns each named reason into a Rejected outcome plus a readable message.</summary>
    public static void RuntimeRecordsAReasonForEveryNamedBlock()
    {
        var combined = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs")
            + AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.ModeControl.cs");
        Assert.Contains("当前是单人模式", combined);
        Assert.Contains("自动游玩进行中", combined);
        Assert.Contains("DualLaunchBlockReasonRules.Evaluate", combined);
        Assert.Contains("DualLaunchOutcome.Rejected", combined);
        Assert.Contains("Task.CompletedTask", combined);
    }
}
