using System.Text.Json;
using System.Text.Json.Serialization;
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
    /// <summary>Cap on persisted chat turns; the overlay keeps the same number in memory.</summary>
    internal const int MaxChatTurns = 80;

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
                    return null;
                }

                var record = JsonSerializer.Deserialize<PlaySessionRecord>(File.ReadAllText(path), JsonOptions);
                return record == null ? RecoverFromCorrupt(path) : Normalize(record);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return RecoverFromCorrupt(path);
            }
        }
    }

    /// <summary>
    /// Persists <paramref name="record"/> atomically, redacting text and trimming to the caps first.
    /// The placeholder run and any IO failure are swallowed -- persistence never blocks play.
    /// </summary>
    public void Save(PlaySessionRecord record)
    {
        if (!IsPersistable(record.RunId))
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                WriteAtomic(PathFor(record.RunId), Normalize(record));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: a session that cannot be written is lost, not fatal.
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
                var path = PathFor(runId!);
                if (File.Exists(path))
                {
                    File.Delete(path);
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
        var chat = record.Chat == null
            ? new List<ChatTurn>()
            : record.Chat
                .Select(turn => new ChatTurn { Role = turn.Role, Text = DiagnosticExport.Redact(turn.Text) })
                .TakeLast(MaxChatTurns)
                .ToList();

        var decisions = record.Decisions == null
            ? new List<DecisionLogEntry>()
            : record.Decisions
                .Select(entry => entry with { reason = DiagnosticExport.Redact(entry.reason) })
                .TakeLast(MaxDecisions)
                .ToList();

        return record with
        {
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Chat = chat,
            Decisions = decisions
        };
    }

    /// <summary>Moves a corrupt file aside and restores the last-good backup when one exists.</summary>
    private PlaySessionRecord? RecoverFromCorrupt(string path)
    {
        try
        {
            var backup = path + ".bak";
            if (File.Exists(path))
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                File.Copy(path, path + ".corrupt-" + stamp, overwrite: false);
            }

            if (File.Exists(backup))
            {
                var record = JsonSerializer.Deserialize<PlaySessionRecord>(File.ReadAllText(backup), JsonOptions);
                if (record != null)
                {
                    File.Copy(backup, path, overwrite: true);
                    return Normalize(record);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
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

    private string PathFor(string runId)
    {
        // A run seed is opaque game data; keep it from becoming a path by stripping separators.
        var safe = string.Concat(runId.Select(c => c is '/' or '\\' or ':' ? '_' : c));
        return System.IO.Path.Combine(_directory, safe + ".json");
    }
}
