using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the screen names the mod resolves and the predicates that make the
/// previously dead-end screens actionable. GameStateService.cs is not part of the offline compile, so
/// the mapping table, the widened guards, and the shared Fake Merchant helper are pinned from source.
/// </summary>
internal static class ScreenResolutionContractTests
{
    private static readonly (string ScreenType, string ScreenName)[] ExpectedMappings =
    {
        ("NGameOverScreen", "GAME_OVER"),
        ("NCardRewardSelectionScreen", "REWARD"),
        ("NChooseACardSelectionScreen", "CARD_SELECTION"),
        ("NDeckCardSelectScreen or NDeckUpgradeSelectScreen or NDeckTransformSelectScreen or NDeckEnchantSelectScreen", "CARD_SELECTION"),
        ("NCardGridSelectionScreen", "CARD_SELECTION"),
        ("NRewardsScreen", "REWARD"),
        ("NTreasureRoom or NTreasureRoomRelicCollection", "CHEST"),
        ("NRestSiteRoom", "REST"),
        ("NMerchantRoom or NMerchantInventory", "SHOP"),
        ("NEventRoom", "EVENT"),
        ("NCombatRoom", "COMBAT"),
        ("NMapScreen or NMapRoom", "MAP"),
        ("NCharacterSelectScreen", "CHARACTER_SELECT"),
        ("NMultiplayerLoadGameScreen", "MULTIPLAYER_LOAD"),
        ("NChooseABundleSelectionScreen", "BUNDLE_SELECTION"),
        ("NCapstoneSubmenuStack", "CAPSTONE_SELECTION"),
        ("NCrystalSphereScreen", "CRYSTAL_SPHERE"),
        ("NTimelineScreen", "TIMELINE"),
        ("NFakeMerchant", "FAKE_MERCHANT"),
        ("NPatchNotesScreen", "PATCH_NOTES"),
        ("NInspectCardScreen", "CARD_INSPECT"),
        ("NInspectRelicScreen", "RELIC_INSPECT"),
        ("NSendFeedbackScreen", "FEEDBACK"),
        ("NSubmenu", "MAIN_MENU"),
        ("NLogoAnimation", "MAIN_MENU"),
        ("NMainMenu", "MAIN_MENU"),
        ("_", "UNKNOWN"),
    };

    public static void EveryScreenMappingIsPinned()
    {
        var rawState = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var body = AgentSourceFixture.MethodBody(rawState, "ResolveNonModalScreen");
        var actual = SwitchMappings(body).ToDictionary(pair => pair.ScreenType, pair => pair.ScreenName);

        Assert.Equal(ExpectedMappings.Length, actual.Count);
        foreach (var (screenType, screenName) in ExpectedMappings)
        {
            Assert.True(actual.TryGetValue(screenType, out var resolved), $"ResolveNonModalScreen must map {screenType}.");
            Assert.Equal(screenName, resolved);
        }

        // These names are the exact strings the sibling skill/documentation tasks publish.
        Assert.Equal("FAKE_MERCHANT", actual["NFakeMerchant"]);
        Assert.Equal("PATCH_NOTES", actual["NPatchNotesScreen"]);
        Assert.Equal("CARD_INSPECT", actual["NInspectCardScreen"]);
        Assert.Equal("RELIC_INSPECT", actual["NInspectRelicScreen"]);
        Assert.Equal("FEEDBACK", actual["NSendFeedbackScreen"]);
    }

    public static void FakeMerchantOpensThroughTheSharedButton()
    {
        var rawState = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var canOpen = Normalize(
            AgentSourceFixture.DeclarationBody(rawState, "public static bool CanOpenShopInventory("));
        Assert.Contains("currentScreen is NMerchantRoom room", canOpen, StringComparison.Ordinal);
        Assert.Contains("room.Inventory != null && !room.Inventory.IsOpen", canOpen, StringComparison.Ordinal);
        Assert.Contains("return GetFakeMerchantButton(currentScreen) != null;", canOpen, StringComparison.Ordinal);

        var helper = Normalize(
            AgentSourceFixture.DeclarationBody(rawState, "public static NMerchantButton? GetFakeMerchantButton("));
        Assert.Contains("currentScreen is not NFakeMerchant fakeMerchant", helper, StringComparison.Ordinal);
        Assert.Contains("fakeMerchant.MerchantButton is not { } merchantButton", helper, StringComparison.Ordinal);
        Assert.Contains("GodotObject.IsInstanceValid(merchantButton)", helper, StringComparison.Ordinal);
        Assert.Contains("merchantButton.IsVisibleInTree()", helper, StringComparison.Ordinal);
        Assert.Contains("merchantButton.IsEnabled", helper, StringComparison.Ordinal);
        // NMerchantButton.OnRelease plays dialogue instead of emitting MerchantOpened for a dead
        // local player, so the advertised button must exclude that state too.
        Assert.Contains("merchantButton.IsLocalPlayerDead", helper, StringComparison.Ordinal);
        Assert.Contains("NMerchantInventory>(\"%Inventory\")", helper, StringComparison.Ordinal);
        Assert.Contains("inventory != null && inventory.IsOpen ? null : merchantButton", helper, StringComparison.Ordinal);

        var rawAction = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var open = Normalize(AgentSourceFixture.MethodBody(rawAction, "ExecuteOpenShopInventoryAsync"));
        Assert.Contains("if (!GameStateService.CanOpenShopInventory(currentScreen))", open, StringComparison.Ordinal);
        Assert.Contains("var fakeMerchantButton = GameStateService.GetFakeMerchantButton(currentScreen);", open, StringComparison.Ordinal);
        Assert.Contains("merchantRoom.OpenInventory();", open, StringComparison.Ordinal);
        Assert.Contains("fakeMerchantButton.ForceClick();", open, StringComparison.Ordinal);
        Assert.Contains("WaitForShopInventoryOpenAsync(TimeSpan.FromSeconds(10))", open, StringComparison.Ordinal);
    }

