using System.Text.Json;
using STS2AIAgent.Game;

namespace STS2AIAgent.Agent;

/// <summary>Run-isolated memory and best-effort, revision-aware persistence for every decision writer.</summary>
internal sealed partial class AgentRuntime
{
    private readonly PlaySessionStore _sessionStore = new();
    private readonly PlaySessionMemory _sessionMemory = new();
    private readonly object _sessionWriteGate = new();
    private readonly Dictionary<string, PlaySessionRecord> _pendingSessionWrites = new(StringComparer.Ordinal);
    private string? _sessionRunId;
    // Delayed callbacks from a prior run cannot re-activate it. A fresh game-thread state read
    // may confirm that seed again (e.g. a legitimately continued game).
    private readonly HashSet<string> _retiredSessionRuns = new(StringComparer.Ordinal);
    private bool _sessionDirty;
    private long _sessionRevision;

    private void MarkSessionDirty()
    {
        lock (_gate) { _sessionDirty = true; _sessionRevision++; }
    }

    // The shared log is diagnostic across all runs. An old action can finish after the UI has
    // entered another run; its log entry must never choose the active chat/strategy session.
    private void OnSessionDecision(DecisionLogEntry entry)
    {
        if (!PlaySessionStore.IsPersistable(entry.run_id)) return;
        lock (_sessionWriteGate)
        {
            string? active;
            lock (_gate) active = _sessionRunId;
            if (entry.run_id == active)
            {
                if (_sessionMemory.Record(entry)) MarkSessionDirty();
            }
            else
            {
                // Keep a late accepted action with the run that owned it without restoring its
                // chat/strategy into the live session. Failed saves remain pending for retry.
                var old = _pendingSessionWrites.TryGetValue(entry.run_id!, out var pending)
                    ? pending : _sessionStore.Load(entry.run_id);
                var history = new PlaySessionMemory();
                history.Restore(entry.run_id!, old?.Decisions);
                if (history.Record(entry))
                    _pendingSessionWrites[entry.run_id!] = (old ?? new PlaySessionRecord { RunId = entry.run_id! })
                        with { Decisions = history.Snapshot(PlaySessionStore.MaxDecisions).ToList() };
            }
            // A log entry alone is never proof of which run the game currently displays.
            if (!PlayRunning) FlushSessionIfDirty();
        }
    }

    internal DecisionLogEntry RecordDecision(string source, string action, string? reason = null,
        string? stateFingerprint = null, int requestsSpent = 0, int? totalTokens = null,
        string? runId = null, double? confidence = null, bool runIdObserved = false)
    {
        string? activeRun;
        // An explicit pre-action snapshot may deliberately be unknown (menu action). Do not fill
        // its null from the last autoplay run; only a caller without an observed ID may fall back.
        lock (_gate) activeRun = runIdObserved ? runId : runId ?? _sessionRunId ?? _runBoundary.RunId;
        return _decisions.Record(source, action, reason, stateFingerprint, requestsSpent, totalTokens,
            runId: activeRun, confidence: confidence);
    }

    // Writers are serialized without blocking history updates on IO. A failed save stays pending
    // across run switches; an older snapshot cannot mark a concurrent new turn clean.
    private void FlushSessionIfDirty()
    {
        lock (_sessionWriteGate)
        {
            string? runId;
            long revision;
            lock (_gate)
            {
                runId = _sessionRunId;
                revision = _sessionRevision;
                if (_sessionDirty && PlaySessionStore.IsPersistable(runId))
                {
                    _pendingSessionWrites[runId!] = new PlaySessionRecord
                    {
                        RunId = runId!, Chat = _history.ToList(), ChatTrimmed = _historyTrimmed,
                        Decisions = _sessionMemory.Snapshot(PlaySessionStore.MaxDecisions).ToList(),
                        Strategy = _strategyStore.Current
                    };
                }
            }
            foreach (var (id, record) in _pendingSessionWrites.ToArray())
            {
                if (!_sessionStore.Save(record)) continue;
                _pendingSessionWrites.Remove(id);
                lock (_gate)
                {
                    if (id == runId && _sessionRunId == runId && _sessionRevision == revision)
                        _sessionDirty = false;
                }
            }
        }
    }

