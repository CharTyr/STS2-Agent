using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>
/// The play-session persistence wiring: which run the in-memory conversation/decisions/strategy belong
/// to, when that context is dirty, and how it is saved and restored around a run boundary.
/// </summary>
/// <remarks>
/// The runtime owns the live context (<see cref="_history"/>, <see cref="_decisions"/>,
/// <see cref="_strategyStore"/>); this partial owns its durability. The model is dirty-and-flush, the
/// same shape the overlay's settings use: every append or update marks the session dirty, and a flush
/// writes the current run's record. A flush happens when the run changes, when auto-play pauses, and
/// when the game exits -- the moments the in-memory copy is about to be abandoned.
///
/// Restore is keyed on the run seed the boundary latches. When the loop observes a run_id that differs
/// from the one the current session belongs to, the old session is flushed and the new run's record is
/// loaded: the conversation goes back into <see cref="_history"/>, the decisions into a restored buffer
/// the play loop's memory provider prefers, and the strategy back into the store. A run with no saved
/// session starts empty, which is what keeps a fresh run from inheriting the last one's context.
/// </remarks>
internal sealed partial class AgentRuntime
{
    private readonly PlaySessionStore _sessionStore = new();

    /// <summary>The run the in-memory session context belongs to; null before any run is known.</summary>
    private string? _sessionRunId;

    /// <summary>True when the live context has changes not yet written to the session file.</summary>
    private bool _sessionDirty;

    /// <summary>
    /// Decisions restored from a continued run's session, fed to the play loop's memory provider in
    /// place of the live log while this run is the active one. Null when the run was started fresh.
    /// </summary>
    private IReadOnlyList<DecisionLogEntry>? _restoredDecisions;

    /// <summary>Marks the current run's session dirty; the next flush writes it.</summary>
    private void MarkSessionDirty()
    {
        lock (_gate)
        {
            _sessionDirty = true;
        }
    }

    /// <summary>
    /// Records a decision and marks the session dirty so the run's decision memory is persisted on the
    /// next flush. Lives beside the session wiring because the dirty mark is what ties a recorded
    /// decision to the run's saved context.
    /// </summary>
    internal DecisionLogEntry RecordDecision(
        string source,
        string action,
        string? reason = null,
        string? stateFingerprint = null,
        int requestsSpent = 0,
        int? totalTokens = null,
        string? runId = null,
        double? confidence = null)
    {
        var entry = _decisions.Record(
            source,
            action,
            reason,
            stateFingerprint,
            requestsSpent,
            totalTokens,
            // The caller may know the run (the HTTP route and the native MCP tool both do); when it
            // does not, the boundary's observation is the best available answer.
            runId: runId ?? _runBoundary.RunId,
            confidence: confidence);
        MarkSessionDirty();
        return entry;
    }

    /// <summary>
    /// Writes the current run's session when it is dirty and belongs to a persistable run. Safe to call
    /// liberally -- a clean session or the placeholder run is a no-op, and the store is best-effort.
    /// </summary>
    private void FlushSessionIfDirty()
    {
        string? runId;
        List<ChatTurn> chat;
        IReadOnlyList<DecisionLogEntry> decisions;
        PlayStrategy strategy;
        lock (_gate)
        {
            if (!_sessionDirty || !PlaySessionStore.IsPersistable(_sessionRunId))
            {
                return;
            }

            runId = _sessionRunId;
            chat = _history.ToList();
            decisions = _decisions.Snapshot(PlaySessionStore.MaxDecisions);
            strategy = _strategyStore.Current;
            _sessionDirty = false;
        }

        _sessionStore.Save(new PlaySessionRecord
        {
            RunId = runId!,
            Character = null,
            Chat = chat,
            Decisions = decisions.ToList(),
            Strategy = strategy
        });
    }

    /// <summary>
    /// Reconciles the session with the run the boundary currently reports. Called from the play loop
    /// each turn; a run_id that differs from the session's run flushes the old session and loads the
    /// new one. The first real run_id after the placeholder simply adopts the run without a load when
    /// no session exists for it.
    /// </summary>
    private void ObserveSessionRunBoundary(string? boundaryRunId)
    {
        if (!PlaySessionStore.IsPersistable(boundaryRunId))
        {
            return;
        }

        string? previous;
        lock (_gate)
        {
            if (boundaryRunId == _sessionRunId)
            {
                return;
            }

            previous = _sessionRunId;
        }

        // Leaving a run: flush whatever that run accumulated before its context is replaced.
        if (previous != null)
        {
            FlushSessionIfDirty();
        }

        var record = _sessionStore.Load(boundaryRunId);
        lock (_gate)
        {
            _sessionRunId = boundaryRunId;
            _sessionDirty = false;
            if (record != null)
            {
                ResetHistoryLocked();
                if (record.Chat != null)
                {
                    _history.AddRange(record.Chat.TakeLast(ChatHistoryLimit));
                }

                _restoredDecisions = record.Decisions;
                if (record.Strategy != null)
                {
                    _strategyStore.Update(record.Strategy);
                }
            }
            else
            {
                // A fresh run starts with an empty conversation and no restored memory.
                ResetHistoryLocked();
                _restoredDecisions = null;
            }
        }

        RaiseChanged();
    }

    /// <summary>
    /// The decision sequence the play loop's memory provider should read: the restored per-run buffer
    /// while this run is the active one, else the live log's tail. Read under the gate so a restore
    /// mid-turn cannot hand the loop a torn list.
    /// </summary>
    private IReadOnlyList<DecisionLogEntry> RecentDecisionsForMemory()
    {
        lock (_gate)
        {
            if (_restoredDecisions != null)
            {
                return _restoredDecisions;
            }
        }

        return _decisions.Snapshot(40);
    }
}
