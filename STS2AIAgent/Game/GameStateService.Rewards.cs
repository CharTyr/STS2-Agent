using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Game;

internal static partial class GameStateService
{

    private static RewardPayload? BuildRewardPayload(IScreenContext? currentScreen)
    {
        if (currentScreen is NRewardsScreen)
        {
            var rewardButtons = GetRewardButtons(currentScreen);
            var proceedButton = GetRewardProceedButton(currentScreen);

            return new RewardPayload
            {
                pending_card_choice = false,
                can_proceed = proceedButton?.IsEnabled ?? false,
                rewards = rewardButtons.Select((button, index) => BuildRewardOptionPayload(button, index)).ToArray(),
                card_options = Array.Empty<RewardCardOptionPayload>()
            };
        }

        if (currentScreen is NCardRewardSelectionScreen)
        {
            var cardOptions = GetCardRewardOptions(currentScreen);
            var alternatives = GetCardRewardAlternativeButtons(currentScreen);

            return new RewardPayload
            {
                pending_card_choice = true,
                can_proceed = false,
                rewards = Array.Empty<RewardOptionPayload>(),
                card_options = cardOptions.Select((holder, index) => BuildRewardCardOptionPayload(holder, index)).ToArray(),
                alternatives = alternatives.Select((button, index) => BuildRewardAlternativePayload(button, index)).ToArray()
            };
        }

        return null;
    }

    private static BundlePayload[]? BuildBundlePayload(IScreenContext? currentScreen)
    {
        if (currentScreen is not NChooseABundleSelectionScreen bundleScreen)
        {
            return null;
        }

        var bundleNodes = GetBundleOptions(currentScreen);
        if (bundleNodes.Count == 0)
        {
            return null;
        }

        return bundleNodes.Select((bundleNode, bundleIndex) =>
        {
            // NCardBundle contains NCard children (not NCardHolder).
            // NCard exposes CardModel via the .Model property.
            var cards = FindDescendants<Node>((Node)bundleNode)
                .Where(n => GodotObject.IsInstanceValid(n) && n.GetType().Name == "NCard")
                .Select(n => n.GetType().GetProperty("Model")?.GetValue(n) as CardModel)
                .Where(cm => cm != null)
                .Select((card, cardIndex) => BuildBundleCardPayload(card!, cardIndex))
                .ToArray();

            return new BundlePayload
            {
                index = bundleIndex,
                cards = cards
            };
        }).ToArray();
    }

    private static RewardOptionPayload BuildRewardOptionPayload(NRewardButton button, int index)
    {
        var reward = button.Reward;

        return new RewardOptionPayload
        {
            index = index,
            reward_type = GetRewardTypeName(reward),
            description = reward?.Description.GetFormattedText() ?? string.Empty,
            claimable = button.IsEnabled
        };
    }

    private static RewardCardOptionPayload BuildRewardCardOptionPayload(NCardHolder holder, int index)
    {
        var card = holder.CardModel;
        return BuildBundleCardPayload(card, index);
    }

    private static RewardCardOptionPayload BuildBundleCardPayload(CardModel? card, int index)
    {
        var resolvedRulesText = GetResolvedCardRulesText(card);
        var dynamicValues = BuildCardDynamicValuePayloads(card);

        return new RewardCardOptionPayload
        {
            index = index,
            card_id = card?.Id.Entry ?? string.Empty,
            name = card?.Title ?? string.Empty,
            upgraded = card?.IsUpgraded ?? false,
            card_type = card?.Type.ToString() ?? string.Empty,
            rarity = card?.Rarity.ToString() ?? string.Empty,
            energy_cost = card?.EnergyCost.GetWithModifiers(CostModifiers.All) ?? 0,
            rules_text = GetCardRulesText(card),
            resolved_rules_text = resolvedRulesText,
            dynamic_values = dynamicValues
        };
    }

    private static RewardAlternativePayload BuildRewardAlternativePayload(NCardRewardAlternativeButton button, int index)
    {
        return new RewardAlternativePayload
        {
            index = index,
            label = button.GetNodeOrNull<MegaLabel>("Label")?.Text ?? button.Name
        };
    }

