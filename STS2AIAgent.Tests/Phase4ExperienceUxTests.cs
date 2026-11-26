using System.Diagnostics;
using System.Text.Json;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// Offline tests for Phase 4 player UX upgrades: 3-tier play presets, spectator dwell,
/// cross-run playstyle preference notes, co-op quick tactical intents, and actionable model
/// troubleshooting hints.
/// </summary>
internal static class Phase4ExperienceUxTests
{
    public static void PlayStylePresetsApplyDetectAndPreserveSecrets()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].ApiKey = "sk-keep-me";
        settings.PlayerPreferences = "Prefer lean Ironclad decks";
        settings.PauseHotkey = "F6";

        Assert.Equal(PlayStylePresetPolicy.Balanced, PlayStylePresetPolicy.Detect(settings));

        Assert.True(PlayStylePresetPolicy.Apply(settings, PlayStylePresetPolicy.Economy));
        Assert.Equal(PlayStylePresetPolicy.Economy, PlayStylePresetPolicy.Detect(settings));
        Assert.Equal(AgentSettings.VisionOff, settings.VisionMode);
        Assert.Equal(4096, settings.MaxOutputTokens);
        Assert.Equal(0, settings.SpectatorHighlightMs);
        Assert.Equal("sk-keep-me", settings.Endpoints[0].ApiKey);
        Assert.Equal("Prefer lean Ironclad decks", settings.PlayerPreferences);
        Assert.Equal("F6", settings.PauseHotkey);

        settings.HudMode = AgentSettings.HudOff;
        Assert.True(PlayStylePresetPolicy.Apply(settings, PlayStylePresetPolicy.Push));
        Assert.Equal(PlayStylePresetPolicy.Push, PlayStylePresetPolicy.Detect(settings));
        Assert.Equal(AgentSettings.TurboFast, settings.TurboMode);
        Assert.Equal(AgentSettings.HudFull, settings.HudMode, "Push preset must enable Full HUD so spectator dwell is visible.");
        Assert.Equal(400, settings.SpectatorHighlightMs);
        Assert.Equal(AgentSettings.DefaultMaxOutputTokens, settings.MaxOutputTokens);

        settings.SpectatorHighlightMs = 800;
        Assert.Null(PlayStylePresetPolicy.Detect(settings));
        Assert.False(PlayStylePresetPolicy.Apply(settings, "nonexistent"));
    }

    public static void SpectatorDwellAndPreferencesNormalizeCloneAndSyncCorrectly()
    {
        var host = AgentSettings.CreateDefault();
        host.SpectatorHighlightMs = 99999;
        host.MaxOutputTokens = 5_000_000;
        // Include control characters and a UTF-16 surrogate pair straddling the 400-char cap.
        host.PlayerPreferences = "  \0\x07" + new string('a', AgentSettings.MaxPlayerPreferencesLength - 1) + "😀extra  ";
        host.EnsureValidShape();
        Assert.Equal(AgentSettings.MaxSpectatorHighlightMs, host.SpectatorHighlightMs);
        Assert.Equal(AgentSettings.MaxMaxOutputTokens, host.MaxOutputTokens);
        Assert.False(host.PlayerPreferences.Contains('\0'));
        Assert.False(host.PlayerPreferences.Contains('\x07'));
        Assert.True(host.PlayerPreferences.Length <= AgentSettings.MaxPlayerPreferencesLength);
        Assert.False(char.IsHighSurrogate(host.PlayerPreferences[^1]), "Truncation must not split a UTF-16 surrogate pair.");

        var clone = SettingsClone.Clone(host);
        Assert.Equal(host.SpectatorHighlightMs, clone.SpectatorHighlightMs);
        Assert.Equal(host.PlayerPreferences, clone.PlayerPreferences);

        var wire = JsonSerializer.Serialize(CompanionPlaySettings.From(host));
        Assert.False(wire.Contains("SpectatorHighlightMs", StringComparison.Ordinal), "Spectator dwell is host-local.");
        var received = JsonSerializer.Deserialize<CompanionPlaySettings>(wire)!;
        Assert.True(received.IsValid);
        var companion = AgentSettings.CreateDefault();
        companion.SpectatorHighlightMs = 800; // e.g., seeded from host settings.json
        received.ApplyTo(companion);
        Assert.Equal(0, companion.SpectatorHighlightMs, "Companion ApplyTo must clear any inherited spectator dwell.");
        Assert.Equal(host.PlayerPreferences, companion.PlayerPreferences);
    }

    public static void TeamQuickCommandsCarryStructuredIntents()
    {
        var commands = TeamQuickCommands.BuildAll();
        Assert.True(commands.Count >= 4);
        var focus = commands.First(c => c.Id == "focus_0");
        Assert.NotNull(focus.Intent);
        Assert.Equal(TeamIntent.FocusFire, focus.Intent!.type);
        Assert.Equal(0, focus.Intent.enemy_index);

        var target = commands.First(c => c.Id == "target_0");
        Assert.NotNull(target.Intent);
        Assert.Equal(TeamIntent.TargetAnnounce, target.Intent!.type);
        Assert.Equal(0, target.Intent.enemy_index);

        var splitConvo = new TeamConversation();
        splitConvo.Add("user", focus.Message, focus.Intent);
        var splitCtx = splitConvo.BuildTeamContext()!;
        Assert.Contains("enemy_index 0", splitCtx);
        Assert.Contains("避免两人重复集火", splitCtx);

        var focusTogetherConvo = new TeamConversation();
        focusTogetherConvo.Add("user", target.Message, target.Intent);
        var togetherCtx = focusTogetherConvo.BuildTeamContext()!;
        Assert.Contains("一起集火 enemy_index 0", togetherCtx);
        Assert.False(togetherCtx.Contains("不要把伤害再倾泻", StringComparison.Ordinal),
            "Focus-together quick command must NOT tell the AI teammate to avoid enemy 0.");

        // Dynamic target selection: skip dead / unhittable enemies and pick the first standing hittable index.
        Assert.Equal(0, TeamQuickCommands.ResolveTargetEnemyIndex(null));
        Assert.Equal(2, TeamQuickCommands.ResolveTargetEnemyIndex(new[] { (0, false, false), (1, true, false), (2, true, true) }));
        Assert.Equal(1, TeamQuickCommands.ResolveTargetEnemyIndex(new[] { (0, false, false), (1, true, false) }));
        Assert.Equal(0, TeamQuickCommands.ResolveTargetEnemyIndex(new[] { (0, false, false) }));

        var shifted = TeamQuickCommands.BuildAll(2);
        var shiftedFocus = shifted.First(c => c.Id == "focus_0");
        var shiftedTarget = shifted.First(c => c.Id == "target_0");
        Assert.Equal(2, shiftedFocus.Intent!.enemy_index);
        Assert.Equal(2, shiftedTarget.Intent!.enemy_index);
        Assert.Contains("2", shiftedFocus.Label);
        Assert.Contains("2", shiftedTarget.Label);
    }

    public static void ModelProbeClassifiesActionableTroubleshootingCases()
    {
        var model = new ResolvedModel(
            new LlmEndpoint { Id = "e1", Name = "ProviderX", BaseUrl = "https://api.example/v1" },
            new LlmModelConfig { Id = "m1", EndpointId = "e1", Model = "demo-model" });

        var quota = ModelRoleProbe.Classify(429, "You exceeded your current quota / insufficient_quota", model);
        Assert.Contains("额度不足", quota.Error);
        Assert.Contains("充值", quota.NextStep);
        Assert.Equal("config", ModelRoleProbe.FailureKind(429, "insufficient_quota"));
        Assert.Equal("config", ModelRoleProbe.FailureKind(402, "Payment Required"));

        var balance402 = ModelRoleProbe.Classify(402, "Payment Required", model);
        Assert.Contains("余额", balance402.Error);

        var region = ModelRoleProbe.Classify(403, "unsupported_country_region_territory", model);
        Assert.Contains("地区", region.Error);
        Assert.Contains("代理", region.NextStep);

        var awsIam403 = ModelRoleProbe.Classify(403, "User is not authorized in region us-east-1", model);
        Assert.Contains("认证失败", awsIam403.Error);

        var noTools = ModelRoleProbe.Classify(422, "This model does not support tools", model);
        Assert.Contains("工具调用", noTools.Error);
        Assert.Contains("Tool Calling", noTools.NextStep);

        var lengthWithToolWord = ModelRoleProbe.Classify(400, "request with tools exceeded maximum context length", model);
        Assert.Contains("长度参数", lengthWithToolWord.Error);
        Assert.Contains("省钱速通", lengthWithToolWord.NextStep);

        var http400Failed = ModelRoleProbe.Classify(400, "Request failed: invalid parameter", model);
        Assert.False(http400Failed.Error.Contains("无法连接", StringComparison.Ordinal), "HTTP 400 must not be misclassified as a connection failure.");

        var tls = ModelRoleProbe.Classify(null, "The SSL connection could not be established", model);
        Assert.Contains("TLS", tls.Error);

        var unnamedWithSecret = new ResolvedModel(
            new LlmEndpoint { Id = "e2", Name = "", BaseUrl = "https://api.example/v1?api_key=sk-supersecretkey1234567890" },
            new LlmModelConfig { Id = "m2", EndpointId = "e2", Model = "demo-model" });
        var redacted = ModelRoleProbe.Classify(400, "Incorrect API key provided: sk-supersecretkey1234567890", unnamedWithSecret);
        Assert.False(redacted.Error.Contains("sk-supersecretkey1234567890", StringComparison.Ordinal), "Secrets must be redacted from probe output.");
    }

    public static async Task PlayerPreferencesInjectIntoStaticPrefixAndSpectatorDwellIsBounded()
    {
        var stateJson = """
            {"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["play_card","end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"player":{"hp":"70/80","block":0,"energy":3},
                       "hand":[{"i":0,"cid":10,"line":"Strike","playable":true,"targets":[0]}],
                       "enemies":[{"i":0,"hp":"20/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        var shiftedStateJson = """
            {"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["play_card","end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"player":{"hp":"70/80","block":0,"energy":3},
                       "hand":[{"i":0,"cid":99,"line":"Defend","playable":true,"targets":[]}],
                       "enemies":[{"i":0,"hp":"20/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        var actionsJson = """[{"name":"play_card","requires_index":true,"requires_target":true},{"name":"end_turn"}]""";
        var snapshotJson = $"{{\"state\":{stateJson},\"available_actions\":{actionsJson}}}";

        var capturedMessages = new List<LlmMessage>();
        var settings = AgentSettings.CreateDefault();
        settings.AutopilotEnabled = false;
        settings.TurnPlansEnabled = false;
        settings.SpectatorHighlightMs = 45;
        settings.HudMode = AgentSettings.HudFull;
        settings.PlayerPreferences = "Act 1 血量健康优先打精英，少拿重复攻击牌";

        Action<string>? capturedValidator = null;
        var bridge = new FakeBridge(stateJson, actionsJson, snapshotJson, v => capturedValidator = v);
        var llm = new FakeLlmFactory(req =>
        {
            capturedMessages.AddRange(req.Messages);
            return new LlmCompletion
            {
                ToolCalls = new List<LlmToolCall>
                {
                    new() { Id = "tc1", Name = "act", ArgumentsJson = """{"action":"play_card","card_index":0,"target_index":0,"reason":"test"}""" }
                }
            };
        });

        var loop = new AgentLoop(bridge, llm, () => settings);
        var sw = Stopwatch.StartNew();
        var result = await loop.PlayOnceAsync(CancellationToken.None);
        sw.Stop();

        Assert.Equal("play_card", result.Acted);
        Assert.True(sw.ElapsedMilliseconds >= 35, $"Expected spectator dwell >= 35ms, got {sw.ElapsedMilliseconds}ms.");
        var prefMsg = capturedMessages.FirstOrDefault(m => m.Role == "user" && (m.Content?.Contains("Standing player playstyle preferences", StringComparison.Ordinal) == true));
        Assert.NotNull(prefMsg);
        Assert.Equal(ContextPart.Static, prefMsg!.ContextPart);
        Assert.Contains("Act 1 血量健康优先打精英", prefMsg.Content);

        Assert.NotNull(capturedValidator);
        var threwDrift = false;
        try { capturedValidator!(shiftedStateJson); }
        catch (ApiException ex) when (ex.StatusCode == 409) { threwDrift = true; }
        Assert.True(threwDrift, "Dwell validateState must reject a card that shifted index during spectator dwell.");
    }

    public static async Task SpectatorDwellInterceptCancelsBeforeDispatch()
    {
        var stateJson = """
            {"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["play_card","end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"player":{"hp":"70/80","block":0,"energy":3},
                       "hand":[{"i":0,"cid":10,"line":"Strike","playable":true,"targets":[0]}],
                       "enemies":[{"i":0,"hp":"20/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        var actionsJson = """[{"name":"play_card","requires_index":true,"requires_target":true},{"name":"end_turn"}]""";
        var snapshotJson = $"{{\"state\":{stateJson},\"available_actions\":{actionsJson}}}";

        var settings = AgentSettings.CreateDefault();
        settings.AutopilotEnabled = false;
        settings.TurnPlansEnabled = false;
        settings.SpectatorHighlightMs = 600;
        settings.HudMode = AgentSettings.HudFull;

        var bridge = new FakeBridge(stateJson, actionsJson, snapshotJson);
        var llm = new FakeLlmFactory(_ => new LlmCompletion
        {
            ToolCalls = new List<LlmToolCall>
            {
                new() { Id = "tc1", Name = "act", ArgumentsJson = """{"action":"play_card","card_index":0,"target_index":0,"reason":"test intercept"}""" }
            }
        });

        using var cts = new CancellationTokenSource();
        var dwellTransitions = new List<bool>();
        var loop = new AgentLoop(bridge, llm, () => settings, reportSpectatorDwell: active =>
        {
            dwellTransitions.Add(active);
            if (active) cts.Cancel();
        });

        var canceled = false;
        try
        {
            await loop.PlayOnceAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        Assert.True(canceled, "Canceling during spectator dwell must abort the turn cleanly.");
        Assert.Equal(0, bridge.ActCalls, "Intercepting during spectator dwell must abort BEFORE ActAsync executes.");
        Assert.Equal(2, dwellTransitions.Count);
        Assert.True(dwellTransitions[0]);
        Assert.False(dwellTransitions[1]);
    }

    public static async Task TurnPlanTailAutoEndsTurnOnlyWhenSafeAndNoMovesLeft()
    {
        const string initial = """
            {"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["play_card","end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"player":{"hp":"70/80","block":0,"energy":2},
                       "hand":[{"i":0,"cid":11,"line":"Strike","playable":true,"targets":[0]},{"i":1,"cid":12,"line":"Defend","playable":true,"targets":[]}],
                       "enemies":[{"i":0,"hp":"20/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        const string afterCard1 = """
            {"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["play_card","end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"player":{"hp":"70/80","block":0,"energy":1},
                       "hand":[{"i":0,"cid":12,"line":"Defend","playable":true,"targets":[]}],
                       "enemies":[{"i":0,"hp":"14/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        const string afterCard2NoMoves = """
            {"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"end_turn_will_kill_player":false,"player":{"hp":"70/80","block":5,"energy":0},
                       "hand":[{"i":0,"cid":13,"line":"Bash","playable":false,"targets":[0]}],
                       "enemies":[{"i":0,"hp":"14/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        const string afterEndTurn = """
            {"screen":"COMBAT","in_combat":true,"turn":2,"available_actions":["play_card","end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"player":{"hp":"70/80","block":0,"energy":3},
                       "hand":[],"enemies":[{"i":0,"hp":"14/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        const string afterCard2Lethal = """
            {"screen":"COMBAT","in_combat":true,"turn":1,"available_actions":["end_turn"],
             "combat":{"action_readiness":{"can_use_combat_actions":true},"end_turn_will_kill_player":true,"player":{"hp":"5/80","block":0,"energy":0},
                       "hand":[],"enemies":[{"i":0,"hp":"14/20","alive":true,"hittable":true}]},
             "run":{"potions":[]}}
            """;
        const string actions = """[{"name":"play_card","requires_index":true,"requires_target":false},{"name":"end_turn"}]""";
        const string twoCardPlan = """{"action":"play_card","card_cid":11,"target_index":0,"reason":"hit","then":[{"action":"play_card","card_cid":12,"reason":"block"}]}""";

        var settings = AgentSettings.CreateDefault();
        settings.AutopilotEnabled = true;
        settings.TurnPlansEnabled = true;
        settings.SpectatorHighlightMs = 0;

        // Safe empty hand after plan -> auto-appends end_turn at 0 extra LLM requests.
        var safeBridge = new SequencedBridge(actions, initial, afterCard1, afterCard2NoMoves, afterEndTurn);
        var llm = new FakeLlmFactory(_ => new LlmCompletion
        {
            ToolCalls = new List<LlmToolCall> { new() { Id = "p1", Name = "act", ArgumentsJson = twoCardPlan } }
        });
        var safeResult = await new AgentLoop(safeBridge, llm, () => settings).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, safeResult.RequestsSpent);
        Assert.Equal(3, safeBridge.ActCalls);
        Assert.Equal("end_turn", safeResult.Acted);
        Assert.Equal(3, safeResult.PlanSteps!.Count);
        Assert.Equal(3, safeResult.PlanSize);
        Assert.Equal(TurnPlanStopReasons.Completed, safeResult.PlanStopReason);

        // Lethal end_turn after plan -> must NOT auto-end turn.
        var lethalBridge = new SequencedBridge(actions, initial, afterCard1, afterCard2Lethal);
        var lethalResult = await new AgentLoop(lethalBridge, llm, () => settings).PlayOnceAsync(CancellationToken.None);
        Assert.Equal(1, lethalResult.RequestsSpent);
        Assert.Equal(2, lethalBridge.ActCalls);
        Assert.Equal("play_card", lethalResult.Acted);
        Assert.Equal(2, lethalResult.PlanSteps!.Count);
        Assert.Equal(TurnPlanStopReasons.Completed, lethalResult.PlanStopReason);
    }

    private sealed class SequencedBridge(string actions, params string[] states) : IGameBridge
    {
        private int _index;
        public int ActCalls { get; private set; }
        private string Current => states[Math.Min(_index, states.Length - 1)];
        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) => Task.FromResult(actions);
        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken) => Task.FromResult($"{{\"state\":{Current},\"available_actions\":{actions}}}");
        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("COMBAT");
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y, string? tool, CancellationToken cancellationToken, bool rawState = false, Action<string>? validateState = null, string? mode = null)
        {
            validateState?.Invoke(Current);
            ActCalls++;
            if (_index + 1 < states.Length) _index++;
            return Task.FromResult("""{"status":"completed","stable":true}""");
        }
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
        public Task<string> GetRunIdAsync(CancellationToken cancellationToken) => Task.FromResult("run_phase4_seq");
    }

    private sealed class FakeBridge(string state, string actions, string snapshot, Action<Action<string>?>? onValidate = null) : IGameBridge
    {
        public int ActCalls { get; private set; }
        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(state);
        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult(state);
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) => Task.FromResult(actions);
        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("COMBAT");
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y, string? tool, CancellationToken cancellationToken, bool rawState = false, Action<string>? validateState = null, string? mode = null)
        {
            ActCalls++;
            onValidate?.Invoke(validateState);
            return Task.FromResult("""{"status":"completed","stable":true}""");
        }
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
        public Task<string> GetRunIdAsync(CancellationToken cancellationToken) => Task.FromResult("run_phase4");
    }

    private sealed class FakeLlmFactory(Func<LlmRequest, LlmCompletion> handler) : ILlmClientFactory
    {
        public ILlmClient Create(LlmEndpoint endpoint, TimeSpan? timeout = null) => new FakeLlmClient(handler);
    }

    private sealed class FakeLlmClient(Func<LlmRequest, LlmCompletion> handler) : ILlmClient
    {
        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
        public Task<string> PingAsync(string model, CancellationToken cancellationToken) => Task.FromResult("ok");
        public Task<bool> ProbeToolCallingAsync(string model, LlmTool tool, string prompt, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
