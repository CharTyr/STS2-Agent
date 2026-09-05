namespace STS2AIAgent.Tests;

/// <summary>
/// Executable source-contract coverage for the Godot-facing unlock screen paths. The full
/// GameStateService is intentionally not linked into the lightweight test project.
/// </summary>
internal static class UnlockScreenContractTests
{
    public static void UnlockCardsScreenWithVisibleGridReportsOnlyUnlockAction()
    {
        var rawStateSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var resolveBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "ResolveNonModalScreen"));
        var actionNamesBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "BuildAvailableActionNames"));
        var actionDescriptorsBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "BuildAvailableActionsPayload"));
        var canSelectBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "CanSelectDeckCard"));

        var unlockScreenIndex = resolveBody.IndexOf(
            "if(currentScreenisNUnlockScreen)", StringComparison.Ordinal);
        var visibleGridIndex = resolveBody.IndexOf(
            "GetVisibleGridCardHolders(rootNode).Count>0", StringComparison.Ordinal);
        Assert.True(unlockScreenIndex >= 0, "NUnlockScreen must have an explicit screen-resolution branch.");
        Assert.True(visibleGridIndex >= 0, "The visible card-grid fallback must remain covered by the regression test.");
        Assert.True(
            unlockScreenIndex < visibleGridIndex,
            "NUnlockCardsScreen must resolve before its visible card grid can report CARD_SELECTION.");
        Assert.Contains(
            "return\"UNLOCK\";",
            resolveBody[unlockScreenIndex..visibleGridIndex],
            StringComparison.Ordinal);

        var unlockNameBranch = SliceUnlockBranch(actionNamesBody, "if(CanEndTurn(currentScreen,combatState))");
        Assert.Contains(
            "if(CanConfirmUnlock(currentScreen)){names.Add(\"confirm_unlock\");}",
            unlockNameBranch,
            StringComparison.Ordinal);
        Assert.Contains("returnnames.ToArray();", unlockNameBranch, StringComparison.Ordinal);
        Assert.False(
            unlockNameBranch.Contains("select_deck_card", StringComparison.Ordinal),
            "UNLOCK available_actions must not expose select_deck_card.");

        var unlockDescriptorBranch = SliceUnlockBranch(
            actionDescriptorsBody,
            "if(CanEndTurn(currentScreen,combatState))");
        Assert.Contains("name=\"confirm_unlock\"", unlockDescriptorBranch, StringComparison.Ordinal);
        Assert.Contains("returnnewAvailableActionsPayload", unlockDescriptorBranch, StringComparison.Ordinal);
        Assert.False(
            unlockDescriptorBranch.Contains("select_deck_card", StringComparison.Ordinal),
            "The action-descriptor endpoint must not expose select_deck_card on UNLOCK.");

        Assert.Contains(
            "if(currentScreenisNUnlockScreen){returnfalse;}",
            canSelectBody,
            StringComparison.Ordinal);
    }

    private static string SliceUnlockBranch(string methodBody, string nextBranch)
    {
        var start = methodBody.IndexOf("if(currentScreenisNUnlockScreen)", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new Exception("NUnlockScreen action branch is missing.");
        }

        var end = methodBody.IndexOf(nextBranch, start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new Exception($"Expected branch after NUnlockScreen is missing: {nextBranch}");
        }

        return methodBody[start..end];
    }
}
