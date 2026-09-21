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

    /// <summary>
    /// The reward screen offers cards, so the card lookup answers with the offers -- not with the
    /// deck the player already owns, which is what the generic menu scene used to hand back. The
    /// fixture carries both so a path that fell through to the fallback cannot pass by accident.
    /// </summary>
    public static void RewardScreenLooksUpTheOfferedCards()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "REWARD",
          "reward": {
            "card_options": [
              {"index": 0, "card_id": "OFFER_ALPHA"},
              {"index": 1, "card_id": "OFFER_BETA"}
            ],
            "rewards": [{"index": 0, "reward_type": "Potion", "description": "Fire Potion", "claimable": true}]
          },
          "run": {
            "deck": [{"card_id": "OWNED_CARD"}],
            "potions": [{"potion_id": "OWNED_POTION"}]
          }
        }
        """);

        Assert.Equal("OFFER_ALPHA,OFFER_BETA",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("REWARD", "cards", doc.RootElement)));
    }

    /// <summary>
    /// One /state response carries the raw payload and the compact agent_view at once, and the two
    /// name the offered cards differently. The compact list alone has to answer too, so a caller
    /// holding only that view still gets the offers.
    /// </summary>
    public static void RewardCompactViewAnswersWithItsOwnOfferedCards()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "REWARD",
          "reward": null,
          "agent_view": {
            "reward": {"cards": [{"i": 0, "card_id": "OFFER_ALPHA"}, {"i": 1, "card_id": "OFFER_BETA"}]}
          },
          "run": {"deck": [{"card_id": "OWNED_CARD"}]}
        }
        """);

        Assert.Equal("OFFER_ALPHA,OFFER_BETA",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("REWARD", "cards", doc.RootElement)));
    }

    /// <summary>
    /// The policy is collection-aware: a reward screen offers cards, so a relic question on that
    /// screen is not answered with the offered card ids. Nothing on the screen names a relic, so the
    /// run-level relics the player already owns answer instead -- and no card id leaks into it.
    /// </summary>
    public static void RewardRelicLookupFallsBackWithoutCardIds()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "REWARD",
          "reward": {"card_options": [{"index": 0, "card_id": "OFFER_ALPHA"}]},
          "run": {
            "deck": [{"card_id": "OWNED_CARD"}],
            "relics": [{"relic_id": "BURNING_BLOOD"}]
          }
        }
        """);

        Assert.Equal("BURNING_BLOOD",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("REWARD", "relics", doc.RootElement)));
    }

    /// <summary>
    /// A reward screen's potion row carries a description and no stable id, so the potion lookup
    /// must not invent one: it falls back to the potions the player already owns.
    /// </summary>
    public static void RewardPotionLookupFallsBackInsteadOfInventingAnId()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "REWARD",
          "reward": {"rewards": [{"index": 0, "reward_type": "Potion", "description": "Fire Potion", "claimable": true}]},
          "run": {"potions": [{"potion_id": "OWNED_POTION"}]}
        }
        """);

        Assert.Equal("OWNED_POTION",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("REWARD", "potions", doc.RootElement)));
    }

    /// <summary>
    /// A card-selection grid is about the cards it shows: its own list, once per card, in the order
    /// the grid presents them -- and never the deck the player is choosing from.
    /// </summary>
    public static void CardSelectionGridOffersItsOwnCardsOnceInOrder()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "CARD_SELECTION",
          "selection": {
            "cards": [
              {"index": 0, "card_id": "SELECT_ALPHA"},
              {"index": 1, "card_id": "SELECT_ALPHA"},
              {"index": 2, "card_id": "SELECT_BETA"}
            ]
          },
          "run": {"deck": [{"card_id": "OWNED_CARD"}]}
        }
        """);

        Assert.Equal("SELECT_ALPHA,SELECT_BETA",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("CARD_SELECTION", "cards", doc.RootElement)));
    }

    /// <summary>
    /// The mirror of the reward case: a card-selection screen never answers a relic question with
    /// the offered card ids. The collection column of SceneItemSources is what holds this.
    /// </summary>
    public static void CardSelectionRelicLookupNeverAnswersWithOfferedCards()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "CARD_SELECTION",
          "selection": {"cards": [{"index": 0, "card_id": "SELECT_ALPHA"}]},
          "run": {
            "deck": [{"card_id": "OWNED_CARD"}],
            "relics": [{"relic_id": "BURNING_BLOOD"}, {"relic_id": "RING_OF_THE_SNAKE"}]
          }
        }
        """);

        Assert.Equal("BURNING_BLOOD,RING_OF_THE_SNAKE",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("CARD_SELECTION", "relics", doc.RootElement)));
    }

    /// <summary>
    /// A chest offers relics: the raw payload's relic_options name them, and the run relics the
    /// player owns are not the answer while the offer is on screen.
    /// </summary>
    public static void ChestOffersItsRelicOptions()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "CHEST",
          "chest": {
            "is_opened": true,
            "relic_options": [
              {"index": 0, "relic_id": "OFFER_RELIC_ALPHA"},
              {"index": 1, "relic_id": "OFFER_RELIC_BETA"}
            ]
          },
          "run": {"relics": [{"relic_id": "BURNING_BLOOD"}]}
        }
        """);

        Assert.Equal("OFFER_RELIC_ALPHA,OFFER_RELIC_BETA",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("CHEST", "relics", doc.RootElement)));
    }

    /// <summary>
    /// The same chest read through the compact agent_view, which renames relic_options to relics.
    /// </summary>
    public static void ChestCompactViewAnswersWithItsOwnRelicOffers()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "CHEST",
          "chest": null,
          "agent_view": {"chest": {"opened": true, "relics": [{"i": 0, "relic_id": "OFFER_RELIC_ALPHA"}]}},
          "run": {"relics": [{"relic_id": "BURNING_BLOOD"}]}
        }
        """);

        Assert.Equal("OFFER_RELIC_ALPHA",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("CHEST", "relics", doc.RootElement)));
    }

    /// <summary>
    /// A chest is about relics, so a card question there falls back to the deck rather than
    /// answering with relic ids.
    /// </summary>
    public static void ChestCardLookupFallsBackToTheDeck()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "CHEST",
          "chest": {"relic_options": [{"index": 0, "relic_id": "OFFER_RELIC_ALPHA"}]},
          "run": {"deck": [{"card_id": "OWNED_CARD"}]}
        }
        """);

        Assert.Equal("OWNED_CARD",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("CHEST", "cards", doc.RootElement)));
    }

    /// <summary>
    /// A bundle screen offers several bundles of cards; every card of every bundle is an offer, in
    /// bundle order, and the owned deck is not.
    /// </summary>
    public static void BundleScreenOffersEveryCardsInItsBundles()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "BUNDLE_SELECTION",
          "bundles": [
            {"index": 0, "cards": [{"index": 0, "card_id": "BUNDLE_A_ONE"}, {"index": 1, "card_id": "BUNDLE_A_TWO"}]},
            {"index": 1, "cards": [{"index": 0, "card_id": "BUNDLE_B_ONE"}]}
          ],
          "run": {"deck": [{"card_id": "OWNED_CARD"}]}
        }
        """);

        Assert.Equal("BUNDLE_A_ONE,BUNDLE_A_TWO,BUNDLE_B_ONE",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("BUNDLE_SELECTION", "cards", doc.RootElement)));
    }

    /// <summary>
    /// The shop was the one screen that already worked, and all three of its stock lists answer from
    /// the offer ids the shelf carries rather than from the run.
    /// </summary>
    public static void ShopOffersStockIdsForCardsRelicsAndPotions()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "SHOP",
          "shop": {
            "cards": [{"index": 0, "card_id": "STOCK_CARD"}],
            "relics": [{"index": 0, "relic_id": "STOCK_RELIC"}],
            "potions": [{"index": 0, "potion_id": "STOCK_POTION"}]
          },
          "run": {
            "deck": [{"card_id": "OWNED_CARD"}],
            "relics": [{"relic_id": "BURNING_BLOOD"}],
            "potions": [{"potion_id": "OWNED_POTION"}]
          }
        }
        """);

        Assert.Equal("STOCK_CARD",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("SHOP", "cards", doc.RootElement)));
        Assert.Equal("STOCK_RELIC",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("SHOP", "relics", doc.RootElement)));
        Assert.Equal("STOCK_POTION",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("SHOP", "potions", doc.RootElement)));
    }

    /// <summary>
    /// The collection column is matched case-insensitively on both sides, so the spelling a caller
    /// happens to use does not decide whether the scene's ids are found.
    /// </summary>
    public static void OfferScreensMatchTheCollectionNameCaseInsensitively()
    {
        using var doc = JsonDocument.Parse("""
        {
          "screen": "REWARD",
          "reward": {"card_options": [{"index": 0, "card_id": "OFFER_ALPHA"}]},
          "run": {"deck": [{"card_id": "OWNED_CARD"}]}
        }
        """);

        Assert.Equal("OFFER_ALPHA",
            string.Join(",", GameDataFilter.DeriveRelevantItemIds("reward", "Cards", doc.RootElement)));
    }
}
