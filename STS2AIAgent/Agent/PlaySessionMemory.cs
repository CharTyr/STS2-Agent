namespace STS2AIAgent.Agent;

/// <summary>
/// The active run's bounded decision memory. Restored and newly accepted decisions share one
/// buffer; the all-run diagnostic log is deliberately not the source of a run's prompt or save.
/// </summary>
internal sealed class PlaySessionMemory
{
    private readonly object _gate = new();
    private readonly List<DecisionLogEntry> _entries = new();
    private string? _runId;

    public void Restore(string runId, IEnumerable<DecisionLogEntry>? entries)
    {
        lock (_gate)
        {
            _runId = runId;
            _entries.Clear();
            if (entries == null) return;
            foreach (var entry in entries.TakeLast(PlaySessionStore.MaxDecisions)) Record(entry);
        }
    }

    public bool Record(DecisionLogEntry? entry)
    {
        lock (_gate)
        {
            if (entry == null || !PlaySessionStore.IsPersistable(_runId) || entry.run_id != _runId)
                return false;
            // Log ids restart with the process, so id alone cannot identify a restored decision.
            if (_entries.Any(old => old.id == entry.id && old.timestamp == entry.timestamp
                && old.source == entry.source && old.action == entry.action)) return false;
            _entries.Add(entry);
            if (_entries.Count > PlaySessionStore.MaxDecisions) _entries.RemoveAt(0);
            return true;
        }
    }

    public IReadOnlyList<DecisionLogEntry> Snapshot(int limit = 40)
    {
        lock (_gate) return _entries.TakeLast(Math.Clamp(limit, 1, PlaySessionStore.MaxDecisions)).ToArray();
    }
}
