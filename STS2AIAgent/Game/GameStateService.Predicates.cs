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

    public static bool CanEndTurn(
        IScreenContext? currentScreen,
        CombatState? combatState,
        bool requireButtonReady = true,
        CombatActionGate? combatActionGate = null)
    {
        if (!CanUseCombatActions(currentScreen, combatState, out _, out var combatRoom, combatActionGate))
        {
            return false;
        }

        if (CombatManager.Instance.IsPlayerReadyToEndTurn(GetLocalPlayer(combatState)!))
        {
            return false;
        }

        return !requireButtonReady || IsEndTurnButtonReady(GetEndTurnButton(combatRoom));
    }

    public static bool CanPlayAnyCard(IScreenContext? currentScreen, CombatState? combatState, CombatActionGate? combatActionGate = null)
    {
        if (!CanUseCombatActions(currentScreen, combatState, out var me, out _, combatActionGate))
        {
            return false;
        }

        return me!.PlayerCombatState!.Hand.Cards.Any(IsCardPlayable);
    }

    public static bool CanChooseMapNode(IScreenContext? currentScreen, RunState? runState)
    {
        // Map votes during an active fight hide end_turn/play_card and desync co-op.
        if (CombatManager.Instance.IsInProgress)
        {
            return false;
        }

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
        // NCardHolder is a plain Control with no enabled/enabled-in-tree state, and
        // ExecuteChooseRewardCardAsync selects a holder by emitting NCardHolder.Pressed directly.
        // The holders this probe returns are therefore exactly the set the executor can resolve; any
        // extra filter here would advertise a gate the executor never applies.
        return GetCardRewardOptions(currentScreen).Count > 0;
    }

    public static bool CanSkipRewardCards(IScreenContext? currentScreen)
    {
        // NCardRewardAlternativeButton is an NButton, so IsEnabled is the same signal CanClaimReward
        // reads from NRewardButton; the getter already drops alternatives that are not visible.
        return GetCardRewardAlternativeButtons(currentScreen).Any(button => button.IsEnabled);
    }

    public static bool CanSelectDeckCard(IScreenContext? currentScreen)
    {
        if (currentScreen is NUnlockScreen)
        {
            return false;
        }

        return GetDeckSelectionOptions(currentScreen).Count > 0;
    }

    public static bool CanCloseCardsView(IScreenContext? currentScreen)
    {
        if (currentScreen is NInspectCardScreen inspectCard)
        {
            return GodotObject.IsInstanceValid(inspectCard) && inspectCard.IsVisibleInTree();
        }

        if (currentScreen is NInspectRelicScreen inspectRelic)
        {
            return GodotObject.IsInstanceValid(inspectRelic) && inspectRelic.IsVisibleInTree();
        }

        return GetCardsViewBackButton(currentScreen) != null;
    }

    public static bool IsClosableCardViewer(IScreenContext? screen)
    {
        return screen is NCardsViewScreen or NCardPileScreen;
    }

    public static bool CanConfirmSelection(IScreenContext? currentScreen)
    {
        if (TryGetCombatHandSelectionMetadata(currentScreen, out _, out var combatMetadata) &&
            combatMetadata.RequiresConfirmation &&
            combatMetadata.CanConfirm)
        {
            return true;
        }

        return TryGetCardGridSelectionMetadata(currentScreen, out var gridMetadata) &&
            gridMetadata.CanConfirm &&
            (gridMetadata.RequiresConfirmation || gridMetadata.MinSelect < gridMetadata.MaxSelect);
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
        catch (Exception ex)
        {
            // Fail closed but never silently: swallowing this hid the difference between "no legal
            // option" and "the probe threw", and the action simply vanished from available_actions.
            Log.Warn($"[STS2AIAgent] choose_event_option probe failed; treating it as unavailable: {ex}");
            return false;
        }
    }

    public static bool CanChooseCapstoneOption(IScreenContext? currentScreen)
    {
        return GetCapstoneButtons(currentScreen).Count > 0;
    }

    public static bool CanPlayCrystalSphere(IScreenContext? currentScreen)
    {
        var minigame = GetCrystalSphereMinigame(currentScreen);
        return minigame is { IsFinished: false };
    }

    public static bool IsCapstonePageOverlay(IScreenContext? currentScreen)
    {
        return currentScreen is NCapstoneSubmenuStack container
            && IsKnownCapstoneContainerPage(container.Stack?.Peek());
    }

    public static bool CanChooseBundle(IScreenContext? currentScreen)
    {
        return GetBundleOptions(currentScreen).Count > 0;
    }

    public static bool CanConfirmBundle(IScreenContext? currentScreen)
    {
        return GetBundleConfirmButtons(currentScreen).Count > 0;
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
        catch (Exception ex)
        {
            // Same rule as the event probe: unavailable, but say so, or a thrown probe looks
            // exactly like a rest site with nothing to offer.
            Log.Warn($"[STS2AIAgent] choose_rest_option probe failed; treating it as unavailable: {ex}");
            return false;
        }
    }

    public static bool CanOpenShopInventory(IScreenContext? currentScreen)
    {
        if (currentScreen is NMerchantRoom room)
        {
            return room.Inventory != null && !room.Inventory.IsOpen;
        }

        return GetFakeMerchantButton(currentScreen) != null;
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
        if (CanUnready(currentScreen))
        {
            return false;
        }

        var multiplayerTestScene = GetMultiplayerTestScene();
        if (multiplayerTestScene != null)
        {
            var lobby = GetMultiplayerTestLobby(multiplayerTestScene);
            return lobby != null
                && !lobby.LocalPlayer.isReady
                && GetMultiplayerLobbyCharacters().Length > 0;
        }

        var characterSelect = GetCharacterSelectScreen(currentScreen);
        if (characterSelect != null && characterSelect.Lobby.LocalPlayer.isReady)
        {
            return false;
        }

        return GetCharacterSelectButtons(currentScreen)
            .Any(button => !button.IsLocked && button.IsEnabled && button.IsVisibleInTree());
    }

    public static bool CanSwitchProfile(IScreenContext? currentScreen)
    {
        return currentScreen is NMainMenu mainMenu &&
            mainMenu.IsVisibleInTree() &&
            mainMenu.SubmenuStack?.SubmenusOpen != true;
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

    public static bool CanSaveAndQuit(IScreenContext? currentScreen, RunState? runState)
    {
        if (currentScreen == null || runState == null)
        {
            return false;
        }

        // A page of the in-run capstone container is a person's menu over a frozen run, and both action
        // surfaces stay empty while one is up. The executor has to refuse the same surface -- the pause
        // menu's own "save and quit" button belongs to the person sitting there, not to the agent.
        if (IsCapstonePageOverlay(currentScreen))
        {
            return false;
        }

        if (NGame.Instance == null || !GodotObject.IsInstanceValid(NGame.Instance))
        {
            return false;
        }

        if (RunManager.Instance.NetService.Type.IsMultiplayer())
        {
            return false;
        }

        return currentScreen is not (NMainMenu or NGameOverScreen or NCharacterSelectScreen or NMultiplayerTest);
    }

    public static bool CanOpenCharacterSelect(IScreenContext? currentScreen)
    {
        if (currentScreen is NSingleplayerSubmenu singleplayerSubmenu && singleplayerSubmenu.IsVisibleInTree())
        {
            return true;
        }

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
        if (currentScreen is NPatchNotesScreen patchNotes)
        {
            return GodotObject.IsInstanceValid(patchNotes) && patchNotes.IsVisibleInTree();
        }

        // The in-run human pages are pages of the capstone container rather than NSubmenu screens of
        // their own, so the submenu branch below never sees them; the container's stack is the thing to
        // pop, and only while a page above the pause menu is the one being shown.
        if (GetClosableCapstonePage(currentScreen) != null)
        {
            return true;
        }

        if (currentScreen is not NSubmenu submenu || !submenu.IsVisibleInTree())
        {
            return false;
        }

        var submenuStack = GetSubmenuStack(submenu);
        return submenuStack != null && submenuStack.SubmenusOpen;
    }

    public static bool CanInviteAiTeammate(IScreenContext? currentScreen)
    {
        if (currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree())
        {
            return false;
        }

        if (AgentRuntime.Instance?.DualLaunching == true)
        {
            return false;
        }

        var autoPlayRunning = AgentRuntime.Instance?.PlayRunning == true;
        return CoopLaunchPolicy.GetStructuralError(InstanceRole.IsCompanion, autoPlayRunning, "MAIN_MENU") == null;
    }

    public static bool CanContinueAiTeammate(IScreenContext? currentScreen)
    {
        if (InstanceRole.IsCompanion)
        {
            return false;
        }

        if (currentScreen is not NMainMenu mainMenu || !mainMenu.IsVisibleInTree())
        {
            return false;
        }

        if (AgentRuntime.Instance?.DualLaunching == true)
        {
            return false;
        }

        var autoPlayRunning = AgentRuntime.Instance?.PlayRunning == true;
        if (CoopLaunchPolicy.GetStructuralError(InstanceRole.IsCompanion, autoPlayRunning, "MAIN_MENU") != null)
        {
            return false;
        }

        return SaveManager.Instance.HasMultiplayerRunSave;
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
        if (GetTimelineTutorial(currentScreen) != null)
        {
            return true;
        }

        var unlockConfirmButton = GetTimelineUnlockConfirmButton(currentScreen);
        if (unlockConfirmButton != null && unlockConfirmButton.IsVisibleInTree() && unlockConfirmButton.IsEnabled)
        {
            return true;
        }

        var inspectCloseButton = GetTimelineInspectCloseButton(currentScreen);
        return inspectCloseButton != null && inspectCloseButton.IsVisibleInTree() && inspectCloseButton.IsEnabled;
    }

    public static bool CanUsePotion(
        IScreenContext? currentScreen,
        CombatState? combatState,
        RunState? runState,
        CombatActionGate? combatActionGate = null)
    {
        var player = GetLocalPlayer(runState);
        if (player == null)
        {
            return false;
        }

        return player.PotionSlots.Any(potion => IsPotionUsable(currentScreen, combatState, player, potion, combatActionGate));
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

    public static bool CanDiscardPotion(IScreenContext? currentScreen, RunState? runState)
    {
        var player = GetLocalPlayer(runState);
        if (player == null || !CanDiscardPotionsInCurrentScreen(currentScreen))
        {
            return false;
        }

        return player.PotionSlots.Any(potion => IsPotionDiscardable(player, potion));
    }

    public static bool CanDiscardPotionAtIndex(IScreenContext? currentScreen, RunState? runState, int optionIndex)
    {
        var player = GetLocalPlayer(runState);
        if (player == null || !CanDiscardPotionsInCurrentScreen(currentScreen) || optionIndex < 0 || optionIndex >= player.PotionSlots.Count)
        {
            return false;
        }

        return IsPotionDiscardable(player, player.PotionSlots[optionIndex]);
    }

    public static bool CanConfirmModal(IScreenContext? currentScreen)
    {
        var hasButton = GetModalConfirmButton(currentScreen) != null;
        return FtueModalPolicy.ExposeConfirm(GetOpenModal()?.GetType().Name, hasButton);
    }

    public static bool CanDismissModal(IScreenContext? currentScreen)
    {
        return GetModalCancelButton(currentScreen) != null;
    }

    public static bool CanReturnToMainMenu(IScreenContext? currentScreen)
    {
        return IsGameOverButtonReady(GetGameOverMainMenuButton(currentScreen));
    }

    public static bool CanContinueGameOver(IScreenContext? currentScreen)
    {
        return IsGameOverButtonReady(GetGameOverContinueButton(currentScreen))
            && !CanReturnToMainMenu(currentScreen);
    }

    public static bool IsCardPlayable(CardModel card)
    {
        return card.CanPlay(out _, out _) && IsCardTargetSupported(card);
    }

    internal static bool IsCombatActionReady()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.IsInProgress ? CombatManager.Instance.DebugOnlyGetState() : null;
        return CanUseCombatActions(currentScreen, combatState, out _, out _);
    }

        private static bool CanUseCombatActions(
        IScreenContext? currentScreen,
        CombatState? combatState,
        out Player? me,
        out NCombatRoom? combatRoom,
        CombatActionGate? combatActionGate = null)
    {
        // Callers inside one state build hand in the gate they already evaluated. The gate advances
        // a shared stability sampler, so evaluating it a second time would answer from a later
        // moment than the payload around it and could disagree with that payload.
        var gate = combatActionGate ?? EvaluateCombatActionGate(currentScreen, combatState);
        me = gate.Me;
        combatRoom = gate.Room;
        return gate.Usable;
    }

    public static bool IsGameOverSummaryStarted(IScreenContext? currentScreen)
    {
        if (currentScreen is not NGameOverScreen gameOver)
        {
            return false;
        }

        if (GetGameOverMainMenuButton(gameOver)?.Visible == true)
        {
            return true;
        }

        try
        {
            if (ReflectedGameMembers.Field(typeof(NGameOverScreen), "_isAnimatingSummary")?.GetValue(gameOver) is true)
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool CanConfirmUnlock(IScreenContext? currentScreen)
    {
        return GetUnlockConfirmButton(currentScreen) != null;
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
}
