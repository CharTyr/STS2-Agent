using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// The facts of a finished run exactly as its GAME_OVER frame showed them.
/// </summary>
/// <remarks>
/// <see cref="Screen"/> is part of the evidence, not decoration. The run boundary also ends when the
/// player walks back to the menu or the run id changes, and neither of those is a finished run: a run
/// that was abandoned, crashed out of, or simply paused for the evening has no score. Only a frame
/// that actually says <c>GAME_OVER</c> may be turned into a summary or written to the journal.
///
/// Everything here is a fact read off that one frame. A field the frame did not carry stays null, so
/// "the frame did not say" and "the frame said zero" never collapse into one answer.
/// </remarks>
internal sealed record RunFacts
{
    public string? RunId { get; init; }

    public string? Screen { get; init; }

    public bool IsVictory { get; init; }

    public string? CharacterId { get; init; }

    public string? CharacterName { get; init; }

    public int? Ascension { get; init; }

    public int? Floor { get; init; }

    public string? ActId { get; init; }

    public string? BossId { get; init; }

    public int? CurrentHp { get; init; }

    public int? MaxHp { get; init; }

    public int? DeckSize { get; init; }

    /// <summary>Relic names in acquisition order, as the frame listed them.</summary>
    public string[] Relics { get; init; } = Array.Empty<string>();

    public RunDiedTo? DiedTo { get; init; }

    /// <summary>Only a GAME_OVER frame is evidence that a run finished.</summary>
    public bool IsGameOver => string.Equals(Screen, RunSummary.GameOverScreen, StringComparison.Ordinal);
}

/// <summary>
/// Who the run ended against, when the game-over frame still showed the fight.
/// </summary>
/// <remarks>
/// This is the one field that is an inference rather than a reading: a frame that shows a dead player
/// and one living enemy is strong evidence, but it is not a field the game reports as "the killer".
/// <see cref="Inferred"/> travels with the value so a reader (and the prompt) can weight it, and the
/// record is only built when the frame makes the answer unambiguous.
/// </remarks>
internal sealed record RunDiedTo
{
    [JsonPropertyName("enemy_id")]
    public string? EnemyId { get; init; }

    [JsonPropertyName("enemy_name")]
    public string? EnemyName { get; init; }

    [JsonPropertyName("inferred")]
    public bool Inferred { get; init; } = true;
}

/// <summary>
/// What the report needs from the session: the game-speed switch, how long this window watched the
/// run, and the session-wide cache reading that is shown with its own label.
/// </summary>
/// <remarks>
/// Spend (requests and tokens) is deliberately absent. It is a per-run fact and comes from that run's
/// own decision rows -- see <see cref="RunSummary.Create"/> -- because a session can hold several runs
/// and a session total presented as "this run" would be a lie about the run it describes.
/// </remarks>
internal sealed record RunCost
{
    /// <summary>
    /// Session-wide prompt tokens, for the explicitly-labelled cache reading only. This is not a
    /// per-run fact: the provider reports cache hits per request, and the runtime does not keep a
    /// per-run prompt/cache split, so the card says "this session" beside it rather than pretending
    /// the number belongs to the run.
    /// </summary>
    public int? SessionPromptTokens { get; init; }

    public int? SessionCachedPromptTokens { get; init; }

    /// <summary>The game-speed switch that was in force while the AI played.</summary>
    public string? Turbo { get; init; }

    /// <summary>
    /// How long this window watched the run, or null when the run was already under way when it was
    /// first observed. The card labels it as watched time for that reason.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    public DateTimeOffset FinishedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One finished run, as the facts a later reader (the player, the journal, the prompt) needs.
/// </summary>
/// <remarks>
/// This is the row the bounded journal stores, one per run. It deliberately carries no advice: the
/// SpireBench result the design cites is that a model writing its own "lessons" is wrong about one in
/// six of them and turns cautious, so the record is facts and the reader draws its own conclusion.
/// </remarks>
internal sealed record RunJournalEntry
{
    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    [JsonPropertyName("character")]
    public string? Character { get; init; }

