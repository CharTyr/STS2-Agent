using STS2AIAgent.Config;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentRuntime
{
    /// <summary>Pause the solo loop and confirm it finished the submitted action before switching modes.</summary>
    /// <returns>False on deadline/cancellation or an unexpected loop failure; does not change the mode.</returns>
    public async Task<bool> PauseForModeSwitchAsync(CancellationToken cancellationToken)
    {
        if (InstanceRole.IsCompanion) return false;
        CancelStrategyRefresh();
        var stopping = _playSession.RequestPause();
        SetStatus(stopping.IsCompleted ? Loc.T("已暂停自动游玩") : Loc.T("正在暂停，等待当前任务完成…"));
        try
        {
            await stopping.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (AutoPlayStoppedException)
        {
            // It ended because its own stop policy fired. It is no longer acting.
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            if (PlayPhase != "paused") return false;
        }
        if (PlayPhase != "paused") return false;
        FlushSessionIfDirty();
        SetStatus(Loc.T("已暂停自动游玩"));
        return true;
    }
}
