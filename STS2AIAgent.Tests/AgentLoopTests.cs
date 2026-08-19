using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Tests;

internal static class GameDataFilterTests
{
    public static void DetectScene_MatchesGuidedMcpRules()
    {
        Assert.Equal("combat", GameDataFilter.DetectScene("COMBAT"));
        Assert.Equal("shop", GameDataFilter.DetectScene("SHOP"));
        Assert.Equal("event", GameDataFilter.DetectScene("EVENT"));
        Assert.Equal("menu", GameDataFilter.DetectScene("REWARD"));
        Assert.Equal("menu", GameDataFilter.DetectScene("MAP"));
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
    }

    public static void DetectsUnsettledActResults()
    {
        Assert.True(ActIndexValidator.IsUnsettled("""{"status":"pending","stable":true}"""));
        Assert.True(ActIndexValidator.IsUnsettled("""{"status":"completed","stable":false}"""));
        Assert.False(ActIndexValidator.IsUnsettled("""{"status":"completed","stable":true}"""));
    }
}

internal static class AgentLoopTests
{
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
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            }
        });
        var settings = AgentSettings.CreateDefault();
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("play_card", result.Acted);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Null(result.Error);
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
            new LlmCompletion { Content = """{"action":"end_turn"}""" }
        });
        var settings = AgentSettings.CreateDefault();
        settings.Models[0].SupportsVision = false;
        settings.Models[0].SupportsTools = false;
        settings.VisionModelId = null;
        var loop = new AgentLoop(bridge, factory, () => settings);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal("end_turn", result.Acted);
        Assert.Equal(1, bridge.ActCalls);
        Assert.Null(result.Error);
        Assert.Equal(0, bridge.CaptureCalls);
        Assert.Null(factory.LastRequest?.Tools);
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
                        ArgumentsJson = """{"action":"play_card","card_index":9}"""
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
                        ArgumentsJson = """{"action":"play_card","card_index":0}"""
                    }
                }
            }
        });
        var loop = new AgentLoop(bridge, factory, AgentSettings.CreateDefault);

        var result = await loop.PlayOnceAsync(CancellationToken.None);

        Assert.Equal(1, bridge.ActCalls);
        Assert.Equal("play_card", result.Acted);
        Assert.Null(result.Error);
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

    private sealed class FakeBridge : IGameBridge
    {
        public int ActCalls { get; private set; }

        public int WaitCalls { get; private set; }

        public int CaptureCalls { get; private set; }

        public bool Actionable { get; set; } = true;

        public bool HonorCancelOnWait { get; set; } = true;

        public string ActResultJson { get; set; } = """{"action":"play_card","status":"completed","stable":true}""";

        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(
                """{"screen":"COMBAT","available_actions":["play_card","end_turn"],"combat":{"hand":[{"i":0,"line":"Strike","targets":[]}],"enemies":[{"i":0}]}}""");
        }

        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("""{"screen":"COMBAT","raw":true}""");
        }

        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("""[{"name":"play_card","requires_index":true,"requires_target":false}]""");
        }

        public Task<IReadOnlyList<string>> GetAvailableActionNamesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>(new[] { "play_card", "end_turn" });
        }

        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("COMBAT");

        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, CancellationToken cancellationToken)
        {
            ActCalls++;
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

        public ILlmClient Create(LlmEndpoint endpoint) =>
            new ScriptedClient(_completions, request => LastRequest = request, CancelCompletions);
    }

    private sealed class ScriptedClient : ILlmClient
    {
        private readonly Queue<LlmCompletion> _completions;
        private readonly Action<LlmRequest> _onRequest;
        private readonly bool _cancelCompletions;

        public ScriptedClient(Queue<LlmCompletion> completions, Action<LlmRequest> onRequest, bool cancelCompletions)
        {
            _completions = completions;
            _onRequest = onRequest;
            _cancelCompletions = cancelCompletions;
        }

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (_cancelCompletions && cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<LlmCompletion>(cancellationToken);
            }

            _onRequest(request);
            if (_completions.Count == 0)
            {
                return Task.FromResult(new LlmCompletion { Content = "done" });
            }

            return Task.FromResult(_completions.Dequeue());
        }

        public Task<string> PingAsync(string model, CancellationToken cancellationToken) => Task.FromResult("pong");
    }
}