    public static void PatchNotesClosePathIsWidenedWithoutWeakeningSubmenus()
    {
        var rawState = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var canClose = Normalize(AgentSourceFixture.MethodBody(rawState, "CanCloseMainMenuSubmenu"));
        Assert.Contains("currentScreen is NPatchNotesScreen patchNotes", canClose, StringComparison.Ordinal);
        Assert.Contains("GodotObject.IsInstanceValid(patchNotes) && patchNotes.IsVisibleInTree()", canClose, StringComparison.Ordinal);
        Assert.Contains("currentScreen is not NSubmenu submenu || !submenu.IsVisibleInTree()", canClose, StringComparison.Ordinal);
        Assert.Contains("submenuStack != null && submenuStack.SubmenusOpen", canClose, StringComparison.Ordinal);

        var rawAction = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var close = Normalize(AgentSourceFixture.MethodBody(rawAction, "ExecuteCloseMainMenuSubmenuAsync"));
        Assert.Contains("currentScreen is NPatchNotesScreen patchNotes", close, StringComparison.Ordinal);
        Assert.Contains("GetPrivateField<NButton>(patchNotes, \"_backButton\")", close, StringComparison.Ordinal);
        Assert.Contains("backButton.ForceClick();", close, StringComparison.Ordinal);
        Assert.Contains("((Node)patchNotes).Call(\"Close\");", close, StringComparison.Ordinal);
        Assert.Contains("WaitForPatchNotesCloseAsync(patchNotes, TimeSpan.FromSeconds(10))", close, StringComparison.Ordinal);
        Assert.Contains("submenuStack.Pop();", close, StringComparison.Ordinal);

        // The existing submenu wait keeps its original condition; patch notes get their own waiter.
        var submenuWait = Normalize(AgentSourceFixture.MethodBody(rawAction, "WaitForMainMenuSubmenuCloseAsync"));
        Assert.Contains("!ReferenceEquals(currentScreen, submenu) || !submenuStack.SubmenusOpen", submenuWait, StringComparison.Ordinal);
        Assert.False(
            submenuWait.Contains("NPatchNotesScreen", StringComparison.Ordinal),
            "The patch-notes condition must not be folded into the submenu wait.");

        var patchNotesWait = Normalize(AgentSourceFixture.MethodBody(rawAction, "IsPatchNotesClosed"));
        Assert.Contains("!patchNotes.IsVisibleInTree()", patchNotesWait, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(), patchNotes)", patchNotesWait, StringComparison.Ordinal);
    }

