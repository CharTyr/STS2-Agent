namespace STS2AIAgent.Tests;

/// <summary>
/// State fields that used to be read by guessed member names, and now read the members the game has.
/// </summary>
/// <remarks>
/// Checked against the installed sts2.dll on 2026-09-18. Two of the guesses were not harmless:
/// <c>RelicModel</c> has no <c>Amount</c>, so every relic's <c>stack</c> was null; and of seven
/// candidate names for a card's modifiers only <c>Keywords</c> existed, holding enum values the
/// token extractor could not turn into text -- so every card reported no <c>mods</c>, and an
/// enchantment (<c>CardModel.Enchantment</c>, singular) was never among the candidates at all.
/// Reintroducing a by-name read is caught by <c>ReflectedMembers.*</c>; these pin what the typed
/// reads mean.
/// </remarks>
internal static class TypedStateReadsContractTests
{
    public static void RelicStackIsTheCounterThePlayerSees()
    {
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadStateService(), "BuildRunRelicPayload"));
        Assert.True(
            body.Contains("stack=SafeReadBool(()=>relic.ShowCounter)?SafeReadNullableInt(()=>relic.DisplayAmount):null", StringComparison.Ordinal),
            "run.relics[].stack must be the counter the relic displays (DisplayAmount, when ShowCounter), "
            + "and null for a relic that shows none.");
    }

    public static void CardModsComeFromKeywordsAndTheEnchantment()
    {
        var body = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadStateService(), "GetCardModifierTags"));
        Assert.True(
            body.Contains("foreach(varkeywordincard.Keywords)", StringComparison.Ordinal) &&
            body.Contains("values.Add(keyword.ToString());", StringComparison.Ordinal),
            "A card's mods must include its CardKeyword values by name, the spelling the glossary aliases match.");
        Assert.True(
            body.Contains("if(card.Enchantmentis{}enchantment)", StringComparison.Ordinal) &&
            body.Contains("values.Add(\"Enchantment\");", StringComparison.Ordinal),
            "An enchanted card's mods must say so, in the spelling the 附魔 glossary alias matches.");
        Assert.False(
            body.Contains("TryGetMemberValue", StringComparison.Ordinal),
            "Card mods must not go back to guessing member names.");
    }

    public static void CombatPilesAreReadByType()
    {
        var state = AgentSourceFixture.ReadStateService();
        foreach (var pile in new[] { "DrawPile", "DiscardPile", "ExhaustPile" })
        {
            Assert.True(
                state.Contains($"PileCards(playerCombatState?.{pile})", StringComparison.Ordinal) &&
                state.Contains($"PileCards(combatPlayer?.{pile})", StringComparison.Ordinal),
                $"agent_view piles must read PlayerCombatState.{pile} directly.");
        }

        Assert.False(
            state.Contains("\"DrawDeck\"", StringComparison.Ordinal),
            "DrawDeck is not a PlayerCombatState member; it was a guess that never resolved.");
    }
}
