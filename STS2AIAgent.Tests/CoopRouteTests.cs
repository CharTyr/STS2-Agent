using STS2AIAgent.Config;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

/// <summary>
/// Issue #85: the "invite an AI teammate" preconditions are split into two routes. Auto-play needs
/// a play model that passed 测试连接; handing the teammate window to an external agent never calls
/// that model at all, so that route has to launch with nothing configured and come up paused.
/// </summary>
internal static class CoopRouteTests
{
    /// <summary>
    /// The model gate is the only thing the external-takeover route drops. Everything that keeps a
    /// launch safe on this machine -- not the companion window, no auto-play already running, main
    /// menu -- still applies, so the split cannot be used to bypass those.
    /// </summary>
    public static void UnverifiedPlayModelLaunchesForExternalTakeoverOnly()
    {
        var settings = AgentSettings.CreateDefault();
        Assert.True(!FirstRunSetup.Evaluate(settings).ReadyToInvite);

        // Auto-play route: unchanged, the unverified play model still refuses the launch.
        Assert.Contains("验证", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", settings));
        // External-takeover route: the same settings launch, they just do not start the loop.
        Assert.True(
            CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", settings, requireVerifiedPlayModel: false) == null);

        // Nothing configured at all is the case the issue was filed for: still a launch.
        var empty = new AgentSettings();
        Assert.Contains("设置", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", empty));
        Assert.True(CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", empty, requireVerifiedPlayModel: false) == null);

        // Only the model gate is skipped; the structural conditions still hold on both routes.
        Assert.Contains(
            "主窗口",
            CoopLaunchPolicy.GetError(true, false, "MAIN_MENU", empty, requireVerifiedPlayModel: false));
        Assert.Contains(
            "暂停",
            CoopLaunchPolicy.GetError(false, true, "MAIN_MENU", empty, requireVerifiedPlayModel: false));
        Assert.Contains(
            "主菜单",
            CoopLaunchPolicy.GetError(false, false, "COMBAT", empty, requireVerifiedPlayModel: false));
        Assert.Contains("模型", CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", empty));

        // A verified play model collapses the two routes: the same settings now auto-play.
        ModelRoleProbe.Upsert(settings, ModelRoleProbe.FromSuccess(ModelRoleNames.Play, settings.TryResolvePlayModel()!));
        Assert.True(CoopLaunchPolicy.GetError(false, false, "MAIN_MENU", settings) == null);
    }

    /// <summary>
    /// Contract for the wiring: the route boolean is derived before the precondition check, reaches
    /// the child as STS2_AGENT_AUTOPLAY, and the supported control entry is the host window's
    /// /teammate/control rather than the companion's private session token.
    /// </summary>
    public static void CompanionRouteReachesTheSupportedSurfaces()
    {
        var launcher = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs");
        Assert.Contains("bool companionAutoPlay = true", launcher);
        Assert.Contains("""STS2_AGENT_AUTOPLAY"] = companionAutoPlay ? "1" : "0";""", launcher);
        Assert.Contains("LaunchCoreAsync(cancellationToken, companionAutoPlay)", launcher);

        var coordinator = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs");
        Assert.Contains("LaunchCompanionAsync(cancellationToken, companionAutoPlay)", coordinator);
        Assert.Contains("bool companionAutoPlay = true", coordinator);

        var actions = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        Assert.Contains("FirstRunSetup.Evaluate(settings).ReadyToInvite", actions);
        Assert.Contains("requireVerifiedPlayModel: companionAutoPlay", actions);
        Assert.Contains("LaunchDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None)", actions);
        Assert.Contains("ContinueDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None)", actions);

        var runtime = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        Assert.Contains("requireVerifiedPlayModel: companionAutoPlay", runtime);
        Assert.Contains("public bool CompanionAutoPlay => _companionAutoPlay;", runtime);
        // An idle companion says why it is idle instead of looking stalled.
        Assert.Contains("等待外部接管", runtime);
        Assert.Contains("TeammateControlResult", runtime);

        // The reported route has to describe the teammate that is actually running. Recording it
        // before the attempt would let a rejected retry (the usual one: "the teammate window is
        // already running") relabel a teammate that the in-process loop is already playing.
        var core = AgentSourceFixture.MethodBody(runtime, "LaunchDualInstanceCoreAsync");
        var recorded = core.IndexOf("_companionAutoPlay = companionAutoPlay;", StringComparison.Ordinal);
        var decided = core.IndexOf("_dualLaunchOutcome = launchResult.Ok", StringComparison.Ordinal);
        var boundToNewSession = core.IndexOf("ReferenceEquals(previousConnection", StringComparison.Ordinal);
        Assert.True(recorded > 0, "The launch route must be recorded somewhere.");
        Assert.True(
            decided > 0 && recorded > decided && boundToNewSession > 0 && recorded > boundToNewSession,
            "The launch route must be recorded after the launch result, inside the new-session guard.");

        var router = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        Assert.Contains("/teammate/control", router);
        Assert.Contains("ControlTeammateResultAsync", router);
        Assert.Contains("companion = BuildCompanionSessionData()", router);
        Assert.Contains("connection.Port", router);
        // The token authorizes this process alone, so the discovery payload must never carry it.
        var discovery = AgentSourceFixture.MethodBody(router, "BuildCompanionSessionData");
        Assert.False(
            discovery.Contains("token", StringComparison.OrdinalIgnoreCase),
            "The /health companion block must not carry the companion session token.");

        // The new entry point stays loopback-only and host-only, like the control it wraps.
        var endpoint = router.IndexOf("/teammate/control", StringComparison.Ordinal);
        Assert.True(endpoint >= 0, "Router.cs must route /teammate/control.");
        var routeBody = router.Substring(endpoint, Math.Min(1200, router.Length - endpoint));
        Assert.Contains("request.IsLocal", routeBody);
        Assert.Contains("InstanceRole.IsCompanion", routeBody);
    }
}
