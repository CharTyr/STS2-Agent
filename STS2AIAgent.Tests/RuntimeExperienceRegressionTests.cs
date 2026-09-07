using STS2AIAgent.Agent;
using STS2AIAgent.Config;

namespace STS2AIAgent.Tests;

// These cases are intentionally kept separate from the existing player
// experience suite so the runtime fixes can be wired into TestRunner without
// changing that suite's ownership.
internal static class RuntimeExperienceRegressionTests
{
    public static void DiagnosticExportRedactsAllCredentialShapes()
    {
        var settings = AgentSettings.CreateDefault();
        settings.Endpoints[0].ApiKey = "raw-provider-key";
        settings.Endpoints[0].BaseUrl =
            "https://api.example.test/v1?api_key=url-provider-key&region=cn";
        settings.RoleTests.Add(new ModelRoleTestRecord
        {
            Role = ModelRoleNames.Conversation,
            Status = "failed",
            CapabilityStatus = "unverified",
            EndpointName = "cloud",
            ModelName = "chat-model",
            Error = "Authorization: Bearer opaque-session-token apiKey=role-test-key",
            NextStep = "retry https://api.example.test/v1?session_token=url-session-token&safe=1"
        });

        var rendered = DiagnosticExport.Render(new DiagnosticSnapshot
        {
            ModVersion = "0.10.2",
            Role = "human",
            PlayPhase = "paused",
            Status = "Authorization: Bearer status-token",
            DualStatus = "apiKey=status-key",
            TeamControlStatus = "session token: team-session-token",
            StopKind = "config",
            StopDetail = "authorization=header-token",
            ApiPrefix = "http://127.0.0.1:8081/?token=api-token",
            McpUrl = "http://127.0.0.1:8081/mcp?access_token=mcp-token",
            McpEnabled = true,
            UsageKnown = false,
            SessionRequests = 2,
            SessionTokens = null,
            RecentEvents = new[]
            {
                "Authorization: Bearer event-token",
                "session_token=event-session-token"
            },
            RecentRequestIds = new[] { "req_1" },
            Settings = settings
        });

        foreach (var secret in new[]
                 {
                     "raw-provider-key",
                     "url-provider-key",
                     "opaque-session-token",
                     "role-test-key",
                     "url-session-token",
                     "status-token",
                     "status-key",
                     "team-session-token",
                     "header-token",
                     "api-token",
                     "mcp-token",
                     "event-token",
                     "event-session-token"
                 })
        {
            Assert.False(rendered.Contains(secret, StringComparison.Ordinal), "secret leaked: " + secret);
        }

        Assert.Contains("api.example.test", rendered);
        Assert.Contains("region=cn", rendered);
        Assert.Contains("safe=1", rendered);
        Assert.Contains("***", rendered);
        Assert.Contains("Authorization: Bearer ***", rendered);
    }

    public static void ModelRoleProbeUsesTheTestedRoleInFallbackErrors()
    {
        var settings = AgentSettings.CreateDefault();
        var model = settings.TryResolvePlayModel()!;

        var conversation = ModelRoleProbe.FromException(
            ModelRoleNames.Conversation,
            model,
            new InvalidOperationException("unexpected ping failure"));
        var play = ModelRoleProbe.FromException(
            ModelRoleNames.Play,
            model,
            new InvalidOperationException("unexpected ping failure"));
        var vision = ModelRoleProbe.FromException(
            ModelRoleNames.Vision,
            model,
            new InvalidOperationException("unexpected ping failure"));

        Assert.Contains("对话模型请求", conversation.Error);
        Assert.Contains("游玩模型请求", play.Error);
        Assert.Contains("视觉模型请求", vision.Error);
        Assert.False(conversation.Error!.Contains("游玩模型请求", StringComparison.Ordinal));
        Assert.False(vision.Error!.Contains("游玩模型请求", StringComparison.Ordinal));
    }

    public static void ModelRoleProbeClassifiesConfigAndNetworkFailures()
    {
        Assert.Equal("config", ModelRoleProbe.FailureKind(401, "unauthorized"));
        Assert.Equal("config", ModelRoleProbe.FailureKind(404, "model missing"));
        Assert.Equal("config", ModelRoleProbe.FailureKind(null, "invalid model name"));
        Assert.Equal("network", ModelRoleProbe.FailureKind(408, "request timeout"));
        Assert.Equal("network", ModelRoleProbe.FailureKind(429, "rate limited"));
        Assert.Equal("network", ModelRoleProbe.FailureKind(503, "service unavailable"));
        Assert.Equal("network", ModelRoleProbe.FailureKind(null, "connection refused"));
    }

    public static void CompletionIdentityRejectsLatePreviousSessionCallbacks()
    {
        var firstTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        var secondTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        var first = new PlaySessionIdentity(1, firstTask);
        var current = new PlaySessionIdentity(2, secondTask);

        Assert.True(PlayerFacingSession.IsCurrentPlaySession(current, current));
        Assert.False(PlayerFacingSession.IsCurrentPlaySession(current, first));
        Assert.False(PlayerFacingSession.IsCurrentPlaySession(
            new PlaySessionIdentity(2, firstTask),
            new PlaySessionIdentity(2, secondTask)));
    }

    public static void SuccessfulModelTestClearsOnlyItsOwnTransientStop()
    {
        Assert.True(PlayerFacingSession.ShouldClearModelTestFailure("config", null, ModelRoleNames.Play));
        Assert.True(PlayerFacingSession.ShouldClearModelTestFailure("network", ModelRoleNames.Play, ModelRoleNames.Play));
        Assert.False(PlayerFacingSession.ShouldClearModelTestFailure("config", ModelRoleNames.Conversation, ModelRoleNames.Play));
        Assert.False(PlayerFacingSession.ShouldClearModelTestFailure("budget", ModelRoleNames.Play, ModelRoleNames.Play));
        Assert.False(PlayerFacingSession.ShouldClearModelTestFailure("run_end", ModelRoleNames.Play, ModelRoleNames.Play));
    }
}
