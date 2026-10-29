using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace STS2AIAgent.Game;

/// <summary>
/// The compact view's strategy projection: the deck shape a card choice is made against, and the
/// route advice behind each selectable map node.
/// </summary>
/// <remarks>
/// Split out of <c>GameStateService.AgentView.cs</c> on 2026-10-28 for the same reason the combat
/// arithmetic got its own partial: the rules live in <see cref="DeckStatsPolicy"/> and
/// <see cref="MapRoutePolicy"/>, which are Godot-free and tested offline, while this file only maps
/// the game's models and the raw payloads onto their inputs. Nothing here reads the game directly --
/// it projects payloads the raw builders already produced.
///
/// Both fields are conditional on purpose:
/// <list type="bullet">
/// <item><c>run.deck_stats</c> is emitted only where a card is being chosen (reward, shop, rest,
/// card selection, bundles). Carrying it in every frame would spend context on numbers no other
/// screen decides with.</item>
/// <item><c>map.route_advice</c> is emitted only on the map with at least two selectable nodes: with
/// one door there is nothing to advise, and off the map there is no graph.</item>
/// </list>
/// A frame that cannot supply the inputs (no run payload, no deck, no graph) gets null, never a
/// guessed number.
/// </remarks>
internal static partial class GameStateService
{
    /// <summary>Screens where a card is being chosen, so the deck's shape is worth carrying.</summary>
    private static readonly HashSet<string> DeckShapeScreens = new(StringComparer.Ordinal)
    {
        "REWARD",
        "SHOP",
        "FAKE_MERCHANT",
        "REST",
        "CARD_SELECTION",
        "BUNDLE_SELECTION"
    };

    /// <summary>
    /// The deck's shape for the current screen, or null off a card-choice screen. The live deck
    /// wins when it can be read; the raw payload's deck is the fallback the compact deck itself uses.
    /// </summary>
    private static object? BuildAgentDeckStats(string screen, CardModel[] liveDeck, DeckCardPayload[] fallbackDeck)
    {
        if (!DeckShapeScreens.Contains(screen))
        {
            return null;
        }

        var stats = DeckStatsPolicy.Summarize(DeckStatCards(liveDeck, fallbackDeck));
        if (stats.Count == 0)
        {
            return null;
        }

        return new
        {
            count = stats.Count,
            attacks = stats.Attacks,
            skills = stats.Skills,
            powers = stats.Powers,
            curses = stats.Curses,
            statuses = stats.Statuses,
            upgraded = stats.Upgraded,
            avg_cost = stats.AverageCost,
            zero_cost = stats.ZeroCost,
            starter_strikes = stats.StarterStrikes,
            starter_defends = stats.StarterDefends
        };
    }

    private static IEnumerable<DeckStatCard> DeckStatCards(CardModel[] liveDeck, DeckCardPayload[] fallbackDeck)
    {
        if (liveDeck.Length > 0)
        {
            foreach (var card in liveDeck)
            {
                var costsX = card.EnergyCost.CostsX;
                yield return new DeckStatCard(
                    card.Id.Entry,
                    card.Type.ToString(),
                    card.Rarity.ToString(),
                    costsX ? null : card.EnergyCost.GetWithModifiers(CostModifiers.All),
                    costsX,
                    card.IsUpgraded);
            }

            yield break;
        }

        foreach (var card in fallbackDeck)
        {
            yield return new DeckStatCard(
                card.card_id,
                card.card_type,
                card.rarity,
                card.costs_x ? null : card.energy_cost,
                card.costs_x,
                card.upgraded);
        }
    }

    /// <summary>
    /// <c>map.route_advice</c>: what lies behind each selectable node, or null when the map offers no
    /// choice. <c>elites</c> and friends are the [least, most] counts on the paths behind that node.
    /// </summary>
    private static object? BuildAgentRouteAdvice(MapPayload map, RunPayload? run)
    {
        if (map.available_nodes.Length < 2)
        {
            return null;
        }

        var nodes = map.nodes
            .Select(node => new MapRouteNode(
                $"{node.row},{node.col}",
                node.node_type,
                node.visited,
                node.children.Select(child => $"{child.row},{child.col}").ToArray()))
            .ToArray();
        var starts = map.available_nodes
            .Select(node => new MapRouteStart(node.index, $"{node.row},{node.col}"))
            .ToArray();
        var advice = MapRoutePolicy.Advise(new MapRouteInput(
            nodes,
            starts,
            run?.current_hp ?? 0,
            run?.max_hp ?? 0,
            run?.gold ?? 0,
            int.TryParse(run?.act_id, out var actIndex) ? actIndex + 1 : 1));
        if (advice == null)
        {
            return null;
        }

        return new
        {
            options = advice.Options.Select(option => new
            {
                i = option.Index,
                elites = new[] { option.Elites.Min, option.Elites.Max },
                rests = new[] { option.Rests.Min, option.Rests.Max },
                shops = new[] { option.Shops.Min, option.Shops.Max },
                unknowns = new[] { option.Unknowns.Min, option.Unknowns.Max },
                combats = new[] { option.Combats.Min, option.Combats.Max },
                treasures = new[] { option.Treasures.Min, option.Treasures.Max },
                score = option.Score,
                why = option.Why
            }).ToArray(),
            recommended_i = advice.Recommended,
            note = advice.Note
        };
    }
}
