using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class GameDataFilterTests
{
    /// <summary>
    /// The screen-to-scene mapping the scene-scoped game-data lookup runs on.
    ///
    /// This is a shared expectation table, not a local assertion: one screen per line, one
    /// <c>Assert.Equal</c> per line. <c>mcp_server/tests/test_scene_field_alignment.py</c> parses
    /// these lines out of this file and asserts the Python mirror classifies the same screens the
    /// same way, so an edit here that the other side does not make turns that test red. The overlap
    /// case matters most: COMBAT_REWARD carries both keywords and only the order of the checks keeps
    /// it a combat screen, while REWARD is the reward overlay the mod actually emits.
    /// </summary>
    public static void DetectScene_MatchesGuidedMcpRules()
    {
        Assert.Equal("shop", GameDataFilter.DetectScene("SHOP"));
        Assert.Equal("shop", GameDataFilter.DetectScene("FAKE_MERCHANT"));
        Assert.Equal("event", GameDataFilter.DetectScene("EVENT"));
        Assert.Equal("combat", GameDataFilter.DetectScene("COMBAT"));
        Assert.Equal("combat", GameDataFilter.DetectScene("COMBAT_REWARD"));
        Assert.Equal("reward", GameDataFilter.DetectScene("REWARD"));
        Assert.Equal("card_selection", GameDataFilter.DetectScene("CARD_SELECTION"));
        Assert.Equal("chest", GameDataFilter.DetectScene("CHEST"));
        Assert.Equal("bundle_selection", GameDataFilter.DetectScene("BUNDLE_SELECTION"));
        Assert.Equal("menu", GameDataFilter.DetectScene("MAP"));
        Assert.Equal("menu", GameDataFilter.DetectScene("MAIN_MENU"));
    }

    public static void ProjectRelevant_KeepsCombatCardFields()
    {
        using var doc = JsonDocument.Parse("""
        [
          {"id":"STRIKE","name":"Strike","description":"Deal 6","type":"Attack","flavor":"ignore me"}
        ]
        """);
        var projected = GameDataFilter.ProjectRelevant("COMBAT", "cards", doc.RootElement, new[] { "STRIKE" });
        Assert.True(projected["STRIKE"].HasValue);
        var item = projected["STRIKE"]!.Value;
        Assert.True(item.TryGetProperty("name", out _));
        Assert.False(item.TryGetProperty("flavor", out _));
    }

    /// <summary>
    /// The offer screens have a field set of their own, so an offered card is projected rather than
    /// handed back whole. Without it the scene would silently fall through to the unprojected answer
    /// and every lookup would carry the entire card record.
    /// </summary>
    public static void ProjectRelevant_KeepsOfferCardFieldsOnReward()
    {
        using var doc = JsonDocument.Parse("""
        [
          {"id":"OFFER","name":"Offer","description":"Deal 9","type":"Attack","rarity":"Common","cost":1,"flavor":"ignore me","vars":{"damage":9}}
        ]
        """);

        foreach (var screen in new[] { "REWARD", "CARD_SELECTION", "BUNDLE_SELECTION" })
        {
            var projected = GameDataFilter.ProjectRelevant(screen, "cards", doc.RootElement, new[] { "OFFER" });
            Assert.True(projected["OFFER"].HasValue, screen);
            var item = projected["OFFER"]!.Value;
            Assert.True(item.TryGetProperty("cost", out _), screen);
            Assert.False(item.TryGetProperty("flavor", out _), screen);
            Assert.False(item.TryGetProperty("vars", out _), screen);
        }
    }
}

internal static class PlayIntentTests
{
    public static void DetectsPlayPhrasesAndIgnoresQuestions()
    {
        Assert.True(PlayIntent.Detect("帮我出牌"));
        Assert.True(PlayIntent.Detect("play for me"));
        Assert.False(PlayIntent.Detect("Please play a card"));
        Assert.False(PlayIntent.Detect("Should I play a card?"));
        Assert.False(PlayIntent.Detect("这张牌怎么样"));
        Assert.False(PlayIntent.Detect(""));
    }
}

internal static class ActIndexValidatorTests
{
    public static void RejectsMissingAndStaleIndexes()
    {
        const string actions = """[{"name":"play_card","requires_index":true}]""";
        const string state = """{"combat":{"hand":[{"i":0,"targets":[]}]}}""";
        Assert.NotNull(ActIndexValidator.Validate("play_card", null, null, null, actions, state));
        Assert.NotNull(ActIndexValidator.Validate("play_card", 9, null, null, actions, state));
        Assert.Null(ActIndexValidator.Validate("play_card", 0, null, null, actions, state));

        const string timelineActions = """[{"name":"choose_timeline_epoch","requires_index":true}]""";
        const string timelineState = """{"timeline":{"slots":[{"i":1,"line":"Epoch 1"}]}}""";
        Assert.NotNull(ActIndexValidator.Validate("choose_timeline_epoch", null, null, 9, timelineActions, timelineState));
        Assert.Null(ActIndexValidator.Validate("choose_timeline_epoch", null, null, 1, timelineActions, timelineState));

        const string eventActions = """[{"name":"choose_event_option","requires_index":true}]""";
        const string eventState = """{"event":{"options":[{"i":0,"locked":true},{"i":1,"locked":false}]}}""";
        Assert.NotNull(ActIndexValidator.Validate("choose_event_option", null, null, 0, eventActions, eventState));
        Assert.Null(ActIndexValidator.Validate("choose_event_option", null, null, 1, eventActions, eventState));
    }

    public static void DetectsUnsettledActResults()
    {
        Assert.True(ActIndexValidator.IsUnsettled("""{"status":"pending","stable":true}"""));
        Assert.True(ActIndexValidator.IsUnsettled("""{"status":"completed","stable":false}"""));
        Assert.False(ActIndexValidator.IsUnsettled("""{"status":"completed","stable":true}"""));
    }

    /// <summary>
    /// A rejection has to be correctable: the field that was wrong, what was submitted, and the
    /// indices the payload the validator just read actually offers -- plus the human-readable message
    /// it has always carried, unchanged.
    /// </summary>
    public static void StructuredIndexRejections()
    {
        const string actions = """[{"name":"play_card","requires_index":true,"requires_target":false}]""";
        const string state = """{"combat":{"hand":[{"i":0,"targets":[3,4]},{"i":1,"targets":[]}]}}""";

        var stale = ActIndexValidator.Validate("play_card", 9, null, null, actions, state);
        Assert.NotNull(stale);
        Assert.Equal("card_index 9 is not in the latest combat.hand.", stale!.Message);
        Assert.Equal("invalid_target", stale.Code);
        Assert.Equal(409, stale.StatusCode);
        Assert.Equal("card_index", stale.Field);
        Assert.Equal(9, stale.Submitted);
        Assert.Equal("combat.hand", stale.ValidField);
        Assert.True(stale.ValidIndices.SequenceEqual(new[] { 0, 1 }), "the hand's own indices");

        var missing = ActIndexValidator.Validate("play_card", null, null, null, actions, state);
        Assert.NotNull(missing);
        Assert.Equal("card_index must come from the latest combat.hand.", missing!.Message);
        Assert.Equal("invalid_request", missing.Code);
        Assert.Equal(400, missing.StatusCode);
        Assert.Equal("card_index", missing.Field);
        Assert.Null(missing.Submitted);
        Assert.True(missing.ValidIndices.SequenceEqual(new[] { 0, 1 }));

        const string targetActions = """[{"name":"play_card","requires_index":true,"requires_target":true}]""";
        var missingTarget = ActIndexValidator.Validate("play_card", 0, null, null, targetActions, state);
        Assert.NotNull(missingTarget);
        Assert.Equal("target_index must come from the latest payload.", missingTarget!.Message);
        Assert.Equal("target_index", missingTarget.Field);
        Assert.Equal("combat.hand[].targets", missingTarget.ValidField);
        Assert.True(missingTarget.ValidIndices.SequenceEqual(new[] { 3, 4 }), "the card's own targets");

        var target = ActIndexValidator.Validate("play_card", 0, 7, null, targetActions, state);
        Assert.NotNull(target);
        Assert.Equal("target_index 7 is not in the latest targets for card 0.", target!.Message);
        Assert.Equal("target_index", target.Field);
        Assert.Equal(7, target.Submitted);
        Assert.Equal("combat.hand[].targets", target.ValidField);
        Assert.True(target.ValidIndices.SequenceEqual(new[] { 3, 4 }));

        const string mapActions = """[{"name":"choose_map_node","requires_index":true}]""";
        const string mapState = """{"map":{"options":[{"i":0},{"i":2}]}}""";
        var option = ActIndexValidator.Validate("choose_map_node", null, null, 1, mapActions, mapState);
        Assert.NotNull(option);
        Assert.Equal("option_index 1 is not in the latest payload for choose_map_node.", option!.Message);
        Assert.Equal("map.options", option.ValidField);
        Assert.Equal("option_index", option.Field);
        Assert.Equal(1, option.Submitted);
        Assert.True(option.ValidIndices.SequenceEqual(new[] { 0, 2 }));

        // The envelope is the HTTP one: the same code, message, details, and retryable the Router
        // writes, with status_code in-process.
        var payload = stale.ToApiException("play_card", new[] { "end_turn" });
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AgentErrorEnvelope.ToPayload(payload)));
        var error = document.RootElement;
        Assert.Equal("invalid_target", error.GetProperty("code").GetString());
        Assert.Equal("card_index 9 is not in the latest combat.hand.", error.GetProperty("message").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(409, error.GetProperty("status_code").GetInt32());
        var details = error.GetProperty("details");
        Assert.Equal("play_card", details.GetProperty("action").GetString());
        Assert.Equal("card_index", details.GetProperty("field").GetString());
        Assert.Equal(9, details.GetProperty("submitted").GetInt32());
        Assert.Equal("combat.hand", details.GetProperty("valid_field").GetString());
        Assert.True(
            details.GetProperty("valid_indices").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(new[] { 0, 1 }),
            "valid_indices must survive serialization");
        Assert.Equal("end_turn", details.GetProperty("available_actions")[0].GetString());
    }

    /// <summary>
    /// A non-object JSON root (array, scalar, null) must read as "settled" rather than throwing
    /// <see cref="InvalidOperationException"/> out of <c>TryGetProperty</c> — the same boundary
    /// <c>AgentLoop.ParseArgs</c> enforces on tool arguments.
    /// </summary>
    public static void NonObjectActResultReadsAsSettled()
    {
        Assert.False(ActIndexValidator.IsUnsettled("[]"));
        Assert.False(ActIndexValidator.IsUnsettled("42"));
        Assert.False(ActIndexValidator.IsUnsettled("\"pending\""));
        Assert.False(ActIndexValidator.IsUnsettled("null"));
        Assert.False(ActIndexValidator.IsUnsettled("true"));
    }
}

