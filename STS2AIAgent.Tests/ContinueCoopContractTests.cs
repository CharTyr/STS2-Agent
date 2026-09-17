namespace STS2AIAgent.Tests;

/// <summary>
/// Source-level contracts for <c>continue_ai_teammate</c>. The state and action services need the
/// game assemblies, so the pieces a client relies on are pinned by reading the source: the action is
/// advertised (descriptor list and <c>available_actions</c>) behind one availability probe, the
/// executor re-checks that probe, a failure after the load flow started is a retryable
/// <c>continue_failed</c>, the load screen only advertises what the executor can perform, and the
/// waits the load flow adds are bounded and cancellable.
/// </summary>
internal static class ContinueCoopContractTests
{
    private const string ActionPath = "STS2AIAgent/Game/GameActionService.cs";
    private const string Guard = "if (CanContinueAiTeammate(currentScreen))";

    public static void ActionIsAdvertisedBehindTheSaveProbe()
    {
        var state = AgentSourceFixture.ReadStateService();

        // The probe mirrors the game's own gate for showing "Load" in the multiplayer submenu, on
        // the host's visible main menu only.
        var probe = AgentSourceFixture.MethodBody(state, "CanContinueAiTeammate");
        Assert.Contains("InstanceRole.IsCompanion", probe, StringComparison.Ordinal);
        Assert.Contains("currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree()", probe, StringComparison.Ordinal);
        Assert.Contains("CoopLaunchPolicy.GetStructuralError", probe, StringComparison.Ordinal);
        Assert.Contains("PlayRunning", probe, StringComparison.Ordinal);
        Assert.Contains("AgentRuntime.Instance?.DualLaunching == true", probe, StringComparison.Ordinal);
        Assert.Contains("return SaveManager.Instance.HasMultiplayerRunSave;", probe, StringComparison.Ordinal);

        // Each exposure list must carry its own guard: a descriptor without the name entry (or the
        // reverse) leaves a rule-following client unable to discover or legally call the action.
        // Both surfaces report one walk, so the guard is pinned once.
        var walker = AgentSourceFixture.DeclarationBody(
            state,
            "private static List<ActionDescriptor> EnumerateAvailableActions(");
        Assert.Contains(Guard, walker, StringComparison.Ordinal);
        Assert.Contains("name = \"continue_ai_teammate\"", walker, StringComparison.Ordinal);
    }

    public static void ExecutorRechecksTheProbeAndFailsRetryably()
    {
        var action = AgentSourceFixture.Read(ActionPath);
        var body = AgentSourceFixture.MethodBody(action, "ExecuteContinueAiTeammateAsync");

        Assert.Contains("CoopLaunchPolicy.GetError(", body, StringComparison.Ordinal);
        Assert.Contains("GameStateService.CanContinueAiTeammate(", body, StringComparison.Ordinal);
        Assert.Contains("DualLaunchOutcomePolicy.IsInProgress(outcome)", body, StringComparison.Ordinal);

        // docs/api.md lists continue_failed as retryable: a game restart clears the stuck ENet port.
        // The throw is the whole statement from its `throw` to the next `;` at statement level, so a
        // reformat of the anonymous details object does not move the assertion.
        var code = body.IndexOf("\"continue_failed\"", StringComparison.Ordinal);
        Assert.True(code >= 0, "continue_failed must be raised from the executor.");
        var start = body.LastIndexOf("throw new ApiException(", code, StringComparison.Ordinal);
        var end = body.IndexOf(";", code, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "continue_failed must be thrown as an ApiException.");
        var statement = body.Substring(start, end - start);
        Assert.Contains("retryable: true", statement, StringComparison.Ordinal);
    }

    public static void LoadScreenAdvertisesOnlyWhatTheExecutorHandles()
    {
        var state = AgentSourceFixture.ReadStateService();
        var action = AgentSourceFixture.Read(ActionPath);

        // embark: the load screen resolves the button and the executor waits for the ready
        // transition instead of reporting completion after a single frame.
        var embarkButton = AgentSourceFixture.MethodBody(state, "GetCharacterEmbarkButton");
        Assert.Contains("GetMultiplayerLoadScreen(currentScreen)", embarkButton, StringComparison.Ordinal);
        var embark = AgentSourceFixture.MethodBody(action, "ExecuteEmbarkAsync");
        Assert.Contains("NMultiplayerLoadGameScreen loadScreen", embark, StringComparison.Ordinal);
        Assert.Contains("WaitForLoadEmbarkTransitionAsync(loadScreen", embark, StringComparison.Ordinal);

        // unready: the executor only handles NCharacterSelectScreen, so the state must not advertise
        // it on the load screen.
        var unreadyButton = AgentSourceFixture.MethodBody(state, "GetCharacterUnreadyButton");
        Assert.True(
            !unreadyButton.Contains("GetMultiplayerLoadScreen", StringComparison.Ordinal),
            "unready must not be advertised on the load screen while the executor has no path for it.");
    }

