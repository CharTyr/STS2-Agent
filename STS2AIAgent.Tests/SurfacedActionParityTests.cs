namespace STS2AIAgent.Tests;

/// <summary>
/// Source-level contracts for "every surfaced signal is one the executor accepts". GameStateService
/// does not compile into this project (it references the game assemblies), so each contract reads the
/// payload builder and pairs it with the Can* probe the executor re-checks, in the same shape as
/// <see cref="DeckSelectionAvailabilityTests"/> and <see cref="RewardScreenContractTests"/>.
/// </summary>
internal static class SurfacedActionParityTests
{
    private const string StatePath = "STS2AIAgent/Game/GameStateService.cs";
    private const string ActionPath = "STS2AIAgent/Game/GameActionService.cs";

    public static void SelectionCanConfirmComesFromTheExecutorProbe()
    {
        var source = AgentSourceFixture.Read(StatePath);
        var payload = Flat(AgentSourceFixture.MethodBody(source, "BuildSelectionPayload"));
        var probe = Flat(AgentSourceFixture.DeclarationBody(
            source,
            "public static bool CanConfirmSelection("));

        Assert.Contains(
            "can_confirm=CanConfirmSelection(currentScreen)",
            payload,
            StringComparison.Ordinal);

        // The raw metadata alone over-reported: a single-select grid that settles on the click has
        // CanConfirm true while confirm_selection is never exposed for it, so the model was told to
        // confirm a screen the executor answers with 409.
        Assert.False(
            payload.Contains("can_confirm=hasCombatHandSelection", StringComparison.Ordinal)
            || payload.Contains("can_confirm=hasCardGridSelection", StringComparison.Ordinal),
            "selection.can_confirm must be the same predicate that exposes confirm_selection, not a "
            + "second copy of the native CanConfirm metadata.");

        // The probe keeps both native gates the payload used to ignore.
        Assert.Contains("combatMetadata.RequiresConfirmation&&combatMetadata.CanConfirm", probe, StringComparison.Ordinal);
        Assert.Contains("gridMetadata.CanConfirm", probe, StringComparison.Ordinal);
        Assert.Contains("gridMetadata.MinSelect<gridMetadata.MaxSelect", probe, StringComparison.Ordinal);
    }

    public static void ModalCanConfirmComesFromTheExecutorProbe()
    {
        var source = AgentSourceFixture.Read(StatePath);
        var payload = Flat(AgentSourceFixture.MethodBody(source, "BuildModalPayload"));
        var probe = Flat(AgentSourceFixture.DeclarationBody(
            source,
            "public static bool CanConfirmModal("));

        Assert.Contains(
            "can_confirm=CanConfirmModal(currentScreen)",
            payload,
            StringComparison.Ordinal);

        // A FTUE popup without its own usable button is still confirmable (the executor closes it
        // directly through FtueModalPolicy), so a bare `confirmButton != null` under-reported it.
        Assert.False(
            payload.Contains("can_confirm=confirmButton!=null", StringComparison.Ordinal),
            "modal.can_confirm must be CanConfirmModal(currentScreen); confirm_modal is exposed for a "
            + "button-less FTUE modal, so a bare button check disagrees with the action list.");
        Assert.Contains(
            "FtueModalPolicy.ExposeConfirm(GetOpenModal()?.GetType().Name,hasButton)",
            probe,
            StringComparison.Ordinal);
    }

    public static void ResolveRewardsDescriptorDoesNotRequireAnIndex()
    {
        var payload = Flat(AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read(StatePath),
            "BuildAvailableActionsPayload"));

