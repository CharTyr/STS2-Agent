using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// Forced moves without the model (W2): every rule fires on its case, and every trade-off is refused.
/// </summary>
internal static class AutopilotPolicyTests
{
    private static AutopilotDecision Decide(string json, bool nonCombatOnly = false, bool instruction = false) =>
        AutopilotPolicy.Decide(json, nonCombatOnly, instruction);

    private const string NoMovesCombat = """
        {"screen":"COMBAT","in_combat":true,"available_actions":["end_turn","discard_potion"],
         "combat":{"action_readiness":{"can_use_combat_actions":true},"end_turn_will_kill_player":false,
                   "hand":[{"i":0,"line":"Wound","playable":false},{"i":1,"line":"Strike","playable":false}]},
         "run":{"potions":[{"i":0,"potion_id":"FIRE_POTION","usable":false},{"i":1,"potion_id":null,"usable":false}]}}
        """;

    public static void CombatWithNothingToDoEndsTheTurn()
    {
        var decision = Decide(NoMovesCombat);
        Assert.Equal("end_turn", decision.Action);
        Assert.Equal("combat_no_moves", decision.RuleId);

        Assert.Equal("end_turn", Decide("""{"screen":"COMBAT","in_combat":true,"available_actions":["end_turn"],"combat":{"action_readiness":{"can_use_combat_actions":true},"hand":[]}}""").Action,
            "An empty hand has nothing to play.");
    }

    public static void CombatWithAnyChoiceGoesToTheModel()
    {
        Assert.False(Decide(NoMovesCombat.Replace("\"playable\":false}]", "\"playable\":true}]")).ShouldAct, "A playable card is a choice.");
        Assert.False(Decide(NoMovesCombat.Replace("\"FIRE_POTION\",\"usable\":false", "\"FIRE_POTION\",\"usable\":true")).ShouldAct, "A usable potion is a choice.");
        Assert.False(Decide(NoMovesCombat.Replace("\"end_turn_will_kill_player\":false", "\"end_turn_will_kill_player\":true")).ShouldAct,
            "A known-lethal end of turn is never automatic.");
        Assert.False(Decide(NoMovesCombat.Replace("\"can_use_combat_actions\":true", "\"can_use_combat_actions\":false")).ShouldAct,
            "A closed combat gate means wait, not act.");
    }

    public static void GuardsYieldToThePlayerAndToNonCombatMode()
    {
        Assert.False(Decide(NoMovesCombat, instruction: true).ShouldAct, "Queued player guidance goes to the model.");
        Assert.False(Decide(NoMovesCombat, nonCombatOnly: true).ShouldAct, "Non-combat mode never acts in combat.");
        Assert.False(Decide("""{"screen":"UNKNOWN","available_actions":["proceed"]}""").ShouldAct);
        Assert.False(Decide("""{"screen":"MAP","available_actions":[]}""").ShouldAct);
        foreach (var junk in new[] { "", "null", "[]", "{", "{\"screen\":3}" })
        {
            Assert.False(Decide(junk).ShouldAct, "Malformed input: " + junk);
        }
    }

    public static void ASinglePathOnTheMapIsTaken()
    {
        var single = Decide("""{"screen":"MAP","available_actions":["choose_map_node"],"map":{"options":[{"i":2,"line":"Monster (3,4)"}],"votes":[]}}""");
        Assert.Equal("choose_map_node", single.Action);
        Assert.Equal(2, single.OptionIndex);
        Assert.Equal("map_single_path", single.RuleId);

        Assert.False(Decide("""{"screen":"MAP","available_actions":["choose_map_node"],"map":{"options":[{"i":0,"line":"Elite"},{"i":1,"line":"Rest"}]}}""").ShouldAct,
            "Two paths are a route decision.");
        Assert.False(Decide("""{"screen":"MAP","available_actions":["choose_map_node"],"map":{"options":[{"i":0,"line":"Monster"}],"votes":[{"local":true,"coord":"3,4"}]}}""").ShouldAct,
            "Already voted locally.");
    }

    public static void RewardsWithoutChoicesAreCollected()
    {
        var all = Decide("""{"screen":"REWARD","available_actions":["claim_reward","collect_rewards_and_proceed"],"reward":{"pending_card_choice":false,"rewards":[{"i":0,"line":"Gold: 获得 25 金币","claimable":true},{"i":1,"line":"Potion: 获得火焰药水","claimable":true}],"cards":[]}}""");
        Assert.Equal("collect_rewards_and_proceed", all.Action);
        Assert.Equal("reward_collect_all", all.RuleId);

        Assert.False(Decide("""{"screen":"REWARD","available_actions":["collect_rewards_and_proceed"],"reward":{"pending_card_choice":true,"rewards":[],"cards":[{"i":0}]}}""").ShouldAct,
            "A pending card choice is the model's.");

        var beltFull = Decide("""{"screen":"REWARD","available_actions":["collect_rewards_and_proceed"],"reward":{"rewards":[{"i":0,"line":"Potion: 获得火焰药水","claimable":false}],"cards":[]}}""");
        Assert.False(beltFull.ShouldAct, "A potion that does not fit is a discard decision.");

        var cardRow = Decide("""{"screen":"REWARD","available_actions":["claim_reward","collect_rewards_and_proceed"],"reward":{"rewards":[{"i":0,"line":"Card: 选择一张卡牌","claimable":true},{"i":1,"line":"Gold: 获得 25 金币","claimable":true}],"cards":[]}}""");
        Assert.Equal("claim_reward", cardRow.Action, "The gold is still claimed while the card row waits for the model.");
        Assert.Equal(1, cardRow.OptionIndex);
        Assert.Equal("reward_claim_safe", cardRow.RuleId);
    }

