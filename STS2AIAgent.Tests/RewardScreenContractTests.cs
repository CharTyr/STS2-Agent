namespace STS2AIAgent.Tests;

/// <summary>
/// Executable source-contract coverage for the reward-card overlay's screen name. The overlay
/// pushes itself onto NOverlayStack and creates visible NGridCardHolders, so the generic grid
/// fallback in ResolveNonModalScreen would otherwise report CARD_SELECTION and route the model
/// to select_deck_card, an action this screen does not offer. GameStateService is intentionally
/// not linked into the lightweight test project, so the ordering is pinned from source, in the
/// same shape as <see cref="UnlockScreenContractTests"/>.
/// </summary>
internal static class RewardScreenContractTests
{
    public static void RewardOverlayBranchPrecedesTheVisibleGrid()
    {
        var rawStateSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var resolveBody = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(rawStateSource, "ResolveNonModalScreen"));

        var rewardScreenIndex = resolveBody.IndexOf(
            "if(currentScreenisNCardRewardSelectionScreen)", StringComparison.Ordinal);
        var visibleGridIndex = resolveBody.IndexOf(
            "GetVisibleGridCardHolders(rootNode).Count>0", StringComparison.Ordinal);
        Assert.True(
            rewardScreenIndex >= 0,
            "The reward-card overlay must have an explicit screen-resolution branch ahead of the visible grid.");
        Assert.True(visibleGridIndex >= 0, "The visible card-grid fallback must remain covered by the regression test.");
        Assert.True(
            rewardScreenIndex < visibleGridIndex,
            "NCardRewardSelectionScreen must resolve before its own visible card grid can report CARD_SELECTION.");
        Assert.Contains(
            "return\"REWARD\";",
            resolveBody[rewardScreenIndex..visibleGridIndex],
            StringComparison.Ordinal);

        // Everything from the generic grid branch on is the switch expression. The other grid-backed
        // screens keep their existing CARD_SELECTION arms; only the reward overlay moved ahead of it.
        var switchTail = resolveBody[visibleGridIndex..];
        Assert.Contains(
            "NChooseACardSelectionScreen=>\"CARD_SELECTION\"",
            switchTail,
            StringComparison.Ordinal);
        Assert.Contains(
            "NDeckCardSelectScreenorNDeckUpgradeSelectScreenorNDeckTransformSelectScreenorNDeckEnchantSelectScreen=>\"CARD_SELECTION\"",
            switchTail,
            StringComparison.Ordinal);
        Assert.Contains(
            "NCardGridSelectionScreen=>\"CARD_SELECTION\"",
            switchTail,
            StringComparison.Ordinal);
    }
}

