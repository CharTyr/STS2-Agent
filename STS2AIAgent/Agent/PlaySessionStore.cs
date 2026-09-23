using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>
/// Best-effort persistence for per-run play sessions, one JSON file per run under a
/// <c>sessions/</c> directory beside the settings file.
/// </summary>
/// <remarks>
/// The store follows the same contract as <see cref="DecisionLog"/>: persistence is best-effort, so an
/// unwritable path or a corrupt file can never stop gameplay -- a failed load returns null and a failed
/// save is swallowed. Writes are atomic (temp file + replace, with a <c>.bak</c> of the last good
/// session) so a crash mid-write cannot leave a half-written session that a later load would trip on.
///
/// Two rules keep the files safe to keep: the placeholder <c>run_unknown</c> is never persisted (it is
/// not a run, and persisting it would merge every pre-run conversation into one bucket), and every text
/// field passes through <see cref="DiagnosticExport.Redact"/> before serialization so an API key pasted
/// into chat cannot land on disk.
/// </remarks>
internal sealed class PlaySessionStore
{
    /// <summary>
    /// Cap on persisted chat turns. The overlay renders the in-memory tail and a continued run
    /// restores the same tail, so this is the shared number between the runtime's history cap and the
    /// file: a longer on-screen log is not silently cut in half by what was saved.
    /// </summary>
    internal const int MaxChatTurns = 200;

    /// <summary>Cap on persisted decisions, matching the live <see cref="DecisionLog"/> window.</summary>
    internal const int MaxDecisions = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly string _directory;

    public PlaySessionStore(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory();
    }

    /// <summary>The directory session files live in.</summary>
    public string Directory => _directory;

    /// <summary>
    /// The default <c>sessions/</c> directory beside the settings file, so a custom
    /// <c>STS2_AGENT_SETTINGS_PATH</c> relocates the sessions with the rest of the mod's state.
    /// </summary>
    public static string DefaultDirectory()
    {
        var settingsPath = SettingsStore.DefaultPath();
        var root = System.IO.Path.GetDirectoryName(settingsPath) ?? ".";
        return System.IO.Path.Combine(root, "sessions");
    }

    /// <summary>
    /// Loads the session for <paramref name="runId"/>, or null when there is none, the id is the
    /// placeholder, or the file cannot be read. A corrupt file is moved aside and, when a last-good
    /// backup exists, restored from it; a load never throws.
    /// </summary>
    public PlaySessionRecord? Load(string? runId)
    {
        if (!IsPersistable(runId))
        {
            return null;
        }

        lock (_gate)
        {
            var path = PathFor(runId!);
            try
            {
                if (!File.Exists(path))
                {
                    // Continue legacy seed-named files only after validating their identity.
                    var legacy = LegacyPathFor(runId!);
                    if (File.Exists(path + ".bak")) return RecoverFromCorrupt(path, runId!);
                    if (!File.Exists(legacy)) return File.Exists(legacy + ".bak") ? RecoverFromCorrupt(legacy, runId!) : null;
                    path = legacy;
                }

                var record = JsonSerializer.Deserialize<PlaySessionRecord>(File.ReadAllText(path), JsonOptions);
                if (record == null) return RecoverFromCorrupt(path, runId!);
                return record.RunId == runId ? Normalize(record) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return RecoverFromCorrupt(path, runId!);
            }
        }
    }

    /// <summary>
    /// Persists <paramref name="record"/> atomically, redacting text and trimming to the caps first.
    /// The placeholder run and any IO failure are swallowed -- persistence never blocks play.
    /// </summary>
    public bool Save(PlaySessionRecord record)
    {
        if (!IsPersistable(record.RunId))
        {
            return false;
        }

        lock (_gate)
        {
            try
            {
                var path = PathFor(record.RunId);
                if (!PreserveLegacyCollision(path, record.RunId)) return false;
                WriteAtomic(path, Normalize(record) with { UpdatedAt = DateTimeOffset.UtcNow.ToString("O") });
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Leave the runtime dirty so a later flush can retry.
                return false;
            }
        }
    }

