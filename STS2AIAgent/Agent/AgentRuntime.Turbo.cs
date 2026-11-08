using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Game;

namespace STS2AIAgent.Agent;

/// <summary>
/// The runtime half of game speed while the AI plays (W1): engage when auto-play starts, release
/// when the loop ends for any reason, and repair a boost a crashed session left behind.
/// </summary>
/// <remarks>
/// The game objects are only touched on the game thread (<see cref="TurboController"/>); this file
/// owns the persisted marker (<see cref="Config.AgentSettings.TurboRestoreFastMode"/>), which is what
/// lets the next launch give the player their setting back after a crash.
/// </remarks>
internal sealed partial class AgentRuntime
{
    private static readonly TimeSpan TurboGameThreadBudget = TimeSpan.FromSeconds(5);

    /// <summary>Apply the configured speed at the start of an auto-play loop. Never throws.</summary>
    private async Task EngageTurboAsync(CancellationToken token)
    {
        try
        {
            string mode;
            string? persisted;
            lock (_gate)
            {
                mode = _settings.TurboMode;
                persisted = _settings.TurboRestoreFastMode;
            }

            var original = await GameThread.InvokeAsync(() => TurboController.Engage(mode, persisted), TurboGameThreadBudget, token);
            if (original != null)
            {
                PersistTurboOriginal(original);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Paused before the speed was applied; the finally-side release covers a late apply.
        }
        catch (Exception ex)
        {
            Log.Warn("[STS2AIAgent.Turbo] Engage failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// Give the player their speed back when the loop ends (pause, stop, failure, run end). Not tied
    /// to the loop's token -- that token is what just ended -- and bounded, so a game thread that
    /// stopped pumping cannot hold the loop's exit; the posted release still runs when it resumes.
    /// </summary>
    private async Task ReleaseTurboAsync()
    {
        try
        {
            var cleared = await GameThread.InvokeAsync(() => TurboController.Release(), TurboGameThreadBudget, CancellationToken.None);
            if (cleared)
            {
                PersistTurboOriginal(null);
            }
        }
        catch (Exception ex)
        {
            // The marker stays set, so the next launch repairs the speed if this release never ran.
            Log.Warn("[STS2AIAgent.Turbo] Release did not complete: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// After a crash the persisted original is still set. Poll (bounded) until the game has loaded its
    /// prefs, then put the player's value back. Fire-and-forget from mod start; never throws.
    /// </summary>
    public void ScheduleTurboRecovery()
    {
        string? stored;
        lock (_gate) stored = _settings.TurboRestoreFastMode;
        if (string.IsNullOrWhiteSpace(stored))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    if (PlayRunning) return;
                    var done = await GameThread.InvokeAsync(() => TurboController.RecoverAtStartup(stored), TurboGameThreadBudget, CancellationToken.None);
                    if (done)
                    {
                        PersistTurboOriginal(null);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("[STS2AIAgent.Turbo] Startup recovery attempt failed: " + ex.GetType().Name);
                }
            }
        });
    }

    private void PersistTurboOriginal(string? original)
    {
        lock (_gate)
        {
            if (string.Equals(_settings.TurboRestoreFastMode, original, StringComparison.Ordinal))
            {
                return;
            }

            _settings.TurboRestoreFastMode = original;
            SaveSettingsQuietly();
        }
    }
}
