using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// What the eleven handlers that had no contract of their own promise a caller.
/// </summary>
/// <remarks>
/// A handler was counted as uncovered when no test read its body and no test named its action. After
/// <c>abandon_run</c> got its contract, eleven handlers were left, serving twelve actions
/// (<c>increase_ascension</c> and <c>decrease_ascension</c> share one).
///
/// Three promises hold for all of them and are checked as a table:
///
/// - **The handler refuses by the rule the action surface offers by.** If <c>/state</c> advertises an
///   action under one predicate and the handler refuses under another, an agent is told it may do
///   something and then told it may not. <c>crystal_set_tool</c> kept an inline copy of
///   <c>CanPlayCrystalSphere</c>; it now calls the predicate.
/// - **A refusal is a 409 <c>invalid_action</c> that names the action**, not a silent return.
/// - **Success is observed, not assumed**: the status is <c>completed</c> only when the handler saw
///   the game move, and <c>pending</c> otherwise.
///
/// The rest are the one or two things per handler that a plausible edit would break.
/// </remarks>
internal static class HandlerBehaviourContractTests
{
    /// <summary>
    /// action, handler, the predicate the surface offers it by, and the one the handler refuses by.
    /// </summary>
    private static readonly (string Action, string Handler, string SurfacePredicate, string HandlerPredicate)[] Handlers =
    {
        ("open_character_select", "ExecuteOpenCharacterSelectAsync", "CanOpenCharacterSelect", "CanOpenCharacterSelect"),
        ("open_timeline", "ExecuteOpenTimelineAsync", "CanOpenTimeline", "CanOpenTimeline"),
        ("confirm_timeline_overlay", "ExecuteConfirmTimelineOverlayAsync", "CanConfirmTimelineOverlay", "CanConfirmTimelineOverlay"),
        ("open_chest", "ExecuteOpenChestAsync", "CanOpenChest", "CanOpenChest"),
        ("choose_treasure_relic", "ExecuteChooseTreasureRelicAsync", "CanChooseTreasureRelic", "CanChooseTreasureRelic"),
        ("crystal_set_tool", "ExecuteCrystalSetToolAsync", "CanPlayCrystalSphere", "CanPlayCrystalSphere"),
        ("close_shop_inventory", "ExecuteCloseShopInventoryAsync", "CanCloseShopInventory", "CanCloseShopInventory"),
        ("disconnect_multiplayer_lobby", "ExecuteDisconnectMultiplayerLobbyAsync", "CanDisconnectMultiplayerLobby", "CanDisconnectMultiplayerLobby"),
        ("increase_ascension", "ExecuteAdjustAscensionAsync", "CanIncreaseAscension", "CanIncreaseAscension"),
        ("decrease_ascension", "ExecuteAdjustAscensionAsync", "CanDecreaseAscension", "CanDecreaseAscension"),
        // The surface asks whether any slot is usable; the handler asks about the slot it was given.
        ("use_potion", "ExecuteUsePotionAsync", "CanUsePotion", "CanUsePotionAtIndex"),
        ("discard_potion", "ExecuteDiscardPotionAsync", "CanDiscardPotion", "CanDiscardPotionAtIndex"),
    };

    // The surface writes each offer as `if (CanX(...)) { descriptors.Add(new ActionDescriptor { name = "x"`.
    private static readonly Regex SurfaceOffer = new(
        @"if\((Can[A-Za-z]+)\([^{}]*\)\)\{descriptors\.Add\(newActionDescriptor\{name=""([a-z_]+)""",
        RegexOptions.Compiled);

