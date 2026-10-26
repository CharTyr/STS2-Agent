using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// #218: a campfire upgrade on a 36-card deck offered 25 of its 27 upgradable cards, because the card
/// grid only instantiates holders near its scroll position and <c>selection.cards</c> indexed those
/// holders. These pin how the whole list is recovered and how an off-screen card is reached.
/// </summary>
internal static class CardGridCandidatePolicyTests
{
    private sealed class Card
    {
        public Card(string id) => Id = id;

        public string Id { get; }

        public override string ToString() => Id;
    }

    public static void FullListReplacesTheVisibleWindow()
    {
        // The issue's shape: 27 cards in the grid's list, the first 25 instantiated.
        var all = Cards(27);
        var visible = all.Take(25).ToArray();

        var resolved = CardGridCandidatePolicy.ResolveCandidates(visible, new[]
        {
            Candidate(0, all),
        });

        Assert.Equal(27, resolved.Count);
        Assert.True(ReferenceEquals(all[25], resolved[25]), "the first off-screen card must keep its list position");
        Assert.True(ReferenceEquals(all[26], resolved[26]), "the last card (TRACKING in the report) must be offered");
    }

    public static void ListsMissingAVisibleCardAreIgnored()
    {
        var all = Cards(30);
        var visible = all.Take(25).ToArray();
        var selected = new[] { all[3] };
        var unrelated = Cards(40);

        var resolved = CardGridCandidatePolicy.ResolveCandidates(visible, new[]
        {
            Candidate(0, selected),
            Candidate(0, unrelated),
        });

        Assert.Equal(25, resolved.Count);
        Assert.True(ReferenceEquals(visible[0], resolved[0]), "with no qualifying list the window stays the list");
    }

    public static void NearestOwnerWinsOverAWiderScreenList()
    {
        // The grid holds the upgradable cards; the screen also holds the whole deck, upgraded cards
        // included. The grid's list is what the player is offered.
        var upgradable = Cards(27);
        var wholeDeck = upgradable.Concat(Cards(9)).ToArray();
        var visible = upgradable.Take(25).ToArray();

        var resolved = CardGridCandidatePolicy.ResolveCandidates(visible, new[]
        {
            Candidate(2, wholeDeck),
            Candidate(0, upgradable),
        });

        Assert.Equal(27, resolved.Count);
    }

    public static void AWindowMirrorDoesNotHideTheFullList()
    {
        // A list on the grid that only mirrors the window must not beat the real backing list, wherever
        // either lives.
        var all = Cards(27);
        var visible = all.Take(25).ToArray();

        var sameOwner = CardGridCandidatePolicy.ResolveCandidates(visible, new[]
        {
            Candidate(0, visible.ToArray()),
            Candidate(0, all),
        });
        Assert.Equal(27, sameOwner.Count);

        var screenOwnsTheList = CardGridCandidatePolicy.ResolveCandidates(visible, new[]
        {
            Candidate(0, visible.ToArray()),
            Candidate(1, all),
        });
        Assert.Equal(27, screenOwnsTheList.Count);
    }

    public static void DuplicateReferencesAreCountedOnce()
    {
        var all = Cards(26);
        var visible = all.Take(25).ToArray();
        var withDuplicates = all.Concat(new[] { all[0], all[25] }).ToArray();

        var resolved = CardGridCandidatePolicy.ResolveCandidates(visible, new[] { Candidate(0, withDuplicates) });

        Assert.Equal(26, resolved.Count);
    }

    public static void EqualButDistinctCardsStayDistinct()
    {
        // Five Strikes are five objects with one id: matching must be by reference, never by value.
        var strikes = Enumerable.Range(0, 5).Select(_ => new Card("STRIKE_SILENT")).ToArray();
        var visible = strikes.Take(4).ToArray();

        var resolved = CardGridCandidatePolicy.ResolveCandidates(visible, new[] { Candidate(0, strikes) });

        Assert.Equal(5, resolved.Count);
        Assert.True(ReferenceEquals(strikes[4], resolved[4]));
    }

    public static void NoVisibleHolderMeansNoSelection()
    {
        // Availability still comes from the holders: a grid that has not drawn anything is not ready.
        var resolved = CardGridCandidatePolicy.ResolveCandidates(Array.Empty<Card>(), new[] { Candidate(0, Cards(27)) });
        Assert.Equal(0, resolved.Count);
    }

    public static void ScrollDirectionPointsAtTheTarget()
    {
        var window = Enumerable.Range(5, 20).ToArray();
        Assert.Equal(1, CardGridCandidatePolicy.ScrollDirection(26, window));
        Assert.Equal(-1, CardGridCandidatePolicy.ScrollDirection(2, window));
        Assert.Equal(0, CardGridCandidatePolicy.ScrollDirection(10, window));
        // Holders whose card is not in the list (-1) do not count as the window.
        Assert.Equal(1, CardGridCandidatePolicy.ScrollDirection(26, new[] { -1, 3, 4 }));
        Assert.Equal(0, CardGridCandidatePolicy.ScrollDirection(26, new[] { -1 }));
        Assert.Equal(0, CardGridCandidatePolicy.ScrollDirection(-1, window));
    }

