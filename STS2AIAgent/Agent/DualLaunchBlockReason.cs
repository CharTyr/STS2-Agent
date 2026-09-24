namespace STS2AIAgent.Agent;

/// <summary>
/// Why a dual-instance launch could not start, evaluated before the gate is even touched.
/// </summary>
/// <remarks>
/// The gate historically returned one bare "no": a companion attempt already in flight, an in-progress
/// mode switch, a running autoplay loop, and "the player chose solo mode" were all squeezed into it.
/// The invite route then answered <c>pending</c> -- as if another attempt were running -- and never
/// changed the outcome, so a caller waiting for the teammate window saw no process, no status, and no
/// error, ever. This enum separates the cases: <see cref="Busy"/> still deserves <c>pending</c>
/// (another attempt owns the gate), while a precondition the player controls names itself so the
/// answer can say what to change, and the outcome records <see cref="DualLaunchOutcome.Rejected"/>
/// instead of staying <c>Idle</c> forever.
///
/// Pure policy on purpose: the inputs sit behind two different runtime locks, so this file takes them
/// as plain booleans and stays compilable offline like <c>DualLaunchOutcome</c>.
/// </remarks>
internal enum DualLaunchBlockReason
{
    /// No block: the launch may claim the gate.
    None,

    /// Host-side multiplayer routes are blocked by the player's own mode switch
    /// (<c>OverlayPlayMode</c> is not `coop`). Tell them where the switch is.
    SoloMode,

    /// Autoplay is currently running on the local player; inviting would race its decisions.
    AutoplayRunning,

    /// A concurrent attempt owns the route (teammate start/launch in flight, or a mode switch in
    /// progress). The correct answer is <c>pending</c>, not an error.
    Busy
}

internal static class DualLaunchBlockReasonRules
{
    /// <summary>
    /// Classifies why a launch may not claim its gate, in the same priority the runtime's old inline
    /// checks had: concurrent tries first, then the player's own mode, then a running autoplay loop.
    /// Companion-role instances inherit the "coop" mode, as the mode gate always allowed.
    /// </summary>
    public static DualLaunchBlockReason Evaluate(
        bool isCompanion,
        string? overlayPlayMode,
        bool playRunning,
        bool teammateStartInFlight,
        bool teammateLaunchInFlight,
        bool modeSwitchInFlight)
    {
        if (teammateStartInFlight || modeSwitchInFlight)
        {
            return DualLaunchBlockReason.Busy;
        }

        if (!isCompanion && !string.Equals(overlayPlayMode, "coop", StringComparison.Ordinal))
        {
            return DualLaunchBlockReason.SoloMode;
        }

        if (playRunning)
        {
            return DualLaunchBlockReason.AutoplayRunning;
        }

        if (teammateLaunchInFlight)
        {
            return DualLaunchBlockReason.Busy;
        }

        return DualLaunchBlockReason.None;
    }

    /// <summary>True when the block means a concurrent attempt is in flight, not a policy refusal.</summary>
    public static bool IsBusy(this DualLaunchBlockReason reason)
    {
        return reason == DualLaunchBlockReason.Busy;
    }
}
