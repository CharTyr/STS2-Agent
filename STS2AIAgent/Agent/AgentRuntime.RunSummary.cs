using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Game;
using STS2AIAgent.Llm;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The runtime's half of the run report: noticing a real GAME_OVER frame, turning it into a scorecard,
/// keeping the bounded cross-run journal, and handing the prompt a factual digest of it.
/// </summary>
/// <remarks>
/// The trigger is deliberately narrow. <c>CurrentRunBoundary</c> also ends a run when the player walks
/// back to the menu, when the run id changes and when an action leaves the run screen -- none of those
/// is a finished run, and a journal that recorded them would tell the player (and the model) that runs
/// were lost that never were. Only <see cref="RunSummary.GameOverScreen"/> observed in a fresh game
/// state may start a capture, and the capture is then re-verified against a state of its own before
/// anything is written.
///
/// Journal writes and the prompt digest are both gated on <c>runJournalEnabled</c>: off means nothing
/// is written to disk and nothing is injected into a prompt, while the in-memory scorecard still shows
/// (it is this run's own facts and is not persisted).
/// </remarks>
internal sealed partial class AgentRuntime
{
    /// <summary>How long the capture may wait for the game thread before giving the attempt up.</summary>
    private static readonly TimeSpan RunSummaryCaptureBudget = TimeSpan.FromSeconds(5);

    /// <summary>How many recorded runs the overlay reads for its "recent runs" line.</summary>
    internal const int RunJournalPreviewLimit = 5;

    private readonly RunJournalStore _runJournal = new();

    private RunSummary? _lastRunSummary;
    private string? _capturedRunId;
    private string? _runObservedRunId;
    private DateTimeOffset? _runObservedAt;
    private int _runSummaryCaptureInFlight;
    private string? _digestKey;
    private string? _digestText;

    /// <summary>
    /// The journal as last read, oldest first; null until first read. Invalidated by a write or a
    /// clear, so the overlay can ask for it on every repaint without touching the disk.
    /// </summary>
    private List<RunJournalEntry>? _journalListing;

    /// <summary>
    /// The scorecard of the last finished run, or null until one is observed. Retired as soon as a new
    /// run becomes the active session, so the card never describes a run the player has moved past.
    /// </summary>
    public RunSummary? LastRunSummary
    {
        get { lock (_gate) return _lastRunSummary; }
    }

    /// <summary>The recorder behind <see cref="LastRunSummary"/>; exposed for the settings clear action.</summary>
    public string RunJournalPath => _runJournal.Path;

    /// <summary>
    /// Finished runs, newest first, at most <paramref name="limit"/>. Served from the cached listing:
    /// the overlay asks on every runtime change, and that event is far too frequent for a file read.
    /// </summary>
    public IReadOnlyList<RunJournalEntry> RecentRunJournal(int limit = RunJournalPreviewLimit)
    {
        var take = Math.Max(1, limit);
        return JournalListing().TakeLast(take).Reverse().ToArray();
    }

    /// <summary>The journal oldest-first, read once and invalidated by every write or clear.</summary>
    private IReadOnlyList<RunJournalEntry> JournalListing()
    {
        List<RunJournalEntry>? cached;
        lock (_gate) cached = _journalListing;
        if (cached != null) return cached;

        var loaded = _runJournal.Load().ToList();
        lock (_gate) _journalListing = loaded;
        return loaded;
    }

    /// <summary>
    /// A new run became the active session (or the same run was confirmed again): stamp the clock the
    /// scorecard reports as "time watched" and retire the previous run's card.
    /// </summary>
    private void NoteRunObserved(string? runId)
    {
        if (!PlaySessionStore.IsPersistable(runId)) return;
        lock (_gate)
        {
            if (string.Equals(_runObservedRunId, runId, StringComparison.Ordinal)) return;
            _runObservedRunId = runId;
            // A run first seen mid-way still gets a truthful "watched for", because the label says so.
            _runObservedAt = DateTimeOffset.UtcNow;
            _lastRunSummary = null;
            _capturedRunId = null;
            _digestKey = null;
            _digestText = null;
        }
    }

    /// <summary>
    /// One fresh game frame said <c>GAME_OVER</c>. That is the only evidence accepted here; the rest of
    /// the work (a state of our own to read the facts from, then the journal write) happens off the
    /// caller's thread and off the game thread, deduped per run.
    /// </summary>
    private void NoteGameOverObserved(string? runId)
    {
        // One writer per settings file: the host window owns the overlay and the report; the teammate
        // process sees the same run and would append a second row for it.
        if (InstanceRole.IsCompanion) return;
        // The game-over frame can already have dropped the run state (its id then reads run_unknown),
        // while the boundary still remembers the seed this session entered.
        if (!PlaySessionStore.IsPersistable(runId)) runId = _runBoundary.RunId;
        if (!PlaySessionStore.IsPersistable(runId)) return;
        lock (_gate)
        {
            if (string.Equals(_capturedRunId, runId, StringComparison.Ordinal)) return;
        }

        if (Interlocked.CompareExchange(ref _runSummaryCaptureInFlight, 1, 0) != 0) return;
        _ = Task.Run(() => CaptureRunSummaryAsync(runId!));
    }

