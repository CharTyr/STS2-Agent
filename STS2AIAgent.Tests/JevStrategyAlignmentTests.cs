using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

/// <summary>
/// The seam between the slow planner and the fast executor.
/// </summary>
/// <remarks>
/// The planner is asked for <c>option_hints</c> "keyed by option kind" -- the action name, e.g.
/// <c>play_card</c> -- and the MCP strategy schema says the same. The choice question it is supposed to
/// guide is keyed by the concrete option ids <see cref="JevOptionEnumerator"/> expands for one frame
/// (<c>play_card:0-&gt;1</c>). Before this suite existed nothing bridged the two, so the planner's
/// per-option guidance named no criterion at all, and a concrete id left over from an earlier frame was
/// forwarded as though that option still existed.
/// <para>
/// These tests pin the enforced invariant -- every key of the forwarded <c>option_hints</c> is a key of
/// the same request's <c>criteria</c> -- together with the two guards the fix must not break: an answer
/// Jev cannot map back to a legal option is still refused rather than executed, and alignment adds no
/// provider request of its own. The live 2026-09-23 run is the fixture's parent: five consecutive
/// <c>end_turn</c> decisions there had five distinct state fingerprints, so those were real turns, not a
/// stuck loop; what the run shows is under-guided execution, which is what these tests address.
/// </para>
/// </remarks>
internal static class JevStrategyAlignmentTests
{
    public static IEnumerable<(string Name, Func<Task> Body)> All()
    {
        yield return ("JevAlign.HintsClosedOverOptions", HintsAreClosedOverTheFramesOptionIds);
        yield return ("JevAlign.KindHintFansOut", KindHintReachesEveryLiveOptionOfThatKind);
        yield return ("JevAlign.ConcreteHintWins", ConcreteHintIsNotOverwrittenByAKindHint);
        yield return ("JevAlign.UnmatchedKeysDropped", () => Task.Run(UnmatchedHintKeysAreReportedNotForwarded));
        yield return ("JevAlign.Bounded", () => Task.Run(AlignmentIsBoundedByItsCap));
        yield return ("JevAlign.NoHintsNoBias", AnEmptyHintMapAddsNoBias);
        yield return ("JevAlign.GoalLeadsTheQuestion", TheGoalLeadsTheChoiceQuestion);
        yield return ("JevAlign.GoalAbsentKeepsWording", AnAbsentGoalKeepsThePreviousInstructionWording);
        yield return ("JevAlign.GoalIsBounded", () => Task.Run(APlannerSentenceIsClampedToTheGoalCap));
        yield return ("JevAlign.PlanScopeNewerEncounter", APlanFromALaterRoundReadsAsANewerEncounter);
        yield return ("JevAlign.PlanScopeOtherScreen", APlanFromAnotherScreenIsNotCurrent);
        yield return ("JevAlign.PlanScopeAbsentWithoutAScope", APlanWithoutAScopeSendsNoScopeFacet);
        yield return ("JevAlign.UnknownChoiceRefused", AnUnknownChoiceIsRefusedRatherThanExecuted);
        yield return ("JevAlign.OneRequestPerDecision", AlignmentCostsNoExtraProviderRequest);
        yield return ("JevAlign.PlannerAsksForGoalAndKinds", ThePlannerAsksForAGoalAndKindKeyedHints);
    }

    /// <summary>
    /// The regression this suite exists for: a planner's kind-keyed hints and its stale concrete ids
    /// must both come out of the request as live option ids of the frame being decided.
    /// </summary>
    private static async Task HintsAreClosedOverTheFramesOptionIds()
    {
        var client = new StubJev();
        var strategy = new PlayStrategy
        {
            Goal = "kill the weakest enemy first",
            OptionHints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["play_card"] = "prefer damage",
                ["end_turn"] = "only when nothing helps",
                ["play_card:7->3"] = "an index from an earlier fight",
                ["choose_map_node"] = "a kind this frame does not offer"
            }
        };

        await new JevExecutionDecider(client, "jev").DecideAsync(CombatFrame, strategy, CancellationToken.None);

