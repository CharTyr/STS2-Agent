using STS2AIAgent.Config;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>One in-process ownership gate for the visible solo/companion mode transition.</summary>
internal sealed partial class AgentRuntime
{
    private readonly object _modeControlGate = new();
    private bool _modeSwitchInFlight;
    private bool _teammateStartInFlight;
    private bool _teammateLaunchInFlight;

    /// <summary>Close both autoplay start routes while a pause is awaiting confirmation.</summary>
    public bool TryBeginPlayModeSwitch(string expectedMode)
    {
        lock (_modeControlGate)
        {
            if (_modeSwitchInFlight || _teammateStartInFlight || _teammateLaunchInFlight
                || InstanceRole.IsCompanion || Settings.OverlayPlayMode != expectedMode)
                return false;
            _modeSwitchInFlight = true;
            return true;
        }
    }

    public void EndPlayModeSwitch()
    {
        lock (_modeControlGate) _modeSwitchInFlight = false;
    }

    /// <summary>Caller holds the mode gate while it chooses a start route.</summary>
    private bool CanStartModeLocked(string mode)
    {
        if (_modeSwitchInFlight) return false;
        return InstanceRole.IsCompanion || Settings.OverlayPlayMode == mode;
    }

    /// <summary>Short gate for the teammate launch request, paired with the UI switch gate.</summary>
    private bool TryBeginTeammateStart()
    {
        lock (_modeControlGate)
        {
            if (!CanStartModeLocked("coop") || !_remoteControlGate.Wait(0)) return false;
            _teammateStartInFlight = true;
            return true;
        }
    }

    private void EndTeammateStart()
    {
        lock (_modeControlGate) _teammateStartInFlight = false;
    }

    /// <summary>
    /// Caller already owns <c>_dualLaunchGate</c>. Returns <see cref="DualLaunchBlockReason.None"/>
    /// when the launch is claimed; otherwise the reason to show the player (or
    /// <see cref="DualLaunchBlockReason.Busy"/>, where the correct answer is still "pending").
    /// </summary>
    private DualLaunchBlockReason TryMarkTeammateLaunch()
    {
        lock (_modeControlGate)
        {
            var reason = DualLaunchBlockReasonRules.Evaluate(
                InstanceRole.IsCompanion,
                Settings.OverlayPlayMode,
                PlayRunning,
                _teammateStartInFlight,
                _teammateLaunchInFlight,
                _modeSwitchInFlight);
            if (reason == DualLaunchBlockReason.None)
            {
                _teammateLaunchInFlight = true;
            }
            return reason;
        }
    }

    private void EndTeammateLaunch()
    {
        lock (_modeControlGate) _teammateLaunchInFlight = false;
    }

    /// <summary>
    /// Marks the launch in-progress on the calling thread so a pending observer does not still
    /// read DualLaunching=false or the idle DualStatus. A concurrent caller that cannot take the
    /// gate leaves a terminal outcome untouched: rewriting Succeeded/Failed/Rejected/Canceled as
    /// InProgress would make a finished attempt look like it never completed. A named refusal
    /// (solo mode, autoplay running) comes out in <paramref name="reason"/> so the caller can
    /// record it instead of answering pending forever.
    /// </summary>
    private bool TryBeginDualLaunch(out DualLaunchBlockReason reason)
    {
        reason = DualLaunchBlockReason.Busy;
        if (!_dualLaunchGate.Wait(0))
        {
            return false;
        }

        // The semaphore is ours, so a mode switch can no longer start underneath this claim.
        // A rejected claim releases it before any status write: callers that receive null must
        // keep observing the previous attempt's outcome.
        reason = TryMarkTeammateLaunch();
        if (reason != DualLaunchBlockReason.None)
        {
            _dualLaunchGate.Release();
            return false;
        }

        _dualLaunching = true;
        _dualStatus = Loc.T("正在检查组队条件…");
        _dualLaunchOutcome = DualLaunchOutcome.InProgress;
        RaiseChanged();
        return true;
    }

    /// <summary>The player-facing explanation when a launch was refused before it began.</summary>
    private static string DualLaunchBlockText(DualLaunchBlockReason reason)
    {
        return reason == DualLaunchBlockReason.SoloMode
            ? Loc.T("当前是单人模式，暂时不能组队或继续联机局。请先在游玩页切换到多人模式，再邀请 AI 队友。")
            : Loc.T("自动游玩进行中，暂时不能组队或继续联机局。请先暂停自动游玩，再邀请 AI 队友。");
    }
}
