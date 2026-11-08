namespace STS2AIAgent.Game;

/// <summary>One deck card as the statistics see it; every field is already a plain number or name.</summary>
/// <remarks>
/// <see cref="Type"/> is the game's <c>CardType</c> name (<c>Attack</c> / <c>Skill</c> / <c>Power</c> /
/// <c>Curse</c> / <c>Status</c>), which both the raw deck payload and <c>CardModel.Type</c> spell the
/// same way. <see cref="EnergyCost"/> is null for an X-cost card, and <see cref="CostsX"/> marks it.
/// </remarks>
internal readonly record struct DeckStatCard(
    string CardId,
    string Type,
    string Rarity,
    int? EnergyCost,
    bool CostsX,
    bool Upgraded);

/// <summary>The compact deck shape a card reward or shop decision actually reads.</summary>
internal sealed record DeckStats(
    int Count,
    int Attacks,
    int Skills,
    int Powers,
    int Curses,
    int Statuses,
    int Upgraded,
    double AverageCost,
    int ZeroCost,
    int StarterStrikes,
    int StarterDefends);

/// <summary>
/// The deck shape that a card reward, a shop and a rest-site upgrade are chosen against: how many
/// attacks and skills are left, how expensive the deck is, and how much of it is still the starter
/// deck the player has not replaced.
/// </summary>
/// <remarks>
/// Pure counting over names the game already exposes, so it is tested offline (the game-side half is
/// the projection in <c>GameStateService.AgentView.cs</c>, which only maps models to
/// <see cref="DeckStatCard"/>). The numbers are deliberately literal: no heuristic score, no advice.
///
/// Rules worth naming, because a model reads these numbers as facts:
/// <list type="bullet">
/// <item>an X-cost card has no numeric cost, so it is excluded from <see cref="DeckStats.AverageCost"/>
/// and from <see cref="DeckStats.ZeroCost"/> rather than counted as 0;</item>
/// <item><see cref="DeckStats.AverageCost"/> is rounded to two decimals and is 0 when the deck has no
/// card with a numeric cost;</item>
/// <item><see cref="DeckStats.StarterStrikes"/> / <see cref="DeckStats.StarterDefends"/> count the
/// starter Basic cards by the game's card-id prefix (<c>StrikeIronclad</c>, <c>DefendSilent</c>, ...)
/// plus the matching type, so Ascender's Bane (a Basic curse) is not mistaken for one;</item>
/// <item>only the first five types are broken out; an unrecognised type still counts toward
/// <see cref="DeckStats.Count"/>.</item>
/// </list>
/// </remarks>
internal static class DeckStatsPolicy
{
    public static DeckStats Summarize(IEnumerable<DeckStatCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var count = 0;
        var attacks = 0;
        var skills = 0;
        var powers = 0;
        var curses = 0;
        var statuses = 0;
        var upgraded = 0;
        var zeroCost = 0;
        var starterStrikes = 0;
        var starterDefends = 0;
        var costSum = 0;
        var costed = 0;

        foreach (var card in cards)
        {
            count++;
            if (card.Upgraded)
            {
                upgraded++;
            }

            if (IsType(card.Type, "Attack"))
            {
                attacks++;
                if (IsStarterCard(card, "Strike"))
                {
                    starterStrikes++;
                }
            }
            else if (IsType(card.Type, "Skill"))
            {
                skills++;
                if (IsStarterCard(card, "Defend"))
                {
                    starterDefends++;
                }
            }
            else if (IsType(card.Type, "Power"))
            {
                powers++;
            }
            else if (IsType(card.Type, "Curse"))
            {
                curses++;
            }
            else if (IsType(card.Type, "Status"))
            {
                statuses++;
            }

            if (card.CostsX || card.EnergyCost is not { } cost)
            {
                continue;
            }

            costSum += Math.Max(0, cost);
            costed++;
            if (cost <= 0)
            {
                zeroCost++;
            }
        }

        var average = costed == 0 ? 0d : Math.Round((double)costSum / costed, 2);
        return new DeckStats(
            count,
            attacks,
            skills,
            powers,
            curses,
            statuses,
            upgraded,
            average,
            zeroCost,
            starterStrikes,
            starterDefends);
    }

    private static bool IsType(string type, string expected)
    {
        return string.Equals(type, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A starter Basic card of that kind. The id prefix is the reliable half -- the starter decks are
    /// <c>StrikeIronclad</c> / <c>DefendIronclad</c> and so on per character -- and the type keeps a
    /// non-starter Basic card of another type out of the count.
    /// </summary>
    private static bool IsStarterCard(DeckStatCard card, string prefix)
    {
        var id = (card.CardId ?? string.Empty).TrimEnd('+');
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
