using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

internal static class PlayerExperienceTests
{
    public static void DefaultSettingsAreUnverifiedAndNotInvitable()
    {
        var settings = AgentSettings.CreateDefault();
        var status = FirstRunSetup.Evaluate(settings);
        Assert.False(status.ReadyToInvite);
        Assert.Equal("filled_unverified", status.Phase);
        Assert.Contains("尚未验证", status.Hint);
        Assert.Equal("unverified", status.Play.Status);
        Assert.Contains("尚未验证", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", settings) ?? "");
    }

    public static void VerifiedPlayFingerprintAllowsInvite()
    {
        var settings = AgentSettings.CreateDefault();
        var play = settings.TryResolvePlayModel()!;
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, play));
        var status = FirstRunSetup.Evaluate(settings);
        Assert.True(status.ReadyToInvite);
        Assert.Equal("verified", status.Phase);
        Assert.True(CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", settings) == null);
    }

    public static void ChangingKeyInvalidatesVerification()
    {
        var settings = AgentSettings.CreateDefault();
        var play = settings.TryResolvePlayModel()!;
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, play));
        settings.Endpoints[0].ApiKey = "sk-changed";
        var status = FirstRunSetup.Evaluate(settings);
        Assert.False(status.ReadyToInvite);
        Assert.Equal("filled_unverified", status.Phase);
    }

    public static async Task ConversationSuccessDoesNotMarkPlayWhenPlayReturns401()
    {
        var settings = TwoRoleSettings();
        var factory = new RolePingFactory(endpoint =>
            string.Equals(endpoint.Id, "chat-ep", StringComparison.Ordinal)
                ? new PingClient("pong")
                : new PingClient(new LlmException("HTTP 401 unauthorized", 401)));
        var loop = new AgentLoop(new UnusedBridge(), factory, () => settings);
        var results = await loop.TestConfiguredRolesAsync(force: true, CancellationToken.None);
        var conversation = results.Single(item => item.Role == ModelRoleNames.Conversation);
        var play = results.Single(item => item.Role == ModelRoleNames.Play);
        Assert.Equal("verified", conversation.Record.Status);
        Assert.Equal("failed", play.Record.Status);
        Assert.Equal(401, play.Record.StatusCode);
        Assert.Contains("游玩", ModelRoleProbe.FormatLine(play.Record));
        Assert.False(ModelRoleProbe.FormatLine(play.Record).Contains("连通成功"));
        Assert.Contains("API Key", play.Record.NextStep);
        Assert.Equal("unverified", play.Record.CapabilityStatus);
    }

    public static async Task FreshVerifiedFingerprintSkipsRetest()
    {
        var settings = AgentSettings.CreateDefault();
        var play = settings.TryResolvePlayModel()!;
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, play));
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Conversation, play));
        var factory = new RolePingFactory(_ => new PingClient("pong") { FailIfCalled = true });
        var loop = new AgentLoop(new UnusedBridge(), factory, () => settings);
        var results = await loop.TestConfiguredRolesAsync(force: false, CancellationToken.None);
        Assert.True(results.Where(item => item.Role != ModelRoleNames.Vision).All(item => item.SkippedBecauseFresh));
        Assert.Equal("unused", results.Single(item => item.Role == ModelRoleNames.Vision).Record.Status);
    }

    public static void EndpointRemovalRequiresConfirmationWhenReferenced()
    {
        var settings = AgentSettings.CreateDefault();
        var impact = SettingsBinding.EndpointRemoval(settings, settings.Endpoints[0].Id);
        Assert.True(impact.Blocked);
        Assert.Contains("对话模型", impact.Message);
        Assert.False(SettingsBinding.EndpointRemoval(settings, "missing").Blocked);
    }

    public static void ModelRemovalRequiresConfirmationWhenBound()
    {
        var settings = AgentSettings.CreateDefault();
        var impact = SettingsBinding.ModelRemoval(settings, settings.Models[0].Id);
        Assert.True(impact.Blocked);
        Assert.Contains("游玩模型", impact.Message);
    }

    public static void MissingUsageIsNotDisplayedAsZero()
    {
        var text = PlayerFacingSession.FormatUsage(false, LlmUsage.Empty, 3);
        Assert.Contains("未知", text);
        Assert.False(text.Contains("Token 消耗：0"));
        Assert.Contains("请求：3 次", text);
    }

    public static void DiagnosticExportRedactsSecretsAndOmitsChat()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].ApiKey = "sk-secretvalue";
        var rendered = DiagnosticExport.Render(new DiagnosticSnapshot
        {
            ModVersion = "0.10.2",
            Role = "human",
            PlayPhase = "paused",
            Status = "就绪",
            DualStatus = "ok",
            TeamControlStatus = "idle",
            StopKind = "config",
            StopDetail = "Authorization: Bearer sk-secretvalue",
            ApiPrefix = "http://127.0.0.1:8081/",
            McpUrl = "http://127.0.0.1:8081/mcp",
            McpEnabled = true,
            UsageKnown = false,
            SessionRequests = 2,
            SessionTokens = null,
            RecentEvents = new[] { "api_key=sk-secretvalue connected" },
            RecentRequestIds = new[] { "req_1" },
            Settings = settings
        });
        Assert.Contains("已脱敏", rendered);
        Assert.Contains(DiagnosticExport.IncludesChatNotice, rendered);
        Assert.Contains("***", rendered);
        Assert.False(rendered.Contains("sk-secretvalue"));
        Assert.Contains("req_1", rendered);
        Assert.Contains("session_tokens=unknown", rendered);
    }

    public static void PlayerFacingMapsPauseAndConfigError()
    {
        var settings = AgentSettings.CreateDefault();
        var unverified = FirstRunSetup.Evaluate(settings);
        var unconfigured = PlayerFacingSession.Compose(BaseSnapshot(unverified) with { });
        Assert.Equal("unconfigured", unconfigured.Kind);
        Assert.Contains("设置", unconfigured.NextAction);

        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, settings.TryResolvePlayModel()!));
        var verified = FirstRunSetup.Evaluate(settings);
        var pausing = PlayerFacingSession.Compose(BaseSnapshot(verified) with
        {
            PlayPhase = "stopping",
            TeamControlPending = true,
            TeamControlStatus = "正在等待队友暂停；已提交的动作会先完成。"
        });
        Assert.Equal("pausing", pausing.Kind);
        Assert.Contains("已提交", pausing.Detail);

        var config = PlayerFacingSession.Compose(BaseSnapshot(verified) with
        {
            StopKind = "config",
            StopDetail = "HTTP 401"
        });
        Assert.Equal("needs_error", config.Kind);
        Assert.Contains("测试", config.NextAction);
    }

    public static void NativeMcpToolsMatchGuidedActContract()
    {
        var names = AgentTools.Mcp.Select(tool => tool.Name).ToArray();
        foreach (var expected in new[]
                 {
                     "health_check", "get_game_state", "get_raw_game_state", "get_available_actions",
                     "get_game_data_item", "get_game_data_items", "get_relevant_game_data",
                     "wait_until_actionable", "act"
                 })
        {
            Assert.True(names.Contains(expected), "missing " + expected);
        }

        var act = AgentTools.Mcp.Single(tool => tool.Name == "act");
        var json = System.Text.Json.JsonSerializer.Serialize(act.Parameters);
        Assert.Contains("action", json);
        Assert.Contains("option_index", json);
        Assert.Contains("target_index", json);
        Assert.Contains("card_index", json);
    }

    private static PlayerFacingSnapshot BaseSnapshot(FirstRunStatus first) => new()
    {
        FirstRun = first,
        PlayPhase = "paused",
        PlayRunning = false,
        Status = "就绪",
        DualLaunching = false,
        DualStatus = "尚未启动双开。",
        TeamControlPending = false,
        TeamControlStatus = "",
        CompanionConnected = false,
        CompanionProcessAlive = false,
        CompanionProcessExited = false,
        WaitingForGame = false,
        WaitingForPlayer = false,
        RequestingModel = false,
        FinishingSubmittedAction = false,
        StopKind = null,
        StopDetail = null,
        UsageKnown = false,
        SessionUsage = LlmUsage.Empty,
        SessionRequests = 0,
        BudgetReason = null,
        IsCompanion = false
    };

    private static AgentSettings TwoRoleSettings()
    {
        var chat = new LlmEndpoint { Id = "chat-ep", Name = "Chat", BaseUrl = "http://127.0.0.1:9/v1", ApiKey = "sk-chat" };
        var play = new LlmEndpoint { Id = "play-ep", Name = "Play", BaseUrl = "http://127.0.0.1:8/v1", ApiKey = "bad" };
        var chatModel = new LlmModelConfig { Id = "chat-model", EndpointId = chat.Id, Model = "chat-ok" };
        var playModel = new LlmModelConfig { Id = "play-model", EndpointId = play.Id, Model = "play-bad" };
        return new AgentSettings
        {
            Endpoints = { chat, play },
            Models = { chatModel, playModel },
            ConversationModelId = chatModel.Id,
            PlayModelId = playModel.Id
        };
    }

    private sealed class UnusedBridge : IGameBridge
    {
        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) => Task.FromResult("[]");
        public Task<IReadOnlyList<string>> GetAvailableActionNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => Task.FromResult("MAIN_MENU");
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y, string? tool, CancellationToken cancellationToken) =>
            Task.FromResult("{}");
        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }

    private sealed class RolePingFactory : ILlmClientFactory
    {
        private readonly Func<LlmEndpoint, ILlmClient> _create;

        public RolePingFactory(Func<LlmEndpoint, ILlmClient> create) => _create = create;

        public ILlmClient Create(LlmEndpoint endpoint) => _create(endpoint);
    }

    private sealed class PingClient : ILlmClient
    {
        private readonly string? _reply;
        private readonly Exception? _error;

        public bool FailIfCalled { get; init; }

        public PingClient(string reply) => _reply = reply;

        public PingClient(Exception error) => _error = error;

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LlmCompletion { Content = "unused" });

        public Task<string> PingAsync(string model, CancellationToken cancellationToken)
        {
            if (FailIfCalled)
            {
                throw new Exception("should skip retest");
            }

            if (_error != null)
            {
                return Task.FromException<string>(_error);
            }

            return Task.FromResult(_reply ?? "pong");
        }
    }
}