    [JsonPropertyName("character_name")]
    public string? CharacterName { get; init; }

    [JsonPropertyName("ascension")]
    public int? Ascension { get; init; }

    [JsonPropertyName("result")]
    public string Result { get; init; } = RunSummary.ResultUnknown;

    [JsonPropertyName("floor")]
    public int? Floor { get; init; }

    [JsonPropertyName("act")]
    public string? Act { get; init; }

    [JsonPropertyName("boss_id")]
    public string? BossId { get; init; }

    [JsonPropertyName("died_to")]
    public RunDiedTo? DiedTo { get; init; }

    [JsonPropertyName("hp_at_end")]
    public int? HpAtEnd { get; init; }

    [JsonPropertyName("deck_size")]
    public int? DeckSize { get; init; }

    [JsonPropertyName("key_relics")]
    public string[] KeyRelics { get; init; } = Array.Empty<string>();

    [JsonPropertyName("requests")]
    public int Requests { get; init; }

    /// <summary>Null when no model reported usage: "unknown" must not read as "nothing spent".</summary>
    [JsonPropertyName("tokens")]
    public long? Tokens { get; init; }

    [JsonPropertyName("finished_at")]
    public string FinishedAt { get; init; } = string.Empty;
}

/// <summary>
/// The end-of-run scorecard: the journal row plus what the session spent to get there.
/// </summary>
/// <remarks>
/// Built from <see cref="RunFacts"/> (a GAME_OVER frame), the decisions the log still holds for that
/// run, and the session's own counters -- never from the model's prose. The three "highlights" are
/// therefore chosen by rule from recorded decisions (the riskiest, the longest planned turn, the last
/// action with a stated reason), not written by a model, and each one is redacted text the decision log
/// already stored.
///
/// The text methods are on the record rather than in the overlay so the whole card can be asserted
/// offline, including the copy text a player pastes into a chat window.
/// </remarks>
internal sealed record RunSummary
{
    public const string GameOverScreen = "GAME_OVER";

    public const string ResultVictory = "victory";

    public const string ResultDefeat = "defeat";

    public const string ResultUnknown = "unknown";

    /// <summary>The decision-log source a forced move (no model request) names itself with.</summary>
    public const string AutopilotSource = "autopilot";

    /// <summary>Decision-log sources for actions an external client sent, not the AI.</summary>
    internal static readonly HashSet<string> ExternalSources = new(StringComparer.Ordinal) { "http_api", "native_mcp" };

    /// <summary>How many finished runs the prompt digest may mention.</summary>
    public const int DigestRuns = 5;

    /// <summary>The hard cap on the prompt digest, in characters.</summary>
    public const int MaxDigestChars = 600;

    /// <summary>How many relics travel with the journal row.</summary>
    public const int MaxKeyRelics = 5;

    /// <summary>The stored fact row. The scorecard adds only the session's own counters.</summary>
    public required RunJournalEntry Entry { get; init; }

    public int Decisions { get; init; }

    public int ModelDecisions { get; init; }

    public int AutopilotDecisions { get; init; }

    public int JevDecisions { get; init; }

    /// <summary>Steps that belonged to a whole-turn plan (a subset of <see cref="ModelDecisions"/>).</summary>
    public int PlanSteps { get; init; }

    /// <summary>Requests this run spent, summed from its own recorded decisions.</summary>
    public int Requests { get; init; }

    /// <summary>Tokens this run spent, or null when no row for it reported usage.</summary>
    public long? Tokens { get; init; }

    /// <summary>
    /// True when the decision window was full, so the run may have spent more than the rows still in
    /// it can prove. The card then prints the counters as lower bounds.
    /// </summary>
    public bool CountsAreLowerBounds { get; init; }

