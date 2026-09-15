namespace STS2AIAgent.Tests;

/// <summary>
/// Source-level contracts for invite_ai_teammate availability. The action must be advertised
/// only when the shared structural launch probe would allow it, without requiring a verified
/// play model (external takeover still launches paused).
/// </summary>
internal static class InviteCoopContractTests
{
    private const string StatePath = "STS2AIAgent/Game/GameStateService.cs";
    private const string Guard = "if (CanInviteAiTeammate(currentScreen))";

    public static void ActionIsAdvertisedBehindTheStructuralProbe()
    {
        var state = AgentSourceFixture.Read(StatePath);
        var probe = AgentSourceFixture.MethodBody(state, "CanInviteAiTeammate");
        Assert.Contains("currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree()", probe, StringComparison.Ordinal);
        Assert.Contains("CoopLaunchPolicy.GetStructuralError", probe, StringComparison.Ordinal);
        Assert.Contains("InstanceRole.IsCompanion", probe, StringComparison.Ordinal);
        Assert.Contains("PlayRunning", probe, StringComparison.Ordinal);
        Assert.Contains("AgentRuntime.Instance?.DualLaunching == true", probe, StringComparison.Ordinal);
        Assert.True(!probe.Contains("ReadyToInvite", StringComparison.Ordinal), "Advertising invite must not require a verified play model.");

        var descriptors = AgentSourceFixture.MethodBody(state, "BuildAvailableActionsPayload");
        Assert.Contains(Guard, descriptors, StringComparison.Ordinal);
        Assert.Contains("name = \"invite_ai_teammate\"", descriptors, StringComparison.Ordinal);
        Assert.True(
            !descriptors.Contains("currentScreen is NMainMenu inviteMenu && inviteMenu.IsVisibleInTree()", StringComparison.Ordinal),
            "invite_ai_teammate descriptors must not use the unguarded main-menu check.");

        var names = AgentSourceFixture.MethodBody(state, "BuildAvailableActionNames");
        Assert.Contains(Guard, names, StringComparison.Ordinal);
        Assert.Contains("names.Add(\"invite_ai_teammate\")", names, StringComparison.Ordinal);
        Assert.True(
            !names.Contains("currentScreen is NMainMenu mainMenu && mainMenu.IsVisibleInTree()", StringComparison.Ordinal)
                || names.IndexOf(Guard, StringComparison.Ordinal) >= 0,
            "invite_ai_teammate names must go through CanInviteAiTeammate.");
    }

    /// <summary>
    /// After the pending-observer change the handler has no await. Keep it a synchronous
    /// <c>Task</c> method so Release builds do not warn CS1998, and keep failures as throws.
    /// </summary>
    public static void PendingExecutorReturnsTaskWithoutAsync()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        Assert.Contains(
            "private static Task<ActionResponsePayload> ExecuteInviteAiTeammateAsync()",
            source,
            StringComparison.Ordinal);
        Assert.True(
            !source.Contains("private static async Task<ActionResponsePayload> ExecuteInviteAiTeammateAsync()", StringComparison.Ordinal),
            "ExecuteInviteAiTeammateAsync must not be async; CS1998 fires in Release without an await.");

        var body = AgentSourceFixture.MethodBody(source, "ExecuteInviteAiTeammateAsync");
        Assert.Contains(@"ObserveBackgroundTask(launch, ""invite_ai_teammate"")", body, StringComparison.Ordinal);
        Assert.Contains("Task.FromResult(new ActionResponsePayload", body, StringComparison.Ordinal);
        Assert.Contains(@"status = ""pending""", body, StringComparison.Ordinal);
        Assert.Contains(@"status = ""completed""", body, StringComparison.Ordinal);
        Assert.Contains("throw new ApiException", body, StringComparison.Ordinal);
        Assert.Contains("DualLaunchOutcomePolicy.IsInProgress(outcome)", body, StringComparison.Ordinal);
        Assert.Contains("DualLaunchOutcomePolicy.IsFailure(outcome)", body, StringComparison.Ordinal);
        Assert.True(
            !body.Contains("Task.FromException", StringComparison.Ordinal),
            "Invite failures must throw, not wrap in Task.FromException.");
        Assert.True(
            !body.Contains("await ", StringComparison.Ordinal),
            "The pending invite executor must not await the launch.");
        Assert.True(
            !body.Contains("FromSeconds(20)", StringComparison.Ordinal),
            "Do not restore the 20s launch wait.");

        Assert.True(
            !body.Contains("message = AgentRuntime.Instance.DualStatus,", StringComparison.Ordinal),
            "Pending invite responses must not copy DualStatus, which can still say the idle dual-launch text.");
        Assert.Contains("var message = AgentRuntime.Instance.DualStatus", body, StringComparison.Ordinal);
        Assert.Contains("正在邀请队友…", body, StringComparison.Ordinal);

        var health = AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs"),
            "BuildHealthData");
        Assert.Contains("dual_status = AgentRuntime.Instance.DualStatus", health, StringComparison.Ordinal);
        Assert.Contains("dual_launch_outcome =", health, StringComparison.Ordinal);
        Assert.Contains("AgentRuntime.Instance.DualLaunchOutcome", health, StringComparison.Ordinal);
        Assert.Contains("dualLaunchOutcome.ToString()", health, StringComparison.Ordinal);
        Assert.Contains("DualLaunchOutcome.Idle", health, StringComparison.Ordinal);
    }
}