        // ExecuteResolveRewardsAsync treats option_index/card_index as optional (absent means
        // "auto"), so requiring an index made callers send one that was then ignored.
        Assert.Contains(
            "name=\"resolve_rewards\",requires_target=false,requires_index=false",
            payload,
            StringComparison.Ordinal);
        Assert.Contains(
            "name=\"collect_rewards_and_proceed\",requires_target=false,requires_index=false",
            payload,
            StringComparison.Ordinal);
    }

    public static void SkipRewardCardsFiltersOnAlternativeButtonEnablement()
    {
        var source = AgentSourceFixture.Read(StatePath);
        var probe = Flat(AgentSourceFixture.DeclarationBody(
            source,
            "public static bool CanSkipRewardCards("));

        // NCardRewardAlternativeButton is an NButton, so IsEnabled is the same signal CanClaimReward
        // reads from NRewardButton. The getter already drops alternatives that are not visible.
        Assert.Contains(
            "GetCardRewardAlternativeButtons(currentScreen).Any(button=>button.IsEnabled)",
            probe,
            StringComparison.Ordinal);

        // Availability and the executor must read the same alternative list.
        var executor = Flat(AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read(ActionPath),
            "ExecuteSkipRewardCardsAsync"));
        Assert.Contains("GameStateService.CanSkipRewardCards(currentScreen)", executor, StringComparison.Ordinal);
        Assert.Contains("GameStateService.GetCardRewardAlternativeButtons(currentScreen)", executor, StringComparison.Ordinal);
    }

    public static void ChooseRewardCardStaysOnTheExecutorCollection()
    {
        var source = AgentSourceFixture.Read(StatePath);
        var probe = Flat(AgentSourceFixture.DeclarationBody(
            source,
            "public static bool CanChooseRewardCard("));

        // Deliberately unfiltered, and this is the conclusion for the reward-card half of the audit:
        // NCardHolder (and NGridCardHolder) derive from Control, which has no IsEnabled, and
        // ExecuteChooseRewardCardAsync picks by emitting NCardHolder.Pressed directly, which bypasses
        // the holder's private _isClickable anti-misclick gate. Count > 0 is therefore exactly the set
        // the executor resolves; an enabled/visible filter would only report a false negative for a
        // pick that still works.
        Assert.Contains(
            "returnGetCardRewardOptions(currentScreen).Count>0;",
            probe,
            StringComparison.Ordinal);

        var executor = Flat(AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read(ActionPath),
            "ExecuteChooseRewardCardAsync"));
        Assert.Contains("GameStateService.CanChooseRewardCard(currentScreen)", executor, StringComparison.Ordinal);
        Assert.Contains("GameStateService.GetCardRewardOptions(currentScreen)", executor, StringComparison.Ordinal);
        Assert.Contains("EmitSignal(NCardHolder.SignalName.Pressed", executor, StringComparison.Ordinal);
    }

    public static void CrystalSphereExposureStaysOnTheScreenTypeGuard()
    {
        var source = AgentSourceFixture.Read(StatePath);
        var probe = Flat(AgentSourceFixture.DeclarationBody(
            source,
            "public static bool CanPlayCrystalSphere("));
        var minigame = Flat(AgentSourceFixture.DeclarationBody(
            source,
            "public static CrystalSphereMinigame? GetCrystalSphereMinigame("));

        // Conclusion for the crystal-sphere row of the audit: no visibility filter is added, because
        // "resolved but not visible" is unreachable. In the shipped game code
        // ActiveScreenContext.GetCurrentScreen() only yields NCrystalSphereScreen through
        // NOverlayStack.Peek(); that screen is pushed with Visible left true (AfterOverlayOpened only
        // tweens modulate:a, AfterOverlayHidden only disables its proceed button), and while the map or
        // a capstone covers the stack GetCurrentScreen returns that other context first. The screen
        // type guard below is therefore the whole gate, and IsFinished is the real remaining condition.
        Assert.Contains(
            "if(currentScreenisnotNCrystalSphereScreencrystalSphereScreen){returnnull;}",
            minigame,
            StringComparison.Ordinal);
        Assert.Contains(
            "varminigame=GetCrystalSphereMinigame(currentScreen);returnminigameis{IsFinished:false};",
            probe,
            StringComparison.Ordinal);
    }

    public static void SkipTargetsEnabledAlternative()
    {
        var executor = Flat(AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read(ActionPath),
            "ExecuteSkipRewardCardsAsync"));
        var probe = Flat(AgentSourceFixture.DeclarationBody(
            AgentSourceFixture.Read(StatePath),
            "public static bool CanSkipRewardCards("));

        // Exposure and execution have to pick from the same enabled-filtered set. The 409 guard only
        // proves that *some* alternative is enabled, so the executor must click the first enabled
        // one; a bare alternatives.First() can land on a disabled button in exactly the case the
        // probe allowed, which is the surfaced-accepts/executor-rejects mismatch this test pins.
        Assert.Contains("Any(button=>button.IsEnabled)", probe, StringComparison.Ordinal);
        Assert.Contains(
            "alternatives.First(button=>button.IsEnabled)",
            executor,
            StringComparison.Ordinal);
        Assert.False(
            executor.Contains("alternatives.First()", StringComparison.Ordinal),
            "ExecuteSkipRewardCardsAsync must not click the first alternative blindly; only the "
            + "enabled-filtered set is what CanSkipRewardCards declared non-empty.");
    }

    private static string Flat(string source) => AgentSourceFixture.WithoutWhitespace(source);
}