    public static IReadOnlyList<NRewardButton> GetRewardButtons(IScreenContext? currentScreen)
    {
        if (currentScreen is not NRewardsScreen rewardScreen)
        {
            return Array.Empty<NRewardButton>();
        }

        return FindDescendants<NRewardButton>(rewardScreen)
            .Where(node => GodotObject.IsInstanceValid(node))
            .OrderBy(node => node.GlobalPosition.Y)
            .ThenBy(node => node.GlobalPosition.X)
            .ToArray();
    }

    public static NProceedButton? GetRewardProceedButton(IScreenContext? currentScreen)
    {
        if (currentScreen is not NRewardsScreen rewardScreen)
        {
            return null;
        }

        return FindDescendants<NProceedButton>(rewardScreen)
            .FirstOrDefault(node => GodotObject.IsInstanceValid(node));
    }

    public static IReadOnlyList<NCardHolder> GetCardRewardOptions(IScreenContext? currentScreen)
    {
        if (currentScreen is not NCardRewardSelectionScreen cardRewardScreen)
        {
            return Array.Empty<NCardHolder>();
        }

        return FindDescendants<NCardHolder>(cardRewardScreen)
            .Where(node => GodotObject.IsInstanceValid(node) && node.CardModel != null)
            .OrderBy(node => node.GlobalPosition.Y)
            .ThenBy(node => node.GlobalPosition.X)
            .ToArray();
    }

    public static IReadOnlyList<NCardRewardAlternativeButton> GetCardRewardAlternativeButtons(IScreenContext? currentScreen)
    {
        if (currentScreen is not NCardRewardSelectionScreen cardRewardScreen)
        {
            return Array.Empty<NCardRewardAlternativeButton>();
        }

        return FindDescendants<NCardRewardAlternativeButton>(cardRewardScreen)
            .Where(node => GodotObject.IsInstanceValid(node) && node.IsVisibleInTree())
            .OrderBy(node => node.GlobalPosition.Y)
            .ThenBy(node => node.GlobalPosition.X)
            .ToArray();
    }

    /// <summary>
    /// Identity of the reward set a reward-related screen belongs to. Returns 0 when it
    /// cannot be resolved, which callers treat as "no scope": an unresolved owner never
    /// honors a recorded skip, so a missing identity re-shows the reward instead of
    /// silently dropping it.
    /// </summary>
    public static ulong GetRewardSetId(IScreenContext? currentScreen)
    {
        if (currentScreen is NRewardsScreen rewardsScreen)
        {
            return GodotObject.IsInstanceValid(rewardsScreen) ? rewardsScreen.GetInstanceId() : 0;
        }

        if (currentScreen is NCardRewardSelectionScreen cardRewardScreen)
        {
            if (!GodotObject.IsInstanceValid(cardRewardScreen))
            {
                return 0;
            }

            // The selection overlay is pushed onto the shared overlay stack while the owning
            // rewards screen stays there as a live sibling, so the owner is found under the
            // selection screen's parent. Any other shape resolves to 0 and is not honored.
            var parent = cardRewardScreen.GetParent();
            if (parent == null)
            {
                return 0;
            }

            var owner = FindDescendants<NRewardsScreen>(parent)
                .FirstOrDefault(screen => GodotObject.IsInstanceValid(screen));
            return owner != null ? owner.GetInstanceId() : 0;
        }

        return 0;
    }

    private static string GetRewardTypeName(Reward? reward)
    {
        return reward switch
        {
            CardReward => "Card",
            GoldReward => "Gold",
            PotionReward => "Potion",
            RelicReward => "Relic",
            CardRemovalReward => "RemoveCard",
            SpecialCardReward => "SpecialCard",
            LinkedRewardSet => "LinkedRewardSet",
            null => "Unknown",
            _ => reward.GetType().Name
        };
    }

    private static bool IsProceedButtonUsable(NProceedButton? button)
    {
        return button != null &&
            GodotObject.IsInstanceValid(button) &&
            button.IsEnabled &&
            button.IsVisibleInTree();
    }

