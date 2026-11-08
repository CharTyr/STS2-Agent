using STS2AIAgent.Agent;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Tests;

/// <summary>
/// Offline tests for the end-of-run report (<see cref="RunSummary"/>) and the bounded cross-run journal
/// (<see cref="RunJournalStore"/>).
/// </summary>
/// <remarks>
/// The report may only be built from a GAME_OVER frame: leaving a run for the menu, a changed run id or
/// an abandoned run are not finishes, and the journal would lie to the player (and to the prompt) if it
/// recorded them. That rule is asserted twice here -- once on the pure builder (a non-GAME_OVER frame
/// yields no summary) and once on the runtime source, where the observation is wired.
///
/// The store's tests run against a fresh temp directory, like <see cref="PlaySessionStoreTests"/>: real
/// files, isolated, deleted afterwards.
/// </remarks>
internal static class RunSummaryTests
{
    public static void OnlyAGameOverFrameBecomesASummary()
    {
        Assert.True(RunSummary.Create(null, Cost(), Array.Empty<DecisionLogEntry>()) == null,
            "No evidence must not produce a report.");

        var notGameOver = Facts() with { Screen = "MAIN_MENU" };
        Assert.True(RunSummary.Create(notGameOver, Cost(), Array.Empty<DecisionLogEntry>()) == null,
            "Leaving for the menu is not a finished run.");

        var unknownRun = Facts() with { RunId = "run_unknown" };
        Assert.True(RunSummary.Create(unknownRun, Cost(), Array.Empty<DecisionLogEntry>()) == null,
            "The run_unknown placeholder is not a run and must not become one.");

        var blankRun = Facts() with { RunId = "  " };
        Assert.Null(RunSummary.Create(blankRun, Cost(), Array.Empty<DecisionLogEntry>()));

        var summary = RunSummary.Create(Facts(), Cost(), Array.Empty<DecisionLogEntry>());
        Assert.NotNull(summary);
        Assert.Equal("seed-game-over", summary!.Entry.RunId);
        Assert.True(summary.IsVictory);
    }

    public static void FactsTravelIntoTheStoredRow()
    {
        var facts = Facts() with
        {
            CharacterId = "ironclad",
            CharacterName = "Ironclad",
            Ascension = 10,
            Floor = 37,
            ActId = "act_2",
            BossId = "boss_guardian",
            CurrentHp = 21,
            MaxHp = 80,
            DeckSize = 24,
            Relics = new[] { "r1", "r2", "r3", "r4", "r5", "r6", "r7" },
            IsVictory = false,
            DiedTo = new RunDiedTo { EnemyId = "slaver", EnemyName = "Slaver", Inferred = true }
        };

        var summary = RunSummary.Create(facts, Cost(), Array.Empty<DecisionLogEntry>())!;
        var entry = summary.Entry;

        Assert.Equal("ironclad", entry.Character);
        Assert.Equal("Ironclad", entry.CharacterName);
        Assert.Equal(10, entry.Ascension);
        Assert.Equal(RunSummary.ResultDefeat, entry.Result);
        Assert.Equal(37, entry.Floor);
        Assert.Equal("act_2", entry.Act);
        Assert.Equal("boss_guardian", entry.BossId);
        Assert.Equal(21, entry.HpAtEnd);
        Assert.Equal(24, entry.DeckSize);
        // The frame lists relics in acquisition order; the five most recent ones travel.
        Assert.Equal(5, entry.KeyRelics.Length);
        Assert.Equal("r7", entry.KeyRelics[^1]);
        Assert.False(entry.KeyRelics.Contains("r1"));
        Assert.Equal("Slaver", entry.DiedTo!.EnemyName);
        Assert.True(entry.DiedTo.Inferred);
        Assert.True(DateTimeOffset.TryParse(entry.FinishedAt, out var finishedAt));
        Assert.Equal(TimeSpan.Zero, finishedAt.Offset);

        // A victory has nobody to have died to, whatever the frame's combat section said.
        var victory = RunSummary.Create(facts with { IsVictory = true }, Cost(), Array.Empty<DecisionLogEntry>())!;
        Assert.Null(victory.Entry.DiedTo);
        Assert.Equal(RunSummary.ResultVictory, victory.Entry.Result);

        // Spend is per-run and comes from this run's own rows: no rows means nothing recorded, and an
        // unknown token count stays null rather than reading as "spent nothing".
        Assert.Equal(0, entry.Requests);
        Assert.Null(entry.Tokens);
        var unknownTokens = RunSummary.Create(
            Facts(),
            Cost(),
            new[] { Decision(9, "agent_loop", "play_card", "seed-game-over", tokens: null) })!;
        Assert.Null(unknownTokens.Entry.Tokens);
        Assert.Equal(1, unknownTokens.Requests);
    }