    /// <summary>Deletes the session for <paramref name="runId"/>; a missing file is not an error.</summary>
    public void Delete(string? runId)
    {
        if (!IsPersistable(runId))
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                var canonical = PathFor(runId!);
                foreach (var path in new[] { canonical, LegacyPathFor(runId!) }.Distinct(StringComparer.Ordinal))
                {
                    foreach (var suffix in new[] { "", ".bak", ".tmp" })
                    {
                        var file = path + suffix;
                        if (!File.Exists(file)) continue;
                        // Legacy names can collide. Never delete another run's surviving file.
                        try
                        {
                            if (JsonSerializer.Deserialize<PlaySessionRecord>(File.ReadAllText(file), JsonOptions)?.RunId != runId) continue;
                        }
                        catch (JsonException) { if (path != canonical) continue; }
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Only a real run seed is persisted; the placeholder and blank ids are not runs.</summary>
    internal static bool IsPersistable(string? runId)
    {
        return !string.IsNullOrWhiteSpace(runId) && runId != "run_unknown";
    }

    /// <summary>
    /// Redacts text, trims the collections to their caps (keeping the newest), and stamps the write
    /// time. Returns a record that is safe to serialize.
    /// </summary>
    private static PlaySessionRecord Normalize(PlaySessionRecord record)
    {
        var validChat = (record.Chat ?? new List<ChatTurn>()).Where(turn => turn != null).ToList();
        var chat = validChat
                .Select(turn => new ChatTurn { Role = DiagnosticExport.Redact(turn.Role ?? "assistant"), Text = DiagnosticExport.Redact(turn.Text ?? "") })
                .TakeLast(MaxChatTurns)
                .ToList();

        var decisions = record.Decisions == null
            ? new List<DecisionLogEntry>()
            : record.Decisions
                .Where(entry => entry != null && entry.run_id == record.RunId)
                .Select(entry => entry with
                {
                    timestamp = DiagnosticExport.Redact(entry.timestamp ?? ""),
                    source = DiagnosticExport.Redact(entry.source ?? "unknown"),
                    action = DiagnosticExport.Redact(entry.action ?? "unknown"),
                    reason = DiagnosticExport.Redact(entry.reason),
                    state_fingerprint = DiagnosticExport.Redact(entry.state_fingerprint),
                    confidence = entry.confidence is >= 0 and <= 1 ? entry.confidence : null
                })
                .TakeLast(MaxDecisions)
                .ToList();

        return record with
        {
            Character = DiagnosticExport.Redact(record.Character),
            ChatTrimmed = (int)Math.Min(int.MaxValue, (long)Math.Max(0, record.ChatTrimmed) + Math.Max(0, validChat.Count - MaxChatTurns)),
            Chat = chat,
            Decisions = decisions,
            Strategy = record.Strategy == null ? null : NormalizeStrategy(record.Strategy)
        };
    }

    private static PlayStrategy NormalizeStrategy(PlayStrategy strategy) => strategy with
    {
        Posture = DiagnosticExport.Redact(strategy.Posture ?? "balanced"),
        Instructions = DiagnosticExport.Redact(strategy.Instructions ?? ""),
        Source = DiagnosticExport.Redact(strategy.Source ?? PlayStrategy.DefaultSource),
        UpdatedAt = DiagnosticExport.Redact(strategy.UpdatedAt ?? PlayStrategy.DefaultTimestamp),
        OptionHints = (strategy.OptionHints ?? new Dictionary<string, string>())
            .GroupBy(pair => DiagnosticExport.Redact(pair.Key))
            .ToDictionary(group => group.Key, group => DiagnosticExport.Redact(group.Last().Value ?? ""), StringComparer.Ordinal)
    };

    /// <summary>Copies a corrupt file aside and restores the last-good backup when one exists.</summary>
    private PlaySessionRecord? RecoverFromCorrupt(string path, string runId)
    {
        try
        {
            var backup = path + ".bak";
            if (File.Exists(path))
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
                File.Copy(path, path + ".corrupt-" + stamp, overwrite: false);
            }

            if (File.Exists(backup))
            {
                var record = JsonSerializer.Deserialize<PlaySessionRecord>(File.ReadAllText(backup), JsonOptions);
                if (record?.RunId == runId)
                {
                    File.Copy(backup, path, overwrite: true);
                    return Normalize(record);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return null;
    }

    private void WriteAtomic(string path, PlaySessionRecord record)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, path + ".bak");
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    // An old "Seed.json" can be the same Windows path as the new canonical "seed.json".
    // Preserve another run in its own canonical location before replacing either legacy copy.
    private bool PreserveLegacyCollision(string path, string runId)
    {
        foreach (var source in new[] { path, path + ".bak" })
        {
            if (!File.Exists(source)) continue;
            PlaySessionRecord? previous;
            try { previous = JsonSerializer.Deserialize<PlaySessionRecord>(File.ReadAllText(source), JsonOptions); }
            catch (JsonException) { continue; }
            if (previous == null || previous.RunId == runId || !IsPersistable(previous.RunId)) continue;
            var target = PathFor(previous.RunId);
            if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(target))
            {
                try
                {
                    if (JsonSerializer.Deserialize<PlaySessionRecord>(File.ReadAllText(target), JsonOptions)?.RunId == previous.RunId) continue;
                }
                catch (JsonException) { }
            }
            WriteAtomic(target, Normalize(previous));
        }
        return true;
    }

    private string PathFor(string runId)
    {
        // Hash case-sensitive, reserved, invalid and overlong seeds instead of replacing characters.
        // Ordinary lowercase names stay compatible with earlier versions and their backups.
        var reserved = runId is "con" or "prn" or "aux" or "nul"
            || (runId.Length == 4 && (runId.StartsWith("com") || runId.StartsWith("lpt")) && char.IsDigit(runId[3]));
        var simple = runId.Length <= 96 && !reserved
            && runId.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
        var name = simple ? runId : "@" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId))).ToLowerInvariant();
        return System.IO.Path.Combine(_directory, name + ".json");
    }

    private string LegacyPathFor(string runId) => System.IO.Path.Combine(_directory,
        string.Concat(runId.Select(c => c is '/' or '\\' or ':' ? '_' : c)) + ".json");
}
