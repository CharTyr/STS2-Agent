namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the action-trust fixes. GameActionService.cs is not part of the
/// offline compile, so the removed "modal means done" / silent-fallback shapes are asserted here.
/// </summary>
internal static class GameActionTrustContractTests
{
    private static string Body(string source, string methodName)
    {
        return AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(source, methodName));
    }

    public static void RewardConsumeNeverFallsBackToTheFirstOption()
    {
        var source = AgentSourceFixture.ReadActionService();
        var body = Body(source, "TryResolveCardRewardAsync");

        Assert.Contains("RewardChoicePolicy.Resolve(", body, StringComparison.Ordinal);
        Assert.Contains("\"invalid_target\"", body, StringComparison.Ordinal);
        Assert.False(
            body.Contains("FirstOrDefault", StringComparison.Ordinal),
            "An explicit out-of-range card index must not fall back to the first option.");
    }

    public static void RewardRequestRejectsAnOutOfRangeIndexBeforeClicking()
    {
        var source = AgentSourceFixture.ReadActionService();
        var body = Body(source, "ExecuteResolveRewardsAsync");

        var validateIndex = body.IndexOf("RewardChoicePolicy.Resolve(", StringComparison.Ordinal);
        var drainIndex = body.IndexOf("DrainRewardFlowAsync(", StringComparison.Ordinal);

        Assert.True(validateIndex >= 0, "resolve_rewards must validate the requested index up front.");
        Assert.True(
            drainIndex > validateIndex,
            "An out-of-range index must be rejected before the reward flow drains or clicks anything.");
        Assert.Contains("\"invalid_target\"", body, StringComparison.Ordinal);

        // A card reward screen that just opened exposes no options for a few frames, so the
        // early check must only judge a non-empty list instead of failing a legal pick.
        Assert.Contains("optionCount>0&&!resolution.IsValid", body, StringComparison.Ordinal);
    }

    public static void ShopRemovalPurchaseFailureSurfaces()
    {
        var source = AgentSourceFixture.ReadActionService();
        var body = Body(source, "ExecuteRemoveCardAtShopAsync");

        Assert.Contains("BackgroundTaskOutcome.DescribeFailure(", body, StringComparison.Ordinal);
        Assert.Contains("!stable&&purchaseFailure!=null", body, StringComparison.Ordinal);
        Assert.Contains("\"invalid_action\"", body, StringComparison.Ordinal);
        Assert.False(
            body.Contains("awaitpurchaseTask", StringComparison.Ordinal),
            "The success path must stay fire-and-forget on the deck-selection screen.");
    }

    public static void MenuExitWaitDoesNotTreatAModalAsSuccess()
    {
        var source = AgentSourceFixture.ReadActionService();
        var waitBody = Body(source, "WaitForMainMenuExitAsync");
        var helperBody = Body(source, "IsMenuExitSettled");

        Assert.Contains("IsMenuExitSettled(", waitBody, StringComparison.Ordinal);
        Assert.False(
            waitBody.Contains("GetOpenModal", StringComparison.Ordinal),
            "An open modal must not be accepted as a completed menu exit.");
        Assert.Contains("MenuTransitionPolicy.IsMenuExited(", helperBody, StringComparison.Ordinal);
    }

    public static void EmbarkWaitDoesNotTreatAModalAsSuccess()
    {
        var source = AgentSourceFixture.ReadActionService();
        var waitBody = Body(source, "WaitForEmbarkTransitionAsync");
        var helperBody = Body(source, "IsEmbarkSettled");

        Assert.Contains("IsEmbarkSettled(", waitBody, StringComparison.Ordinal);
        Assert.False(
            waitBody.Contains("GetOpenModal", StringComparison.Ordinal),
            "An open modal must not be accepted as a completed embark transition.");
        Assert.Contains("MenuTransitionPolicy.IsEmbarkSettled(", helperBody, StringComparison.Ordinal);
    }

    public static void CharacterSelectNeedsTheScreenItself()
    {
        var source = AgentSourceFixture.ReadActionService();

        Assert.False(
            source.Contains("IsCharacterSelectOpenOrActionableModal", StringComparison.Ordinal),
            "An actionable modal must no longer count as an open character select.");
        Assert.False(
            source.Contains(
                "CanConfirmModal(currentScreen) || GameStateService.CanDismissModal(currentScreen)",
                StringComparison.Ordinal),
            "The character-select transition must not settle on a modal alone.");
        var openBody = Body(source, "IsCharacterSelectOpen");
        Assert.Contains("MenuTransitionPolicy.IsCharacterSelectSettled(", openBody, StringComparison.Ordinal);
        Assert.Contains(
            "modalOpen:GameStateService.GetOpenModal()!=null",
            openBody,
            StringComparison.Ordinal);
    }

    public static void PlayCardIdentityIsVerifiedBeforeTheCardIsPlayed()
    {
        var source = AgentSourceFixture.ReadActionService();

        // The request carries the identity as a wire field. Router deserializes ActionRequest through
        // the shared helper, so the property is the whole binding -- there is no hand-copied field
        // list to update for a new optional argument.
        var requestType = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(source, "internal sealed class ActionRequest"));
        Assert.Contains("publiclong?card_cid{get;init;}", requestType, StringComparison.Ordinal);
        Assert.Contains(
            "ReadJsonBodyAsync<ActionRequest>(request,cancellationToken)",
            AgentSourceFixture.WithoutWhitespace(
                AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs")),
            StringComparison.Ordinal);

        var body = Body(source, "ExecutePlayCardAsync");
        var validate = body.IndexOf(
            "ValidateCardIdentity(request,hand,request.card_index.Value,screen);",
            StringComparison.Ordinal);
        var play = body.IndexOf("card.TryManualPlay(", StringComparison.Ordinal);
        var rangeCheck = body.IndexOf("card_indexisoutofrange.", StringComparison.Ordinal);
        Assert.True(validate >= 0, "play_card must verify card_cid against the live hand.");
        Assert.True(
            rangeCheck >= 0 && validate > rangeCheck,
            "The identity check belongs after the position bounds check, so an out-of-range index stays "
            + "an index error rather than being reported as a stale card.");
        Assert.True(
            play > validate,
            "The identity must be verified before TryManualPlay: once the native call runs the card is "
            + "played, and no later check can undo it.");

        var helper = Body(source, "ValidateCardIdentity");
        Assert.Contains("GameStateService.TryGetCombatCardId(hand[cardIndex])", helper, StringComparison.Ordinal);
        Assert.Contains("ApiException(409,\"invalid_target\"", helper, StringComparison.Ordinal);
        Assert.Contains("stale=true", helper, StringComparison.Ordinal);
        // Reject, never retarget: no fallback play, no search for the cid elsewhere in the hand.
        Assert.False(
            helper.Contains("TryManualPlay", StringComparison.Ordinal),
            "The identity check must not play the card it was asked to verify.");
        Assert.False(
            helper.Contains("FirstOrDefault", StringComparison.Ordinal) ||
            helper.Contains("FindCardIndex", StringComparison.Ordinal),
            "A stale identity must be refused; searching the hand for the cid would play a card the "
            + "caller never validated against the state it read.");
        Assert.False(
            helper.Contains("await", StringComparison.Ordinal),
            "The check reads the hand the handler is about to index in one synchronous step, so no "
            + "other action can interleave between the check and the play.");
    }

    public static void BundleHandlersNeverFabricateAnEmptyState()
    {
        var source = AgentSourceFixture.ReadActionService();

        Assert.False(
            source.Contains("?? new GameStatePayload()", StringComparison.Ordinal),
            "Bundle handlers must surface state_unavailable instead of a fabricated empty state.");
        Assert.False(
            source.Contains("??newGameStatePayload()", StringComparison.Ordinal),
            "Bundle handlers must surface state_unavailable instead of a fabricated empty state.");

        foreach (var methodName in new[] { "ExecuteChooseBundleAsync", "ExecuteConfirmBundleAsync" })
        {
            var body = Body(source, methodName);
            Assert.Contains("BuildActionState(", body, StringComparison.Ordinal);
            Assert.False(body.Contains("newGameStatePayload()", StringComparison.Ordinal));
        }

        // The shared builder is also called from the handlers above, so locate its declaration
        // rather than its last call site (the MethodBody helper takes the last match).
        var stripped = AgentSourceFixture.WithoutWhitespace(source);
        var helperStart = stripped.IndexOf("GameStatePayloadBuildActionState(", StringComparison.Ordinal);
        Assert.True(helperStart >= 0, "Bundle handlers must share a state builder that fails honestly.");
        var helperWindow = stripped.Substring(helperStart, Math.Min(400, stripped.Length - helperStart));
        Assert.Contains("\"state_unavailable\"", helperWindow, StringComparison.Ordinal);
    }
}