    public static void DecisionsAreSplitBySourceAndRun()
    {
        var decisions = new[]
        {
            Decision(1, "agent_loop", "play_card", "seed-game-over", reason: "打最左边"),
            Decision(2, "autopilot", "end_turn", "seed-game-over", reason: "没有能打出的牌"),
            Decision(3, "agent_loop", "play_card", "seed-game-over", reason: "易伤优先", confidence: 0.82),
            Decision(4, "agent_loop", "play_card", "seed-game-over", reason: "计划第二步", planIndex: 1, planSize: 3),
            // Another run's row must not leak into this report.
            Decision(5, "agent_loop", "play_card", "seed-other", reason: "other run"),
            // An external client's action is not the AI's decision (live 2026-10-29: a debug command
            // was counted as "model" and picked as the highlight).
            Decision(6, "http_api", "run_console_command", "seed-game-over", reason: "debug", requests: 0, tokens: null),
            Decision(7, "native_mcp", "play_card", "seed-game-over", reason: "external", requests: 0, tokens: null)
        };

        var summary = RunSummary.Create(Facts(), Cost(), decisions)!;

        Assert.Equal(4, summary.Decisions);
        Assert.Equal(2, summary.ModelDecisions);
        Assert.Equal(1, summary.AutopilotDecisions);
        Assert.Equal(1, summary.JevDecisions);
        Assert.Equal(1, summary.PlanSteps);
        Assert.Equal(summary.Decisions, summary.ModelDecisions + summary.AutopilotDecisions + summary.JevDecisions);
        Assert.False(summary.Highlights.Any(line => line.Contains("debug") || line.Contains("external")),
            "an external client's action is never an AI highlight");

        // Spend is this run's own: the other run's row is neither counted nor paid for.
        Assert.Equal(4, summary.Requests);
        Assert.Equal(400, summary.Tokens);
        Assert.True(Math.Abs(summary.RequestsPerFloor!.Value - 4.0 / 12) < 0.001, "requests per floor");

        // A full decision window means earlier rows are already gone, so the card must say "at least".
        var bounded = RunSummary.Create(Facts(), Cost(), decisions, countsAreLowerBounds: true)!;
        Assert.True(bounded.CountsAreLowerBounds);
        Assert.Contains("≥4", string.Join("\n", bounded.Lines()));
    }

    public static void HighlightsArePickedByRuleFromRecordedDecisions()
    {
        var decisions = new[]
        {
            Decision(1, "agent_loop", "play_card", "seed-game-over", reason: "贪一刀", danger: 0.9),
            Decision(2, "agent_loop", "play_card", "seed-game-over", reason: "安全牌", danger: 0.1),
            Decision(3, "agent_loop", "play_card", "seed-game-over", reason: "三步计划", planIndex: 0, planSize: 3),
            Decision(4, "agent_loop", "end_turn", "seed-game-over", reason: "收尾")
        };

        var summary = RunSummary.Create(Facts(), Cost(), decisions)!;

        Assert.True(summary.Highlights.Count is > 0 and <= 3, "At most three highlights.");
        Assert.True(summary.Highlights.Any(line => line.Contains("贪一刀", StringComparison.Ordinal)),
            "The riskiest accepted action is a highlight.");
        Assert.True(summary.Highlights.Any(line => line.Contains("三步计划", StringComparison.Ordinal)),
            "The longest planned turn is a highlight.");
        Assert.True(summary.Highlights.Any(line => line.Contains("收尾", StringComparison.Ordinal)),
            "The last stated reason is a highlight.");
        Assert.Equal(summary.Highlights.Count, summary.Highlights.Distinct().Count());

        // No decisions, no highlights: the report never invents one.
        var empty = RunSummary.Create(Facts(), Cost(), Array.Empty<DecisionLogEntry>())!;
        Assert.Equal(0, empty.Highlights.Count);
    }