    private async Task CaptureRunSummaryAsync(string expectedRunId)
    {
        try
        {
            GameStatePayload? state;
            try
            {
                state = await GameThread
                    .InvokeAsync(GameStateService.BuildStatePayload, RunSummaryCaptureBudget, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or InvalidOperationException)
            {
                // The game thread is busy or not pumping. A journal write is not worth a stall, and a
                // later GAME_OVER observation may still succeed.
                NoteEvent("run summary skipped: " + ex.GetType().Name);
                return;
            }

            if (state == null || !string.Equals(state.screen, RunSummary.GameOverScreen, StringComparison.Ordinal))
            {
                return;
            }

            var facts = ReadGameOverFacts(state, expectedRunId);
            if (facts == null) return;
            var snapshot = _decisions.Snapshot(PlaySessionStore.MaxDecisions);
            var summary = RunSummary.Create(
                facts,
                CurrentRunCost(),
                snapshot,
                // A full window has already evicted this run's earliest rows, so the counters it still
                // holds are a lower bound and the card says "at least" rather than stating a total.
                countsAreLowerBounds: snapshot.Count >= PlaySessionStore.MaxDecisions);
            if (summary == null) return;
            ApplyRunSummary(summary);
        }
        catch (Exception ex)
        {
            // Best effort, exactly like the session files: a report must never be able to stop play.
            NoteEvent("run summary skipped: " + DiagnosticExport.Redact(ex.GetType().Name + ": " + ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _runSummaryCaptureInFlight, 0);
        }
    }

    /// <summary>
    /// The facts a GAME_OVER frame carries, or null when the frame is not that evidence.
    /// </summary>
    private static RunFacts? ReadGameOverFacts(GameStatePayload state, string? expectedRunId)
    {
        if (!string.Equals(state.screen, RunSummary.GameOverScreen, StringComparison.Ordinal)) return null;
        var gameOver = state.game_over;
        // The screen alone can be reached before the summary payload exists; without the victory flag
        // and floor there is no fact to store.
        if (gameOver == null) return null;

        var run = state.run;
        var runId = PlaySessionStore.IsPersistable(state.run_id) ? state.run_id : expectedRunId;
        if (!PlaySessionStore.IsPersistable(runId)) return null;

        return new RunFacts
        {
            RunId = runId,
            Screen = state.screen,
            IsVictory = gameOver.is_victory,
            CharacterId = run?.character_id ?? gameOver.character_id,
            CharacterName = run?.character_name,
            Ascension = run?.ascension,
            Floor = gameOver.floor ?? run?.floor,
            ActId = run?.act_id,
            BossId = run?.boss_id,
            CurrentHp = run?.current_hp,
            MaxHp = run?.max_hp,
            DeckSize = run?.deck?.Length,
            Relics = run?.relics?.Select(relic => relic.name).ToArray() ?? Array.Empty<string>(),
            // Only a defeat can have a killer, and only an unambiguous one is recorded.
            DiedTo = gameOver.is_victory ? null : ReadDiedTo(state.combat)
        };
    }

    /// <summary>
    /// The one living enemy left on the game-over frame, when there is exactly one. Two survivors are
    /// not evidence of who landed the last hit, so the field stays empty rather than guessing.
    /// </summary>
    private static RunDiedTo? ReadDiedTo(CombatPayload? combat)
    {
        if (combat?.enemies is not { Length: > 0 } enemies) return null;
        var alive = enemies.Where(enemy => enemy.is_alive).ToArray();
        if (alive.Length != 1) return null;
        var enemy = alive[0];
        return new RunDiedTo
        {
            EnemyId = string.IsNullOrWhiteSpace(enemy.enemy_id) ? null : enemy.enemy_id,
            EnemyName = string.IsNullOrWhiteSpace(enemy.name) ? null : enemy.name,
            Inferred = true
        };
    }

    private RunCost CurrentRunCost()
    {
        LlmUsage usage;
        bool usageKnown;
        string? turbo;
        DateTimeOffset? observedAt;
        lock (_gate)
        {
            usage = _sessionUsage;
            usageKnown = _sessionUsageKnown;
            turbo = _settings.TurboMode;
            observedAt = _runObservedAt;
        }

        var now = DateTimeOffset.UtcNow;
        return new RunCost
        {
            // Only the session-wide cache reading travels; spend is per-run and comes from the run's
            // own decision rows, never from these session totals.
            SessionPromptTokens = usageKnown ? usage.PromptTokens : null,
            SessionCachedPromptTokens = usageKnown ? usage.CachedPromptTokens : null,
            Turbo = turbo,
            Duration = observedAt is { } started && now > started ? now - started : null,
            FinishedAt = now
        };
    }

    private void ApplyRunSummary(RunSummary summary)
    {
        var runId = summary.Entry.RunId;
        if (RunJournalAllowed() && runId != null && _runJournal.Append(summary.Entry))
        {
            lock (_gate) _journalListing = null;
        }

        lock (_gate)
        {
            _lastRunSummary = summary;
            _capturedRunId = runId;
            _digestKey = null;
            _digestText = null;
        }

        NoteEvent("run finished: " + summary.OneLine());
        RaiseChanged();
    }

    /// <summary>
    /// The prompt's recent-runs digest, at most <paramref name="maxChars"/> characters, or the empty
    /// string when the journal is off or this character has no finished runs.
    /// </summary>
    /// <remarks>
    /// Cached per character and invalidated by a finished run or a clear, because the caller is the
    /// prompt builder: it asks on every decision, and the answer only changes once a run.
    /// </remarks>
    public string RunJournalDigest(string? characterId, string? characterName, int maxChars = RunSummary.MaxDigestChars)
    {
        if (!RunJournalAllowed() || maxChars <= 0) return string.Empty;
        var key = (characterId ?? string.Empty) + "\u0000" + (characterName ?? string.Empty) + "\u0000" + maxChars;
        lock (_gate)
        {
            if (_digestText != null && string.Equals(_digestKey, key, StringComparison.Ordinal)) return _digestText;
        }

        // Uses the cached listing: the prompt builder asks once per decision, and the listing only
        // changes when a run is recorded or the record is cleared.
        var text = RunSummary.FormatDigest(JournalListing(), characterId, characterName, maxChars);
        lock (_gate)
        {
            _digestKey = key;
            _digestText = text;
        }

        return text;
    }

    /// <summary>
    /// The digest for the character the compact state names, for the prompt builder that already holds
    /// that state. A state without a run, or without a character, asks the journal for its newest runs.
    /// </summary>
    public string RunJournalDigestForState(string? compactStateJson, int maxChars = RunSummary.MaxDigestChars)
    {
        return RunJournalDigest(null, ReadCompactCharacterName(compactStateJson), maxChars);
    }

    private static string? ReadCompactCharacterName(string? compactStateJson)
    {
        if (string.IsNullOrWhiteSpace(compactStateJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(compactStateJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("run", out var run) ||
                run.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return run.TryGetProperty("character", out var character) && character.ValueKind == JsonValueKind.String
                ? character.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the recorded runs and reports the outcome. The caller is a UI button; the message is
    /// already translated and also becomes the status line, like the other session tools.
    /// </summary>
    public bool TryClearRunJournal(out string message)
    {
        var cleared = _runJournal.Clear();
        lock (_gate)
        {
            _journalListing = null;
            _digestKey = null;
            _digestText = null;
        }

        message = cleared
            ? Loc.T("已清空战绩记录。")
            : Loc.T("清空战绩记录失败：文件可能被占用。");
        SetStatus(message);
        RaiseChanged();
        return cleared;
    }

    /// <summary>The additive <c>/health.last_run_summary</c> projection: facts only, null before one.</summary>
    internal object? BuildLastRunSummaryForHealth()
    {
        var summary = LastRunSummary;
        if (summary == null) return null;
        var entry = summary.Entry;
        return new
        {
            run_id = entry.RunId,
            result = entry.Result,
            character = entry.Character,
            character_name = entry.CharacterName,
            ascension = entry.Ascension,
            floor = entry.Floor,
            act = entry.Act,
            boss_id = entry.BossId,
            died_to = entry.DiedTo == null
                ? null
                : new
                {
                    enemy_id = entry.DiedTo.EnemyId,
                    enemy_name = entry.DiedTo.EnemyName,
                    inferred = entry.DiedTo.Inferred
                },
            hp_at_end = entry.HpAtEnd,
            deck_size = entry.DeckSize,
            key_relics = entry.KeyRelics,
            requests = entry.Requests,
            tokens = entry.Tokens,
            counts_are_lower_bounds = summary.CountsAreLowerBounds,
            finished_at = entry.FinishedAt,
            decisions = summary.Decisions,
            requests_per_floor = summary.RequestsPerFloor
        };
    }

    private bool RunJournalAllowed()
    {
        lock (_gate)
        {
            return _settings.RunJournalEnabled;
        }
    }
}
