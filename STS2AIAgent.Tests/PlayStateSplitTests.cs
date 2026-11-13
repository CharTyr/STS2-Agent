using System.Text.Json;
using System.Text.Json.Nodes;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class PlayStateSplitTests
{
    private static string BuildBossCombatFrame(
        int turn,
        string hp = "52/80",
        int gold = 214,
        int penNibStack = 7,
        bool includePotion = true,
        string? extraDeckCard = null,
        string? extraGlossaryKey = null)
    {
        var deck = new JsonArray
        {
            new JsonObject { ["line"] = "打击+ [1] ×3" },
            new JsonObject { ["line"] = "防御+ [1] ×4" },
            new JsonObject { ["line"] = "痛击+ [2] ×1" },
            new JsonObject { ["line"] = "耸肩无视+ [1] ×3" },
            new JsonObject { ["line"] = "战斗专注 [0] ×2" },
            new JsonObject { ["line"] = "旋风斩+ [X] ×1" },
            new JsonObject { ["line"] = "恶魔形态 [3] ×1" },
            new JsonObject { ["line"] = "重刃+ [2] ×2" },
            new JsonObject { ["line"] = "缴械+ [1] ×2" },
            new JsonObject { ["line"] = "黑暗之拥+ [1] ×1" },
            new JsonObject { ["line"] = "无惧疼痛+ [1] ×2" },
            new JsonObject { ["line"] = "祭品+ [0] ×1" },
        };
        if (extraDeckCard != null)
        {
            deck.Add(new JsonObject { ["line"] = extraDeckCard });
        }

        var glossary = new JsonObject
        {
            ["格挡"] = "直到下一回合开始前，抵消等量伤害。",
            ["易伤"] = "受到来自攻击的伤害提高 50%。",
            ["虚弱"] = "攻击造成的伤害降低 25%。",
            ["力量"] = "每点力量使攻击伤害提高 1 点。",
            ["敏捷"] = "每点敏捷使从卡牌获得的格挡提高 1 点。",
            ["消耗"] = "打出后移出本场战斗。",
            ["虚无"] = "回合结束仍在手牌中时被消耗。",
            ["多层护甲"] = "回合结束时获得等量格挡，受未格挡伤害时减少 1 层。",
            ["荆棘"] = "受到攻击时对攻击者造成反伤。",
            ["人工制品"] = "免疫下一次受到的负面效果。",
            ["仪式"] = "每回合结束时获得等量力量。",
            ["激怒"] = "每当玩家打出一张技能牌时，获得力量。",
        };
        if (extraGlossaryKey != null)
        {
            glossary[extraGlossaryKey] = "倒计时结束时吞噬目标造成致命伤害。";
        }

        var potions = new JsonArray();
        if (includePotion)
        {
            potions.Add(new JsonObject
            {
                ["i"] = 0,
                ["potion_id"] = "STRENGTH_POTION",
                ["line"] = "力量药水",
                ["description"] = "获得 2 点力量。",
                ["usable"] = true,
                ["discard"] = true,
                ["targets"] = new JsonArray()
            });
        }

        var root = new JsonObject
        {
            ["version"] = 13,
            ["screen"] = "COMBAT",
            ["in_combat"] = true,
            ["run_id"] = "RUN_BOSS_234",
            ["turn"] = turn,
            ["available_actions"] = new JsonArray("play_card", "use_potion", "end_turn"),
            ["combat"] = new JsonObject
            {
                ["action_readiness"] = new JsonObject
                {
                    ["can_use_combat_actions"] = true,
                    ["reason"] = "ready",
                    ["local_turn_ready"] = true
                },
                ["threat"] = new JsonObject { ["incoming"] = 18 + turn * 2 },
                ["player"] = new JsonObject
                {
                    ["hp"] = hp,
                    ["block"] = turn * 3,
                    ["energy"] = 3,
                    ["powers"] = new JsonArray("力量 3")
                },
                ["hand"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["i"] = 0,
                        ["cid"] = 101 + turn,
                        ["card_id"] = "STRIKE",
                        ["type"] = "attack",
                        ["line"] = "打击+ [1] 造成12点伤害",
                        ["playable"] = true,
                        ["energy_cost"] = 1,
                        ["targets"] = new JsonArray(0)
                    },
                    new JsonObject
                    {
                        ["i"] = 1,
                        ["cid"] = 201 + turn,
                        ["card_id"] = "DEFEND",
                        ["type"] = "skill",
                        ["line"] = "防御+ [1] 获得8点格挡",
                        ["playable"] = true,
                        ["energy_cost"] = 1
                    }
                },
                ["draw"] = new JsonArray(new JsonObject { ["line"] = "重刃+ ×2" }),
                ["discard"] = new JsonArray(new JsonObject { ["line"] = "痛击+ ×1" }),
                ["exhaust"] = new JsonArray(),
                ["enemies"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["i"] = 0,
                        ["enemy_id"] = "ACT3_BOSS",
                        ["name"] = "终焉守卫",
                        ["hp"] = $"{320 - turn * 25}/320",
                        ["block"] = 0,
                        ["intent"] = "MEGA_SLAM",
                        ["move_id"] = "MEGA_SLAM",
                        ["alive"] = true,
                        ["hittable"] = true,
                        ["intents"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["i"] = 0,
                                ["intent_type"] = "Attack",
                                ["damage"] = 18 + turn * 2,
                                ["hits"] = 1,
                                ["total_damage"] = 18 + turn * 2
                            }
                        }
                    }
                }
            },
            ["run"] = new JsonObject
            {
                ["character"] = "铁甲战士",
                ["ascension"] = 15,
                ["ascension_effects"] = new JsonArray(
                    new JsonObject { ["level"] = 2, ["name"] = "普通敌人更致命", ["description"] = "普通敌人攻击力提升。" },
                    new JsonObject { ["level"] = 4, ["name"] = "Boss 更致命", ["description"] = "Boss 攻击力与机制强化。" },
                    new JsonObject { ["level"] = 9, ["name"] = "Boss 更肉", ["description"] = "Boss 最大生命值提升。" }),
                ["floor"] = 50,
                ["act_id"] = "ACT_3",
                ["boss_id"] = "ACT3_BOSS",
                ["hp"] = hp,
                ["gold"] = gold,
                ["max_energy"] = 4,
                ["base_orb_slots"] = 0,
                ["deck"] = deck,
                ["relics"] = new JsonArray("燃烧之血", "钢笔尖", "锚", "船夹板", "发条靴", "金刚杵", "奇异勺子", "香炉"),
                ["relic_ids"] = new JsonArray("BURNING_BLOOD", "PEN_NIB", "ANCHOR", "HORN_CLEAT", "BOOT", "VAJRA", "STRANGE_SPOON", "INCENSE_BURNER"),
                ["relic_stacks"] = new JsonArray(-1, penNibStack, -1, -1, -1, -1, -1, turn % 6),
                ["relic_descriptions"] = new JsonArray(
                    "战斗结束时回复 6 点生命。",
                    "每打出第 10 张攻击牌时伤害翻倍。",
                    "每场战斗开始时获得 10 点格挡。",
                    "第 2 回合开始时获得 14 点格挡。",
                    "每当造成 4 点或更少未格挡攻击伤害时将其提升至 5 点。",
                    "战斗开始时获得 1 点力量。",
                    "消耗牌打出时有 50% 几率不消耗而是丢入弃牌堆。",
                    "每 6 回合获得 1 层无实体。"),
                ["potions"] = potions
            },
            ["glossary"] = glossary
        };
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public static void HoistsStaticRunAndBaselineGlossaryIntoStableJson()
    {
        var cache = new PlayStateSplitCache();
        var dieted = PromptStateDiet.Apply(BuildBossCombatFrame(turn: 1));
        var split = cache.Split(dieted, "COMBAT");

        Assert.NotNull(split.StaticRunJson);
        using var staticDoc = JsonDocument.Parse(split.StaticRunJson!);
        using var dynamicDoc = JsonDocument.Parse(split.DynamicStateJson);

        var staticRoot = staticDoc.RootElement;
        Assert.Equal("RUN_BOSS_234", staticRoot.GetProperty("run_id").GetString());
        var staticRun = staticRoot.GetProperty("run");
        Assert.Equal("铁甲战士", staticRun.GetProperty("character").GetString());
        Assert.Equal(15, staticRun.GetProperty("ascension").GetInt32());
        Assert.Equal(12, staticRun.GetProperty("deck").GetArrayLength());
        Assert.Equal(8, staticRun.GetProperty("relics").GetArrayLength());
        Assert.Equal(8, staticRun.GetProperty("relic_descriptions").GetArrayLength());
        Assert.Equal(12, staticRoot.GetProperty("glossary").EnumerateObject().Count());

        var dynamicRoot = dynamicDoc.RootElement;
        var dynamicRun = dynamicRoot.GetProperty("run");
        Assert.False(dynamicRun.TryGetProperty("deck", out _), "deck must be hoisted out of volatile state");
        Assert.False(dynamicRun.TryGetProperty("relics", out _), "relics must be hoisted out of volatile state");
        Assert.False(dynamicRun.TryGetProperty("relic_descriptions", out _), "relic_descriptions must be hoisted out of volatile state");
        Assert.False(dynamicRoot.TryGetProperty("glossary", out _), "baseline glossary must be omitted from volatile state");
        Assert.Equal("52/80", dynamicRun.GetProperty("hp").GetString());
        Assert.Equal(8, dynamicRun.GetProperty("relic_stacks").GetArrayLength(), "live relic_stacks counters stay dynamic");
        Assert.Equal(1, dynamicRun.GetProperty("potions").GetArrayLength(), "live potions stay dynamic");
        Assert.True(split.DynamicStateJson.Length < dieted.Length * 0.50,
            $"Dynamic Boss frame should drop >50% of chars (dieted={dieted.Length}, dynamic={split.DynamicStateJson.Length}).");
    }

    public static void KeepsStaticJsonByteIdenticalAcrossBossCombatFramesAndTransientGlossary()
    {
        var cache = new PlayStateSplitCache();
        var turn1 = cache.Split(PromptStateDiet.Apply(BuildBossCombatFrame(turn: 1, hp: "52/80", penNibStack: 7, includePotion: true)), "COMBAT");
        var turn2 = cache.Split(PromptStateDiet.Apply(BuildBossCombatFrame(turn: 2, hp: "41/80", penNibStack: 9, includePotion: false, extraGlossaryKey: "砂坑")), "COMBAT");
        var turn3 = cache.Split(PromptStateDiet.Apply(BuildBossCombatFrame(turn: 3, hp: "33/80", penNibStack: 1, includePotion: false)), "COMBAT");

        Assert.Equal(turn1.StaticRunJson, turn2.StaticRunJson, "Static prefix must stay byte-for-byte identical when HP, potions, relic_stacks, and Boss keywords change.");
        Assert.Equal(turn1.StaticRunJson, turn3.StaticRunJson);

        using var turn2Dynamic = JsonDocument.Parse(turn2.DynamicStateJson);
        var deltaGlossary = turn2Dynamic.RootElement.GetProperty("glossary");
        Assert.Equal(1, deltaGlossary.EnumerateObject().Count(), "Only newly introduced combat-transient glossary keys stay in dynamic state.");
        Assert.True(deltaGlossary.TryGetProperty("砂坑", out _));

        using var turn3Dynamic = JsonDocument.Parse(turn3.DynamicStateJson);
        Assert.False(turn3Dynamic.RootElement.TryGetProperty("glossary", out _));
    }

    public static void InvalidatesCacheImmediatelyWhenDeckOrRelicsChange()
    {
        var cache = new PlayStateSplitCache();
        var before = cache.Split(PromptStateDiet.Apply(BuildBossCombatFrame(turn: 1)), "COMBAT");
        var afterDeckGain = cache.Split(PromptStateDiet.Apply(BuildBossCombatFrame(turn: 2, extraDeckCard: "腐化+ [2] ×1")), "COMBAT");

        Assert.NotNull(before.StaticRunJson);
        Assert.NotNull(afterDeckGain.StaticRunJson);
        Assert.True(!string.Equals(before.StaticRunJson, afterDeckGain.StaticRunJson, StringComparison.Ordinal),
            "Adding a card to run.deck must immediately refresh StaticRunJson.");
        Assert.Contains("腐化+", afterDeckGain.StaticRunJson!);
    }

    public static void MinimalOrInvalidSnapshotsPassThroughWithoutStaticMessage()
    {
        var cache = new PlayStateSplitCache();
        Assert.Null(cache.Split(null).StaticRunJson);
        Assert.Null(cache.Split("{broken").StaticRunJson);
        var minimal = """{"screen":"COMBAT","available_actions":["end_turn"],"combat":{"hand":[],"enemies":[{"i":0}]}}""";
        var result = cache.Split(minimal, "COMBAT");
        Assert.Null(result.StaticRunJson);
        Assert.Equal(minimal, result.DynamicStateJson);
    }

    public static async Task EndToEndAgentLoopPlacesStaticRunContextBeforeCompactionAndUserMessages()
    {
        var bridge = new BossSplitBridge(BuildBossCombatFrame(turn: 1, hp: "52/80", penNibStack: 7));
        var completions = new Queue<LlmCompletion>(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[] { new LlmToolCall { Id = "c1", Name = "act", ArgumentsJson = """{"action":"play_card","card_index":0,"target_index":0,"reason":"hit boss"}""" } }
            },
            new LlmCompletion
            {
                ToolCalls = new[] { new LlmToolCall { Id = "c2", Name = "act", ArgumentsJson = """{"action":"play_card","card_index":1,"reason":"block slam"}""" } }
            }
        });
        var requests = new List<LlmRequest>();
        var decisions = Enumerable.Range(1, 10)
            .Select(i => new DecisionLogEntry(i, $"t{i}", "agent", "play_card", $"turn {i}", $"fp{i}", 1, 1000, "RUN_BOSS_234"))
            .ToList();
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].ContextWindow = 10_000;
        var loop = new AgentLoop(
            bridge,
            new CapturingFactory(requests, completions),
            () => settings,
            recentDecisions: () => decisions,
            lastPromptTokens: () => 9_000);

        var r1 = await loop.PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", r1.Acted);

        decisions.Add(new DecisionLogEntry(11, "t11", "agent", "play_card", "hit boss", "fp11", 1, 1000, "RUN_BOSS_234"));
        bridge.StateJson = BuildBossCombatFrame(turn: 2, hp: "38/80", penNibStack: 8, includePotion: false, extraGlossaryKey: "砂坑");
        var r2 = await loop.PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", r2.Acted);

        Assert.Equal(2, requests.Count);
        var m1 = requests[0].Messages;
        var m2 = requests[1].Messages;
        var staticIdx1 = m1.ToList().FindIndex(m => (m.Content ?? string.Empty).StartsWith("Current run static context:\n", StringComparison.Ordinal));
        var compactionIdx1 = m1.ToList().FindIndex(m => (m.Content ?? string.Empty).Contains("Context compaction:", StringComparison.Ordinal));
        var stateIdx1 = m1.ToList().FindIndex(m => (m.Content ?? string.Empty).StartsWith("Latest compact game state:\n", StringComparison.Ordinal));

        Assert.True(staticIdx1 > 0, "Static run context message must be present.");
        Assert.Equal("system", m1[staticIdx1].Role);
        Assert.True(compactionIdx1 > staticIdx1, "Static run context must precede volatile ContextCompaction so prefix cache covers deck/relics/glossary.");
        Assert.True(stateIdx1 > compactionIdx1, "Dynamic compact state must follow ContextCompaction.");

        for (var i = 0; i <= staticIdx1; i++)
        {
            Assert.Equal(m1[i].Role, m2[i].Role);
            Assert.Equal(m1[i].Content, m2[i].Content, $"Prefix message {i} must stay byte-identical across Boss turns.");
        }

        var prefixChars = m2.Take(staticIdx1 + 1).Sum(m => (m.Content ?? string.Empty).Length);
        var totalChars = m2.Sum(m => (m.Content ?? string.Empty).Length);
        Assert.True((double)prefixChars / totalChars >= 0.72,
            $"Byte-stable prefix should cover >=72% of Boss prompt chars (prefix={prefixChars}, total={totalChars}).");
    }

    private sealed class CapturingFactory : ILlmClientFactory, ILlmClient
    {
        private readonly List<LlmRequest> _requests;
        private readonly Queue<LlmCompletion> _completions;

        public CapturingFactory(List<LlmRequest> requests, Queue<LlmCompletion> completions)
        {
            _requests = requests;
            _completions = completions;
        }

        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return Task.FromResult(_completions.Dequeue());
        }

        public Task<string> PingAsync(string model, CancellationToken cancellationToken) => Task.FromResult("pong");

        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class BossSplitBridge : IGameBridge
    {
        public string StateJson { get; set; }

        public BossSplitBridge(string stateJson) => StateJson = stateJson;

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(StateJson);

        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(StateJson);

        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) =>
            Task.FromResult("""{"available_actions":[{"action":"play_card","requires_card_index":true},{"action":"end_turn"}]}""");

        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken) =>
            Task.FromResult($"{{\"state\":{StateJson},\"available_actions\":[{{\"action\":\"play_card\",\"requires_card_index\":true}},{{\"action\":\"end_turn\"}}]}}");

        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("COMBAT");

        public Task<string> ActAsync(
            string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y,
            string? tool, CancellationToken cancellationToken, bool rawState = false, Action<string>? validateState = null, string? mode = null)
        {
            validateState?.Invoke(StateJson);
            return Task.FromResult($"{{\"action\":\"{action}\",\"status\":\"completed\",\"stable\":true,\"state\":{StateJson}}}");
        }

        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => Task.FromResult("{}");

        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("[]");

        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("[]");

        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }
}
