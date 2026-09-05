namespace STS2AIAgent.Tests;

/// <summary>
/// Executable source-contract tests for the Godot-facing game-over services. The full services
/// are intentionally not linked into this lightweight test project, so these assertions keep the
/// public action protocol covered without pulling the game runtime into unit tests.
/// </summary>
internal static class GameOverContractTests
{
    public static void DedicatedContinueActionIsWiredEndToEnd()
    {
        var actionSource = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs"));
        var stateSource = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs"));
        var promptSource = AgentSourceFixture.Read("STS2AIAgent/Agent/PlayPrompt.cs");

        Assert.Contains(
            "\"continue_game_over\"=>ExecuteContinueGameOverAsync()",
            actionSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "names.Add(\"continue_game_over\")",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains("continue_game_over", promptSource, StringComparison.Ordinal);
        Assert.Contains("NGameOverContinueButton", actionSource, StringComparison.Ordinal);
    }

    public static void ReturnActionRequiresVisibleAndEnabledMainMenuButton()
    {
        var stateSource = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs"));

        Assert.Contains(
            "can_return_to_main_menu=mainMenuButton?.Visible==true&&mainMenuButton?.IsEnabled==true",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "GetNodeOrNull<NReturnToMainMenuButton>(\"%MainMenuButton\")",
            stateSource,
            StringComparison.Ordinal);
        Assert.False(
            stateSource.Contains("can_return_to_main_menu=true", StringComparison.Ordinal),
            "GAME_OVER must never advertise return_to_main_menu before the native summary button is visible and enabled.");
        Assert.Contains(
            "if(gameOver.can_return_to_main_menu){names.Add(\"return_to_main_menu\")",
            stateSource,
            StringComparison.Ordinal);
    }

    public static void ContinueAndReturnUseNativeButtonsWithoutSkippingSummary()
    {
        var rawActionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var actionSource = AgentSourceFixture.WithoutWhitespace(rawActionSource);
        var rawStateSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var continueBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawActionSource, "ExecuteContinueGameOverAsync"));
        var returnBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawActionSource, "ExecuteReturnToMainMenuAsync"));
        var returnGateBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "CanReturnToMainMenu"));
        var buttonReadyBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "IsGameOverButtonReady"));

        Assert.Contains("ExecuteContinueGameOverAsync", actionSource, StringComparison.Ordinal);
        Assert.Contains("NGameOverContinueButton", continueBody, StringComparison.Ordinal);
        Assert.Contains("ForceClick", continueBody, StringComparison.Ordinal);
        Assert.Contains("WaitForGameOverSummaryReadyAsync", continueBody, StringComparison.Ordinal);
        Assert.Contains("NReturnToMainMenuButton", returnBody, StringComparison.Ordinal);
        Assert.Contains("ForceClick", returnBody, StringComparison.Ordinal);
        Assert.Contains("WaitForGameOverExitAsync", returnBody, StringComparison.Ordinal);
        Assert.Contains("CanReturnToMainMenu(currentScreen)", returnBody, StringComparison.Ordinal);
        Assert.Contains("GetGameOverMainMenuButton(currentScreen)", returnGateBody, StringComparison.Ordinal);
        Assert.Contains("IsGameOverButtonReady", returnGateBody, StringComparison.Ordinal);
        Assert.Contains("IsVisibleInTree()", buttonReadyBody, StringComparison.Ordinal);
        Assert.Contains("IsEnabled", buttonReadyBody, StringComparison.Ordinal);
        Assert.False(
            returnBody.Contains("NGameOverScreen.MethodName.ReturnToMainMenu", StringComparison.Ordinal),
            "return_to_main_menu must click the native summary button instead of invoking the screen method and bypassing score/unlock persistence.");
        Assert.False(
            continueBody.Contains("GetProceedButton", StringComparison.Ordinal),
            "continue_game_over must resolve NGameOverContinueButton directly, not reuse the generic proceed-button path.");
    }

    public static void ContinueWaitsForNativeSummaryReadiness()
    {
        var rawActionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var waitBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawActionSource, "WaitForGameOverSummaryReadyAsync"));

        Assert.Contains(
            "GameStateService.CanReturnToMainMenu(currentScreen)",
            waitBody,
            StringComparison.Ordinal);
        Assert.False(
            waitBody.Contains("CanContinueGameOver", StringComparison.Ordinal),
            "Disabling Continue starts the native summary animation; it must not complete the action before the summary button is ready.");
    }

    public static void GameOverPayloadKeepsContinueSummaryAndReturnAsDistinctPhases()
    {
        var stateSource = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs"));

        Assert.True(
            stateSource.Contains("can_continue=continueButton?.Visible==true&&continueButton?.IsEnabled==true", StringComparison.Ordinal) ||
            stateSource.Contains("can_continue=continueButton?.IsEnabled??false", StringComparison.Ordinal),
            "GAME_OVER can_continue must reflect the native Continue button's enabled state.");
        Assert.Contains(
            "showing_summary=mainMenuButton?.Visible==true",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "can_continue=continueButton?.Visible==true&&continueButton?.IsEnabled==true&&continueButton?.IsVisibleInTree()==true&&!canReturnToMainMenu",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "if(gameOver.can_continue){names.Add(\"continue_game_over\")",
            stateSource,
            StringComparison.Ordinal);
    }

    public static void GameOverPayloadReportsPhysicalProgressSaveVerification()
    {
        var rawStateSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var stateSource = AgentSourceFixture.WithoutWhitespace(rawStateSource);
        var verificationBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "VerifyGameOverProgressSave"));

        Assert.Contains("privateconstintStateVersion=14", stateSource, StringComparison.Ordinal);
        Assert.Contains("privateconstintAgentViewVersion=9", stateSource, StringComparison.Ordinal);
        Assert.Contains("save_status=saveVerification.Status", stateSource, StringComparison.Ordinal);
        Assert.Contains("save_verified=saveVerification.Verified", stateSource, StringComparison.Ordinal);
        Assert.Contains("save_error=saveVerification.Error", stateSource, StringComparison.Ordinal);
        Assert.Contains("save_status=gameOver.save_status", stateSource, StringComparison.Ordinal);
        Assert.Contains("save_verified=gameOver.save_verified", stateSource, StringComparison.Ordinal);
        Assert.Contains("save_error=gameOver.save_error", stateSource, StringComparison.Ordinal);
        Assert.Contains("if(!summaryReady)", verificationBody, StringComparison.Ordinal);
        Assert.Contains("saveManager.Progress.ToSerializable()", verificationBody, StringComparison.Ordinal);
        Assert.Contains("SaveManager.ToJson(expectedProgress)", verificationBody, StringComparison.Ordinal);
        Assert.Contains("ProgressSaveManager.fileName", verificationBody, StringComparison.Ordinal);
        Assert.Contains("profileScopedPath=saveManager.GetProfileScopedPath(relativePath)", verificationBody, StringComparison.Ordinal);
        Assert.Contains("Godot.FileAccess.Open(profileScopedPath,Godot.FileAccess.ModeFlags.Read)", verificationBody, StringComparison.Ordinal);
        Assert.Contains("ProgressSaveVerification.VerifyJson(expectedJson,persistedFile.GetAsText())", verificationBody, StringComparison.Ordinal);
        Assert.False(
            verificationBody.Contains("ProjectSettings.GlobalizePath", StringComparison.Ordinal),
            "Verification must stay on the same Godot user:// filesystem as SaveManager.");
        Assert.False(
            verificationBody.Contains("SaveProgressFile", StringComparison.Ordinal),
            "Verification must observe the native save result and must not perform a replacement save.");
    }
}
