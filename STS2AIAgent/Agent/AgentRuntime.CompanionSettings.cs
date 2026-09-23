using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentRuntime
{
    private readonly SemaphoreSlim _companionSettingsGate = new(1, 1);

    /// <summary>Applies a bounded settings-only patch on the companion; the caller authenticates first.</summary>
    /// <remarks>SaveSettings persists a clone before publishing it to readers. Nothing logs the patch.</remarks>
    public bool ApplyCompanionSettings(CompanionSettingsPatch patch)
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion accepts live settings.");
        ArgumentNullException.ThrowIfNull(patch);
        if (!patch.IsValid) throw new ArgumentException("Invalid companion Jev settings.", nameof(patch));
        if (!_companionSettingsGate.Wait(0)) throw new InvalidOperationException("Companion settings update already in progress.");
        try
        {
            // SaveSettings writes the clone before publishing it to concurrent readers. This gate
            // serializes incoming patches; it is not held across arbitrary cancellation callbacks.
            var copy = SettingsClone.Clone(Settings);
            patch.ApplyTo(copy);
            SaveSettings(copy);
            return copy.DualLayerCoopEnabled;
        }
        finally { _companionSettingsGate.Release(); }
    }

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

    /// <summary>Settings sync status for the host. No secret is included in either property.</summary>
    public CompanionJevSnapshot CompanionJevStatus()
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion exposes this reading.");
        lock (_gate)
        {
            return new CompanionJevSnapshot
            {
                Choice = LastJevChoice,
                Probabilities = LastJevProbabilities,
                Danger = LastJevDanger,
                Latency = LastJevLatency,
                DualLayerCoopEnabled = _settings.DualLayerCoopEnabled
            };
        }
    }
}