    public static void SingleOptionScreensAreResolved()
    {
        var eventOne = Decide("""{"screen":"EVENT","available_actions":["choose_event_option"],"event":{"options":[{"i":0,"locked":true},{"i":1,"locked":false,"kill":false}]}}""");
        Assert.Equal("choose_event_option", eventOne.Action);
        Assert.Equal(1, eventOne.OptionIndex);
        Assert.False(Decide("""{"screen":"EVENT","available_actions":["choose_event_option"],"event":{"options":[{"i":0},{"i":1}]}}""").ShouldAct);
        Assert.False(Decide("""{"screen":"EVENT","available_actions":["choose_event_option"],"event":{"options":[{"i":0,"kill":true}]}}""").ShouldAct,
            "An option that kills the player is never automatic.");

        Assert.Equal(0, Decide("""{"screen":"REST","available_actions":["choose_rest_option"],"rest":{"options":[{"i":0,"option_id":"HEAL","enabled":true},{"i":1,"option_id":"SMITH","enabled":false}]}}""").OptionIndex);
        Assert.False(Decide("""{"screen":"REST","available_actions":["choose_rest_option"],"rest":{"options":[{"i":0,"enabled":true},{"i":1,"enabled":true}]}}""").ShouldAct,
            "Heal or smith is a decision.");

        Assert.Equal("open_chest", Decide("""{"screen":"CHEST","available_actions":["open_chest"],"chest":{"opened":false,"relics":[]}}""").Action);
        Assert.Equal("chest_single_relic", Decide("""{"screen":"CHEST","available_actions":["choose_treasure_relic"],"chest":{"opened":true,"relics":[{"i":0}]}}""").RuleId);
        Assert.False(Decide("""{"screen":"CHEST","available_actions":["choose_treasure_relic"],"chest":{"opened":true,"relics":[{"i":0},{"i":1},{"i":2}]}}""").ShouldAct);

        Assert.Equal("capstone_single_option", Decide("""{"screen":"CAPSTONE_SELECTION","available_actions":["choose_capstone_option"],"capstone":{"options":[{"i":3}]}}""").RuleId);
    }

    public static void ModalsAreConfirmedOnlyWhenThereIsNoCancel()
    {
        var ftue = Decide("""{"screen":"MODAL","in_combat":false,"available_actions":["confirm_modal"],"modal":{"type":"NFtueCombatModal","confirm":true,"dismiss":false}}""");
        Assert.Equal("confirm_modal", ftue.Action);
        Assert.Equal("modal_ftue", ftue.RuleId);
        Assert.False(Decide("""{"screen":"MODAL","available_actions":["confirm_modal","dismiss_modal"],"modal":{"type":"NFtueCombatModal","confirm":true,"dismiss":true}}""").ShouldAct,
            "A dialog with a cancel is a decision.");
        Assert.False(Decide("""{"screen":"MODAL","in_combat":false,"available_actions":["confirm_modal"],"modal":{"type":"NAbandonRunConfirm","confirm":true,"dismiss":false}}""").ShouldAct,
            "A non-tutorial confirm outside combat is left to the model.");
    }

    public static void GameOverAndLoneButtonsContinue()
    {
        Assert.Equal("continue_game_over", Decide("""{"screen":"GAME_OVER","available_actions":["continue_game_over","return_to_main_menu"]}""").Action);
        var lone = Decide("""{"screen":"REST","available_actions":["proceed","save_and_quit"],"rest":{"options":[]}}""");
        Assert.Equal("proceed", lone.Action, "Passive actions do not count against the last action standing.");
        Assert.Equal("single_action", lone.RuleId);
        Assert.Equal("confirm_unlock", Decide("""{"screen":"UNLOCK","available_actions":["confirm_unlock"]}""").Action);
        Assert.False(Decide("""{"screen":"SHOP","available_actions":["proceed","buy_card"]}""").ShouldAct, "Shopping is a decision.");
    }

    public static void TradeOffsAreNeverAutomatic()
    {
        foreach (var action in new[]
                 {
                     "buy_card", "buy_relic", "buy_potion", "remove_card_at_shop", "choose_reward_card", "skip_reward_cards",
                     "select_deck_card", "confirm_selection", "use_potion", "play_card", "choose_bundle", "confirm_bundle",
                     "choose_timeline_epoch", "resolve_rewards", "open_shop_inventory", "dismiss_modal", "embark",
                     "select_character", "abandon_run", "host_multiplayer_lobby"
                 })
        {
            Assert.False(Decide($$"""{"screen":"CARD_SELECTION","available_actions":["{{action}}"]}""").ShouldAct,
                action + " alone must still go to the model.");
        }
    }
}