    /// <summary>Session-wide cache reading for the labelled line; null when never reported.</summary>
    public int? SessionCacheHitPercent { get; init; }

    public string? Turbo { get; init; }

    /// <summary>How long this window watched the run; the card labels it as watched time.</summary>
    public TimeSpan? Duration { get; init; }

    public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();

    public bool IsVictory => Entry.Result == ResultVictory;

    /// <summary>Requests per floor, or null when the frame did not say which floor it ended on.</summary>
    public double? RequestsPerFloor => Entry.Floor is > 0 ? Requests / (double)Entry.Floor.Value : null;

    /// <summary>
    /// Turns one GAME_OVER frame into a scorecard, or null when the frame is not that evidence.
    /// </summary>
    /// <remarks>
    /// Spend is taken from the run's own decision rows, never from the session totals: one session can
    /// cover several runs, and a session total stamped on one run's report would describe the wrong
    /// thing. <paramref name="countsAreLowerBounds"/> says the caller's window was full, in which case
    /// the counters are printed as "at least".
    /// </remarks>
    public static RunSummary? Create(
        RunFacts? facts,
        RunCost cost,
        IReadOnlyList<DecisionLogEntry>? decisions,
        bool countsAreLowerBounds = false)
    {
        if (facts == null || !facts.IsGameOver) return null;
        // `run_unknown` is the mod's placeholder for "no run identified yet", not a run: a journal row
        // keyed on it would collect every unidentified finish into one bucket.
        if (string.IsNullOrWhiteSpace(facts.RunId) ||
            string.Equals(facts.RunId, "run_unknown", StringComparison.Ordinal)) return null;

        // The report is about the AI's own play. An action an external client sent over HTTP/MCP is not
        // an AI decision: counting it as "model" (and picking a debug command as a highlight) was what
        // the first live report showed (2026-10-29).
        var runDecisions = (decisions ?? Array.Empty<DecisionLogEntry>())
            .Where(entry => entry != null && string.Equals(entry.run_id, facts.RunId, StringComparison.Ordinal)
                && !ExternalSources.Contains(entry.source))
            .OrderBy(entry => entry.id)
            .ToList();

        // A whole-turn plan records its cost on the first step only, so a plain sum is the run's real
        // spend rather than one row's worth per step.
        var requests = runDecisions.Sum(decision => Math.Max(0, decision.requests_spent));
        long tokens = 0;
        var tokensKnown = false;
        foreach (var decision in runDecisions)
        {
            if (decision.total_tokens is not { } rowTokens) continue;
            tokens += Math.Max(0, rowTokens);
            tokensKnown = true;
        }

        var entry = new RunJournalEntry
        {
            RunId = facts.RunId,
            Character = Clip(facts.CharacterId, 64),
            CharacterName = Clip(facts.CharacterName, 64),
            Ascension = facts.Ascension is { } ascension and >= 0 ? ascension : null,
            Result = facts.IsVictory ? ResultVictory : ResultDefeat,
            Floor = facts.Floor,
            Act = Clip(facts.ActId, 40),
            BossId = Clip(facts.BossId, 64),
            DiedTo = facts.IsVictory ? null : facts.DiedTo,
            HpAtEnd = facts.CurrentHp,
            DeckSize = facts.DeckSize,
            // The frame lists relics in acquisition order, so the tail is the most recent ones -- the
            // boss, elite and shop relics that shaped the run -- without inventing a rarity ranking the
            // payload does not carry.
            KeyRelics = facts.Relics
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .TakeLast(MaxKeyRelics)
                .Select(name => Clip(name, 48)!)
                .ToArray(),
            Requests = requests,
            Tokens = tokensKnown ? tokens : null,
            FinishedAt = cost.FinishedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        };

        var jev = runDecisions.Count(decision => decision.confidence != null);
        var autopilot = runDecisions.Count(decision =>
            string.Equals(decision.source, AutopilotSource, StringComparison.Ordinal));
        return new RunSummary
        {
            Entry = entry,
            Decisions = runDecisions.Count,
            // The three sources are disjoint by construction: a forced move names itself, a Jev move
            // carries a confidence, and everything left came back from the model.
            ModelDecisions = Math.Max(0, runDecisions.Count - jev - autopilot),
            AutopilotDecisions = autopilot,
            JevDecisions = jev,
            PlanSteps = runDecisions.Count(decision => decision.plan_index != null),
            Requests = entry.Requests,
            Tokens = entry.Tokens,
            CountsAreLowerBounds = countsAreLowerBounds,
            SessionCacheHitPercent = cost.SessionPromptTokens is > 0 && cost.SessionCachedPromptTokens is { } cached
                ? (int)Math.Round(Math.Clamp(cached / (double)cost.SessionPromptTokens.Value, 0, 1) * 100)
                : null,
            Turbo = Clip(cost.Turbo, 16),
            Duration = cost.Duration,
            Highlights = PickHighlights(runDecisions)
        };
    }

