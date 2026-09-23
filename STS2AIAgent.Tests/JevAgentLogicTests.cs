using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// The pure decision logic the two-layer engine introduces: the strategy value type and store, the
/// decider seam, the act-argument serialization, and the option enumerator that fills each frame's
/// action descriptor with the concrete indices Jev would pick. No game, no Godot, no provider -- the
/// fixtures are hand-written snapshots and the assertions read the exact JSON shapes the bridge and the
/// act handler use.
/// </summary>
internal static class JevAgentLogicTests
{
    // ---- PlayStrategy -------------------------------------------------------

    /// <summary>
    /// A strategy written to JSON and read back has to keep every field, including the macro goal, the
    /// screen/round the plan was written for, and the per-option hints, or a planner's guidance silently
    /// degrades across a reload.
    /// </summary>
    public static void PlayStrategy_RoundTripsEveryField()
    {
        var strategy = new PlayStrategy
        {
            Posture = "aggressive",
            Goal = "kill the weakest enemy before it acts",
            Instructions = "go for lethal on the Guy",
            OptionHints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // The planner's contract: hints are keyed by option KIND; the executor re-keys them.
                ["play_card"] = "prefer this strike",
                ["end_turn"] = "only as a last resort"
            },
            PlanScreen = "COMBAT",
            PlanRound = 3,
            UpdatedAt = "2026-10-02T12:00:00Z",
            Source = "llm"
        };

