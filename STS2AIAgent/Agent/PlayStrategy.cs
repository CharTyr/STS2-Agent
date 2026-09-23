using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>
/// The LLM planner's standing guidance for the fast execution model: a posture, free-text
/// instructions, and per-option hints Jev reads when it weighs the concrete options a screen offers.
/// </summary>
/// <remarks>
/// This is the whole output of the slow half of the two-layer engine. The planner never names a
/// specific card index or target -- those are stale the moment the game advances a frame -- it only
/// shapes how Jev chooses among the concrete options <see cref="JevOptionEnumerator"/> expands from the
/// live snapshot. That keeps the planner's value (strategy) and the executor's job (this exact click)
/// from depending on the same frame.
/// <para>
/// JSON is tolerant on the way in (<see cref="TryParse"/> keeps defaults for any missing field) and
/// faithful on the way out (<see cref="ToJson"/>), so a persisted or MCP-delivered strategy survives a
/// round trip. Snake_case keys match the rest of the agent wire conventions.
/// </para>
/// </remarks>
internal sealed record PlayStrategy
{
    /// <summary>Who is allowed to write this strategy: "default", "llm", or "mcp".</summary>
    public const string DefaultSource = "default";

    /// <summary>The <see cref="UpdatedAt"/> value a never-planned strategy carries.</summary>
    public const string DefaultTimestamp = "1970-01-01T00:00:00Z";

    /// <summary>
    /// Longest <see cref="Goal"/> the executor forwards. A planner that ignores "one short sentence"
    /// must not inflate every subsequent per-frame request.
    /// </summary>
    internal const int MaxGoalCharacters = 400;

    /// <summary>The overall bias: "balanced", "aggressive", or "defensive".</summary>
    public string Posture { get; init; } = "balanced";

    /// <summary>Free-text planner guidance Jev reads verbatim when it decides.</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>
    /// The macro objective the planner is pursuing on this screen or act, in one short sentence
    /// ("kill the weakest enemy before it buffs"). Empty means the planner stated none.
    /// </summary>
    /// <remarks>
    /// The per-frame options say what is legal; the goal says what the frame is FOR. It rides in front
    /// of the concrete choice so the execution model weighs the frame against a standing objective
    /// instead of re-deriving one from the hand it happens to be holding -- that re-derivation is what
    /// the live 2026-09-23 run shows as long, unsure prose followed by a low-confidence end_turn.
    /// </remarks>
    public string Goal { get; init; } = string.Empty;

    /// <summary>
    /// The screen this plan was written for, or empty when the plan did not record one.
    /// </summary>
    /// <remarks>
    /// The refresh key is run+screen+act, so a combat plan is reused by every combat in the act. The
    /// executor compares this field against the frame it is deciding, which is what makes carried-over
    /// guidance visible instead of silently assumed current.
    /// </remarks>
    public string PlanScreen { get; init; } = string.Empty;

    /// <summary>The combat round this plan was written in, or null outside combat.</summary>
    /// <remarks>
    /// A combat round counter restarts at 1 for each encounter, so a frame whose round is below this
    /// value proves the plan predates the current encounter. It is evidence, not a heuristic: nothing
    /// is inferred when this is null.
    /// </remarks>
    public int? PlanRound { get; init; }

