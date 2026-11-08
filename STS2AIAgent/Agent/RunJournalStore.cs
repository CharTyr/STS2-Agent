using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using STS2AIAgent.Config;

namespace STS2AIAgent.Agent;

/// <summary>
/// The bounded, redacted record of finished runs: one JSON row per run in <c>journal.jsonl</c>, beside
/// the settings file so a custom <c>STS2_AGENT_SETTINGS_PATH</c> relocates it with the rest of the
/// mod's state (the same rule <see cref="PlaySessionStore"/> follows for its <c>sessions/</c> files).
/// </summary>
/// <remarks>
/// The contract is the same best-effort one the session store and the decision log follow: an
/// unwritable path, a locked file or a corrupt row can never stop play, so a failed load returns what
/// parsed and a failed save is reported as <c>false</c> rather than thrown.
///
/// Three bounds keep the file safe to keep and honest to read:
/// <list type="bullet">
/// <item>at most <see cref="MaxEntries"/> rows, newest kept -- the same order of magnitude as the
/// decision log's window, and enough for the prompt digest to talk about "your last runs";</item>
/// <item>one row per <c>run_id</c>. A run can be observed as finished from more than one read (the
/// state route, an action, the auto-play turn), and a journal that grows a duplicate per observation
/// would say a player lost the same run four times;</item>
/// <item>every text field passes through <see cref="DiagnosticExport.Redact"/> on the way in and again
/// on the way out, so a hand-edited file or a provider error quoted into a field cannot persist a key.</item>
/// </list>
///
/// Writes replace the whole file atomically (temp file + replace), because trimming to the cap means
/// the file is rewritten rather than appended to. A crash therefore leaves either the previous journal
/// or the new one, never half of each.
/// </remarks>
internal sealed class RunJournalStore
{
    /// <summary>How many finished runs are kept. The oldest row falls off the front.</summary>
    public const int MaxEntries = 50;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly string _path;

    public RunJournalStore(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    /// <summary>The journal file.</summary>
    public string Path => _path;

    /// <summary>
    /// The default <c>journal.jsonl</c> beside the settings file, so <c>STS2_AGENT_SETTINGS_PATH</c>
    /// moves the journal together with the sessions and the decision log.
    /// </summary>
    public static string DefaultPath()
    {
        var settingsPath = SettingsStore.DefaultPath();
        var root = System.IO.Path.GetDirectoryName(settingsPath) ?? ".";
        return System.IO.Path.Combine(root, "journal.jsonl");
    }

    /// <summary>
    /// Every recorded run, oldest first, at most <see cref="MaxEntries"/> rows. A missing file is an
    /// empty journal; unparsable rows are skipped individually rather than failing the whole read.
    /// </summary>
    public IReadOnlyList<RunJournalEntry> Load()
    {
        lock (_gate)
        {
            return LoadLocked();
        }
    }

    /// <summary>
    /// Appends <paramref name="entry"/> unless that run is already recorded, trims to the cap and
    /// rewrites the file atomically. Returns false (and writes nothing) on a duplicate or an IO error.
    /// </summary>
    public bool Append(RunJournalEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.RunId)) return false;

