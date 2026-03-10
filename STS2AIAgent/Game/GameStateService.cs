using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;

namespace STS2AIAgent.Game;

internal static class GameStateService
{
    private const int StateVersion = 1;

    public static GameStatePayload BuildStatePayload()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var screen = ResolveScreen(currentScreen);
        var availableActions = BuildAvailableActionNames(currentScreen, combatState, runState);

        return new GameStatePayload
        {
            state_version = StateVersion,
            run_id = runState?.Rng.StringSeed ?? "run_unknown",
            screen = screen,
            in_combat = CombatManager.Instance.IsInProgress,
            turn = combatState?.RoundNumber,
            available_actions = availableActions,
            combat = BuildCombatPayload(combatState),
            run = BuildRunPayload(runState),
            map = BuildMapPayload(currentScreen, runState),
            selection = BuildSelectionPayload(currentScreen),
            @event = null,
            shop = null,
            rest = null,
            reward = BuildRewardPayload(currentScreen),
            game_over = null
        };
    }

    public static AvailableActionsPayload BuildAvailableActionsPayload()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var descriptors = new List<ActionDescriptor>();

        if (CanEndTurn(currentScreen, combatState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "end_turn",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanPlayAnyCard(currentScreen, combatState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "play_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanChooseMapNode(currentScreen, runState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_map_node",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanCollectRewardsAndProceed(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "collect_rewards_and_proceed",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanClaimReward(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "claim_reward",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanChooseRewardCard(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_reward_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanSkipRewardCards(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "skip_reward_cards",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanSelectDeckCard(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "select_deck_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanProceed(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "proceed",
                requires_target = false,
                requires_index = false
            });
        }

        return new AvailableActionsPayload
        {
            screen = ResolveScreen(currentScreen),
            actions = descriptors.ToArray()
        };
    }

    public static string ResolveScreen(IScreenContext? currentScreen)
    {
        return currentScreen switch
        {
            NGameOverScreen => "GAME_OVER",
            NCardRewardSelectionScreen => "REWARD",
            NDeckCardSelectScreen => "CARD_SELECTION",
            NRewardsScreen => "REWARD",
            NTreasureRoom or NTreasureRoomRelicCollection => "CHEST",
            NRestSiteRoom => "REST",
            NMerchantRoom or NMerchantInventory => "SHOP",
            NEventRoom => "EVENT",
            NCombatRoom => "COMBAT",
            NMapScreen or NMapRoom => "MAP",
            NCharacterSelectScreen => "CHARACTER_SELECT",
            NPatchNotesScreen => "MAIN_MENU",
            NSubmenu => "MAIN_MENU",
            NLogoAnimation => "MAIN_MENU",
            NMainMenu => "MAIN_MENU",
            _ => "UNKNOWN"
        };
    }

    public static bool CanEndTurn(IScreenContext? currentScreen, CombatState? combatState)
    {
        if (!CanUseCombatActions(currentScreen, combatState, out _, out _))
        {
            return false;
        }

        return !CombatManager.Instance.IsPlayerReadyToEndTurn(LocalContext.GetMe(combatState)!);
    }

    public static bool CanPlayAnyCard(IScreenContext? currentScreen, CombatState? combatState)
    {
        if (!CanUseCombatActions(currentScreen, combatState, out var me, out _))
        {
            return false;
        }

        return me!.PlayerCombatState!.Hand.Cards.Any(IsCardPlayable);
    }

    public static Player? GetLocalPlayer(CombatState? combatState)
    {
        return LocalContext.GetMe(combatState);
    }

    public static bool CanChooseMapNode(IScreenContext? currentScreen, RunState? runState)
    {
        return GetAvailableMapNodes(currentScreen, runState).Count > 0;
    }

    public static bool CanCollectRewardsAndProceed(IScreenContext? currentScreen)
    {
        return currentScreen is NRewardsScreen || currentScreen is NCardRewardSelectionScreen;
    }

    public static bool CanClaimReward(IScreenContext? currentScreen)
    {
        return GetRewardButtons(currentScreen).Any(button => button.IsEnabled);
    }

    public static bool CanChooseRewardCard(IScreenContext? currentScreen)
    {
        return GetCardRewardOptions(currentScreen).Count > 0;
    }

    public static bool CanSkipRewardCards(IScreenContext? currentScreen)
    {
        return GetCardRewardAlternativeButtons(currentScreen).Count > 0;
    }

    public static bool CanSelectDeckCard(IScreenContext? currentScreen)
    {
        return GetDeckSelectionOptions(currentScreen).Count > 0;
    }

    public static bool CanProceed(IScreenContext? currentScreen)
    {
        return GetProceedButton(currentScreen) != null;
    }

    public static IReadOnlyList<NMapPoint> GetAvailableMapNodes(IScreenContext? currentScreen, RunState? runState)
    {
        if (!TryGetMapScreen(currentScreen, runState, out var mapScreen))
        {
            return Array.Empty<NMapPoint>();
        }

        return FindDescendants<NMapPoint>(mapScreen!)
            .Where(node => GodotObject.IsInstanceValid(node) && node.IsEnabled)
            .OrderBy(node => node.Point.coord.row)
            .ThenBy(node => node.Point.coord.col)
            .ToArray();
    }

    public static IReadOnlyList<NRewardButton> GetRewardButtons(IScreenContext? currentScreen)
    {
        if (currentScreen is not NRewardsScreen rewardScreen)
        {
            return Array.Empty<NRewardButton>();
        }

        return FindDescendants<NRewardButton>(rewardScreen)
            .Where(node => GodotObject.IsInstanceValid(node))
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
            .ToArray();
    }

    public static IReadOnlyList<NGridCardHolder> GetDeckSelectionOptions(IScreenContext? currentScreen)
    {
        if (currentScreen is not NDeckCardSelectScreen deckCardSelectScreen)
        {
            return Array.Empty<NGridCardHolder>();
        }

        return FindDescendants<NGridCardHolder>(deckCardSelectScreen)
            .Where(node => GodotObject.IsInstanceValid(node) && node.CardModel != null)
            .ToArray();
    }

    public static string? GetDeckSelectionPrompt(IScreenContext? currentScreen)
    {
        if (currentScreen is not NDeckCardSelectScreen deckCardSelectScreen)
        {
            return null;
        }

        return deckCardSelectScreen.GetNodeOrNull<MegaRichTextLabel>("%BottomLabel")?.Text;
    }

    public static NProceedButton? GetProceedButton(IScreenContext? currentScreen)
    {
        if (currentScreen is null || currentScreen is NRewardsScreen || currentScreen is NCardRewardSelectionScreen)
        {
            return null;
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

    public static Creature? ResolveEnemyTarget(CombatState combatState, int targetIndex)
    {
        var enemies = combatState.Enemies.ToList();
        if (targetIndex < 0 || targetIndex >= enemies.Count)
        {
            return null;
        }

        return enemies[targetIndex];
    }

    public static bool CardRequiresTarget(CardModel card)
    {
        return card.TargetType == TargetType.AnyEnemy || card.TargetType == TargetType.AnyAlly;
    }

    public static bool IsCardPlayable(CardModel card)
    {
        return card.CanPlay(out _, out _);
    }

    public static string? GetUnplayableReasonCode(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return GetUnplayableReasonCode(reason);
    }

    public static string? GetUnplayableReasonCode(UnplayableReason reason)
    {
        if (reason == UnplayableReason.None)
        {
            return null;
        }

        if (reason.HasFlag(UnplayableReason.EnergyCostTooHigh))
        {
            return "not_enough_energy";
        }

        if (reason.HasFlag(UnplayableReason.StarCostTooHigh))
        {
            return "not_enough_stars";
        }

        if (reason.HasFlag(UnplayableReason.NoLivingAllies))
        {
            return "no_living_allies";
        }

        if (reason.HasFlag(UnplayableReason.BlockedByHook))
        {
            return "blocked_by_hook";
        }

        if (reason.HasFlag(UnplayableReason.HasUnplayableKeyword) || reason.HasFlag(UnplayableReason.BlockedByCardLogic))
        {
            return "unplayable";
        }

        return reason.ToString();
    }

    private static bool CanUseCombatActions(IScreenContext? currentScreen, CombatState? combatState, out Player? me, out NCombatRoom? combatRoom)
    {
        me = null;
        combatRoom = null;

        if (combatState == null || currentScreen is not NCombatRoom room)
        {
            return false;
        }

        combatRoom = room;

        if (!CombatManager.Instance.IsInProgress ||
            CombatManager.Instance.IsOverOrEnding ||
            !CombatManager.Instance.IsPlayPhase ||
            CombatManager.Instance.PlayerActionsDisabled)
        {
            return false;
        }

        if (combatRoom.Mode != CombatRoomMode.ActiveCombat)
        {
            return false;
        }

        var hand = combatRoom.Ui?.Hand;
        if (hand == null || hand.InCardPlay || hand.IsInCardSelection || hand.CurrentMode != MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Mode.Play)
        {
            return false;
        }

        me = LocalContext.GetMe(combatState);
        if (me == null || !me.Creature.IsAlive)
        {
            return false;
        }

        return true;
    }

    private static string[] BuildAvailableActionNames(IScreenContext? currentScreen, CombatState? combatState, RunState? runState)
    {
        var names = new List<string>();

        if (CanEndTurn(currentScreen, combatState))
        {
            names.Add("end_turn");
        }

        if (CanPlayAnyCard(currentScreen, combatState))
        {
            names.Add("play_card");
        }

        if (CanChooseMapNode(currentScreen, runState))
        {
            names.Add("choose_map_node");
        }

        if (CanCollectRewardsAndProceed(currentScreen))
        {
            names.Add("collect_rewards_and_proceed");
        }

        if (CanClaimReward(currentScreen))
        {
            names.Add("claim_reward");
        }

        if (CanChooseRewardCard(currentScreen))
        {
            names.Add("choose_reward_card");
        }

        if (CanSkipRewardCards(currentScreen))
        {
            names.Add("skip_reward_cards");
        }

        if (CanSelectDeckCard(currentScreen))
        {
            names.Add("select_deck_card");
        }

        if (CanProceed(currentScreen))
        {
            names.Add("proceed");
        }

        return names.ToArray();
    }

    private static CombatPayload? BuildCombatPayload(CombatState? combatState)
    {
        var me = LocalContext.GetMe(combatState);
        if (combatState == null || me?.PlayerCombatState == null)
        {
            return null;
        }

        var hand = me.PlayerCombatState.Hand.Cards.ToList();
        var enemies = combatState.Enemies.ToList();

        return new CombatPayload
        {
            player = new CombatPlayerPayload
            {
                current_hp = me.Creature.CurrentHp,
                max_hp = me.Creature.MaxHp,
                block = me.Creature.Block,
                energy = me.PlayerCombatState.Energy,
                stars = me.PlayerCombatState.Stars
            },
            hand = hand.Select((card, index) => BuildHandCardPayload(card, index)).ToArray(),
            enemies = enemies.Select((enemy, index) => BuildEnemyPayload(enemy, index)).ToArray()
        };
    }

    private static RunPayload? BuildRunPayload(RunState? runState)
    {
        var player = LocalContext.GetMe(runState);
        if (player == null)
        {
            return null;
        }

        return new RunPayload
        {
            current_hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            gold = player.Gold,
            max_energy = player.MaxEnergy,
            deck = player.Deck.Cards.Select((card, index) => BuildDeckCardPayload(card, index)).ToArray(),
            relics = player.Relics.Select((relic, index) => new RunRelicPayload
            {
                index = index,
                relic_id = relic.Id.Entry,
                name = relic.Title.GetFormattedText(),
                is_melted = relic.IsMelted
            }).ToArray(),
            potions = player.PotionSlots.Select((potion, index) => new RunPotionPayload
            {
                index = index,
                potion_id = potion?.Id.Entry,
                name = potion?.Title.GetFormattedText(),
                occupied = potion != null
            }).ToArray()
        };
    }

    private static MapPayload? BuildMapPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (!TryGetMapScreen(currentScreen, runState, out var mapScreen))
        {
            return null;
        }

        var availableNodes = FindDescendants<NMapPoint>(mapScreen!)
            .Where(node => GodotObject.IsInstanceValid(node) && node.IsEnabled)
            .OrderBy(node => node.Point.coord.row)
            .ThenBy(node => node.Point.coord.col)
            .ToArray();

        return new MapPayload
        {
            current_node = BuildMapCoordPayload(runState!.CurrentMapCoord),
            is_travel_enabled = mapScreen!.IsTravelEnabled,
            is_traveling = mapScreen.IsTraveling,
            map_generation_count = RunManager.Instance.MapSelectionSynchronizer.MapGenerationCount,
            available_nodes = availableNodes.Select((node, index) => BuildMapNodePayload(node, index)).ToArray()
        };
    }

    private static SelectionPayload? BuildSelectionPayload(IScreenContext? currentScreen)
    {
        if (currentScreen is not NDeckCardSelectScreen)
        {
            return null;
        }

        var cards = GetDeckSelectionOptions(currentScreen);

        return new SelectionPayload
        {
            kind = "deck_card_select",
            prompt = GetDeckSelectionPrompt(currentScreen) ?? string.Empty,
            cards = cards.Select((holder, index) => BuildSelectionCardPayload(holder.CardModel!, index)).ToArray()
        };
    }

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

    private static CombatHandCardPayload BuildHandCardPayload(CardModel card, int index)
    {
        card.CanPlay(out var reason, out _);

        return new CombatHandCardPayload
        {
            index = index,
            card_id = card.Id.Entry,
            name = card.Title,
            upgraded = card.IsUpgraded,
            target_type = card.TargetType.ToString(),
            requires_target = CardRequiresTarget(card),
            costs_x = card.EnergyCost.CostsX,
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers()),
            playable = reason == UnplayableReason.None,
            unplayable_reason = GetUnplayableReasonCode(reason)
        };
    }

    private static CombatEnemyPayload BuildEnemyPayload(Creature enemy, int index)
    {
        return new CombatEnemyPayload
        {
            index = index,
            enemy_id = enemy.ModelId.Entry,
            name = enemy.Name,
            current_hp = enemy.CurrentHp,
            max_hp = enemy.MaxHp,
            block = enemy.Block,
            is_alive = enemy.IsAlive,
            is_hittable = enemy.IsHittable,
            intent = enemy.Monster?.NextMove?.Id
        };
    }

    private static MapNodePayload BuildMapNodePayload(NMapPoint node, int index)
    {
        return new MapNodePayload
        {
            index = index,
            row = node.Point.coord.row,
            col = node.Point.coord.col,
            node_type = node.Point.PointType.ToString(),
            state = node.State.ToString()
        };
    }

    private static MapCoordPayload? BuildMapCoordPayload(MapCoord? coord)
    {
        if (!coord.HasValue)
        {
            return null;
        }

        return new MapCoordPayload
        {
            row = coord.Value.row,
            col = coord.Value.col
        };
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

        return new RewardCardOptionPayload
        {
            index = index,
            card_id = card?.Id.Entry ?? string.Empty,
            name = card?.Title ?? string.Empty,
            upgraded = card?.IsUpgraded ?? false
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

    private static DeckCardPayload BuildDeckCardPayload(CardModel card, int index)
    {
        return new DeckCardPayload
        {
            index = index,
            card_id = card.Id.Entry,
            name = card.Title,
            upgraded = card.IsUpgraded,
            card_type = card.Type.ToString(),
            rarity = card.Rarity.ToString(),
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers())
        };
    }

    private static SelectionCardPayload BuildSelectionCardPayload(CardModel card, int index)
    {
        return new SelectionCardPayload
        {
            index = index,
            card_id = card.Id.Entry,
            name = card.Title,
            upgraded = card.IsUpgraded,
            card_type = card.Type.ToString(),
            rarity = card.Rarity.ToString()
        };
    }

    private static bool IsProceedButtonUsable(NProceedButton? button)
    {
        return button != null &&
            GodotObject.IsInstanceValid(button) &&
            button.IsEnabled &&
            button.IsVisibleInTree();
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

    private static bool TryGetMapScreen(IScreenContext? currentScreen, RunState? runState, out NMapScreen? mapScreen)
    {
        mapScreen = currentScreen as NMapScreen ?? NMapScreen.Instance;
        if (runState == null || currentScreen is not (NMapScreen or NMapRoom))
        {
            return false;
        }

        if (mapScreen == null || !GodotObject.IsInstanceValid(mapScreen))
        {
            return false;
        }

        return mapScreen.IsVisibleInTree() && mapScreen.IsOpen;
    }

    private static List<T> FindDescendants<T>(Node root) where T : Node
    {
        var found = new List<T>();
        FindDescendantsRecursive(root, found);
        return found;
    }

    private static void FindDescendantsRecursive<T>(Node node, List<T> found) where T : Node
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (node is T typedNode)
        {
            found.Add(typedNode);
        }

        foreach (Node child in node.GetChildren())
        {
            FindDescendantsRecursive(child, found);
        }
    }
}

internal sealed class GameStatePayload
{
    public int state_version { get; init; }

    public string run_id { get; init; } = "run_unknown";

    public string screen { get; init; } = "UNKNOWN";

    public bool in_combat { get; init; }

    public int? turn { get; init; }

    public string[] available_actions { get; init; } = Array.Empty<string>();

    public CombatPayload? combat { get; init; }

    public RunPayload? run { get; init; }

    public MapPayload? map { get; init; }

    public SelectionPayload? selection { get; init; }

    public object? @event { get; init; }

    public object? shop { get; init; }

    public object? rest { get; init; }

    public RewardPayload? reward { get; init; }

    public object? game_over { get; init; }
}

internal sealed class AvailableActionsPayload
{
    public string screen { get; init; } = "UNKNOWN";

    public ActionDescriptor[] actions { get; init; } = Array.Empty<ActionDescriptor>();
}

internal sealed class CombatPayload
{
    public CombatPlayerPayload player { get; init; } = new();

    public CombatHandCardPayload[] hand { get; init; } = Array.Empty<CombatHandCardPayload>();

    public CombatEnemyPayload[] enemies { get; init; } = Array.Empty<CombatEnemyPayload>();
}

internal sealed class RunPayload
{
    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int gold { get; init; }

    public int max_energy { get; init; }

    public DeckCardPayload[] deck { get; init; } = Array.Empty<DeckCardPayload>();

    public RunRelicPayload[] relics { get; init; } = Array.Empty<RunRelicPayload>();

    public RunPotionPayload[] potions { get; init; } = Array.Empty<RunPotionPayload>();
}

internal sealed class MapPayload
{
    public MapCoordPayload? current_node { get; init; }

    public bool is_travel_enabled { get; init; }

    public bool is_traveling { get; init; }

    public int map_generation_count { get; init; }

    public MapNodePayload[] available_nodes { get; init; } = Array.Empty<MapNodePayload>();
}

internal sealed class SelectionPayload
{
    public string kind { get; init; } = string.Empty;

    public string prompt { get; init; } = string.Empty;

    public SelectionCardPayload[] cards { get; init; } = Array.Empty<SelectionCardPayload>();
}

internal sealed class MapCoordPayload
{
    public int row { get; init; }

    public int col { get; init; }
}

internal sealed class MapNodePayload
{
    public int index { get; init; }

    public int row { get; init; }

    public int col { get; init; }

    public string node_type { get; init; } = string.Empty;

    public string state { get; init; } = string.Empty;
}

internal sealed class CombatPlayerPayload
{
    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int block { get; init; }

    public int energy { get; init; }

    public int stars { get; init; }
}

internal sealed class CombatHandCardPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string target_type { get; init; } = string.Empty;

    public bool requires_target { get; init; }

    public bool costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public bool playable { get; init; }

    public string? unplayable_reason { get; init; }
}

internal sealed class CombatEnemyPayload
{
    public int index { get; init; }

    public string enemy_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int block { get; init; }

    public bool is_alive { get; init; }

    public bool is_hittable { get; init; }

    public string? intent { get; init; }
}

internal sealed class RewardPayload
{
    public bool pending_card_choice { get; init; }

    public bool can_proceed { get; init; }

    public RewardOptionPayload[] rewards { get; init; } = Array.Empty<RewardOptionPayload>();

    public RewardCardOptionPayload[] card_options { get; init; } = Array.Empty<RewardCardOptionPayload>();

    public RewardAlternativePayload[] alternatives { get; init; } = Array.Empty<RewardAlternativePayload>();
}

internal sealed class RewardOptionPayload
{
    public int index { get; init; }

    public string reward_type { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool claimable { get; init; }
}

internal sealed class RewardCardOptionPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }
}

internal sealed class RewardAlternativePayload
{
    public int index { get; init; }

    public string label { get; init; } = string.Empty;
}

internal sealed class DeckCardPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public int energy_cost { get; init; }

    public int star_cost { get; init; }
}

internal sealed class SelectionCardPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;
}

internal sealed class RunRelicPayload
{
    public int index { get; init; }

    public string relic_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool is_melted { get; init; }
}

internal sealed class RunPotionPayload
{
    public int index { get; init; }

    public string? potion_id { get; init; }

    public string? name { get; init; }

    public bool occupied { get; init; }
}

internal sealed class ActionDescriptor
{
    public string name { get; init; } = string.Empty;

    public bool requires_target { get; init; }

    public bool requires_index { get; init; }
}