    public static void CardAndCopyTextCarryTheSameFacts()
    {
        // 40 requests and 12,000 tokens over 12 floors: the value the card prints is this run's own.
        var summary = RunSummary.Create(Facts(), Cost(), new[]
        {
            Decision(1, "agent_loop", "play_card", "seed-game-over",
                reason: "贪一刀", danger: 0.9, requests: 40, tokens: 12000)
        })!;

        var previous = Loc.Current;
        try
        {
            Loc.SetLanguage("zhs");
            var lines = summary.Lines();
            Assert.True(lines.Any(line => line.Contains("结果：胜利", StringComparison.Ordinal)), "result line");
            Assert.True(lines.Any(line => line.Contains("第 12 层", StringComparison.Ordinal)), "floor line");
            Assert.True(lines.Any(line => line.Contains("Ironclad", StringComparison.Ordinal)), "character line");
            Assert.True(lines.Any(line => line.Contains("请求：40 次", StringComparison.Ordinal)), "requests line");
            Assert.True(lines.Any(line => line.Contains("Token：12,000", StringComparison.Ordinal)), "token line");
            // The cache reading is labelled as the session's, not the run's.
            Assert.True(lines.Any(line => line.Contains("本次会话缓存命中：50%", StringComparison.Ordinal)), "cache line");
            Assert.True(lines.Any(line => line.Contains("极速", StringComparison.Ordinal)), "speed line");
            Assert.True(lines.Any(line => line.Contains("贪一刀", StringComparison.Ordinal)), "highlight line");

            var copy = summary.ToCopyText();
            Assert.True(copy.StartsWith("本轮成绩单", StringComparison.Ordinal), "a pasted report names itself");
            Assert.Contains("结果：胜利", copy);
            Assert.Equal(summary.ToCardText(), string.Join("\n", summary.Lines()));

            // English is looked up from the same keys, not frozen at construction.
            Loc.SetLanguage("eng");
            Assert.Contains("Result: Victory", summary.ToCardText());
            Assert.Contains("Floor 12", summary.ToCardText());
            Assert.Contains("Cache hit this session: 50%", summary.ToCardText());
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    public static void DigestIsBoundedFactsForOneCharacter()
    {
        var entries = Enumerable.Range(0, 8)
            .Select(index => new RunJournalEntry
            {
                RunId = "seed-" + index,
                Character = "ironclad",
                CharacterName = "Ironclad",
                Result = index % 2 == 0 ? RunSummary.ResultVictory : RunSummary.ResultDefeat,
                Floor = 40 + index,
                Ascension = 10,
                FinishedAt = "2026-10-" + (10 + index).ToString("00") + "T10:00:00Z"
            })
            .ToList();
        entries.Add(new RunJournalEntry
        {
            RunId = "seed-silent",
            Character = "silent",
            Result = RunSummary.ResultDefeat,
            Floor = 3,
            DiedTo = new RunDiedTo { EnemyId = "slaver", EnemyName = "Slaver", Inferred = true },
            FinishedAt = "2026-10-20T10:00:00Z"
        });

        var digest = RunSummary.FormatDigest(entries, "ironclad", "Ironclad");

        Assert.True(digest.Length <= RunSummary.MaxDigestChars, "the prompt budget is a hard cap");
        Assert.Contains("Finished runs with this character", digest);
        Assert.Equal(RunSummary.DigestRuns, digest.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
        // Newest first: the last appended same-character run comes before the older ones.
        Assert.True(digest.IndexOf("floor 47", StringComparison.Ordinal) <
                    digest.IndexOf("floor 43", StringComparison.Ordinal), "newest first");
        Assert.False(digest.Contains("silent", StringComparison.Ordinal), "another character's runs stay out");

        // Facts only: no instruction, no lesson.
        Assert.False(digest.Contains("should", StringComparison.OrdinalIgnoreCase));
        Assert.False(digest.Contains("advice on", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("died to Slaver (inferred)", RunSummary.FormatDigest(entries, "silent", null, 200));

        // An unknown character falls back to the newest runs overall.
        var overall = RunSummary.FormatDigest(entries, null, null);
        Assert.Contains("Finished runs (newest first", overall);

        // A known character with no finished runs injects nothing at all.
        Assert.Equal(string.Empty, RunSummary.FormatDigest(entries, "defect", "Defect"));

        // Any budget is honoured, including one smaller than a line.
        foreach (var budget in new[] { 1, 2, 8, 20, 40, 80, 160, 320, 600 })
        {
            var bounded = RunSummary.FormatDigest(entries, "ironclad", null, budget);
            Assert.True(bounded.Length <= budget, "budget " + budget + " was exceeded: " + bounded.Length);
        }

        Assert.Equal(string.Empty, RunSummary.FormatDigest(Array.Empty<RunJournalEntry>(), null, null));
        Assert.Equal(string.Empty, RunSummary.FormatDigest(entries, "ironclad", null, 0));
    }

    public static void DiedToIsOnlyRecordedWhenTheFrameMakesItUnambiguous()
    {
        // One death line in the digest, marked as the inference it is.
        var entries = new[]
        {
            new RunJournalEntry
            {
                RunId = "seed-died",
                Character = "ironclad",
                Result = RunSummary.ResultDefeat,
                Floor = 14,
                DiedTo = new RunDiedTo { EnemyId = "slaver", EnemyName = "Slaver", Inferred = true },
                FinishedAt = "2026-10-29T10:00:00Z"
            }
        };

        var digest = RunSummary.FormatDigest(entries, "ironclad", null);
        Assert.Contains("died to Slaver (inferred)", digest);
        Assert.Contains("floor 14", digest);
    }

    public static void JournalKeepsOneBoundedRowPerRun()
    {
        WithStore(store =>
        {
            Assert.True(store.Append(Entry("seed-a", floor: 3)));
            Assert.True(store.Append(Entry("seed-b", floor: 4)));
            Assert.Equal(2, store.Load().Count);

            // The same finished run observed twice is one row, not two.
            Assert.False(store.Append(Entry("seed-a", floor: 3)));
            Assert.Equal(2, store.Load().Count);

            // The cap keeps the newest and drops the oldest.
            for (var index = 0; index < RunJournalStore.MaxEntries + 5; index++)
            {
                Assert.True(store.Append(Entry("seed-cap-" + index, floor: index)));
            }

            var loaded = store.Load();
            Assert.Equal(RunJournalStore.MaxEntries, loaded.Count);
            Assert.Equal("seed-cap-" + (RunJournalStore.MaxEntries + 4), loaded[^1].RunId);
            Assert.False(loaded.Any(entry => entry.RunId == "seed-cap-0"), "the oldest row fell off the front");

            // One JSON row per line, and no temp file left behind by the atomic rewrite.
            var lines = File.ReadAllLines(store.Path).Where(line => line.Length > 0).ToArray();
            Assert.Equal(RunJournalStore.MaxEntries, lines.Length);
            Assert.False(File.Exists(store.Path + ".tmp"), "the atomic rewrite must clean up its temp file");

            Assert.True(store.Clear());
            Assert.Equal(0, store.Load().Count);
            Assert.False(File.Exists(store.Path));
            // Clearing again is not an error, and the store is usable afterwards.
            Assert.True(store.Clear());
            Assert.True(store.Append(Entry("seed-after-clear", floor: 1)));
            Assert.Equal(1, store.Load().Count);
        });
    }

    public static void JournalRowsAreRedactedAndRepairedOnRead()
    {
        WithStore(store =>
        {
            const string secret = "sk-abcdefghijklmnopqrstuvwxyz0123456789";
            Assert.True(store.Append(Entry("seed-secret", floor: 9) with
            {
                Character = secret,
                DiedTo = new RunDiedTo { EnemyName = "Bearer " + secret, Inferred = true }
            }));

            var raw = File.ReadAllText(store.Path);
            Assert.False(raw.Contains(secret, StringComparison.Ordinal), "a key must never reach the journal file");

            var loaded = store.Load().Single();
            Assert.False((loaded.Character ?? string.Empty).Contains(secret, StringComparison.Ordinal));
            Assert.False((loaded.DiedTo?.EnemyName ?? string.Empty).Contains(secret, StringComparison.Ordinal));

            // A torn line, an unknown result and a duplicate run id are repaired on read rather than
            // failing the whole journal.
            var path = store.Path;
            var good = File.ReadAllLines(path).Last();
            File.WriteAllLines(path, new[]
            {
                "not json at all",
                good.Replace("\"seed-secret\"", "\"seed-weird\"", StringComparison.Ordinal)
                    .Replace("\"defeat\"", "\"something-else\"", StringComparison.Ordinal),
                good
            });

            var repaired = store.Load();
            Assert.Equal(2, repaired.Count);
            Assert.Equal(RunSummary.ResultUnknown, repaired[0].Result);
            Assert.Equal("seed-secret", repaired[1].RunId);
        });
    }

    public static void StoreDigestReadsTheJournal()
    {
        WithStore(store =>
        {
            store.Append(Entry("seed-1", floor: 11) with { Character = "ironclad", CharacterName = "Ironclad", Result = RunSummary.ResultDefeat });
            store.Append(Entry("seed-2", floor: 12) with { Character = "ironclad", CharacterName = "Ironclad", Result = RunSummary.ResultVictory });

            var digest = store.Digest("ironclad", "Ironclad");
            Assert.Contains("floor 12", digest);
            Assert.Contains("floor 11", digest);
            Assert.Equal(string.Empty, store.Digest("silent", "Silent"));
            Assert.True(store.Digest("ironclad", null, 25).Length <= 25);
        });
    }

    public static void RuntimeOnlyRecordsAConfirmedGameOver()
    {
        var session = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.Session.cs");
        var observe = AgentSourceFixture.MethodBody(session, "ObserveSessionStateSnapshot");
        var guard = observe.IndexOf("string.Equals(screen, RunSummary.GameOverScreen", StringComparison.Ordinal);
        var call = observe.IndexOf("NoteGameOverObserved(runId)", StringComparison.Ordinal);
        Assert.True(guard >= 0, "the observation must name the GAME_OVER screen");
        Assert.True(call > guard, "the record call must sit inside the GAME_OVER guard, not on any run exit");

        var capture = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.RunSummary.cs");
        var read = AgentSourceFixture.MethodBody(capture, "ReadGameOverFacts");
        Assert.Contains("RunSummary.GameOverScreen", read);
        Assert.Contains("state.game_over", read);

        var apply = AgentSourceFixture.MethodBody(capture, "ApplyRunSummary");
        Assert.True(apply.Contains("RunJournalAllowed()", StringComparison.Ordinal),
            "nothing may be written to the journal while the switch is off");
    }

    private static RunFacts Facts() => new()
    {
        RunId = "seed-game-over",
        Screen = RunSummary.GameOverScreen,
        IsVictory = true,
        CharacterId = "ironclad",
        CharacterName = "Ironclad",
        Ascension = 5,
        Floor = 12,
        ActId = "act_1",
        BossId = "boss_hexaghost",
        CurrentHp = 33,
        MaxHp = 80,
        DeckSize = 18,
        Relics = new[] { "burning_blood" }
    };

    private static RunCost Cost() => new()
    {
        SessionPromptTokens = 10000,
        SessionCachedPromptTokens = 5000,
        Turbo = "instant",
        Duration = TimeSpan.FromSeconds(754),
        FinishedAt = DateTimeOffset.Parse("2026-10-29T01:02:03Z")
    };

    private static DecisionLogEntry Decision(
        long id,
        string source,
        string action,
        string runId,
        string? reason = null,
        double? danger = null,
        double? confidence = null,
        int? planIndex = null,
        int? planSize = null,
        int requests = 1,
        int? tokens = 100)
    {
        return new DecisionLogEntry(
            id,
            "2026-10-29T01:00:0" + id + "Z",
            source,
            action,
            reason,
            null,
            requests,
            tokens,
            runId,
            confidence,
            null,
            null,
            danger,
            null,
            null,
            null,
            null,
            null,
            null,
            planIndex,
            planSize);
    }

    private static RunJournalEntry Entry(string runId, int floor) => new()
    {
        RunId = runId,
        Character = "ironclad",
        CharacterName = "Ironclad",
        Ascension = 5,
        Result = RunSummary.ResultDefeat,
        Floor = floor,
        Act = "act_1",
        BossId = "boss_hexaghost",
        DiedTo = new RunDiedTo { EnemyId = "slaver", EnemyName = "Slaver", Inferred = true },
        HpAtEnd = 0,
        DeckSize = 18,
        KeyRelics = new[] { "burning_blood" },
        Requests = 40,
        Tokens = 12000,
        FinishedAt = "2026-10-29T01:02:03Z"
    };

    /// <summary>Runs <paramref name="body"/> against a journal in a fresh temp dir, then cleans up.</summary>
    private static void WithStore(Action<RunJournalStore> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sts2-journal-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            body(new RunJournalStore(Path.Combine(directory, "journal.jsonl")));
        }
        finally
        {
            try
            {
                if (System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
