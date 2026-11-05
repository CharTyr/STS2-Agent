using System.Text.Json;
using System.Text.Json.Nodes;
using STS2AIAgent.Agent;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class McpStateSlimmerTests
{
    private static string BuildBloatedCombatState(
        string runId = "run_alpha",
        int extraCardCount = 0,
        string? extraRelicId = null,
        string? extraGlossaryTerm = null)
    {
        var deck = new JsonArray
        {
            new JsonObject { ["line"] = "打击*5 [1] 造成6点伤害。", ["card_ids"] = new JsonArray("STRIKE_IRONCLAD"), ["keywords"] = new JsonArray(), ["mods"] = new JsonArray() },
            new JsonObject { ["line"] = "防御*4 [1] 获得5点格挡。", ["card_ids"] = new JsonArray("DEFEND_IRONCLAD"), ["keywords"] = new JsonArray("格挡"), ["mods"] = new JsonArray() },
            new JsonObject { ["line"] = "痛击*1 [2] 造成8点伤害，给予2层易伤。", ["card_ids"] = new JsonArray("BASH"), ["keywords"] = new JsonArray("易伤"), ["mods"] = new JsonArray() },
            new JsonObject { ["line"] = "耸肩无视*2 [1] 获得8点格挡，抽1张牌。", ["card_ids"] = new JsonArray("SHRUG_IT_OFF"), ["keywords"] = new JsonArray("格挡"), ["mods"] = new JsonArray() },
            new JsonObject { ["line"] = "发炎*1 [1] 获得2点力量。", ["card_ids"] = new JsonArray("INFLAME"), ["keywords"] = new JsonArray("力量"), ["mods"] = new JsonArray() },
        };
        for (var i = 0; i < extraCardCount; i++)
        {
            deck.Add(new JsonObject
            {
                ["line"] = $"顺劈斩#{i} [1] 对所有敌人造成8点伤害。",
                ["card_ids"] = new JsonArray("CLEAVE"),
                ["keywords"] = new JsonArray(),
                ["mods"] = new JsonArray()
            });
        }

        var relics = new JsonArray("燃烧之血", "金刚杵", "锚");
        var relicIds = new JsonArray("BURNING_BLOOD", "VAJRA", "ANCHOR");
        var relicDescs = new JsonArray(
            "在战斗结束时，回复6点生命。",
            "在每场战斗开始时，获得1点力量。",
            "在每场战斗开始时，获得10点格挡。");
        if (extraRelicId is not null)
        {
            relics.Add("准备背包");
            relicIds.Add(extraRelicId);
            relicDescs.Add("在每场战斗开始时，额外抽2张牌。");
        }

        var mapNodes = new JsonArray();
        for (var floor = 1; floor <= 15; floor++)
        {
            mapNodes.Add(new JsonObject
            {
                ["x"] = floor % 4,
                ["y"] = floor,
                ["type"] = "MONSTER",
                ["children"] = new JsonArray(new JsonObject { ["x"] = 0, ["y"] = floor + 1 })
            });
        }

        var hand = new JsonArray();
        for (var i = 0; i < 6; i++)
        {
            hand.Add(new JsonObject
            {
                ["i"] = i,
                ["cid"] = 10 + i,
                ["card_id"] = i % 2 == 0 ? "STRIKE_IRONCLAD" : "DEFEND_IRONCLAD",
                ["line"] = i % 2 == 0 ? "打击 [1] 造成6点伤害。" : "防御 [1] 获得5点格挡。",
                ["playable"] = true,
                ["energy_cost"] = 1,
                ["type"] = i % 2 == 0 ? "Attack" : "Skill",
                ["can_play_result"] = true,
                ["why"] = null,
                ["unplayable_reason"] = null,
                ["unplayable_reason_raw"] = "",
                ["unplayable_preventer_id"] = null,
                ["unplayable_preventer_type"] = null,
                ["star_cost"] = null,
                ["target"] = i % 2 == 0 ? "enemy" : null,
                ["targets"] = i % 2 == 0 ? new JsonArray(0) : new JsonArray(),
                ["afflictions"] = new JsonArray(),
                ["enchantments"] = new JsonArray(),
            });
        }

        var glossary = new JsonObject
        {
            ["格挡"] = "直到下回合开始前，抵挡等量的伤害。",
            ["易伤"] = "受到来自攻击的伤害增加50%。",
            ["力量"] = "每点力量使攻击伤害增加1点。",
            ["虚弱"] = "攻击造成的伤害减少25%。",
        };
        if (extraGlossaryTerm is not null)
        {
            glossary[extraGlossaryTerm] = "通过卡牌获得的格挡减少25%。";
        }

        var state = new JsonObject
        {
            ["version"] = 13,
            ["compact_agent_view"] = true,
            ["screen"] = "COMBAT",
            ["in_combat"] = true,
            ["run_id"] = runId,
            ["turn"] = 2,
            ["available_actions"] = new JsonArray("play_card", "end_turn"),
            ["shop"] = null,
            ["event"] = null,
            ["reward"] = null,
            ["rest"] = null,
            ["chest"] = null,
            ["selection"] = null,
            ["bundles"] = null,
            ["capstone"] = null,
            ["crystal_sphere"] = null,
            ["map"] = new JsonObject
            {
                ["open"] = false,
                ["current"] = "0,4",
                ["options"] = new JsonArray(),
                ["nodes"] = mapNodes,
                ["boss"] = "BOSS_SLIME",
            },
            ["run"] = new JsonObject
            {
                ["character"] = "铁甲战士",
                ["ascension"] = 10,
                ["ascension_effects"] = new JsonArray(
                    new JsonObject { ["level"] = 1, ["name"] = "精英增多", ["description"] = "地图上出现更多精英。" },
                    new JsonObject { ["level"] = 2, ["name"] = "敌人更致命", ["description"] = "普通敌人伤害更高。" }),
                ["floor"] = 5,
                ["hp"] = "64/80",
                ["gold"] = 123,
                ["deck"] = deck,
                ["relics"] = relics,
                ["relic_ids"] = relicIds,
                ["relic_stacks"] = new JsonArray(0, 1, 0),
                ["relic_descriptions"] = relicDescs,
                ["players"] = new JsonArray(
                    new JsonObject
                    {
                        ["player_id"] = "1",
                        ["is_local"] = true,
                        ["current_hp"] = 64,
                        ["max_hp"] = 80,
                        ["gold"] = 123,
                        ["deck"] = deck.DeepClone(),
                        ["relics"] = relics.DeepClone(),
                        ["potions"] = new JsonArray(),
                    }),
                ["piles"] = new JsonObject
                {
                    ["draw"] = deck.DeepClone(),
                    ["discard"] = deck.DeepClone(),
                    ["exhaust"] = new JsonArray(),
                },
            },
            ["combat"] = new JsonObject
            {
                ["energy"] = "3/3",
                ["card_value_note"] = PromptStateDiet.CardValueNote,
                ["action_readiness"] = new JsonObject
                {
                    ["can_use_combat_actions"] = true,
                    ["reason"] = "ok",
                    ["local_turn_ready"] = true,
                    ["debug_noise_flag_1"] = false,
                    ["debug_noise_flag_2"] = "none",
                    ["debug_noise_flag_3"] = 0,
                },
                ["hand"] = hand,
                ["enemies"] = new JsonArray(
                    new JsonObject { ["i"] = 0, ["enemy_id"] = "JAW_WORM", ["hp"] = "42/42", ["alive"] = true, ["hittable"] = true }),
            },
            ["glossary"] = glossary,
        };

        return state.ToJsonString(McpStateSlimmer.CompactJson);
    }

    public static void IntraFrameAndSessionDedupSlashPayloadAndTrackMutations()
    {
        var rawCompact = BuildBloatedCombatState();
        var dedup = new McpSessionDedup();

        var first = dedup.ApplyToStateJson("sess-1", rawCompact, fullState: false);
        Assert.True(
            first.Length <= rawCompact.Length * 0.70,
            $"First call should shrink bloated compact state by >=30% ({first.Length} vs {rawCompact.Length}).");

        using (var doc1 = JsonDocument.Parse(first))
        {
            var root1 = doc1.RootElement;
            Assert.False(root1.TryGetProperty("shop", out _), "Null top-level sections dropped.");
            Assert.False(root1.GetProperty("map").TryGetProperty("nodes", out _), "Off-MAP graph nodes dropped.");
            var run1 = root1.GetProperty("run");
            Assert.False(run1.TryGetProperty("piles", out _), "Duplicate run.piles dropped in combat.");
            Assert.False(run1.GetProperty("players")[0].TryGetProperty("deck", out _), "Solo player duplicate deck dropped.");
            Assert.True(run1.TryGetProperty("deck", out _), "First call retains full run.deck.");
            Assert.True(run1.TryGetProperty("relic_descriptions", out _), "First call retains relic_descriptions.");
            Assert.True(root1.TryGetProperty("glossary", out _), "First call retains glossary.");
            Assert.True(root1.GetProperty("combat").TryGetProperty("card_value_note", out _), "First call retains card_value_note.");
        }

        var second = dedup.ApplyToStateJson("sess-1", rawCompact, fullState: false);
        Assert.True(
            second.Length <= rawCompact.Length * 0.42,
            $"Second combat call in same session/run should shrink by >=58% ({second.Length} vs {rawCompact.Length}).");

        using (var doc2 = JsonDocument.Parse(second))
        {
            var root2 = doc2.RootElement;
            var run2 = root2.GetProperty("run");
            Assert.False(run2.TryGetProperty("deck", out _));
            Assert.True(run2.GetProperty("deck_unchanged").GetBoolean());
            Assert.Equal(5, run2.GetProperty("deck_groups").GetInt32());
            Assert.False(run2.TryGetProperty("relic_descriptions", out _));
            Assert.True(run2.GetProperty("relic_descriptions_unchanged").GetBoolean());
            Assert.True(run2.TryGetProperty("relics", out _), "Lightweight relic names stay visible.");
            Assert.True(run2.TryGetProperty("relic_stacks", out _), "Dynamic relic_stacks stay visible.");
            Assert.False(run2.TryGetProperty("ascension_effects", out _));
            Assert.True(run2.GetProperty("ascension_effects_unchanged").GetBoolean());
            Assert.False(root2.TryGetProperty("glossary", out _));
            Assert.True(root2.GetProperty("glossary_unchanged").GetBoolean());
            Assert.False(root2.GetProperty("combat").TryGetProperty("card_value_note", out _));
        }

        // Mutate deck, add a relic, and introduce one new glossary keyword: all three must re-emit!
        var mutated = BuildBloatedCombatState(extraCardCount: 1, extraRelicId: "BAG_OF_PREP", extraGlossaryTerm: "脆弱");
        var third = dedup.ApplyToStateJson("sess-1", mutated, fullState: false);
        using (var doc3 = JsonDocument.Parse(third))
        {
            var root3 = doc3.RootElement;
            var run3 = root3.GetProperty("run");
            Assert.Equal(6, run3.GetProperty("deck").GetArrayLength(), "Mutated deck must re-emit immediately.");
            Assert.Equal(4, run3.GetProperty("relic_descriptions").GetArrayLength(), "Mutated relic descriptions must re-emit.");
            var glossaryDelta = root3.GetProperty("glossary");
            Assert.True(root3.GetProperty("glossary_delta").GetBoolean(), "Only newly encountered glossary entry is sent as delta.");
            Assert.Equal(1, glossaryDelta.EnumerateObject().Count());
            Assert.True(glossaryDelta.TryGetProperty("脆弱", out _));
        }

        // Explicit full_state=true restores all static blocks and refreshes baseline.
        var forcedFull = dedup.ApplyToStateJson("sess-1", mutated, fullState: true);
        using (var docFull = JsonDocument.Parse(forcedFull))
        {
            var rootFull = docFull.RootElement;
            Assert.True(rootFull.GetProperty("run").TryGetProperty("deck", out _));
            Assert.True(rootFull.GetProperty("run").TryGetProperty("relic_descriptions", out _));
            Assert.Equal(5, rootFull.GetProperty("glossary").EnumerateObject().Count());
            Assert.False(rootFull.TryGetProperty("glossary_delta", out _));
        }

        // Switching run_id automatically resets static baselines.
        var newRun = dedup.ApplyToStateJson("sess-1", BuildBloatedCombatState(runId: "run_beta"), fullState: false);
        using (var docNewRun = JsonDocument.Parse(newRun))
        {
            Assert.True(docNewRun.RootElement.GetProperty("run").TryGetProperty("deck", out _));
            Assert.True(docNewRun.RootElement.TryGetProperty("glossary", out _));
        }
    }

    public static async Task NativeServer_FullStateDecideGuidanceAndPlanSteps()
    {
        var compact = BuildBloatedCombatState();
        var bridge = new SlimMcpBridge(compact);
        var server = new NativeMcpServer(bridge, () => new { status = "ok" }, "0.17.0");
        server.SetEnabled(true, "http://127.0.0.1:8080/mcp");

        var init = await server.ProcessAsync(
            "POST",
            "application/json",
            null,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""",
            CancellationToken.None);
        var session = init.SessionId!;

        // 1st decide(): full state + full scene_guidance
        var decide1 = await CallToolAsync(server, session, "decide", "{}");
        Assert.True(decide1.GetProperty("state").GetProperty("run").TryGetProperty("deck", out _));
        Assert.True(decide1.GetProperty("scene_guidance").TryGetProperty("playbook", out _));

        // 2nd decide() on same screen: state static blocks + scene_guidance deduplicated
        var decide2 = await CallToolAsync(server, session, "decide", "{}");
        Assert.True(decide2.GetProperty("state").GetProperty("run").GetProperty("deck_unchanged").GetBoolean());
        Assert.True(decide2.GetProperty("scene_guidance").GetProperty("guidance_unchanged").GetBoolean());

        // get_game_state(full_state=true): re-sends full static blocks
        var fullRead = await CallToolAsync(server, session, "get_game_state", """{"full_state":true}""");
        Assert.True(fullRead.GetProperty("run").TryGetProperty("deck", out _));
        Assert.True(fullRead.TryGetProperty("glossary", out _));

        // act with then=[...]: strips intermediate step states and keeps single deduplicated final_state
        var planAct = await CallToolAsync(
            server,
            session,
            "act",
            """{"action":"play_card","card_cid":10,"target_index":0,"then":[{"action":"play_card","card_cid":11},{"action":"end_turn"}]}""");
        var steps = planAct.GetProperty("plan").GetProperty("steps");
        Assert.Equal(3, steps.GetArrayLength());
        foreach (var step in steps.EnumerateArray())
        {
            Assert.False(step.TryGetProperty("state", out _), "Intermediate plan.steps must not duplicate full state.");
        }
        Assert.False(planAct.TryGetProperty("state", out _), "Stale step-0 state omitted when final_state is present.");
        Assert.True(planAct.GetProperty("final_state").GetProperty("run").GetProperty("deck_unchanged").GetBoolean());
        Assert.Equal(126, planAct.GetProperty("final_state").GetProperty("run").GetProperty("gold").GetInt32());

        // DELETE /mcp resets session deduplication
        await server.ProcessAsync("DELETE", "application/json", session, null, CancellationToken.None);
        var afterDelete = await CallToolAsync(server, session, "get_game_state", "{}");
        Assert.True(afterDelete.GetProperty("run").TryGetProperty("deck", out _), "DELETE /mcp must clear session dedup.");
    }

    private static async Task<JsonElement> CallToolAsync(NativeMcpServer server, string session, string name, string argsJson)
    {
        var body = $"{{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{{\"name\":\"{name}\",\"arguments\":{argsJson}}}}}";
        var res = await server.ProcessAsync("POST", "application/json", session, body, CancellationToken.None);
        using var outer = JsonDocument.Parse(res.Body!);
        var text = outer.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        using var inner = JsonDocument.Parse(text);
        return inner.RootElement.Clone();
    }

    private sealed class SlimMcpBridge : IGameBridge
    {
        private string _compact;
        private int _gold = 123;

        public SlimMcpBridge(string compact) => _compact = compact;

        public Task<string?> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("COMBAT");
        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(_compact);
        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(_compact);
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) =>
            Task.FromResult("""[{"name":"play_card","requires_index":true,"requires_target":false},{"name":"end_turn","requires_index":false,"requires_target":false}]""");
        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"{{\"state\":{_compact},\"available_actions\":[{{\"name\":\"play_card\",\"requires_index\":true,\"requires_target\":false}},{{\"name\":\"end_turn\",\"requires_index\":false,\"requires_target\":false}}]}}");
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<(bool Actionable, string CompactJson)> WaitUntilActionableWithStateAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult((true, _compact));
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y, string? tool, CancellationToken cancellationToken, bool rawState = false, Action<string>? validateState = null)
        {
            validateState?.Invoke(_compact);
            _gold++;
            var node = System.Text.Json.Nodes.JsonNode.Parse(_compact)!.AsObject();
            node["run"]!["gold"] = _gold;
            _compact = node.ToJsonString(McpStateSlimmer.CompactJson);
            return Task.FromResult($"{{\"action\":\"{action}\",\"status\":\"completed\",\"stable\":true,\"state\":{_compact}}}");
        }
        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string>? itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }
}
