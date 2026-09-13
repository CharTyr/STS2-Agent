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

    /// <summary>
    /// The route split reached POST /action but not the overlay: the Invite button kept calling the
    /// auto-play overload, so with no verified play model the click was refused at the old model
    /// gate while the same request over the API launched the teammate for external takeover. The
    /// button has to choose the route the way the API does, and the tab's first line has to
    /// describe that route instead of asking for a connection test.
    /// </summary>
    public static void OverlayInviteFollowsTheApiRoute()
    {
        var overlay = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");
        var launch = AgentSourceFixture.DeclarationBody(overlay, "private async Task LaunchDualAsync()");
        Assert.Contains("var companionAutoPlay = FirstRunSetup.Evaluate(settings).ReadyToInvite;", launch);
        Assert.Contains("LaunchDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None)", launch);
        Assert.True(
            !overlay.Contains("LaunchDualInstanceAsync(HarvestSettings(), CancellationToken.None)", StringComparison.Ordinal),
            "the overlay must not call the auto-play-only overload any more.");

        var hint = AgentSourceFixture.DeclarationBody(overlay, "private static string FirstRunHintText()");
        Assert.Contains("firstRun.ReadyToInvite", hint);
        Assert.Contains("_firstRunHint.Text = FirstRunHintText();", overlay);
        // The description under the invite follows the same route, so the two lines never contradict each other.
        var dualHint = AgentSourceFixture.DeclarationBody(overlay, "private static string DualHintText()");
        Assert.Contains("ReadyToInvite", dualHint);
        Assert.Contains("_dualHint.Text = DualHintText();", overlay);
    }

    /// <summary>
    /// The overlay's AI Teammate tab is the in-game face of both PR #83 and PR #84: a Continue
    /// button that goes through the same runtime entry as continue_ai_teammate and is offered only
    /// where that action would be accepted, and a checkbox that writes CompanionAutoSelectCharacter
    /// straight into settings so the companion reads it at launch.
    /// </summary>
    public static void OverlayOffersContinueAndCharacterChoice()
    {
        var overlay = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");

        // Continue chooses the route the same way the invite does (see OverlayInviteFollowsTheApiRoute).
        Assert.Contains("ContinueDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None)", overlay);
        Assert.Contains("GameStateService.CanContinueAiTeammate(ActiveScreenContext.Instance.GetCurrentScreen())", overlay);
        Assert.Contains("_dualContinueButton.Disabled = AgentRuntime.Instance.DualLaunching || !canContinue;", overlay);

        Assert.Contains("settings.CompanionAutoSelectCharacter = !on;", overlay);
        Assert.Contains("current.CompanionAutoSelectCharacter = !_companionChoiceToggle.ButtonPressed;", overlay);
        Assert.Contains("SetPressedNoSignal(!AgentRuntime.Instance.Settings.CompanionAutoSelectCharacter)", overlay);
    }

    /// <summary>
    /// Every way of starting the in-process loop is one decision, so all of them answer to the same
    /// model gate. The companion's own /session/control used to skip it, which left a caller able to
    /// start a model loop that the host's Resume button, /companion/control and /teammate/control all
    /// refuse. Pausing stays ungated: nothing should be stuck unable to stop it.
    /// </summary>
    public static void EveryStartEntryPointSharesTheModelGate()
    {
        var runtime = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentRuntime.cs");
        var companion = AgentSourceFixture.MethodBody(runtime, "SetCompanionRunningAsync");

        var gate = companion.IndexOf("FirstRunSetup.Evaluate(Settings)", StringComparison.Ordinal);
        var start = companion.IndexOf("StartAutoPlay()", StringComparison.Ordinal);
        var pause = companion.IndexOf("RequestPause()", StringComparison.Ordinal);
        Assert.True(gate > 0, "The companion control path must check the play model before it starts the loop.");
        Assert.True(start > gate, "The model gate must come before StartAutoPlay on the companion.");
        Assert.True(pause > 0, "Pausing must stay available on the companion.");
        Assert.False(
            companion.IndexOf("FirstRunSetup.Evaluate(Settings)", pause, StringComparison.Ordinal) > 0 &&
                companion.LastIndexOf("FirstRunSetup.Evaluate(Settings)", StringComparison.Ordinal) > pause,
            "The pause branch must not be behind the model gate.");

        // The two other starts keep their gates; a regression there is the same bug from the host side.
        var host = AgentSourceFixture.MethodBody(runtime, "ControlTeammateResultAsync");
        Assert.True(
            host.IndexOf("FirstRunSetup.Evaluate(Settings)", StringComparison.Ordinal) > 0,
            "POST /teammate/control must keep its model gate.");
        var overlay = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");
        Assert.Contains("AgentRuntime.Instance.ControlTeammateAsync(true", overlay);
    }
}