    /// <summary>Result, floor, character, what killed it -- the one-line form used in lists.</summary>
    public string OneLine()
    {
        var parts = new List<string> { ResultLabel(), FloorLabel() };
        if (!string.IsNullOrWhiteSpace(Entry.CharacterName ?? Entry.Character))
        {
            parts.Add(CharacterLabel());
        }

        if (Entry.DiedTo is { } diedTo)
        {
            parts.Add(Loc.T("倒在 {0}", DiedToName(diedTo)));
        }

        return string.Join(" · ", parts);
    }

    public string ResultLabel() => Entry.Result switch
    {
        ResultVictory => Loc.T("胜利"),
        ResultDefeat => Loc.T("失败"),
        _ => Loc.T("结果未知")
    };

    public string CharacterLabel()
    {
        var name = Clip(Entry.CharacterName, 64) ?? Clip(Entry.Character, 64);
        if (string.IsNullOrWhiteSpace(name)) return Loc.T("未知角色");
        return Entry.Ascension is > 0 ? Loc.T("{0}（难度 {1}）", name, Entry.Ascension) : name;
    }

    public string FloorLabel() => Entry.Floor is { } floor ? Loc.T("第 {0} 层", floor) : Loc.T("层数未知");

    /// <summary>The card's lines, in reading order. Empty parts are left out rather than blanked.</summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>
        {
            Loc.T("结果：{0}", ResultLabel())
        };

        var where = FloorLabel();
        if (!string.IsNullOrWhiteSpace(Entry.Act)) where += " · " + Loc.T("幕 {0}", Entry.Act);
        if (!string.IsNullOrWhiteSpace(Entry.BossId)) where += " · " + Loc.T("BOSS {0}", Entry.BossId);
        lines.Add(where);

        if (!string.IsNullOrWhiteSpace(Entry.CharacterName ?? Entry.Character))
        {
            lines.Add(Loc.T("角色：{0}", CharacterLabel()));
        }

        if (Duration is { } duration)
        {
            // Watched time, not "time to clear": the mod may have been started mid-run.
            lines.Add(Loc.T("本窗口观察用时：{0}", FormatDuration(duration)));
        }

        if (Entry.DiedTo is { } diedTo)
        {
            lines.Add(Loc.T("倒在：{0}", DiedToName(diedTo)));
        }

        lines.Add(Loc.T("决策：{0} 条（模型 {1} · 自动处理 {2} · 快速判断 {3}，其中整回合规划 {4} 步）",
            Decisions, ModelDecisions, AutopilotDecisions, JevDecisions, PlanSteps));

