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
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
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
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;

namespace STS2AIAgent.Game;

internal static class GameStateService
{
    private const int StateVersion = 6;
    private const int AgentViewVersion = 1;

    public static GameStatePayload BuildStatePayload()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var screen = ResolveScreen(currentScreen);
        var session = BuildSessionPayload(currentScreen, runState);
        var availableActions = BuildAvailableActionNames(currentScreen, combatState, runState);
        var combat = BuildCombatPayload(combatState);
        var run = BuildRunPayload(currentScreen, combatState, runState);
        var multiplayer = BuildMultiplayerPayload(currentScreen, runState);
        var multiplayerLobby = BuildMultiplayerLobbyPayload(currentScreen);
        var map = BuildMapPayload(currentScreen, runState);
        var selection = BuildSelectionPayload(currentScreen);
        var characterSelect = BuildCharacterSelectPayload(currentScreen);
        var timeline = BuildTimelinePayload(currentScreen);
        var chest = BuildChestPayload(currentScreen);
        var eventPayload = BuildEventPayload(currentScreen);
        var shop = BuildShopPayload(currentScreen);
        var rest = BuildRestPayload(currentScreen);
        var reward = BuildRewardPayload(currentScreen);
        var modal = BuildModalPayload(currentScreen);
        var gameOver = BuildGameOverPayload(currentScreen, runState);