internal static class AgentLoopTests
{
    public static async Task NonCombatOnly_YieldsBeforeWaitModelOrAction()
    {
        var settings = AgentSettings.CreateDefault();
        settings.NonCombatOnlyEnabled = true;
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>());

        var result = await new AgentLoop(bridge, factory, () => settings)
            .PlayOnceAsync(CancellationToken.None);

        Assert.True(result.WaitingForCombat,
            $"Expected combat wait, got acted={result.Acted ?? "<null>"}, error={result.Error ?? "<null>"}, requests={result.RequestsSpent}.");
        Assert.True(result.WaitingForGame);
        Assert.Equal(0, bridge.WaitCalls);
        Assert.Equal(0, bridge.ActCalls);
        Assert.Equal(0, factory.Requests.Count);
    }

    public static async Task NonCombatOnly_AllowsNonCombatActions()
    {
        var settings = AgentSettings.CreateDefault();
        settings.NonCombatOnlyEnabled = true;
        var bridge = RewardBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "choose-reward",
                        Name = "act",
                        ArgumentsJson = "{\"action\":\"choose_reward_card\",\"option_index\":0}"
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("choose_reward_card", result.Acted);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(1, factory.Requests.Count);
    }

    public static async Task NonCombatOnly_RechecksBeforeDispatchAndKeepsWaiting()
    {
        var settings = AgentSettings.CreateDefault();
        settings.NonCombatOnlyEnabled = true;
        var bridge = RewardBridge();
        bridge.ActionSnapshots.Enqueue(
            """{"state":{"screen":"COMBAT","in_combat":true,"available_actions":["end_turn"]},"available_actions":[{"name":"end_turn","requires_index":false}]}""");
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                Usage = new LlmUsage { PromptTokens = 10, CompletionTokens = 2, TotalTokens = 12 },
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "stale-reward-choice",
                        Name = "act",
                        ArgumentsJson = "{\"action\":\"choose_reward_card\",\"option_index\":0}"
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.True(result.WaitingForCombat,
            $"Expected combat wait, got acted={result.Acted ?? "<null>"}, error={result.Error ?? "<null>"}, requests={result.RequestsSpent}.");
        Assert.Equal(0, bridge.ActCalls);
        Assert.Equal(1, factory.Requests.Count);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Equal(12, result.Usage?.TotalTokens);
    }

    public static async Task NonCombatOnly_OverlayAndFinalDispatchAreGuarded()
    {
        foreach (var screen in new[] { "CARD_SELECTION", "MODAL", "UNKNOWN" })
        {
            var settings = AgentSettings.CreateDefault();
            settings.NonCombatOnlyEnabled = true;
            var bridge = new FakeBridge { CompactStateJson = "{\"screen\":\"" + screen + "\",\"in_combat\":true}" };
            var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>());
            var result = await new AgentLoop(bridge, factory, () => settings).PlayOnceAsync(CancellationToken.None);
            Assert.True(result.WaitingForCombat);
            Assert.Equal(0, factory.Requests.Count);
        }
        var config = AgentSettings.CreateDefault();
        config.NonCombatOnlyEnabled = true;
        var race = RewardBridge();
        race.AtDispatchStateJson = """{"screen":"CARD_SELECTION","in_combat":true}""";
        var client = new ScriptedClientFactory(new[] { new LlmCompletion { ToolCalls = new[] {
            new LlmToolCall { Id = "race", Name = "act", ArgumentsJson = """{"action":"choose_reward_card","option_index":0}""" }
        } } });
        var waited = await new AgentLoop(race, client, () => config).PlayOnceAsync(CancellationToken.None);
        Assert.True(waited.WaitingForCombat);
        Assert.Equal(0, race.ActCalls);
        Assert.Equal(1, waited.RequestsSpent);
    }

    private static FakeBridge RewardBridge() => new()
    {
        CompactStateJson =
            """{"screen":"REWARD","in_combat":false,"available_actions":["choose_reward_card","skip_reward_cards"],"reward":{"cards":[{"i":0,"line":"Strike"}]}}""",
        AvailableActionsJson =
            """[{"name":"choose_reward_card","requires_index":true,"requires_target":false},{"name":"skip_reward_cards","requires_index":false,"requires_target":false}]""",
        Screen = "REWARD"
    };

    public static async Task PauseAfterModelResponseDoesNotDispatchAct()
    {
        using var cancellation = new CancellationTokenSource();
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall
            {
                Id = "late", Name = "act", ArgumentsJson = "{\"action\":\"play_card\",\"card_index\":0}"
            } } }
        }) { OnRequest = () => cancellation.Cancel() };
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);
        var canceled = false;
        try { await loop.PlayOnceAsync(cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.True(canceled);
        Assert.Equal(0, bridge.ActCalls);
    }
    public static async Task TeamChat_CannotActEvenWithPlayIntent()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "forbidden", Name = "act", ArgumentsJson = "{\"action\":\"end_turn\"}" } } },
            new LlmCompletion { Content = "我会在下一步考虑集火。" }
        });
        var settings = AgentSettings.CreateDefault();
        var loop = new AgentLoop(bridge, factory, () => settings);
        var result = await loop.ChatAsync("帮我出牌", Array.Empty<ChatTurn>(),
            new ChatOptions { TeammateConversation = true }, CancellationToken.None);
        Assert.Equal(0, bridge.ActCalls);
        Assert.Null(result.Acted);
        Assert.False(factory.LastRequest!.Tools!.Any(tool => tool.Name == "act"));
        Assert.Contains("read-only", factory.LastRequest.Messages[0].Content);
    }

    public static async Task ReadOnlyChat_CannotActEvenWithPlayIntent()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "forbidden", Name = "act", ArgumentsJson = "{\"action\":\"end_turn\"}" } } },
            new LlmCompletion { Content = "这波先控场。" }
        });
        var settings = AgentSettings.CreateDefault();
        var loop = new AgentLoop(bridge, factory, () => settings);
        var result = await loop.ChatAsync("帮我出牌", Array.Empty<ChatTurn>(),
            new ChatOptions { ReadOnly = true }, CancellationToken.None);
        Assert.Equal(0, bridge.ActCalls);
        Assert.Null(result.Acted);
        Assert.False(factory.LastRequest!.Tools!.Any(tool => tool.Name == "act"));
    }

    public static async Task ProactiveChat_InjectsToneIntoSystemPrompt()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[] { new LlmCompletion { Content = "先看对面意图。" } });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);
        var result = await loop.ChatAsync(
            ProactiveChatPolicy.BuildPrompt(ProactiveChatMoment.CombatStart),
            Array.Empty<ChatTurn>(),
            new ChatOptions
            {
                AttachState = true,
                ReadOnly = true,
                ExtraSystemInstruction = ProactiveChatTones.BuildSystemInstruction(ProactiveChatTones.Terse)
            },
            CancellationToken.None);
        Assert.Contains("clipped field report", factory.LastRequest!.Messages[0].Content);
        Assert.Equal(0, bridge.ActCalls);
        Assert.Null(result.Acted);
    }

    public static async Task TeamSuggestion_ReachesNextPlayDecision()
    {
        var conversation = new STS2AIAgent.Multiplayer.TeamConversation();
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>());
        var loop = new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault, conversation.BuildDecisionContext);
        conversation.Add("user", "focus left");
        await loop.PlayOnceAsync(CancellationToken.None);
        Assert.True(factory.LastRequest!.Messages.Any(message => message.Content?.Contains("focus left") == true));
        Assert.True(factory.LastRequest.Messages.Any(message => message.Content?.Contains("historical context") == true));
        conversation.Clear();
        await loop.PlayOnceAsync(CancellationToken.None);
        Assert.False(factory.LastRequest!.Messages.Any(message => message.Content?.Contains("focus left") == true));
    }

    public static async Task PlayOnce_RethrowsRunBoundaryAfterAct()
    {
        var bridge = new FakeBridge
        {
            CompactStateJson =
                """{"screen":"COMBAT","run_id":"run_1","session":{"phase":"run"},"available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"targets":[]}]}}""",
            AfterActCompactStateJson =
                """{"screen":"MAIN_MENU","run_id":"run_unknown","session":{"phase":"menu"},"available_actions":["switch_profile"]}"""
        };
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);
        var stopped = false;
        try
        {
            await loop.PlayOnceAsync(CancellationToken.None, new CurrentRunBoundary().Check);
        }
        catch (AutoPlayStoppedException)
        {
            stopped = true;
        }

        Assert.True(stopped);
        Assert.Equal(1, bridge.ActCalls);
    }

    public static async Task PlayOnce_InvokesCheckStateOnGetGameState()
    {
        var checks = 0;
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall { Id = "call_state", Name = "get_game_state", ArgumentsJson = "{}" },
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);
        await loop.PlayOnceAsync(CancellationToken.None, _ => checks++);
        Assert.True(checks >= 3, "expected checkState on start, get_game_state, and act");
        Assert.Equal(1, bridge.ActCalls);
    }

    /// <summary>
    /// A decision used to read the whole state three times before asking the model: once to check
    /// the run boundary, once inside the wait, once after it. The wait now hands back the frame it
    /// judged actionable, so a turn reads the state once before deciding (W9, 2026-10-28).
    /// </summary>
    public static async Task PlayOnce_ReadsTheStateOnceBeforeDeciding()
    {
        var checks = new List<string>();
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall { Id = "call_act", Name = "act", ArgumentsJson = """{"action":"play_card","card_index":0}""" }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);
        await loop.PlayOnceAsync(CancellationToken.None, json => checks.Add(json));

        Assert.Equal(1, bridge.WaitCalls);
        // One read is the wait's own frame (the one the model decides from), the other is the settled
        // state after the act, which the no-progress guard fingerprints. There used to be four.
        Assert.Equal(2, bridge.StateReads, "One read before deciding and one after the act settles.");
        Assert.Equal(3, checks.Count,
            "The boundary is checked on the decided frame, on the act's own decision snapshot, and on the settled frame.");
        Assert.Equal(1, bridge.ActCalls);
    }

    private const string ForcedEndTurnState =
        """{"screen":"COMBAT","in_combat":true,"available_actions":["end_turn"],"combat":{"action_readiness":{"can_use_combat_actions":true},"end_turn_will_kill_player":false,"hand":[{"i":0,"line":"Wound","playable":false}],"enemies":[{"i":0}]}}""";

    /// <summary>A forced move is made with no model request and is recorded as "autopilot" (W2).</summary>
    public static async Task PlayOnce_ForcedMoveSpendsNoRequest()
    {
        var bridge = new FakeBridge
        {
            CompactStateJson = ForcedEndTurnState,
            AvailableActionsJson = """[{"name":"end_turn","requires_index":false,"requires_target":false}]""",
            ActResultJson = """{"action":"end_turn","status":"completed","stable":true}"""
        };
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>());
        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);

        Assert.Equal("end_turn", result.Acted);
        Assert.Equal("autopilot", result.Source);
        Assert.Equal(0, result.RequestsSpent);
        Assert.Equal(0, factory.Requests.Count, "No model request for a forced move.");
        Assert.Equal(1, bridge.ActCalls);
        Assert.Contains(AgentLoop.ReasonFor(new AutopilotDecision("end_turn", null, "combat_no_moves", null)), result.Reasoning);
    }

    public static async Task PlayOnce_ForcedMoveYieldsToSwitchAndPlayerGuidance()
    {
        var settings = AgentSettings.CreateDefault();
        settings.AutopilotEnabled = false;
        var bridge = new FakeBridge
        {
            CompactStateJson = ForcedEndTurnState,
            AvailableActionsJson = """[{"name":"end_turn","requires_index":false,"requires_target":false}]"""
        };
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "a", Name = "act", ArgumentsJson = """{"action":"end_turn"}""" } } }
        });
        var off = await new AgentLoop(bridge, factory, () => settings).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, factory.Requests.Count, "With the switch off the model decides, as in v0.17.0.");
        Assert.Null(off.Source);

        var guided = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "b", Name = "act", ArgumentsJson = """{"action":"end_turn"}""" } } }
        });
        var loop = new AgentLoop(new FakeBridge
            {
                CompactStateJson = ForcedEndTurnState,
                AvailableActionsJson = """[{"name":"end_turn","requires_index":false,"requires_target":false}]"""
            },
            guided,
            AgentSettings.CreateDefault,
            peekPlayInstruction: _ => ("g1", "play defensively"));
        var withGuidance = await loop.PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, guided.Requests.Count, "Queued player guidance goes to the model even on a forced frame.");
        Assert.Null(withGuidance.Source);
    }

    private static string PlanState(int turn, string hp, string hand, bool enemyAlive = true)
    {
        var alive = enemyAlive ? "true" : "false";
        return "{\"screen\":\"COMBAT\",\"in_combat\":true,\"turn\":" + turn
            + ",\"available_actions\":[\"play_card\",\"end_turn\"],\"combat\":{\"action_readiness\":{\"can_use_combat_actions\":true},\"player\":{\"hp\":\""
            + hp + "\"},\"hand\":[" + hand + "],\"enemies\":[{\"i\":0,\"alive\":" + alive + ",\"hittable\":" + alive + "}]}}";
    }

    private const string StrikeA = """{"i":0,"cid":11,"line":"Strike","playable":true,"targets":[0]}""";
    private const string DefendB = """{"i":1,"cid":12,"line":"Defend","playable":true,"targets":[]}""";
    private const string BashC = """{"i":2,"cid":13,"line":"Bash","playable":true,"targets":[0]}""";

    private static FakeBridge PlanBridge(params string[] after)
    {
        var bridge = new FakeBridge
        {
            CompactStateJson = PlanState(1, "40/80", string.Join(",", StrikeA, DefendB, BashC)),
            AvailableActionsJson = """[{"name":"play_card","requires_index":true,"requires_target":false},{"name":"end_turn","requires_index":false,"requires_target":false}]"""
        };
        foreach (var state in after) bridge.AfterActStates.Enqueue(state);
        return bridge;
    }

    private static ScriptedClientFactory PlanModel(string arguments) => new(new[]
    {
        new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "plan", Name = "act", ArgumentsJson = arguments } } }
    });

    private const string ThreeStepPlan =
        """{"action":"play_card","card_cid":11,"target_index":0,"reason":"hit","then":[{"action":"play_card","card_cid":12,"reason":"block"},{"action":"end_turn"}]}""";

    /// <summary>
    /// One request plays a whole turn (W3): each step re-resolves its card by cid against the hand as
    /// it is then, and the receipt lists every step.
    /// </summary>
    public static async Task Plan_ThreeStepsOneRequest()
    {
        var bridge = PlanBridge(
            PlanState(1, "40/80", """{"i":0,"cid":12,"line":"Defend","playable":true,"targets":[]},{"i":1,"cid":13,"line":"Bash","playable":true,"targets":[0]}"""),
            PlanState(1, "40/80", """{"i":0,"cid":13,"line":"Bash","playable":true,"targets":[0]}"""),
            PlanState(2, "40/80", ""));
        var factory = PlanModel(ThreeStepPlan);
        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);

        Assert.Equal(1, factory.Requests.Count,
            $"The whole turn costs one request (acts={bridge.ActCalls}, error={result.Error}, stop={result.PlanStopReason}).");
        Assert.Equal(3, bridge.ActCalls);
        Assert.Equal(("play_card", (int?)0, (int?)0), bridge.ActLog[0]);
        Assert.Equal(("play_card", (int?)0, (int?)null), bridge.ActLog[1], "cid 12 moved from position 1 to 0.");
        Assert.Equal("end_turn", bridge.ActLog[2].Action);
        Assert.Equal(TurnPlanStopReasons.Completed, result.PlanStopReason);
        Assert.Equal(3, result.PlanSteps!.Count);
        Assert.Equal(3, result.PlanSize);
        Assert.Equal("end_turn", result.Acted, "The receipt describes the last executed step.");
        Assert.Equal(1, result.RequestsSpent);
        Assert.True(factory.Requests[0].Tools!.Single(tool => tool.Name == "act").Parameters.ToString()!.Length > 0);
    }

    public static async Task Plan_StopsOnANewCardOrADeadTarget()
    {
        var drew = PlanBridge(PlanState(1, "40/80", DefendB.Replace("\"i\":1", "\"i\":0") + """,{"i":1,"cid":99,"line":"Drawn","playable":true,"targets":[]}"""));
        var drewResult = await new AgentLoop(drew, PlanModel(ThreeStepPlan), AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, drew.ActCalls, "A drawn card is new information: stop and ask again.");
        Assert.Equal(TurnPlanStopReasons.NewCards, drewResult.PlanStopReason);
        Assert.Equal(1, drewResult.PlanSteps!.Count);

        var deadTargetPlan = """{"action":"play_card","card_cid":12,"then":[{"action":"play_card","card_cid":11,"target_index":0}]}""";
        var dead = PlanBridge(PlanState(1, "40/80", StrikeA, enemyAlive: false));
        var deadResult = await new AgentLoop(dead, PlanModel(deadTargetPlan), AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, dead.ActCalls);
        Assert.Equal(TurnPlanStopReasons.TargetGone, deadResult.PlanStopReason);

        var hurt = PlanBridge(PlanState(1, "33/80", DefendB.Replace("\"i\":1", "\"i\":0")));
        var hurtResult = await new AgentLoop(hurt, PlanModel(ThreeStepPlan), AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(TurnPlanStopReasons.TookDamage, hurtResult.PlanStopReason);
        Assert.Equal(1, hurt.ActCalls);
    }

    public static async Task Plan_CardIdentityIsRecheckedAtDispatch()
    {
        var bridge = PlanBridge();
        bridge.AtDispatchStateJson = PlanState(1, "40/80", """{"i":0,"cid":99,"playable":true,"targets":[0]}""");
        await new AgentLoop(bridge, PlanModel(ThreeStepPlan), AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(0, bridge.ActCalls, "A different card now occupies position zero: dispatch must refuse it.");
    }

    public static async Task Plan_RejectedFirstActRunsNothingAndSwitchOffIgnoresThen()
    {
        var rejected = PlanBridge();
        var rejectedResult = await new AgentLoop(rejected, PlanModel("""{"action":"play_card","card_cid":77,"then":[{"action":"end_turn"}]}"""), AgentSettings.CreateDefault)
            .PlayOnceAsync(CancellationToken.None);
        Assert.Equal(0, rejected.ActCalls, "A first act the game never took runs none of its plan.");
        Assert.Null(rejectedResult.PlanSteps);

        var settings = AgentSettings.CreateDefault();
        settings.TurnPlansEnabled = false;
        var off = PlanBridge(PlanState(1, "40/80", DefendB.Replace("\"i\":1", "\"i\":0")));
        var offFactory = PlanModel(ThreeStepPlan);
        var offResult = await new AgentLoop(off, offFactory, () => settings).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, off.ActCalls, "With plans off only the first act runs.");
        Assert.Equal(TurnPlanStopReasons.Disabled, offResult.PlanStopReason);
        Assert.False(offFactory.Requests[0].Tools!.Single(tool => tool.Name == "act").Description.Contains("then", StringComparison.Ordinal),
            "With plans off the model is not offered `then` at all.");
    }

    public static async Task Plan_PlayerGuidanceStopsTheRest()
    {
        var bridge = PlanBridge(PlanState(1, "40/80", DefendB.Replace("\"i\":1", "\"i\":0")));
        // The player types guidance while the model is answering: the first act still runs (the
        // model chose it), the rest of the plan yields so the guidance reaches the next decision.
        var guidanceQueued = false;
        var factory = PlanModel(ThreeStepPlan);
        factory.OnRequest = () => guidanceQueued = true;
        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault,
            peekPlayInstruction: _ => guidanceQueued ? ("g", "stop and defend") : null).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(TurnPlanStopReasons.PlayerInstruction, result.PlanStopReason);
        Assert.Equal(1, bridge.ActCalls);
    }

    public static async Task PlayOnce_TimedOutWaitStillChecksTheBoundaryOnce()
    {
        var checks = 0;
        var bridge = new FakeBridge { Actionable = false };
        var loop = new AgentLoop(bridge, new ScriptedClientFactory(Array.Empty<LlmCompletion>()), AgentSettings.CreateDefault);
        var result = await loop.PlayOnceAsync(CancellationToken.None, _ => checks++);

        Assert.True(result.WaitingForGame);
        Assert.Equal(1, checks);
        Assert.Equal(1, bridge.StateReads);
        Assert.Equal(0, result.RequestsSpent);
    }

    public static async Task PlayOnce_StopsFurtherLlmCallsWhenRequestBudgetIsSpent()
    {
        var calls = 0;
        var guard = new SessionBudgetGuard(maxRequests: 1);
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall { Id = "call_state", Name = "get_game_state", ArgumentsJson = "{}" }
                }
            },
            new LlmCompletion { Content = "should not be requested" }
        })
        {
            OnRequest = () => calls++
        };
        var loop = new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault, budgetGuard: () => guard);
        var result = await loop.PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.Contains("请求次数上限", result.Error);
    }

    public static async Task PlayOnce_ExecutesSingleValidatedAct()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                Content = "playing strike",
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_state",
                        Name = "get_game_state",
                        ArgumentsJson = "{}"
                    },
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0,"reason":"Strike before ending the turn."}"""
                    }
                }
            }
        });
        var settings = AgentSettings.CreateDefault();
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("play_card", result.Acted);
        Assert.Equal("Strike before ending the turn.", result.Reasoning);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Null(result.Error);
    }

    public static async Task PlayOnce_ForwardsCrystalSphereArguments()
    {
        var bridge = new FakeBridge
        {
            CompactStateJson =
                """{"screen":"CRYSTAL_SPHERE","available_actions":["crystal_clear_cell"],"crystal_sphere":{"grid_width":11,"grid_height":11}}""",
            AvailableActionsJson =
                """[{"name":"crystal_clear_cell","requires_index":false,"requires_target":false}]""",
            Screen = "CRYSTAL_SPHERE"
        };
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson =
                            """{"action":"crystal_clear_cell","x":4,"y":7,"tool":"small"}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("crystal_clear_cell", result.Acted);
        Assert.Equal("crystal_clear_cell", bridge.LastAction);
        Assert.Equal(4, bridge.LastX);
        Assert.Equal(7, bridge.LastY);
        Assert.Equal("small", bridge.LastTool);
        Assert.Null(result.Error);
    }

    public static void ActToolSchema_IncludesCrystalSphereArguments()
    {
        var act = AgentTools.Play.Single(tool => tool.Name == "act");
        var schema = JsonSerializer.Serialize(act.Parameters);

        Assert.Contains("\"x\"", schema);
        Assert.Contains("\"y\"", schema);
        Assert.Contains("\"tool\"", schema);
        Assert.Contains("\"reason\"", schema);
        Assert.Contains("shown to the player", schema, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task PlayOnce_SkipsWhenNotActionable()
    {
        var bridge = new FakeBridge { Actionable = false };
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>());
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(0, bridge.ActCalls);
        Assert.Contains("actionable", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task PlayOnce_RejectsIndexNotInLatestPayload()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":9}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(0, bridge.ActCalls);
        Assert.Contains("card_index", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task PlayOnce_WaitsWhenActIsPending()
    {
        var bridge = new FakeBridge
        {
            ActResultJson = """{"action":"play_card","status":"pending","stable":false}"""
        };
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("play_card", result.Acted);
        Assert.True(bridge.WaitCalls >= 2, "expected a second wait after pending act");
        Assert.Null(result.Error);
        Assert.Contains("completed", result.ActResultJson, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task PlayOnce_DoesNotCaptureWithoutVision()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "end the turn" }
        });
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = false;
        settings.VisionModelId = null;
        var loop = new AgentLoop(bridge, factory, () => settings);

        await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(0, bridge.CaptureCalls);
    }

    public static async Task Chat_DoesNotExecuteAct()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                Content = null,
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            },
            new LlmCompletion { Content = "I will not press buttons in chat." }
        });
        var settings = AgentSettings.CreateDefault();
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.ChatAsync(
            "这张牌怎么样",
            Array.Empty<ChatTurn>(),
            new ChatOptions { AttachState = false, AttachScreenshot = false },
            CancellationToken.None);

        Assert.Equal(0, bridge.ActCalls);
        Assert.Null(result.Acted);
        Assert.Contains("will not press buttons", result.AssistantText, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task PlayOnce_UsesPerModelThinkingIntensity()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "end the turn" }
        });
        var settings = AgentSettings.CreateDefault();
        settings.ThinkingIntensity = "low";
        settings.Models[0].ThinkingIntensity = "high";
        settings.Models[0].SupportsVision = false;
        var loop = new AgentLoop(bridge, factory, () => settings);

        await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(ThinkingIntensity.High, factory.LastRequest?.Thinking);
    }

    public static async Task PlayOnce_TextOnlyJsonActWithoutTools()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = """{"action":"end_turn","reason":"No playable cards remain."}""" }
        });
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = false;
        settings.Models[0].SupportsTools = false;
        settings.VisionModelId = null;
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("end_turn", result.Acted);
        Assert.Equal("No playable cards remain.", result.Reasoning);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Null(result.Error);
        Assert.Equal(0, bridge.CaptureCalls);
        Assert.Null(factory.LastRequest?.Tools);
    }

    public static async Task PlayOnce_TextOnlyCrystalJsonForwardsCoordinatesAndNullTool()
    {
        var bridge = new FakeBridge
        {
            CompactStateJson =
                """{"screen":"CRYSTAL_SPHERE","available_actions":["crystal_clear_cell"],"crystal_sphere":{"grid_width":11,"grid_height":11}}""",
            AvailableActionsJson =
                """[{"name":"crystal_clear_cell","requires_index":false,"requires_target":false}]""",
            Screen = "CRYSTAL_SPHERE"
        };
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                Content =
                    """{"action":"crystal_clear_cell","x":2,"y":9,"tool":null}"""
            }
        });
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = false;
        settings.Models[0].SupportsTools = false;
        settings.VisionModelId = null;
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("crystal_clear_cell", result.Acted);
        Assert.Equal(2, bridge.LastX);
        Assert.Equal(9, bridge.LastY);
        Assert.Null(bridge.LastTool);
        Assert.Null(result.Error);
    }

    public static async Task PlayOnce_WaitUntilActionableTool()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_wait",
                        Name = "wait_until_actionable",
                        ArgumentsJson = """{"timeout_seconds":5}"""
                    }
                }
            },
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("play_card", result.Acted);
        Assert.True(bridge.WaitCalls >= 2, "expected wait_until_actionable in addition to the pre-step wait");
        Assert.Null(result.Error);
    }

    public static void ParsesActJsonFromMarkdownFence()
    {
        Assert.True(ActJsonParser.TryParse("```json\n{\"action\":\"proceed\"}\n```", out var json));
        Assert.Contains("proceed", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(ActJsonParser.TryParse("I would play a card.", out _));
    }

    public static async Task Chat_AllowsActWhenUserAsks()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            },
            new LlmCompletion { Content = "Played strike." }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.ChatAsync(
            "帮我出牌",
            Array.Empty<ChatTurn>(),
            new ChatOptions { AttachState = false, AttachScreenshot = false },
            CancellationToken.None);

        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal("play_card", result.Acted);
        Assert.Contains("Played strike", result.AssistantText);
    }

    public static async Task Chat_IgnoresPlayACardAdviceQuestion()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            },
            new LlmCompletion { Content = "I will not press buttons in chat." }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.ChatAsync(
            "Should I play a card?",
            Array.Empty<ChatTurn>(),
            new ChatOptions { AttachState = false, AttachScreenshot = false },
            CancellationToken.None);

        Assert.Equal(0, bridge.ActCalls);
        Assert.Null(result.Acted);
    }

    public static async Task PlayOnce_IgnoresJsonWhenToolsEnabled()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = """Advice: {"action":"end_turn"} is fine.""" }
        });
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsTools = true;
        settings.Models[0].SupportsVision = false;
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(0, bridge.ActCalls);
        Assert.Null(result.Acted);
    }

    public static async Task PlayOnce_RetriesAfterFailedAct()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "bad",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":9,"reason":"This rejected choice must not leak."}"""
                    }
                }
            },
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "good",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0,"reason":"Use the legal strike."}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal("Use the legal strike.", result.Reasoning);
        Assert.Null(result.Error);
    }

    /// <summary>
    /// The prompt order is a cache contract: every message that does not change between two decisions
    /// on the same screen comes first, and only this step's state and the instruction answering it
    /// come last. The state used to be message 1, which invalidated everything after it -- the
    /// playbook in the system prompt included -- on every single step.
    /// </summary>
    public static async Task PlayOnce_PutsTheStaticPrefixBeforeTheDynamicState()
    {
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>());
        var loop = new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault);

        await loop.PlayOnceAsync(CancellationToken.None);

        var messages = factory.Requests[0].Messages;
        Assert.Contains(PlayPrompt.PlayContract, messages[0].Content);
        Assert.Equal("system", messages[0].Role);

        // Static system prompts, then the state, then the one instruction it answers.
        var firstUser = messages.ToList().FindIndex(message => message.Role == "user");
        Assert.True(firstUser > 0, "the first message must be the static system prompt");
        for (var index = firstUser; index < messages.Count; index++)
        {
            Assert.Equal("user", messages[index].Role);
        }

        Assert.Equal(messages.Count - 2, firstUser);
        Assert.Contains("Latest compact game state", messages[^2].Content);
        Assert.Contains(PlayPrompt.ActNowInstruction, messages[^1].Content);

        // The state carries the only copy of the screen playbook: one section, not the document.
        Assert.Contains("## COMBAT", string.Join("\n", messages.Select(message => message.Content)));
        Assert.False(
            string.Join("\n", messages.Select(message => message.Content))
                .Contains(PlayPrompt.ScreenPlaybooks, StringComparison.Ordinal),
            "no step may carry the whole screen-playbooks document");
    }

    public static async Task PlayOnce_KeepsTheStaticPrefixStableAcrossSteps()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>());
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        await loop.PlayOnceAsync(CancellationToken.None);
        var first = factory.Requests[0].Messages;

        // Same screen, different turn: everything before the state has to be byte-identical, which is
        // what lets a provider reuse the prefix it cached for the previous step.
        bridge.CompactStateJson =
            """{"screen":"COMBAT","available_actions":["end_turn"],"combat":{"hand":[{"i":1,"line":"Defend","targets":[]}],"enemies":[{"i":0}]}}""";
        var secondStart = factory.Requests.Count;
        await loop.PlayOnceAsync(CancellationToken.None);
        var second = factory.Requests[secondStart].Messages;

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count - 2; index++)
        {
            Assert.Equal(first[index].Role, second[index].Role);
            Assert.Equal(first[index].Content, second[index].Content);
        }

        Assert.True(
            !string.Equals(first[^2].Content, second[^2].Content, StringComparison.Ordinal),
            "the state message is the part that must move between steps");
    }

    /// <summary>
    /// The vision path keeps the same order: the screenshot is supporting context, and it is not the
    /// last thing the model reads before it is told to act.
    /// </summary>
    public static async Task PlayOnce_KeepsTheStateLastWhenVisionIsAttached()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[] { new LlmCompletion { Content = "end the turn" } });
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = true;
        // Since 2026-10-28 a screenshot is on demand by default; this test is about where one goes.
        settings.VisionMode = AgentSettings.VisionEveryStep;
        var loop = new AgentLoop(bridge, factory, () => settings);

        await loop.PlayOnceAsync(CancellationToken.None);

        var messages = factory.Requests[0].Messages;
        Assert.Equal(1, bridge.CaptureCalls);
        var screenshot = messages.ToList().FindIndex(message => message.ImageJpeg != null);
        Assert.True(screenshot > 0, "the attached screenshot must be in the request");
        Assert.True(screenshot < messages.Count - 2, "the screenshot must precede the state and the instruction");
        Assert.Contains("Latest compact game state", messages[^2].Content);
        Assert.Contains(PlayPrompt.ActNowInstruction, messages[^1].Content);
    }

    /// <summary>
    /// A model that cannot call tools is told the JSON shape in the static system prompt, which is
    /// also where prefix caching wants it: the instruction does not move per step.
    /// </summary>
    public static async Task PlayOnce_JsonFallbackStaysInTheStaticPrefix()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = """{"action":"end_turn","reason":"No playable cards remain."}""" }
        });
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsTools = false;
        settings.Models[0].SupportsVision = false;
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("end_turn", result.Acted);
        var messages = factory.Requests[0].Messages;
        Assert.Contains(PlayPrompt.JsonActFallback, messages[0].Content);
        Assert.Contains("Latest compact game state", messages[^2].Content);
        Assert.Contains(PlayPrompt.ActNowInstruction, messages[^1].Content);
    }

    /// <summary>
    /// Dual-layer fallback: Jev's low-confidence answer hands the turn to the LLM, and the LLM must
    /// see the planner's standing strategy (goal/posture/instructions/hints) — not only Jev's screen
    /// playbook. Before this fix the macro goal reached Jev but vanished on the fallback path.
    /// </summary>
    public static async Task PlayOnce_FallbackSeesThePlannerGoal()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            }
        });
        var settings = AgentSettings.CreateDefault();
        settings.DualLayerSoloEnabled = true;
        var store = new StrategyStore();
        store.Update(new PlayStrategy
        {
            Goal = "kill the Shrinker Beetle before it buffs",
            Posture = "aggressive",
            Instructions = "open with Bash to apply Vulnerable",
            OptionHints = new Dictionary<string, string>(StringComparer.Ordinal) { ["play_card"] = "prefer attacks" },
            Source = "llm"
        });
        var decider = new StaticDecider(new ExecutionDecision { Confidence = 0.1, Reason = "unsure" });
        var loop = new AgentLoop(bridge, factory, () => settings,
            decider: decider, strategyStore: store, confidenceThreshold: () => 0.35);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("play_card", result.Acted);
        var messages = factory.LastRequest!.Messages;
        var text = string.Join("\n", messages.Select(m => m.Content));
        Assert.Contains("kill the Shrinker Beetle before it buffs", text);
        Assert.Contains("open with Bash to apply Vulnerable", text);
        Assert.Contains("prefer attacks", text);
        // It rides the static prefix, before the per-step state, so the prompt cache survives it.
        var goalIndex = messages.ToList().FindIndex(m => m.Content?.Contains("kill the Shrinker Beetle") == true);
        var stateIndex = messages.ToList().FindIndex(m => m.Content?.Contains("Latest compact game state") == true);
        Assert.True(goalIndex >= 0 && stateIndex > goalIndex, "the strategy must precede the per-step state");
    }

    /// <summary>No strategy written yet (default store) adds nothing: the prompt stays byte-identical.</summary>
    public static async Task PlayOnce_DefaultStrategyAddsNoMessage()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion
            {
                ToolCalls = new[]
                {
                    new LlmToolCall
                    {
                        Id = "call_act",
                        Name = "act",
                        ArgumentsJson = """{"action":"end_turn"}"""
                    }
                }
            }
        });
        var settings = AgentSettings.CreateDefault();
        settings.DualLayerSoloEnabled = true;
        var store = new StrategyStore();
        var decider = new StaticDecider(new ExecutionDecision { Confidence = 0.1, Reason = "unsure" });
        var loop = new AgentLoop(bridge, factory, () => settings,
            decider: decider, strategyStore: store, confidenceThreshold: () => 0.35);

        await loop.PlayOnceAsync(CancellationToken.None);

        var text = string.Join("\n", factory.LastRequest!.Messages.Select(m => m.Content));
        Assert.False(text.Contains("Standing strategy from the planner"), "a default strategy must not cost prompt tokens");
    }

    private sealed class StaticDecider : IActionDecider
    {
        private readonly ExecutionDecision _decision;

        public StaticDecider(ExecutionDecision decision)
        {
            _decision = decision;
        }

        public Task<ExecutionDecision> DecideAsync(string snapshotJson, PlayStrategy strategy, CancellationToken cancellationToken)
        {
            return Task.FromResult(_decision);
        }
    }

    public static async Task PlayOnce_PropagatesCancellation()
    {
        var bridge = new FakeBridge { HonorCancelOnWait = false };
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>()) { CancelCompletions = true };
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var threw = false;
        try
        {
            await loop.PlayOnceAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        Assert.True(threw, "expected cancellation to propagate");
        Assert.Equal(0, bridge.ActCalls);
    }

    public static async Task PlayOnce_UnexpectedExceptionAfterTheRequestStillCountsIt()
    {
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>())
        {
            CompleteThrows = new JsonException("truncated stream")
        };
        var loop = new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("truncated stream", result.Error);
        Assert.True(result.RequestsSpent >= 1, "a request that already left the client must still be counted");
        Assert.False(result.RequiresConfiguration);

        factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>())
        {
            CompleteThrows = new LlmException("HTTP 401", 401)
        };
        result = await new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault)
            .PlayOnceAsync(CancellationToken.None);
        Assert.True(result.RequiresConfiguration);
        Assert.True(result.RequestsSpent >= 1);

        factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>())
        {
            CompleteThrows = new LlmException("HTTP 500", 500)
        };
        result = await new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault)
            .PlayOnceAsync(CancellationToken.None);
        Assert.False(result.RequiresConfiguration);
        Assert.True(result.RequestsSpent >= 1);
    }

    public static async Task Chat_ErrorPathRecordsTheSpentRequestOnTheBudgetGuard()
    {
        var guard = new SessionBudgetGuard(maxRequests: 8);
        Assert.Equal(0, guard.RequestCount);
        var factory = new ScriptedClientFactory(Array.Empty<LlmCompletion>())
        {
            CompleteThrows = new LlmException("provider 500", 500)
        };
        var loop = new AgentLoop(
            new FakeBridge(),
            factory,
            AgentSettings.CreateDefault,
            budgetGuard: () => guard);

        var result = await loop.ChatAsync(
            "hello",
            Array.Empty<ChatTurn>(),
            new ChatOptions
            {
                TeammateConversation = true,
                AttachState = true,
                AttachScreenshot = false
            },
            CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.True(result.RequestsSpent >= 1, "a failed chat turn still spent a request");
        Assert.False(result.RequiresConfiguration);
        Assert.Null(guard.Observe(result));
        Assert.Equal(result.RequestsSpent, guard.RequestCount);

        var source = AgentSourceFixture.ReadAgentRuntime();
        var reply = AgentSourceFixture.MethodBody(source, "ReplyToTeammateAsync");
        var replyAccount = reply.IndexOf("AccountTurn(result, recordBudget: true)", StringComparison.Ordinal);
        var replyError = reply.IndexOf("if (result.Error != null)", StringComparison.Ordinal);
        Assert.True(
            replyAccount >= 0 && replyError >= 0 && replyAccount < replyError,
            "ReplyToTeammateAsync must record the turn before throwing on result.Error.");

        var proactive = AgentSourceFixture.MethodBody(source, "TryProactiveChatAsync");
        var proactiveAccount = proactive.IndexOf("AccountTurn(result, recordBudget: true)", StringComparison.Ordinal);
        var proactiveError = proactive.IndexOf("if (result.Error != null)", StringComparison.Ordinal);
        Assert.True(
            proactiveAccount >= 0 && proactiveError >= 0 && proactiveAccount < proactiveError,
            "TryProactiveChatAsync must record the turn before returning on result.Error.");
    }

    public static void McpRoot_DetectsValidLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "sts2-agent-tests", Guid.NewGuid().ToString("N"), "mcp_server");
        Directory.CreateDirectory(Path.Combine(root, "src", "sts2_mcp"));
        File.WriteAllText(Path.Combine(root, "pyproject.toml"), "name='x'");
        File.WriteAllText(Path.Combine(root, "src", "sts2_mcp", "server.py"), "pass");
        Assert.True(McpProcessLauncher.IsMcpRoot(root));
        Assert.Equal(Path.GetFullPath(root), McpProcessLauncher.FindMcpRoot(root));
        Assert.False(McpProcessLauncher.IsMcpRoot(Path.GetTempPath()));
    }

    public static async Task ModelProbeHonorsPreCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new ProbeClientFactory(_ => Task.FromResult("pong"));
        var loop = new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault);
        var canceled = false;
        try { await loop.TestConfiguredRolesAsync(true, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
        Assert.True(canceled, "A canceled probe must not become a verified or failed model test.");
        Assert.Equal(0, factory.PingCalls);
    }

    public static async Task ModelProbePropagatesInFlightCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var factory = new ProbeClientFactory(token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<string>(token);
        });
        var loop = new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault);
        var canceled = false;
        try { await loop.TestConfiguredRolesAsync(true, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
        Assert.True(canceled, "Caller cancellation must not be cached as endpoint failure.");
        Assert.Equal(1, factory.PingCalls);
    }

    public static void CompletionErrors_ExplainsThinkingModelSilence()
    {
        var sunk = new LlmCompletion { Content = "", Reasoning = new string('x', 900), FinishReason = "length" };
        var message = CompletionErrors.Empty(sunk, null, 1);
        Assert.NotNull(message);
        Assert.Contains("finish_reason=length", message!);
        Assert.Contains("reasoning_content", message);
        Assert.True(CompletionErrors.IsReasoningBudgetExhausted(sunk));

        Assert.False(CompletionErrors.IsReasoningBudgetExhausted(new LlmCompletion { Content = "", Reasoning = "", FinishReason = "length" }));
        Assert.False(CompletionErrors.IsReasoningBudgetExhausted(new LlmCompletion { Content = "answer", Reasoning = "thought", FinishReason = "length" }));
        Assert.False(CompletionErrors.IsReasoningBudgetExhausted(new LlmCompletion { Content = "", Reasoning = "thought", FinishReason = "stop" }));

        var silent = new LlmCompletion { Content = "", Reasoning = "", FinishReason = "stop" };
        Assert.NotNull(CompletionErrors.Empty(silent, null, 3));
    }

    public static async Task PlayOnce_MarksReasoningBudgetExhaustionForRecovery()
    {
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "", Reasoning = "I need to evaluate targets.", FinishReason = "length" },
            new LlmCompletion { Content = "", Reasoning = "I still need to evaluate targets.", FinishReason = "length" }
        });
        var result = await new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.True(result.ReasoningBudgetExhausted);
        Assert.NotNull(result.Error);
        // Changed 2026-10-28 (W6): a reply that spent its whole output on reasoning is not a stream
        // fault, so it is no longer re-sent here; the recovery policy retries it with backoff.
        Assert.Equal(1, result.RequestsSpent);
    }

    /// <summary>
    /// Prose instead of a tool call is answered with a one-line nudge on the same (streamed)
    /// conversation, not a whole non-streaming re-send. Changed 2026-10-28 (W6): the old demotion
    /// fired on any tool-less reply and doubled the cost of 63% of decisions.
    /// </summary>
    public static async Task PlayOnce_DemotesToollessStreamToNonStreamingOnce()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "I will act next." },
            new LlmCompletion
            {
                ToolCalls = new[] { new LlmToolCall { Id = "act-after-nudge", Name = "act", ArgumentsJson = "{\"action\":\"play_card\",\"card_index\":0}" } }
            }
        });

        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(2, factory.Requests.Count);
        Assert.True(factory.Requests[0].Stream);
        Assert.True(factory.Requests[1].Stream, "A nudge keeps the stream; only an empty reply demotes it.");
        Assert.Equal(AgentLoop.ActNowNudge, factory.Requests[1].Messages[^1].Content);
        Assert.False(result.StreamDemoted);
        Assert.Equal(2, result.RequestsSpent);
    }

    public static async Task PlayOnce_SilentStreamDemotesOnce()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "" },
            new LlmCompletion
            {
                ToolCalls = new[] { new LlmToolCall { Id = "act-after-demotion", Name = "act", ArgumentsJson = "{\"action\":\"play_card\",\"card_index\":0}" } }
            }
        });

        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(2, factory.Requests.Count);
        Assert.True(factory.Requests[0].Stream);
        Assert.False(factory.Requests[1].Stream, "An empty streamed reply is the provider failing to stream tools.");
        Assert.True(result.StreamDemoted);
    }

    public static async Task PlayOnce_NudgeThatFailsThenDemotesAndStops()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "Thinking about it." },
            new LlmCompletion { Content = "Still thinking." },
            new LlmCompletion { Content = "No idea." }
        });

        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Null(result.Acted);
        Assert.NotNull(result.Error);
        Assert.Equal(3, factory.Requests.Count, "prose → nudge → one non-streaming re-send, then give up");
        Assert.False(factory.Requests[2].Stream);
        Assert.Equal(0, bridge.ActCalls);
    }

    /// <summary>
    /// A tool-capable model that writes its act as JSON in the reply is taken at its word (W7.4):
    /// asking again cost a full request for an answer already given.
    /// </summary>
    public static async Task PlayOnce_JsonActInTextRunsEvenWithNativeTools()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "{\"action\":\"play_card\",\"card_index\":0,\"reason\":\"only attack\"}" }
        });

        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(1, factory.Requests.Count);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Equal(1, bridge.ActCalls);
    }

    /// <summary>
    /// The in-game loop already carries the fresh state, so it does not advertise the state readers;
    /// the MCP surfaces keep them (ADR 0002).
    /// </summary>
    public static async Task PlayOnce_AdvertisesNoStateReadersAndCountsReadRounds()
    {
        var bridge = new FakeBridge();
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "read", Name = "get_game_state", ArgumentsJson = "{}" } } },
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "act", Name = "act", ArgumentsJson = "{\"action\":\"play_card\",\"card_index\":0}" } } }
        });

        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        var advertised = factory.Requests[0].Tools!.Select(tool => tool.Name).ToArray();
        Assert.False(advertised.Contains("get_game_state"));
        Assert.False(advertised.Contains("get_raw_game_state"));
        Assert.False(advertised.Contains("get_available_actions"));
        Assert.True(advertised.Contains("act") && advertised.Contains("get_relevant_game_data"));
        Assert.True(AgentTools.Mcp.Any(tool => tool.Name == "get_game_state"), "The MCP surface keeps the state reader.");
        Assert.Equal("play_card", result.Acted, "A state reader the model names anyway is still answered.");
        Assert.Equal(2, result.ToolRounds);
        Assert.Equal(1, result.ReadToolRounds);
        Assert.Contains(PlayPrompt.ActNowInstruction, factory.Requests[0].Messages[^1].Content);
    }

    public static async Task PlayOnce_DemotesTruncatedActStreamToNonStreamingOnce()
    {
        // Two playable cards: the missing index cannot be inferred, so the stream really is broken.
        // (A single-card hand is completed instead; see PlayOnce_FillsTheOnlyPossibleIndex.)
        var bridge = new FakeBridge
        {
            CompactStateJson = """{"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"line":"Strike","targets":[]},{"i":1,"line":"Defend","targets":[]}],"enemies":[{"i":0}]}}"""
        };
        var factory = new ScriptedClientFactory(new[]
        {
            // What the gateway actually streamed live: a closed JSON object holding only the action.
            new LlmCompletion
            {
                ToolCalls = new[] { new LlmToolCall { Id = "truncated", Name = "act", ArgumentsJson = "{\"action\":\"play_card\"}" } }
            },
            new LlmCompletion
            {
                ToolCalls = new[] { new LlmToolCall { Id = "complete", Name = "act", ArgumentsJson = "{\"action\":\"play_card\",\"card_index\":0}" } }
            }
        });

        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal(2, factory.Requests.Count);
        Assert.True(factory.Requests[0].Stream);
        Assert.False(factory.Requests[1].Stream);
    }

    /// <summary>
    /// W7: an act whose index was cut off is completed from the frame when only one choice exists,
    /// instead of re-sending the whole request; and a provider that truncates twice stops being
    /// streamed for act turns.
    /// </summary>
    public static async Task PlayOnce_FillsTheOnlyPossibleIndex()
    {
        var bridge = new FakeBridge
        {
            CompactStateJson = """{"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"line":"Strike","playable":true,"targets":[0]},{"i":1,"line":"Wound","playable":false,"targets":[]}],"enemies":[{"i":0,"alive":true,"hittable":true}]}}"""
        };
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { ToolCalls = new[] { new LlmToolCall { Id = "cut", Name = "act", ArgumentsJson = "{\"action\":\"play_card\"}" } } }
        });
        var result = await new AgentLoop(bridge, factory, AgentSettings.CreateDefault).PlayOnceAsync(CancellationToken.None);
        Assert.Equal("play_card", result.Acted);
        Assert.Equal(1, factory.Requests.Count, "The only playable card and its only target need no second request.");
        Assert.Equal(((string, int?, int?))("play_card", 0, 0), bridge.ActLog[0]);
    }

    public static async Task PlayOnce_LearnsThatStreamedArgumentsAreTruncated()
    {
        var twoCards = """{"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"line":"Strike","targets":[]},{"i":1,"line":"Defend","targets":[]}],"enemies":[{"i":0}]}}""";
        LlmCompletion Truncated() => new() { ToolCalls = new[] { new LlmToolCall { Id = "t", Name = "act", ArgumentsJson = "{\"action\":\"play_card\"}" } } };
        LlmCompletion Complete() => new() { ToolCalls = new[] { new LlmToolCall { Id = "c", Name = "act", ArgumentsJson = "{\"action\":\"play_card\",\"card_index\":0}" } } };
        var factory = new ScriptedClientFactory(new[] { Truncated(), Complete(), Truncated(), Complete(), Complete() });
        var loop = new AgentLoop(new FakeBridge { CompactStateJson = twoCards }, factory, AgentSettings.CreateDefault);
        await loop.PlayOnceAsync(CancellationToken.None);
        await loop.PlayOnceAsync(CancellationToken.None);
        await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(5, factory.Requests.Count);
        Assert.True(factory.Requests[0].Stream && factory.Requests[2].Stream, "The first two turns still try streaming.");
        Assert.False(factory.Requests[4].Stream, "After two truncations the act turn goes out without streaming from the start.");
    }

    public static Task TruncatedStreamedActDetection()
    {
        Assert.True(AgentLoop.LooksLikeTruncatedStreamedAct("{\"action\":\"play_card\"}"));
        Assert.True(AgentLoop.LooksLikeTruncatedStreamedAct("{\"action\":\"choose_map_node\"}"));
        Assert.False(AgentLoop.LooksLikeTruncatedStreamedAct("{\"action\":\"play_card\",\"card_index\":2}"));
        Assert.False(AgentLoop.LooksLikeTruncatedStreamedAct("{\"action\":\"choose_map_node\",\"option_index\":1}"));
        Assert.False(AgentLoop.LooksLikeTruncatedStreamedAct("{\"action\":\"end_turn\"}"));
        Assert.False(AgentLoop.LooksLikeTruncatedStreamedAct("{\"action\":\"play_card\",\"card_cid\":17}"),
            "A card named by card_cid is complete (W3).");
        Assert.False(AgentLoop.LooksLikeTruncatedStreamedAct("{\"action\":"));
        Assert.True(AgentLoop.IsMissingIndexError("target_index must come from the latest payload for this card."));
        Assert.True(AgentLoop.IsMissingIndexError("card_index must come from the latest combat.hand."));
        Assert.False(AgentLoop.IsMissingIndexError("Action is not in available_actions."));
        Assert.False(AgentLoop.IsMissingIndexError(null));
        return Task.CompletedTask;
    }

    /// <summary>
    /// The turn's own request carries the live-reasoning callback, so a partial the client reports while
    /// the reply is streaming reaches the caller that shows it. Without this line the whole streaming
    /// path exists and nothing in game ever sees it.
    /// </summary>
    public static async Task ReasoningDeltaReachesTheTurnCallback()
    {
        var factory = new ScriptedClientFactory(new[]
        {
            new LlmCompletion { Content = "done", Reasoning = "final thought" }
        });

        var seen = new List<string>();
        // The fake client reports a partial exactly where the real one does: mid-request.
        factory.OnRequest = () => factory.LastRequest!.OnReasoningDelta?.Invoke("live partial");
        var loop = new AgentLoop(
            new FakeBridge(),
            factory,
            AgentSettings.CreateDefault,
            onReasoningDelta: seen.Add);

        await loop.PlayOnceAsync(CancellationToken.None);

        // The default model supports tools and the fake answers with text only, so the turn demotes the
        // stream once and asks again: every request it makes reports through the same callback.
        Assert.True(seen.Count > 0, "the turn's reasoning callback never fired");
        Assert.True(seen.All(partial => partial == "live partial"), "a request reported something else");
        Assert.Equal(factory.Requests.Count, seen.Count);
        Assert.True(
            factory.Requests.All(request => request.OnReasoningDelta != null),
            "a model request was dispatched without the live-reasoning callback");
    }

    public static async Task ModelProbeStillReportsProviderFailure()
    {
        var factory = new ProbeClientFactory(_ => Task.FromException<string>(new OperationCanceledException("provider timeout")));
        var loop = new AgentLoop(new FakeBridge(), factory, AgentSettings.CreateDefault);
        var result = await loop.TestConfiguredRolesAsync(true, CancellationToken.None);
        Assert.Equal("failed", result.First(item => item.Role == ModelRoleNames.Play).Record.Status);
    }

    private sealed class ProbeClientFactory(Func<CancellationToken, Task<string>> ping) : ILlmClientFactory, ILlmClient
    {
        public int PingCalls { get; private set; }
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) => this;
        public Task<string> PingAsync(string model, CancellationToken cancellationToken)
        {
            PingCalls++;
            return ping(cancellationToken);
        }
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A model probe must not invoke gameplay completion.");
        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FakeBridge : IGameBridge
    {
        public Queue<string> ActionSnapshots { get; } = new();

        public int ActCalls { get; private set; }

        public int WaitCalls { get; private set; }

        public int CaptureCalls { get; private set; }

        public string? LastAction { get; private set; }

        public int? LastX { get; private set; }

        public int? LastY { get; private set; }

        public string? LastTool { get; private set; }

        public bool Actionable { get; set; } = true;

        public bool HonorCancelOnWait { get; set; } = true;

        public string ActResultJson { get; set; } = """{"action":"play_card","status":"completed","stable":true}""";

        public string CompactStateJson { get; set; } =
            """{"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"line":"Strike","targets":[]}],"enemies":[{"i":0}]}}""";

        public string? AfterActCompactStateJson { get; set; }
        public string? AtDispatchStateJson { get; set; }

        /// <summary>States the game moves through, one per act (wins over AfterActCompactStateJson).</summary>
        public Queue<string> AfterActStates { get; } = new();

        /// <summary>Every dispatched act as (action, card_index, target_index).</summary>
        public List<(string Action, int? Card, int? Target)> ActLog { get; } = new();

        public string AvailableActionsJson { get; set; } =
            """[{"name":"play_card","requires_index":true,"requires_target":false}]""";

        public string Screen { get; set; } = "COMBAT";

        public int StateReads { get; private set; }

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken)
        {
            StateReads++;
            return Task.FromResult(CompactStateJson);
        }

        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("""{"screen":"COMBAT","raw":true}""");
        }

        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(AvailableActionsJson);
        }

        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken)
        {
            if (ActionSnapshots.Count > 0)
            {
                return Task.FromResult(ActionSnapshots.Dequeue());
            }

            // The same two halves the real bridge reads in one game-thread turn.
            using var state = JsonDocument.Parse(CompactStateJson);
            using var actions = JsonDocument.Parse(AvailableActionsJson);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                state = state.RootElement.Clone(),
                available_actions = actions.RootElement.Clone()
            }));
        }

        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult(Screen);

        public Task<string> ActAsync(
            string action,
            int? cardIndex,
            int? targetIndex,
            int? optionIndex,
            int? x,
            int? y,
            string? tool,
            CancellationToken cancellationToken,
            bool rawState = false,
            Action<string>? validateState = null)
        {
            validateState?.Invoke(AtDispatchStateJson ?? CompactStateJson);
            ActCalls++;
            LastAction = action;
            LastX = x;
            LastY = y;
            LastTool = tool;
            ActLog.Add((action, cardIndex, targetIndex));
            if (AfterActStates.Count > 0)
            {
                CompactStateJson = AfterActStates.Dequeue();
            }
            else if (AfterActCompactStateJson != null)
            {
                CompactStateJson = AfterActCompactStateJson;
            }

            return Task.FromResult(ActResultJson);
        }

        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken)
        {
            return Task.FromResult("null");
        }

        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
        {
            return Task.FromResult("{}");
        }

        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
        {
            return Task.FromResult("{}");
        }

        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (HonorCancelOnWait)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            WaitCalls++;
            return Task.FromResult(Actionable);
        }

        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult<byte[]?>(new byte[] { 0xFF, 0xD8 });
        }
    }

    private sealed class ScriptedClientFactory : ILlmClientFactory
    {
        private readonly Queue<LlmCompletion> _completions;

        public ScriptedClientFactory(IEnumerable<LlmCompletion> completions)
        {
            _completions = new Queue<LlmCompletion>(completions);
        }

        public LlmRequest? LastRequest { get; private set; }

        public List<LlmRequest> Requests { get; } = new();

        public bool CancelCompletions { get; set; }
        public Action? OnRequest { get; set; }
        public Exception? CompleteThrows { get; set; }

        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? requestTimeout = null) =>
            new ScriptedClient(
                _completions,
                request => { LastRequest = request; Requests.Add(request); OnRequest?.Invoke(); },
                CancelCompletions,
                CompleteThrows);
    }

    private sealed class ScriptedClient : ILlmClient
    {
        private readonly Queue<LlmCompletion> _completions;
        private readonly Action<LlmRequest> _onRequest;
        private readonly bool _cancelCompletions;
        private readonly Exception? _completeThrows;

        public ScriptedClient(
            Queue<LlmCompletion> completions,
            Action<LlmRequest> onRequest,
            bool cancelCompletions,
            Exception? completeThrows)
        {
            _completions = completions;
            _onRequest = onRequest;
            _cancelCompletions = cancelCompletions;
            _completeThrows = completeThrows;
        }

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (_cancelCompletions && cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<LlmCompletion>(cancellationToken);
            }

            _onRequest(request);
            if (_completeThrows != null)
            {
                throw _completeThrows;
            }
            if (_completions.Count == 0)
            {
                return Task.FromResult(new LlmCompletion { Content = "done" });
            }

            return Task.FromResult(_completions.Dequeue());
        }

        public Task<string> PingAsync(string model, CancellationToken cancellationToken) => Task.FromResult("pong");

        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}