    /// <summary>
    /// Per-option nudges. The planner writes them keyed by option KIND (the action name, e.g.
    /// <c>play_card</c>); <see cref="JevExecutionDecider"/> re-keys them onto the frame's concrete
    /// <see cref="JevOption.Id"/> values, and only those reach Jev. An empty map means no bias.
    /// </summary>
    public IReadOnlyDictionary<string, string> OptionHints { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>When the planner wrote this, as an ISO-8601 timestamp.</summary>
    public string UpdatedAt { get; init; } = DefaultTimestamp;

    /// <summary>Which half of the engine produced this strategy: "default", "llm", or "mcp".</summary>
    public string Source { get; init; } = DefaultSource;

    /// <summary>The strategy the store starts at, before any planner has run.</summary>
    public static PlayStrategy Default { get; } = new();

    /// <summary>
    /// Parse a strategy, keeping a default for every field the JSON omits or mis-types. Returns null
    /// when the text is not a JSON object at all, so a caller can distinguish "no strategy" from "a
    /// partial one".
    /// </summary>
    public static PlayStrategy? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            return new PlayStrategy
            {
                Posture = ReadString(root, "posture") ?? Default.Posture,
                Goal = ClampGoal(ReadString(root, "goal")),
                Instructions = ReadString(root, "instructions") ?? Default.Instructions,
                OptionHints = ReadOptionHints(root) ?? Default.OptionHints,
                PlanScreen = ReadString(root, "plan_screen") ?? Default.PlanScreen,
                PlanRound = ReadRound(root),
                UpdatedAt = ReadString(root, "updated_at") ?? Default.UpdatedAt,
                Source = ReadString(root, "source") ?? Default.Source
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The strategy as JSON, honoring the same keys <see cref="TryParse"/> reads.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            posture = Posture,
            goal = Goal,
            instructions = Instructions,
            option_hints = OptionHints,
            plan_screen = PlanScreen,
            plan_round = PlanRound,
            updated_at = UpdatedAt,
            source = Source
        });
    }

    /// <summary>
    /// The bounded form of a goal, for both the parser and a planner-delivered strategy that skipped
    /// it. Keeping the clamp here means a runaway sentence cannot inflate a per-frame request through
    /// any writer.
    /// </summary>
    internal static string ClampGoal(string? goal)
    {
        if (string.IsNullOrEmpty(goal))
        {
            return string.Empty;
        }

        if (goal.Length <= MaxGoalCharacters)
        {
            return goal;
        }

        // Never split a surrogate pair: a lone half is not valid text to put in a JSON request.
        var cut = MaxGoalCharacters;
        if (char.IsHighSurrogate(goal[cut - 1]))
        {
            cut--;
        }

        return goal[..cut];
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// The recorded combat round, or null when the plan carried none. A negative or non-integral value
    /// is treated as absent rather than clamped: a bad round must not manufacture staleness evidence.
    /// </summary>
    private static int? ReadRound(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("plan_round", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var round)
            && round >= 0
            ? round
            : null;
    }

    internal static IReadOnlyDictionary<string, string>? ReadOptionHints(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("option_hints", out var hints)
            || hints.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in hints.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                dictionary[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return dictionary;
    }
}

/// <summary>
/// An external planner's partial strategy update: the fields it named, and nothing else.
/// </summary>
/// <remarks>
/// <c>POST /strategy</c> and MCP <c>update_play_strategy</c> both promise that omitted fields keep their
/// current values. <see cref="PlayStrategy.TryParse"/> cannot express that -- it fills every omitted
/// field with its default -- so the HTTP route used to wipe instructions and hints on a posture-only
/// nudge, and the native MCP's hand-written merge dropped the macro goal. Both writers now read an
/// update and apply it here.
/// </remarks>
internal sealed record PlayStrategyUpdate(
    string? Posture,
    string? Goal,
    string? Instructions,
    IReadOnlyDictionary<string, string>? OptionHints)
{
    /// <summary>
    /// Reads the recognised, correctly typed fields of <paramref name="root"/>. Null when none is
    /// present (or the value is not an object): an empty update is a caller mistake, not a reset.
    /// </summary>
    public static PlayStrategyUpdate? TryRead(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var update = new PlayStrategyUpdate(
            ReadString(root, "posture"),
            ReadString(root, "goal"),
            ReadString(root, "instructions"),
            PlayStrategy.ReadOptionHints(root));
        return update.IsEmpty ? null : update;
    }

    /// <summary>True when the update names no field at all.</summary>
    public bool IsEmpty => Posture == null && Goal == null && Instructions == null && OptionHints == null;

    /// <summary>
    /// The strategy after this update: named fields replace, omitted fields keep <paramref name="current"/>.
    /// </summary>
    /// <remarks>
    /// The plan scope (<see cref="PlayStrategy.PlanScreen"/> / <see cref="PlayStrategy.PlanRound"/>) says
    /// which frame the guidance was written for. A posture nudge leaves the guidance as written, so the
    /// scope stays; rewriting the goal, instructions or hints makes it guidance from outside any recorded
    /// frame, so the scope is cleared rather than left claiming a frame it was not written for.
    /// </remarks>
    public PlayStrategy ApplyTo(PlayStrategy current, string source)
    {
        var rewritesGuidance = Goal != null || Instructions != null || OptionHints != null;
        return current with
        {
            Posture = Posture ?? current.Posture,
            Goal = Goal != null ? PlayStrategy.ClampGoal(Goal) : current.Goal,
            Instructions = Instructions ?? current.Instructions,
            OptionHints = OptionHints ?? current.OptionHints,
            PlanScreen = rewritesGuidance ? string.Empty : current.PlanScreen,
            PlanRound = rewritesGuidance ? null : current.PlanRound,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Source = source
        };
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
