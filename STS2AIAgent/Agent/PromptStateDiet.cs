using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace STS2AIAgent.Agent;

/// <summary>
/// Which prompt-only projections <see cref="PromptStateDiet.Apply"/> is allowed to perform.
/// </summary>
/// <remarks>
/// Flags rather than a mode because the rules are independent: each one answers "can this decision
/// still be made without this payload?" from its own evidence (the screen, the player count, whether
/// combat is present), and each can be switched on alone so a test can prove it leaves everything
/// else in the document alone.
/// </remarks>
[Flags]
internal enum DietRules
{
    None = 0,
    MapGraphOnlyOnMap = 1,
    SoloPlayersSlim = 2,
    CombatDropsRunPiles = 4,
    ReadinessTrim = 8,
    DropCardValueNote = 16,
    HandCardNulls = 32,
    NullTopLevelSections = 64,
    All = 127,
}

/// <summary>
/// Projects the compact game state down to the part the current decision can use, for the prompt
/// only.
/// </summary>
/// <remarks>
/// The same compact <c>agent_view</c> is pasted into every model prompt, and roughly 2,500-2,900
/// tokens of it are dead weight on a typical frame: the whole map graph while a reward is on screen,
/// the solo player's deck/relics/potions repeated inside <c>run.players</c>, <c>run.piles</c>
/// duplicating <c>combat.draw/discard/exhaust</c>, ~25 readiness flags, a constant note, and ~10 null
/// fields on every hand card. None of it changes a decision; all of it is paid for again on the next
/// turn, and again on the one after that.
///
/// Invariants, because callers rely on them:
/// <list type="bullet">
/// <item>Only a string handed in by the caller is transformed. The public compact view keeps its
/// shape; nothing here is reachable from the HTTP or MCP surface.</item>
/// <item>Bad input is returned verbatim (null becomes the empty string). This runs on the prompt
/// path, where throwing would end the turn.</item>
/// <item>Only whole subtrees are dropped or whitelisted. Unknown and future keys pass through
/// untouched, so a new field needs no edit here -- but a new <em>duplicate</em> of one of the fields
/// below does, and is the way to spot one.</item>
/// <item>The remaining keys keep their original order, and Chinese text stays literal UTF-8. An
/// escaped <c>\u6253\u51FB</c> costs six tokens per character instead of one.</item>
/// </list>
/// </remarks>
internal static class PromptStateDiet
{
    /// <summary>
    /// The constant that used to ride inside every combat frame. The caller moves this text into the
    /// static system prompt, where it is paid for once per request instead of once per state read.
    /// </summary>
    public const string CardValueNote =
        "Hand line and values.preview include live player modifiers such as Strength. Do not apply modifiers already reflected by preview again. Preview is not a guaranteed target hit: check target-specific Vulnerable, caps and block.";

    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>What a map is worth on a screen where the player cannot pick a node.</summary>
    private static readonly HashSet<string> MapKeysOutsideTheGraph = new(StringComparer.Ordinal)
    {
        "open",
        "current",
        "options",
        "votes",
    };

    /// <summary>
    /// A solo run's own inventory, all of which the top level of <c>run</c> already carries. Anything
    /// else in the element (<c>deck</c>, <c>relics</c>, <c>potions</c>, ...) is a copy, not a fact.
    /// </summary>
    private static readonly HashSet<string> SoloPlayerKeys = new(StringComparer.Ordinal)
    {
        "player_id",
        "is_local",
        "current_hp",
        "max_hp",
        "gold",
    };

    /// <summary>
    /// The readiness flags a decision reads: whether actions are allowed at all, why not, what is
    /// running, whether a modal owns the input, and which hand phase is open.
    /// </summary>
    private static readonly HashSet<string> ReadinessKeys = new(StringComparer.Ordinal)
    {
        "can_use_combat_actions",
        "reason",
        "running_action_type",
        "modal_open",
        "modal_type",
        "player_actions_disabled",
        "hand_in_card_play",
        "hand_in_card_selection",
        "local_turn_ready",
        "end_turn_kick",
    };