    public static IReadOnlyList<NButton> GetBundleConfirmButtons(IScreenContext? currentScreen)
    {
        if (currentScreen is not NChooseABundleSelectionScreen bundleScreen)
        {
            return Array.Empty<NButton>();
        }

        // Look specifically for NConfirmButton or button named "Confirm"
        return FindDescendants<NButton>((Node)bundleScreen)
            .Where(b => GodotObject.IsInstanceValid(b) && b.IsVisibleInTree() && b.IsEnabled
                && (b.GetType().Name == "NConfirmButton" || b.Name == "Confirm"))
            .ToArray();
    }

    public static IReadOnlyList<Control> GetBundleOptions(IScreenContext? currentScreen)
    {
        if (currentScreen is not NChooseABundleSelectionScreen bundleScreen)
        {
            return Array.Empty<Control>();
        }

        // NCardBundle nodes represent the selectable card packs
        return FindDescendants<Control>((Node)bundleScreen)
            .Where(n => GodotObject.IsInstanceValid(n) && n.IsVisibleInTree() && n.GetType().Name == "NCardBundle")
            .ToArray();
    }

    /// <summary>
    /// Screens that draw the same visible card grid and are left with their own BackButton. They
    /// share <c>close_cards_view</c>, so the availability probe, the back-button lookup, and the
    /// executor's settled check all agree on this one set.
    /// </summary>
    public static NTreasureRoomRelicCollection? GetTreasureRelicCollection(IScreenContext? currentScreen)
    {
        if (currentScreen is NTreasureRoomRelicCollection relicCollection)
        {
            return relicCollection;
        }

        if (currentScreen is NTreasureRoom treasureRoom)
        {
            var nestedCollection = treasureRoom.GetNodeOrNull<NTreasureRoomRelicCollection>("%RelicCollection");
            if (nestedCollection != null &&
                GodotObject.IsInstanceValid(nestedCollection) &&
                nestedCollection.Visible)
            {
                return nestedCollection;
            }
        }

        return null;
    }

    private static ChestPayload? BuildChestPayload(IScreenContext? currentScreen)
    {
        var relicCollection = GetTreasureRelicCollection(currentScreen);
        if (relicCollection != null)
        {
            var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
            var hasRelicBeenClaimed = GetProceedButton(currentScreen) != null;
            return new ChestPayload
            {
                is_opened = true,
                has_relic_been_claimed = hasRelicBeenClaimed,
                relic_options = BuildTreasureRelicOptions(relics)
            };
        }

        if (currentScreen is NTreasureRoom treasureRoom)
        {
            var chestButton = treasureRoom.GetNodeOrNull<NButton>("%Chest");
            var isOpened = chestButton == null || !GodotObject.IsInstanceValid(chestButton) || !chestButton.IsEnabled;
            var hasRelicBeenClaimed = GetProceedButton(currentScreen) != null;

            return new ChestPayload
            {
                is_opened = isOpened,
                has_relic_been_claimed = hasRelicBeenClaimed,
                relic_options = Array.Empty<ChestRelicOptionPayload>()
            };
        }

        return null;
    }

    private static ChestRelicOptionPayload[] BuildTreasureRelicOptions(IReadOnlyList<RelicModel>? relics)
    {
        if (relics == null || relics.Count == 0)
        {
            return Array.Empty<ChestRelicOptionPayload>();
        }

        return relics.Select((relic, index) => new ChestRelicOptionPayload
        {
            index = index,
            relic_id = relic.Id.Entry,
            name = relic.Title.GetFormattedText(),
            rarity = relic.Rarity.ToString()
        }).ToArray();
    }

    public static IReadOnlyList<NCardHolder> GetDeckSelectionOptions(IScreenContext? currentScreen)
    {
        if (currentScreen is NCardsViewScreen)
        {
            return Array.Empty<NCardHolder>();
        }

        if (currentScreen is NCardGridSelectionScreen cardSelectScreen)
        {
            return GetVisibleGridCardHolders(cardSelectScreen)
                .Cast<NCardHolder>()
                .ToArray();
        }

        if (currentScreen is NChooseACardSelectionScreen chooseCardScreen)
        {
            return GetVisibleGridCardHolders(chooseCardScreen)
                .Cast<NCardHolder>()
                .ToArray();
        }

        if (TryGetCombatHandSelection(currentScreen, out var hand))
        {
            return hand!.ActiveHolders
                .Where(node => GodotObject.IsInstanceValid(node) && node.Visible && node.CardModel != null)
                .OrderBy(node => node.GetIndex())
                .Cast<NCardHolder>()
                .ToArray();
        }

        // No generic "any visible grid holder in the screen subtree" fallback here on purpose.
        // ExecuteSelectDeckCardAsync only accepts the three shapes above (native card-grid metadata,
        // the choose-a-card screen, or combat-hand metadata) and rejects everything else with 409.
        // A subtree scan would advertise select_deck_card on viewer screens whose holders never react
        // (NCardRewardSelectionScreen, NCardPileScreen, NCardLibrary) and on /state.selection would
        // synthesize a 1/1 deck_card_select, so availability must stay inside the executable set.
        return Array.Empty<NCardHolder>();
    }

