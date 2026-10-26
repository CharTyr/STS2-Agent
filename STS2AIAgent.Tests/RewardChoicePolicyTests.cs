using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class RewardChoicePolicyTests
{
    public static void ExplicitIndexPicksThatOption()
    {
        var resolution = RewardChoicePolicy.Resolve(1, 3);

        Assert.Equal(RewardChoiceKind.Pick, resolution.Kind);
        Assert.True(resolution.IsValid);
        Assert.Equal(1, resolution.Index);
        Assert.Null(resolution.Reason);
    }

    public static void ExplicitOutOfRangeIndexIsInvalid()
    {
        var above = RewardChoicePolicy.Resolve(3, 3);
        Assert.Equal(RewardChoiceKind.Pick, above.Kind);
        Assert.False(above.IsValid);
        Assert.NotNull(above.Reason);

        // An explicit negative index other than the documented -1 skip must not
        // silently degrade into "pick the first card".
        var below = RewardChoicePolicy.Resolve(-5, 3);
        Assert.Equal(RewardChoiceKind.Pick, below.Kind);
        Assert.False(below.IsValid);
        Assert.NotNull(below.Reason);
    }

    public static void MissingIndexResolvesToTheAutomaticKind()
    {
        // Auto resolves to index 0 as a descriptor, but the drain never executes it: the handler
        // stops at the card screen for an automatic choice (see
        // RewardChoiceThreadingContractTests.AutomaticChoiceStopsAtTheCardDecision), so this index
        // is a placeholder, not a pick.
        var resolution = RewardChoicePolicy.Resolve(RewardChoicePolicy.AutoChoice, 2);

        Assert.Equal(RewardChoiceKind.Auto, resolution.Kind);
        Assert.True(resolution.IsValid);
        Assert.Equal(0, resolution.Index);
    }

    public static void SkipIsValidWithoutOptions()
    {
        foreach (var optionCount in new[] { 0, 3 })
        {
            var resolution = RewardChoicePolicy.Resolve(RewardChoicePolicy.SkipChoice, optionCount);
            Assert.Equal(RewardChoiceKind.Skip, resolution.Kind);
            Assert.True(resolution.IsValid);
        }
    }

    public static void AutoWithoutOptionsIsNotAPick()
    {
        var resolution = RewardChoicePolicy.Resolve(RewardChoicePolicy.AutoChoice, 0);

        Assert.Equal(RewardChoiceKind.Auto, resolution.Kind);
        Assert.False(resolution.IsValid);
        Assert.Contains("no card reward options", resolution.Reason);
    }

    // #219: with every potion slot occupied the game swallowed the click and claim_reward still
    // answered "completed", so a client believed the potion was taken.
    public static void PotionRewardIsNotClaimableWithAFullBelt()
    {
        Assert.False(RewardClaimPolicy.IsClaimable(rowEnabled: true, isPotionReward: true, hasOpenPotionSlot: false));
        Assert.True(RewardClaimPolicy.IsClaimable(rowEnabled: true, isPotionReward: true, hasOpenPotionSlot: true));
    }

    public static void FullBeltDoesNotAffectOtherRewardsOrDisabledRows()
    {
        Assert.True(RewardClaimPolicy.IsClaimable(rowEnabled: true, isPotionReward: false, hasOpenPotionSlot: false));
        Assert.False(RewardClaimPolicy.IsClaimable(rowEnabled: false, isPotionReward: true, hasOpenPotionSlot: true));
        Assert.False(RewardClaimPolicy.IsClaimable(rowEnabled: false, isPotionReward: false, hasOpenPotionSlot: true));
    }

    public static void ClaimRewardRejectsAFullBeltBeforeClicking()
    {
        var source = AgentSourceFixture.ReadActionService();
        var body = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(source, "ExecuteClaimRewardAsync"));

        var guard = body.IndexOf("IsRewardButtonClaimable(selectedReward)", StringComparison.Ordinal);
        var click = body.IndexOf("selectedReward.ForceClick()", StringComparison.Ordinal);

        Assert.True(guard >= 0, "claim_reward must check potion capacity for the selected row.");
        Assert.True(click > guard, "The capacity check must run before the reward row is clicked.");
        Assert.Contains("reason=RewardClaimPolicy.PotionSlotsFullReason", body, StringComparison.Ordinal);
        Assert.Contains("RewardClaimPolicy.PotionSlotsFullMessage", body, StringComparison.Ordinal);
    }

    public static void ClaimableFlagAndActionGateShareTheCapacityRule()
    {
        var rewards = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.Rewards.cs");
        var predicates = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.Predicates.cs");

        Assert.Contains(
            "claimable=IsRewardButtonClaimable(button)",
            AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(rewards, "BuildRewardOptionPayload")),
            StringComparison.Ordinal);
        Assert.Contains(
            "IsRewardButtonClaimable(",
            AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(predicates, "CanClaimReward")),
            StringComparison.Ordinal);
    }

    // #219 asked for a way to replace a potion from the reward screen: only the card-choice
    // sub-screen may keep blocking discard_potion.
    public static void RewardScreenPermitsPotionDiscard()
    {
        var potions = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.Potions.cs");
        var body = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(potions, "CanDiscardPotionsInCurrentScreen"));

        Assert.Contains("NCardRewardSelectionScreen", body, StringComparison.Ordinal);
        Assert.False(
            body.Contains("NRewardsScreen", StringComparison.Ordinal),
            "The main reward screen must allow discard_potion so a full belt can be freed.");
    }
}
