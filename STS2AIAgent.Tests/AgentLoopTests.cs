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
            new ChatOptions { TeammateConversation = true, AllowAct = true }, CancellationToken.None);
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
            new ChatOptions { ReadOnly = true, AllowAct = true }, CancellationToken.None);
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
            new ChatOptions { AttachState = false, AttachScreenshot = false, AllowAct = false },
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
            new ChatOptions { AttachState = false, AttachScreenshot = false, AllowAct = false },
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
            new ChatOptions { AttachState = false, AttachScreenshot = false, AllowAct = false },
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

        var messages = factory.LastRequest!.Messages;
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
        Assert.Contains("Choose the next legal action", messages[^1].Content);

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
        var first = factory.LastRequest!.Messages;

        // Same screen, different turn: everything before the state has to be byte-identical, which is
        // what lets a provider reuse the prefix it cached for the previous step.
        bridge.CompactStateJson =
            """{"screen":"COMBAT","available_actions":["end_turn"],"combat":{"hand":[{"i":1,"line":"Defend","targets":[]}],"enemies":[{"i":0}]}}""";
        await loop.PlayOnceAsync(CancellationToken.None);
        var second = factory.LastRequest!.Messages;

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
        var loop = new AgentLoop(bridge, factory, () => settings);

        await loop.PlayOnceAsync(CancellationToken.None);

        var messages = factory.LastRequest!.Messages;
        Assert.Equal(1, bridge.CaptureCalls);
        var screenshot = messages.ToList().FindIndex(message => message.ImageJpeg != null);
        Assert.True(screenshot > 0, "the attached screenshot must be in the request");
        Assert.True(screenshot < messages.Count - 2, "the screenshot must precede the state and the instruction");
        Assert.Contains("Latest compact game state", messages[^2].Content);
        Assert.Contains("Choose the next legal action", messages[^1].Content);
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
        var messages = factory.LastRequest!.Messages;
        Assert.Contains(PlayPrompt.JsonActFallback, messages[0].Content);
        Assert.Contains("Latest compact game state", messages[^2].Content);
        Assert.Contains("Choose the next legal action", messages[^1].Content);
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
        var sank = new LlmCompletion { Content = "", Reasoning = new string('x', 900), FinishReason = "length" };
        var message = CompletionErrors.Empty(sank, null, 1);
        Assert.NotNull(message);
        Assert.Contains("finish_reason=length", message!);
        Assert.Contains("reasoning_content", message);

        var silent = new LlmCompletion { Content = "", Reasoning = "", FinishReason = "stop" };
        Assert.NotNull(CompletionErrors.Empty(silent, null, 3));
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
        public ILlmClient Create(LlmEndpoint endpoint) => this;
        public Task<string> PingAsync(string model, CancellationToken cancellationToken)
        {
            PingCalls++;
            return ping(cancellationToken);
        }
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A model probe must not invoke gameplay completion.");
    }

    private sealed class FakeBridge : IGameBridge
    {
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

        public string AvailableActionsJson { get; set; } =
            """[{"name":"play_card","requires_index":true,"requires_target":false}]""";

        public string Screen { get; set; } = "COMBAT";

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken)
        {
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
            bool rawState = false)
        {
            ActCalls++;
            LastAction = action;
            LastX = x;
            LastY = y;
            LastTool = tool;
            if (AfterActCompactStateJson != null)
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

        public bool CancelCompletions { get; set; }
        public Action? OnRequest { get; set; }
        public Exception? CompleteThrows { get; set; }

        public ILlmClient Create(LlmEndpoint endpoint) =>
            new ScriptedClient(
                _completions,
                request => { LastRequest = request; OnRequest?.Invoke(); },
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