    private static string Body(string handler) =>
        AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadActionService(), handler));

    public static void EachHandlerRefusesByTheRuleTheSurfaceOffersBy()
    {
        var state = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.ReadStateService());
        var offeredBy = SurfaceOffer.Matches(state)
            .GroupBy(match => match.Groups[2].Value)
            .ToDictionary(group => group.Key, group => group.Select(match => match.Groups[1].Value).ToArray());

        foreach (var (action, handler, surfacePredicate, handlerPredicate) in Handlers)
        {
            Assert.True(
                offeredBy.TryGetValue(action, out var predicates) && predicates.SequenceEqual(new[] { surfacePredicate }),
                $"The action surface should offer {action} under {surfacePredicate} alone; it offers it under "
                + (predicates == null ? "nothing this test can find" : string.Join(", ", predicates))
                + ". Update this table only if the handler's refusal moved with it.");

            var body = Body(handler);
            Assert.True(
                body.Contains($"GameStateService.{handlerPredicate}(", StringComparison.Ordinal),
                $"{handler} must refuse by GameStateService.{handlerPredicate}, the rule {action} is offered by. "
                + "A handler with a rule of its own can refuse what /state just advertised.");
            Assert.True(
                body.Contains("ApiException(409,\"invalid_action\"", StringComparison.Ordinal),
                $"{handler} must answer an unavailable {action} with 409 invalid_action.");
        }
    }

    public static void EachHandlerReportsWhatItObserved()
    {
        foreach (var handler in Handlers.Select(entry => entry.Handler).Distinct())
        {
            var body = Body(handler);
            Assert.False(
                body.Contains("status=\"completed\"", StringComparison.Ordinal),
                $"{handler} reports completed unconditionally. The status has to come from a wait that "
                + "saw the game move, or an agent is told an action landed that did not.");
            Assert.True(
                Regex.IsMatch(body, @"status=[A-Za-z]+\?""completed"":""pending"""),
                $"{handler} must derive status from what it observed: completed when the wait saw the "
                + "change, pending when it did not.");
        }
    }

    public static void PotionsAreRefusedPreciselyAndQueuedLikeTheGameDoes()
    {
        foreach (var (action, handler) in new[]
                 {
                     ("use_potion", "ExecuteUsePotionAsync"),
                     ("discard_potion", "ExecuteDiscardPotionAsync"),
                 })
        {
            var body = Body(handler);
            Assert.True(
                body.Contains($"ApiException(400,\"invalid_request\",\"{action}requiresoption_index.\"", StringComparison.Ordinal),
                $"{action} without option_index is a malformed request (400), not an unavailable action.");
            Assert.True(
                body.Contains("ApiException(409,\"invalid_target\",\"Theselectedpotionslotisempty.\"", StringComparison.Ordinal),
                $"{action} on an empty slot must say so, rather than acting on nothing.");
        }

        // Discarding goes through the game's synchronised action queue. Removing the potion from the
        // slot directly would change one client's state without telling a co-op partner.
        Assert.True(
            Body("ExecuteDiscardPotionAsync").Contains(
                "RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(newDiscardPotionGameAction(",
                StringComparison.Ordinal),
            "discard_potion must enqueue a DiscardPotionGameAction through the ActionQueueSynchronizer.");
        Assert.True(
            Body("ExecuteUsePotionAsync").Contains("potion.EnqueueManualUse(target);", StringComparison.Ordinal),
            "use_potion must go through PotionModel.EnqueueManualUse, the game's own entry point.");

        // Area and random potions pick their own targets. Handing the game Owner.Creature for one of
        // them makes it drop the use as an invalid target, and use_potion then waits out its timeout
        // and answers pending for a potion that was never going to be drunk.
        var resolve = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadActionService(), "ResolvePotionTarget"));
        foreach (var selfTargeting in new[] { "AllEnemies", "AllAllies", "RandomEnemy", "TargetedNoCreature" })
        {
            Assert.True(
                resolve.Contains($"TargetType.{selfTargeting}=>null", StringComparison.Ordinal),
                $"A {selfTargeting} potion must be used with no explicit target.");
        }
    }

    public static void TreasureRelicsAreChosenThroughTheSynchronizer()
    {
        var body = Body("ExecuteChooseTreasureRelicAsync");
        Assert.True(
            body.Contains("ApiException(400,\"invalid_request\",\"choose_treasure_relicrequiresoption_index.\"", StringComparison.Ordinal),
            "choose_treasure_relic without option_index must be a 400.");
        // The index is checked against, and spent on, the same list: the synchroniser's CurrentRelics.
        // Checking it against the on-screen nodes while picking from the synchroniser would let an
        // index that is valid on one be out of range on the other.
        Assert.True(
            body.Contains("varrelics=RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;", StringComparison.Ordinal) &&
            body.Contains("RunManager.Instance.TreasureRoomRelicSynchronizer.PickRelicLocally(request.option_index.Value);", StringComparison.Ordinal),
            "choose_treasure_relic must range-check against, and pick from, TreasureRoomRelicSynchronizer.");
    }

    public static void MissingButtonsAreTransientNotRefusals()
    {
        // A button that is not there yet is a UI state that passes; a 409 would tell the caller the
        // action is impossible. Each of these must say retryable instead.
        foreach (var (handler, message) in new[]
                 {
                     ("ExecuteOpenChestAsync", "Chestbuttonnotfound."),
                     ("ExecuteCloseShopInventoryAsync", "Shopbackbuttonnotfound."),
                     ("ExecuteOpenCharacterSelectAsync", "Characterselectscreenisunavailable."),
                     ("ExecuteConfirmTimelineOverlayAsync", "Timelineinspectclosebuttonisunavailable."),
                 })
        {
            var body = Body(handler);
            var at = body.IndexOf($"ApiException(503,\"state_unavailable\",\"{message}\"", StringComparison.Ordinal);
            Assert.True(at >= 0, $"{handler} must answer a missing control with 503 state_unavailable ('{message}').");
            Assert.True(
                body.IndexOf("retryable:true", at, StringComparison.Ordinal) > at,
                $"{handler}'s 503 for '{message}' must be retryable.");
        }
    }

    public static void TimelineOverlayRevalidatesBeforeEachClick()
    {
        // The overlay animates and can close between the guard at the top and the click. Every branch
        // re-reads its button and checks it is still there, visible and enabled -- clicking a stale
        // reference reports pending for a click that never happened.
        var body = Body("ExecuteConfirmTimelineOverlayAsync");
        foreach (var getter in new[]
                 {
                     "GetTimelineTutorialAcknowledgeButton",
                     "GetTimelineUnlockConfirmButton",
                     "GetTimelineInspectCloseButton",
                 })
        {
            Assert.True(
                body.Contains($"GameStateService.{getter}(currentScreen)", StringComparison.Ordinal),
                $"confirm_timeline_overlay must re-read {getter} before clicking.");
        }

        Assert.True(
            Regex.Matches(body, @"!confirmButton\.IsEnabled|!closeButton\.IsEnabled|!tutorialButton\.IsEnabled").Count >= 3,
            "Each confirm_timeline_overlay branch must refuse a disabled button rather than click it.");
    }

    public static void CrystalToolIsConfirmedByReadingItBack()
    {
        var body = Body("ExecuteCrystalSetToolAsync");
        // TrySetCrystalSphereTool returning true is not evidence the tool changed; the live minigame is.
        Assert.True(
            body.Contains("liveMinigame.CrystalSphereTool==tool", StringComparison.Ordinal),
            "crystal_set_tool must confirm the tool by reading the live minigame back.");
        Assert.True(
            body.Contains("status=confirmed?\"completed\":\"pending\"", StringComparison.Ordinal),
            "crystal_set_tool's status must follow that read-back.");
    }

    public static void AscensionMovesOneStepThroughTheLobby()
    {
        var body = Body("ExecuteAdjustAscensionAsync");
        // Through the lobby, which syncs the change to a co-op partner; and the wait is for the exact
        // target, so a change that lands elsewhere is not reported as done.
        Assert.True(
            body.Contains("vartargetAscension=characterSelectScreen.Lobby.Ascension+delta;", StringComparison.Ordinal) &&
            body.Contains("characterSelectScreen.Lobby.SyncAscensionChange(targetAscension);", StringComparison.Ordinal) &&
            body.Contains("WaitForLobbyAscensionTransitionAsync(characterSelectScreen,targetAscension,", StringComparison.Ordinal),
            "increase/decrease_ascension must move by exactly one step through Lobby.SyncAscensionChange "
            + "and wait for that exact level.");

        var dispatch = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.ReadActionService());
        Assert.True(
            dispatch.Contains("\"increase_ascension\"=>ExecuteAdjustAscensionAsync(1,\"increase_ascension\")", StringComparison.Ordinal) &&
            dispatch.Contains("\"decrease_ascension\"=>ExecuteAdjustAscensionAsync(-1,\"decrease_ascension\")", StringComparison.Ordinal),
            "increase_ascension must step +1 and decrease_ascension -1, each reporting its own name.");
    }

    public static void GameOverWaitIsDismissedOnlyWhileWaiting()
    {
        var body = Body("ExecuteDismissGameOverWaitAsync");
        // The surface offers this when the game-over payload says waiting_for_other_players, which is
        // IsWaitingForOtherPlayers. The handler must ask the same question.
        Assert.True(
            body.Contains("currentScreenisnotNGameOverScreen||!GameStateService.IsWaitingForOtherPlayers(currentScreen)", StringComparison.Ordinal),
            "dismiss_game_over_wait must refuse unless the game-over screen is waiting for other players.");
        Assert.True(
            AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.ReadStateService())
                .Contains("waiting_for_other_players=IsWaitingForOtherPlayers(currentScreen)", StringComparison.Ordinal),
            "The surface offers dismiss_game_over_wait on waiting_for_other_players; that has to stay "
            + "IsWaitingForOtherPlayers, the question the handler asks.");
        Assert.True(
            body.Contains("ApiException(409,\"invalid_action\"", StringComparison.Ordinal),
            "An unavailable dismiss_game_over_wait must answer 409 invalid_action.");
    }

    public static void LobbyDisconnectQuitsThroughTheRegistry()
    {
        var body = Body("ExecuteDisconnectMultiplayerLobbyAsync");
        Assert.True(
            body.Contains("ReflectedGameMembers.Method(typeof(NMultiplayerTest),\"Disconnect\")", StringComparison.Ordinal),
            "disconnect_multiplayer_lobby must resolve Disconnect through the registry, which probes it at load.");
        Assert.True(
            body.Contains("NetError.Quit", StringComparison.Ordinal),
            "disconnect_multiplayer_lobby must leave with NetError.Quit -- a deliberate leave, not an "
            + "error the partner would be shown as a dropped connection.");
    }
}
