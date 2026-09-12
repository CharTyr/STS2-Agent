namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the two card viewers that draw the same visible card grid:
/// NCardLibrary (the deck / compendium viewer) and NCardPileScreen (a combat draw / discard /
/// exhaust pile). Both would otherwise fall through to the generic grid branch in
/// ResolveNonModalScreen and be reported as CARD_SELECTION, an action they deliberately do not
/// offer, and both were dead ends until their close paths were widened. GameStateService and
/// GameActionService are not linked into this project, so the branch ordering, the screen names,
/// the shared closable-viewer predicate, and the submenu-stack lookup are pinned from source in
/// the same shape as <see cref="RewardScreenContractTests"/> and
/// <see cref="DeckSelectionAvailabilityTests"/>.
/// </summary>
internal static class CardViewerScreenContractTests
{
    private const string StatePath = "STS2AIAgent/Game/GameStateService.cs";
    private const string ActionPath = "STS2AIAgent/Game/GameActionService.cs";

    public static void ViewerBranchesPrecedeTheVisibleGrid()
    {
        var resolveBody = Flat(AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read(StatePath),
            "ResolveNonModalScreen"));

        var libraryIndex = resolveBody.IndexOf("if(currentScreenisNCardLibrary)", StringComparison.Ordinal);
        var pileIndex = resolveBody.IndexOf("if(currentScreenisNCardPileScreen)", StringComparison.Ordinal);
        var visibleGridIndex = resolveBody.IndexOf(
            "GetVisibleGridCardHolders(rootNode).Count>0", StringComparison.Ordinal);

        Assert.True(libraryIndex >= 0, "NCardLibrary must have its own screen-resolution branch before the visible grid.");
        Assert.True(pileIndex >= 0, "NCardPileScreen must have its own screen-resolution branch before the visible grid.");
        Assert.True(visibleGridIndex >= 0, "The visible card-grid fallback must remain covered by this contract.");
        Assert.True(
            libraryIndex < visibleGridIndex,
            "NCardLibrary must resolve before its own visible card grid can report CARD_SELECTION.");
        Assert.True(
            pileIndex < visibleGridIndex,
            "NCardPileScreen must resolve before its own visible card grid can report CARD_SELECTION.");
        Assert.True(
            libraryIndex < pileIndex,
            "The two viewer branches keep a stable order: CARD_LIBRARY then CARD_PILE.");

        Assert.Contains("return\"CARD_LIBRARY\";", resolveBody[libraryIndex..pileIndex], StringComparison.Ordinal);
        Assert.Contains("return\"CARD_PILE\";", resolveBody[pileIndex..visibleGridIndex], StringComparison.Ordinal);
    }

    public static void ClosableViewerSetIsSharedByProbeAndExecutor()
    {
        var stateSource = AgentSourceFixture.Read(StatePath);
        var actionSource = AgentSourceFixture.Read(ActionPath);

        // One predicate owns the widened set: NCardsViewScreen plus NCardPileScreen. Narrowing it
        // back to NCardsViewScreen alone has to turn this test red.
        var predicate = Flat(AgentSourceFixture.DeclarationBody(
            stateSource,
            "public static bool IsClosableCardViewer("));
        Assert.Contains(
            "screenisNCardsViewScreenorNCardPileScreen",
            predicate,
            StringComparison.Ordinal);

        var backButton = Flat(AgentSourceFixture.DeclarationBody(
            stateSource,
            "public static NButton? GetCardsViewBackButton("));
        Assert.Contains("IsClosableCardViewer(currentScreen)", backButton, StringComparison.Ordinal);
        Assert.False(
            backButton.Contains("NCardPileScreen", StringComparison.Ordinal),
            "The widened viewer set must live only in IsClosableCardViewer, so a screen cannot be "
            + "added to the back-button lookup alone.");

        var canClose = Flat(AgentSourceFixture.DeclarationBody(
            stateSource,
            "public static bool CanCloseCardsView("));
        Assert.Contains("returnGetCardsViewBackButton(currentScreen)!=null;", canClose, StringComparison.Ordinal);

        // The executor's settled check consults the same shared set, so a screen that offers
        // close_cards_view also settles when it closes.
        var closed = Flat(AgentSourceFixture.MethodBody(actionSource, "IsCardsViewClosed"));
        Assert.Contains("GameStateService.IsClosableCardViewer(currentScreen)", closed, StringComparison.Ordinal);
    }

    public static void SubmenuStackLookupUsesTheBaseClass()
    {
        var stateSource = AgentSourceFixture.Read(StatePath);
        var actionSource = AgentSourceFixture.Read(ActionPath);

        var lookup = Flat(AgentSourceFixture.DeclarationBody(
            stateSource,
            "public static NSubmenuStack? GetSubmenuStack("));
        Assert.Contains("currentisNSubmenuStacksubmenuStack", lookup, StringComparison.Ordinal);
        Assert.False(
            lookup.Contains("NMainMenuSubmenuStack", StringComparison.Ordinal),
            "The lookup must match the shared base NSubmenuStack: the in-run deck library hangs off "
            + "NRunSubmenuStack, a sibling of the main-menu stack.");

        var canClose = Flat(AgentSourceFixture.DeclarationBody(
            stateSource,
            "public static bool CanCloseMainMenuSubmenu("));
        Assert.Contains("GetSubmenuStack(submenu)", canClose, StringComparison.Ordinal);

        var close = Flat(AgentSourceFixture.MethodBody(actionSource, "ExecuteCloseMainMenuSubmenuAsync"));
        Assert.Contains("GameStateService.GetSubmenuStack(submenu)", close, StringComparison.Ordinal);

        Assert.False(
            stateSource.Contains("GetMainMenuSubmenuStack", StringComparison.Ordinal),
            "The main-menu-only lookup name must be gone so the in-run stack is never silently ignored.");
        Assert.False(
            actionSource.Contains("GetMainMenuSubmenuStack", StringComparison.Ordinal),
            "The executor must call the widened lookup too.");
    }

    private static string Flat(string source) => AgentSourceFixture.WithoutWhitespace(source);
}