    public static void InspectOverlaysCloseThroughTheirOwnClose()
    {
        var rawState = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var canClose = Normalize(AgentSourceFixture.MethodBody(rawState, "CanCloseCardsView"));
        Assert.Contains("currentScreen is NInspectCardScreen inspectCard", canClose, StringComparison.Ordinal);
        Assert.Contains("currentScreen is NInspectRelicScreen inspectRelic", canClose, StringComparison.Ordinal);
        Assert.Contains("return GetCardsViewBackButton(currentScreen) != null;", canClose, StringComparison.Ordinal);

        var rawAction = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var close = Normalize(AgentSourceFixture.MethodBody(rawAction, "ExecuteCloseCardsViewAsync"));
        Assert.Contains("inspectCard.Close();", close, StringComparison.Ordinal);
        Assert.Contains("inspectRelic.Close();", close, StringComparison.Ordinal);
        Assert.Contains("GameStateService.GetCardsViewBackButton(currentScreen)", close, StringComparison.Ordinal);
        Assert.Contains("WaitForCardsViewCloseAsync(currentScreen, TimeSpan.FromSeconds(10))", close, StringComparison.Ordinal);

        var closed = Normalize(AgentSourceFixture.MethodBody(rawAction, "IsCardsViewClosed"));
        Assert.Contains("closedScreen is NInspectCardScreen or NInspectRelicScreen", closed, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(currentScreen, closedScreen)", closed, StringComparison.Ordinal);
        Assert.Contains("return currentScreen is not NCardsViewScreen;", closed, StringComparison.Ordinal);
    }

    public static void PauseMenuIsNotADecisionScreen()
    {
        var rawState = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var resolveBody = Flat(AgentSourceFixture.MethodBody(rawState, "ResolveNonModalScreen"));

        // The pause menu rides in the same NCapstoneSubmenuStack as the Settings/Compendium/Feedback
        // overlays, and the container keeps its PauseMenu Type while a deeper page (settings, compendium,
        // card library) is pushed on top. The guard therefore has to ask for the pause menu itself, and it
        // must claim PAUSE_MENU before the combat branch and the visible card grid can name a paused game
        // COMBAT or CARD_SELECTION.
        const string pauseGuard = "if(IsPauseMenuOverlay(currentScreen))";
        var pauseIndex = resolveBody.IndexOf(pauseGuard, StringComparison.Ordinal);
        var combatIndex = resolveBody.IndexOf("FindActiveCombatRoom(currentScreen)", StringComparison.Ordinal);
        var visibleGridIndex = resolveBody.IndexOf(
            "GetVisibleGridCardHolders(rootNode).Count>0", StringComparison.Ordinal);

        Assert.True(pauseIndex >= 0, "ResolveNonModalScreen must claim PAUSE_MENU for the pause overlay.");
        Assert.True(combatIndex >= 0, "The combat branch must remain covered by this contract.");
        Assert.True(visibleGridIndex >= 0, "The visible card-grid fallback must remain covered by this contract.");
        Assert.True(pauseIndex < combatIndex, "PAUSE_MENU must resolve before FindActiveCombatRoom can report COMBAT.");
        Assert.True(pauseIndex < visibleGridIndex, "PAUSE_MENU must resolve before the visible grid can report CARD_SELECTION.");
        Assert.Contains("return\"PAUSE_MENU\";", resolveBody[pauseIndex..combatIndex], StringComparison.Ordinal);
        Assert.Contains(
            "NCapstoneSubmenuStack=>\"CAPSTONE_SELECTION\"",
            resolveBody,
            StringComparison.Ordinal);

        // The pause menu is the one capstone-container type whose buttons are not a decision list, so
        // the option getter drops it and both action lists stay empty while it is up.
        var capstoneButtons = Flat(AgentSourceFixture.DeclarationBody(
            rawState,
            "public static IReadOnlyList<NButton> GetCapstoneButtons("));
        Assert.Contains(
            "IsPauseMenuOverlay(capstoneScreen)",
            capstoneButtons,
            StringComparison.Ordinal);

        // The overlay test itself has to prove the page on top of the stack, or a card library browsed
        // from the pause menu would report a paused game the agent cannot act in.
        var overlay = Flat(AgentSourceFixture.DeclarationBody(
            rawState,
            "public static bool IsPauseMenuOverlay("));
        Assert.Contains("CapstoneSubmenuType.PauseMenu", overlay, StringComparison.Ordinal);
        Assert.Contains("Stack?.Peek()isNPauseMenu", overlay, StringComparison.Ordinal);

        var names = Flat(AgentSourceFixture.MethodBody(rawState, "BuildAvailableActionNames"));
        var descriptors = Flat(AgentSourceFixture.MethodBody(rawState, "BuildAvailableActionsPayload"));
        Assert.Contains(pauseGuard, names, StringComparison.Ordinal);
        Assert.Contains(pauseGuard, descriptors, StringComparison.Ordinal);

        // The pause branch returns ahead of the combat actions, so nothing is advertised while paused.
        var pauseInNames = names.IndexOf(pauseGuard, StringComparison.Ordinal);
        var endTurnIndex = names.IndexOf(
            "if(CanEndTurn(currentScreen,combatState,requireButtonReady:false))",
            StringComparison.Ordinal);
        Assert.True(
            endTurnIndex >= 0 && pauseInNames >= 0 && pauseInNames < endTurnIndex,
            "The pause branch must return before the combat actions are advertised.");
    }

    private static (string ScreenType, string ScreenName)[] SwitchMappings(string methodBody)
    {
        var normalized = Normalize(methodBody);
        var start = normalized.IndexOf("return currentScreen switch", StringComparison.Ordinal);
        Assert.True(start >= 0, "ResolveNonModalScreen must keep its screen switch expression.");

        var end = normalized.IndexOf("};", start, StringComparison.Ordinal);
        var slice = end > start ? normalized[start..(end + 2)] : normalized[start..];

        return Regex.Matches(slice, "([A-Za-z_][A-Za-z0-9_]*(?: or [A-Za-z_][A-Za-z0-9_]*)*)\\s*=>\\s*\"([A-Z_]+)\"")
            .Select(match => (ScreenType: match.Groups[1].Value, ScreenName: match.Groups[2].Value))
            .ToArray();
    }

    private static string Normalize(string source)
    {
        return Regex.Replace(source, "\\s+", " ");
    }

    private static string Flat(string source) => AgentSourceFixture.WithoutWhitespace(source);
}
