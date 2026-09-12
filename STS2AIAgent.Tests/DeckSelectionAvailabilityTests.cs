namespace STS2AIAgent.Tests;

/// <summary>
/// Source-level contracts for <c>select_deck_card</c> availability. GameStateService does not compile
/// into this project (it needs the game assemblies), so "available implies executable" is pinned by
/// reading both method bodies: the availability probe and the executor guard must agree on the same
/// three screen shapes. The generic "any visible grid holder in the screen subtree" scan used to make
/// the two sets disagree -- /state advertised select_deck_card (and a synthetic 1/1 deck_card_select)
/// for NCardRewardSelectionScreen / NCardPileScreen / NCardLibrary, while the executor answered 409.
/// </summary>
internal static class DeckSelectionAvailabilityTests
{
    private const string StatePath = "STS2AIAgent/Game/GameStateService.cs";
    private const string ActionPath = "STS2AIAgent/Game/GameActionService.cs";
    private const string AvailabilityDeclaration =
        "public static IReadOnlyList<NCardHolder> GetDeckSelectionOptions(";

    public static void AvailabilityMatchesTheExecutableSet()
    {
        // DeclarationBody, not MethodBody: GetDeckSelectionOptions is called again from
        // BuildSelectionPayload below its own declaration, so a name lookup would land on that call site.
        var body = Flat(AgentSourceFixture.DeclarationBody(
            AgentSourceFixture.Read(StatePath),
            AvailabilityDeclaration));

        // The deleted branch was `if (currentScreen is Node rootNode) { return GetVisibleGridCardHolders(rootNode)... }`.
        // Matching the type test rather than the local name keeps this red for any rewrite of that
        // generic scan (`Node node`, `is Node`, ...): availability may only come from the three
        // executable shapes, because a subtree scan cannot prove any holder reacts to a click.
        Assert.False(
            body.Contains("currentScreenisNode", StringComparison.Ordinal),
            "GetDeckSelectionOptions must not fall back to a generic `currentScreen is Node` subtree scan: "
            + "it advertises select_deck_card on screens ExecuteSelectDeckCardAsync rejects with 409.");

        // The three shapes the executor accepts must stay reachable from the availability probe.
        Assert.Contains("currentScreenisNCardGridSelectionScreen", body, StringComparison.Ordinal);
        Assert.Contains("currentScreenisNChooseACardSelectionScreen", body, StringComparison.Ordinal);
        Assert.Contains("TryGetCombatHandSelection(currentScreen,outvarhand)", body, StringComparison.Ordinal);
        Assert.Contains("returnArray.Empty<NCardHolder>();", body, StringComparison.Ordinal);
    }

    public static void ExecutorKeepsItsGuard()
    {
        var body = Flat(AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read(ActionPath),
            "ExecuteSelectDeckCardAsync"));

        // Deleting the fallback alone would be undone by a later "just drop the 409 guard" change:
        // this pins the executor's executable set to the same three criteria the probe reports.
        Assert.Contains(
            "!isCombatHandSelection&&!isCardGridSelection&&currentScreenisnotNChooseACardSelectionScreen",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "thrownewApiException(409,\"invalid_action\"",
            body,
            StringComparison.Ordinal);

        var availability = Flat(AgentSourceFixture.DeclarationBody(
            AgentSourceFixture.Read(StatePath),
            AvailabilityDeclaration));
        foreach (var criterion in new[]
                 {
                     "NCardGridSelectionScreen",
                     "NChooseACardSelectionScreen",
                     "TryGetCombatHandSelection"
                 })
        {
            Assert.Contains(criterion, availability, StringComparison.Ordinal);
            Assert.Contains(criterion, body, StringComparison.Ordinal);
        }
    }

    private static string Flat(string source) => AgentSourceFixture.WithoutWhitespace(source);
}
