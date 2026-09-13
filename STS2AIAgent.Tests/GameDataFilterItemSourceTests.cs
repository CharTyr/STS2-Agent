using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// Behavioural tests for <see cref="GameDataFilter.DeriveRelevantItemIds"/>: the ids
/// <c>get_relevant_game_data</c> looks up when the caller omits them. These call the production
/// code directly (GameDataFilter.cs is compiled into this project); they do not read its source.
/// </summary>
internal static class GameDataFilterItemSourceTests
{
    /// <summary>
    /// On the combat screen the card ids come from <c>combat.hand[]</c> in hand order and the
    /// monster ids from <c>combat.enemies[]</c> in enemy order, not from a run-level list.
    /// </summary>
    public static void CombatHandAndEnemyIdsFollowTheSurfaceOrder()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "combat": {
            "hand": [
              {"card_id": "STRIKE_IRONCLAD"},
              {"card_id": "DEFEND_IRONCLAD"},
              {"card_id": "BASH"}
            ],
            "enemies": [
              {"enemy_id": "FUZZY_WURM_CRAWLER"},
              {"enemy_id": "SHRINKER_BEETLE"}
            ]
          },
          "run": {"deck": [{"card_id": "FALLBACK_CARD"}]}
        }
        """);

        Assert.Equal("STRIKE_IRONCLAD,DEFEND_IRONCLAD,BASH",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("COMBAT", "cards", doc.RootElement)));
        Assert.Equal("FUZZY_WURM_CRAWLER,SHRINKER_BEETLE",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("COMBAT", "monsters", doc.RootElement)));
    }

    /// <summary>
    /// The combat powers source names two paths, so the answer merges the player's powers first and
    /// then each enemy's powers in enemy order; order matters because that is the surface order.
    /// </summary>
    public static void CombatPowersMergePlayerPowersThenEnemyPowers()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "combat": {
            "player": {"powers": [{"power_id": "STRENGTH"}, {"power_id": "DEXTERITY"}]},
            "enemies": [
              {"enemy_id": "E1", "powers": [{"power_id": "VULNERABLE"}]},
              {"enemy_id": "E2", "powers": [{"power_id": "WEAK"}]}
            ]
          }
        }
        """);

        Assert.Equal("STRENGTH,DEXTERITY,VULNERABLE,WEAK",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("COMBAT", "powers", doc.RootElement)));
    }

    /// <summary>
    /// A null potion id in the state is not an error: the collection simply derives no ids, and the
    /// caller gets an empty list rather than a failure.
    /// </summary>
    public static void NullPotionIdYieldsNoIdsInsteadOfAnError()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "run": {"potions": [{"potion_id": null}]}
        }
        """);

        var ids = GameDataFilter.DeriveRelevantItemIds("COMBAT", "potions", doc.RootElement);
        Assert.NotNull(ids);
        Assert.Equal(0, ids.Count);
    }

    /// <summary>
    /// A screen that is about something else declares no card source, so the lookup falls back to
    /// the deck the player already owns instead of answering with nothing.
    /// </summary>
    public static void NonCombatScreenFallsBackToTheDeck()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "MAP",
          "combat": {"hand": [{"card_id": "NOT_IN_HAND_NOW"}]},
          "run": {"deck": [{"card_id": "BASH"}, {"card_id": "STRIKE_IRONCLAD"}]}
        }
        """);

        Assert.Equal("BASH,STRIKE_IRONCLAD",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("MAP", "cards", doc.RootElement)));
    }

    /// <summary>
    /// An unknown collection derives nothing rather than guessing a nearby source.
    /// </summary>
    public static void UnknownCollectionYieldsNoIds()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "combat": {"hand": [{"card_id": "BASH"}]}
        }
        """);

        var ids = GameDataFilter.DeriveRelevantItemIds("COMBAT", "nonsense", doc.RootElement);
        Assert.NotNull(ids);
        Assert.Equal(0, ids.Count);
    }

    /// <summary>
    /// A card that sits in hand twice is looked up once, and it keeps the position of its first
    /// appearance so the derived list stays in surface order.
    /// </summary>
    public static void DuplicateIdsAppearOnceInFirstSeenOrder()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "combat": {"hand": [
            {"card_id": "STRIKE_IRONCLAD"},
            {"card_id": "BASH"},
            {"card_id": "STRIKE_IRONCLAD"}
          ]}
        }
        """);

        Assert.Equal("STRIKE_IRONCLAD,BASH",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("COMBAT", "cards", doc.RootElement)));
    }

    /// <summary>
    /// Scene and collection matching is case-insensitive, so a caller that sends "combat"/"Cards"
    /// reaches the same sources as the canonical spelling.
    /// </summary>
    public static void CollectionAndScreenMatchingIsCaseInsensitive()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "combat": {"hand": [{"card_id": "STRIKE_IRONCLAD"}, {"card_id": "BASH"}]},
          "run": {"deck": [{"card_id": "FALLBACK_CARD"}]}
        }
        """);

        Assert.Equal("STRIKE_IRONCLAD,BASH",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("combat", "Cards", doc.RootElement)));
    }

    /// <summary>
    /// An empty id string is skipped, exactly like the Python mirror: it must not be looked up and
    /// must not enter the dedup set, so it never appears in the derived list.
    /// </summary>
    public static void EmptyStringIdsAreSkipped()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "combat": {"hand": [
            {"card_id": ""},
            {"card_id": "STRIKE_IRONCLAD"},
            {"card_id": ""}
          ]}
        }
        """);

        var ids = GameDataFilter.DeriveRelevantItemIds("COMBAT", "cards", doc.RootElement);
        Assert.Equal(1, ids.Count);
        Assert.Equal("STRIKE_IRONCLAD", ids[0]);
    }

    /// <summary>
    /// Combat declares no relic source, so a relic lookup on that screen falls back to the run
    /// relics the player already owns, in the order the run lists them.
    /// </summary>
    public static void CombatRelicsFallBackToTheRunRelics()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "COMBAT",
          "run": {"relics": [{"relic_id": "BURNING_BLOOD"}, {"relic_id": "RING_OF_THE_SNAKE"}]}
        }
        """);

        Assert.Equal("BURNING_BLOOD,RING_OF_THE_SNAKE",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("COMBAT", "relics", doc.RootElement)));
    }

    /// <summary>
    /// A screen can classify into a scene whose payload is absent on that very screen: FAKE_MERCHANT
    /// reads as shop, but the state carries no shop block there because the merchant room the ids
    /// would come from does not exist. The answer has to fall back to the run-level ids instead of
    /// reporting nothing, or the tool would silently return an empty map on that screen.
    /// </summary>
    public static void SceneSourceWithoutItsPayloadFallsBack()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "FAKE_MERCHANT",
          "shop": null,
          "run": {
            "deck": [{"card_id": "STRIKE_IRONCLAD"}],
            "relics": [{"relic_id": "BURNING_BLOOD"}]
          }
        }
        """);

        Assert.Equal("STRIKE_IRONCLAD",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("FAKE_MERCHANT", "cards", doc.RootElement)));
        Assert.Equal("BURNING_BLOOD",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("FAKE_MERCHANT", "relics", doc.RootElement)));
    }
}
