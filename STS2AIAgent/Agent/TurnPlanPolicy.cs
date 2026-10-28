using System.Text.Json;

namespace STS2AIAgent.Agent;

/// <summary>Why a whole-turn plan stopped. The same strings on both MCP surfaces and in the game.</summary>
internal static class TurnPlanStopReasons
{
    public const string Completed = "completed";
    public const string CardGone = "card_gone";
    public const string CardUnplayable = "card_unplayable";
    public const string TargetGone = "target_gone";
    public const string IllegalAction = "illegal_action";
    public const string ScreenChanged = "screen_changed";
    public const string TurnChanged = "turn_changed";
    public const string NewCards = "new_cards";
    public const string TookDamage = "took_damage";
    public const string PendingChoice = "pending_choice";
    public const string Unsettled = "unsettled";
    public const string Rejected = "rejected";
    public const string OutcomeUnknown = "outcome_unknown";
    public const string Canceled = "canceled";
    public const string PlayerInstruction = "player_instruction";
    public const string Disabled = "disabled";
}

internal sealed record PlanStep(string Action, uint? CardCid, int? CardIndex, int? TargetIndex, int? OptionIndex, string? Reason);

internal sealed record TurnPlan(PlanStep? First, IReadOnlyList<PlanStep> Then, IReadOnlyList<string> Warnings);

internal sealed record StepResolution(string Action, int? CardIndex, int? TargetIndex, int? OptionIndex, string? StopReason)
{
    public bool Ok => StopReason == null;
}

/// <summary>
/// A whole combat turn from one model request (W3): the first action in the act's own fields, the
/// rest in <c>then</c>. This class is the pure part -- parse, re-resolve each step against fresh
/// state, and decide when the situation moved away from what the model planned against.
/// </summary>
/// <remarks>
/// Cards are named by <c>card_cid</c> (the per-combat id in <c>combat.hand[].cid</c>) because hand
/// positions shift every time a card leaves the hand; a step that names a card only by position is
/// refused, except the first, whose position is fresh. Anything surprising stops the plan -- drawn
/// cards, a target that died, damage taken, a choice popping up, a new screen or turn -- and the
/// model is asked again with the new state. A step is never retried: the executor stops on the first
/// step that does not complete. The same rules run on both MCP surfaces; the shared vectors in
/// <c>mcp_server/tests/fixtures/turn_plan_vectors.json</c> keep the copies equal.
/// </remarks>
internal static class TurnPlanPolicy
{
    public const int MaxThenSteps = 9;

    private static readonly HashSet<string> StepKeys = new(StringComparer.Ordinal)
    {
        "action", "card_cid", "card_index", "target_index", "option_index", "reason"
    };

    public static TurnPlan Parse(string? actArgumentsJson)
    {
        var warnings = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(actArgumentsJson) ? "null" : actArgumentsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new TurnPlan(null, Array.Empty<PlanStep>(), new[] { "arguments: not a JSON object" });
            }

