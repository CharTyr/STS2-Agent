namespace STS2AIAgent.Agent;

/// <summary>
/// Structured result of a local dual-instance (AI teammate) launch attempt.
/// Callers classify on this value instead of matching <see cref="AgentRuntime.DualStatus"/>,
/// which is localized display text and therefore not a reliable success signal.
/// </summary>
internal enum DualLaunchOutcome
{
    /// <summary>No launch has been attempted yet; an untouched value is never a success.</summary>
    Idle,

    /// <summary>A concurrent launch is already in flight; this attempt is neither success nor failure.</summary>
    InProgress,

    /// <summary>The teammate instance was launched and its API connection was confirmed.</summary>
    Succeeded,

    /// <summary>The launch was refused before starting (companion window, running auto-play, wrong screen, pending team work).</summary>
    Rejected,

    /// <summary>The launch started but failed (lobby creation, process exit, or an unexpected error).</summary>
    Failed,

    /// <summary>The launch was canceled while waiting for the teammate to connect.</summary>
    Canceled
}

/// <summary>
/// Pure classification policy for <see cref="DualLaunchOutcome"/>. Kept free of Godot and
/// MegaCrit references so the offline test harness can exercise it.
/// </summary>
internal static class DualLaunchOutcomePolicy
{
    /// <summary>Rejected, failed, and canceled attempts are all reported as a failed invite.</summary>
    public static bool IsFailure(DualLaunchOutcome outcome)
    {
        return outcome is DualLaunchOutcome.Rejected
            or DualLaunchOutcome.Failed
            or DualLaunchOutcome.Canceled;
    }

    /// <summary>A concurrent launch is still in flight; the caller must not report completion.</summary>
    public static bool IsInProgress(DualLaunchOutcome outcome)
    {
        return outcome == DualLaunchOutcome.InProgress;
    }
}