    public static void LoadEmbarkWaitIsBoundedAndSettlesOnEveryExit()
    {
        var action = AgentSourceFixture.Read(ActionPath);

        var wait = AgentSourceFixture.MethodBody(action, "WaitForLoadEmbarkTransitionAsync");
        Assert.Contains("var deadline = DateTime.UtcNow + timeout;", wait, StringComparison.Ordinal);
        Assert.Contains("while (DateTime.UtcNow < deadline)", wait, StringComparison.Ordinal);
        Assert.Contains("return IsLoadEmbarkSettled(screen);", wait, StringComparison.Ordinal);

        // Settled means: the screen was replaced or freed, a modal took over, or the game disabled
        // Embark (which NMultiplayerLoadGameScreen.OnEmbarkPressed does synchronously on ready).
        var settled = AgentSourceFixture.MethodBody(action, "IsLoadEmbarkSettled");
        Assert.Contains("!ReferenceEquals(currentScreen, screen)", settled, StringComparison.Ordinal);
        Assert.Contains("!GodotObject.IsInstanceValid(screen)", settled, StringComparison.Ordinal);
        Assert.Contains("GameStateService.GetOpenModal() != null", settled, StringComparison.Ordinal);
        Assert.Contains("!GameStateService.CanEmbark(currentScreen)", settled, StringComparison.Ordinal);
    }

    public static void StartLocalLoadWaitsHonourTheCoordinatorToken()
    {
        // Scope: the coordinator's token reaches every wait inside StartLocalLoadAsync. The HTTP
        // executor itself still passes CancellationToken.None, the same as invite_ai_teammate.
        var action = AgentSourceFixture.Read(ActionPath);
        Assert.Contains(
            "StartLocalLoadAsync(CancellationToken cancellationToken",
            action,
            StringComparison.Ordinal);

        var load = AgentSourceFixture.MethodBody(action, "StartLocalLoadAsync");
        var waits = 0;
        var index = 0;
        while ((index = load.IndexOf("WaitForMainMenuSubmenuOpenAsync<", index, StringComparison.Ordinal)) >= 0)
        {
            var end = load.IndexOf(");", index, StringComparison.Ordinal);
            var call = load.Substring(index, end - index);
            Assert.True(
                call.EndsWith(", cancellationToken", StringComparison.Ordinal),
                "every submenu wait inside StartLocalLoadAsync must receive the cancellation token: " + call);
            waits++;
            index = end;
        }

        Assert.True(waits >= 3, "StartLocalLoadAsync opens the multiplayer submenu and waits for the load screen.");

        var submenuWait = AgentSourceFixture.DeclarationBody(
            action,
            "private static async Task<bool> WaitForMainMenuSubmenuOpenAsync<TSubmenu>(");
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", submenuWait, StringComparison.Ordinal);

        var coordinator = AgentSourceFixture.Read("STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs");
        Assert.Contains("GameActionService.StartLocalLoadAsync(cancellationToken)", coordinator, StringComparison.Ordinal);
    }

    /// <summary>
    /// After the pending-observer change the handler has no await. Keep it a synchronous
    /// <c>Task</c> method so Release builds do not warn CS1998, and keep failures as throws.
    /// </summary>
    public static void PendingExecutorReturnsTaskWithoutAsync()
    {
        var action = AgentSourceFixture.Read(ActionPath);
        Assert.Contains(
            "private static Task<ActionResponsePayload> ExecuteContinueAiTeammateAsync()",
            action,
            StringComparison.Ordinal);
        Assert.True(
            !action.Contains("private static async Task<ActionResponsePayload> ExecuteContinueAiTeammateAsync()", StringComparison.Ordinal),
            "ExecuteContinueAiTeammateAsync must not be async; CS1998 fires in Release without an await.");

        var body = AgentSourceFixture.MethodBody(action, "ExecuteContinueAiTeammateAsync");
        Assert.Contains(@"ObserveBackgroundTask(launch, ""continue_ai_teammate"")", body, StringComparison.Ordinal);
        Assert.Contains("Task.FromResult(new ActionResponsePayload", body, StringComparison.Ordinal);
        Assert.Contains(@"status = ""pending""", body, StringComparison.Ordinal);
        Assert.Contains(@"status = ""completed""", body, StringComparison.Ordinal);
        Assert.Contains("throw new ApiException", body, StringComparison.Ordinal);
        Assert.Contains("DualLaunchOutcomePolicy.IsInProgress(outcome)", body, StringComparison.Ordinal);
        Assert.Contains("DualLaunchOutcomePolicy.IsFailure(outcome)", body, StringComparison.Ordinal);
        Assert.True(
            !body.Contains("Task.FromException", StringComparison.Ordinal),
            "Continue failures must throw, not wrap in Task.FromException.");
        Assert.True(
            !body.Contains("await ", StringComparison.Ordinal),
            "The pending continue executor must not await the launch.");
        Assert.True(
            !body.Contains("FromSeconds(20)", StringComparison.Ordinal),
            "Do not restore the 20s launch wait.");

        Assert.True(
            !body.Contains("message = AgentRuntime.Instance.DualStatus,", StringComparison.Ordinal),
            "Pending continue responses must not copy DualStatus, which can still say the idle dual-launch text.");
        Assert.Contains("var message = AgentRuntime.Instance.DualStatus", body, StringComparison.Ordinal);
        Assert.Contains("正在读档接回队友…", body, StringComparison.Ordinal);
    }
}