        // Per-run spend, from this run's own decision rows. A full window means earlier rows are gone,
        // so the numbers are printed as lower bounds instead of as a total they cannot prove.
        var requestsPrefix = CountsAreLowerBounds ? "≥" : string.Empty;
        lines.Add(RequestsPerFloor is { } perFloor
            ? Loc.T("请求：{0}{1} 次 · 每层约 {2} 次",
                requestsPrefix, Requests, perFloor.ToString("0.#", CultureInfo.InvariantCulture))
            : Loc.T("请求：{0}{1} 次", requestsPrefix, Requests));

        lines.Add(Tokens is { } tokens
            ? Loc.T("Token：{0}{1}", requestsPrefix, tokens.ToString("N0", CultureInfo.InvariantCulture))
            : Loc.T("Token：未知"));

        // A session-wide reading, kept apart from the run's own token count and labelled as such: the
        // provider reports cache hits per request and no per-run split exists to claim.
        if (SessionCacheHitPercent is { } hit)
        {
            lines.Add(Loc.T("本次会话缓存命中：{0}%", hit));
        }

        if (!string.IsNullOrWhiteSpace(Turbo))
        {
            lines.Add(Loc.T("游戏速度：{0}", TurboLabel()));
        }

        foreach (var highlight in Highlights)
        {
            lines.Add(Loc.T("高光：{0}", highlight));
        }

        return lines;
    }

    /// <summary>The card body: one fact per line.</summary>
    public string ToCardText() => string.Join("\n", Lines());

