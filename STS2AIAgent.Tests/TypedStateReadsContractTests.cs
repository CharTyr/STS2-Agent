using System.Text.Json;

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

    public static void ShopStateUsesTheLiveOddsAndRemovalCount()
    {
        var payloads = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.Payloads.cs");
        var runPayload = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.DeclarationBody(payloads, "internal sealed class RunPayload"));
        Assert.True(runPayload.Contains("publicfloatcard_rarity_odds_value{get;init;}", StringComparison.Ordinal),
            "The rarity offset must be a non-nullable float so the exact runtime value is emitted.");
        Assert.True(runPayload.Contains("publicintcard_shop_removals_used{get;init;}", StringComparison.Ordinal),
            "The removal count must be a non-nullable integer, including zero.");

        var builder = AgentSourceFixture.WithoutWhitespace(
            AgentSourceFixture.MethodBody(AgentSourceFixture.ReadStateService(), "BuildRunPayload"));
        Assert.True(builder.Contains("card_rarity_odds_value=player.PlayerOdds.CardRarity.CurrentValue", StringComparison.Ordinal),
            "The /state rarity offset must come directly from Player.PlayerOdds.CardRarity.CurrentValue.");
        Assert.True(builder.Contains("card_shop_removals_used=player.ExtraFields.CardShopRemovalsUsed", StringComparison.Ordinal),
            "The /state removal count must come directly from Player.ExtraFields.CardShopRemovalsUsed.");

        var defaultValues = JsonSerializer.Serialize(new { card_rarity_odds_value = -0.05f, card_shop_removals_used = 0 });
        using var defaultJson = JsonDocument.Parse(defaultValues);
        Assert.Equal(-0.05f, defaultJson.RootElement.GetProperty("card_rarity_odds_value").GetSingle());
        Assert.Equal(0, defaultJson.RootElement.GetProperty("card_shop_removals_used").GetInt32());

        const float positiveOffset = 0.12345679f;
        var positiveValues = JsonSerializer.Serialize(new { card_rarity_odds_value = positiveOffset, card_shop_removals_used = 3 });
        using var positiveJson = JsonDocument.Parse(positiveValues);
        Assert.Equal(positiveOffset, positiveJson.RootElement.GetProperty("card_rarity_odds_value").GetSingle());
        Assert.Equal(3, positiveJson.RootElement.GetProperty("card_shop_removals_used").GetInt32());
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