    // Only a fresh game-thread snapshot of an explicit menu state can prove that the old run
    // ended. `run_unknown` alone occurs transiently in run/unlock screens and must not clear it.
    internal void ObserveSessionStateSnapshot(string? runId, string? screen, string? phase)
    {
        if (PlaySessionStore.IsPersistable(runId))
        {
            ObserveSessionRunBoundary(runId, confirmed: true);
            return;
        }
        // Phase may briefly lag the visible screen during a transition. Require an explicit
        // menu/lobby screen as well as a non-run phase, never phase alone on a run screen.
        if (screen is not ("MAIN_MENU" or "CHARACTER_SELECT" or "MULTIPLAYER_LOBBY" or "MULTIPLAYER_LOAD")
            || phase is not ("menu" or "character_select" or "multiplayer_lobby")) return;
        lock (_sessionWriteGate)
        {
            lock (_gate) { if (_sessionRunId == null) return; }
            FlushSessionIfDirty();
            CancelStrategyRefresh();
            lock (_gate)
            {
                _retiredSessionRuns.Add(_sessionRunId!);
                _sessionRunId = null;
                DropPlayInstructionsForRun(null);
                ResetHistoryLocked();
                _sessionMemory.Restore("run_unknown", null);
                _strategyStore.Update(PlayStrategy.Default);
                _strategyPlanner = null;
                _lastPromptTokens = 0;
                _lastJevChoice = null;
                _lastJevProbabilities = null;
                _lastJevDanger = null;
                _lastJevLatency = null;
                _sessionRevision++;
                _sessionDirty = false;
            }
        }
    }

    // A confirmed observation is called in the same game-thread unit as the state read. Async
    // post-read callbacks are weaker evidence: a response from the previous run may arrive late.
    internal void ObserveSessionRunBoundary(string? boundaryRunId, bool confirmed = false)
    {
        if (!PlaySessionStore.IsPersistable(boundaryRunId)) return;
        lock (_sessionWriteGate)
        {
            lock (_gate)
            {
                if (_sessionRunId == boundaryRunId) return;
                if (!confirmed && _retiredSessionRuns.Contains(boundaryRunId!)) return;
            }
            FlushSessionIfDirty();
            CancelStrategyRefresh();
            var record = _pendingSessionWrites.TryGetValue(boundaryRunId!, out var pending)
                ? pending : _sessionStore.Load(boundaryRunId);
            lock (_gate)
            {
                if (_sessionRunId != null) _retiredSessionRuns.Add(_sessionRunId);
                if (confirmed) _retiredSessionRuns.Remove(boundaryRunId!);
                _sessionRunId = boundaryRunId;
                DropPlayInstructionsForRun(boundaryRunId);
                ResetHistoryLocked();
                if (record?.Chat != null) _history.AddRange(record.Chat.TakeLast(ChatHistoryLimit));
                _historyTrimmed = record?.ChatTrimmed ?? 0;
                _sessionMemory.Restore(boundaryRunId!, record?.Decisions);
                _strategyStore.Update(record?.Strategy ?? PlayStrategy.Default);
                _strategyPlanner = null;
                _lastPromptTokens = 0;
                _lastJevChoice = null;
                _lastJevProbabilities = null;
                _lastJevDanger = null;
                _lastJevLatency = null;
                _sessionRevision++;
                _sessionDirty = false;
            }
        }
    }

    private async Task ObserveCurrentSessionAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Confirm within the posted work, not after awaiting its response on an arbitrary thread.
        await GameThread.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            var state = GameStateService.BuildStatePayload();
            ObserveSessionStateSnapshot(state.run_id, state.screen, state.session.phase);
        });
    }

    private void ObserveSessionState(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("run_id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            // The callback may describe a frame observed before a newer /state or /action.
            // Only the game thread's fresh read can establish a different active session.
            var runId = id.GetString();
            lock (_gate) { if (_sessionRunId != runId) return; }
            ObserveSessionRunBoundary(runId);
        }
    }

    private IReadOnlyList<DecisionLogEntry> RecentDecisionsForMemory() => _sessionMemory.Snapshot();
}
