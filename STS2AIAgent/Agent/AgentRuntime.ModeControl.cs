using STS2AIAgent.Config;

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

    /// <summary>Caller already owns <c>_dualLaunchGate</c>. A false result writes no launch outcome.</summary>
    private bool TryMarkTeammateLaunch()
    {
        lock (_modeControlGate)
        {
            if (!CanStartModeLocked("coop") || _teammateLaunchInFlight || PlayRunning) return false;
            _teammateLaunchInFlight = true;
            return true;
        }
    }

    private void EndTeammateLaunch()
    {
        lock (_modeControlGate) _teammateLaunchInFlight = false;
    }
}