    /// <summary>
    /// Never dropped by the null sweep even when empty: the index is the act argument, the id is the
    /// metadata lookup, the line is the card, and playable/cost/type are what the decision turns on.
    /// </summary>
    private static readonly HashSet<string> HandCardIdentityKeys = new(StringComparer.Ordinal)
    {
        "i",
        "cid",
        "card_id",
        "line",
        "playable",
        "energy_cost",
        "type",
    };

    /// <summary>
    /// Unplayable diagnostics. On a playable card they are stale noise from an earlier frame; on an
    /// unplayable one <c>why</c> is the only explanation the model gets.
    /// </summary>
    private static readonly HashSet<string> PlayableCardNoiseKeys = new(StringComparer.Ordinal)
    {
        "can_play_result",
        "why",
        "unplayable_reason",
        "unplayable_reason_raw",
        "unplayable_preventer_id",
        "unplayable_preventer_type",
    };

    /// <summary>
    /// Top-level keys whose null is itself information: an unknown screen, an empty action list
    /// ("nothing to do now" is not "unknown"), <c>in_combat=false</c>, the run identity, and a null
    /// <c>turn</c> meaning "not in a turn".
    /// </summary>
    private static readonly HashSet<string> RetainedTopLevelKeys = new(StringComparer.Ordinal)
    {
        "screen",
        "available_actions",
        "in_combat",
        "run_id",
        "turn",
    };

    /// <summary>
    /// Returns a leaner JSON document for the prompt, or the input unchanged when it is not a JSON
    /// object. Never throws.
    /// </summary>
    public static string Apply(string? compactStateJson, DietRules rules = DietRules.All)
    {
        if (compactStateJson is null) return string.Empty;
        if (string.IsNullOrWhiteSpace(compactStateJson)) return compactStateJson;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(compactStateJson);
        }
        catch (JsonException)
        {
            // Not JSON at all. Re-encoding it would only lose the caller's own diagnostics.
            return compactStateJson;
        }

        // A JSON array, a bare literal or "null" is not a state; hand it back rather than inventing
        // an empty object the caller might mistake for a real frame.
        if (root is not JsonObject state) return compactStateJson;

        if (Has(rules, DietRules.MapGraphOnlyOnMap)) ApplyMapGraphOnlyOnMap(state);
        if (Has(rules, DietRules.SoloPlayersSlim)) ApplySoloPlayersSlim(state);
        if (Has(rules, DietRules.CombatDropsRunPiles)) ApplyCombatDropsRunPiles(state);
        if (Has(rules, DietRules.ReadinessTrim)) ApplyReadinessTrim(state);
        if (Has(rules, DietRules.DropCardValueNote)) ApplyDropCardValueNote(state);
        if (Has(rules, DietRules.HandCardNulls)) ApplyHandCardNulls(state);
        if (Has(rules, DietRules.NullTopLevelSections)) ApplyNullTopLevelSections(state);