internal static class GameDataExportSchemaTests
{
    /// <summary>
    /// Every (scene, collection, field) the filter projects must be a field the game really
    /// exports. Silent skipping in <c>ProjectFields</c> is exactly why a typo here costs the
    /// model its most important combat numbers, so collect every violation at once.
    /// </summary>
    public static void SceneFieldsExistInTheExportSchema()
    {
        var violations = new List<string>();
        foreach (var scene in GameDataFilter.SceneFieldSetView)
        {
            foreach (var collection in scene.Value)
            {
                if (!GameDataExportSchema.TryGetFields(collection.Key, out var exported))
                {
                    violations.Add($"{scene.Key}/{collection.Key}: collection is not an exported /data collection");
                    continue;
                }

                var exportedSet = new HashSet<string>(exported, StringComparer.Ordinal);
                foreach (var field in collection.Value)
                {
                    if (!exportedSet.Contains(field))
                    {
                        violations.Add(
                            $"{scene.Key}/{collection.Key}: '{field}' is not exported (exports: {string.Join(", ", exported)})");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Scene field sets reference fields the game never exports: " + string.Join(" | ", violations));
    }

    /// <summary>
    /// Reverse drift guard: every field named in the inventory must still be assigned inside the
    /// matching <c>ExportXxx()</c> method body of <c>GameDataExportService</c>. Scoping to the
    /// method body keeps an unrelated name elsewhere in the file from satisfying the assertion.
    /// </summary>
    public static void ExportSchemaFieldsAppearInTheExportCode()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameDataExportService.cs");
        var missing = new List<string>();
        foreach (var pair in GameDataExportSchema.Collections)
        {
            var methodName = "Export" + char.ToUpperInvariant(pair.Key[0]) + pair.Key[1..];
            var body = AgentSourceFixture.MethodBody(source, methodName);
            foreach (var field in pair.Value)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(body, $@"\b{System.Text.RegularExpressions.Regex.Escape(field)}\s*="))
                {
                    missing.Add($"{pair.Key}.{field} ({methodName})");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Export schema lists fields GameDataExportService never assigns: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The filter's known collection list must stay the same set as the export contract.
    /// </summary>
    public static void KnownCollectionsMatchTheExportSchema()
    {
        var known = new HashSet<string>(GameDataFilter.KnownCollections, StringComparer.Ordinal);
        var exported = new HashSet<string>(GameDataExportSchema.Collections.Keys, StringComparer.Ordinal);

        Assert.True(
            known.SetEquals(exported),
            $"KnownCollections [{string.Join(", ", known.OrderBy(value => value, StringComparer.Ordinal))}] does not match export schema [{string.Join(", ", exported.OrderBy(value => value, StringComparer.Ordinal))}]");
    }
}