        var parsed = PlayStrategy.TryParse(strategy.ToJson());
        Assert.NotNull(parsed);
        AssertSameStrategy(strategy, parsed!);
    }

    /// <summary>
    /// A JSON that only names some fields keeps a default for the rest rather than throwing or
    /// clearing the ones it did not mention -- a partial MCP plan must not wipe the posture.
    /// </summary>
    public static void PlayStrategy_TryParseToleratesMissingFields()
    {
        var parsed = PlayStrategy.TryParse("""{"posture":"defensive"}""");
        Assert.NotNull(parsed);
        Assert.Equal("defensive", parsed!.Posture);
        Assert.Equal(string.Empty, parsed.Instructions);
        Assert.Equal(string.Empty, parsed.Goal);
        Assert.Equal(string.Empty, parsed.PlanScreen);
        Assert.Null(parsed.PlanRound);
        Assert.Equal(PlayStrategy.DefaultSource, parsed.Source);
        Assert.Equal(PlayStrategy.DefaultTimestamp, parsed.UpdatedAt);
        Assert.Equal(0, parsed.OptionHints.Count);

        // A plan that records the frame it was written for keeps that scope; a bad round is absent.
        var scoped = PlayStrategy.TryParse("""{"goal":"hold the line","plan_screen":"COMBAT","plan_round":4}""");
        Assert.Equal("hold the line", scoped!.Goal);
        Assert.Equal("COMBAT", scoped.PlanScreen);
        Assert.Equal(4, scoped.PlanRound!.Value);
        Assert.Null(PlayStrategy.TryParse("""{"plan_round":-1}""")!.PlanRound);
        Assert.Null(PlayStrategy.TryParse("""{"plan_round":"3"}""")!.PlanRound);

        var empty = PlayStrategy.TryParse("""{}""");
        Assert.NotNull(empty);
        Assert.Equal("balanced", empty!.Posture);

        Assert.Null(PlayStrategy.TryParse("not json"));
        Assert.Null(PlayStrategy.TryParse(""));
        Assert.Null(PlayStrategy.TryParse("[]"));
        Assert.Null(PlayStrategy.TryParse("null"));
    }

    public static void PlayStrategy_DefaultIsBalancedAndMarksItsSource()
    {
        Assert.Equal("balanced", PlayStrategy.Default.Posture);
        Assert.Equal(PlayStrategy.DefaultSource, PlayStrategy.Default.Source);
        Assert.Equal(string.Empty, PlayStrategy.Default.Instructions);
        Assert.Equal(string.Empty, PlayStrategy.Default.Goal);
        Assert.Equal(string.Empty, PlayStrategy.Default.PlanScreen);
        Assert.Null(PlayStrategy.Default.PlanRound);
        Assert.Equal(0, PlayStrategy.Default.OptionHints.Count);
    }

    // ---- StrategyStore ------------------------------------------------------

    public static void StrategyStore_StartsAtDefault()
    {
        var store = new StrategyStore();
        Assert.Equal("balanced", store.Current.Posture);
        Assert.Equal(PlayStrategy.DefaultSource, store.Current.Source);
    }

    /// <summary>
    /// A planner thread writes while decider threads read. Every read must see a whole, non-null
    /// strategy -- the lock is what makes the swap atomic -- and the final update must be visible.
    /// </summary>
    public static void StrategyStore_UpdateIsVisibleUnderConcurrency()
    {
        var store = new StrategyStore();
        var writers = Enumerable.Range(0, 8)
            .Select(i => new PlayStrategy { Posture = "p" + i, Source = "llm" })
            .ToArray();

        var seenNulls = 0;
        var producers = writers.Select(strategy => Task.Run(() =>
        {
            for (var round = 0; round < 2000; round++)
            {
                store.Update(strategy);
            }
        })).ToArray();

        var consumers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 2000; round++)
            {
                if (store.Current is null)
                {
                    Interlocked.Increment(ref seenNulls);
                }
            }
        })).ToArray();

        Task.WaitAll(producers.Concat(consumers).ToArray());
        Assert.Equal(0, seenNulls);

        store.Update(new PlayStrategy { Posture = "final", Source = "mcp" });
        Assert.Equal("final", store.Current.Posture);
        Assert.Equal("mcp", store.Current.Source);
    }

    // ---- IActionDecider -----------------------------------------------------

    /// <summary>
    /// The decider seam is a contract, not a base class: a minimal implementation returns the decision
    /// it was handed, which is the shape the orchestrator will act on.
    /// </summary>
    public static void ActionDecider_ReturnsItsDecision()
    {
        var expected = new ExecutionDecision
        {
            Action = "play_card",
            CardIndex = 0,
            TargetIndex = 1,
            Reason = "kill the slime"
        };

        var decision = new StubDecider(expected)
            .DecideAsync("{}", PlayStrategy.Default, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.Equal("play_card", decision.Action);
        Assert.Equal(0, decision.CardIndex);
        Assert.Equal(1, decision.TargetIndex);
        Assert.Equal("kill the slime", decision.Reason);
    }

    // ---- ExecutionDecision --------------------------------------------------

    /// <summary>
    /// The act arguments must be the exact object <see cref="ActJsonParser"/> and the act handler read,
    /// with unused parameters omitted so a null never reaches a handler that wanted an integer.
    /// </summary>
    public static void ExecutionDecision_ToActArgumentsJsonMatchesActShape()
    {
        var decision = new ExecutionDecision
        {
            Action = "play_card",
            CardIndex = 0,
            TargetIndex = 1,
            Reason = "hit hardest target"
        };

        var json = decision.ToActArgumentsJson();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("play_card", root.GetProperty("action").GetString());
        Assert.Equal(0, root.GetProperty("card_index").GetInt32());
        Assert.Equal(1, root.GetProperty("target_index").GetInt32());
        Assert.Equal("hit hardest target", root.GetProperty("reason").GetString());
        Assert.False(root.TryGetProperty("option_index", out _));
        Assert.False(root.TryGetProperty("x", out _));
        Assert.False(root.TryGetProperty("tool", out _));

        // The same object ActJsonParser.TryParse accepts as a fallback act.
        Assert.True(ActJsonParser.TryParse(json, out var parsed));
        Assert.Contains("\"action\":\"play_card\"", parsed, StringComparison.Ordinal);
    }

    public static void ExecutionDecision_OptionIndexDecisionCarriesItsIndex()
    {
        var decision = new ExecutionDecision { Action = "choose_map_node", OptionIndex = 2 };
        using var document = JsonDocument.Parse(decision.ToActArgumentsJson());
        var root = document.RootElement;
        Assert.Equal("choose_map_node", root.GetProperty("action").GetString());
        Assert.Equal(2, root.GetProperty("option_index").GetInt32());
        Assert.False(root.TryGetProperty("card_index", out _));
    }

    public static void ExecutionDecision_EmptyDecisionSerializesToEmptyObject()
    {
        Assert.Equal("{}", new ExecutionDecision().ToActArgumentsJson());
    }

    // ---- JevOptionEnumerator: COMBAT ---------------------------------------

    /// <summary>
    /// A combat frame expands to one option per playable card per legal target, plus end_turn, and the
    /// card plays come first so the 255-cap keeps them over the fallback actions.
    /// </summary>
    public static void Enumerate_CombatCardsTimesTargetsAndEndTurn()
    {
        var options = JevOptionEnumerator.Enumerate(CombatSnapshot);
        var ids = options.Select(option => option.Id).ToList();

        Assert.True(ids.Contains("play_card:0->0"), "strike on the first target");
        Assert.True(ids.Contains("play_card:0->1"), "strike on the second target");
        Assert.True(ids.Contains("play_card:1"), "a no-target card is a single option");
        Assert.True(ids.Contains("end_turn"));

        var firstPlay = ids.FindIndex(id => id.StartsWith("play_card:", StringComparison.Ordinal));
        var endTurn = ids.IndexOf("end_turn");
        Assert.True(firstPlay >= 0 && firstPlay < endTurn, "card plays precede end_turn");

        Assert.False(ids.Any(id => id.StartsWith("play_card:2", StringComparison.Ordinal)), "an unplayable card yields no option");
        Assert.False(ids.Any(id => id.StartsWith("use_potion:1", StringComparison.Ordinal)), "an empty potion slot yields no option");

        var strike = options.First(option => option.Id == "play_card:0->0");
        Assert.Equal("play_card", strike.Action);
        Assert.Equal(0, strike.CardIndex);
        Assert.Equal(0, strike.TargetIndex);
        Assert.True(strike.Description.Contains("Louse", StringComparison.Ordinal), strike.Description);
    }

    // ---- JevOptionEnumerator: MAP ------------------------------------------

    public static void Enumerate_MapNodeOptions()
    {
        var options = JevOptionEnumerator.Enumerate(MapSnapshot);
        Assert.Equal(2, options.Count);
        var first = options.First(o => o.Id == "choose_map_node:0");
        Assert.Equal("choose_map_node", first.Action);
        Assert.Equal(0, first.OptionIndex);
        var elite = options.First(o => o.Id == "choose_map_node:2");
        Assert.Equal(2, elite.OptionIndex);
    }

    // ---- JevOptionEnumerator: indexed + targets + locks ---------------------

    /// <summary>
    /// A REST frame expands each option even though its descriptor does not set requires_index, and a
    /// targeted rest option grows into one option per target; a locked EVENT option is dropped.
    /// </summary>
    public static void Enumerate_IndexedOptionsExpandTargetsAndSkipLocks()
    {
        var restIds = JevOptionEnumerator.Enumerate(RestSnapshot).Select(o => o.Id).ToList();
        Assert.True(restIds.Contains("choose_rest_option:0"), "an untargeted rest option stays single");
        Assert.True(restIds.Contains("choose_rest_option:1->3"), "a targeted rest option expands");
        Assert.True(restIds.Contains("choose_rest_option:1->4"));

        var eventOptions = JevOptionEnumerator.Enumerate(EventSnapshot);
        Assert.Single(eventOptions);
        Assert.Equal("choose_event_option:1", eventOptions[0].Id);
    }

    // ---- JevOptionEnumerator: REWARD ----------------------------------------

    /// <summary>
    /// A reward frame with a pending card choice expands resolve_rewards into one option per card
    /// plus an explicit skip, so the execution model makes the deck-building decision instead of the
    /// game side silently taking the first card.
    /// </summary>
    public static void Enumerate_RewardCardChoiceExpandsResolveRewards()
    {
        var options = JevOptionEnumerator.Enumerate(RewardSnapshot);
        var ids = options.Select(option => option.Id).ToList();

        Assert.True(ids.Contains("resolve_rewards:0"), "first card is its own option");
        Assert.True(ids.Contains("resolve_rewards:1"), "second card is its own option");
        Assert.True(ids.Contains("resolve_rewards:skip"), "an explicit skip option exists");
        Assert.False(ids.Contains("resolve_rewards"), "the bare macro is gone while cards are pending");

        var take = options.First(option => option.Id == "resolve_rewards:1");
        Assert.Equal("resolve_rewards", take.Action);
        Assert.Equal(1, take.OptionIndex);
        Assert.True(take.Description.Contains("Demon Form", StringComparison.Ordinal), take.Description);

        var skip = options.First(option => option.Id == "resolve_rewards:skip");
        Assert.Equal(-1, skip.OptionIndex);
    }

    /// <summary>With no card choice pending, resolve_rewards stays a single bare cleanup option.</summary>
    public static void Enumerate_RewardWithoutCardsKeepsBareMacro()
    {
        var options = JevOptionEnumerator.Enumerate(RewardNoCardSnapshot);
        var ids = options.Select(option => option.Id).ToList();

        Assert.True(ids.Contains("resolve_rewards"), "no card choice means plain cleanup");
        Assert.False(ids.Any(id => id.StartsWith("resolve_rewards:", StringComparison.Ordinal)),
            "no per-card options without a pending choice");
    }

    // ---- JevOptionEnumerator: no-arg fallback -------------------------------

    public static void Enumerate_NoArgFallbackScreenYieldsSingleOptions()
    {
        var options = JevOptionEnumerator.Enumerate(ChestSnapshot);
        Assert.Single(options);
        Assert.Equal("open_chest", options[0].Id);
        Assert.Equal("open_chest", options[0].Action);
        Assert.Null(options[0].OptionIndex);
        Assert.Null(options[0].CardIndex);
        Assert.Null(options[0].TargetIndex);
    }

    // ---- JevOptionEnumerator: 255 cap ---------------------------------------

    /// <summary>
    /// A frame with more than 255 card-target combinations is capped, and every surviving option is a
    /// combat card play -- they were added first, so truncation drops later choices, not the plays.
    /// </summary>
    public static void Enumerate_CapsAt255WithCardPlaysFirst()
    {
        var options = JevOptionEnumerator.Enumerate(ManyTargetsSnapshot());
        Assert.Equal(255, options.Count);
        Assert.True(options.All(option => option.Action == "play_card"), "only card plays survive the cap");
        Assert.True(options.All(option => option.CardIndex.HasValue), "every survivor names a card");
    }

    // ---- JevOptionEnumerator: defensive -------------------------------------

    /// <summary>
    /// Malformed or half-written snapshots must degrade to "nothing to act on" rather than throw out of
    /// the decision loop.
    /// </summary>
    public static void Enumerate_MalformedInputDoesNotThrow()
    {
        Assert.Equal(0, JevOptionEnumerator.Enumerate("").Count);
        Assert.Equal(0, JevOptionEnumerator.Enumerate("not json").Count);
        Assert.Equal(0, JevOptionEnumerator.Enumerate("{").Count);
        Assert.Equal(0, JevOptionEnumerator.Enumerate("[]").Count);
        Assert.Equal(0, JevOptionEnumerator.Enumerate("42").Count);
        Assert.Equal(0, JevOptionEnumerator.Enumerate("{}").Count);
        Assert.Equal(0, JevOptionEnumerator.Enumerate("""{"state":{},"available_actions":"nope"}""").Count);
        Assert.Equal(0, JevOptionEnumerator.Enumerate("""{"state":{"screen":"COMBAT"},"available_actions":[{"name":"play_card"}]}""").Count);
    }

    // ---- helpers ------------------------------------------------------------

    private static void AssertSameStrategy(PlayStrategy expected, PlayStrategy actual)
    {
        Assert.Equal(expected.Posture, actual.Posture);
        Assert.Equal(expected.Goal, actual.Goal);
        Assert.Equal(expected.Instructions, actual.Instructions);
        Assert.Equal(expected.PlanScreen, actual.PlanScreen);
        Assert.Equal(expected.PlanRound, actual.PlanRound);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(expected.OptionHints.Count, actual.OptionHints.Count);
        foreach (var (key, value) in expected.OptionHints)
        {
            Assert.True(actual.OptionHints.TryGetValue(key, out var read) && read == value, $"hint '{key}' must round-trip");
        }
    }

    // ---- fixtures -----------------------------------------------------------

    private const string CombatSnapshot = """
{
  "state": {
    "screen": "COMBAT",
    "available_actions": ["end_turn", "play_card", "use_potion"],
    "combat": {
      "hand": [
        {"i": 0, "line": "Strike", "playable": true, "targets": [0, 1]},
        {"i": 1, "line": "Defend", "playable": true, "targets": []},
        {"i": 2, "line": "Bash", "playable": false, "targets": [0]}
      ],
      "enemies": [
        {"i": 0, "name": "Louse", "alive": true},
        {"i": 1, "name": "Slime", "alive": true}
      ]
    },
    "run": {
      "potions": [
        {"i": 0, "line": "Fire Potion", "usable": true, "targets": [0, 1]},
        {"i": 1, "line": "empty", "usable": false, "targets": []}
      ]
    }
  },
  "available_actions": [
    {"name": "end_turn", "requires_index": false, "requires_target": false},
    {"name": "play_card", "requires_index": true, "requires_target": false},
    {"name": "use_potion", "requires_index": true, "requires_target": false}
  ]
}
""";

    private const string MapSnapshot = """
{
  "state": {
    "screen": "MAP",
    "available_actions": ["choose_map_node"],
    "map": {
      "options": [
        {"i": 0, "line": "Monster (0,1)"},
        {"i": 2, "line": "Elite (0,2)"}
      ]
    }
  },
  "available_actions": [
    {"name": "choose_map_node", "requires_index": true, "requires_target": false}
  ]
}
""";

    private const string RestSnapshot = """
{
  "state": {
    "screen": "REST",
    "available_actions": ["choose_rest_option"],
    "rest": {
      "options": [
        {"i": 0, "line": "Rest [HP]", "requires_target": false, "valid_target_indices": []},
        {"i": 1, "line": "Smith", "requires_target": true, "valid_target_indices": [3, 4]}
      ]
    }
  },
  "available_actions": [
    {"name": "choose_rest_option", "requires_index": false, "requires_target": false}
  ]
}
""";

    private const string EventSnapshot = """
{
  "state": {
    "screen": "EVENT",
    "available_actions": ["choose_event_option"],
    "event": {
      "options": [
        {"i": 0, "line": "Take the gold", "locked": true},
        {"i": 1, "line": "Leave", "locked": false}
      ]
    }
  },
  "available_actions": [
    {"name": "choose_event_option", "requires_index": true, "requires_target": false}
  ]
}
""";

    private const string ChestSnapshot = """
{
  "state": {
    "screen": "CHEST",
    "available_actions": ["open_chest"]
  },
  "available_actions": [
    {"name": "open_chest", "requires_index": false, "requires_target": false}
  ]
}
""";

    private const string RewardSnapshot = """
{
  "state": {
    "screen": "REWARD",
    "available_actions": ["resolve_rewards", "choose_reward_card", "skip_reward_cards"],
    "reward": {
      "pending_card_choice": true,
      "can_proceed": false,
      "rewards": [
        {"i": 0, "line": "card: Choose a card", "claimable": true}
      ],
      "cards": [
        {"i": 0, "line": "Strike"},
        {"i": 1, "line": "Demon Form"}
      ],
      "alternatives": []
    }
  },
  "available_actions": [
    {"name": "resolve_rewards", "requires_index": false, "requires_target": false},
    {"name": "choose_reward_card", "requires_index": true, "requires_target": false},
    {"name": "skip_reward_cards", "requires_index": false, "requires_target": false}
  ]
}
""";

    private const string RewardNoCardSnapshot = """
{
  "state": {
    "screen": "REWARD",
    "available_actions": ["resolve_rewards"],
    "reward": {
      "pending_card_choice": false,
      "can_proceed": true,
      "rewards": [
        {"i": 0, "line": "gold: 99", "claimable": true}
      ],
      "cards": [],
      "alternatives": []
    }
  },
  "available_actions": [
    {"name": "resolve_rewards", "requires_index": false, "requires_target": false}
  ]
}
""";

    private static string ManyTargetsSnapshot()
    {
        // 13 playable cards x 20 targets each = 260, past the 255 cap, all combat plays.
        var hand = new List<string>();
        for (var card = 0; card < 13; card++)
        {
            var targets = string.Join(", ", Enumerable.Range(0, 20));
            hand.Add($"{{\"i\": {card}, \"line\": \"Card{card}\", \"playable\": true, \"targets\": [{targets}]}}");
        }

        return
            "{\"state\":{\"screen\":\"COMBAT\",\"combat\":{\"hand\":[" +
            string.Join(",", hand) +
            "]}},\"available_actions\":[{\"name\":\"play_card\",\"requires_index\":true}]}";
    }

    private sealed class StubDecider : IActionDecider
    {
        private readonly ExecutionDecision _decision;

        public StubDecider(ExecutionDecision decision)
        {
            _decision = decision;
        }

        public Task<ExecutionDecision> DecideAsync(string snapshotJson, PlayStrategy strategy, CancellationToken cancellationToken)
        {
            return Task.FromResult(_decision);
        }
    }
}
