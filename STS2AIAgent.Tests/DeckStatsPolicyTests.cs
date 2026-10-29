using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

/// <summary>
/// The deck shape a card reward, shop or rest-site decision reads (W5.1): counts by type, the average
/// energy cost, and how much of the starter deck is still there.
/// </summary>
internal static class DeckStatsPolicyTests
{
    /// <summary>One registration for the group, so the shared test catalog grows by a line, not five.</summary>
    public static void RunAll()
    {
        CountsTypesUpgradesAndTheStarterDeck();
        XCostCardsStayOutOfTheCostNumbers();
        EmptyDeckSummarizesToZeros();
        StarterCountsNeedTheCardPrefixAndTheType();
        UnknownTypesStillCount();
    }

    private static DeckStatCard Card(
        string cardId,
        string type,
        int? cost,
        bool upgraded = false,
        bool costsX = false,
        string rarity = "Common") =>
        new(cardId, type, rarity, costsX ? null : cost, costsX, upgraded);

    public static void CountsTypesUpgradesAndTheStarterDeck()
    {
        var stats = DeckStatsPolicy.Summarize(new[]
        {
            Card("StrikeIronclad", "Attack", 1, rarity: "Basic"),
            Card("StrikeIronclad", "Attack", 1, rarity: "Basic"),
            Card("DefendIronclad", "Skill", 1, rarity: "Basic"),
            Card("Bash", "Attack", 2, upgraded: true, rarity: "Basic"),
            Card("Inflame", "Power", 2),
            Card("AscendersBane", "Curse", null, rarity: "Basic"),
            Card("Wound", "Status", null)
        });

        Assert.Equal(7, stats.Count);
        Assert.Equal(3, stats.Attacks);
        Assert.Equal(1, stats.Skills);
        Assert.Equal(1, stats.Powers);
        Assert.Equal(1, stats.Curses);
        Assert.Equal(1, stats.Statuses);
        Assert.Equal(1, stats.Upgraded);
        Assert.Equal(2, stats.StarterStrikes, "two unupgraded StrikeIronclad; Bash is not a Strike");
        Assert.Equal(1, stats.StarterDefends);
        Assert.Equal(1.4, stats.AverageCost, "costs 1, 1, 1, 2, 2 over five costed cards");
        Assert.Equal(0, stats.ZeroCost);
    }

    /// <summary>An X-cost card has no numeric cost: it is in none of the cost numbers rather than a 0.</summary>
    public static void XCostCardsStayOutOfTheCostNumbers()
    {
        var stats = DeckStatsPolicy.Summarize(new[]
        {
            Card("Whirlwind", "Attack", null, costsX: true),
            Card("DefendIronclad", "Skill", 1),
            Card("Prepared", "Skill", 0)
        });

        Assert.Equal(0.5, stats.AverageCost, "the 1 and the real 0 are costed; the X-cost card is not");
        Assert.Equal(1, stats.ZeroCost);
        Assert.Equal(1, stats.Attacks, "the X-cost attack still counts as an attack");
    }

    public static void EmptyDeckSummarizesToZeros()
    {
        var stats = DeckStatsPolicy.Summarize(Array.Empty<DeckStatCard>());
        Assert.Equal(0, stats.Count);
        Assert.Equal(0d, stats.AverageCost);
        Assert.Equal(0, stats.StarterStrikes);
        Assert.Equal(0, stats.StarterDefends);
    }

    /// <summary>
    /// The starter count is a card-id prefix plus the type, not the rarity alone: a Basic curse or a
    /// Basic non-starter attack must not be read as the starter card it is not.
    /// </summary>
    public static void StarterCountsNeedTheCardPrefixAndTheType()
    {
        var stats = DeckStatsPolicy.Summarize(new[]
        {
            Card("StrikeIronclad+", "Attack", 1, upgraded: true, rarity: "Basic"),
            Card("DefendSilent", "Skill", 1, rarity: "Basic"),
            Card("AscendersBane", "Curse", null, rarity: "Basic"),
            Card("StrikeDummy", "Skill", 1, rarity: "Uncommon")
        });

        Assert.Equal(1, stats.StarterStrikes, "the upgraded Strike still counts; a skill named Strike does not");
        Assert.Equal(1, stats.StarterDefends);
        Assert.Equal(1, stats.Upgraded);
    }

    /// <summary>An unrecognised type is not dropped from the count; it just has no bucket.</summary>
    public static void UnknownTypesStillCount()
    {
        var stats = DeckStatsPolicy.Summarize(new[]
        {
            Card("Quest", "Quest", 1),
            Card("StrikeIronclad", "Attack", 1)
        });

        Assert.Equal(2, stats.Count);
        Assert.Equal(1, stats.Attacks);
        Assert.Equal(1.0, stats.AverageCost);
    }
}