    public static bool TryGetCardGridSelectionMetadata(
        IScreenContext? currentScreen,
        out CardGridSelectionMetadata metadata)
    {
        metadata = default;
        // Every concrete card-grid screen declares its own private _prefs/_selectedCards: deck, simple,
        // upgrade, transform and enchant alike. Guarding on the base type keeps the probe on one path,
        // and the field lookups below stay the real gate - a future subclass without those fields
        // still fails safely instead of needing a new name in a list here.
        if (currentScreen is not NCardGridSelectionScreen ||
            ReflectionMemberAccessor.TryGetValue(currentScreen, "_prefs") is not CardSelectorPrefs prefs ||
            ReflectionMemberAccessor.TryGetValue(currentScreen, "_selectedCards") is not IEnumerable selectedCards)
        {
            return false;
        }

        var selectedCount = 0;
        foreach (var _ in selectedCards)
        {
            selectedCount++;
        }

        metadata = new CardGridSelectionMetadata(
            prefs.MinSelect,
            prefs.MaxSelect,
            selectedCount,
            prefs.RequireManualConfirmation,
            selectedCount >= prefs.MinSelect && selectedCount <= prefs.MaxSelect);
        return true;
    }

    public static string? GetDeckSelectionPrompt(IScreenContext? currentScreen)
    {
        if (currentScreen is NCardsViewScreen)
        {
            return null;
        }

        if (currentScreen is NCardGridSelectionScreen cardSelectScreen)
        {
            return cardSelectScreen.GetNodeOrNull<MegaRichTextLabel>("%BottomLabel")?.Text;
        }

        if (currentScreen is NChooseACardSelectionScreen chooseCardScreen)
        {
            return SafeReadString(() => chooseCardScreen.GetNodeOrNull<NCommonBanner>("Banner")?.label.Text);
        }

        if (TryGetCombatHandSelection(currentScreen, out var hand))
        {
            return SafeReadString(() => hand!.GetNodeOrNull<MegaRichTextLabel>("%SelectionHeader")?.Text);
        }

        if (currentScreen is Node rootNode)
        {
            return SafeReadString(() =>
                rootNode.GetNodeOrNull<MegaRichTextLabel>("%BottomLabel")?.Text ??
                FindDescendants<MegaRichTextLabel>(rootNode)
                    .FirstOrDefault(label => label.IsVisibleInTree() && !string.IsNullOrWhiteSpace(label.Text))?.Text);
        }

        return null;
    }