        return new GameStatePayload
        {
            state_version = StateVersion,
            run_id = runState?.Rng.StringSeed ?? "run_unknown",
            screen = screen,
            session = session,
            in_combat = CombatManager.Instance.IsInProgress,
            turn = combatState?.RoundNumber,
            available_actions = availableActions,
            combat = combat,
            run = run,
            multiplayer = multiplayer,
            multiplayer_lobby = multiplayerLobby,
            map = map,
            selection = selection,
            character_select = characterSelect,
            timeline = timeline,
            chest = chest,
            @event = eventPayload,
            shop = shop,
            rest = rest,
            reward = reward,
            modal = modal,
            game_over = gameOver,
            agent_view = BuildAgentViewPayload(
                screen,
                session,
                runState?.Rng.StringSeed ?? "run_unknown",
                combatState?.RoundNumber,
                availableActions,
                combatState,
                runState,
                combat,
                run,
                map,
                selection,
                characterSelect,
                timeline,
                chest,
                eventPayload,
                shop,
                rest,
                reward,
                modal,
                gameOver)
        };
    }

    private static SessionPayload BuildSessionPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (GetMultiplayerTestScene() != null)
        {
            return new SessionPayload
            {
                mode = "multiplayer",
                phase = "multiplayer_lobby",
                control_scope = "local_player"
            };
        }

        var characterSelectScreen = GetCharacterSelectScreen(currentScreen);
        if (characterSelectScreen != null)
        {
            return new SessionPayload
            {
                mode = characterSelectScreen.Lobby.NetService.Type.IsMultiplayer() ? "multiplayer" : "singleplayer",
                phase = "character_select",
                control_scope = "local_player"
            };
        }

        if (runState != null)
        {
            return new SessionPayload
            {
                mode = RunManager.Instance.NetService.Type.IsMultiplayer() ? "multiplayer" : "singleplayer",
                phase = "run",
                control_scope = "local_player"
            };
        }

        return new SessionPayload
        {
            mode = "singleplayer",
            phase = "menu",
            control_scope = "local_player"
        };
    }

    public static AvailableActionsPayload BuildAvailableActionsPayload()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var descriptors = new List<ActionDescriptor>();

        if (GetOpenModal() != null)
        {
            if (CanConfirmModal(currentScreen))
            {
                descriptors.Add(new ActionDescriptor
                {
                    name = "confirm_modal",
                    requires_target = false,
                    requires_index = false
                });
            }

            if (CanDismissModal(currentScreen))
            {
                descriptors.Add(new ActionDescriptor
                {
                    name = "dismiss_modal",
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

        if (CanContinueRun(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "continue_run",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanAbandonRun(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "abandon_run",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanOpenCharacterSelect(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_character_select",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanOpenTimeline(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_timeline",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanCloseMainMenuSubmenu(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "close_main_menu_submenu",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanChooseTimelineEpoch(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_timeline_epoch",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanConfirmTimelineOverlay(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "confirm_timeline_overlay",
                requires_target = false,
                requires_index = false
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

        if (CanCloseCardsView(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "close_cards_view",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanConfirmSelection(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "confirm_selection",
                requires_target = false,
                requires_index = false
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

        if (CanOpenChest(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_chest",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanChooseTreasureRelic(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_treasure_relic",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanChooseEventOption(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_event_option",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanChooseRestOption(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_rest_option",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanOpenShopInventory(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_shop_inventory",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanCloseShopInventory(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "close_shop_inventory",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanBuyShopCard(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "buy_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanBuyShopRelic(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "buy_relic",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanBuyShopPotion(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "buy_potion",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanRemoveCardAtShop(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "remove_card_at_shop",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanSelectCharacter(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "select_character",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanEmbark(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "embark",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanUnready(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "unready",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanHostMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "host_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanJoinMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "join_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanReadyMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "ready_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanDisconnectMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "disconnect_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanIncreaseAscension(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "increase_ascension",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanDecreaseAscension(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "decrease_ascension",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanUsePotion(currentScreen, combatState, runState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "use_potion",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanDiscardPotion(runState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "discard_potion",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanReturnToMainMenu(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "return_to_main_menu",
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
        if (GetOpenModal() != null)
        {
            return "MODAL";
        }

        var screen = ResolveNonModalScreen(currentScreen);
        if (screen == "UNKNOWN" && currentScreen != null)
        {
            Log.Warn($"[STS2AIAgent] Unhandled screen type: {currentScreen.GetType().FullName}");
        }

        return screen;
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

    public static Player? GetLocalPlayer(RunState? runState)
    {
        return LocalContext.GetMe(runState);
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

    public static bool CanCloseCardsView(IScreenContext? currentScreen)
    {
        return GetCardsViewBackButton(currentScreen) != null;
    }

    public static bool CanConfirmSelection(IScreenContext? currentScreen)
    {
        return TryGetCombatHandSelectionMetadata(currentScreen, out _, out var metadata) &&
            metadata.RequiresConfirmation &&
            metadata.CanConfirm;
    }

    public static bool CanProceed(IScreenContext? currentScreen)
    {
        if (currentScreen is NRewardsScreen or NCardRewardSelectionScreen)
        {
            return false;
        }

        return GetProceedButton(currentScreen) != null;
    }

    public static bool CanOpenChest(IScreenContext? currentScreen)
    {
        if (currentScreen is not NTreasureRoom treasureRoom)
        {
            return false;
        }

        var chestButton = treasureRoom.GetNodeOrNull<NButton>("%Chest");
        return chestButton != null && GodotObject.IsInstanceValid(chestButton) && chestButton.IsEnabled;
    }

    public static bool CanChooseTreasureRelic(IScreenContext? currentScreen)
    {
        if (GetTreasureRelicCollection(currentScreen) == null)
        {
            return false;
        }

        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        return relics != null && relics.Count > 0;
    }

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

    public static bool CanChooseEventOption(IScreenContext? currentScreen)
    {
        if (currentScreen is not NEventRoom)
        {
            return false;
        }

        try
        {
            var eventModel = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            if (eventModel == null)
            {
                return false;
            }

            // Finished events have a synthetic proceed option
            if (eventModel.IsFinished)
            {
                return true;
            }

            // Non-finished events need at least one non-locked option
            return eventModel.CurrentOptions.Any(o => !o.IsLocked);
        }
        catch
        {
            return false;
        }
    }

    public static bool CanChooseRestOption(IScreenContext? currentScreen)
    {
        if (currentScreen is not NRestSiteRoom)
        {
            return false;
        }

        try
        {
            var options = RunManager.Instance.RestSiteSynchronizer.GetLocalOptions();
            return options != null && options.Any(o => o.IsEnabled);
        }
        catch
        {
            return false;
        }
    }

    public static bool CanOpenShopInventory(IScreenContext? currentScreen)
    {
        var room = GetMerchantRoom(currentScreen);
        return room != null && room.Inventory != null && !room.Inventory.IsOpen && currentScreen is NMerchantRoom;
    }

    public static bool CanCloseShopInventory(IScreenContext? currentScreen)
    {
        return currentScreen is NMerchantInventory inventory && inventory.IsOpen;
    }

    public static bool CanBuyShopCard(IScreenContext? currentScreen)
    {
        var inventoryScreen = GetMerchantInventoryScreen(currentScreen);
        return inventoryScreen != null && inventoryScreen.IsOpen &&
            GetMerchantCardEntries(currentScreen).Any(entry => entry.IsStocked && entry.EnoughGold);
    }

    public static bool CanBuyShopRelic(IScreenContext? currentScreen)
    {
        var inventoryScreen = GetMerchantInventoryScreen(currentScreen);
        return inventoryScreen != null && inventoryScreen.IsOpen &&
            GetMerchantRelicEntries(currentScreen).Any(entry => entry.IsStocked && entry.EnoughGold);
    }

    public static bool CanBuyShopPotion(IScreenContext? currentScreen)
    {
        var inventoryScreen = GetMerchantInventoryScreen(currentScreen);
        var inventory = GetMerchantInventory(currentScreen);
        return inventoryScreen != null && inventoryScreen.IsOpen &&
            GetMerchantPotionEntries(currentScreen).Any(entry => CanPurchaseShopPotion(inventory?.Player, entry));
    }

    public static bool CanRemoveCardAtShop(IScreenContext? currentScreen)
    {
        var inventoryScreen = GetMerchantInventoryScreen(currentScreen);
        var entry = GetMerchantCardRemovalEntry(currentScreen);
        return inventoryScreen != null && inventoryScreen.IsOpen &&
            entry?.IsStocked == true && entry.EnoughGold;
    }

    public static bool CanSelectCharacter(IScreenContext? currentScreen)
    {
        var multiplayerTestScene = GetMultiplayerTestScene();
        if (multiplayerTestScene != null)
        {
            return GetMultiplayerTestLobby(multiplayerTestScene) != null && GetMultiplayerLobbyCharacters().Length > 0;
        }

        return GetCharacterSelectButtons(currentScreen)
            .Any(button => !button.IsLocked && button.IsEnabled && button.IsVisibleInTree());
    }

    public static bool CanContinueRun(IScreenContext? currentScreen)
    {
        if (currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree())
        {
            return false;
        }

        if (mainMenu.SubmenuStack?.SubmenusOpen == true)
        {
            return false;
        }

        var continueButton = GetMainMenuContinueButton(mainMenu);
        return continueButton != null && continueButton.IsVisibleInTree() && continueButton.IsEnabled;
    }

    public static bool CanAbandonRun(IScreenContext? currentScreen)
    {
        if (currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree())
        {
            return false;
        }

        if (mainMenu.SubmenuStack?.SubmenusOpen == true)
        {
            return false;
        }

        var abandonButton = GetMainMenuAbandonRunButton(mainMenu);
        return abandonButton != null && abandonButton.IsVisibleInTree() && abandonButton.IsEnabled;
    }

    public static bool CanOpenCharacterSelect(IScreenContext? currentScreen)
    {
        if (currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree())
        {
            return false;
        }

        if (mainMenu.SubmenuStack?.SubmenusOpen == true)
        {
            return false;
        }

        var singleplayerButton = GetMainMenuSingleplayerButton(mainMenu);
        if (singleplayerButton != null && singleplayerButton.IsVisibleInTree() && singleplayerButton.IsEnabled)
        {
            return true;
        }

        // Some main-menu states still allow the singleplayer submenu to open even when the
        // button has not become visible in the scene tree. If there is no active run flow to
        // continue or abandon, prefer exposing character select instead of hard-blocking.
        return !CanContinueRun(currentScreen) && !CanAbandonRun(currentScreen);
    }

    public static bool CanOpenTimeline(IScreenContext? currentScreen)
    {
        if (currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree())
        {
            return false;
        }

        if (mainMenu.SubmenuStack?.SubmenusOpen == true)
        {
            return false;
        }

        var timelineButton = GetMainMenuTimelineButton(mainMenu);
        return timelineButton != null && timelineButton.IsVisibleInTree() && timelineButton.IsEnabled;
    }

    public static bool CanCloseMainMenuSubmenu(IScreenContext? currentScreen)
    {
        if (currentScreen is not NSubmenu submenu || !submenu.IsVisibleInTree())
        {
            return false;
        }

        var submenuStack = GetMainMenuSubmenuStack(submenu);
        return submenuStack != null && submenuStack.SubmenusOpen;
    }

    public static bool CanEmbark(IScreenContext? currentScreen)
    {
        var embarkButton = GetCharacterEmbarkButton(currentScreen);
        return embarkButton != null && embarkButton.IsEnabled && embarkButton.IsVisibleInTree();
    }

    public static bool CanUnready(IScreenContext? currentScreen)
    {
        var multiplayerTestScene = GetMultiplayerTestScene();
        var multiplayerLobby = multiplayerTestScene != null ? GetMultiplayerTestLobby(multiplayerTestScene) : null;
        if (multiplayerLobby != null)
        {
            return multiplayerLobby.LocalPlayer.isReady;
        }

        var unreadyButton = GetCharacterUnreadyButton(currentScreen);
        return unreadyButton != null && unreadyButton.IsEnabled && unreadyButton.IsVisibleInTree();
    }

    public static bool CanHostMultiplayerLobby(IScreenContext? currentScreen)
    {
        var scene = GetMultiplayerTestScene();
        return scene != null && GetMultiplayerTestLobby(scene) == null;
    }

    public static bool CanJoinMultiplayerLobby(IScreenContext? currentScreen)
    {
        var scene = GetMultiplayerTestScene();
        return scene != null && GetMultiplayerTestLobby(scene) == null;
    }

    public static bool CanReadyMultiplayerLobby(IScreenContext? currentScreen)
    {
        var scene = GetMultiplayerTestScene();
        var lobby = scene != null ? GetMultiplayerTestLobby(scene) : null;
        return lobby != null && !lobby.LocalPlayer.isReady;
    }

    public static bool CanDisconnectMultiplayerLobby(IScreenContext? currentScreen)
    {
        var scene = GetMultiplayerTestScene();
        return scene != null && GetMultiplayerTestLobby(scene) != null;
    }

    public static bool CanIncreaseAscension(IScreenContext? currentScreen)
    {
        return CanAdjustAscension(currentScreen, delta: 1);
    }

    public static bool CanDecreaseAscension(IScreenContext? currentScreen)
    {
        return CanAdjustAscension(currentScreen, delta: -1);
    }

    public static bool CanChooseTimelineEpoch(IScreenContext? currentScreen)
    {
        return GetTimelineSlots(currentScreen).Any(slot => slot.State is EpochSlotState.Obtained or EpochSlotState.Complete);
    }

    public static bool CanConfirmTimelineOverlay(IScreenContext? currentScreen)
    {
        var unlockConfirmButton = GetTimelineUnlockConfirmButton(currentScreen);
        if (unlockConfirmButton != null && unlockConfirmButton.IsVisibleInTree() && unlockConfirmButton.IsEnabled)
        {
            return true;
        }

        var inspectCloseButton = GetTimelineInspectCloseButton(currentScreen);
        return inspectCloseButton != null && inspectCloseButton.IsVisibleInTree() && inspectCloseButton.IsEnabled;
    }

    public static bool CanUsePotion(IScreenContext? currentScreen, CombatState? combatState, RunState? runState)
    {
        var player = GetLocalPlayer(runState);
        if (player == null)
        {
            return false;
        }

        return player.PotionSlots.Any(potion => IsPotionUsable(currentScreen, combatState, player, potion));
    }

    public static bool CanUsePotionAtIndex(IScreenContext? currentScreen, CombatState? combatState, RunState? runState, int optionIndex)
    {
        var player = GetLocalPlayer(runState);
        if (player == null || optionIndex < 0 || optionIndex >= player.PotionSlots.Count)
        {
            return false;
        }

        return IsPotionUsable(currentScreen, combatState, player, player.PotionSlots[optionIndex]);
    }

    public static bool CanDiscardPotion(RunState? runState)
    {
        var player = GetLocalPlayer(runState);
        if (player == null)
        {
            return false;
        }

        return player.PotionSlots.Any(potion => IsPotionDiscardable(player, potion));
    }

    public static bool CanDiscardPotionAtIndex(RunState? runState, int optionIndex)
    {
        var player = GetLocalPlayer(runState);
        if (player == null || optionIndex < 0 || optionIndex >= player.PotionSlots.Count)
        {
            return false;
        }

        return IsPotionDiscardable(player, player.PotionSlots[optionIndex]);
    }

    public static bool CanConfirmModal(IScreenContext? currentScreen)
    {
        return GetModalConfirmButton(currentScreen) != null;
    }

    public static bool CanDismissModal(IScreenContext? currentScreen)
    {
        return GetModalCancelButton(currentScreen) != null;
    }

    public static bool CanReturnToMainMenu(IScreenContext? currentScreen)
    {
        return currentScreen is NGameOverScreen;
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

        if (currentScreen is Node rootNode)
        {
            return GetVisibleGridCardHolders(rootNode)
                .Cast<NCardHolder>()
                .ToArray();
        }

        return Array.Empty<NCardHolder>();
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

    public static bool TryGetCombatHandSelection(IScreenContext? currentScreen, out NPlayerHand? hand)
    {
        hand = null;

        if (currentScreen is not NCombatRoom combatRoom)
        {
            return false;
        }

        hand = combatRoom.Ui?.Hand;
        return hand != null &&
            GodotObject.IsInstanceValid(hand) &&
            hand.IsInCardSelection &&
            hand.CurrentMode is NPlayerHand.Mode.SimpleSelect or NPlayerHand.Mode.UpgradeSelect;
    }

    private static CardSelectorPrefs? TryGetCombatHandSelectionPrefs(NPlayerHand hand)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(NPlayerHand).GetField("_prefs", flags);
        if (field?.GetValue(hand) is CardSelectorPrefs prefs)
        {
            return prefs;
        }

        return null;
    }

    public static bool TryGetCombatHandSelectionMetadata(
        IScreenContext? currentScreen,
        out NPlayerHand? hand,
        out CombatHandSelectionMetadata metadata)
    {
        metadata = default;
        if (!TryGetCombatHandSelection(currentScreen, out hand) || hand == null)
        {
            return false;
        }

        var prefs = TryGetCombatHandSelectionPrefs(hand);
        var requiresConfirmation = prefs?.RequireManualConfirmation ?? false;
        var canConfirm = requiresConfirmation &&
            TryGetCombatHandConfirmButton(hand, out var confirmButton) &&
            confirmButton!.Visible &&
            confirmButton.IsEnabled;

        metadata = new CombatHandSelectionMetadata(
            prefs?.MinSelect ?? 1,
            prefs?.MaxSelect ?? 1,
            GetCombatHandSelectedCount(hand),
            requiresConfirmation,
            canConfirm);
        return true;
    }

    private static int GetCombatHandSelectedCount(NPlayerHand hand)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(NPlayerHand).GetField("_selectedCards", flags);
        return field?.GetValue(hand) is System.Collections.ICollection collection ? collection.Count : 0;
    }

    private static bool TryGetCombatHandConfirmButton(NPlayerHand hand, out NConfirmButton? confirmButton)
    {
        confirmButton = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton")
            ?? hand.GetNodeOrNull<NConfirmButton>("SelectModeConfirmButton");
        return confirmButton != null && GodotObject.IsInstanceValid(confirmButton);
    }

    private static string SafeReadString(Func<string?> getter, string fallback = "")
    {
        try
        {
            var value = getter();
            return value == null ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool SafeReadBool(Func<bool> getter, bool fallback = false)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private static string GetCardRulesText(CardModel? card)
    {
        if (card == null)
        {
            return string.Empty;
        }

        foreach (var memberName in new[]
        {
            "Description",
            "RulesText",
            "Body",
            "Text",
            "RawText",
            "DescriptionText"
        })
        {
            var text = TryReadCardTextMember(card, memberName);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return NormalizeCardRulesText(text);
            }
        }

        return string.Empty;
    }

    private static string TryReadCardTextMember(object instance, string memberName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        try
        {
            var property = instance.GetType().GetProperty(memberName, flags);
            if (property != null)
            {
                return TryCoerceText(property.GetValue(instance));
            }

            var field = instance.GetType().GetField(memberName, flags);
            if (field != null)
            {
                return TryCoerceText(field.GetValue(instance));
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string TryCoerceText(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var valueType = value.GetType();

        try
        {
            var getFormattedText = valueType.GetMethod("GetFormattedText", flags, null, Type.EmptyTypes, null);
            if (getFormattedText != null && getFormattedText.ReturnType == typeof(string))
            {
                return getFormattedText.Invoke(value, null) as string ?? string.Empty;
            }
        }
        catch
        {
        }

        try
        {
            var textProperty = valueType.GetProperty("Text", flags);
            if (textProperty != null && textProperty.PropertyType == typeof(string))
            {
                return textProperty.GetValue(value) as string ?? string.Empty;
            }
        }
        catch
        {
        }

        return value.ToString() ?? string.Empty;
    }

    private static string NormalizeCardRulesText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(value, @"\[(?:/?[^\]]+)\]", string.Empty);
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
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
        if (currentScreen is not NCardsViewScreen cardsViewScreen)
        {
            return null;
        }

        var backButton = cardsViewScreen.GetNodeOrNull<NButton>("BackButton");
        return backButton != null &&
            GodotObject.IsInstanceValid(backButton) &&
            backButton.IsVisibleInTree() &&
            backButton.IsEnabled
            ? backButton
            : null;
    }

    public static Creature? ResolveEnemyTarget(CombatState combatState, int targetIndex)
    {
        var enemies = combatState.Enemies.ToList();
        if (targetIndex < 0 || targetIndex >= enemies.Count)
        {
            return null;
        }

        var enemy = enemies[targetIndex];
        return enemy.IsAlive && enemy.IsHittable ? enemy : null;
    }

    public static Creature? ResolvePlayerTarget(CombatState combatState, int targetIndex)
    {
        var players = GetOrderedCombatPlayers(combatState);
        if (targetIndex < 0 || targetIndex >= players.Count)
        {
            return null;
        }

        var player = players[targetIndex];
        return player.Creature.IsAlive ? player.Creature : null;
    }

    public static bool CardRequiresTarget(CardModel card)
    {
        return RequiresIndexedCardTarget(card.TargetType);
    }

    public static bool IsCardPlayable(CardModel card)
    {
        return card.CanPlay(out _, out _) && IsCardTargetSupported(card);
    }

    public static bool IsCardTargetSupported(CardModel card)
    {
        return card.TargetType switch
        {
            TargetType.None => true,
            TargetType.Self => true,
            TargetType.AnyEnemy => true,
            TargetType.AllEnemies => true,
            TargetType.RandomEnemy => true,
            TargetType.AnyAlly => true,
            TargetType.AllAllies => true,
            _ => false
        };
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

        if (GetOpenModal() != null)
        {
            if (CanConfirmModal(currentScreen))
            {
                names.Add("confirm_modal");
            }

            if (CanDismissModal(currentScreen))
            {
                names.Add("dismiss_modal");
            }

            return names.ToArray();
        }

        if (CanEndTurn(currentScreen, combatState))
        {
            names.Add("end_turn");
        }

        if (CanPlayAnyCard(currentScreen, combatState))
        {
            names.Add("play_card");
        }

        if (CanContinueRun(currentScreen))
        {
            names.Add("continue_run");
        }

        if (CanAbandonRun(currentScreen))
        {
            names.Add("abandon_run");
        }

        if (CanOpenCharacterSelect(currentScreen))
        {
            names.Add("open_character_select");
        }

        if (CanOpenTimeline(currentScreen))
        {
            names.Add("open_timeline");
        }

        if (CanCloseMainMenuSubmenu(currentScreen))
        {
            names.Add("close_main_menu_submenu");
        }

        if (CanChooseTimelineEpoch(currentScreen))
        {
            names.Add("choose_timeline_epoch");
        }

        if (CanConfirmTimelineOverlay(currentScreen))
        {
            names.Add("confirm_timeline_overlay");
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

        if (CanCloseCardsView(currentScreen))
        {
            names.Add("close_cards_view");
        }

        if (CanConfirmSelection(currentScreen))
        {
            names.Add("confirm_selection");
        }

        if (CanProceed(currentScreen))
        {
            names.Add("proceed");
        }

        if (CanOpenChest(currentScreen))
        {
            names.Add("open_chest");
        }

        if (CanChooseTreasureRelic(currentScreen))
        {
            names.Add("choose_treasure_relic");
        }

        if (CanChooseEventOption(currentScreen))
        {
            names.Add("choose_event_option");
        }

        if (CanChooseRestOption(currentScreen))
        {
            names.Add("choose_rest_option");
        }

        if (CanOpenShopInventory(currentScreen))
        {
            names.Add("open_shop_inventory");
        }

        if (CanCloseShopInventory(currentScreen))
        {
            names.Add("close_shop_inventory");
        }

        if (CanBuyShopCard(currentScreen))
        {
            names.Add("buy_card");
        }

        if (CanBuyShopRelic(currentScreen))
        {
            names.Add("buy_relic");
        }

        if (CanBuyShopPotion(currentScreen))
        {
            names.Add("buy_potion");
        }

        if (CanRemoveCardAtShop(currentScreen))
        {
            names.Add("remove_card_at_shop");
        }

        if (CanSelectCharacter(currentScreen))
        {
            names.Add("select_character");
        }

        if (CanEmbark(currentScreen))
        {
            names.Add("embark");
        }

        if (CanUnready(currentScreen))
        {
            names.Add("unready");
        }

        if (CanHostMultiplayerLobby(currentScreen))
        {
            names.Add("host_multiplayer_lobby");
        }

        if (CanJoinMultiplayerLobby(currentScreen))
        {
            names.Add("join_multiplayer_lobby");
        }

        if (CanReadyMultiplayerLobby(currentScreen))
        {
            names.Add("ready_multiplayer_lobby");
        }

        if (CanDisconnectMultiplayerLobby(currentScreen))
        {
            names.Add("disconnect_multiplayer_lobby");
        }

        if (CanIncreaseAscension(currentScreen))
        {
            names.Add("increase_ascension");
        }

        if (CanDecreaseAscension(currentScreen))
        {
            names.Add("decrease_ascension");
        }

        if (CanUsePotion(currentScreen, combatState, runState))
        {
            names.Add("use_potion");
        }

        if (CanDiscardPotion(runState))
        {
            names.Add("discard_potion");
        }

        if (CanReturnToMainMenu(currentScreen))
        {
            names.Add("return_to_main_menu");
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
        var orbQueue = me.PlayerCombatState.OrbQueue;
        var orbs = orbQueue.Orbs.ToList();
        var connectedPlayerIds = GetConnectedPlayerIds(combatState.RunState as RunState);

        return new CombatPayload
        {
            player = new CombatPlayerPayload
            {
                current_hp = me.Creature.CurrentHp,
                max_hp = me.Creature.MaxHp,
                block = me.Creature.Block,
                energy = me.PlayerCombatState.Energy,
                stars = me.PlayerCombatState.Stars,
                focus = me.Creature.GetPowerAmount<FocusPower>(),
                powers = BuildCreaturePowerPayloads(me.Creature),
                base_orb_slots = me.BaseOrbSlotCount,
                orb_capacity = orbQueue.Capacity,
                empty_orb_slots = Math.Max(0, orbQueue.Capacity - orbs.Count),
                orbs = orbs.Select((orb, index) => BuildCombatOrbPayload(orb, index)).ToArray()
            },
            players = GetOrderedCombatPlayers(combatState)
                .Select(player => BuildCombatPlayerSummaryPayload(player, combatState, connectedPlayerIds, me.NetId))
                .ToArray(),
            hand = hand.Select((card, index) => BuildHandCardPayload(combatState, card, index)).ToArray(),
            enemies = enemies.Select((enemy, index) => BuildEnemyPayload(enemy, index)).ToArray()
        };
    }

    private static RunPayload? BuildRunPayload(IScreenContext? currentScreen, CombatState? combatState, RunState? runState)
    {
        var player = LocalContext.GetMe(runState);
        if (player == null)
        {
            return null;
        }

        var connectedPlayerIds = GetConnectedPlayerIds(runState);

        return new RunPayload
        {
            character_id = player.Character.Id.Entry,
            character_name = player.Character.Title.GetFormattedText(),
            floor = runState.TotalFloor,
            current_hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            gold = player.Gold,
            max_energy = player.MaxEnergy,
            base_orb_slots = player.BaseOrbSlotCount,
            deck = player.Deck.Cards.Select((card, index) => BuildDeckCardPayload(card, index)).ToArray(),
            relics = player.Relics.Select((relic, index) => BuildRunRelicPayload(relic, index)).ToArray(),
            players = runState!.Players
                .OrderBy(runState.GetPlayerSlotIndex)
                .Select(otherPlayer => BuildRunPlayerSummaryPayload(runState, otherPlayer, connectedPlayerIds, player.NetId))
                .ToArray(),
            potions = player.PotionSlots.Select((potion, index) =>
                BuildRunPotionPayload(currentScreen, combatState, player, potion, index)).ToArray()
        };
    }

    private static object BuildAgentViewPayload(
        string screen,
        SessionPayload session,
        string runId,
        int? turn,
        string[] availableActions,
        CombatState? combatState,
        RunState? runState,
        CombatPayload? combat,
        RunPayload? run,
        MapPayload? map,
        SelectionPayload? selection,
        CharacterSelectPayload? characterSelect,
        TimelinePayload? timeline,
        ChestPayload? chest,
        EventPayload? eventPayload,
        ShopPayload? shop,
        RestPayload? rest,
        RewardPayload? reward,
        ModalPayload? modal,
        GameOverPayload? gameOver)
    {
        var glossaryTerms = new HashSet<string>(StringComparer.Ordinal);

        return new
        {
            version = AgentViewVersion,
            screen,
            run_id = runId,
            session,
            turn,
            actions = availableActions,
            available_actions = availableActions,
            combat = BuildAgentCombatPayload(combatState, combat, glossaryTerms),
            run = BuildAgentRunPayload(combatState, runState, run, glossaryTerms),
            map = BuildAgentMapPayload(map),
            selection = BuildAgentSelectionPayload(selection, glossaryTerms),
            character_select = BuildAgentCharacterSelectPayload(characterSelect),
            timeline = BuildAgentTimelinePayload(timeline),
            chest = BuildAgentChestPayload(chest),
            @event = BuildAgentEventPayload(eventPayload),
            shop = BuildAgentShopPayload(shop, glossaryTerms),
            rest = BuildAgentRestPayload(rest),
            reward = BuildAgentRewardPayload(reward, glossaryTerms),
            modal = BuildAgentModalPayload(modal),
            game_over = BuildAgentGameOverPayload(gameOver),
            glossary = BuildAgentGlossary(glossaryTerms)
        };
    }

    private static object? BuildAgentCombatPayload(
        CombatState? combatState,
        CombatPayload? combat,
        HashSet<string> glossaryTerms)
    {
        if (combat == null)
        {
            return null;
        }

        var liveHand = LocalContext.GetMe(combatState)?.PlayerCombatState?.Hand.Cards.ToList()
            ?? new List<CardModel>();
        var playerCombatState = LocalContext.GetMe(combatState)?.PlayerCombatState;

        return new
        {
            player = new
            {
                hp = $"{combat.player.current_hp}/{combat.player.max_hp}",
                block = combat.player.block,
                energy = combat.player.energy,
                stars = combat.player.stars,
                focus = combat.player.focus,
                orbs = combat.player.orbs.Select(orb => FormatOrbLine(orb)).ToArray()
            },
            hand = combat.hand.Select(card =>
                BuildAgentHandCardPayload(
                    card,
                    card.index >= 0 && card.index < liveHand.Count ? liveHand[card.index] : null,
                    glossaryTerms)).ToArray(),
            draw = BuildAgentCardStacks(ReadCombatPileCards(playerCombatState, "DrawPile", "DrawDeck"), glossaryTerms),
            discard = BuildAgentCardStacks(ReadCombatPileCards(playerCombatState, "DiscardPile"), glossaryTerms),
            exhaust = BuildAgentCardStacks(ReadCombatPileCards(playerCombatState, "ExhaustPile"), glossaryTerms),
            enemies = combat.enemies.Select(enemy => new
            {
                i = enemy.index,
                name = enemy.name,
                hp = $"{enemy.current_hp}/{enemy.max_hp}",
                block = enemy.block,
                intent = enemy.intent,
                alive = enemy.is_alive,
                hittable = enemy.is_hittable
            }).ToArray()
        };
    }

    private static object? BuildAgentRunPayload(
        CombatState? combatState,
        RunState? runState,
        RunPayload? run,
        HashSet<string> glossaryTerms)
    {
        if (run == null)
        {
            return null;
        }

        var player = LocalContext.GetMe(runState);
        var deckCards = player?.Deck.Cards.ToArray() ?? Array.Empty<CardModel>();
        var combatPlayer = LocalContext.GetMe(combatState)?.PlayerCombatState;

        return new
        {
            character = run.character_name,
            floor = run.floor,
            hp = $"{run.current_hp}/{run.max_hp}",
            gold = run.gold,
            max_energy = run.max_energy,
            base_orb_slots = run.base_orb_slots,
            deck = deckCards.Length > 0
                ? BuildAgentCardStacks(deckCards, glossaryTerms)
                : BuildAgentCardStacks(run.deck, glossaryTerms),
            relics = run.relics
                .Select(relic => relic.is_melted ? $"{relic.name} (熔毁)" : relic.name)
                .ToArray(),
            potions = run.potions.Select(potion => new
            {
                i = potion.index,
                line = FormatPotionLine(potion),
                usable = potion.can_use,
                discard = potion.can_discard,
                target = NormalizeTargetHint(potion.target_type),
                targets = potion.valid_target_indices
            }).ToArray(),
            piles = new
            {
                draw = BuildAgentCardStacks(ReadCombatPileCards(combatPlayer, "DrawPile", "DrawDeck"), glossaryTerms),
                discard = BuildAgentCardStacks(ReadCombatPileCards(combatPlayer, "DiscardPile"), glossaryTerms),
                exhaust = BuildAgentCardStacks(ReadCombatPileCards(combatPlayer, "ExhaustPile"), glossaryTerms)
            }
        };
    }

    private static object? BuildAgentSelectionPayload(SelectionPayload? selection, HashSet<string> glossaryTerms)
    {
        if (selection == null)
        {
            return null;
        }

        CollectGlossaryTerms(glossaryTerms, selection.prompt);

        return new
        {
            kind = selection.kind,
            prompt = selection.prompt,
            min = selection.min_select,
            max = selection.max_select,
            selected = selection.selected_count,
            confirm = selection.can_confirm,
            cards = selection.cards.Select(card => BuildAgentChoiceCardPayload(card.index, card.name, card.upgraded, card.energy_cost, card.star_cost, card.costs_x, card.star_costs_x, card.rules_text, glossaryTerms)).ToArray()
        };
    }

    private static object? BuildAgentRewardPayload(RewardPayload? reward, HashSet<string> glossaryTerms)
    {
        if (reward == null)
        {
            return null;
        }

        foreach (var option in reward.rewards)
        {
            CollectGlossaryTerms(glossaryTerms, option.description);
        }

        return new
        {
            pending_card_choice = reward.pending_card_choice,
            can_proceed = reward.can_proceed,
            rewards = reward.rewards.Select(option => new
            {
                i = option.index,
                line = $"{option.reward_type}: {option.description}",
                claimable = option.claimable
            }).ToArray(),
            cards = reward.card_options.Select(card => BuildAgentChoiceCardPayload(card.index, card.name, card.upgraded, null, null, false, false, card.rules_text, glossaryTerms)).ToArray(),
            alternatives = reward.alternatives.Select(option => new
            {
                i = option.index,
                line = option.label
            }).ToArray()
        };
    }

    private static object? BuildAgentEventPayload(EventPayload? eventPayload)
    {
        if (eventPayload == null)
        {
            return null;
        }

        return new
        {
            id = eventPayload.event_id,
            title = eventPayload.title,
            finished = eventPayload.is_finished,
            options = eventPayload.options.Select(option => new
            {
                i = option.index,
                line = FormatEventOptionLine(option),
                locked = option.is_locked,
                proceed = option.is_proceed
            }).ToArray()
        };
    }

    private static object? BuildAgentShopPayload(ShopPayload? shop, HashSet<string> glossaryTerms)
    {
        if (shop == null)
        {
            return null;
        }

        return new
        {
            open = shop.is_open,
            can_open = shop.can_open,
            can_close = shop.can_close,
            cards = shop.cards.Select(card =>
                BuildAgentPricedCardPayload(
                    card.index,
                    card.name,
                    card.upgraded,
                    card.energy_cost,
                    card.star_cost,
                    card.costs_x,
                    card.star_costs_x,
                    card.rules_text,
                    card.price,
                    card.enough_gold,
                    glossaryTerms)).ToArray(),
            relics = shop.relics.Select(relic => new
            {
                i = relic.index,
                line = $"{relic.name} [{relic.rarity}] | {relic.price}g",
                affordable = relic.enough_gold,
                stocked = relic.is_stocked
            }).ToArray(),
            potions = shop.potions.Select(potion => new
            {
                i = potion.index,
                line = $"{potion.name ?? "空"}{(string.IsNullOrWhiteSpace(potion.usage) ? string.Empty : $"：{potion.usage}")} | {potion.price}g",
                affordable = potion.enough_gold,
                stocked = potion.is_stocked
            }).ToArray(),
            remove = shop.card_removal == null
                ? null
                : new
                {
                    price = shop.card_removal.price,
                    affordable = shop.card_removal.enough_gold,
                    available = shop.card_removal.available,
                    used = shop.card_removal.used
                }
        };
    }

    private static object? BuildAgentRestPayload(RestPayload? rest)
    {
        if (rest == null)
        {
            return null;
        }

        return new
        {
            options = rest.options.Select(option => new
            {
                i = option.index,
                line = string.IsNullOrWhiteSpace(option.description)
                    ? option.title
                    : $"{option.title}: {option.description}",
                enabled = option.is_enabled
            }).ToArray()
        };
    }

    private static object? BuildAgentMapPayload(MapPayload? map)
    {
        if (map == null)
        {
            return null;
        }

        return new
        {
            current = map.current_node == null ? null : $"{map.current_node.row},{map.current_node.col}",
            options = map.available_nodes.Select(node => new
            {
                i = node.index,
                line = $"{node.node_type} ({node.row},{node.col})"
            }).ToArray()
        };
    }

    private static object? BuildAgentCharacterSelectPayload(CharacterSelectPayload? characterSelect)
    {
        if (characterSelect == null)
        {
            return null;
        }

        return new
        {
            selected = characterSelect.selected_character_id,
            embark = characterSelect.can_embark,
            ascension = characterSelect.ascension,
            characters = characterSelect.characters.Select(character => new
            {
                i = character.index,
                line = character.is_random ? $"{character.name} (随机)" : character.name,
                locked = character.is_locked,
                selected = character.is_selected
            }).ToArray()
        };
    }

    private static object? BuildAgentTimelinePayload(TimelinePayload? timeline)
    {
        if (timeline == null)
        {
            return null;
        }

        return new
        {
            back = timeline.back_enabled,
            confirm = timeline.can_confirm_overlay,
            slots = timeline.slots.Select(slot => new
            {
                i = slot.index,
                line = $"{slot.title} [{slot.state}]",
                actionable = slot.is_actionable
            }).ToArray()
        };
    }

    private static object? BuildAgentChestPayload(ChestPayload? chest)
    {
        if (chest == null)
        {
            return null;
        }

        return new
        {
            opened = chest.is_opened,
            claimed = chest.has_relic_been_claimed,
            relics = chest.relic_options.Select(relic => new
            {
                i = relic.index,
                line = $"{relic.name} [{relic.rarity}]"
            }).ToArray()
        };
    }

    private static object? BuildAgentModalPayload(ModalPayload? modal)
    {
        if (modal == null)
        {
            return null;
        }

        return new
        {
            type = modal.type_name,
            confirm = modal.can_confirm,
            dismiss = modal.can_dismiss,
            confirm_label = modal.confirm_label,
            dismiss_label = modal.dismiss_label
        };
    }

    private static object? BuildAgentGameOverPayload(GameOverPayload? gameOver)
    {
        if (gameOver == null)
        {
            return null;
        }

        return new
        {
            victory = gameOver.is_victory,
            floor = gameOver.floor,
            character = gameOver.character_id,
            can_continue = gameOver.can_continue,
            can_return = gameOver.can_return_to_main_menu
        };
    }

    private static object BuildAgentHandCardPayload(
        CombatHandCardPayload card,
        CardModel? liveCard,
        HashSet<string> glossaryTerms)
    {
        var mods = GetCardModifierTags(liveCard);
        var keywords = GetGlossaryMatches(card.rules_text, mods);
        CollectGlossaryTerms(glossaryTerms, card.rules_text, mods);

        return new
        {
            i = card.index,
            line = FormatCardLine(card.name, card.upgraded, 1, card.energy_cost, card.star_cost, card.costs_x, card.star_costs_x, card.rules_text),
            playable = card.playable,
            target = card.requires_target ? NormalizeTargetHint(card.target_index_space ?? card.target_type) : null,
            targets = card.requires_target ? card.valid_target_indices : Array.Empty<int>(),
            why = card.playable ? null : card.unplayable_reason,
            keywords,
            mods
        };
    }

    private static object BuildAgentChoiceCardPayload(
        int index,
        string name,
        bool upgraded,
        int? energyCost,
        int? starCost,
        bool costsX,
        bool starCostsX,
        string rulesText,
        HashSet<string> glossaryTerms)
    {
        var keywords = GetGlossaryMatches(rulesText);
        CollectGlossaryTerms(glossaryTerms, rulesText);

        return new
        {
            i = index,
            line = FormatCardLine(name, upgraded, 1, energyCost, starCost, costsX, starCostsX, rulesText),
            keywords,
            mods = Array.Empty<string>()
        };
    }

    private static object BuildAgentPricedCardPayload(
        int index,
        string name,
        bool upgraded,
        int energyCost,
        int starCost,
        bool costsX,
        bool starCostsX,
        string rulesText,
        int price,
        bool enoughGold,
        HashSet<string> glossaryTerms)
    {
        var keywords = GetGlossaryMatches(rulesText);
        CollectGlossaryTerms(glossaryTerms, rulesText);

        return new
        {
            i = index,
            line = $"{FormatCardLine(name, upgraded, 1, energyCost, starCost, costsX, starCostsX, rulesText)} | {price}g",
            affordable = enoughGold,
            keywords,
            mods = Array.Empty<string>()
        };
    }

    private static object[] BuildAgentCardStacks(IEnumerable<CardModel> cards, HashSet<string> glossaryTerms)
    {
        var descriptors = cards
            .Select(card => BuildAgentCardDescriptor(card, glossaryTerms))
            .ToArray();

        return BuildAgentCardStacks(descriptors);
    }

    private static object[] BuildAgentCardStacks(IEnumerable<DeckCardPayload> cards, HashSet<string> glossaryTerms)
    {
        var descriptors = cards
            .Select(card => BuildAgentCardDescriptor(card, glossaryTerms))
            .ToArray();

        return BuildAgentCardStacks(descriptors);
    }

    private static object[] BuildAgentCardStacks(IEnumerable<AgentCardDescriptor> descriptors)
    {
        return descriptors
            .GroupBy(descriptor => descriptor.GroupKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var line = FormatCardLine(first.name, first.upgraded, group.Count(), first.energy_cost, first.star_cost, first.costs_x, first.star_costs_x, first.rules_text);

                return new
                {
                    line,
                    keywords = first.keywords,
                    mods = first.mods
                };
            })
            .OrderBy(item => item.line, StringComparer.Ordinal)
            .Cast<object>()
            .ToArray();
    }

    private static AgentCardDescriptor BuildAgentCardDescriptor(CardModel card, HashSet<string> glossaryTerms)
    {
        var rulesText = GetCardRulesText(card);
        var mods = GetCardModifierTags(card);
        var keywords = GetGlossaryMatches(rulesText, mods);
        CollectGlossaryTerms(glossaryTerms, rulesText, mods);

        return new AgentCardDescriptor(
            card.Title,
            card.IsUpgraded,
            card.EnergyCost.GetWithModifiers(CostModifiers.All),
            Math.Max(0, card.GetStarCostWithModifiers()),
            card.EnergyCost.CostsX,
            card.HasStarCostX,
            rulesText,
            keywords,
            mods);
    }

    private static AgentCardDescriptor BuildAgentCardDescriptor(DeckCardPayload card, HashSet<string> glossaryTerms)
    {
        var keywords = GetGlossaryMatches(card.rules_text);
        CollectGlossaryTerms(glossaryTerms, card.rules_text);

        return new AgentCardDescriptor(
            card.name,
            card.upgraded,
            card.energy_cost,
            card.star_cost,
            card.costs_x,
            card.star_costs_x,
            card.rules_text,
            keywords,
            Array.Empty<string>());
    }

    private static string FormatCardLine(
        string name,
        bool upgraded,
        int count,
        int? energyCost,
        int? starCost,
        bool costsX,
        bool starCostsX,
        string rulesText)
    {
        var title = upgraded && !name.EndsWith("+", StringComparison.Ordinal) ? $"{name}+" : name;
        if (count > 1)
        {
            title = $"{title}*{count}";
        }

        var cost = FormatCardCost(energyCost, starCost, costsX, starCostsX);
        var prefix = string.IsNullOrWhiteSpace(cost) ? title : $"{title} [{cost}]";
        return string.IsNullOrWhiteSpace(rulesText)
            ? prefix
            : $"{prefix}：{rulesText}";
    }

    private static string FormatCardCost(int? energyCost, int? starCost, bool costsX, bool starCostsX)
    {
        var parts = new List<string>();
        if (costsX)
        {
            parts.Add("X费");
        }
        else if (energyCost.HasValue)
        {
            parts.Add($"{Math.Max(0, energyCost.Value)}费");
        }

        if (starCostsX)
        {
            parts.Add("X星");
        }
        else if (starCost.HasValue && starCost.Value > 0)
        {
            parts.Add($"{starCost.Value}星");
        }

        return string.Join("/", parts);
    }

    private static string FormatOrbLine(CombatOrbPayload orb)
    {
        return $"{orb.name} 被动{orb.passive_value}/激发{orb.evoke_value}";
    }

    private static string FormatPotionLine(RunPotionPayload potion)
    {
        if (!potion.occupied)
        {
            return $"{potion.index}: 空";
        }

        var usage = string.IsNullOrWhiteSpace(potion.usage) ? string.Empty : $"：{potion.usage}";
        return $"{potion.index}: {potion.name}{usage}";
    }

    private static string FormatEventOptionLine(EventOptionPayload option)
    {
        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(option.title))
        {
            segments.Add(option.title);
        }

        if (!string.IsNullOrWhiteSpace(option.description))
        {
            segments.Add(option.description);
        }

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(option.text_key))
        {
            segments.Add(option.text_key);
        }

        return string.Join(" | ", segments);
    }

    private static CardModel[] ReadCombatPileCards(object? playerCombatState, params string[] memberNames)
    {
        if (playerCombatState == null)
        {
            return Array.Empty<CardModel>();
        }

        foreach (var memberName in memberNames)
        {
            var memberValue = TryGetMemberValue(playerCombatState, memberName);
            var cards = ExtractCards(memberValue);
            if (cards.Length > 0 || memberValue != null)
            {
                return cards;
            }
        }

        return Array.Empty<CardModel>();
    }

    private static CardModel[] ExtractCards(object? value)
    {
        return ExtractCards(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static CardModel[] ExtractCards(object? value, HashSet<object> visited)
    {
        if (value == null)
        {
            return Array.Empty<CardModel>();
        }

        if (!visited.Add(value))
        {
            return Array.Empty<CardModel>();
        }

        if (value is IEnumerable enumerable and not string)
        {
            var cards = new List<CardModel>();
            foreach (var item in enumerable)
            {
                if (item is CardModel card)
                {
                    cards.Add(card);
                }
            }

            if (cards.Count > 0)
            {
                return cards.ToArray();
            }
        }

        foreach (var memberName in new[] { "Cards", "CardModels", "Entries", "List" })
        {
            var nested = TryGetMemberValue(value, memberName);
            if (nested == null)
            {
                continue;
            }

            var cards = ExtractCards(nested, visited);
            if (cards.Length > 0)
            {
                return cards;
            }
        }

        return Array.Empty<CardModel>();
    }

    private static object? TryGetMemberValue(object instance, string memberName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        try
        {
            var property = instance.GetType().GetProperty(memberName, flags);
            if (property != null)
            {
                return property.GetValue(instance);
            }

            var field = instance.GetType().GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(instance);
            }
        }
        catch
        {
        }

        return null;
    }

    private static string[] GetCardModifierTags(CardModel? card)
    {
        if (card == null)
        {
            return Array.Empty<string>();
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var memberName in new[]
        {
            "Enchantments",
            "Enchants",
            "Modifiers",
            "ModifierIds",
            "Affixes",
            "Augments",
            "Keywords"
        })
        {
            var memberValue = TryGetMemberValue(card, memberName);
            foreach (var token in ExtractModifierTokens(memberValue))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                values.Add(NormalizeCardRulesText(token));
            }
        }

        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> ExtractModifierTokens(object? value)
    {
        if (value == null)
        {
            yield break;
        }

        if (value is string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }

            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                foreach (var token in ExtractModifierTokens(item))
                {
                    yield return token;
                }
            }

            yield break;
        }

        foreach (var memberName in new[] { "Title", "Name", "Keyword", "Text", "Description", "Label" })
        {
            var memberValue = TryGetMemberValue(value, memberName);
            if (TryCoerceText(memberValue) is { Length: > 0 } memberText)
            {
                yield return memberText;
            }
        }

        var idValue = TryGetMemberValue(value, "Id");
        if (idValue != null)
        {
            var entryValue = TryGetMemberValue(idValue, "Entry");
            if (entryValue is string entryText && !string.IsNullOrWhiteSpace(entryText))
            {
                yield return entryText;
            }
        }
    }

    private static string[] GetGlossaryMatches(string text, params string[][] modifierGroups)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (keyword, _) in AgentKeywordDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(text) && text.Contains(keyword, StringComparison.Ordinal))
            {
                values.Add(keyword);
            }

            foreach (var modifierGroup in modifierGroups)
            {
                if (modifierGroup.Any(modifier => modifier.Contains(keyword, StringComparison.Ordinal)))
                {
                    values.Add(keyword);
                }
            }
        }

        return values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static void CollectGlossaryTerms(HashSet<string> glossaryTerms, string? text, params string[][] modifierGroups)
    {
        if (glossaryTerms.Count >= AgentKeywordDefinitions.Length)
        {
            return;
        }

        foreach (var keyword in GetGlossaryMatches(text ?? string.Empty, modifierGroups))
        {
            glossaryTerms.Add(keyword);
        }
    }

    private static Dictionary<string, string> BuildAgentGlossary(HashSet<string> glossaryTerms)
    {
        var glossary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (keyword, definition) in AgentKeywordDefinitions)
        {
            if (glossaryTerms.Contains(keyword))
            {
                glossary[keyword] = definition;
            }
        }

        return glossary;
    }

    private static string? NormalizeTargetHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("enemy", StringComparison.Ordinal))
        {
            return "enemy";
        }

        if (normalized.Contains("player", StringComparison.Ordinal) || normalized.Contains("self", StringComparison.Ordinal))
        {
            return "player";
        }

        return normalized;
    }

    private static readonly (string keyword, string definition)[] AgentKeywordDefinitions =
    {
        ("力量", "每层力量通常使攻击额外造成 1 点伤害。"),
        ("敏捷", "每层敏捷通常使获得的格挡额外增加 1。"),
        ("易伤", "易伤单位会承受更多攻击伤害。"),
        ("虚弱", "虚弱单位造成的攻击伤害会降低。"),
        ("脆弱", "脆弱单位获得的格挡会减少。"),
        ("格挡", "格挡会优先抵消即将受到的伤害。"),
        ("消耗", "消耗牌打出后会移出本场战斗。"),
        ("保留", "保留牌在回合结束时不会被弃掉。"),
        ("中毒", "中毒会在回合结束时造成等量生命损失，然后层数减少。"),
        ("眩晕", "眩晕通常是无法主动打出的状态牌。"),
        ("灼伤", "灼伤通常会在手中或结算时带来额外伤害。"),
        ("虚空", "虚空通常会在抽到时消耗能量或妨碍出牌。"),
        ("力量流失", "力量流失会临时降低力量。"),
        ("集中", "集中通常会强化充能球的被动与激发效果。"),
        ("球位", "球位决定你能同时容纳多少个充能球。"),
        ("附魔", "附魔是卡牌附着的额外词条或效果层。"),
        ("灌注", "灌注表示卡牌带有额外附着效果。"),
        ("临时", "临时牌通常会在回合结束或打出后离开牌组流转。")
    };

    private static MultiplayerPayload? BuildMultiplayerPayload(IScreenContext? currentScreen, RunState? runState)
    {
        var multiplayerTestScene = GetMultiplayerTestScene();
        var multiplayerTestLobby = multiplayerTestScene != null ? GetMultiplayerTestLobby(multiplayerTestScene) : null;
        if (multiplayerTestLobby != null)
        {
            return new MultiplayerPayload
            {
                is_multiplayer = true,
                net_game_type = multiplayerTestLobby.NetService.Type.ToString(),
                local_player_id = NetIdToString(multiplayerTestLobby.LocalPlayer.id),
                player_count = multiplayerTestLobby.Players.Count,
                connected_player_ids = multiplayerTestLobby.Players
                    .OrderBy(player => player.slotId)
                    .Select(player => NetIdToString(player.id))
                    .ToArray()
            };
        }

        var characterSelectScreen = GetCharacterSelectScreen(currentScreen);
        if (characterSelectScreen != null)
        {
            var lobby = characterSelectScreen.Lobby;
            if (!lobby.NetService.Type.IsMultiplayer())
            {
                return null;
            }

            return new MultiplayerPayload
            {
                is_multiplayer = true,
                net_game_type = lobby.NetService.Type.ToString(),
                local_player_id = NetIdToString(lobby.LocalPlayer.id),
                player_count = lobby.Players.Count,
                connected_player_ids = lobby.Players
                    .OrderBy(player => player.slotId)
                    .Select(player => NetIdToString(player.id))
                    .ToArray()
            };
        }

        if (runState == null || !RunManager.Instance.NetService.Type.IsMultiplayer())
        {
            return null;
        }

        var localPlayer = GetLocalPlayer(runState);
        return new MultiplayerPayload
        {
            is_multiplayer = true,
            net_game_type = RunManager.Instance.NetService.Type.ToString(),
            local_player_id = localPlayer != null ? NetIdToString(localPlayer.NetId) : null,
            player_count = runState.Players.Count,
            connected_player_ids = GetConnectedPlayerIds(runState)
                .OrderBy(id => runState.GetPlayerSlotIndex(id))
                .Select(NetIdToString)
                .ToArray()
        };
    }

    private static MultiplayerLobbyPayload? BuildMultiplayerLobbyPayload(IScreenContext? currentScreen)
    {
        var scene = GetMultiplayerTestScene();
        if (scene == null)
        {
            return null;
        }

        var lobby = GetMultiplayerTestLobby(scene);
        var selectedCharacterId = lobby?.LocalPlayer.character?.Id.Entry
            ?? GetMultiplayerTestCharacterPaginator(scene)?.Character?.Id.Entry;
        var localPlayerId = lobby != null ? NetIdToString(lobby.LocalPlayer.id) : null;

        return new MultiplayerLobbyPayload
        {
            net_game_type = lobby?.NetService.Type.ToString() ?? NetGameType.Singleplayer.ToString(),
            join_host = GetMultiplayerLobbyJoinHost(),
            join_port = GetMultiplayerLobbyJoinPort(),
            local_net_id_hint = NetIdToString(GetMultiplayerLobbyJoinNetIdHint()),
            has_lobby = lobby != null,
            is_host = lobby?.NetService.Type == NetGameType.Host,
            is_client = lobby?.NetService.Type == NetGameType.Client,
            local_ready = lobby?.LocalPlayer.isReady ?? false,
            can_host = CanHostMultiplayerLobby(currentScreen),
            can_join = CanJoinMultiplayerLobby(currentScreen),
            can_ready = CanReadyMultiplayerLobby(currentScreen),
            can_disconnect = CanDisconnectMultiplayerLobby(currentScreen),
            can_unready = CanUnready(currentScreen),
            selected_character_id = selectedCharacterId,
            player_count = lobby?.Players.Count ?? 0,
            max_players = lobby != null
                ? lobby.MaxPlayers > 0 ? lobby.MaxPlayers : lobby.Players.Count
                : 4,
            players = lobby?.Players
                .OrderBy(player => player.slotId)
                .Select(player => BuildCharacterSelectPlayerPayload(player, lobby.LocalPlayer.id))
                .ToArray() ?? Array.Empty<CharacterSelectPlayerPayload>(),
            characters = GetMultiplayerLobbyCharacters()
                .Select((character, index) => new CharacterSelectOptionPayload
                {
                    index = index,
                    character_id = character.Id.Entry,
                    name = character.Title.GetFormattedText(),
                    is_locked = false,
                    is_selected = selectedCharacterId == character.Id.Entry,
                    is_random = false
                })
                .ToArray()
        };
    }

    private static MapPayload? BuildMapPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (!TryGetMapScreen(currentScreen, runState, out var mapScreen))
        {
            return null;
        }

        var visibleNodes = FindDescendants<NMapPoint>(mapScreen!)
            .Where(node => GodotObject.IsInstanceValid(node))
            .GroupBy(node => node.Point.coord)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => node.GlobalPosition.Y)
                    .ThenBy(node => node.GlobalPosition.X)
                    .First());

        var availableNodes = visibleNodes.Values
            .Where(node => node.IsEnabled)
            .OrderBy(node => node.Point.coord.row)
            .ThenBy(node => node.Point.coord.col)
            .ToArray();
        var availableCoords = new HashSet<MapCoord>(availableNodes.Select(node => node.Point.coord));
        var visitedCoords = new HashSet<MapCoord>(runState!.VisitedMapCoords);
        var allMapPoints = GetAllMapPoints(runState.Map);

        return new MapPayload
        {
            current_node = BuildMapCoordPayload(runState!.CurrentMapCoord),
            is_travel_enabled = mapScreen!.IsTravelEnabled,
            is_traveling = mapScreen.IsTraveling,
            map_generation_count = RunManager.Instance.MapSelectionSynchronizer.MapGenerationCount,
            rows = runState.Map.GetRowCount(),
            cols = runState.Map.GetColumnCount(),
            starting_node = BuildMapCoordPayload(runState.Map.StartingMapPoint.coord),
            boss_node = BuildMapCoordPayload(runState.Map.BossMapPoint.coord),
            second_boss_node = BuildMapCoordPayload(runState.Map.SecondBossMapPoint?.coord),
            nodes = allMapPoints
                .Select(point => BuildMapGraphNodePayload(
                    point,
                    visibleNodes.TryGetValue(point.coord, out var mapNode) ? mapNode : null,
                    visitedCoords,
                    availableCoords,
                    runState.CurrentMapCoord,
                    runState.Map.StartingMapPoint.coord,
                    runState.Map.BossMapPoint.coord,
                    runState.Map.SecondBossMapPoint?.coord))
                .ToArray(),
            available_nodes = availableNodes.Select((node, index) => BuildMapNodePayload(node, index)).ToArray()
        };
    }

    private static SelectionPayload? BuildSelectionPayload(IScreenContext? currentScreen)
    {
        var cards = GetDeckSelectionOptions(currentScreen);
        if (cards.Count == 0)
        {
            return null;
        }

        var combatHandSelection = TryGetCombatHandSelectionMetadata(currentScreen, out _, out var metadata)
            ? metadata
            : default;

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
            min_select = combatHandSelection.MinSelect,
            max_select = combatHandSelection.MaxSelect,
            selected_count = combatHandSelection.SelectedCount,
            requires_confirmation = combatHandSelection.RequiresConfirmation,
            can_confirm = combatHandSelection.CanConfirm,
            cards = cards.Select((holder, index) => BuildSelectionCardPayload(holder.CardModel!, index)).ToArray()
        };
    }

    private static CharacterSelectPayload? BuildCharacterSelectPayload(IScreenContext? currentScreen)
    {
        var screen = GetCharacterSelectScreen(currentScreen);
        if (screen == null)
        {
            return null;
        }

        var buttons = GetCharacterSelectButtons(currentScreen);
        try
        {
            var lobby = screen.Lobby;
            var localPlayer = lobby.LocalPlayer;
            var waitingPanel = screen.GetNodeOrNull<Control>("ReadyAndWaitingPanel");
            var selectedCharacterId = localPlayer.character?.Id.Entry;

            return new CharacterSelectPayload
            {
                selected_character_id = selectedCharacterId,
                is_multiplayer = lobby.NetService.Type.IsMultiplayer(),
                net_game_type = lobby.NetService.Type.ToString(),
                can_embark = CanEmbark(currentScreen),
                can_unready = CanUnready(currentScreen),
                can_increase_ascension = CanIncreaseAscension(currentScreen),
                can_decrease_ascension = CanDecreaseAscension(currentScreen),
                local_ready = localPlayer.isReady,
                is_waiting_for_players = waitingPanel?.Visible ?? false,
                player_count = lobby.Players.Count,
                max_players = lobby.MaxPlayers > 0 ? lobby.MaxPlayers : lobby.Players.Count,
                ascension = lobby.Ascension,
                max_ascension = lobby.MaxAscension,
                seed = lobby.Seed,
                modifier_ids = lobby.Modifiers.Select(modifier => modifier.Id.Entry).ToArray(),
                players = lobby.Players
                    .OrderBy(player => player.slotId)
                    .Select(player => BuildCharacterSelectPlayerPayload(player, localPlayer.id))
                    .ToArray(),
                characters = buttons.Select((button, index) => new CharacterSelectOptionPayload
                {
                    index = index,
                    character_id = button.Character.Id.Entry,
                    name = button.Character.Title.GetFormattedText(),
                    is_locked = button.IsLocked,
                    is_selected = button.IsRandom
                        ? selectedCharacterId == button.Character.Id.Entry
                        : selectedCharacterId == button.Character.Id.Entry,
                    is_random = button.IsRandom
                }).ToArray()
            };
        }
        catch
        {
            return new CharacterSelectPayload
            {
                players = Array.Empty<CharacterSelectPlayerPayload>(),
                characters = buttons.Select((button, index) => new CharacterSelectOptionPayload
                {
                    index = index,
                    character_id = button.Character.Id.Entry,
                    name = button.Character.Title.GetFormattedText(),
                    is_locked = button.IsLocked,
                    is_selected = false,
                    is_random = button.IsRandom
                }).ToArray()
            };
        }
    }

    private static EventPayload? BuildEventPayload(IScreenContext? currentScreen)
    {
        if (currentScreen is not NEventRoom)
        {
            return null;
        }

        try
        {
            var eventModel = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            if (eventModel == null)
            {
                return null;
            }

            var options = new List<EventOptionPayload>();

            if (eventModel.IsFinished)
            {
                // Mirror NEventRoom.SetOptions(): synthesize a Proceed option
                options.Add(new EventOptionPayload
                {
                    index = 0,
                    text_key = "PROCEED",
                    title = "Proceed",
                    description = "",
                    is_locked = false,
                    is_proceed = true
                });
            }
            else
            {
                var currentOptions = eventModel.CurrentOptions;
                for (int i = 0; i < currentOptions.Count; i++)
                {
                    var opt = currentOptions[i];
                    options.Add(new EventOptionPayload
                    {
                        index = i,
                        text_key = SafeReadString(() => opt.TextKey),
                        title = SafeReadString(() => opt.Title?.GetFormattedText()),
                        description = SafeReadString(() => opt.Description?.GetFormattedText()),
                        is_locked = SafeReadBool(() => opt.IsLocked),
                        is_proceed = SafeReadBool(() => opt.IsProceed),
                        will_kill_player = GetEventOptionWillKillPlayer(eventModel, opt),
                        has_relic_preview = GetReflectedProperty(opt, "Relic") != null
                    });
                }
            }

            return new EventPayload
            {
                event_id = SafeReadString(() => eventModel.Id?.Entry, "unknown"),
                title = SafeReadString(() => eventModel.Title?.GetFormattedText()),
                description = SafeReadString(() => eventModel.Description?.GetFormattedText()),
                is_finished = SafeReadBool(() => eventModel.IsFinished),
                options = options.ToArray()
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"[STS2AIAgent] Failed to build event payload on screen {currentScreen.GetType().FullName}: {ex}");
            return null;
        }
    }

    private static RestPayload? BuildRestPayload(IScreenContext? currentScreen)
    {
        if (currentScreen is not NRestSiteRoom)
        {
            return null;
        }

        try
        {
            var options = RunManager.Instance.RestSiteSynchronizer.GetLocalOptions();
            if (options == null)
            {
                return new RestPayload
                {
                    options = Array.Empty<RestOptionPayload>()
                };
            }

            return new RestPayload
            {
                options = options.Select((opt, i) => new RestOptionPayload
                {
                    index = i,
                    option_id = opt.OptionId ?? "unknown",
                    title = opt.Title?.GetFormattedText() ?? "",
                    description = opt.Description?.GetFormattedText() ?? "",
                    is_enabled = opt.IsEnabled
                }).ToArray()
            };
        }
        catch
        {
            return null;
        }
    }

    private static ShopPayload? BuildShopPayload(IScreenContext? currentScreen)
    {
        var merchantRoom = GetMerchantRoom(currentScreen);
        var inventoryScreen = GetMerchantInventoryScreen(currentScreen);
        var inventory = inventoryScreen?.Inventory ?? merchantRoom?.Inventory?.Inventory;

        if (merchantRoom == null && inventoryScreen == null)
        {
            return null;
        }

        if (inventory == null)
        {
            return new ShopPayload
            {
                is_open = inventoryScreen?.IsOpen ?? false,
                can_open = CanOpenShopInventory(currentScreen),
                can_close = CanCloseShopInventory(currentScreen),
                cards = Array.Empty<ShopCardPayload>(),
                relics = Array.Empty<ShopRelicPayload>(),
                potions = Array.Empty<ShopPotionPayload>(),
                card_removal = null
            };
        }

        var cards = inventory.CharacterCardEntries
            .Select((entry, index) => BuildShopCardPayload(entry, index, "character"))
            .Concat(inventory.ColorlessCardEntries.Select((entry, index) =>
                BuildShopCardPayload(entry, inventory.CharacterCardEntries.Count + index, "colorless")))
            .ToArray();

        return new ShopPayload
        {
            is_open = inventoryScreen?.IsOpen ?? false,
            can_open = CanOpenShopInventory(currentScreen),
            can_close = CanCloseShopInventory(currentScreen),
            cards = cards,
            relics = inventory.RelicEntries.Select((entry, index) => BuildShopRelicPayload(entry, index)).ToArray(),
            potions = inventory.PotionEntries.Select((entry, index) => BuildShopPotionPayload(entry, index, inventory.Player)).ToArray(),
            card_removal = BuildShopCardRemovalPayload(inventory.CardRemovalEntry)
        };
    }

    private static TimelinePayload? BuildTimelinePayload(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        if (timelineScreen == null)
        {
            return null;
        }

        var slots = GetTimelineSlots(currentScreen)
            .Select((slot, index) => new TimelineSlotPayload
            {
                index = index,
                epoch_id = slot.model.Id,
                title = slot.model.Title.GetFormattedText() ?? slot.model.Id,
                state = slot.State.ToString().ToLowerInvariant(),
                is_actionable = slot.State is EpochSlotState.Obtained or EpochSlotState.Complete
            })
            .ToArray();

        return new TimelinePayload
        {
            back_enabled = GetTimelineBackButton(currentScreen)?.IsEnabled == true,
            inspect_open = GetTimelineInspectScreen(currentScreen)?.Visible == true,
            unlock_screen_open = GetTimelineUnlockScreen(currentScreen) != null,
            can_choose_epoch = CanChooseTimelineEpoch(currentScreen),
            can_confirm_overlay = CanConfirmTimelineOverlay(currentScreen),
            slots = slots
        };
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

    private static ModalPayload? BuildModalPayload(IScreenContext? currentScreen)
    {
        var modal = GetOpenModal();
        if (modal is not Node modalNode)
        {
            return null;
        }

        var confirmButton = GetModalConfirmButton(currentScreen);
        var cancelButton = GetModalCancelButton(currentScreen);

        return new ModalPayload
        {
            type_name = modal.GetType().Name,
            underlying_screen = currentScreen is Node node && ReferenceEquals(node, modalNode)
                ? ResolveUnderlyingScreen(modalNode)
                : null,
            can_confirm = confirmButton != null,
            can_dismiss = cancelButton != null,
            confirm_label = GetButtonLabel(confirmButton),
            dismiss_label = GetButtonLabel(cancelButton)
        };
    }

    private static GameOverPayload? BuildGameOverPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (currentScreen is not NGameOverScreen screen)
        {
            return null;
        }

        var player = LocalContext.GetMe(runState);
        var continueButton = screen.GetNodeOrNull<NButton>("%ContinueButton");
        var mainMenuButton = screen.GetNodeOrNull<NButton>("%MainMenuButton");
        var history = RunManager.Instance.History;

        return new GameOverPayload
        {
            is_victory = history?.Win ?? (runState?.CurrentRoom?.IsVictoryRoom ?? false),
            floor = runState?.TotalFloor,
            character_id = player?.Character.Id.Entry,
            can_continue = continueButton?.IsEnabled ?? false,
            can_return_to_main_menu = true,
            showing_summary = mainMenuButton?.Visible == true || mainMenuButton?.IsEnabled == true
        };
    }

    private static CombatHandCardPayload BuildHandCardPayload(CombatState combatState, CardModel card, int index)
    {
        card.CanPlay(out var reason, out _);
        var targetSupported = IsCardTargetSupported(card);
        var targetIndexSpace = GetCardTargetIndexSpace(card);
        var validTargetIndices = GetCardTargetIndices(combatState, card);

        return new CombatHandCardPayload
        {
            index = index,
            card_id = card.Id.Entry,
            name = card.Title,
            upgraded = card.IsUpgraded,
            target_type = card.TargetType.ToString(),
            requires_target = CardRequiresTarget(card),
            target_index_space = targetIndexSpace,
            valid_target_indices = validTargetIndices,
            costs_x = card.EnergyCost.CostsX,
            star_costs_x = card.HasStarCostX,
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers()),
            rules_text = GetCardRulesText(card),
            playable = targetSupported && reason == UnplayableReason.None,
            unplayable_reason = targetSupported
                ? GetUnplayableReasonCode(reason)
                : "unsupported_target_type"
        };
    }

    private static CombatEnemyPayload BuildEnemyPayload(Creature enemy, int index)
    {
        var moveId = enemy.Monster?.NextMove?.Id;
        var intents = BuildEnemyIntentPayloads(enemy);

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
            powers = BuildCreaturePowerPayloads(enemy),
            intent = moveId,
            move_id = moveId,
            intents = intents
        };
    }

    private static CombatPowerPayload[] BuildCreaturePowerPayloads(Creature creature)
    {
        var powersValue = creature.GetType().GetProperty("Powers")?.GetValue(creature);
        if (powersValue is not System.Collections.IEnumerable powersEnumerable)
        {
            return Array.Empty<CombatPowerPayload>();
        }

        var result = new List<CombatPowerPayload>();
        var index = 0;

        foreach (var power in powersEnumerable)
        {
            if (power == null)
            {
                continue;
            }

            var powerType = power.GetType();
            var idEntry = SafeReadString(() =>
            {
                var idValue = powerType.GetProperty("Id")?.GetValue(power);
                if (idValue == null)
                {
                    return string.Empty;
                }

                return idValue.GetType().GetProperty("Entry")?.GetValue(idValue)?.ToString();
            });

            var title = SafeReadString(() =>
            {
                var titleValue = powerType.GetProperty("Title")?.GetValue(power);
                if (titleValue == null)
                {
                    return string.Empty;
                }

                return titleValue.GetType().GetMethod("GetFormattedText")?.Invoke(titleValue, null)?.ToString();
            });

            var amount = GetReflectedNullableIntProperty(power, "Amount");

            var isDebuff = string.Equals(
                GetReflectedProperty(power, "TypeForCurrentAmount")?.ToString()
                    ?? GetReflectedProperty(power, "Type")?.ToString(),
                "Debuff",
                StringComparison.Ordinal);

            result.Add(new CombatPowerPayload
            {
                index = index,
                power_id = string.IsNullOrWhiteSpace(idEntry) ? "unknown_power" : idEntry,
                name = string.IsNullOrWhiteSpace(title) ? idEntry : title,
                amount = amount,
                is_debuff = isDebuff
            });
            index += 1;
        }

        return result.ToArray();
    }

    private static CombatEnemyIntentPayload[] BuildEnemyIntentPayloads(Creature enemy)
    {
        var nextMove = enemy.Monster?.NextMove;
        if (nextMove == null)
        {
            return Array.Empty<CombatEnemyIntentPayload>();
        }

        var targets = enemy.CombatState?.Players
            .Select(player => player.Creature)
            .ToArray() ?? Array.Empty<Creature>();

        return nextMove.Intents
            .Select((intent, index) => BuildEnemyIntentPayload(intent, enemy, targets, index))
            .ToArray();
    }

    private static CombatEnemyIntentPayload BuildEnemyIntentPayload(
        AbstractIntent intent,
        Creature owner,
        Creature[] targets,
        int index)
    {
        int? damage = null;
        int? hits = null;
        int? totalDamage = null;
        int? statusCardCount = null;

        if (intent is AttackIntent attackIntent)
        {
            damage = SafeReadNullableInt(() => attackIntent.GetSingleDamage(targets, owner));
            hits = SafeReadNullableInt(() => Math.Max(1, attackIntent.Repeats));
            totalDamage = SafeReadNullableInt(() => attackIntent.GetTotalDamage(targets, owner));
        }

        if (intent is StatusIntent statusIntent)
        {
            statusCardCount = SafeReadNullableInt(() => statusIntent.CardCount);
        }

        var label = SafeReadString(() => intent.GetIntentLabel(targets, owner).GetFormattedText(), string.Empty);

        return new CombatEnemyIntentPayload
        {
            index = index,
            intent_type = intent.IntentType.ToString(),
            label = string.IsNullOrWhiteSpace(label) ? null : label,
            damage = damage,
            hits = hits,
            total_damage = totalDamage,
            status_card_count = statusCardCount
        };
    }

    private static int? SafeReadNullableInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static CombatOrbPayload BuildCombatOrbPayload(OrbModel orb, int slotIndex)
    {
        return new CombatOrbPayload
        {
            slot_index = slotIndex,
            orb_id = orb.Id.Entry,
            name = orb.Title.GetFormattedText(),
            passive_value = orb.PassiveVal,
            evoke_value = orb.EvokeVal,
            is_front = slotIndex == 0
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

    private static MapGraphNodePayload BuildMapGraphNodePayload(
        MapPoint point,
        NMapPoint? mapNode,
        HashSet<MapCoord> visitedCoords,
        HashSet<MapCoord> availableCoords,
        MapCoord? currentCoord,
        MapCoord startCoord,
        MapCoord bossCoord,
        MapCoord? secondBossCoord)
    {
        return new MapGraphNodePayload
        {
            row = point.coord.row,
            col = point.coord.col,
            node_type = point.PointType.ToString(),
            state = ResolveMapPointState(point.coord, mapNode, visitedCoords, availableCoords, currentCoord),
            visited = visitedCoords.Contains(point.coord),
            is_current = currentCoord.HasValue && currentCoord.Value == point.coord,
            is_available = availableCoords.Contains(point.coord),
            is_start = point.coord == startCoord,
            is_boss = point.coord == bossCoord,
            is_second_boss = secondBossCoord.HasValue && point.coord == secondBossCoord.Value,
            parents = point.parents
                .OrderBy(parent => parent.coord.row)
                .ThenBy(parent => parent.coord.col)
                .Select(parent => BuildMapCoordPayload(parent.coord)!)
                .ToArray(),
            children = point.Children
                .OrderBy(child => child.coord.row)
                .ThenBy(child => child.coord.col)
                .Select(child => BuildMapCoordPayload(child.coord)!)
                .ToArray()
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

    private static IReadOnlyList<MapPoint> GetAllMapPoints(ActMap map)
    {
        var points = new Dictionary<MapCoord, MapPoint>();

        void AddPoint(MapPoint? point)
        {
            if (point == null)
            {
                return;
            }

            points[point.coord] = point;
        }

        foreach (var point in map.GetAllMapPoints())
        {
            AddPoint(point);
        }

        AddPoint(map.StartingMapPoint);
        AddPoint(map.BossMapPoint);
        AddPoint(map.SecondBossMapPoint);

        return points.Values
            .OrderBy(point => point.coord.row)
            .ThenBy(point => point.coord.col)
            .ToArray();
    }

    private static string ResolveMapPointState(
        MapCoord coord,
        NMapPoint? mapNode,
        HashSet<MapCoord> visitedCoords,
        HashSet<MapCoord> availableCoords,
        MapCoord? currentCoord)
    {
        if (mapNode != null)
        {
            return mapNode.State.ToString();
        }

        if (availableCoords.Contains(coord))
        {
            return MapPointState.Travelable.ToString();
        }

        if (visitedCoords.Contains(coord) || (currentCoord.HasValue && currentCoord.Value == coord))
        {
            return MapPointState.Traveled.ToString();
        }

        return MapPointState.Untravelable.ToString();
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
            upgraded = card?.IsUpgraded ?? false,
            rules_text = GetCardRulesText(card)
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

    private static RunRelicPayload BuildRunRelicPayload(RelicModel relic, int index)
    {
        return new RunRelicPayload
        {
            index = index,
            relic_id = relic.Id.Entry,
            name = relic.Title.GetFormattedText(),
            description = GetDynamicFormattedTextProperty(relic, "DynamicDescription", "Description"),
            stack = GetReflectedNullableIntProperty(relic, "Amount"),
            is_melted = relic.IsMelted
        };
    }

    private static RunPotionPayload BuildRunPotionPayload(
        IScreenContext? currentScreen,
        CombatState? combatState,
        Player player,
        PotionModel? potion,
        int index)
    {
        var requiresTarget = potion != null && PotionRequiresTarget(combatState, potion);
        var targetIndexSpace = potion != null ? GetPotionTargetIndexSpace(combatState, potion) : null;
        var validTargetIndices = potion != null ? GetPotionTargetIndices(combatState, potion) : Array.Empty<int>();

        return new RunPotionPayload
        {
            index = index,
            potion_id = potion?.Id.Entry,
            name = potion?.Title.GetFormattedText(),
            description = potion != null ? GetDynamicFormattedTextProperty(potion, "DynamicDescription", "Description") : null,
            rarity = potion != null ? GetReflectedStringProperty(potion, "Rarity") : null,
            occupied = potion != null,
            usage = potion?.Usage.ToString(),
            target_type = potion?.TargetType.ToString(),
            is_queued = potion?.IsQueued ?? false,
            requires_target = requiresTarget,
            target_index_space = targetIndexSpace,
            valid_target_indices = validTargetIndices,
            can_use = IsPotionUsable(currentScreen, combatState, player, potion),
            can_discard = IsPotionDiscardable(player, potion)
        };
    }

    private static object? GetReflectedProperty(object target, string propertyName)
    {
        try
        {
            return target.GetType().GetProperty(propertyName)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetReflectedStringProperty(object target, string propertyName)
    {
        var value = GetReflectedProperty(target, propertyName);
        return value?.ToString();
    }

    private static string? GetReflectedFormattedTextProperty(object target, string propertyName)
    {
        var value = GetReflectedProperty(target, propertyName);
        if (value == null)
        {
            return null;
        }

        try
        {
            return value.GetType().GetMethod("GetFormattedText")?.Invoke(value, null)?.ToString();
        }
        catch
        {
            return value.ToString();
        }
    }

    private static string? GetDynamicFormattedTextProperty(object target, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetReflectedFormattedTextProperty(target, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int? GetReflectedNullableIntProperty(object target, string propertyName)
    {
        try
        {
            var value = GetReflectedProperty(target, propertyName);
            return value == null ? null : Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool GetReflectedBoolProperty(object target, string propertyName)
    {
        try
        {
            var value = GetReflectedProperty(target, propertyName);
            return value != null && Convert.ToBoolean(value);
        }
        catch
        {
            return false;
        }
    }

    private static bool GetEventOptionWillKillPlayer(object eventModel, object option)
    {
        try
        {
            var owner = GetReflectedProperty(eventModel, "Owner");
            var willKillPlayer = GetReflectedProperty(option, "WillKillPlayer") as Delegate;
            if (owner == null || willKillPlayer == null)
            {
                return false;
            }

            return willKillPlayer.DynamicInvoke(owner) as bool? ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static CharacterSelectPlayerPayload BuildCharacterSelectPlayerPayload(LobbyPlayer player, ulong localPlayerId)
    {
        return new CharacterSelectPlayerPayload
        {
            player_id = NetIdToString(player.id),
            slot_index = player.slotId,
            is_local = player.id == localPlayerId,
            character_id = player.character?.Id.Entry,
            character_name = player.character?.Title.GetFormattedText(),
            is_ready = player.isReady,
            max_multiplayer_ascension_unlocked = player.maxMultiplayerAscensionUnlocked
        };
    }

    private static RunPlayerSummaryPayload BuildRunPlayerSummaryPayload(
        RunState runState,
        Player player,
        IReadOnlyCollection<ulong> connectedPlayerIds,
        ulong localPlayerId)
    {
        return new RunPlayerSummaryPayload
        {
            player_id = NetIdToString(player.NetId),
            slot_index = runState.GetPlayerSlotIndex(player),
            is_local = player.NetId == localPlayerId,
            is_connected = connectedPlayerIds.Contains(player.NetId),
            character_id = player.Character.Id.Entry,
            character_name = player.Character.Title.GetFormattedText(),
            current_hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            gold = player.Gold,
            is_alive = player.Creature.IsAlive
        };
    }

    private static CombatPlayerSummaryPayload BuildCombatPlayerSummaryPayload(
        Player player,
        CombatState combatState,
        IReadOnlyCollection<ulong> connectedPlayerIds,
        ulong localPlayerId)
    {
        return new CombatPlayerSummaryPayload
        {
            player_id = NetIdToString(player.NetId),
            slot_index = combatState.RunState is RunState runState ? runState.GetPlayerSlotIndex(player) : 0,
            is_local = player.NetId == localPlayerId,
            is_connected = connectedPlayerIds.Contains(player.NetId),
            character_id = player.Character.Id.Entry,
            character_name = player.Character.Title.GetFormattedText(),
            current_hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            block = player.Creature.Block,
            energy = player.PlayerCombatState?.Energy ?? 0,
            stars = player.PlayerCombatState?.Stars ?? 0,
            focus = player.Creature.GetPowerAmount<FocusPower>(),
            is_alive = player.Creature.IsAlive
        };
    }

    private static string? GetCardTargetIndexSpace(CardModel card)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => "enemies",
            TargetType.AnyAlly => "players",
            _ => null
        };
    }

    private static string? GetPotionTargetIndexSpace(CombatState? combatState, PotionModel potion)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => "enemies",
            TargetType.AnyPlayer when PotionRequiresExplicitPlayerSelection(combatState, potion) => "players",
            TargetType.AnyAlly => "players",
            _ => null
        };
    }

    private static int[] GetCardTargetIndices(CombatState combatState, CardModel card)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => GetTargetableEnemyIndices(combatState),
            TargetType.AnyAlly => GetTargetablePlayerIndices(combatState, card.Owner, allowSelf: false),
            _ => Array.Empty<int>()
        };
    }

    private static int[] GetPotionTargetIndices(CombatState? combatState, PotionModel potion)
    {
        if (combatState == null)
        {
            return Array.Empty<int>();
        }

        return potion.TargetType switch
        {
            TargetType.AnyEnemy => GetTargetableEnemyIndices(combatState),
            TargetType.AnyPlayer when PotionRequiresExplicitPlayerSelection(combatState, potion) => GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: true),
            TargetType.AnyAlly => GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: false),
            _ => Array.Empty<int>()
        };
    }

    private static ShopCardPayload BuildShopCardPayload(MerchantCardEntry entry, int index, string category)
    {
        var card = entry.CreationResult?.Card;
        return new ShopCardPayload
        {
            index = index,
            category = category,
            card_id = card?.Id.Entry ?? string.Empty,
            name = card?.Title ?? string.Empty,
            upgraded = card?.IsUpgraded ?? false,
            card_type = card?.Type.ToString() ?? string.Empty,
            rarity = card?.Rarity.ToString() ?? string.Empty,
            costs_x = card?.EnergyCost.CostsX ?? false,
            star_costs_x = card?.HasStarCostX ?? false,
            energy_cost = card?.EnergyCost.GetWithModifiers(CostModifiers.All) ?? 0,
            star_cost = card != null ? Math.Max(0, card.GetStarCostWithModifiers()) : 0,
            rules_text = GetCardRulesText(card),
            price = entry.IsStocked ? entry.Cost : 0,
            on_sale = entry.IsOnSale,
            is_stocked = entry.IsStocked,
            enough_gold = entry.IsStocked && entry.EnoughGold
        };
    }

    private static ShopRelicPayload BuildShopRelicPayload(MerchantRelicEntry entry, int index)
    {
        var relic = entry.Model;
        return new ShopRelicPayload
        {
            index = index,
            relic_id = relic?.Id.Entry ?? string.Empty,
            name = relic?.Title.GetFormattedText() ?? string.Empty,
            rarity = relic?.Rarity.ToString() ?? string.Empty,
            price = entry.IsStocked ? entry.Cost : 0,
            is_stocked = entry.IsStocked,
            enough_gold = entry.IsStocked && entry.EnoughGold
        };
    }

    private static ShopPotionPayload BuildShopPotionPayload(MerchantPotionEntry entry, int index, Player? player)
    {
        var potion = entry.Model;
        return new ShopPotionPayload
        {
            index = index,
            potion_id = potion?.Id.Entry,
            name = potion?.Title.GetFormattedText(),
            rarity = potion?.Rarity.ToString(),
            usage = potion?.Usage.ToString(),
            price = entry.IsStocked ? entry.Cost : 0,
            is_stocked = entry.IsStocked,
            enough_gold = CanPurchaseShopPotion(player, entry)
        };
    }

    private static bool CanPurchaseShopPotion(Player? player, MerchantPotionEntry entry)
    {
        return entry.IsStocked &&
            entry.EnoughGold &&
            player?.PotionSlots.Any(slot => slot == null) == true;
    }

    private static ShopCardRemovalPayload? BuildShopCardRemovalPayload(MerchantCardRemovalEntry? entry)
    {
        if (entry == null)
        {
            return null;
        }

        return new ShopCardRemovalPayload
        {
            price = entry.IsStocked ? entry.Cost : 0,
            available = entry.IsStocked,
            used = entry.Used,
            enough_gold = entry.IsStocked && entry.EnoughGold
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
            costs_x = card.EnergyCost.CostsX,
            star_costs_x = card.HasStarCostX,
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers()),
            rules_text = GetCardRulesText(card)
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
            rarity = card.Rarity.ToString(),
            costs_x = card.EnergyCost.CostsX,
            star_costs_x = card.HasStarCostX,
            energy_cost = card.EnergyCost.GetWithModifiers(CostModifiers.All),
            star_cost = Math.Max(0, card.GetStarCostWithModifiers()),
            rules_text = GetCardRulesText(card)
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

    private static bool IsPotionUsable(IScreenContext? currentScreen, CombatState? combatState, Player player, PotionModel? potion)
    {
        if (potion == null || !IsPotionDiscardable(player, potion))
        {
            return false;
        }

        if (!potion.PassesCustomUsabilityCheck || !IsPotionTargetSupported(combatState, potion))
        {
            return false;
        }

        return potion.Usage switch
        {
            PotionUsage.AnyTime => true,
            PotionUsage.CombatOnly => CanUseCombatActions(currentScreen, combatState, out _, out _),
            _ => false
        };
    }

    private static bool IsPotionDiscardable(Player player, PotionModel? potion)
    {
        return potion != null &&
            !potion.IsQueued &&
            !potion.Owner.Creature.IsDead &&
            player.CanRemovePotions;
    }

    public static bool PotionRequiresTarget(CombatState? combatState, PotionModel potion)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => true,
            TargetType.AnyPlayer => PotionRequiresExplicitPlayerSelection(combatState, potion),
            TargetType.AnyAlly => combatState != null && GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: false).Length > 0,
            _ => false
        };
    }

    private static bool IsPotionTargetSupported(CombatState? combatState, PotionModel potion)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => GetTargetableEnemyIndices(combatState).Length > 0,
            TargetType.AnyPlayer => PotionRequiresExplicitPlayerSelection(combatState, potion)
                ? GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: true).Length > 0
                : true,
            TargetType.AnyAlly => GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: false).Length > 0,
            TargetType.TargetedNoCreature => true,
            _ => true
        };
    }

    private static bool PotionRequiresExplicitPlayerSelection(CombatState? combatState, PotionModel potion)
    {
        return combatState != null &&
            CombatManager.Instance.IsInProgress &&
            potion.Owner.RunState.Players.Count > 1 &&
            combatState.PlayerCreatures.Count(creature => creature.IsAlive) > 1;
    }

    private static bool RequiresIndexedCardTarget(TargetType targetType)
    {
        return targetType == TargetType.AnyEnemy || targetType == TargetType.AnyAlly;
    }

    public static int[] GetTargetableEnemyIndices(CombatState? combatState)
    {
        if (combatState == null)
        {
            return Array.Empty<int>();
        }

        return combatState.Enemies
            .Select((enemy, index) => new { enemy, index })
            .Where(entry => entry.enemy.IsAlive && entry.enemy.IsHittable)
            .Select(entry => entry.index)
            .ToArray();
    }

    public static int[] GetTargetablePlayerIndices(CombatState? combatState, Player owner, bool allowSelf)
    {
        if (combatState == null)
        {
            return Array.Empty<int>();
        }

        return GetOrderedCombatPlayers(combatState)
            .Select((player, index) => new { player, index })
            .Where(entry => entry.player.Creature.IsAlive)
            .Where(entry => allowSelf || entry.player.NetId != owner.NetId)
            .Select(entry => entry.index)
            .ToArray();
    }

    private static IReadOnlyList<Player> GetOrderedCombatPlayers(CombatState combatState)
    {
        return combatState.Players
            .OrderBy(player => combatState.RunState is RunState runState ? runState.GetPlayerSlotIndex(player) : 0)
            .ToArray();
    }

    private static NMerchantRoom? GetMerchantRoom(IScreenContext? currentScreen)
    {
        return currentScreen switch
        {
            NMerchantRoom room => room,
            NMerchantInventory => NMerchantRoom.Instance,
            _ => null
        };
    }

    private static NMerchantInventory? GetMerchantInventoryScreen(IScreenContext? currentScreen)
    {
        return currentScreen switch
        {
            NMerchantInventory inventory => inventory,
            NMerchantRoom room when room.Inventory != null => room.Inventory,
            _ => null
        };
    }

    public static MerchantInventory? GetMerchantInventory(IScreenContext? currentScreen)
    {
        return GetMerchantInventoryScreen(currentScreen)?.Inventory ?? GetMerchantRoom(currentScreen)?.Inventory?.Inventory;
    }

    public static IReadOnlyList<MerchantCardEntry> GetMerchantCardEntries(IScreenContext? currentScreen)
    {
        var inventory = GetMerchantInventory(currentScreen);
        if (inventory == null)
        {
            return Array.Empty<MerchantCardEntry>();
        }

        return inventory.CharacterCardEntries.Concat(inventory.ColorlessCardEntries).ToArray();
    }

    public static IReadOnlyList<MerchantRelicEntry> GetMerchantRelicEntries(IScreenContext? currentScreen)
    {
        return GetMerchantInventory(currentScreen)?.RelicEntries?.ToArray() ?? Array.Empty<MerchantRelicEntry>();
    }

    public static IReadOnlyList<MerchantPotionEntry> GetMerchantPotionEntries(IScreenContext? currentScreen)
    {
        return GetMerchantInventory(currentScreen)?.PotionEntries?.ToArray() ?? Array.Empty<MerchantPotionEntry>();
    }

    public static MerchantCardRemovalEntry? GetMerchantCardRemovalEntry(IScreenContext? currentScreen)
    {
        return GetMerchantInventory(currentScreen)?.CardRemovalEntry;
    }

    public static NCharacterSelectScreen? GetCharacterSelectScreen(IScreenContext? currentScreen)
    {
        return currentScreen as NCharacterSelectScreen;
    }

    public static NMultiplayerTest? GetMultiplayerTestScene()
    {
        var currentScene = NGame.Instance?.RootSceneContainer?.CurrentScene;
        return currentScene is NMultiplayerTest multiplayerTest && multiplayerTest.IsVisibleInTree()
            ? multiplayerTest
            : null;
    }

    public static StartRunLobby? GetMultiplayerTestLobby(NMultiplayerTest scene)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(NMultiplayerTest).GetField("_lobby", flags);
        return field?.GetValue(scene) as StartRunLobby;
    }

    public static NMultiplayerTestCharacterPaginator? GetMultiplayerTestCharacterPaginator(NMultiplayerTest scene)
    {
        return scene.GetNodeOrNull<NMultiplayerTestCharacterPaginator>("CharacterChooser");
    }

    public static string GetMultiplayerLobbyJoinHost()
    {
        var raw = System.Environment.GetEnvironmentVariable("STS2_MULTIPLAYER_HOST_IP");
        return string.IsNullOrWhiteSpace(raw) ? "127.0.0.1" : raw.Trim();
    }

    public static int GetMultiplayerLobbyJoinPort()
    {
        return 33771;
    }

    public static ulong GetMultiplayerLobbyJoinNetIdHint()
    {
        var raw = System.Environment.GetEnvironmentVariable("STS2_MULTIPLAYER_NET_ID");
        if (!string.IsNullOrWhiteSpace(raw) && ulong.TryParse(raw.Trim(), out var parsed))
        {
            return parsed;
        }

        return (ulong)System.Environment.ProcessId;
    }

    public static CharacterModel[] GetMultiplayerLobbyCharacters()
    {
        return
        [
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Regent>(),
            ModelDb.Character<Necrobinder>(),
            ModelDb.Character<Defect>()
        ];
    }

    public static IReadOnlyList<NCharacterSelectButton> GetCharacterSelectButtons(IScreenContext? currentScreen)
    {
        var screen = GetCharacterSelectScreen(currentScreen);
        if (screen == null)
        {
            return Array.Empty<NCharacterSelectButton>();
        }

        return FindDescendants<NCharacterSelectButton>(screen)
            .Where(node => GodotObject.IsInstanceValid(node))
            .OrderBy(node => node.GlobalPosition.Y)
            .ThenBy(node => node.GlobalPosition.X)
            .ToArray();
    }

    public static NConfirmButton? GetCharacterEmbarkButton(IScreenContext? currentScreen)
    {
        return GetCharacterSelectScreen(currentScreen)?.GetNodeOrNull<NConfirmButton>("ConfirmButton");
    }

    public static NBackButton? GetCharacterUnreadyButton(IScreenContext? currentScreen)
    {
        return GetCharacterSelectScreen(currentScreen)?.GetNodeOrNull<NBackButton>("UnreadyButton");
    }

    public static NMainMenuTextButton? GetMainMenuContinueButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/ContinueButton");
    }

    public static NMainMenuTextButton? GetMainMenuAbandonRunButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/AbandonRunButton");
    }

    public static NMainMenuTextButton? GetMainMenuSingleplayerButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/SingleplayerButton");
    }

    public static NMainMenuTextButton? GetMainMenuTimelineButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/TimelineButton");
    }

    public static NTimelineScreen? GetTimelineScreen(IScreenContext? currentScreen)
    {
        if (currentScreen is NTimelineScreen timelineScreen && timelineScreen.IsVisibleInTree())
        {
            return timelineScreen;
        }

        return null;
    }

    public static IReadOnlyList<NEpochSlot> GetTimelineSlots(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        if (timelineScreen == null)
        {
            return Array.Empty<NEpochSlot>();
        }

        return FindDescendants<NEpochSlot>(timelineScreen)
            .Where(slot => slot.IsVisibleInTree() && slot.model != null && slot.State != EpochSlotState.NotObtained)
            .OrderBy(slot => slot.GlobalPosition.X)
            .ThenBy(slot => slot.GlobalPosition.Y)
            .ToArray();
    }

    public static NEpochInspectScreen? GetTimelineInspectScreen(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        var inspectScreen = timelineScreen?.GetNodeOrNull<NEpochInspectScreen>("%EpochInspectScreen");
        return inspectScreen?.Visible == true ? inspectScreen : null;
    }

    public static NUnlockScreen? GetTimelineUnlockScreen(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        if (timelineScreen == null)
        {
            return null;
        }

        return FindDescendants<NUnlockScreen>(timelineScreen)
            .FirstOrDefault(screen => screen.IsVisibleInTree());
    }

    public static NButton? GetTimelineBackButton(IScreenContext? currentScreen)
    {
        return GetTimelineScreen(currentScreen)?.GetNodeOrNull<NButton>("BackButton");
    }

    public static NButton? GetTimelineInspectCloseButton(IScreenContext? currentScreen)
    {
        return GetTimelineInspectScreen(currentScreen)?.GetNodeOrNull<NButton>("%CloseButton");
    }

    public static NButton? GetTimelineUnlockConfirmButton(IScreenContext? currentScreen)
    {
        return GetTimelineUnlockScreen(currentScreen)?.GetNodeOrNull<NButton>("ConfirmButton");
    }

    public static NMainMenuSubmenuStack? GetMainMenuSubmenuStack(Node? node)
    {
        var current = node;
        while (current != null)
        {
            if (current is NMainMenuSubmenuStack submenuStack)
            {
                return submenuStack;
            }

            current = current.GetParent();
        }

        return null;
    }

    public static IScreenContext? GetOpenModal()
    {
        return NModalContainer.Instance?.OpenModal;
    }

    public static NButton? GetModalConfirmButton(IScreenContext? currentScreen)
    {
        return FindModalButton("VerticalPopup/YesButton", "ConfirmButton", "%ConfirmButton", "%Confirm", "%AcknowledgeButton");
    }

    public static NButton? GetModalCancelButton(IScreenContext? currentScreen)
    {
        return FindModalButton("VerticalPopup/NoButton", "CancelButton", "%CancelButton", "%BackButton");
    }

    private static NButton? FindModalButton(params string[] paths)
    {
        var modal = GetOpenModal();
        if (modal is not Node modalNode)
        {
            return null;
        }

        foreach (var path in paths)
        {
            var button = modalNode.GetNodeOrNull<NButton>(path);
            if (button != null && GodotObject.IsInstanceValid(button) && button.IsEnabled && button.IsVisibleInTree())
            {
                return button;
            }
        }

        return null;
    }

    private static string? ResolveUnderlyingScreen(Node modalNode)
    {
        var parent = modalNode.GetParent();
        while (parent != null)
        {
            if (parent is IScreenContext screenContext && !ReferenceEquals(parent, modalNode))
            {
                return ResolveNonModalScreen(screenContext);
            }

            parent = parent.GetParent();
        }

        return null;
    }

    private static string ResolveNonModalScreen(IScreenContext? currentScreen)
    {
        if (currentScreen != null &&
            TryGetCombatHandSelection(currentScreen, out _))
        {
            return "CARD_SELECTION";
        }

        if (currentScreen is NCardsViewScreen)
        {
            return "CARDS_VIEW";
        }

        if (currentScreen is Node rootNode &&
            GetVisibleGridCardHolders(rootNode).Count > 0)
        {
            return "CARD_SELECTION";
        }

        if (GetMultiplayerTestScene() != null)
        {
            return "MULTIPLAYER_LOBBY";
        }

        return currentScreen switch
        {
            NGameOverScreen => "GAME_OVER",
            NCardRewardSelectionScreen => "REWARD",
            NChooseACardSelectionScreen => "CARD_SELECTION",
            NDeckCardSelectScreen or NDeckUpgradeSelectScreen or NDeckTransformSelectScreen or NDeckEnchantSelectScreen => "CARD_SELECTION",
            NCardGridSelectionScreen => "CARD_SELECTION",
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

    private static string? GetButtonLabel(NButton? button)
    {
        if (button == null)
        {
            return null;
        }

        return button.GetNodeOrNull<MegaLabel>("Label")?.Text ?? button.Name.ToString();
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

    private static IReadOnlyList<NGridCardHolder> GetVisibleGridCardHolders(Node root)
    {
        return FindDescendants<NGridCardHolder>(root)
            .Where(node => GodotObject.IsInstanceValid(node) && node.IsVisibleInTree() && node.CardModel != null)
            .OrderBy(node => node.GlobalPosition.Y)
            .ThenBy(node => node.GlobalPosition.X)
            .ToArray();
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

    private static bool CanAdjustAscension(IScreenContext? currentScreen, int delta)
    {
        var screen = GetCharacterSelectScreen(currentScreen);
        if (screen == null)
        {
            return false;
        }

        var lobby = screen.Lobby;
        if (lobby.NetService.Type == NetGameType.Client || lobby.LocalPlayer.isReady)
        {
            return false;
        }

        var nextAscension = lobby.Ascension + delta;
        return nextAscension >= 0 && nextAscension <= lobby.MaxAscension;
    }

    private static IReadOnlyCollection<ulong> GetConnectedPlayerIds(RunState? runState)
    {
        if (runState == null)
        {
            return Array.Empty<ulong>();
        }

        var connectedPlayerIds = RunManager.Instance.RunLobby?.ConnectedPlayerIds;
        if (connectedPlayerIds != null && connectedPlayerIds.Count > 0)
        {
            return connectedPlayerIds;
        }

        return runState.Players.Select(player => player.NetId).ToArray();
    }

    private static string NetIdToString(ulong netId)
    {
        return netId.ToString();
    }
}

internal sealed class GameStatePayload
{
    public int state_version { get; init; }

    public string run_id { get; init; } = "run_unknown";

    public string screen { get; init; } = "UNKNOWN";

    public SessionPayload session { get; init; } = new();

    public bool in_combat { get; init; }

    public int? turn { get; init; }

    public string[] available_actions { get; init; } = Array.Empty<string>();

    public CombatPayload? combat { get; init; }

    public RunPayload? run { get; init; }

    public MultiplayerPayload? multiplayer { get; init; }

    public MultiplayerLobbyPayload? multiplayer_lobby { get; init; }

    public MapPayload? map { get; init; }

    public SelectionPayload? selection { get; init; }

    public CharacterSelectPayload? character_select { get; init; }

    public TimelinePayload? timeline { get; init; }

    public ChestPayload? chest { get; init; }

    public EventPayload? @event { get; init; }

    public ShopPayload? shop { get; init; }

    public RestPayload? rest { get; init; }

    public RewardPayload? reward { get; init; }

    public ModalPayload? modal { get; init; }

    public GameOverPayload? game_over { get; init; }

    public object? agent_view { get; init; }
}

internal sealed class SessionPayload
{
    public string mode { get; init; } = "singleplayer";

    public string phase { get; init; } = "menu";

    public string control_scope { get; init; } = "local_player";
}

internal sealed class AvailableActionsPayload
{
    public string screen { get; init; } = "UNKNOWN";

    public ActionDescriptor[] actions { get; init; } = Array.Empty<ActionDescriptor>();
}

internal sealed class CombatPayload
{
    public CombatPlayerPayload player { get; init; } = new();

    public CombatPlayerSummaryPayload[] players { get; init; } = Array.Empty<CombatPlayerSummaryPayload>();

    public CombatHandCardPayload[] hand { get; init; } = Array.Empty<CombatHandCardPayload>();

    public CombatEnemyPayload[] enemies { get; init; } = Array.Empty<CombatEnemyPayload>();
}

internal sealed class RunPayload
{
    public string character_id { get; init; } = string.Empty;

    public string character_name { get; init; } = string.Empty;

    public int floor { get; init; }

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int gold { get; init; }

    public int max_energy { get; init; }

    public int base_orb_slots { get; init; }

    public DeckCardPayload[] deck { get; init; } = Array.Empty<DeckCardPayload>();

    public RunRelicPayload[] relics { get; init; } = Array.Empty<RunRelicPayload>();

    public RunPlayerSummaryPayload[] players { get; init; } = Array.Empty<RunPlayerSummaryPayload>();

    public RunPotionPayload[] potions { get; init; } = Array.Empty<RunPotionPayload>();
}

internal sealed class MultiplayerPayload
{
    public bool is_multiplayer { get; init; }

    public string net_game_type { get; init; } = string.Empty;

    public string? local_player_id { get; init; }

    public int player_count { get; init; }

    public string[] connected_player_ids { get; init; } = Array.Empty<string>();
}

internal sealed class MultiplayerLobbyPayload
{
    public string net_game_type { get; init; } = string.Empty;

    public string join_host { get; init; } = "127.0.0.1";

    public int join_port { get; init; }

    public string? local_net_id_hint { get; init; }

    public bool has_lobby { get; init; }

    public bool is_host { get; init; }

    public bool is_client { get; init; }

    public bool local_ready { get; init; }

    public bool can_host { get; init; }

    public bool can_join { get; init; }

    public bool can_ready { get; init; }

    public bool can_disconnect { get; init; }

    public bool can_unready { get; init; }

    public string? selected_character_id { get; init; }

    public int player_count { get; init; }

    public int max_players { get; init; }

    public CharacterSelectPlayerPayload[] players { get; init; } = Array.Empty<CharacterSelectPlayerPayload>();

    public CharacterSelectOptionPayload[] characters { get; init; } = Array.Empty<CharacterSelectOptionPayload>();
}

internal sealed class MapPayload
{
    public MapCoordPayload? current_node { get; init; }

    public bool is_travel_enabled { get; init; }

    public bool is_traveling { get; init; }

    public int map_generation_count { get; init; }

    public int rows { get; init; }

    public int cols { get; init; }

    public MapCoordPayload? starting_node { get; init; }

    public MapCoordPayload? boss_node { get; init; }

    public MapCoordPayload? second_boss_node { get; init; }

    public MapGraphNodePayload[] nodes { get; init; } = Array.Empty<MapGraphNodePayload>();

    public MapNodePayload[] available_nodes { get; init; } = Array.Empty<MapNodePayload>();
}

internal sealed class SelectionPayload
{
    public string kind { get; init; } = string.Empty;

    public string prompt { get; init; } = string.Empty;

    public int min_select { get; init; } = 1;

    public int max_select { get; init; } = 1;

    public int selected_count { get; init; }

    public bool requires_confirmation { get; init; }

    public bool can_confirm { get; init; }

    public SelectionCardPayload[] cards { get; init; } = Array.Empty<SelectionCardPayload>();
}

internal readonly record struct CombatHandSelectionMetadata(
    int MinSelect,
    int MaxSelect,
    int SelectedCount,
    bool RequiresConfirmation,
    bool CanConfirm);

internal sealed class CharacterSelectPayload
{
    public string? selected_character_id { get; init; }

    public bool is_multiplayer { get; init; }

    public string net_game_type { get; init; } = string.Empty;

    public bool can_embark { get; init; }

    public bool can_unready { get; init; }

    public bool can_increase_ascension { get; init; }

    public bool can_decrease_ascension { get; init; }

    public bool local_ready { get; init; }

    public bool is_waiting_for_players { get; init; }

    public int player_count { get; init; }

    public int max_players { get; init; }

    public int ascension { get; init; }

    public int max_ascension { get; init; }

    public string? seed { get; init; }

    public string[] modifier_ids { get; init; } = Array.Empty<string>();

    public CharacterSelectPlayerPayload[] players { get; init; } = Array.Empty<CharacterSelectPlayerPayload>();

    public CharacterSelectOptionPayload[] characters { get; init; } = Array.Empty<CharacterSelectOptionPayload>();
}

internal sealed class CharacterSelectPlayerPayload
{
    public string player_id { get; init; } = string.Empty;

    public int slot_index { get; init; }

    public bool is_local { get; init; }

    public string? character_id { get; init; }

    public string? character_name { get; init; }

    public bool is_ready { get; init; }

    public int max_multiplayer_ascension_unlocked { get; init; }
}

internal sealed class CharacterSelectOptionPayload
{
    public int index { get; init; }

    public string character_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool is_locked { get; init; }

    public bool is_selected { get; init; }

    public bool is_random { get; init; }
}

internal sealed class TimelinePayload
{
    public bool back_enabled { get; init; }

    public bool inspect_open { get; init; }

    public bool unlock_screen_open { get; init; }

    public bool can_choose_epoch { get; init; }

    public bool can_confirm_overlay { get; init; }

    public TimelineSlotPayload[] slots { get; init; } = Array.Empty<TimelineSlotPayload>();
}

internal sealed class TimelineSlotPayload
{
    public int index { get; init; }

    public string epoch_id { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string state { get; init; } = string.Empty;

    public bool is_actionable { get; init; }
}

internal sealed class ChestPayload
{
    public bool is_opened { get; init; }

    public bool has_relic_been_claimed { get; init; }

    public ChestRelicOptionPayload[] relic_options { get; init; } = Array.Empty<ChestRelicOptionPayload>();
}

internal sealed class ChestRelicOptionPayload
{
    public int index { get; init; }

    public string relic_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;
}

internal sealed class EventPayload
{
    public string event_id { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool is_finished { get; init; }

    public EventOptionPayload[] options { get; init; } = Array.Empty<EventOptionPayload>();
}

internal sealed class EventOptionPayload
{
    public int index { get; init; }

    public string text_key { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool is_locked { get; init; }

    public bool is_proceed { get; init; }

    public bool will_kill_player { get; init; }

    public bool has_relic_preview { get; init; }
}

internal sealed class RestPayload
{
    public RestOptionPayload[] options { get; init; } = Array.Empty<RestOptionPayload>();
}

internal sealed class RestOptionPayload
{
    public int index { get; init; }

    public string option_id { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool is_enabled { get; init; }
}

internal sealed class ShopPayload
{
    public bool is_open { get; init; }

    public bool can_open { get; init; }

    public bool can_close { get; init; }

    public ShopCardPayload[] cards { get; init; } = Array.Empty<ShopCardPayload>();

    public ShopRelicPayload[] relics { get; init; } = Array.Empty<ShopRelicPayload>();

    public ShopPotionPayload[] potions { get; init; } = Array.Empty<ShopPotionPayload>();

    public ShopCardRemovalPayload? card_removal { get; init; }
}

internal sealed class ShopCardPayload
{
    public int index { get; init; }

    public string category { get; init; } = string.Empty;

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;

    public int price { get; init; }

    public bool on_sale { get; init; }

    public bool is_stocked { get; init; }

    public bool enough_gold { get; init; }
}

internal sealed class ShopRelicPayload
{
    public int index { get; init; }

    public string relic_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public int price { get; init; }

    public bool is_stocked { get; init; }

    public bool enough_gold { get; init; }
}

internal sealed class ShopPotionPayload
{
    public int index { get; init; }

    public string? potion_id { get; init; }

    public string? name { get; init; }

    public string? rarity { get; init; }

    public string? usage { get; init; }

    public int price { get; init; }

    public bool is_stocked { get; init; }

    public bool enough_gold { get; init; }
}

internal sealed class ShopCardRemovalPayload
{
    public int price { get; init; }

    public bool available { get; init; }

    public bool used { get; init; }

    public bool enough_gold { get; init; }
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

internal sealed class MapGraphNodePayload
{
    public int row { get; init; }

    public int col { get; init; }

    public string node_type { get; init; } = string.Empty;

    public string state { get; init; } = string.Empty;

    public bool visited { get; init; }

    public bool is_current { get; init; }

    public bool is_available { get; init; }

    public bool is_start { get; init; }

    public bool is_boss { get; init; }

    public bool is_second_boss { get; init; }

    public MapCoordPayload[] parents { get; init; } = Array.Empty<MapCoordPayload>();

    public MapCoordPayload[] children { get; init; } = Array.Empty<MapCoordPayload>();
}

internal sealed class CombatPlayerPayload
{
    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int block { get; init; }

    public int energy { get; init; }

    public int stars { get; init; }

    public int focus { get; init; }

    public CombatPowerPayload[] powers { get; init; } = Array.Empty<CombatPowerPayload>();

    public int base_orb_slots { get; init; }

    public int orb_capacity { get; init; }

    public int empty_orb_slots { get; init; }

    public CombatOrbPayload[] orbs { get; init; } = Array.Empty<CombatOrbPayload>();
}

internal sealed class CombatPlayerSummaryPayload
{
    public string player_id { get; init; } = string.Empty;

    public int slot_index { get; init; }

    public bool is_local { get; init; }

    public bool is_connected { get; init; }

    public string character_id { get; init; } = string.Empty;

    public string character_name { get; init; } = string.Empty;

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int block { get; init; }

    public int energy { get; init; }

    public int stars { get; init; }

    public int focus { get; init; }

    public bool is_alive { get; init; }
}

internal sealed class RunPlayerSummaryPayload
{
    public string player_id { get; init; } = string.Empty;

    public int slot_index { get; init; }

    public bool is_local { get; init; }

    public bool is_connected { get; init; }

    public string character_id { get; init; } = string.Empty;

    public string character_name { get; init; } = string.Empty;

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int gold { get; init; }

    public bool is_alive { get; init; }
}

internal sealed class CombatOrbPayload
{
    public int slot_index { get; init; }

    public string orb_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public decimal passive_value { get; init; }

    public decimal evoke_value { get; init; }

    public bool is_front { get; init; }
}

internal sealed class CombatHandCardPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string target_type { get; init; } = string.Empty;

    public bool requires_target { get; init; }

    public string? target_index_space { get; init; }

    public int[] valid_target_indices { get; init; } = Array.Empty<int>();

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;

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

    public CombatPowerPayload[] powers { get; init; } = Array.Empty<CombatPowerPayload>();

    public string? intent { get; init; }

    public string? move_id { get; init; }

    public CombatEnemyIntentPayload[] intents { get; init; } = Array.Empty<CombatEnemyIntentPayload>();
}

internal sealed class CombatEnemyIntentPayload
{
    public int index { get; init; }

    public string intent_type { get; init; } = string.Empty;

    public string? label { get; init; }

    public int? damage { get; init; }

    public int? hits { get; init; }

    public int? total_damage { get; init; }

    public int? status_card_count { get; init; }
}

internal sealed class CombatPowerPayload
{
    public int index { get; init; }

    public string power_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public int? amount { get; init; }

    public bool is_debuff { get; init; }
}

internal sealed class RewardPayload
{
    public bool pending_card_choice { get; init; }

    public bool can_proceed { get; init; }

    public RewardOptionPayload[] rewards { get; init; } = Array.Empty<RewardOptionPayload>();

    public RewardCardOptionPayload[] card_options { get; init; } = Array.Empty<RewardCardOptionPayload>();

    public RewardAlternativePayload[] alternatives { get; init; } = Array.Empty<RewardAlternativePayload>();
}

internal sealed class ModalPayload
{
    public string type_name { get; init; } = string.Empty;

    public string? underlying_screen { get; init; }

    public bool can_confirm { get; init; }

    public bool can_dismiss { get; init; }

    public string? confirm_label { get; init; }

    public string? dismiss_label { get; init; }
}

internal sealed class GameOverPayload
{
    public bool is_victory { get; init; }

    public int? floor { get; init; }

    public string? character_id { get; init; }

    public bool can_continue { get; init; }

    public bool can_return_to_main_menu { get; init; }

    public bool showing_summary { get; init; }
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

    public string rules_text { get; init; } = string.Empty;
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

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;
}

internal sealed class SelectionCardPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;
}

internal sealed class RunRelicPayload
{
    public int index { get; init; }

    public string relic_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public string? description { get; init; }

    public int? stack { get; init; }

    public bool is_melted { get; init; }
}

internal sealed class RunPotionPayload
{
    public int index { get; init; }

    public string? potion_id { get; init; }

    public string? name { get; init; }

    public string? description { get; init; }

    public string? rarity { get; init; }

    public bool occupied { get; init; }

    public string? usage { get; init; }

    public string? target_type { get; init; }

    public bool is_queued { get; init; }

    public bool requires_target { get; init; }

    public string? target_index_space { get; init; }

    public int[] valid_target_indices { get; init; } = Array.Empty<int>();

    public bool can_use { get; init; }

    public bool can_discard { get; init; }
}

internal readonly record struct AgentCardDescriptor(
    string name,
    bool upgraded,
    int energy_cost,
    int star_cost,
    bool costs_x,
    bool star_costs_x,
    string rules_text,
    string[] keywords,
    string[] mods)
{
    public string GroupKey =>
        string.Join(
            "\u001f",
            name,
            upgraded ? "1" : "0",
            energy_cost.ToString(),
            star_cost.ToString(),
            costs_x ? "1" : "0",
            star_costs_x ? "1" : "0",
            rules_text,
            string.Join("\u001e", mods));
}

internal sealed class ActionDescriptor
{
    public string name { get; init; } = string.Empty;

    public bool requires_target { get; init; }

    public bool requires_index { get; init; }
}
