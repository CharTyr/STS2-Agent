using System.Text.Json;
using System.Text.Json.Serialization;

namespace STS2AIAgent.Agent;

internal sealed record DecisionLogEntry(
    long id,
    string timestamp,
    string source,
    string action,
    string? reason,
    string? state_fingerprint,
    int requests_spent,
    int? total_tokens);

/// <summary>
/// Owns the bounded, redacted record of accepted agent decisions.
/// </summary>
/// <remarks>
/// Callers only record accepted actions and read snapshots; file rotation, serialization failures,
/// redaction and concurrency stay inside this module. Persistence is best-effort so an unwritable
/// diagnostic path can never stop gameplay.
/// </remarks>
internal sealed class DecisionLog
{
    private const int DefaultCapacity = 200;
    private const long DefaultMaxFileBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly List<DecisionLogEntry> _entries = new();
    private readonly string? _path;
    private readonly int _capacity;
    private readonly long _maxFileBytes;
    private long _nextId;

    public DecisionLog(string? path = null, int capacity = DefaultCapacity, long maxFileBytes = DefaultMaxFileBytes)
    {
        _path = path;
        _capacity = Math.Max(1, capacity);
        _maxFileBytes = Math.Max(1, maxFileBytes);
    }

    /// <summary>
    /// Raised after an entry is stored, for consumers that mirror the log elsewhere (the SSE
    /// stream). Handlers run outside the lock so a slow subscriber cannot stall recording, and a
    /// throwing handler must not lose the entry -- the log is already committed at that point.
    /// </summary>
    public event Action<DecisionLogEntry>? Recorded;

    public DecisionLogEntry Record(
        string source,
        string action,
        string? reason = null,
        string? stateFingerprint = null,
        int requestsSpent = 0,
        int? totalTokens = null,
        DateTimeOffset? timestamp = null)
    {
        var safeSource = Clean(source, "unknown", 48);
        var safeAction = Clean(action, "unknown", 96);
        var safeReason = CleanOptional(reason, 500);
        var safeFingerprint = CleanOptional(stateFingerprint, 256);

        DecisionLogEntry entry;
        lock (_gate)
        {
            entry = new DecisionLogEntry(
                ++_nextId,
                (timestamp ?? DateTimeOffset.UtcNow).ToString("O"),
                safeSource,
                safeAction,
                safeReason,
                safeFingerprint,
                Math.Max(0, requestsSpent),
                totalTokens is >= 0 ? totalTokens : null);

            _entries.Add(entry);
            if (_entries.Count > _capacity)
            {
                _entries.RemoveRange(0, _entries.Count - _capacity);
            }

            AppendBestEffort(entry);
        }

        // Notified after the entry is committed and the lock is released: a mirror must not be able
        // to stall recording, and a throwing one must not lose a decision that already happened.
        try
        {
            Recorded?.Invoke(entry);
        }
        catch (Exception)
        {
            // A mirroring failure is not the recorder's failure.
        }

        return entry;
    }

    public IReadOnlyList<DecisionLogEntry> Snapshot(int limit = 50)
    {
        lock (_gate)
        {
            var take = Math.Clamp(limit, 1, _capacity);
            var start = Math.Max(0, _entries.Count - take);
            return _entries.Skip(start).ToArray();
        }
    }

    public string RenderJson(int limit = 50)
    {
        return JsonSerializer.Serialize(new { decisions = Snapshot(limit) }, JsonOptions);
    }

    private void AppendBestEffort(DecisionLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_path) && new FileInfo(_path).Length >= _maxFileBytes)
            {
                File.Move(_path, _path + ".previous", overwrite: true);
            }

            File.AppendAllText(_path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
        catch (Exception)
        {
            // Diagnostics must never stop or alter gameplay.
        }
    }

    private static string Clean(string? value, string fallback, int maxLength)
    {
        var clean = CleanOptional(value, maxLength);
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static string? CleanOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = DiagnosticExport.Redact(value.Trim())
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }
}