        lock (_gate)
        {
            try
            {
                var normalized = Normalize(entry);
                var entries = LoadLocked();
                if (entries.Any(existing => string.Equals(existing.RunId, normalized.RunId, StringComparison.Ordinal)))
                {
                    // The first evidence for a run is the one that owns its row; a later observation of
                    // the same finished run must not append a second line.
                    return false;
                }

                var rows = entries
                    .Concat(new[] { normalized })
                    .TakeLast(MaxEntries)
                    .ToList();
                WriteAtomic(rows);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Deletes the journal. Returns true when nothing is left on disk afterwards; a missing file is a
    /// success, because the caller's intent ("no runs recorded") already holds.
    /// </summary>
    public bool Clear()
    {
        lock (_gate)
        {
            try
            {
                foreach (var file in new[] { _path, _path + ".tmp" })
                {
                    if (File.Exists(file)) File.Delete(file);
                }

                return !File.Exists(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return !File.Exists(_path);
            }
        }
    }

    /// <summary>
    /// The prompt digest for <paramref name="characterId"/>/<paramref name="characterName"/>, at most
    /// <paramref name="maxChars"/> characters. See <see cref="RunSummary.FormatDigest"/> for the shape.
    /// </summary>
    public string Digest(string? characterId, string? characterName, int maxChars = RunSummary.MaxDigestChars)
    {
        return RunSummary.FormatDigest(Load(), characterId, characterName, maxChars);
    }

    private List<RunJournalEntry> LoadLocked()
    {
        try
        {
            if (!File.Exists(_path)) return new List<RunJournalEntry>();

            var entries = new List<RunJournalEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(_path, Utf8NoBom))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                RunJournalEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize<RunJournalEntry>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    // One torn or hand-mangled row must not cost the other forty-nine.
                    continue;
                }

                if (entry == null || string.IsNullOrWhiteSpace(entry.RunId)) continue;
                var normalized = Normalize(entry);
                // A file that was duplicated by hand (or written twice by an older build) still reads
                // as one row per run; the last copy wins, matching the append rule.
                if (!seen.Add(normalized.RunId!))
                {
                    entries.RemoveAll(existing => string.Equals(existing.RunId, normalized.RunId, StringComparison.Ordinal));
                }

                entries.Add(normalized);
            }

            return entries.Count <= MaxEntries ? entries : entries.TakeLast(MaxEntries).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new List<RunJournalEntry>();
        }
    }

    private void WriteAtomic(IReadOnlyList<RunJournalEntry> entries)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) System.IO.Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.Append(JsonSerializer.Serialize(entry, JsonOptions)).Append('\n');
        }

        var tempPath = _path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, builder.ToString(), Utf8NoBom);
            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    /// <summary>
    /// Redacts, clips and range-checks one row so what is on disk is what a reader can trust: the
    /// stored result is always one of the three known words, and no text field can carry a key.
    /// </summary>
    private static RunJournalEntry Normalize(RunJournalEntry entry)
    {
        var result = entry.Result switch
        {
            RunSummary.ResultVictory => RunSummary.ResultVictory,
            RunSummary.ResultDefeat => RunSummary.ResultDefeat,
            _ => RunSummary.ResultUnknown
        };

        return entry with
        {
            RunId = Text(entry.RunId, 64),
            Character = Text(entry.Character, 64),
            CharacterName = Text(entry.CharacterName, 64),
            Ascension = entry.Ascension is >= 0 and <= 20 ? entry.Ascension : null,
            Result = result,
            Floor = entry.Floor is >= 0 and <= 999 ? entry.Floor : null,
            Act = Text(entry.Act, 40),
            BossId = Text(entry.BossId, 64),
            DiedTo = NormalizeDiedTo(entry.DiedTo),
            HpAtEnd = entry.HpAtEnd is >= 0 and <= 9999 ? entry.HpAtEnd : null,
            DeckSize = entry.DeckSize is >= 0 and <= 999 ? entry.DeckSize : null,
            KeyRelics = (entry.KeyRelics ?? Array.Empty<string>())
                .Select(name => Text(name, 48))
                .Where(name => name != null)
                .TakeLast(RunSummary.MaxKeyRelics)
                .Select(name => name!)
                .ToArray(),
            Requests = Math.Max(0, entry.Requests),
            Tokens = entry.Tokens is >= 0 ? entry.Tokens : null,
            FinishedAt = Text(entry.FinishedAt, 40) ?? string.Empty
        };
    }

    private static RunDiedTo? NormalizeDiedTo(RunDiedTo? diedTo)
    {
        if (diedTo == null) return null;
        var id = Text(diedTo.EnemyId, 64);
        var name = Text(diedTo.EnemyName, 48);
        if (id == null && name == null) return null;
        return new RunDiedTo { EnemyId = id, EnemyName = name, Inferred = diedTo.Inferred };
    }

    private static string? Text(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var flat = DiagnosticExport.Redact(value)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (flat.Length == 0) return null;
        return flat.Length <= maxChars ? flat : flat[..maxChars];
    }
}