        return state.ToJsonString(Compact);
    }

    private static bool Has(DietRules rules, DietRules rule) => (rules & rule) != 0;

    /// <summary>
    /// Off the map screen the graph is unactionable: ~20 nodes with their children, paid for on every
    /// reward, event and combat frame. The four retained keys are the ones a room can still react to
    /// (is the map open, where am I, what are the walkable options, how are the others voting).
    /// </summary>
    private static void ApplyMapGraphOnlyOnMap(JsonObject state)
    {
        if (string.Equals(Text(state["screen"]), "MAP", StringComparison.OrdinalIgnoreCase)) return;
        if (state["map"] is not JsonObject map) return;

        foreach (var key in map.Select(pair => pair.Key).ToList())
        {
            if (!MapKeysOutsideTheGraph.Contains(key)) map.Remove(key);
        }
    }

    /// <summary>
    /// In a solo run <c>run.players[0]</c> re-lists the deck, relics and potions the top level of
    /// <c>run</c> already carries -- several hundred tokens of pure duplication. In multiplayer those
    /// same fields are the *other* players' inventories, which the local deck does not describe, so
    /// the whole element stays. <c>combat.players</c> is a different question (HP, block, status in
    /// this fight) and is never touched.
    /// </summary>
    private static void ApplySoloPlayersSlim(JsonObject state)
    {
        if (!IsSinglePlayerRun(state)) return;
        if (state["run"] is not JsonObject run || run["players"] is not JsonArray players) return;

        foreach (var node in players)
        {
            if (node is not JsonObject player) continue;
            foreach (var key in player.Select(pair => pair.Key).ToList())
            {
                if (!SoloPlayerKeys.Contains(key)) player.Remove(key);
            }
        }
    }

    private static bool IsSinglePlayerRun(JsonObject state)
    {
        var multiplayer = state["multiplayer"] as JsonObject;
        if (multiplayer is null || Flag(multiplayer["is_multiplayer"]) != true) return true;

        // A lobby can be flagged multiplayer before anyone else joins; one player entry means there is
        // nobody else's inventory to report.
        var players = state["run"] is JsonObject run ? run["players"] as JsonArray : null;
        return players is null || players.Count <= 1;
    }

    /// <summary>
    /// <c>run.piles</c> is the same three piles <c>combat.draw/discard/exhaust</c> already reports,
    /// in a coarser shape. Out of combat there is no combat block to report them, so it stays.
    /// </summary>
    private static void ApplyCombatDropsRunPiles(JsonObject state)
    {
        if (state["combat"] is not JsonObject) return;
        if (state["run"] is JsonObject run) run.Remove("piles");
    }

    /// <summary>
    /// ~25 bookkeeping flags, of which a decision reads the ten below. Out of combat the entire block
    /// is a leftover from the last fight -- it describes a turn that no longer exists -- so it goes.
    /// </summary>
    private static void ApplyReadinessTrim(JsonObject state)
    {
        if (state["combat"] is not JsonObject combat) return;
        if (!combat.ContainsKey("action_readiness")) return;

        if (Flag(state["in_combat"]) == false)
        {
            combat.Remove("action_readiness");
            return;
        }

        if (combat["action_readiness"] is not JsonObject readiness) return;
        foreach (var key in readiness.Select(pair => pair.Key).ToList())
        {
            if (!ReadinessKeys.Contains(key)) readiness.Remove(key);
        }
    }

    /// <summary>
    /// The note is identical on every frame; the caller moves
    /// <see cref="CardValueNote"/> into the static system prompt.
    /// </summary>
    private static void ApplyDropCardValueNote(JsonObject state)
    {
        if (state["combat"] is JsonObject combat) combat.Remove("card_value_note");
    }

    /// <summary>
    /// ~10 null/empty fields per hand card, up to ten cards. Each is a key, a null and a comma --
    /// three tokens saying "not applicable", repeated. Empty *objects* are deliberately kept: a
    /// present-but-empty block still reports that the block exists, which a missing key does not.
    /// </summary>
    private static void ApplyHandCardNulls(JsonObject state)
    {
        if (state["combat"] is not JsonObject combat || combat["hand"] is not JsonArray hand) return;

        foreach (var node in hand)
        {
            if (node is not JsonObject card) continue;
            var playable = Flag(card["playable"]) == true;

            foreach (var key in card.Select(pair => pair.Key).ToList())
            {
                if (HandCardIdentityKeys.Contains(key)) continue;
                if (playable && PlayableCardNoiseKeys.Contains(key))
                {
                    card.Remove(key);
                    continue;
                }

                if (CarriesNothing(card[key])) card.Remove(key);
            }
        }
    }

    /// <summary>
    /// A dozen <c>"shop":null,"event":null</c> pairs on every frame, each asserting an absence the
    /// screen already states. The protected five keep their nulls because the null is the fact.
    /// </summary>
    private static void ApplyNullTopLevelSections(JsonObject state)
    {
        foreach (var key in state.Select(pair => pair.Key).ToList())
        {
            if (RetainedTopLevelKeys.Contains(key)) continue;
            if (state[key] is null) state.Remove(key);
        }
    }

    /// <summary>JSON null, an empty array, or an empty string -- and nothing else.</summary>
    private static bool CarriesNothing(JsonNode? node)
    {
        if (node is null) return true;
        if (node is JsonArray array) return array.Count == 0;
        return node is JsonValue value
            && value.GetValueKind() == JsonValueKind.String
            && value.GetValue<string>().Length == 0;
    }

    /// <summary>The string a node holds, or null when it is missing, JSON null or not a string.</summary>
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    /// <summary>The boolean a node holds, or null when it is missing, JSON null or not a boolean.</summary>
    private static bool? Flag(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? value.GetValue<bool>()
            : null;
}
