namespace STS2AIAgent.Game;

/// <summary>
/// Bounded, thread-safe record of runtime faults caught while building optional <c>/state</c>
/// sub-sections. Exposed on <c>GET /health.compatibility.state_builder_faults</c> so an upstream
/// game patch that breaks one screen reader degrades that section to <c>null</c> instead of
/// failing every <c>/state</c> request with HTTP 500, while remaining immediately visible to
/// players, logs and validation tooling.
/// </summary>
internal sealed class StateSectionFaultTracker
{
    public const int MaxTrackedSections = 32;
    internal static readonly TimeSpan LogRepeatInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public string Section { get; init; } = string.Empty;
        public bool Active { get; set; }
        public int Count { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset LastSeenUtc { get; set; }
        public DateTimeOffset LastLoggedUtc { get; set; }
        public string LastLoggedSignature { get; set; } = string.Empty;
    }

    internal sealed record FaultSnapshot(
        string section,
        bool active,
        int count,
        string error_type,
        string message,
        string last_seen_utc);

    /// <summary>
    /// Records one exception thrown while building <paramref name="section"/> and returns whether
    /// the caller should emit a warning log entry (deduplicated per signature within
    /// <see cref="LogRepeatInterval"/> so 60 Hz ticks cannot flood the game log).
    /// </summary>
    public bool RecordFault(string section, Exception exception, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return false;
        }

        var errorType = exception.GetType().Name;
        var message = TrimMessage(exception.Message);
        var signature = errorType + ":" + message;

        lock (_gate)
        {
            if (!_entries.TryGetValue(section, out var entry))
            {
                if (_entries.Count >= MaxTrackedSections)
                {
                    EvictOldestInactiveOrOldest();
                }

                entry = new Entry { Section = section };
                _entries[section] = entry;
            }

            entry.Active = true;
            entry.Count++;
            entry.ErrorType = errorType;
            entry.Message = message;
            entry.LastSeenUtc = utcNow;

            var shouldLog = !string.Equals(entry.LastLoggedSignature, signature, StringComparison.Ordinal)
                || entry.LastLoggedUtc == default
                || utcNow - entry.LastLoggedUtc >= LogRepeatInterval;
            if (shouldLog)
            {
                entry.LastLoggedSignature = signature;
                entry.LastLoggedUtc = utcNow;
            }

            return shouldLog;
        }
    }

    /// <summary>Marks a previously faulting section healthy again without erasing its fault history.</summary>
    public void RecordSuccess(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(section, out var entry))
            {
                entry.Active = false;
            }
        }
    }

    public bool HasActiveFaults
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values.Any(entry => entry.Active);
            }
        }
    }

    public FaultSnapshot[] Snapshot()
    {
        lock (_gate)
        {
            return _entries.Values
                .OrderByDescending(entry => entry.Active)
                .ThenBy(entry => entry.Section, StringComparer.Ordinal)
                .Select(entry => new FaultSnapshot(
                    entry.Section,
                    entry.Active,
                    entry.Count,
                    entry.ErrorType,
                    entry.Message,
                    entry.LastSeenUtc.ToString("o")))
                .ToArray();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void EvictOldestInactiveOrOldest()
    {
        var victim = _entries.Values
            .OrderBy(entry => entry.Active ? 1 : 0)
            .ThenBy(entry => entry.LastSeenUtc)
            .FirstOrDefault();
        if (victim != null)
        {
            _entries.Remove(victim.Section);
        }
    }

    private static string TrimMessage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var flat = raw.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= 240 ? flat : flat[..240] + "...";
    }
}
