namespace STS2AIAgent.Tests;

/// <summary>
/// Executable source-contract coverage for the Godot-facing unlock screen paths. The full
/// GameStateService is intentionally not linked into the lightweight test project.
/// </summary>
internal static class UnlockScreenContractTests
{
    public static void UnlockCardsScreenWithVisibleGridReportsOnlyUnlockAction()
    {
        var rawStateSource = AgentSourceFixture.ReadStateService();
        var resolveBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "ResolveNonModalScreen"));
        var walkerBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(
                rawStateSource,
                "private static List<ActionDescriptor> EnumerateAvailableActions("));
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

        // One walk feeds both surfaces, so the UNLOCK branch is pinned once instead of once per surface.
        var unlockBranch = SliceUnlockBranch(
            walkerBody,
            "if(CanEndTurn(currentScreen,combatState,requireButtonReady:false,combatActionGate:combatActionGate))");
        Assert.Contains("name=\"confirm_unlock\"", unlockBranch, StringComparison.Ordinal);
        Assert.Contains("returndescriptors;", unlockBranch, StringComparison.Ordinal);
        Assert.False(
            unlockBranch.Contains("select_deck_card", StringComparison.Ordinal),
            "UNLOCK must not expose select_deck_card on either action surface.");

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
