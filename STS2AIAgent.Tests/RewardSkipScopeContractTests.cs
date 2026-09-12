namespace STS2AIAgent.Tests;

/// <summary>
/// Source-level guards for the reward-set-scoped skip: the read side must go through
/// <c>RewardSkipScope.AppliesTo</c> rather than an unscoped bool, and the owner resolution must
/// cover both reward screens so the scope can be recorded and honored.
/// </summary>
internal static class RewardSkipScopeContractTests
{
    public static void TheUnscopedSkipFieldIsGone()
    {
        var actionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");

        Assert.False(
            actionSource.Contains("_cardRewardSkipped", StringComparison.Ordinal),
            "GameActionService.cs still carries the process-wide skip bool.");
        Assert.Contains("RewardSkipScope CardRewardSkips", actionSource);
    }

    public static void TheButtonFilterGoesThroughTheScope()
    {
        var actionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var filter = AgentSourceFixture.MethodBody(actionSource, "TryGetNextClaimableRewardButton");

        Assert.Contains("CardRewardSkips.AppliesTo(rewardsScreen.GetInstanceId())", filter);
        Assert.False(
            filter.Contains("_cardRewardSkipped", StringComparison.Ordinal),
            "The reward-button filter reads an unscoped skip again.");
    }

    public static void TheOwnerResolutionCoversBothRewardScreens()
    {
        var stateSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var resolver = AgentSourceFixture.MethodBody(stateSource, "GetRewardSetId");

        Assert.Contains("NRewardsScreen", resolver);
        Assert.Contains("NCardRewardSelectionScreen", resolver);
        Assert.Contains("GetInstanceId()", resolver);
    }

    public static void SkipRewardCardsRecordsTheScope()
    {
        var actionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var skip = AgentSourceFixture.MethodBody(actionSource, "ExecuteSkipRewardCardsAsync");

        Assert.Contains("CardRewardSkips.MarkSkipped(GameStateService.GetRewardSetId(currentScreen))", skip);
    }

    public static void TheDrainRecordsAndClearsTheScope()
    {
        var actionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var cardResolution = AgentSourceFixture.MethodBody(actionSource, "TryResolveCardRewardAsync");
        var drain = AgentSourceFixture.MethodBody(actionSource, "DrainRewardFlowAsync");

        Assert.Contains(
            "CardRewardSkips.MarkSkipped(GameStateService.GetRewardSetId(cardRewardScreen))",
            cardResolution);
        Assert.Contains("CardRewardSkips.Clear()", drain);
    }

    public static void ExplicitPicksClearTheScope()
    {
        var actionSource = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var resolve = AgentSourceFixture.MethodBody(actionSource, "ExecuteResolveRewardsAsync");
        var choose = AgentSourceFixture.MethodBody(actionSource, "ExecuteChooseRewardCardAsync");

        Assert.Contains("CardRewardSkips.Clear()", resolve);
        Assert.Contains("CardRewardSkips.Clear()", choose);
    }
}
