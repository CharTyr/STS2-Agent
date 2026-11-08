using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The prompt-only projection of the compact state (W6): each rule, each rule alone, the things it
/// must never drop, and that bad input passes through.
/// </summary>
internal static class PromptStateDietTests
{
    private const string Combat = """
        {"version":13,"screen":"COMBAT","in_combat":true,"run_id":"R1","turn":2,"available_actions":["play_card","end_turn"],
         "multiplayer":null,"shop":null,"event":null,
         "combat":{"action_readiness":{"can_use_combat_actions":true,"reason":"ready","actions_settled":true,"is_paused":false,"snapshot_stable":true,"player_action_phase":true,"combat_room_mode":"x"},
                   "threat":{"incoming":9},
                   "card_value_note":"Hand line and values.preview include live player modifiers such as Strength.",
                   "player":{"hp":"30/80","block":0,"energy":3},
                   "end_turn_will_kill_player":false,"lethal_risks":[],
                   "hand":[{"i":0,"cid":17,"type":"attack","card_id":"STRIKE","line":"打击 [1]","playable":true,"can_play_result":true,"why":null,"unplayable_reason":null,"target":"enemy","targets":[0,1],"keywords":[],"mods":[],"vs":[{"enemy":0,"damage":6}]},
                           {"i":1,"cid":18,"card_id":"WOUND","line":"伤口","playable":false,"why":"unplayable","unplayable_reason":"unplayable","targets":[],"target":null}],
                   "enemies":[{"i":0,"hp":"12/40","alive":true,"hittable":true},{"i":1,"hp":"8/20","alive":true,"hittable":true}]},
         "run":{"hp":"30/80","gold":99,"deck":[{"line":"打击*5"}],"potions":[{"i":0,"potion_id":"FIRE_POTION","usable":true}],
                "players":[{"player_id":"1","is_local":true,"current_hp":30,"max_hp":80,"gold":99,"deck":[{"line":"打击*5"}],"relics":["燃烧之血"],"potions":[]}],
                "piles":{"draw":[{"line":"防御*4"}],"discard":[],"exhaust":[]}},
         "map":{"open":false,"current":"3,1","boss_node":"14,3","nodes":[{"coord":"0,0","children":["1,1"]},{"coord":"1,1","children":["2,1"]}],"votes":[],"options":[]}}
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static void TheWholeDietKeepsWhatDecisionsRead()
    {
        var output = PromptStateDiet.Apply(Combat);
        var root = Parse(output);
        Assert.Equal("COMBAT", root.GetProperty("screen").GetString());
        Assert.Equal(2, root.GetProperty("available_actions").GetArrayLength());
        Assert.True(root.GetProperty("combat").GetProperty("action_readiness").GetProperty("can_use_combat_actions").GetBoolean());
        Assert.Equal(2, root.GetProperty("combat").GetProperty("enemies").GetArrayLength());
        Assert.False(root.GetProperty("combat").GetProperty("end_turn_will_kill_player").GetBoolean());
        Assert.Equal(1, root.GetProperty("run").GetProperty("potions").GetArrayLength());
        Assert.Equal(9, root.GetProperty("combat").GetProperty("threat").GetProperty("incoming").GetInt32(), "Unknown/future keys pass through.");
        var strike = root.GetProperty("combat").GetProperty("hand")[0];
        Assert.Equal(17, strike.GetProperty("cid").GetInt32());
        Assert.Equal(1, strike.GetProperty("vs").GetArrayLength());
        Assert.True(output.Contains("打击", StringComparison.Ordinal), "Chinese text stays literal.");
        Assert.True(output.Length <= Combat.Replace("\n", "").Replace("  ", "").Length * 0.8,
            $"The diet should cut this frame noticeably (got {output.Length} chars).");
        Assert.Equal(output, PromptStateDiet.Apply(output), "Applying twice changes nothing.");
    }

    public static void EachRuleDoesItsOneThing()
    {
        var map = Parse(PromptStateDiet.Apply(Combat, DietRules.MapGraphOnlyOnMap));
        Assert.False(map.GetProperty("map").TryGetProperty("nodes", out _), "No graph off the map screen.");
        Assert.True(map.GetProperty("map").TryGetProperty("options", out _));
        Assert.True(map.GetProperty("run").TryGetProperty("piles", out _), "Other rules did not run.");

        var onMap = Parse(PromptStateDiet.Apply(Combat.Replace("\"screen\":\"COMBAT\"", "\"screen\":\"MAP\""), DietRules.MapGraphOnlyOnMap));
        Assert.True(onMap.GetProperty("map").TryGetProperty("nodes", out _), "The map screen keeps its graph.");

        var solo = Parse(PromptStateDiet.Apply(Combat, DietRules.SoloPlayersSlim)).GetProperty("run").GetProperty("players")[0];
        Assert.False(solo.TryGetProperty("deck", out _));
        Assert.Equal(30, solo.GetProperty("current_hp").GetInt32());

        var piles = Parse(PromptStateDiet.Apply(Combat, DietRules.CombatDropsRunPiles));
        Assert.False(piles.GetProperty("run").TryGetProperty("piles", out _));

        var readiness = Parse(PromptStateDiet.Apply(Combat, DietRules.ReadinessTrim)).GetProperty("combat").GetProperty("action_readiness");
        Assert.True(readiness.TryGetProperty("reason", out _));
        Assert.False(readiness.TryGetProperty("snapshot_stable", out _));

        var note = Parse(PromptStateDiet.Apply(Combat, DietRules.DropCardValueNote));
        Assert.False(note.GetProperty("combat").TryGetProperty("card_value_note", out _));

        var hand = Parse(PromptStateDiet.Apply(Combat, DietRules.HandCardNulls)).GetProperty("combat").GetProperty("hand");
        Assert.False(hand[0].TryGetProperty("why", out _));
        Assert.False(hand[0].TryGetProperty("can_play_result", out _), "Playable cards lose unplayable diagnostics.");
        Assert.False(hand[0].TryGetProperty("keywords", out _), "Empty arrays go.");
        Assert.True(hand[0].TryGetProperty("targets", out _), "A non-empty target list stays.");
        Assert.Equal("unplayable", hand[1].GetProperty("why").GetString(), "An unplayable card keeps its reason.");
        Assert.False(hand[1].TryGetProperty("target", out _));

        var top = Parse(PromptStateDiet.Apply(Combat, DietRules.NullTopLevelSections));
        Assert.False(top.TryGetProperty("shop", out _));
        Assert.True(top.TryGetProperty("combat", out _));
    }

    public static void MultiplayerAndOutOfCombatKeepTheirData()
    {
        var coop = Combat.Replace("\"multiplayer\":null", "\"multiplayer\":{\"is_multiplayer\":true,\"player_count\":2}")
            .Replace("\"players\":[{", "\"players\":[{\"player_id\":\"2\",\"is_local\":false,\"deck\":[]},{");
        var players = Parse(PromptStateDiet.Apply(coop)).GetProperty("run").GetProperty("players");
        Assert.True(players[1].TryGetProperty("deck", out _), "In co-op the party's inventories are facts, not copies.");

        var reward = """{"screen":"REWARD","in_combat":false,"combat":null,"run":{"piles":{"draw":[]}},"reward":{"rewards":[]}}""";
        var trimmed = Parse(PromptStateDiet.Apply(reward));
        Assert.True(trimmed.GetProperty("run").TryGetProperty("piles", out _), "No combat block, so the piles stay.");
        Assert.True(trimmed.TryGetProperty("reward", out _));
    }

    public static void BadInputPassesThrough()
    {
        Assert.Equal(string.Empty, PromptStateDiet.Apply(null));
        Assert.Equal("  ", PromptStateDiet.Apply("  "));
        Assert.Equal("[]", PromptStateDiet.Apply("[]"));
        Assert.Equal("{not json", PromptStateDiet.Apply("{not json"));
        Assert.Equal("null", PromptStateDiet.Apply("null"));
    }
}
