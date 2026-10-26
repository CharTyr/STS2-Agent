using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using STS2AIAgent.Game;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentRuntime
{
    /// <summary>
    /// Companion-only: quit this game once the host that launched it is gone (closed, crashed or
    /// force-killed), so an invisible AI teammate never keeps running and spending the budget.
    /// A companion started without a host PID (manual/dev launch) never self-quits.
    /// </summary>
    private async Task WatchHostAsync(CancellationToken cancellationToken)
    {
        if (!CompanionHostWatch.TryReadHostPid(Environment.GetEnvironmentVariable(CompanionHostWatch.EnvironmentName), out var hostPid))
        {
            return;
        }

        var hostStart = CompanionHostWatch.TryReadStartTime(hostPid);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(CompanionHostWatch.PollInterval, cancellationToken);
                if (!CompanionHostWatch.IsHostGone(hostPid, hostStart))
                {
                    continue;
                }

                Log.Warn($"{LogPrefix} Host process {hostPid} is gone; stopping the AI teammate and quitting.");
                StopAutoPlay();
                FlushSessionIfDirty();
                try
                {
                    await GameThread.InvokeAsync(() =>
                    {
                        NGame.Instance?.GetTree()?.Quit();
                        return true;
                    }, TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
                {
                }

                // A game thread that no longer pumps cannot run Quit; do not leave the process behind.
                await Task.Delay(TimeSpan.FromSeconds(15), CancellationToken.None);
                Environment.Exit(0);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

}