        var hints = StrategyFacet(client).GetProperty("option_hints");
        var criteria = CriteriaKeys(client);
        var forwarded = hints.EnumerateObject().Select(property => property.Name).ToList();
        Assert.NotEmpty(forwarded);
        foreach (var key in forwarded)
        {
            Assert.True(criteria.Contains(key), $"forwarded hint '{key}' must name a criterion of the same request");
        }

        Assert.True(criteria.Contains("play_card:0->0"), "the frame still offers the first card on the first target");
        Assert.False(hints.TryGetProperty("play_card", out _), "the kind key itself is not an option id");
        Assert.False(hints.TryGetProperty("play_card:7->3", out _), "a stale index is never forwarded");
        Assert.False(hints.TryGetProperty("choose_map_node", out _), "a kind absent from the frame is never forwarded");
        Assert.Equal("prefer damage", hints.GetProperty("play_card:0->0").GetString());
        Assert.Equal("only when nothing helps", hints.GetProperty("end_turn").GetString());

        // The executor is told how much of the guidance survived, so a plan written for another frame
        // reads as such instead of silently looking current.
        var strategyFacet = StrategyFacet(client);
        Assert.Equal(4, strategyFacet.GetProperty("option_hints_used").GetInt32());
        Assert.Equal(2, strategyFacet.GetProperty("option_hints_dropped").GetInt32());
    }

    private static async Task KindHintReachesEveryLiveOptionOfThatKind()
    {
        var client = new StubJev();
        var strategy = new PlayStrategy
        {
            OptionHints = new Dictionary<string, string>(StringComparer.Ordinal) { ["play_card"] = "prefer damage" }
        };

        await new JevExecutionDecider(client, "jev").DecideAsync(CombatFrame, strategy, CancellationToken.None);

        var hints = StrategyFacet(client).GetProperty("option_hints");
        var playIds = CriteriaKeys(client).Where(key => key.StartsWith("play_card:", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, playIds.Count);
        foreach (var id in playIds)
        {
            Assert.Equal("prefer damage", hints.GetProperty(id).GetString());
        }

        Assert.False(hints.TryGetProperty("end_turn", out _), "an unhinted kind stays unbiased");
    }

    private static async Task ConcreteHintIsNotOverwrittenByAKindHint()
    {
        var client = new StubJev();
        var strategy = new PlayStrategy
        {
            OptionHints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["play_card"] = "kind-wide nudge",
                ["play_card:0->0"] = "specific nudge"
            }
        };

        await new JevExecutionDecider(client, "jev").DecideAsync(CombatFrame, strategy, CancellationToken.None);

        var hints = StrategyFacet(client).GetProperty("option_hints");
        Assert.Equal("specific nudge", hints.GetProperty("play_card:0->0").GetString());
        Assert.Equal("kind-wide nudge", hints.GetProperty("play_card:0->1").GetString());
    }

    /// <summary>
    /// What the alignment refuses is reported, not silently swallowed: a dropped hint is the visible
    /// signal that the planner wrote an id for a frame it was not looking at.
    /// </summary>
    private static void UnmatchedHintKeysAreReportedNotForwarded()
    {
        var options = JevOptionEnumerator.Enumerate(CombatFrame);
        var alignment = JevOptionEnumerator.AlignHints(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["play_card"] = "kind",
                ["play_card:7->3"] = "stale",
                ["choose_map_node"] = "foreign",
                ["end_turn"] = "kind"
            },
            options);

        Assert.Equal(2, alignment.DroppedKeys.Count);
        Assert.True(alignment.DroppedKeys.Contains("play_card:7->3"));
        Assert.True(alignment.DroppedKeys.Contains("choose_map_node"));
        foreach (var key in alignment.OptionHints.Keys)
        {
            Assert.True(options.Any(option => string.Equals(option.Id, key, StringComparison.Ordinal)),
                $"aligned hint '{key}' must name an enumerated option");
        }
    }

    private static void AlignmentIsBoundedByItsCap()
    {
        // 13 playable cards x 20 targets = 260, past the enumerator's own 255 cap.
        var hand = new List<string>();
        for (var card = 0; card < 13; card++)
        {
            hand.Add($"{{\"i\": {card}, \"line\": \"Card{card}\", \"playable\": true, \"targets\": [{string.Join(", ", Enumerable.Range(0, 20))}]}}");
        }

        var frame = "{\"state\":{\"screen\":\"COMBAT\",\"combat\":{\"hand\":[" + string.Join(",", hand)
            + "]}},\"available_actions\":[{\"name\":\"play_card\",\"requires_index\":true}]}";
        var options = JevOptionEnumerator.Enumerate(frame);
        Assert.True(options.Count > JevOptionEnumerator.MaxAlignedHintEntries, "the fixture must exceed the cap");

        var alignment = JevOptionEnumerator.AlignHints(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["play_card"] = "prefer damage" },
            options);
        Assert.Equal(JevOptionEnumerator.MaxAlignedHintEntries, alignment.OptionHints.Count);
        Assert.Equal(0, alignment.DroppedKeys.Count);

        // Concrete ids past the cap, then a key that matches nothing. The cap must not end the scan:
        // every key that is not forwarded is reported, including the unknown one listed after it.
        var concrete = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in options.Take(JevOptionEnumerator.MaxAlignedHintEntries + 6))
        {
            concrete[option.Id] = "specific";
        }

        concrete["play_card:99->99"] = "stale";
        var capped = JevOptionEnumerator.AlignHints(concrete, options);
        Assert.Equal(JevOptionEnumerator.MaxAlignedHintEntries, capped.OptionHints.Count);
        Assert.Equal(7, capped.DroppedKeys.Count);
        Assert.True(capped.DroppedKeys.Contains("play_card:99->99"), "a key after the cap must still be reported");
    }

    private static async Task AnEmptyHintMapAddsNoBias()
    {
        var client = new StubJev();
        await new JevExecutionDecider(client, "jev")
            .DecideAsync(CombatFrame, PlayStrategy.Default, CancellationToken.None);

        var hints = StrategyFacet(client).GetProperty("option_hints");
        Assert.Equal(0, hints.EnumerateObject().Count());
    }

    private static async Task TheGoalLeadsTheChoiceQuestion()
    {
        var client = new StubJev();
        var strategy = new PlayStrategy { Goal = "kill the weakest enemy first" };
        await new JevExecutionDecider(client, "jev").DecideAsync(CombatFrame, strategy, CancellationToken.None);

        var instructions = ChoiceInstructions(client);
        Assert.Equal("kill the weakest enemy first", instructions.GetProperty("strategy").GetProperty("goal").GetString());
        var goal = instructions.GetProperty("goal").GetString()!;
        Assert.Contains("kill the weakest enemy first", goal);
        Assert.Contains("Pick the single best action", goal);
    }

    /// <summary>
    /// A strategy that states no goal must read exactly as it did before this field existed, so the
    /// default and MCP-written paths keep their previous prompt.
    /// </summary>
    private static async Task AnAbsentGoalKeepsThePreviousInstructionWording()
    {
        var client = new StubJev();
        await new JevExecutionDecider(client, "jev")
            .DecideAsync(CombatFrame, PlayStrategy.Default, CancellationToken.None);

        var instructions = ChoiceInstructions(client);
        Assert.Equal(
            "Pick the single best action to take right now in this Slay the Spire 2 frame.",
            instructions.GetProperty("goal").GetString());
        Assert.Equal(string.Empty, instructions.GetProperty("strategy").GetProperty("goal").GetString());
    }

    private static void APlannerSentenceIsClampedToTheGoalCap()
    {
        var runaway = new string('x', PlayStrategy.MaxGoalCharacters + 40);
        var parsed = PlayStrategy.TryParse("{\"goal\":\"" + runaway + "\"}");
        Assert.NotNull(parsed);
        Assert.Equal(PlayStrategy.MaxGoalCharacters, parsed!.Goal.Length);

        var shortGoal = PlayStrategy.TryParse("{\"goal\":\"kill the weakest enemy first\"}");
        Assert.Equal("kill the weakest enemy first", shortGoal!.Goal);
        Assert.Equal(string.Empty, PlayStrategy.Default.Goal);
    }

    /// <summary>
    /// A combat round counter restarts at 1 for each encounter, so a frame whose round is BELOW the round
    /// the plan recorded proves the plan predates this encounter. The facet says only that; nothing is
    /// inferred when the relation cannot be established.
    /// </summary>
    private static async Task APlanFromALaterRoundReadsAsANewerEncounter()
    {
        var laterPlan = new PlayStrategy { PlanScreen = "COMBAT", PlanRound = 5 };
        var client = new StubJev();
        await new JevExecutionDecider(client, "jev").DecideAsync(CombatFrame, laterPlan, CancellationToken.None);

        var scope = StrategyFacet(client).GetProperty("plan_scope");
        Assert.True(scope.GetProperty("newer_encounter_than_plan").GetBoolean());
        Assert.True(scope.GetProperty("same_screen").GetBoolean());
        Assert.Equal(1, scope.GetProperty("current_round").GetInt32());
        Assert.Equal(5, scope.GetProperty("written_at_round").GetInt32());
        Assert.Contains("general posture", scope.GetProperty("note").GetString());

        var sameFight = new StubJev();
        await new JevExecutionDecider(sameFight, "jev")
            .DecideAsync(CombatFrame, new PlayStrategy { PlanScreen = "COMBAT", PlanRound = 1 }, CancellationToken.None);
        Assert.False(StrategyFacet(sameFight).GetProperty("plan_scope").GetProperty("newer_encounter_than_plan").GetBoolean());
    }

    private static async Task APlanFromAnotherScreenIsNotCurrent()
    {
        var client = new StubJev();
        await new JevExecutionDecider(client, "jev")
            .DecideAsync(CombatFrame, new PlayStrategy { PlanScreen = "MAP" }, CancellationToken.None);

        var scope = StrategyFacet(client).GetProperty("plan_scope");
        Assert.False(scope.GetProperty("same_screen").GetBoolean());
        Assert.Equal("MAP", scope.GetProperty("written_for_screen").GetString());
        Assert.Equal("COMBAT", scope.GetProperty("current_screen").GetString());
    }

    private static async Task APlanWithoutAScopeSendsNoScopeFacet()
    {
        var client = new StubJev();
        await new JevExecutionDecider(client, "jev")
            .DecideAsync(CombatFrame, new PlayStrategy { Instructions = "mcp wrote this" }, CancellationToken.None);

        // Null here, and absent on the wire: JevClient serializes with WhenWritingNull.
        Assert.Equal(JsonValueKind.Null, StrategyFacet(client).GetProperty("plan_scope").ValueKind);
        Assert.Equal("mcp wrote this", StrategyFacet(client).GetProperty("instructions").GetString());
    }

    /// <summary>
    /// The safety the alignment must not weaken: Jev naming an id the enumerator did not produce is
    /// refused with an error and an accounted request, never executed.
    /// </summary>
    private static async Task AnUnknownChoiceIsRefusedRatherThanExecuted()
    {
        var client = new StubJev { Choice = "play_card:99->99" };
        var decision = await new JevExecutionDecider(client, "jev")
            .DecideAsync(CombatFrame, PlayStrategy.Default, CancellationToken.None);

        Assert.NotNull(decision.Error);
        Assert.Null(decision.Action);
        Assert.Null(decision.CardIndex);
        Assert.Equal(1, decision.RequestsSpent);
    }

    private static async Task AlignmentCostsNoExtraProviderRequest()
    {
        var client = new StubJev();
        var strategy = new PlayStrategy
        {
            Goal = "kill the weakest enemy first",
            OptionHints = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["play_card"] = "prefer damage",
                ["end_turn"] = "only when nothing helps"
            }
        };

        var decision = await new JevExecutionDecider(client, "jev")
            .DecideAsync(CombatFrame, strategy, CancellationToken.None);

        Assert.Equal(1, client.Calls);
        Assert.Equal(1, decision.RequestsSpent);
        Assert.Equal("end_turn", decision.Action);
        Assert.Equal(2, client.LastRequest!.Questions.Count);
        Assert.Equal(2, client.LastRequest!.Questions.Values.Count(question => question.Type is "choice" or "score"));
    }

    /// <summary>
    /// The planner half of the same seam: it must ask for a goal and for hints keyed by option kind, and
    /// it must record which frame the plan was written for so the executor can tell a live plan from
    /// carried-over guidance. The hints stay kind-keyed in the store; the executor re-keys them per frame.
    /// </summary>
    private static async Task ThePlannerAsksForAGoalAndKindKeyedHints()
    {
        var model = new CapturingPlannerModel();
        var store = new StrategyStore();
        var planner = new StrategyPlanner(model, AgentSettings.CreateDefault, store);

        var (adopted, _) = await planner.RefreshAsync(CombatFrame, CancellationToken.None);

        Assert.True(adopted, "a parseable plan is adopted");
        Assert.Contains("\"goal\"", model.SystemPrompt);
        Assert.Contains("option kind", model.SystemPrompt);
        Assert.Contains("never a card, target, or option index", model.SystemPrompt);

        Assert.Equal("kill the weakest enemy first", store.Current.Goal);
        Assert.Equal("prefer damage", store.Current.OptionHints["play_card"]);
        Assert.Equal("COMBAT", store.Current.PlanScreen);
        Assert.Equal(1, store.Current.PlanRound!.Value);
        Assert.Equal("llm", store.Current.Source);
    }

    // ---- helpers ------------------------------------------------------------

    private static JsonElement ChoiceInstructions(StubJev client) =>
        JsonSerializer.SerializeToElement(client.LastRequest!.Questions["action"].Instructions);

    private static JsonElement StrategyFacet(StubJev client) =>
        ChoiceInstructions(client).GetProperty("strategy");

    private static HashSet<string> CriteriaKeys(StubJev client)
    {
        var criteria = JsonSerializer.SerializeToElement(client.LastRequest!.Questions["action"].Criteria);
        return criteria.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    }

    // ---- fixtures -----------------------------------------------------------

    /// <summary>
    /// One combat frame: two playable cards (one targeted on two enemies, one untargeted) and
    /// <c>end_turn</c>. Three play options plus <c>end_turn</c> is enough to tell a kind hint's fan-out
    /// from a single concrete id.
    /// </summary>
    internal const string CombatFrame = """
{
  "state": {
    "screen": "COMBAT",
    "turn": 1,
    "available_actions": ["end_turn", "play_card"],
    "combat": {
      "hand": [
        {"i": 0, "line": "Strike", "playable": true, "targets": [0, 1]},
        {"i": 1, "line": "Defend", "playable": true, "targets": []}
      ],
      "enemies": [
        {"i": 0, "name": "Louse", "alive": true},
        {"i": 1, "name": "Slime", "alive": true}
      ]
    }
  },
  "available_actions": [
    {"name": "end_turn", "requires_index": false, "requires_target": false},
    {"name": "play_card", "requires_index": true, "requires_target": false}
  ]
}
""";

    /// <summary>A planner reply in the documented shape, and the prompt it was asked with.</summary>
    private sealed class CapturingPlannerModel : ILlmClientFactory, ILlmClient
    {
        public string? SystemPrompt { get; private set; }

        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken token)
        {
            SystemPrompt = string.Join(
                "\n",
                request.Messages.Where(message => message.Role == "system").Select(message => message.Content));
            return Task.FromResult(new LlmCompletion
            {
                Content = """
                {"goal":"kill the weakest enemy first","posture":"aggressive","instructions":"focus fire",
                 "option_hints":{"play_card":"prefer damage"}}
                """
            });
        }

        public Task<string> PingAsync(string model, CancellationToken token) => throw new NotSupportedException();

        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken token) =>
            Task.FromResult(true);
    }

    /// <summary>Answers the decider's choice question with a fixed id, and records the request.</summary>
    private sealed class StubJev : IJevClient
    {
        public int Calls { get; private set; }

        public JevRequest? LastRequest { get; private set; }

        public string Choice { get; init; } = "end_turn";

        public Task<JevResponse> SystemOneAsync(JevRequest request, CancellationToken token)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new JevResponse
            {
                Answers = new Dictionary<string, JevAnswer>
                {
                    ["action"] = new JevAnswer { Type = "choice", Choice = Choice, Confidence = 0.9 }
                },
                Usage = new JevUsage { InputTokens = 10, OutputTokens = 2 }
            });
        }

        public Task<string> PingAsync(CancellationToken token) => Task.FromResult("OK");
    }
}
