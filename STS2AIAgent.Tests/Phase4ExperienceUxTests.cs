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
        // Include control characters and a UTF-16 surrogate pair straddling the 400-char cap.
        host.PlayerPreferences = "  \0\x07" + new string('a', AgentSettings.MaxPlayerPreferencesLength - 1) + "😀extra  ";
        host.EnsureValidShape();
        Assert.Equal(AgentSettings.MaxSpectatorHighlightMs, host.SpectatorHighlightMs);
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

    private sealed class FakeBridge(string state, string actions, string snapshot, Action<Action<string>?>? onValidate = null) : IGameBridge
    {
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