    private static SelectionPayload? BuildSelectionPayload(IScreenContext? currentScreen)
    {
        var cards = GetDeckSelectionOptions(currentScreen);
        if (cards.Count == 0)
        {
            return null;
        }

        var hasCombatHandSelection = TryGetCombatHandSelectionMetadata(
            currentScreen, out _, out var combatHandSelection);
        var hasCardGridSelection = TryGetCardGridSelectionMetadata(
            currentScreen, out var cardGridSelection);

        return new SelectionPayload
        {
            kind = currentScreen switch
            {
                NDeckUpgradeSelectScreen => "deck_upgrade_select",
                NDeckTransformSelectScreen => "deck_transform_select",
                NDeckEnchantSelectScreen => "deck_enchant_select",
                NChooseACardSelectionScreen => "choose_card_select",
                _ when TryGetCombatHandSelection(currentScreen, out var hand) => hand!.CurrentMode == NPlayerHand.Mode.UpgradeSelect
                    ? "combat_hand_upgrade_select"
                    : "combat_hand_select",
                _ => "deck_card_select"
            },
            prompt = GetDeckSelectionPrompt(currentScreen) ?? string.Empty,
            min_select = hasCombatHandSelection
                ? combatHandSelection.MinSelect
                : hasCardGridSelection ? cardGridSelection.MinSelect : 1,
            max_select = hasCombatHandSelection
                ? combatHandSelection.MaxSelect
                : hasCardGridSelection ? cardGridSelection.MaxSelect : 1,
            selected_count = hasCombatHandSelection
                ? combatHandSelection.SelectedCount
                : hasCardGridSelection ? cardGridSelection.SelectedCount : 0,
            requires_confirmation = hasCombatHandSelection
                ? combatHandSelection.RequiresConfirmation
                : hasCardGridSelection && cardGridSelection.RequiresConfirmation,
            // Same predicate that exposes confirm_selection: a single-select grid that settles on the
            // click must not advertise can_confirm, or the model would look for a confirmation the
            // executor rejects.
            can_confirm = CanConfirmSelection(currentScreen),
            cards = cards.Select((holder, index) => BuildSelectionCardPayload(
                holder.CardModel!,
                index,
                IsCardSelected(currentScreen, holder.CardModel!))).ToArray()
        };
    }

    private static SelectionCardPayload BuildSelectionCardPayload(CardModel card, int index, bool selected)
    {
        var resolvedRulesText = GetResolvedCardRulesText(card);
        var dynamicValues = BuildCardDynamicValuePayloads(card);
        return new SelectionCardPayload
        {
            index = index,
            selected = selected,
            card_id = card.Id.Entry,
            name = card.Title,
            upgraded = card.IsUpgraded,
            card_type = card.Type.ToString(),
            rarity = card.Rarity.ToString(),
            costs_x = card.EnergyCost.CostsX,
            star_costs_x = card.HasStarCostX,
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers()),
            rules_text = GetCardRulesText(card),
            resolved_rules_text = resolvedRulesText,
            dynamic_values = dynamicValues
        };
    }

    private static bool IsCardSelected(IScreenContext? currentScreen, CardModel card)
    {
        if (currentScreen is NCardGridSelectionScreen &&
            ReflectionMemberAccessor.TryGetValue(currentScreen, "_selectedCards") is IEnumerable selectedGridCards)
        {
            foreach (var item in selectedGridCards)
            {
                if (ReferenceEquals(item, card))
                {
                    return true;
                }
            }
        }

        if (TryGetCombatHandSelection(currentScreen, out var hand) && hand != null)
        {
            if (ReflectedGameMembers.Field(typeof(NPlayerHand), "_selectedCards")?.GetValue(hand) is IEnumerable selectedHandCards)
            {
                foreach (var item in selectedHandCards)
                {
                    if (ReferenceEquals(item, card))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static NProceedButton? GetProceedButton(IScreenContext? currentScreen)
    {
        if (currentScreen is null || currentScreen is NCardRewardSelectionScreen)
        {
            return null;
        }

        if (currentScreen is NRewardsScreen rewardsScreen)
        {
            var rewardProceedButton = GetRewardProceedButton(rewardsScreen);
            return IsProceedButtonUsable(rewardProceedButton)
                ? rewardProceedButton
                : null;
        }

        if (currentScreen is IRoomWithProceedButton roomWithProceedButton)
        {
            return IsProceedButtonUsable(roomWithProceedButton.ProceedButton)
                ? roomWithProceedButton.ProceedButton
                : null;
        }

        if (currentScreen is not Node rootNode)
        {
            return null;
        }

        return FindDescendants<NProceedButton>(rootNode)
            .FirstOrDefault(IsProceedButtonUsable);
    }

    public static NButton? GetCardsViewBackButton(IScreenContext? currentScreen)
    {
        if (currentScreen is not Node screenNode || !IsClosableCardViewer(currentScreen))
        {
            return null;
        }

        var backButton = screenNode.GetNodeOrNull<NButton>("BackButton");
        return backButton != null &&
            GodotObject.IsInstanceValid(backButton) &&
            backButton.IsVisibleInTree() &&
            backButton.IsEnabled
            ? backButton
            : null;
    }
}