    public static void OnlyClickOrSelectHandlersAreInvoked()
    {
        Assert.Equal(0, CardGridCandidatePolicy.RankCardClickHandler("OnCardClicked"));
        Assert.Equal(1, CardGridCandidatePolicy.RankCardClickHandler("OnHolderPressed"));
        Assert.Equal(2, CardGridCandidatePolicy.RankCardClickHandler("SelectCard"));

        foreach (var refused in new[]
                 {
                     "DeselectCard", "UnselectCard", "RemoveCard", "OnCardHovered", "OnCardFocused",
                     "ShowPreview", "InspectCard", "HighlightCard", "AddCard", "UpgradeCard",
                     "TransformCard", "EnchantCard", "SetCards", "",
                 })
        {
            Assert.Null(CardGridCandidatePolicy.RankCardClickHandler(refused));
        }
    }

    public static void StateAndExecutorIndexTheSameFullList()
    {
        var state = AgentSourceFixture.ReadStateService();
        var payload = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(state, "BuildSelectionPayload"));
        Assert.Contains("varcards=GetDeckSelectionCards(currentScreen);", payload);
        Assert.False(
            payload.Contains("GetDeckSelectionOptions(", StringComparison.Ordinal),
            "selection.cards must come from the full card list, not the on-screen holders (#218).");

        var cards = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.DeclarationBody(
            state,
            "public static IReadOnlyList<CardModel> GetDeckSelectionCards("));
        Assert.Contains("varholders=GetDeckSelectionOptions(currentScreen);", cards);
        Assert.Contains("CardGridCandidatePolicy.ResolveCandidates(", cards);

        var executor = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadActionService(), "ExecuteSelectDeckCardAsync"));
        Assert.Contains("varoptions=GameStateService.GetDeckSelectionCards(currentScreen);", executor);
        Assert.Contains("awaitPressCardGridCardAsync(gridScreen,selectedCard,options,request.option_index.Value);", executor);
    }

    public static void OffScreenCardsScrollBeforeAnyFallback()
    {
        var actions = AgentSourceFixture.ReadActionService();
        var press = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(actions, "PressCardGridCardAsync"));

        // Visible holder, then scroll, then a hidden holder: a player's click path before any handler call.
        // After the frames scrolling waited on, a screen that is no longer up gets nothing clicked.
        var visible = press.IndexOf("FindCardGridHolder(screen,card,includeHidden:false)", StringComparison.Ordinal);
        var scroll = press.IndexOf("awaitScrollCardGridToCardAsync(screen,card,cards)", StringComparison.Ordinal);
        var current = press.IndexOf("if(!IsCurrentCardGridScreen(screen))", StringComparison.Ordinal);
        var hidden = press.IndexOf("FindCardGridHolder(screen,card,includeHidden:true)", StringComparison.Ordinal);
        var handler = press.IndexOf("awaitTryInvokeCardGridClickHandlerAsync(screen,card)", StringComparison.Ordinal);
        Assert.True(visible >= 0 && visible < scroll && scroll < current && current < hidden && hidden < handler,
            "PressCardGridCardAsync must try the visible holder, scrolling, then (on a screen still up) a hidden holder, then the handler, in that order.");
        Assert.Contains("reason=\"screen_changed\"", press);

        // An unreachable card is an honest 409, never a click on whatever holder happens to be there.
        Assert.Contains("reason=\"card_not_reachable\"", press);

        var scrollBody = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(actions, "ScrollCardGridToCardAsync"));
        Assert.Contains("step<CardGridScrollStepLimit&&DateTime.UtcNow<deadline", scrollBody);
        Assert.Contains("if(!IsCurrentCardGridScreen(screen)){returnnull;}", scrollBody);
        Assert.Contains(
            "IsCurrentCardGridScreen(NCardGridSelectionScreenscreen)=>GodotObject.IsInstanceValid(screen)&&ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(),screen);",
            AgentSourceFixture.WithoutWhitespace(actions));

        var handlerBody = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(actions, "TryInvokeCardGridClickHandlerAsync"));
        Assert.Contains("parameters[0].ParameterType!=typeof(CardModel)", handlerBody);
        Assert.Contains("CardGridCandidatePolicy.RankCardClickHandler(method.Name)", handlerBody);
        // A Task-returning handler finishes before the click settle starts reading the selection.
        Assert.Contains("varfinished=awaitWaitForGameTaskAsync(pending,CardGridHandlerTimeout);", handlerBody);
        Assert.Contains("awaitfinished;", handlerBody);

        // The fallback scroll only touches controls on the holders' own ancestor chain.
        var controls = AgentSourceFixture.WithoutWhitespace(AgentSourceFixture.MethodBody(actions, "ScrollCardGridControls"));
        Assert.Contains("nodeisScrollContainercontainer&&chain.Contains(container)", controls);
        Assert.Contains("chain.Contains(parent)", controls);
    }

    private static Card[] Cards(int count) =>
        Enumerable.Range(0, count).Select(index => new Card($"CARD_{index}")).ToArray();

    private static CardGridCandidatePolicy.Candidate<Card> Candidate(int rank, IReadOnlyList<Card> items) =>
        new(rank, items);
}
