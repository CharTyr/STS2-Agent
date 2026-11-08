using System.Text.Json;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Agent;

/// <summary>A forced move to make without the model, or <see cref="None"/>.</summary>
internal sealed record AutopilotDecision(string? Action, int? OptionIndex, string RuleId, string? Detail)
{
    public static readonly AutopilotDecision None = new(null, null, "none", null);

    public bool ShouldAct => Action != null;
}

/// <summary>
/// Recognises steps with no judgment in them and names the one action to take, so auto-play does
/// not pay a model request (about 25k tokens and several seconds) for them (W2).
/// </summary>
/// <remarks>
/// The ledger of 2026-10-28 showed 15% of accepted decisions were forced: 46 of 305 were "nothing
/// playable, end the turn", and single-path maps, gold rows and tutorial popups made up much of the
/// rest. This policy only ever takes a step that has exactly one sensible answer. It never makes a
/// trade-off: no card picks, no shopping or removal, no choice between several event/map/rest
/// options, no potions, no character or difficulty choice, no lobby or timeline action, no confirm of
/// a dialog that offers a cancel. A known-lethal end of turn is also left to the model and the safety
/// rails rather than taken automatically.
///
/// Pure (compact JSON in, decision out) so every rule and every refusal is tested offline. The
/// executor (<c>AgentLoop.Autopilot.cs</c>) still runs the chosen action through the same legality,
/// index and dispatch-time checks as a model's act.
/// </remarks>
internal static class AutopilotPolicy
{
    private static readonly HashSet<string> PassiveActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "discard_potion", "save_and_quit"
    };

    private static readonly HashSet<string> AdminActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "abandon_run", "return_to_main_menu", "close_main_menu_submenu", "switch_profile", "unready",
        "open_character_select", "open_timeline", "invite_ai_teammate", "continue_ai_teammate", "continue_run",
        "host_multiplayer_lobby", "join_multiplayer_lobby", "ready_multiplayer_lobby", "disconnect_multiplayer_lobby",
        "increase_ascension", "decrease_ascension", "embark", "select_character", "close_cards_view"
    };

    /// <summary>Actions that are always a decision, so the last-action-standing rule never takes them.</summary>
    private static readonly HashSet<string> NeverAutomatic = new(StringComparer.OrdinalIgnoreCase)
    {
        "play_card", "use_potion", "choose_reward_card", "skip_reward_cards", "select_deck_card", "confirm_selection",
        "buy_card", "buy_relic", "buy_potion", "remove_card_at_shop", "choose_bundle", "confirm_bundle",
        "choose_timeline_epoch", "confirm_timeline_overlay", "crystal_set_tool", "crystal_clear_cell", "resolve_rewards",
        "dismiss_modal", "confirm_modal", "open_shop_inventory", "close_shop_inventory", "end_turn",
        // Only through its own rule, which checks for pending card picks and a full potion belt.
        "collect_rewards_and_proceed", "continue_game_over"
    };

    public static AutopilotDecision Decide(string? compactStateJson, bool nonCombatOnlyEnabled, bool playerInstructionQueued)
    {
        if (playerInstructionQueued || string.IsNullOrWhiteSpace(compactStateJson))
        {
            return AutopilotDecision.None;
        }

        try
        {
            using var document = JsonDocument.Parse(compactStateJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return AutopilotDecision.None;
            }

            var screen = ReadString(root, "screen")?.Trim().ToUpperInvariant();
            var inCombat = ReadBool(root, "in_combat") == true;
            var actions = ReadStrings(root, "available_actions");
            if (string.IsNullOrEmpty(screen) || screen == "UNKNOWN" || actions.Count == 0)
            {
                return AutopilotDecision.None;
            }

            if (nonCombatOnlyEnabled && NonCombatOnlyPolicy.IsCombat(screen, inCombat))
            {
                return AutopilotDecision.None;
            }

            return Modal(root, screen, actions, inCombat)
                ?? GameOver(screen, actions)
                ?? Combat(root, screen, actions)
                ?? Map(root, screen, actions)
                ?? Reward(root, screen, actions)
                ?? SingleOption(root, screen, actions)
                ?? LastActionStanding(actions)
                ?? AutopilotDecision.None;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return AutopilotDecision.None;
        }
    }

    private static AutopilotDecision? Modal(JsonElement root, string screen, IReadOnlyList<string> actions, bool inCombat)
    {
        var modal = Object(root, "modal");
        // The co-op companion's rule, reused rather than restated: a tutorial (FTUE) popup or an
        // in-combat notice that offers only "confirm".
        var decision = CompanionPlayPolicy.DecideBlockingModal(
            screen,
            actions,
            modal is { } m ? ReadString(m, "type") : null,
            modal is { } c && ReadBool(c, "confirm") == true,
            modal is { } d && ReadBool(d, "dismiss") == true,
            inCombat);
        return decision.Kind == CompanionImmediateDecision.Act && decision.Action != null
            ? new AutopilotDecision(decision.Action, null, "modal_ftue", "confirm-only popup")
            : null;
    }

    private static AutopilotDecision? GameOver(string screen, IReadOnlyList<string> actions)
    {
        if (screen != "GAME_OVER" || !Has(actions, "continue_game_over"))
        {
            return null;
        }

        var others = actions.Where(action => !PassiveActions.Contains(action) && !AdminActions.Contains(action)
            && !action.Equals("continue_game_over", StringComparison.OrdinalIgnoreCase)
            && !action.Equals("dismiss_game_over_wait", StringComparison.OrdinalIgnoreCase));
        return others.Any() ? null : new AutopilotDecision("continue_game_over", null, "game_over_continue", null);
    }

    private static AutopilotDecision? Combat(JsonElement root, string screen, IReadOnlyList<string> actions)
    {
        if (screen != "COMBAT" || !Has(actions, "end_turn") || Object(root, "combat") is not { } combat)
        {
            return null;
        }

        if (Object(combat, "action_readiness") is not { } readiness || ReadBool(readiness, "can_use_combat_actions") != true)
        {
            return null;
        }

        if (ReadBool(combat, "end_turn_will_kill_player") == true)
        {
            // Known lethal: the model and the Sandpit/lethal rails decide, never an automatic click.
            return null;
        }

        foreach (var card in Array(combat, "hand"))
        {
            if (ReadBool(card, "playable") == true)
            {
                return null;
            }
        }

        if (Object(root, "run") is { } run)
        {
            foreach (var potion in Array(run, "potions"))
            {
                if (ReadString(potion, "potion_id") != null && ReadBool(potion, "usable") == true)
                {
                    return null;
                }
            }
        }

        return new AutopilotDecision("end_turn", null, "combat_no_moves", "no playable card or usable potion");
    }

    private static AutopilotDecision? Map(JsonElement root, string screen, IReadOnlyList<string> actions)
    {
        if (screen != "MAP" || !Has(actions, "choose_map_node") || Object(root, "map") is not { } map)
        {
            return null;
        }

        if (Array(map, "votes").Any(vote => ReadBool(vote, "local") == true))
        {
            return null;
        }

        var options = Array(map, "options").ToList();
        return options.Count == 1 && ReadInt(options[0], "i") is { } index
            ? new AutopilotDecision("choose_map_node", index, "map_single_path", null)
            : null;
    }

    private static AutopilotDecision? Reward(JsonElement root, string screen, IReadOnlyList<string> actions)
    {
        if (screen != "REWARD" || Object(root, "reward") is not { } reward)
        {
            return null;
        }

        var rows = Array(reward, "rewards").ToList();
        var cardsPending = ReadBool(reward, "pending_card_choice") == true || Array(reward, "cards").Any();
        var fullBeltPotion = rows.Any(row => RowType(row) == "potion" && ReadBool(row, "claimable") == false);
        var unopenedCard = rows.Any(row => RowType(row) == "card" && ReadBool(row, "claimable") == true);

        if (Has(actions, "collect_rewards_and_proceed") && !cardsPending && !fullBeltPotion && !unopenedCard)
        {
            return new AutopilotDecision("collect_rewards_and_proceed", null, "reward_collect_all", null);
        }

        if (Has(actions, "claim_reward"))
        {
            // Gold and relic rows have no downside; card rows and potion rows are choices.
            var safe = rows.FirstOrDefault(row => ReadBool(row, "claimable") == true && RowType(row) is "gold" or "relic");
            if (safe.ValueKind == JsonValueKind.Object && ReadInt(safe, "i") is { } index)
            {
                return new AutopilotDecision("claim_reward", index, "reward_claim_safe", RowType(safe));
            }
        }

        return null;
    }

    private static AutopilotDecision? SingleOption(JsonElement root, string screen, IReadOnlyList<string> actions)
    {
        switch (screen)
        {
            case "EVENT" when Has(actions, "choose_event_option") && Object(root, "event") is { } eventPayload:
            {
                var open = Array(eventPayload, "options").Where(option => ReadBool(option, "locked") != true).ToList();
                if (open.Count == 1 && ReadBool(open[0], "kill") != true && ReadInt(open[0], "i") is { } index)
                {
                    return new AutopilotDecision("choose_event_option", index, "event_single_option", null);
                }

                return null;
            }
            case "CAPSTONE_SELECTION" when Has(actions, "choose_capstone_option") && Object(root, "capstone") is { } capstone:
            {
                var open = Array(capstone, "options")
                    .Where(option => ReadBool(option, "locked") != true && ReadBool(option, "enabled") != false)
                    .ToList();
                return open.Count == 1 && ReadInt(open[0], "i") is { } index
                    ? new AutopilotDecision("choose_capstone_option", index, "capstone_single_option", null)
                    : null;
            }
            case "REST" when Has(actions, "choose_rest_option") && Object(root, "rest") is { } rest:
            {
                var enabled = Array(rest, "options").Where(option => ReadBool(option, "enabled") == true).ToList();
                return enabled.Count == 1 && ReadBool(enabled[0], "requires_target") != true && ReadInt(enabled[0], "i") is { } index
                    ? new AutopilotDecision("choose_rest_option", index, "rest_single_option", ReadString(enabled[0], "option_id"))
                    : null;
            }
            case "CHEST" when Object(root, "chest") is { } chest:
            {
                if (Has(actions, "choose_treasure_relic"))
                {
                    var relics = Array(chest, "relics").ToList();
                    return relics.Count == 1 && ReadInt(relics[0], "i") is { } index
                        ? new AutopilotDecision("choose_treasure_relic", index, "chest_single_relic", null)
                        : null;
                }

                return Has(actions, "open_chest") && ReadBool(chest, "opened") != true
                    ? new AutopilotDecision("open_chest", null, "chest_open", null)
                    : null;
            }
            default:
                return null;
        }
    }

    private static AutopilotDecision? LastActionStanding(IReadOnlyList<string> actions)
    {
        var remaining = actions
            .Where(action => !PassiveActions.Contains(action) && !AdminActions.Contains(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (remaining.Count != 1)
        {
            return null;
        }

        var action = remaining[0];
        return NeverAutomatic.Contains(action) || ActIndexValidator.RequiresIndex(action)
            ? null
            : new AutopilotDecision(action, null, "single_action", null);
    }

    /// <summary>The reward row's kind: the text before the first ':' of its line, lower-cased.</summary>
    private static string RowType(JsonElement row)
    {
        var line = ReadString(row, "line") ?? string.Empty;
        var colon = line.IndexOf(':');
        return (colon > 0 ? line[..colon] : line).Trim().ToLowerInvariant();
    }

    private static bool Has(IReadOnlyList<string> actions, string name) =>
        actions.Any(action => action.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static JsonElement? Object(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList()
            : Enumerable.Empty<JsonElement>();

    private static IReadOnlyList<string> ReadStrings(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
            : new List<string>();

    private static string? ReadString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
            ? value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    private static int? ReadInt(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