    /// <summary>
    /// The text the copy button puts on the clipboard. It starts with the title so a pasted report is
    /// self-describing, and carries the same facts as the card -- nothing extra.
    /// </summary>
    public string ToCopyText()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Loc.T("本轮成绩单"));
        foreach (var line in Lines())
        {
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    public string TurboLabel() => Turbo switch
    {
        null or "" => Loc.T("未记录"),
        "off" => Loc.T("关闭"),
        "fast" => Loc.T("快速"),
        "instant" => Loc.T("极速"),
        _ => Turbo!
    };

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        var totalSeconds = (long)Math.Round(duration.TotalSeconds);
        return totalSeconds >= 60
            ? Loc.T("{0} 分 {1} 秒", totalSeconds / 60, totalSeconds % 60)
            : Loc.T("{0} 秒", totalSeconds);
    }

    private static string DiedToName(RunDiedTo diedTo)
    {
        var name = Clip(diedTo.EnemyName, 64) ?? Clip(diedTo.EnemyId, 64);
        if (string.IsNullOrWhiteSpace(name)) return Loc.T("未知敌人");
        return diedTo.Inferred ? Loc.T("{0}（推断）", name) : name;
    }

    /// <summary>
    /// Up to three recorded reasons worth reading again, chosen by rule: the riskiest accepted move,
    /// the longest planned turn, and the last action that stated a reason. Duplicates collapse, and a
    /// run with fewer distinct decisions simply gets fewer lines.
    /// </summary>
    private static IReadOnlyList<string> PickHighlights(IReadOnlyList<DecisionLogEntry> runDecisions)
    {
        var picked = new List<string>(3);

        void Add(DecisionLogEntry? decision)
        {
            if (decision == null || picked.Count >= 3) return;
            var line = DescribeDecision(decision);
            if (line.Length == 0 || picked.Contains(line, StringComparer.Ordinal)) return;
            picked.Add(line);
        }

        Add(runDecisions
            .Where(decision => decision.danger != null)
            .OrderByDescending(decision => decision.danger)
            .ThenByDescending(decision => decision.id)
            .FirstOrDefault());
        Add(runDecisions
            .Where(decision => decision.plan_size != null)
            .OrderByDescending(decision => decision.plan_size)
            .ThenByDescending(decision => decision.id)
            .FirstOrDefault());
        Add(runDecisions.LastOrDefault(decision => !string.IsNullOrWhiteSpace(decision.reason)));
        for (var index = runDecisions.Count - 1; index >= 0 && picked.Count < 3; index--)
        {
            Add(runDecisions[index]);
        }

        return picked;
    }

    private static string DescribeDecision(DecisionLogEntry decision)
    {
        var action = Clip(PlayerLabels.Action(decision.action), 48) ?? string.Empty;
        var reason = Clip(decision.reason, 80);
        if (action.Length == 0) return reason ?? string.Empty;
        return string.IsNullOrWhiteSpace(reason) ? action : action + " — " + reason;
    }

    /// <summary>
    /// The prompt's "recent runs" digest: English facts about this character's last few finished runs,
    /// newest first, capped at <paramref name="maxChars"/> characters.
    /// </summary>
    /// <remarks>
    /// English on purpose: it rides the play prompt, whose static prefix is English. Facts only -- the
    /// model is told floors and deaths, never what to conclude from them. An unknown character (no
    /// character in the state yet) falls back to the newest runs overall; a known character with no
    /// finished runs yields the empty string, so nothing is injected at all.
    /// </remarks>
    public static string FormatDigest(
        IReadOnlyList<RunJournalEntry>? entries,
        string? characterId,
        string? characterName,
        int maxChars = MaxDigestChars)
    {
        if (entries == null || entries.Count == 0 || maxChars <= 0) return string.Empty;

        var knownCharacter = !string.IsNullOrWhiteSpace(characterId) || !string.IsNullOrWhiteSpace(characterName);
        var matches = entries
            .Where(entry => entry != null && (!knownCharacter || MatchesCharacter(entry, characterId, characterName)))
            .TakeLast(DigestRuns)
            .Reverse()
            .ToList();
        if (matches.Count == 0) return string.Empty;

        var header = knownCharacter
            ? "Finished runs with this character (newest first; facts only, no advice):"
            : "Finished runs (newest first; facts only, no advice):";
        var builder = new StringBuilder(header);
        foreach (var entry in matches)
        {
            var line = "\n- " + DescribeFactLine(entry);
            if (builder.Length + line.Length > maxChars) break;
            builder.Append(line);
        }

        var text = builder.ToString();
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static string DescribeFactLine(RunJournalEntry entry)
    {
        var parts = new List<string>
        {
            entry.Result switch
            {
                ResultVictory => "victory",
                ResultDefeat => "defeat",
                _ => "result unknown"
            }
        };

        if (entry.Floor is { } floor) parts.Add("floor " + floor.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(entry.Act)) parts.Add("act " + entry.Act);
        if (entry.Ascension is > 0) parts.Add("ascension " + entry.Ascension.Value.ToString(CultureInfo.InvariantCulture));
        if (entry.DiedTo is { } diedTo)
        {
            var killedBy = Clip(diedTo.EnemyName, 48) ?? Clip(diedTo.EnemyId, 48);
            if (!string.IsNullOrWhiteSpace(killedBy))
            {
                parts.Add("died to " + killedBy + (diedTo.Inferred ? " (inferred)" : string.Empty));
            }
        }
        else if (!string.IsNullOrWhiteSpace(entry.BossId))
        {
            parts.Add("boss " + entry.BossId);
        }

        if (!string.IsNullOrWhiteSpace(entry.FinishedAt) && entry.FinishedAt.Length >= 10)
        {
            parts.Add("on " + entry.FinishedAt[..10]);
        }

        return string.Join(" · ", parts);
    }

    private static bool MatchesCharacter(RunJournalEntry entry, string? characterId, string? characterName)
    {
        return Matches(entry.Character, characterId) || Matches(entry.CharacterName, characterName);

        static bool Matches(string? left, string? right) =>
            !string.IsNullOrWhiteSpace(left) &&
            !string.IsNullOrWhiteSpace(right) &&
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One-line, whitespace-flattened, length-capped text; null for blank input.</summary>
    internal static string? Clip(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var flat = text.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return flat.Length <= maxChars ? flat : flat[..maxChars] + "…";
    }
}