            var first = ReadStep(root, "", warnings, allowNested: true);
            var then = new List<PlanStep>();
            if (root.TryGetProperty("then", out var list))
            {
                if (list.ValueKind != JsonValueKind.Array)
                {
                    warnings.Add("then: not an array");
                }
                else
                {
                    var k = 0;
                    foreach (var item in list.EnumerateArray())
                    {
                        var prefix = $"then[{k}]: ";
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            warnings.Add(prefix + "missing action");
                        }
                        else if (then.Count >= MaxThenSteps)
                        {
                            // Counted once below.
                        }
                        else if (ReadStep(item, prefix, warnings, allowNested: false) is { } step)
                        {
                            then.Add(step);
                        }
                        else
                        {
                            warnings.Add(prefix + "missing action");
                        }

                        k++;
                    }

                    if (list.GetArrayLength() > MaxThenSteps && then.Count == MaxThenSteps)
                    {
                        warnings.Add($"then: truncated to {MaxThenSteps} steps");
                    }
                }
            }

            return new TurnPlan(first, then, warnings);
        }
        catch (JsonException)
        {
            return new TurnPlan(null, Array.Empty<PlanStep>(), new[] { "arguments: not a JSON object" });
        }
    }

    /// <summary>
    /// The step's concrete act arguments against fresh compact state, or why it cannot run.
    /// <paramref name="requireCid"/> refuses a <c>play_card</c> that names its card only by position.
    /// </summary>
    public static StepResolution Resolve(PlanStep step, string? compactStateJson, bool requireCid = false)
    {
        var action = step.Action;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(compactStateJson) ? "null" : compactStateJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Stop(action, TurnPlanStopReasons.IllegalAction);
            }

            var available = root.TryGetProperty("available_actions", out var names) && names.ValueKind == JsonValueKind.Array
                ? names.EnumerateArray().Where(name => name.ValueKind == JsonValueKind.String).Select(name => name.GetString()!).ToList()
                : new List<string>();
            if (!available.Any(name => name.Equals(action, StringComparison.OrdinalIgnoreCase)))
            {
                return Stop(action, TurnPlanStopReasons.IllegalAction);
            }

            if (!action.Equals("play_card", StringComparison.OrdinalIgnoreCase))
            {
                return new StepResolution(action, step.CardIndex, step.TargetIndex, step.OptionIndex, null);
            }

            var hand = Hand(root);
            JsonElement? card = null;
            if (step.CardCid is { } cid)
            {
                card = hand.FirstOrDefault(entry => ReadUInt(entry, "cid") == cid) is { ValueKind: JsonValueKind.Object } byCid ? byCid : null;
            }
            else if (requireCid)
            {
                return Stop(action, TurnPlanStopReasons.IllegalAction);
            }
            else if (step.CardIndex is { } position)
            {
                card = hand.FirstOrDefault(entry => ReadInt(entry, "i") == position) is { ValueKind: JsonValueKind.Object } byIndex ? byIndex : null;
            }
            else
            {
                return Stop(action, TurnPlanStopReasons.IllegalAction);
            }

            if (card is not { } found || ReadInt(found, "i") is not { } index)
            {
                return Stop(action, TurnPlanStopReasons.CardGone);
            }

            if (ReadBool(found, "playable") != true)
            {
                return Stop(action, TurnPlanStopReasons.CardUnplayable);
            }

            var targets = found.TryGetProperty("targets", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _)).Select(item => item.GetInt32()).ToList()
                : new List<int>();
            if (targets.Count == 0)
            {
                return new StepResolution(action, index, null, null, null);
            }

            var standing = targets.Where(target => EnemyStanding(root, target)).ToList();
            if (step.TargetIndex is { } chosen)
            {
                return standing.Contains(chosen)
                    ? new StepResolution(action, index, chosen, null, null)
                    : Stop(action, TurnPlanStopReasons.TargetGone);
            }

            return standing.Count == 1
                ? new StepResolution(action, index, standing[0], null, null)
                : Stop(action, TurnPlanStopReasons.TargetGone);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Stop(action, TurnPlanStopReasons.IllegalAction);
        }
    }

    /// <summary>
    /// The first way the current frame left the situation the model planned against, or null.
    /// Unreadable state counts as changed: the plan stops rather than acting blind.
    /// </summary>
    public static string? Diverged(string? planTimeCompactJson, string? currentCompactJson)
    {
        try
        {
            using var planDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(planTimeCompactJson) ? "null" : planTimeCompactJson);
            using var currentDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(currentCompactJson) ? "null" : currentCompactJson);
            var plan = planDocument.RootElement;
            var current = currentDocument.RootElement;
            if (plan.ValueKind != JsonValueKind.Object || current.ValueKind != JsonValueKind.Object)
            {
                return TurnPlanStopReasons.ScreenChanged;
            }

            var currentScreen = ReadString(current, "screen");
            if (IsObject(current, "selection") || IsObject(current, "modal")
                || (IsObject(current, "combat") && current.GetProperty("combat") is var combat
                    && IsObject(combat, "action_readiness") && ReadBool(combat.GetProperty("action_readiness"), "hand_in_card_selection") == true)
                || currentScreen is "CARD_SELECTION" or "MODAL")
            {
                return TurnPlanStopReasons.PendingChoice;
            }

            if (!string.Equals(currentScreen, ReadString(plan, "screen"), StringComparison.Ordinal))
            {
                return TurnPlanStopReasons.ScreenChanged;
            }

            if (ReadInt(plan, "turn") is { } planTurn && ReadInt(current, "turn") is { } currentTurn && planTurn != currentTurn)
            {
                return TurnPlanStopReasons.TurnChanged;
            }

            var planCids = Hand(plan).Select(card => ReadUInt(card, "cid")).Where(cid => cid != null).ToHashSet();
            if (planCids.Count > 0 && Hand(current).Any(card => ReadUInt(card, "cid") is { } cid && !planCids.Contains(cid)))
            {
                return TurnPlanStopReasons.NewCards;
            }

            if (PlayerHp(plan) is { } before && PlayerHp(current) is { } after && after < before)
            {
                return TurnPlanStopReasons.TookDamage;
            }

            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return TurnPlanStopReasons.ScreenChanged;
        }
    }

    /// <summary>The hand position of the card with this per-combat id, or null.</summary>
    public static int? FindCardIndex(string? compactStateJson, uint cid)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(compactStateJson) ? "null" : compactStateJson);
            var root = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("state", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                ? nested
                : document.RootElement;
            return root.ValueKind != JsonValueKind.Object
                ? null
                : Hand(root).Where(card => ReadUInt(card, "cid") == cid).Select(card => ReadInt(card, "i")).FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PlanStep? ReadStep(JsonElement element, string prefix, List<string> warnings, bool allowNested)
    {
        var action = ReadString(element, "action")?.Trim().ToLowerInvariant();
        if (!allowNested)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "then")
                {
                    warnings.Add(prefix + "nested then ignored");
                }
                else if (!StepKeys.Contains(property.Name))
                {
                    warnings.Add(prefix + "unknown key " + property.Name + " ignored");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return null;
        }

        uint? cid = null;
        if (ReadLenientInt(element, "card_cid") is { } rawCid)
        {
            if (rawCid < 0)
            {
                warnings.Add(prefix + "card_cid must be >= 0");
            }
            else
            {
                cid = (uint)rawCid;
            }
        }

        return new PlanStep(
            action,
            cid,
            ReadLenientInt(element, "card_index") is { } card ? (int)card : null,
            ReadLenientInt(element, "target_index") is { } target ? (int)target : null,
            ReadLenientInt(element, "option_index") is { } option ? (int)option : null,
            ReadString(element, "reason"));
    }

    private static StepResolution Stop(string action, string reason) => new(action, null, null, null, reason);

    private static List<JsonElement> Hand(JsonElement root) =>
        IsObject(root, "combat") && root.GetProperty("combat").TryGetProperty("hand", out var hand) && hand.ValueKind == JsonValueKind.Array
            ? hand.EnumerateArray().Where(card => card.ValueKind == JsonValueKind.Object).ToList()
            : new List<JsonElement>();

    private static bool EnemyStanding(JsonElement root, int index)
    {
        if (!IsObject(root, "combat") || !root.GetProperty("combat").TryGetProperty("enemies", out var enemies) || enemies.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var enemy in enemies.EnumerateArray())
        {
            if (enemy.ValueKind == JsonValueKind.Object && ReadInt(enemy, "i") == index)
            {
                return ReadBool(enemy, "alive") == true && ReadBool(enemy, "hittable") == true;
            }
        }

        return false;
    }

    private static int? PlayerHp(JsonElement root)
    {
        if (!IsObject(root, "combat") || !IsObject(root.GetProperty("combat"), "player"))
        {
            return null;
        }

        var hp = ReadString(root.GetProperty("combat").GetProperty("player"), "hp");
        var slash = hp?.IndexOf('/') ?? -1;
        return slash > 0 && int.TryParse(hp![..slash].Trim(), out var value) ? value : null;
    }

    private static bool IsObject(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object;

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? ReadBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static uint? ReadUInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number) ? number : null;

    /// <summary>A number, or a numeric string -- models write <c>"3"</c> as often as <c>3</c>.</summary>
    private static long? ReadLenientInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString()?.Trim(), out var parsed) => parsed,
            _ => null
        };
    }
}
