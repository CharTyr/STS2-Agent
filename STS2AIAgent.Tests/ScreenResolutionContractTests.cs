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
}
