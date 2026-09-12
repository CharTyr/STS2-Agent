namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the request-scoped reward choice. GameActionService.cs is not
/// part of the offline compile, so the static-field shape it must never reintroduce is asserted
/// against the source text.
/// </summary>
internal static class RewardChoiceThreadingContractTests
{
    private static string Body(string source, string methodName)
    {
        return AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(source, methodName));
    }

    public static void RewardChoiceIsNeverStaticState()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");

        Assert.False(
            source.Contains("_pendingCardRewardChoice", StringComparison.Ordinal),
            "A card reward choice must die with the request; a static field can leak into a later call.");
    }

    public static void DrainTakesAndForwardsTheChoice()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var stripped = AgentSourceFixture.WithoutWhitespace(source);

        // Required parameter, declared at the call site rather than defaulted.
        Assert.Contains(
            "DrainRewardFlowAsync(TimeSpantimeout,RewardFlowChoiceStatechoice)",
            stripped,
            StringComparison.Ordinal);

        var drain = Body(source, "DrainRewardFlowAsync");
        Assert.Contains(
            "TryResolveCardRewardAsync(cardRewardScreen,deadline,choice)",
            drain,
            StringComparison.Ordinal);

        // The request's own choice must reach the drain: passing an automatic choice from
        // resolve_rewards would silently drop the explicit index without tripping any other test.
        var resolveRewards = Body(source, "ExecuteResolveRewardsAsync");
        Assert.Contains("DrainRewardFlowAsync(TimeSpan.FromSeconds(", resolveRewards, StringComparison.Ordinal);
        Assert.Contains(",choice)", resolveRewards, StringComparison.Ordinal);

        // The consume-time reset lives in the state object, not in scattered assignments.
        var resolve = Body(source, "TryResolveCardRewardAsync");
        Assert.Contains("choice.ConsumePendingChoice()", resolve, StringComparison.Ordinal);
        Assert.False(
            resolve.Contains("Choice=RewardChoicePolicy.AutoChoice", StringComparison.Ordinal),
            "The consume-time reset must not be reimplemented as an assignment in the handler.");
    }

    public static void CollectRewardsAsksForTheAutomaticChoice()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var body = Body(source, "ExecuteCollectRewardsAndProceedAsync");

        Assert.Contains("RewardChoicePolicy.AutoChoice", body, StringComparison.Ordinal);
        Assert.Contains(
            "newRewardFlowChoiceState(RewardChoicePolicy.AutoChoice)",
            body,
            StringComparison.Ordinal);
    }
}
