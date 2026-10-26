using STS2AIAgent.Config;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentRuntime
{
    private readonly SemaphoreSlim _companionSettingsGate = new(1, 1);
    // Protected by the mode gate: a settings response cannot undo an explicit pause request.
    private bool _companionPauseRequested;

    /// <summary>Applies a bounded settings-only patch on the companion; the caller authenticates first.</summary>
    /// <remarks>SaveSettings persists a clone before publishing it to readers. Nothing logs the patch.</remarks>
    public async Task<bool> ApplyCompanionSettingsAsync(CompanionSettingsPatch patch, CancellationToken cancellationToken)
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion accepts live settings.");
        ArgumentNullException.ThrowIfNull(patch);
        if (!patch.IsValid) throw new ArgumentException("Invalid companion Jev settings.", nameof(patch));
        if (!_companionSettingsGate.Wait(0)) throw new InvalidOperationException("Companion settings update already in progress.");
        var controlHeld = false;
        try
        {
            await _companionControlGate.WaitAsync(cancellationToken);
            controlHeld = true;
            var wasRunning = PlayPhase == "running";
            if (patch.PlaySettings != null)
            {
                var stopping = _playSession.RequestPause();
                try { await stopping.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
                catch (AutoPlayStoppedException) { }
            }
            cancellationToken.ThrowIfCancellationRequested();
            // SaveSettings writes the clone before publishing it to concurrent readers. This gate
            // serializes incoming patches; it is not held across arbitrary cancellation callbacks.
            var copy = SettingsClone.Clone(Settings);
            patch.ApplyTo(copy);
            SaveSettings(copy);
            cancellationToken.ThrowIfCancellationRequested();
            if (wasRunning && patch.PlaySettings != null && FirstRunSetup.Evaluate(copy).ReadyToInvite)
            {
                lock (_modeControlGate)
                {
                    if (!_companionPauseRequested) StartAutoPlay();
                }
            }
            return copy.DualLayerCoopEnabled;
        }
        finally
        {
            if (controlHeld) _companionControlGate.Release();
            _companionSettingsGate.Release();
        }
    }

    public async Task<string> SetCompanionRunningAsync(bool running, CancellationToken cancellationToken)
    {
        if (!InstanceRole.IsCompanion) throw new InvalidOperationException("Only a companion can receive control requests.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!running)
        {
            lock (_modeControlGate)
            {
                _companionPauseRequested = true;
                _companionAutoStartSuppressed = true;
            }
            // Cancel further dispatch before waiting behind a configuration save or submitted action.
            _ = _playSession.RequestPause();
        }
        await _companionControlGate.WaitAsync(cancellationToken);
        try
        {
            _companionAutoStartSuppressed = true;
            if (running)
            {
                if (!_companionReady) throw new InvalidOperationException(Loc.T("队友尚未完成组队，请稍后继续。"));
                // Starting the in-process loop is one decision no matter who asks for it, so the
                // companion answers to the same model gate as the host's Resume button and
                // POST /teammate/control. Without this, the companion's own /session/control was a
                // way to start a model loop that every other entry point refuses. Pausing is always
                // allowed: no caller should be stuck unable to stop it.
                var firstRun = FirstRunSetup.Evaluate(Settings);
                if (!firstRun.ReadyToInvite) throw new InvalidOperationException(firstRun.Hint);
                lock (_modeControlGate)
                {
                    _companionPauseRequested = false;
                    StartAutoPlay();
                }
            }
            else
            {
                var stopping = _playSession.RequestPause();
                SetStatus(stopping.IsCompleted ? Loc.T("已暂停自动游玩") : Loc.T("正在暂停，等待当前任务完成…"));
                NoteEvent(Status);
                try
                {
                    await stopping.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (AutoPlayStoppedException)
                {
                    // The loop already ended; treat that as a completed pause.
                }
                if (PlayPhase == "paused") SetStatus(Loc.T("已暂停自动游玩"));
            }
            return PlayPhase;
        }
        finally { _companionControlGate.Release(); }
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
