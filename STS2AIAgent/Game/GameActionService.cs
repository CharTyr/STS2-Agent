using Godot;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

internal static class GameActionService
{
    /// <summary>
    /// Remembers an explicit skip_reward_cards so the drain that follows does not re-open the
    /// card reward. Scoped to the reward set that recorded the skip, so it can never suppress
    /// a different reward set's card reward after the drain exits.
    /// </summary>
    private static readonly RewardSkipScope CardRewardSkips = new();

    /// <summary>
    /// Mid-turn card play counters. Maintained by the mod since the game's
    /// internal counters are not accessible via reflection. Synchronized to
    /// the current combat round when state is read and incremented by play_card.
    /// </summary>
    internal static int CardsPlayedThisTurn { get; private set; }
    internal static int AttacksPlayedThisTurn { get; private set; }
    internal static int SkillsPlayedThisTurn { get; private set; }
    internal static int LastTurnNumber { get; private set; }

    internal static void SyncCardPlayCounters(int currentTurn)
    {
        if (currentTurn == LastTurnNumber)
        {
            return;
        }

        CardsPlayedThisTurn = 0;
        AttacksPlayedThisTurn = 0;
        SkillsPlayedThisTurn = 0;
        LastTurnNumber = currentTurn;
    }

    /// <summary>
    /// Rolls the optimistic mid-turn counters back when a play_card never left the hand.
    /// </summary>
    private static void RollBackCardPlayCounters(string cardType)
    {
        CardsPlayedThisTurn = Math.Max(0, CardsPlayedThisTurn - 1);
        if (cardType == "Attack")
        {
            AttacksPlayedThisTurn = Math.Max(0, AttacksPlayedThisTurn - 1);
        }
        else if (cardType == "Skill")
        {
            SkillsPlayedThisTurn = Math.Max(0, SkillsPlayedThisTurn - 1);
        }
    }

    public static Task<ActionResponsePayload> ExecuteAsync(ActionRequest request)
    {
        var actionName = request.action?.Trim().ToLowerInvariant();
        var localPlayerId = System.Environment.GetEnvironmentVariable("STS2_MULTIPLAYER_NET_ID");
        if (string.IsNullOrWhiteSpace(localPlayerId) && !InstanceRole.IsCompanion)
        {
            localPlayerId = "1";
        }

        if (!CompanionActPolicy.Allows(
                actionName,
                isCompanion: InstanceRole.IsCompanion,
                actorIsLocal: true,
                requestedPlayerId: request.player_id,
                localPlayerId: localPlayerId))
        {
            throw new ApiException(403, "forbidden_actor", "AI teammate can only act for its own character.", new
            {
                action = request.action,
                player_id = request.player_id
            });
        }

        return actionName switch
        {
            "resolve_rewards" => ExecuteResolveRewardsAsync(request),
            "end_turn" => ExecuteEndTurnAsync(),
            "play_card" => ExecutePlayCardAsync(request),
            "switch_profile" => ExecuteSwitchProfileAsync(request),
            "continue_run" => ExecuteContinueRunAsync(),
            "continue_game_over" => ExecuteContinueGameOverAsync(),
            "dismiss_game_over_wait" => ExecuteDismissGameOverWaitAsync(),
            "abandon_run" => ExecuteAbandonRunAsync(),
            "save_and_quit" => ExecuteSaveAndQuitAsync(),
            "open_character_select" => ExecuteOpenCharacterSelectAsync(),
            "open_timeline" => ExecuteOpenTimelineAsync(),
            "confirm_unlock" => ExecuteConfirmUnlockAsync(),
            "close_main_menu_submenu" => ExecuteCloseMainMenuSubmenuAsync(),
            "choose_timeline_epoch" => ExecuteChooseTimelineEpochAsync(request),
            "confirm_timeline_overlay" => ExecuteConfirmTimelineOverlayAsync(),
            "choose_map_node" => ExecuteChooseMapNodeAsync(request),
            "collect_rewards_and_proceed" => ExecuteCollectRewardsAndProceedAsync(),
            "claim_reward" => ExecuteClaimRewardAsync(request),
            "choose_reward_card" => ExecuteChooseRewardCardAsync(request),
            "skip_reward_cards" => ExecuteSkipRewardCardsAsync(),
            "select_deck_card" => ExecuteSelectDeckCardAsync(request),
            "close_cards_view" => ExecuteCloseCardsViewAsync(),
            "confirm_selection" => ExecuteConfirmSelectionAsync(),
            "proceed" => ExecuteProceedAsync(),
            "open_chest" => ExecuteOpenChestAsync(),
            "choose_treasure_relic" => ExecuteChooseTreasureRelicAsync(request),
            "choose_event_option" => ExecuteChooseEventOptionAsync(request),
            "crystal_set_tool" => ExecuteCrystalSetToolAsync(request),
            "crystal_clear_cell" => ExecuteCrystalClearCellAsync(request),
            "choose_capstone_option" => ExecuteChooseCapstoneOptionAsync(request),
            "choose_bundle" => ExecuteChooseBundleAsync(request),
            "confirm_bundle" => ExecuteConfirmBundleAsync(),
            "choose_rest_option" => ExecuteChooseRestOptionAsync(request),
            "open_shop_inventory" => ExecuteOpenShopInventoryAsync(),
            "close_shop_inventory" => ExecuteCloseShopInventoryAsync(),
            "buy_card" => ExecuteBuyCardAsync(request),
            "buy_relic" => ExecuteBuyRelicAsync(request),
            "buy_potion" => ExecuteBuyPotionAsync(request),
            "remove_card_at_shop" => ExecuteRemoveCardAtShopAsync(),
            "select_character" => ExecuteSelectCharacterAsync(request),
            "embark" => ExecuteEmbarkAsync(),
            "unready" => ExecuteUnreadyAsync(),
            "host_multiplayer_lobby" => ExecuteHostMultiplayerLobbyAsync(),
            "join_multiplayer_lobby" => ExecuteJoinMultiplayerLobbyAsync(),
            "ready_multiplayer_lobby" => ExecuteReadyMultiplayerLobbyAsync(),
            "disconnect_multiplayer_lobby" => ExecuteDisconnectMultiplayerLobbyAsync(),
            "increase_ascension" => ExecuteAdjustAscensionAsync(1, "increase_ascension"),
            "decrease_ascension" => ExecuteAdjustAscensionAsync(-1, "decrease_ascension"),
            "use_potion" => ExecuteUsePotionAsync(request),
            "discard_potion" => ExecuteDiscardPotionAsync(request),
            "run_console_command" => ExecuteRunConsoleCommandAsync(request),
            "confirm_modal" => ExecuteConfirmModalAsync(),
            "dismiss_modal" => ExecuteDismissModalAsync(),
            "return_to_main_menu" => ExecuteReturnToMainMenuAsync(),
            "invite_ai_teammate" => ExecuteInviteAiTeammateAsync(),
            "continue_ai_teammate" => ExecuteContinueAiTeammateAsync(),
            _ => throw new ApiException(409, "invalid_action", "Action is not supported yet.", new
            {
                action = request.action
            })
        };
    }

    private static async Task<ActionResponsePayload> ExecuteEndTurnAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var me = GameStateService.GetLocalPlayer(combatState);
        if (me == null)
        {
            throw new ApiException(503, "state_unavailable", "Local player is unavailable.", new
            {
                action = "end_turn",
                screen
            }, retryable: true);
        }

        if (CombatManager.Instance.IsPaused)
        {
            CombatManager.Instance.Unpause();
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            combatState = CombatManager.Instance.DebugOnlyGetState();
            currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            me = GameStateService.GetLocalPlayer(combatState) ?? me;
            if (CombatManager.Instance.IsPlayerReadyToEndTurn(me) ||
                GameStateService.CanEndTurn(currentScreen, combatState, requireButtonReady: false))
            {
                break;
            }

            if (NGame.Instance == null)
            {
                break;
            }

            await GameThread.WaitForNextFrameAsync();
        }

        var playerCombatState = me.Creature.CombatState
            ?? throw new ApiException(503, "state_unavailable", "Combat state is unavailable.", new
            {
                action = "end_turn",
                screen
            }, retryable: true);
        var roundNumber = playerCombatState.RoundNumber;
        var endTurnButton = GameStateService.GetEndTurnButton(GameStateService.FindActiveCombatRoom(currentScreen));

        if (!CombatManager.Instance.IsPlayerReadyToEndTurn(me))
        {
            if (endTurnButton == null)
            {
                throw new ApiException(503, "state_unavailable", "End turn button is unavailable.", new
                {
                    action = "end_turn",
                    screen
                }, retryable: true);
            }

            await CommitEndTurnButtonAsync(endTurnButton, me);
            if (NGame.Instance != null)
            {
                await GameThread.WaitForNextFrameAsync();
                await GameThread.WaitForNextFrameAsync();
            }

            if (GameStateService.GetOpenModal() != null)
            {
                var modalType = GameStateService.GetOpenModal()?.GetType().Name;
                if (FtueModalPolicy.IsCombatRulesFtue(modalType))
                {
                    throw new ApiException(409, "invalid_action", "Combat rules FTUE is still open; confirm pages instead of ending the turn.", new
                    {
                        action = "end_turn",
                        modal_type = modalType
                    });
                }

                if (GameStateService.TryCloseOpenFtue())
                {
                    await CommitEndTurnButtonAsync(endTurnButton, me);
                }
            }
        }

        var stable = await WaitForEndTurnTransitionAsync(roundNumber, TimeSpan.FromSeconds(5));

        return new ActionResponsePayload
        {
            action = "end_turn",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static int _endTurnKickRound = int.MinValue;

    internal static string EndTurnKickDetail { get; } = string.Empty;

    internal static void EnsureEndTurnPhaseStarts()
    {
        // Intentionally empty: GET /state and end_turn must not reflectively mutate CombatManager.
        _ = _endTurnKickRound;
    }

    private static async Task CommitEndTurnButtonAsync(NEndTurnButton endTurnButton, Player me)
    {
        _ = me;
        if (CombatManager.Instance.IsPaused)
        {
            CombatManager.Instance.Unpause();
        }

        endTurnButton.DebugPress();
        await WaitForEndTurnLongPressAsync(endTurnButton);
        endTurnButton.CallReleaseLogic();
        endTurnButton.DebugRelease();

        if (CombatManager.Instance.IsPaused)
        {
            CombatManager.Instance.Unpause();
        }
    }

    private static async Task WaitForEndTurnLongPressAsync(NEndTurnButton endTurnButton)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var bar = typeof(NEndTurnButton).GetField("_longPressBar", flags)?.GetValue(endTurnButton);
        if (bar == null)
        {
            return;
        }

        var enabled = bar.GetType().GetField("_enabled", flags)?.GetValue(bar) as bool?;
        if (enabled != true)
        {
            return;
        }

        var duration = bar.GetType().GetField("_longPressDuration", flags)?.GetValue(bar) is double seconds
            ? seconds
            : 0.45;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(0.05, duration + 0.1));
        while (DateTime.UtcNow < deadline && NGame.Instance != null)
        {
            await GameThread.WaitForNextFrameAsync();
        }
    }

    private static async Task<bool> WaitForEndTurnTransitionAsync(int previousRound, TimeSpan timeout)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await GameThread.WaitForNextFrameAsync();

            if (IsEndTurnStable(previousRound))
            {
                return true;
            }
        }

        return IsEndTurnStable(previousRound);
    }

    private static bool IsEndTurnStable(int previousRound)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return true;
        }

        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
        {
            return true;
        }

        if (combatState.RoundNumber != previousRound)
        {
            return true;
        }

        if (combatState.CurrentSide != CombatSide.Player)
        {
            return true;
        }

        if (!GameStateService.IsPlayerActionPhase(combatState))
        {
            return true;
        }

        var localPlayer = GameStateService.GetLocalPlayer(combatState);
        return localPlayer != null && CombatManager.Instance.IsPlayerReadyToEndTurn(localPlayer);
    }

    private static async Task<ActionResponsePayload> ExecutePlayCardAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanPlayAnyCard(currentScreen, combatState))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "play_card",
                screen
            });
        }

        if (request.card_index == null)
        {
            throw new ApiException(400, "invalid_request", "play_card requires card_index.", new
            {
                action = "play_card"
            });
        }

        var me = GameStateService.GetLocalPlayer(combatState)
            ?? throw new ApiException(503, "state_unavailable", "Local player is unavailable.", new
            {
                action = "play_card",
                screen
            }, retryable: true);

        var hand = me.PlayerCombatState?.Hand.Cards.ToList()
            ?? throw new ApiException(503, "state_unavailable", "Hand is unavailable.", new
            {
                action = "play_card",
                screen
            }, retryable: true);

        if (request.card_index < 0 || request.card_index >= hand.Count)
        {
            throw new ApiException(409, "invalid_target", "card_index is out of range.", new
            {
                action = "play_card",
                card_index = request.card_index,
                hand_count = hand.Count
            });
        }

        var card = hand[request.card_index.Value];
        if (!GameStateService.IsCardTargetSupported(card))
        {
            throw new ApiException(409, "invalid_action", "This target type is not supported by the API.", new
            {
                action = "play_card",
                card_index = request.card_index,
                card_id = card.Id.Entry,
                target_type = card.TargetType.ToString(),
                screen
            });
        }

        var target = ResolveCardTarget(request, combatState, card);

        if (!card.TryManualPlay(target))
        {
            throw new ApiException(409, "invalid_action", "Card cannot be played in the current state.", new
            {
                action = "play_card",
                card_index = request.card_index,
                target_index = request.target_index,
                card_id = card.Id.Entry,
                screen
            });
        }

        var currentTurn = combatState?.RoundNumber ?? 0;
        SyncCardPlayCounters(currentTurn);
        CardsPlayedThisTurn++;
        var cardType = card.Type.ToString();
        if (cardType == "Attack") AttacksPlayedThisTurn++;
        else if (cardType == "Skill") SkillsPlayedThisTurn++;

        var stable = await WaitForPlayCardTransitionAsync(card, TimeSpan.FromSeconds(12));
        if (!stable)
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                TryCancelRunningPlayerAction();
                await GameThread.WaitForNextFrameAsync();
                stable = IsPlayCardStable(card);
                if (stable || ArePlayerDrivenActionsSettled())
                {
                    break;
                }
            }
        }

        if (CardPlayCounterPolicy.ShouldRollBack(
                playSettled: stable,
                combatInProgress: CombatManager.Instance.IsInProgress,
                cardStillInHand: card.Pile?.Type == PileType.Hand))
        {
            RollBackCardPlayCounters(cardType);
        }

        return new ActionResponsePayload
        {
            action = "play_card",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteOpenCharacterSelectAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanOpenCharacterSelect(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "open_character_select",
                screen
            });
        }

        if (currentScreen is NSingleplayerSubmenu openSubmenu)
        {
            ClickSingleplayerStandardButton(openSubmenu);
        }
        else if (currentScreen is NMainMenu mainMenu)
        {
            var singleplayerSubmenu = mainMenu.SubmenuStack.GetSubmenuType<NSingleplayerSubmenu>();
            if (singleplayerSubmenu != null)
            {
                mainMenu.SubmenuStack.Push(singleplayerSubmenu);
                await WaitForMainMenuSubmenuOpenAsync<NSingleplayerSubmenu>(mainMenu, TimeSpan.FromSeconds(5));
                ClickSingleplayerStandardButton(singleplayerSubmenu);
            }
            else
            {
                var characterSelectScreen = mainMenu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                characterSelectScreen.InitializeSingleplayer();
                mainMenu.SubmenuStack.Push(characterSelectScreen);
            }
        }
        else
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "open_character_select",
                screen
            });
        }

        var stable = await WaitForCharacterSelectOpenAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "open_character_select",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable
                ? "Action completed."
                : MenuTransitionPolicy.DescribeUnsettled("open_character_select", CurrentModalName()),
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteOpenTimelineAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (currentScreen is not NMainMenu mainMenu || !GameStateService.CanOpenTimeline(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "open_timeline",
                screen
            });
        }

        mainMenu.SubmenuStack.PushSubmenuType<NTimelineScreen>();
        var stable = await WaitForMainMenuSubmenuOpenAsync<NTimelineScreen>(mainMenu, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "open_timeline",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteCloseMainMenuSubmenuAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanCloseMainMenuSubmenu(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "close_main_menu_submenu",
                screen
            });
        }

        bool stable;
        if (currentScreen is NPatchNotesScreen patchNotes)
        {
            var backButton = GetPrivateField<NButton>(patchNotes, "_backButton");
            if (backButton != null &&
                GodotObject.IsInstanceValid(backButton) &&
                backButton.IsVisibleInTree() &&
                backButton.IsEnabled)
            {
                backButton.ForceClick();
            }
            else
            {
                ((Node)patchNotes).Call("Close");
            }

            stable = await WaitForPatchNotesCloseAsync(patchNotes, TimeSpan.FromSeconds(10));
        }
        else if (currentScreen is NCapstoneSubmenuStack capstonePageContainer &&
            GameStateService.GetClosableCapstonePage(capstonePageContainer) is { } capstonePage)
        {
            // The page is left the way its own BackButton leaves it: the game wires every submenu's back
            // button to Stack.Pop(), which closes this page and shows the page below it again. Popping a
            // page above the pause menu therefore cannot resume the run -- the pause menu is still on top.
            var capstoneStack = capstonePageContainer.Stack
                ?? throw new ApiException(503, "state_unavailable", "Capstone submenu stack is unavailable.", new
                {
                    action = "close_main_menu_submenu",
                    screen
                }, retryable: true);

            capstoneStack.Pop();
            stable = await WaitForCapstonePageCloseAsync(capstoneStack, capstonePage, TimeSpan.FromSeconds(10));
        }
        else
        {
            if (currentScreen is not NSubmenu submenu)
            {
                throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
                {
                    action = "close_main_menu_submenu",
                    screen
                });
            }

            var submenuStack = GameStateService.GetSubmenuStack(submenu)
                ?? throw new ApiException(503, "state_unavailable", "Main menu submenu stack is unavailable.", new
                {
                    action = "close_main_menu_submenu",
                    screen
                }, retryable: true);

            submenuStack.Pop();
            stable = await WaitForMainMenuSubmenuCloseAsync(submenuStack, submenu, TimeSpan.FromSeconds(10));
        }

        return new ActionResponsePayload
        {
            action = "close_main_menu_submenu",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteChooseTimelineEpochAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseTimelineEpoch(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_timeline_epoch",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "option_index is required.", new
            {
                action = "choose_timeline_epoch"
            });
        }

        var slot = ResolveTimelineSlot(currentScreen, request.option_index.Value);
        var previousState = slot.State;

        slot.ForceClick();
        var stable = await WaitForTimelineEpochTransitionAsync(slot, previousState, TimeSpan.FromSeconds(15));

        return new ActionResponsePayload
        {
            action = "choose_timeline_epoch",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteConfirmTimelineOverlayAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanConfirmTimelineOverlay(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "confirm_timeline_overlay",
                screen
            });
        }

        var tutorial = GameStateService.GetTimelineTutorial(currentScreen);
        if (tutorial != null)
        {
            var buttonDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            NButton? tutorialButton = GameStateService.GetTimelineTutorialAcknowledgeButton(currentScreen);
            while (DateTime.UtcNow < buttonDeadline)
            {
                tutorialButton = GameStateService.GetTimelineTutorialAcknowledgeButton(currentScreen);
                if (tutorialButton != null && tutorialButton.IsEnabled)
                {
                    break;
                }

                await WaitForNextFrameAsync();
            }

            // Re-read the button immediately before clicking. The tutorial animates in, so the
            // reference held by the wait loop may be stale; a missing or disabled button means there
            // is nothing to confirm, so surface it instead of clicking nothing and guessing pending.
            tutorialButton = GameStateService.GetTimelineTutorialAcknowledgeButton(currentScreen);
            if (tutorialButton == null || !tutorialButton.IsEnabled)
            {
                throw new ApiException(503, "state_unavailable", "Timeline tutorial acknowledge button is unavailable.", new
                {
                    action = "confirm_timeline_overlay",
                    screen
                }, retryable: true);
            }

            tutorialButton.ForceClick();

            var tutorialGone = await WaitForTimelineTutorialClosedAsync(tutorial, TimeSpan.FromSeconds(15));
            return new ActionResponsePayload
            {
                action = "confirm_timeline_overlay",
                status = tutorialGone ? "completed" : "pending",
                stable = tutorialGone,
                message = tutorialGone ? "Action completed." : "Action queued but state is still transitioning.",
                state = GameStateService.BuildStatePayload()
            };
        }

        var unlockScreen = GameStateService.GetTimelineUnlockScreen(currentScreen);
        if (unlockScreen != null)
        {
            // Revalidate the target right before the click: the overlay can close or the button can
            // become disabled between the availability guard at the top of the handler and here.
            var confirmButton = GameStateService.GetTimelineUnlockConfirmButton(currentScreen);
            if (confirmButton == null ||
                !GodotObject.IsInstanceValid(confirmButton) ||
                !confirmButton.IsVisibleInTree() ||
                !confirmButton.IsEnabled)
            {
                throw new ApiException(503, "state_unavailable", "Timeline unlock confirm button is unavailable.", new
                {
                    action = "confirm_timeline_overlay",
                    screen
                }, retryable: true);
            }

            confirmButton.ForceClick();
            var unlockType = unlockScreen.GetType();
            var stable = await WaitForTimelineUnlockTransitionAsync(unlockType, TimeSpan.FromSeconds(10));

            return new ActionResponsePayload
            {
                action = "confirm_timeline_overlay",
                status = stable ? "completed" : "pending",
                stable = stable,
                message = stable ? "Action completed." : "Action queued but state is still transitioning.",
                state = GameStateService.BuildStatePayload()
            };
        }

        // Revalidate the close target right before the click so a closed or disabled overlay is not
        // clicked blindly; the getter reads live node state, not the guard's earlier snapshot.
        var closeButton = GameStateService.GetTimelineInspectCloseButton(currentScreen);
        if (closeButton == null ||
            !GodotObject.IsInstanceValid(closeButton) ||
            !closeButton.IsVisibleInTree() ||
            !closeButton.IsEnabled)
        {
            throw new ApiException(503, "state_unavailable", "Timeline inspect close button is unavailable.", new
            {
                action = "confirm_timeline_overlay",
                screen
            }, retryable: true);
        }

        closeButton.ForceClick();
        var inspectScreen = GameStateService.GetTimelineInspectScreen(currentScreen);
        var stableInspect = await WaitForTimelineInspectCloseAsync(inspectScreen, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "confirm_timeline_overlay",
            status = stableInspect ? "completed" : "pending",
            stable = stableInspect,
            message = stableInspect ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteConfirmUnlockAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        var unlockScreen = GameStateService.GetActiveUnlockScreen(currentScreen);
        if (unlockScreen == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "confirm_unlock",
                screen
            });
        }

        var confirmButton = GameStateService.GetUnlockConfirmButton(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Unlock confirm button is unavailable.", new
            {
                action = "confirm_unlock",
                screen
            }, retryable: true);

        confirmButton.ForceClick();
        var stable = await WaitForUnlockScreenClosedAsync(unlockScreen, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "confirm_unlock",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForUnlockScreenClosedAsync(NUnlockScreen unlockScreen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            if (!GodotObject.IsInstanceValid(unlockScreen) || !unlockScreen.IsVisibleInTree())
            {
                return true;
            }
        }

        return !GodotObject.IsInstanceValid(unlockScreen) || !unlockScreen.IsVisibleInTree();
    }

    private static async Task<ActionResponsePayload> ExecuteSwitchProfileAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanSwitchProfile(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "switch_profile",
                screen
            });
        }

        if (request.option_index is not int profileId || profileId is < 1 or > 3)
        {
            throw new ApiException(400, "invalid_request", "switch_profile requires option_index in the range 1..3.", new
            {
                action = "switch_profile"
            });
        }

        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game))
        {
            throw new ApiException(503, "state_unavailable", "Game instance is unavailable.", new
            {
                action = "switch_profile",
                screen
            }, retryable: true);
        }

        NMainMenu? initialMainMenu = null;
        if (SaveManager.Instance.CurrentProfileId != profileId)
        {
            initialMainMenu = currentScreen as NMainMenu;
            SaveManager.Instance.SwitchProfileId(profileId);
            var prefsReadResult = SaveManager.Instance.InitPrefsData();
            var progressReadResult = SaveManager.Instance.InitProgressData();
            game.ReloadMainMenu();
            game.CheckShowSaveFileError(
                progressReadResult,
                prefsReadResult,
                new ReadSaveResult<SettingsSave>(new SettingsSave()));
        }

        var stable = await WaitForProfileSwitchAsync(initialMainMenu, profileId, TimeSpan.FromSeconds(15));
        return new ActionResponsePayload
        {
            action = "switch_profile",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForProfileSwitchAsync(
        NMainMenu? initialMainMenu,
        int profileId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (SaveManager.Instance.CurrentProfileId == profileId &&
                currentScreen is NMainMenu mainMenu &&
                (initialMainMenu == null || mainMenu != initialMainMenu) &&
                GodotObject.IsInstanceValid(mainMenu) &&
                mainMenu.IsInsideTree() &&
                mainMenu.IsVisibleInTree() &&
                mainMenu.SubmenuStack?.SubmenusOpen != true)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<ActionResponsePayload> ExecuteContinueRunAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (currentScreen is not NMainMenu mainMenu || !GameStateService.CanContinueRun(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "continue_run",
                screen
            });
        }

        var continueButton = GameStateService.GetMainMenuContinueButton(mainMenu)
            ?? throw new ApiException(503, "state_unavailable", "Continue button is unavailable.", new
            {
                action = "continue_run",
                screen
            }, retryable: true);

        continueButton.ForceClick();
        var stable = await WaitForMainMenuExitAsync(mainMenu, TimeSpan.FromSeconds(15));

        return new ActionResponsePayload
        {
            action = "continue_run",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable
                ? "Action completed."
                : MenuTransitionPolicy.DescribeUnsettled("continue_run", CurrentModalName()),
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteAbandonRunAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (currentScreen is not NMainMenu mainMenu || !GameStateService.CanAbandonRun(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "abandon_run",
                screen
            });
        }

        var abandonButton = GameStateService.GetMainMenuAbandonRunButton(mainMenu)
            ?? throw new ApiException(503, "state_unavailable", "Abandon run button is unavailable.", new
            {
                action = "abandon_run",
                screen
            }, retryable: true);

        abandonButton.ForceClick();
        var stable = await WaitForMainMenuModalAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "abandon_run",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteSaveAndQuitAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanSaveAndQuit(currentScreen, runState))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "save_and_quit",
                screen
            });
        }

        var pauseMenu = FindPauseMenu();
        if (pauseMenu == null)
        {
            var pauseButton = FindFirstInGame<NTopBarPauseButton>();
            if (pauseButton == null || !pauseButton.IsVisibleInTree())
            {
                throw new ApiException(503, "state_unavailable", "Pause button is unavailable.", new
                {
                    action = "save_and_quit",
                    screen
                }, retryable: true);
            }

            pauseButton.ForceClick();
            pauseMenu = await WaitForPauseMenuAsync(TimeSpan.FromSeconds(5));
        }

        if (pauseMenu == null)
        {
            throw new ApiException(503, "state_unavailable", "Pause menu did not open.", new
            {
                action = "save_and_quit",
                screen
            }, retryable: true);
        }

        var closeTimeout = TimeSpan.FromSeconds(20);
        var closeTimedOut = false;
        var closeTask = InvokePrivateTask(pauseMenu, "CloseToMenu");
        if (closeTask != null)
        {
            var completedCloseTask = await WaitForGameTaskAsync(closeTask, closeTimeout);
            var closeOutcome = ClassifyGameTaskWait(closeTask, completedCloseTask == null);
            if (closeOutcome == GameTaskWaitOutcome.Failed)
            {
                throw new ApiException(409, "invalid_action", $"Save and quit failed: {DescribeGameTaskFailure(closeTask)}.", new
                {
                    action = "save_and_quit",
                    screen
                });
            }

            closeTimedOut = closeOutcome == GameTaskWaitOutcome.TimedOut;
            if (closeTimedOut)
            {
                ObserveBackgroundTask(closeTask, "save_and_quit");
            }
        }
        else
        {
            var saveAndQuitButton = GetPrivateField<NButton>(pauseMenu, "_saveAndQuitButton");
            if (saveAndQuitButton == null || !saveAndQuitButton.IsVisibleInTree() || !saveAndQuitButton.IsEnabled)
            {
                throw new ApiException(503, "state_unavailable", "Save and Quit button is unavailable.", new
                {
                    action = "save_and_quit",
                    screen
                }, retryable: true);
            }

            saveAndQuitButton.ForceClick();
        }

        var stable = await WaitForMainMenuAfterSaveAndQuitAsync(closeTimeout);

        return new ActionResponsePayload
        {
            action = "save_and_quit",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable
                ? "Action completed."
                : closeTimedOut
                    ? GameTaskWaitPolicy.DescribeTimeout("save_and_quit", closeTimeout)
                    : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static Creature? ResolveCardTarget(ActionRequest request, CombatState? combatState, CardModel card)
    {
        if (!GameStateService.CardRequiresTarget(card))
        {
            return null;
        }

        if (combatState == null)
        {
            throw new ApiException(503, "state_unavailable", "Combat state is unavailable.", new
            {
                action = "play_card",
                card_id = card.Id.Entry
            }, retryable: true);
        }

        if (request.target_index == null)
        {
            throw new ApiException(409, "invalid_target", "This card requires target_index.", new
            {
                action = "play_card",
                card_id = card.Id.Entry,
                target_type = card.TargetType.ToString(),
                target_index_space = card.TargetType == TargetType.AnyEnemy ? "enemies" : "players"
            });
        }

        if (card.TargetType == TargetType.AnyEnemy)
        {
            var enemy = GameStateService.ResolveEnemyTarget(combatState, request.target_index.Value);
            if (enemy == null)
            {
                throw new ApiException(409, "invalid_target", "target_index is out of range for combat.enemies[].", new
                {
                    action = "play_card",
                    card_id = card.Id.Entry,
                    target_index = request.target_index,
                    target_index_space = "enemies"
                });
            }

            return enemy;
        }

        if (card.TargetType == TargetType.AnyAlly)
        {
            var allyTargetIndices = GameStateService.GetTargetablePlayerIndices(combatState, card.Owner, allowSelf: false);
            if (!allyTargetIndices.Contains(request.target_index.Value))
            {
                throw new ApiException(409, "invalid_target", "target_index is out of range for combat.players[].", new
                {
                    action = "play_card",
                    card_id = card.Id.Entry,
                    target_index = request.target_index,
                    target_index_space = "players"
                });
            }

            return GameStateService.ResolvePlayerTarget(combatState, request.target_index.Value);
        }

        throw new ApiException(409, "invalid_action", "This target type is not supported yet.", new
        {
            action = "play_card",
            card_id = card.Id.Entry,
            target_type = card.TargetType.ToString()
        });
    }

    private static async Task<bool> WaitForPlayCardTransitionAsync(CardModel card, TimeSpan timeout)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await GameThread.WaitForNextFrameAsync();

            if (IsPlayCardStable(card))
            {
                return true;
            }

            if (IsPlayCardAwaitingPlayerInput())
            {
                return false;
            }
        }

        return IsPlayCardStable(card);
    }

    private static bool IsPlayCardStable(CardModel card)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return true;
        }

        if (card.Pile?.Type == PileType.Hand)
        {
            return false;
        }

        return ArePlayerDrivenActionsSettled();
    }

    private static bool IsPlayCardAwaitingPlayerInput()
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        return currentScreen != null && GameStateService.ResolveScreen(currentScreen) == "CARD_SELECTION";
    }

    internal static bool TryCancelRunningPlayerAction()
    {
        var executor = RunManager.Instance.ActionExecutor;
        var running = executor.CurrentlyRunningAction;
        if (running == null)
        {
            return false;
        }

        // Nested player choice is a legitimate wait. Cancelling it drops the other
        // player's selection UI. The hang we recover from is ExecuteAction never
        // finishing while the hand is still in Play mode.
        if (running.State is GameActionState.GatheringPlayerChoice or GameActionState.ReadyToResumeExecuting)
        {
            return false;
        }

        var canceled = false;
        try
        {
            running.Cancel();
            canceled = true;
        }
        catch
        {
        }

        try
        {
            // ActionExecutor has Cancel(), not CancelAction(). Cancel() trips the
            // per-frame wait so the executor can drop a stuck Execute() task.
            executor.Cancel();
            canceled = true;
        }
        catch
        {
        }

        if (ReferenceEquals(executor.CurrentlyRunningAction, running))
        {
            try
            {
                var setter = executor.GetType()
                    .GetProperty(nameof(ActionExecutor.CurrentlyRunningAction))
                    ?.GetSetMethod(nonPublic: true);
                setter?.Invoke(executor, new object?[] { null });
                canceled = true;
            }
            catch
            {
            }
        }

        return canceled;
    }

    private static bool ArePlayerDrivenActionsSettled()
    {
        var runningAction = RunManager.Instance.ActionExecutor.CurrentlyRunningAction;
        if (runningAction != null && ActionQueueSet.IsGameActionPlayerDriven(runningAction))
        {
            return false;
        }

        try
        {
            var readyAction = RunManager.Instance.ActionQueueSet.GetReadyAction();
            if (readyAction != null && ActionQueueSet.IsGameActionPlayerDriven(readyAction))
            {
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return true;
    }

    internal static bool AreGameActionsSettled()
    {
        if (RunManager.Instance.ActionExecutor.CurrentlyRunningAction != null)
        {
            return false;
        }

        try
        {
            if (!RunManager.Instance.ActionQueueSet.IsEmpty)
            {
                return false;
            }

            return RunManager.Instance.ActionQueueSet.GetReadyAction() == null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<ActionResponsePayload> ExecuteChooseMapNodeAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseMapNode(currentScreen, runState))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_map_node",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "choose_map_node requires option_index.", new
            {
                action = "choose_map_node"
            });
        }

        var availableNodes = GameStateService.GetAvailableMapNodes(currentScreen, runState);
        if (request.option_index < 0 || request.option_index >= availableNodes.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "choose_map_node",
                option_index = request.option_index,
                node_count = availableNodes.Count
            });
        }

        var selectedNode = availableNodes[request.option_index.Value];
        var roomEntered = false;
        var isMultiplayerVote = runState != null && RunManager.Instance.NetService.Type.IsMultiplayer() && runState.Players.Count > 1;

        void OnRoomEntered()
        {
            roomEntered = true;
        }

        RunManager.Instance.RoomEntered += OnRoomEntered;
        try
        {
            if (isMultiplayerVote)
            {
                var mapScreen = NMapScreen.Instance
                    ?? currentScreen as NMapScreen
                    ?? throw new ApiException(503, "state_unavailable", "Map screen is unavailable.", new
                    {
                        action = "choose_map_node",
                        screen
                    }, retryable: true);

                mapScreen.OnMapPointSelectedLocally(selectedNode);
            }
            else
            {
                selectedNode.ForceClick();
            }

            var stable = isMultiplayerVote
                ? await WaitForMultiplayerMapVoteOrTransitionAsync(selectedNode.Point.coord, TimeSpan.FromSeconds(10), () => roomEntered)
                : await WaitForMapTransitionAsync(TimeSpan.FromSeconds(10), () => roomEntered);
            var roomStarted = HasEnteredMapDestination(() => roomEntered);

            return new ActionResponsePayload
            {
                action = "choose_map_node",
                status = stable ? "completed" : "pending",
                stable = stable,
                message = stable
                    ? roomStarted
                        ? "Action completed."
                        : "Map vote submitted. Waiting for other players to finish choosing."
                    : "Action queued but state is still transitioning.",
                state = GameStateService.BuildStatePayload()
            };
        }
        finally
        {
            RunManager.Instance.RoomEntered -= OnRoomEntered;
        }
    }

    private static async Task<bool> WaitForMultiplayerMapVoteOrTransitionAsync(
        MapCoord targetCoord,
        TimeSpan timeout,
        Func<bool> roomEntered)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await GameThread.WaitForNextFrameAsync();

            if (HasEnteredMapDestination(roomEntered) || IsLocalMapVoteRegistered(targetCoord))
            {
                return true;
            }
        }

        return HasEnteredMapDestination(roomEntered) || IsLocalMapVoteRegistered(targetCoord);
    }

    private static async Task<bool> WaitForMapTransitionAsync(TimeSpan timeout, Func<bool> roomEntered)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await GameThread.WaitForNextFrameAsync();

            if (IsMapTransitionStable(roomEntered))
            {
                return true;
            }
        }

        return IsMapTransitionStable(roomEntered);
    }

    private static bool IsMapTransitionStable(Func<bool> roomEntered)
    {
        if (!HasEnteredMapDestination(roomEntered))
        {
            return false;
        }

        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var runState = RunManager.Instance.DebugOnlyGetState();
        if (!DoesScreenMatchCurrentRoom(currentScreen, runState?.CurrentRoom))
        {
            return false;
        }

        return IsStableScreenState(currentScreen, allowMapScreen: false);
    }

    private static bool HasEnteredMapDestination(Func<bool> roomEntered)
    {
        if (roomEntered())
        {
            return true;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        return runState?.CurrentRoom is not null && runState.CurrentRoom is not MapRoom;
    }

    private static bool IsLocalMapVoteRegistered(MapCoord targetCoord)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        var localPlayer = GameStateService.GetLocalPlayer(runState);
        if (runState == null || localPlayer == null)
        {
            return false;
        }

        var vote = RunManager.Instance.MapSelectionSynchronizer.GetVote(localPlayer);
        return vote.HasValue &&
            vote.Value.coord.row == targetCoord.row &&
            vote.Value.coord.col == targetCoord.col;
    }

    private static bool DoesScreenMatchCurrentRoom(IScreenContext? currentScreen, AbstractRoom? currentRoom)
    {
        if (currentRoom == null)
        {
            return false;
        }

        var screen = GameStateService.ResolveScreen(currentScreen);
        return currentRoom switch
        {
            CombatRoom => screen == "COMBAT",
            EventRoom => screen == "EVENT",
            MerchantRoom => screen == "SHOP",
            RestSiteRoom => screen == "REST",
            TreasureRoom => screen == "CHEST",
            MapRoom => screen == "MAP",
            _ => screen != "UNKNOWN" && screen != "MAP"
        };
    }

    private static bool IsStableScreenState(IScreenContext? currentScreen, bool allowMapScreen)
    {
        var screen = GameStateService.ResolveScreen(currentScreen);
        if (screen == "UNKNOWN")
        {
            return false;
        }

        if (screen == "COMBAT")
        {
            var combatRoom = GameStateService.FindActiveCombatRoom(currentScreen);
            return combatRoom != null &&
                combatRoom.Mode == CombatRoomMode.ActiveCombat &&
                CombatManager.Instance.IsInProgress &&
                !CombatManager.Instance.IsOverOrEnding &&
                GameStateService.IsPlayerActionPhase(CombatManager.Instance.DebugOnlyGetState()) &&
                !CombatManager.Instance.PlayerActionsDisabled &&
                CombatManager.Instance.DebugOnlyGetState() != null;
        }

        if (screen != "MAP")
        {
            return true;
        }

        if (!allowMapScreen)
        {
            return false;
        }

        return currentScreen is NMapScreen mapScreen && !mapScreen.IsTraveling;
    }

    private static async Task<ActionResponsePayload> ExecuteProceedAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanProceed(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "proceed",
                screen
            });
        }

        var proceedButton = GameStateService.GetProceedButton(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Proceed button not found.", new
            {
                action = "proceed",
                screen
            }, retryable: true);

        proceedButton.ForceClick();
        var stable = await WaitForProceedTransitionAsync(currentScreen, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "proceed",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForProceedTransitionAsync(
        IScreenContext? previousScreen,
        TimeSpan timeout)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsProceedStable(previousScreen))
            {
                return true;
            }
        }

        return IsProceedStable(previousScreen);
    }

    private static bool IsProceedStable(IScreenContext? previousScreen)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (ReferenceEquals(currentScreen, previousScreen))
        {
            return false;
        }

        return IsStableScreenState(currentScreen, allowMapScreen: true);
    }

    private static async Task<ActionResponsePayload> ExecuteCrystalSetToolAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var minigame = GameStateService.GetCrystalSphereMinigame(currentScreen);
        if (minigame == null || minigame.IsFinished)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "crystal_set_tool",
                screen
            });
        }

        var tool = ParseCrystalSphereTool(request.tool, "crystal_set_tool");
        if (!GameStateService.TrySetCrystalSphereTool(currentScreen, tool))
        {
            throw new ApiException(503, "state_unavailable", "Crystal Sphere tool controls are unavailable.", new
            {
                action = "crystal_set_tool",
                screen
            }, retryable: true);
        }

        await WaitForNextFrameAsync();

        // Evidence: the model tool is the observable source of truth. TrySetCrystalSphereTool assigns
        // CrystalSphereMinigame.CrystalSphereTool before returning true, so re-reading the live
        // minigame confirms the requested tool is actually active instead of trusting the call alone.
        var confirmed = GameStateService.GetCrystalSphereMinigame(ActiveScreenContext.Instance.GetCurrentScreen())
            is { } liveMinigame && liveMinigame.CrystalSphereTool == tool;

        return new ActionResponsePayload
        {
            action = "crystal_set_tool",
            status = confirmed ? "completed" : "pending",
            stable = confirmed,
            message = confirmed
                ? $"Crystal sphere tool set to {tool.ToString().ToLowerInvariant()}."
                : $"Crystal sphere tool was not observed as {tool.ToString().ToLowerInvariant()} after the request.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteCrystalClearCellAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var minigame = GameStateService.GetCrystalSphereMinigame(currentScreen);
        if (minigame == null || minigame.IsFinished)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "crystal_clear_cell",
                screen
            });
        }

        if (request.x is not int x || request.y is not int y)
        {
            throw new ApiException(400, "invalid_request", "Parameters 'x' and 'y' are required.", new
            {
                action = "crystal_clear_cell",
                screen
            });
        }

        var grid = minigame.GridSize;
        if (x < 0 || x >= grid.X || y < 0 || y >= grid.Y)
        {
            throw new ApiException(400, "invalid_request",
                $"Cell ({x},{y}) is outside the {grid.X}x{grid.Y} crystal sphere grid.", new
                {
                    action = "crystal_clear_cell",
                    screen,
                    x,
                    y
                });
        }

        // Optional atomic tool switch so agents can play one divination per call.
        if (request.tool != null)
        {
            var tool = ParseCrystalSphereTool(request.tool, "crystal_clear_cell");
            if (!GameStateService.TrySetCrystalSphereTool(currentScreen, tool))
            {
                throw new ApiException(503, "state_unavailable", "Crystal Sphere tool controls are unavailable.", new
                {
                    action = "crystal_clear_cell",
                    screen
                }, retryable: true);
            }
        }

        var divinationsBefore = minigame.DivinationCount;
        var cellTimeout = TimeSpan.FromSeconds(10);
        var cellClickTask = minigame.CellClicked(minigame.cells[x, y]);
        var completedCellClickTask = await WaitForGameTaskAsync(cellClickTask, cellTimeout);
        var cellClickOutcome = ClassifyGameTaskWait(cellClickTask, completedCellClickTask == null);
        if (cellClickOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Crystal sphere cell clear failed: {DescribeGameTaskFailure(cellClickTask)}.", new
            {
                action = "crystal_clear_cell",
                screen,
                x,
                y
            });
        }

        if (cellClickOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundTask(cellClickTask, "crystal_clear_cell");
        }

        var stable = await WaitForCrystalSphereSettleAsync(currentScreen, divinationsBefore, cellTimeout);

        return new ActionResponsePayload
        {
            action = "crystal_clear_cell",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable
                ? "Action completed."
                : cellClickOutcome == GameTaskWaitOutcome.TimedOut
                    ? GameTaskWaitPolicy.DescribeTimeout("crystal_clear_cell", cellTimeout)
                    : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static CrystalSphereMinigame.CrystalSphereToolType ParseCrystalSphereTool(
        string? rawTool,
        string action)
    {
        return rawTool?.Trim().ToLowerInvariant() switch
        {
            "big" => CrystalSphereMinigame.CrystalSphereToolType.Big,
            "small" => CrystalSphereMinigame.CrystalSphereToolType.Small,
            _ => throw new ApiException(
                400,
                "invalid_request",
                "Parameter 'tool' must be \"big\" or \"small\".",
                new { action, tool = rawTool })
        };
    }

    private static async Task<bool> WaitForCrystalSphereSettleAsync(
        IScreenContext? screenContext,
        int divinationsBefore,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            var screenChanged = !ReferenceEquals(currentScreen, screenContext);
            var minigame = GameStateService.GetCrystalSphereMinigame(currentScreen);
            if (CrystalSphereSettlePolicy.IsSettled(
                    screenChanged,
                    minigame != null,
                    divinationsBefore,
                    minigame?.DivinationCount ?? divinationsBefore,
                    minigame?.IsFinished ?? false,
                    GameStateService.CanProceed(currentScreen)))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<ActionResponsePayload> ExecuteResolveRewardsAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanCollectRewardsAndProceed(currentScreen) &&
            currentScreen is not NCardRewardSelectionScreen)
        {
            throw new ApiException(409, "invalid_action", "Not on reward screen.", new
            {
                action = "resolve_rewards",
                screen
            });
        }

        // option_index: -1 = skip card, 0/1/2 = pick that card, absent = auto (first card)
        // card_index is accepted as a backwards-compatible alias for picking a card.
        int pendingChoice;
        if (request.option_index.HasValue)
        {
            pendingChoice = request.option_index.Value == -1
                ? RewardChoicePolicy.SkipChoice
                : request.option_index.Value;
            if (request.option_index.Value == -1)
            {
                CardRewardSkips.MarkSkipped(GameStateService.GetRewardSetId(currentScreen));
            }
            else
            {
                CardRewardSkips.Clear();
            }
        }
        else if (request.card_index.HasValue)
        {
            if (request.card_index.Value < 0)
            {
                throw new ApiException(409, "invalid_target", "card_index is out of range.", new
                {
                    action = "resolve_rewards",
                    card_index = request.card_index.Value,
                    screen
                });
            }

            pendingChoice = request.card_index.Value;
            CardRewardSkips.Clear();
        }
        else
        {
            pendingChoice = RewardChoicePolicy.AutoChoice;
            CardRewardSkips.Clear();
        }

        // Reject an explicit index that cannot exist before anything is clicked. Only a
        // non-empty option list is authoritative: a card reward screen that just opened
        // exposes no holders for a few frames (TryResolveCardRewardAsync waits 24 frames
        // for the same reason), so rejecting an empty list would fail a legal pick. An
        // empty list (or no open screen) defers to the consume-time re-check, which still
        // throws before selecting any card.
        if (currentScreen is NCardRewardSelectionScreen openCardRewardScreen)
        {
            var optionCount = GameStateService.GetCardRewardOptions(openCardRewardScreen).Count;
            var resolution = RewardChoicePolicy.Resolve(pendingChoice, optionCount);
            if (optionCount > 0 && !resolution.IsValid)
            {
                throw new ApiException(409, "invalid_target", resolution.Reason ?? "option_index is out of range.", new
                {
                    action = "resolve_rewards",
                    option_index = pendingChoice,
                    option_count = optionCount,
                    screen
                });
            }
        }

        var choice = new RewardFlowChoiceState(pendingChoice);

        var stable = await DrainRewardFlowAsync(TimeSpan.FromSeconds(20), choice);

        return new ActionResponsePayload
        {
            action = "resolve_rewards",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "All rewards resolved." : "Reward flow still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteCollectRewardsAndProceedAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanCollectRewardsAndProceed(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "collect_rewards_and_proceed",
                screen
            });
        }

        var stable = await DrainRewardFlowAsync(
            TimeSpan.FromSeconds(20),
            new RewardFlowChoiceState(RewardChoicePolicy.AutoChoice));

        return new ActionResponsePayload
        {
            action = "collect_rewards_and_proceed",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Reward flow is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteClaimRewardAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanClaimReward(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "claim_reward",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "claim_reward requires option_index.", new
            {
                action = "claim_reward"
            });
        }

        var rewardButtons = GameStateService.GetRewardButtons(currentScreen);

        if (request.option_index < 0 || request.option_index >= rewardButtons.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "claim_reward",
                option_index = request.option_index,
                option_count = rewardButtons.Count
            });
        }

        var selectedReward = rewardButtons[request.option_index.Value];
        if (!selectedReward.IsEnabled)
        {
            throw new ApiException(409, "invalid_action", "The selected reward is not claimable in the current state.", new
            {
                action = "claim_reward",
                option_index = request.option_index
            });
        }

        var previousRewardCount = rewardButtons.Count(button => button.IsEnabled);
        selectedReward.ForceClick();
        var stable = await WaitForRewardButtonResolutionAsync(currentScreen, previousRewardCount, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "claim_reward",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteChooseRewardCardAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseRewardCard(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_reward_card",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "choose_reward_card requires option_index.", new
            {
                action = "choose_reward_card"
            });
        }

        var options = GameStateService.GetCardRewardOptions(currentScreen);
        if (request.option_index < 0 || request.option_index >= options.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "choose_reward_card",
                option_index = request.option_index,
                option_count = options.Count
            });
        }

        var selected = options[request.option_index.Value];
        var previousOptionCount = options.Count;
        selected.EmitSignal(NCardHolder.SignalName.Pressed, selected);
        CardRewardSkips.Clear(); // Card was taken, clear any prior skip
        var stable = await WaitForRewardCardResolutionAsync(currentScreen, previousOptionCount, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "choose_reward_card",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteSkipRewardCardsAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanSkipRewardCards(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "skip_reward_cards",
                screen
            });
        }

        var alternatives = GameStateService.GetCardRewardAlternativeButtons(currentScreen);
        // Deliberately the same enabled-filtered set CanSkipRewardCards gates on: its
        // Any(button => button.IsEnabled) probe is what let this action through, so clicking the
        // first *enabled* alternative keeps the executed target inside the collection the guard
        // just proved non-empty; the unfiltered First() could pick a disabled button instead.
        var selected = alternatives.First(button => button.IsEnabled);
        selected.ForceClick();
        CardRewardSkips.MarkSkipped(GameStateService.GetRewardSetId(currentScreen));
        var stable = await WaitForRewardCardResolutionAsync(currentScreen, GameStateService.GetCardRewardOptions(currentScreen).Count, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "skip_reward_cards",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteSelectDeckCardAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanSelectDeckCard(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "select_deck_card",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "select_deck_card requires option_index.", new
            {
                action = "select_deck_card"
            });
        }

        var options = GameStateService.GetDeckSelectionOptions(currentScreen);
        if (request.option_index < 0 || request.option_index >= options.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "select_deck_card",
                option_index = request.option_index,
                option_count = options.Count
            });
        }

        var isCombatHandSelection = GameStateService.TryGetCombatHandSelectionMetadata(currentScreen, out var combatHand, out var combatHandSelection);
        var isCardGridSelection = GameStateService.TryGetCardGridSelectionMetadata(
            currentScreen, out var cardGridSelection);

        // CanSelectDeckCard only proves that *some* card holder is visible: it also returns true for
        // screens whose holders are discovered by a generic descendant scan. Without the native
        // selection metadata (combat hand / card grid) or the choose-a-card screen that has its own
        // resolution wait, there is no click target we can prove reacts, so answer with an honest
        // invalid_action instead of clicking an unknown node and reporting pending forever.
        if (!isCombatHandSelection &&
            !isCardGridSelection &&
            currentScreen is not NChooseACardSelectionScreen)
        {
            throw new ApiException(409, "invalid_action", "No clickable card selection target is available for the current screen.", new
            {
                action = "select_deck_card",
                screen,
                option_index = request.option_index,
                option_count = options.Count
            });
        }

        var selected = options[request.option_index.Value];
        if (isCombatHandSelection)
        {
            if (selected is not NHandCardHolder handHolder)
            {
                throw new ApiException(503, "state_unavailable", "Combat hand selection holder is unavailable.", new
                {
                    action = "select_deck_card",
                    screen
                }, retryable: true);
            }

            combatHand!.Call(
                combatHand.CurrentMode == NPlayerHand.Mode.UpgradeSelect
                    ? NPlayerHand.MethodName.SelectCardInUpgradeMode
                    : NPlayerHand.MethodName.SelectCardInSimpleMode,
                handHolder);
            combatHand.Call(NPlayerHand.MethodName.CheckIfSelectionComplete);
        }
        else
        {
            selected.EmitSignal(NCardHolder.SignalName.Pressed, selected);
        }

        var stable = currentScreen switch
        {
            NCardGridSelectionScreen cardGridScreen
                when isCardGridSelection =>
                await SettleCardGridSelectionClickAsync(
                    cardGridScreen, cardGridSelection.SelectedCount, TimeSpan.FromSeconds(10)),
            NCardGridSelectionScreen cardSelectScreen => await ConfirmDeckSelectionAsync(cardSelectScreen, TimeSpan.FromSeconds(10)),
            NChooseACardSelectionScreen chooseCardScreen => await WaitForChooseCardSelectionResolutionAsync(chooseCardScreen, TimeSpan.FromSeconds(10)),
            _ when isCombatHandSelection => await WaitForCombatHandSelectionStepAsync(combatHandSelection, TimeSpan.FromSeconds(10)),
            _ => false
        };

        return new ActionResponsePayload
        {
            action = "select_deck_card",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteConfirmSelectionAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanConfirmSelection(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "confirm_selection",
                screen
            });
        }

        if (currentScreen is NCardGridSelectionScreen cardGridScreen)
        {
            var stableGrid = await ConfirmDeckSelectionAsync(cardGridScreen, TimeSpan.FromSeconds(10));
            return new ActionResponsePayload
            {
                action = "confirm_selection",
                status = stableGrid ? "completed" : "pending",
                stable = stableGrid,
                message = stableGrid ? "Action completed." : "Action queued but state is still transitioning.",
                state = GameStateService.BuildStatePayload()
            };
        }

        if (!GameStateService.TryGetCombatHandSelection(currentScreen, out var combatHand) ||
            combatHand == null ||
            !TryGetCombatHandConfirmButton(combatHand, out var confirmButton) ||
            confirmButton == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "confirm_selection",
                screen
            });
        }

        confirmButton.ForceClick();
        var stable = await WaitForCombatHandSelectionResolutionAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "confirm_selection",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteCloseCardsViewAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanCloseCardsView(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "close_cards_view",
                screen
            });
        }

        if (currentScreen is NInspectCardScreen inspectCard)
        {
            inspectCard.Close();
        }
        else if (currentScreen is NInspectRelicScreen inspectRelic)
        {
            inspectRelic.Close();
        }
        else
        {
            var backButton = GameStateService.GetCardsViewBackButton(currentScreen)
                ?? throw new ApiException(503, "state_unavailable", "Cards view back button is unavailable.", new
                {
                    action = "close_cards_view",
                    screen
                }, retryable: true);

            backButton.ForceClick();
        }

        var stable = await WaitForCardsViewCloseAsync(currentScreen, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "close_cards_view",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForChooseCardSelectionResolutionAsync(
        NChooseACardSelectionScreen selectionScreen,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            var screenClosed = currentScreen is not NChooseACardSelectionScreen || !GodotObject.IsInstanceValid(selectionScreen);
            if (screenClosed)
            {
                if (CombatManager.Instance.IsInProgress)
                {
                    if (CombatManager.Instance.IsOverOrEnding || GameStateService.IsCombatActionReady())
                    {
                        return true;
                    }
                }
                else
                {
                    for (var i = 0; i < 5; i++)
                    {
                        await WaitForNextFrameAsync();
                    }

                    if (ArePlayerDrivenActionsSettled())
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static async Task<bool> WaitForCardsViewCloseAsync(IScreenContext? closedScreen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsCardsViewClosed(closedScreen))
            {
                return true;
            }
        }

        return IsCardsViewClosed(closedScreen);
    }

    /// <summary>
    /// The card list is closed once it is no longer the current screen. The inspect overlays share
    /// <c>close_cards_view</c> but are their own screen types, so they settle the same way.
    /// </summary>
    private static bool IsCardsViewClosed(IScreenContext? closedScreen)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (closedScreen is NInspectCardScreen or NInspectRelicScreen)
        {
            return !ReferenceEquals(currentScreen, closedScreen);
        }

        // NCardPileScreen joined close_cards_view, so the widened viewer set has to be consulted
        // before the original cards-view test. The final line still settles plain NCardsViewScreen.
        if (GameStateService.IsClosableCardViewer(currentScreen) && currentScreen is not NCardsViewScreen)
        {
            return false;
        }

        return currentScreen is not NCardsViewScreen;
    }

    private static async Task<bool> WaitForCombatHandSelectionResolutionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            var selectionClosed = !GameStateService.TryGetCombatHandSelection(currentScreen, out var currentHand) ||
                currentHand == null ||
                !GodotObject.IsInstanceValid(currentHand);
            if (selectionClosed)
            {
                if (CombatManager.Instance.IsInProgress)
                {
                    if (CombatManager.Instance.IsOverOrEnding || GameStateService.IsCombatActionReady())
                    {
                        return true;
                    }
                }
                else if (ArePlayerDrivenActionsSettled())
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<bool> WaitForCombatHandSelectionStepAsync(
        CombatHandSelectionMetadata previousSelection,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (!GameStateService.TryGetCombatHandSelectionMetadata(currentScreen, out _, out var currentSelection))
            {
                if (!CombatManager.Instance.IsInProgress)
                {
                    return ArePlayerDrivenActionsSettled();
                }

                if (CombatManager.Instance.IsOverOrEnding || GameStateService.IsCombatActionReady())
                {
                    return true;
                }

                continue;
            }

            if (currentSelection.SelectedCount != previousSelection.SelectedCount)
            {
                if (!currentSelection.RequiresConfirmation &&
                    currentSelection.SelectedCount >= currentSelection.MaxSelect)
                {
                    continue;
                }

                // The click landed, but the overlay is still open: either more picks are allowed or
                // the player has to confirm. select_deck_card is one step of a combat-hand
                // selection, so report it as pending and let confirm_selection end it, the same way
                // use_potion reports the selection it opens.
                return false;
            }
        }

        if (!GameStateService.TryGetCombatHandSelection(ActiveScreenContext.Instance.GetCurrentScreen(), out _) &&
            CombatManager.Instance.IsInProgress)
        {
            return CombatManager.Instance.IsOverOrEnding || GameStateService.IsCombatActionReady();
        }

        return false;
    }

    private static bool TryGetCombatHandConfirmButton(NPlayerHand hand, out NConfirmButton? confirmButton)
    {
        confirmButton = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton")
            ?? hand.GetNodeOrNull<NConfirmButton>("SelectModeConfirmButton");
        return confirmButton != null && GodotObject.IsInstanceValid(confirmButton);
    }

    private static async Task<bool> DrainRewardFlowAsync(TimeSpan timeout, RewardFlowChoiceState choice)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        var attemptedRewardButtons = new HashSet<ulong>();

        while (DateTime.UtcNow < deadline)
        {
            if (await TryAdvanceRewardModalAsync())
            {
                continue;
            }

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();

            if (currentScreen is NCardRewardSelectionScreen cardRewardScreen)
            {
                if (!await TryResolveCardRewardAsync(cardRewardScreen, deadline, choice))
                {
                    return false;
                }

                continue;
            }

            if (currentScreen is not NRewardsScreen rewardsScreen)
            {
                CardRewardSkips.Clear();
                return true;
            }

            if (TryGetNextClaimableRewardButton(rewardsScreen, attemptedRewardButtons, out var rewardButton))
            {
                attemptedRewardButtons.Add(rewardButton!.GetInstanceId());
                await ClickRewardButtonAsync(rewardButton, deadline);
                continue;
            }

            var proceedButton = GameStateService.GetRewardProceedButton(rewardsScreen);
            if (proceedButton != null && proceedButton.IsEnabled)
            {
                proceedButton.ForceClick();
                return await WaitForRewardFlowExitAsync(rewardsScreen, deadline);
            }

            if (await TryEscapeEmptyRewardsScreenAsync(rewardsScreen, deadline))
            {
                return true;
            }
        }

        return IsRewardFlowStable();
    }

    private static async Task<bool> TryEscapeEmptyRewardsScreenAsync(NRewardsScreen rewardsScreen, DateTime deadline)
    {
        var emptySince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            if (!GodotObject.IsInstanceValid(rewardsScreen) ||
                ActiveScreenContext.Instance.GetCurrentScreen() != rewardsScreen)
            {
                return true;
            }

            if (TryGetNextClaimableRewardButton(rewardsScreen, new HashSet<ulong>(), out _))
            {
                return false;
            }

            var proceedButton = GameStateService.GetRewardProceedButton(rewardsScreen);
            if (proceedButton != null && proceedButton.IsEnabled)
            {
                proceedButton.ForceClick();
                return await WaitForRewardFlowExitAsync(rewardsScreen, deadline);
            }

            if (DateTime.UtcNow - emptySince < TimeSpan.FromSeconds(1))
            {
                continue;
            }

            try
            {
                rewardsScreen.Call("TryEnableProceedButton");
            }
            catch
            {
            }

            proceedButton = GameStateService.GetRewardProceedButton(rewardsScreen);
            if (proceedButton != null)
            {
                proceedButton.ForceClick();
                var proceedDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                if (proceedDeadline > deadline)
                {
                    proceedDeadline = deadline;
                }

                if (await WaitForRewardFlowExitAsync(rewardsScreen, proceedDeadline))
                {
                    return true;
                }
            }

            try
            {
                NOverlayStack.Instance?.Remove(rewardsScreen);
            }
            catch
            {
            }

            if (await WaitForRewardFlowExitAsync(rewardsScreen, deadline))
            {
                return true;
            }

            try
            {
                _ = RunManager.Instance.ProceedFromTerminalRewardsScreen();
            }
            catch
            {
            }

            return await WaitForRewardFlowExitAsync(rewardsScreen, deadline);
        }

        return IsRewardFlowStable();
    }

    private static bool TryGetNextClaimableRewardButton(
        NRewardsScreen rewardsScreen,
        HashSet<ulong> attemptedRewardButtons,
        out NRewardButton? rewardButton)
    {
        var hasPotionSlots = GameStateService.GetLocalPlayer(RunManager.Instance.DebugOnlyGetState())?.HasOpenPotionSlots ?? false;
        rewardButton = GameStateService
            .GetRewardButtons(rewardsScreen)
            .FirstOrDefault(button =>
                button.IsEnabled &&
                !attemptedRewardButtons.Contains(button.GetInstanceId()) &&
                (button.Reward is not PotionReward || hasPotionSlots) &&
                (!CardRewardSkips.AppliesTo(rewardsScreen.GetInstanceId()) || button.Reward is not CardReward));

        return rewardButton != null;
    }

    private static async Task ClickRewardButtonAsync(NRewardButton rewardButton, DateTime deadline)
    {
        var previousRewardCount = GameStateService.GetRewardButtons(ActiveScreenContext.Instance.GetCurrentScreen()).Count;
        rewardButton.ForceClick();

        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is NCardRewardSelectionScreen)
            {
                return;
            }

            var rewardButtons = GameStateService.GetRewardButtons(currentScreen);
            if (!GodotObject.IsInstanceValid(rewardButton) || rewardButtons.Count != previousRewardCount)
            {
                return;
            }
        }
    }

    private static async Task<bool> TryResolveCardRewardAsync(
        NCardRewardSelectionScreen cardRewardScreen,
        DateTime deadline,
        RewardFlowChoiceState choice)
    {
        for (var i = 0; i < 24 && DateTime.UtcNow < deadline; i++)
        {
            await WaitForNextFrameAsync();
        }

        var options = GameStateService.GetCardRewardOptions(cardRewardScreen);
        var resolution = RewardChoicePolicy.Resolve(choice.ConsumePendingChoice(), options.Count);

        // An explicit index missing from the live option list must fail instead of
        // silently falling back to the first option. "No choice given" with no options
        // yet keeps waiting, matching the documented auto behavior.
        if (!resolution.IsValid)
        {
            if (resolution.Kind != RewardChoiceKind.Pick)
            {
                return false;
            }

            throw new ApiException(409, "invalid_target", resolution.Reason ?? "option_index is out of range.", new
            {
                action = "resolve_rewards",
                option_index = resolution.Index,
                option_count = options.Count
            });
        }

        // If resolve_rewards requested a skip, click the skip alternative
        if (resolution.Kind == RewardChoiceKind.Skip)
        {
            var alternatives = GameStateService.GetCardRewardAlternativeButtons(cardRewardScreen);
            if (alternatives.Count > 0)
            {
                alternatives.First().ForceClick();
                CardRewardSkips.MarkSkipped(GameStateService.GetRewardSetId(cardRewardScreen));
            }
            while (DateTime.UtcNow < deadline)
            {
                await WaitForNextFrameAsync();
                if (!GodotObject.IsInstanceValid(cardRewardScreen) ||
                    ActiveScreenContext.Instance.GetCurrentScreen() is not NCardRewardSelectionScreen)
                    return true;
            }
            return false;
        }

        var selected = options[resolution.Index];

        selected.EmitSignal(NCardHolder.SignalName.Pressed, selected);
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (!GodotObject.IsInstanceValid(cardRewardScreen) ||
                ActiveScreenContext.Instance.GetCurrentScreen() is not NCardRewardSelectionScreen)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForRewardFlowExitAsync(NRewardsScreen rewardsScreen, DateTime deadline)
    {
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (!GodotObject.IsInstanceValid(rewardsScreen))
            {
                return true;
            }

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen != rewardsScreen)
            {
                return true;
            }

            if (NOverlayStack.Instance?.Peek() != rewardsScreen)
            {
                return true;
            }
        }

        return IsRewardFlowStable();
    }

    private static bool IsRewardFlowStable()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        return currentScreen is not NRewardsScreen && currentScreen is not NCardRewardSelectionScreen;
    }

    private static async Task<bool> TryAdvanceRewardModalAsync()
    {
        var modal = GameStateService.GetOpenModal();
        if (modal == null)
        {
            return false;
        }

        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var button = GameStateService.GetModalConfirmButton(currentScreen);
        if (button != null)
        {
            button.ForceClick();
            await WaitForNextFrameAsync();
            return true;
        }

        if (FtueModalPolicy.CloseFtueDirectly(modal.GetType().Name, hasUsableConfirmButton: false) &&
            GameStateService.TryCloseOpenFtue())
        {
            await WaitForNextFrameAsync();
            return true;
        }

        await WaitForNextFrameAsync();
        return true;
    }

    private static async Task<bool> WaitForRewardCardResolutionAsync(
        IScreenContext? previousScreen,
        int previousOptionCount,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (!ReferenceEquals(currentScreen, previousScreen))
            {
                return true;
            }

            if (GameStateService.GetCardRewardOptions(currentScreen).Count != previousOptionCount)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForRewardButtonResolutionAsync(
        IScreenContext? previousScreen,
        int previousRewardCount,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (!ReferenceEquals(currentScreen, previousScreen))
            {
                return true;
            }

            var currentRewardCount = GameStateService.GetRewardButtons(currentScreen).Count(button => button.IsEnabled);
            if (currentRewardCount != previousRewardCount)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> ConfirmDeckSelectionAsync(NCardGridSelectionScreen screen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        // The screen-level confirm is not always the last step: on deck, transform and enchant screens
        // it only opens a preview that still needs its own confirm. Keep that click inside the loop so
        // one confirm_selection call drives the whole sequence, and cap it so an enabled-but-inert
        // button cannot be hammered while the loop waits for its deadline.
        var stageOneClicks = 0;
        var framesUntilNextStageOneClick = 0;

        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (framesUntilNextStageOneClick > 0)
            {
                framesUntilNextStageOneClick--;
            }

            if (!GodotObject.IsInstanceValid(screen) ||
                ActiveScreenContext.Instance.GetCurrentScreen() is not NCardGridSelectionScreen)
            {
                return await WaitForDeckSelectionResolutionAsync(screen, deadline);
            }

            var previewContainer = screen.GetNodeOrNull<Control>("%PreviewContainer");
            var previewConfirm = screen.GetNodeOrNull<NConfirmButton>("%PreviewConfirm")
                ?? previewContainer?.GetNodeOrNull<NConfirmButton>("Confirm");
            if (previewContainer?.Visible == true && previewConfirm?.IsEnabled == true)
            {
                previewConfirm.ForceClick();
                return await WaitForDeckSelectionResolutionAsync(screen, deadline);
            }

            if (screen is NDeckTransformSelectScreen transformScreen &&
                TryGetDeckTransformConfirmButton(transformScreen, out var transformConfirm))
            {
                transformConfirm!.ForceClick();
                return await WaitForDeckSelectionResolutionAsync(screen, deadline);
            }

            if (screen is NDeckEnchantSelectScreen enchantScreen &&
                TryGetDeckEnchantConfirmButton(enchantScreen, out var enchantConfirm))
            {
                enchantConfirm!.ForceClick();
                return await WaitForDeckSelectionResolutionAsync(screen, deadline);
            }

            if (screen is NDeckUpgradeSelectScreen upgradeScreen &&
                TryGetDeckUpgradeConfirmButton(upgradeScreen, out var upgradeConfirm))
            {
                upgradeConfirm!.ForceClick();
                return await WaitForDeckSelectionResolutionAsync(screen, deadline);
            }

            var confirmButton = screen.GetNodeOrNull<NConfirmButton>("%Confirm")
                ?? screen.GetNodeOrNull<NConfirmButton>("Confirm");
            if (confirmButton?.IsEnabled == true &&
                stageOneClicks < StageOneConfirmClickLimit &&
                framesUntilNextStageOneClick == 0)
            {
                stageOneClicks++;
                framesUntilNextStageOneClick = StageOneConfirmCooldownFrames;
                confirmButton.ForceClick();

                // Deliberately keep looping: the next iterations pick up either the closed screen or
                // the preview this click may have opened. Returning here instead made the caller wait
                // out the whole timeout with the preview on screen and then need a second call.
                continue;
            }
        }

        return false;
    }

    private const int StageOneConfirmClickLimit = 2;
    private const int StageOneConfirmCooldownFrames = 5;

    private static async Task<bool> SettleCardGridSelectionClickAsync(
        NCardGridSelectionScreen screen,
        int previousSelectedCount,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (!GodotObject.IsInstanceValid(screen) ||
                !ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(), screen))
            {
                return await WaitForDeckSelectionResolutionAsync(screen, deadline);
            }

            if (!GameStateService.TryGetCardGridSelectionMetadata(screen, out var metadata) ||
                metadata.SelectedCount == previousSelectedCount)
            {
                continue;
            }

            if (metadata.SelectedCount < previousSelectedCount ||
                metadata.SelectedCount < metadata.MinSelect ||
                metadata.SelectedCount < metadata.MaxSelect)
            {
                return true;
            }

            var remaining = deadline - DateTime.UtcNow;
            return remaining > TimeSpan.Zero &&
                await ConfirmDeckSelectionAsync(screen, remaining);
        }

        return false;
    }

    private static bool TryGetDeckUpgradeConfirmButton(
        NDeckUpgradeSelectScreen screen,
        out NConfirmButton? confirmButton)
    {
        var singlePreview = screen.GetNodeOrNull<Control>("%UpgradeSinglePreviewContainer");
        if (singlePreview?.Visible == true)
        {
            confirmButton = singlePreview.GetNodeOrNull<NConfirmButton>("Confirm");
            return confirmButton?.IsEnabled == true;
        }

        var multiPreview = screen.GetNodeOrNull<Control>("%UpgradeMultiPreviewContainer");
        if (multiPreview?.Visible == true)
        {
            confirmButton = multiPreview.GetNodeOrNull<NConfirmButton>("Confirm");
            return confirmButton?.IsEnabled == true;
        }

        confirmButton = null;
        return false;
    }

    private static bool TryGetDeckTransformConfirmButton(
        NDeckTransformSelectScreen screen,
        out NConfirmButton? confirmButton)
    {
        var previewContainer = screen.GetNodeOrNull<Control>("%PreviewContainer");
        if (previewContainer?.Visible == true)
        {
            confirmButton = previewContainer.GetNodeOrNull<NConfirmButton>("Confirm");
            return confirmButton?.IsEnabled == true;
        }

        confirmButton = null;
        return false;
    }

    private static bool TryGetDeckEnchantConfirmButton(
        NDeckEnchantSelectScreen screen,
        out NConfirmButton? confirmButton)
    {
        var singlePreview = screen.GetNodeOrNull<Control>("%EnchantSinglePreviewContainer");
        if (singlePreview?.Visible == true)
        {
            confirmButton = singlePreview.GetNodeOrNull<NConfirmButton>("Confirm");
            return confirmButton?.IsEnabled == true;
        }

        var multiPreview = screen.GetNodeOrNull<Control>("%EnchantMultiPreviewContainer");
        if (multiPreview?.Visible == true)
        {
            confirmButton = multiPreview.GetNodeOrNull<NConfirmButton>("Confirm");
            return confirmButton?.IsEnabled == true;
        }

        confirmButton = null;
        return false;
    }

    private static async Task<bool> WaitForDeckSelectionResolutionAsync(NCardGridSelectionScreen screen, DateTime deadline)
    {
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            var screenClosed = !GodotObject.IsInstanceValid(screen) || currentScreen is not NCardGridSelectionScreen;

            if (screenClosed)
            {
                if (CombatManager.Instance.IsInProgress)
                {
                    if (CombatManager.Instance.IsOverOrEnding || GameStateService.IsCombatActionReady())
                    {
                        return true;
                    }
                }
                else
                {
                    for (var i = 0; i < 5; i++)
                    {
                        await WaitForNextFrameAsync();
                    }

                    if (ArePlayerDrivenActionsSettled())
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static async Task<ActionResponsePayload> ExecuteOpenChestAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (currentScreen is not NTreasureRoom treasureRoom || !GameStateService.CanOpenChest(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "open_chest",
                screen
            });
        }

        var chestButton = treasureRoom.GetNodeOrNull<NButton>("%Chest")
            ?? throw new ApiException(503, "state_unavailable", "Chest button not found.", new
            {
                action = "open_chest",
                screen
            }, retryable: true);

        chestButton.ForceClick();
        var stable = await WaitForChestOpenTransitionAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "open_chest",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForChestOpenTransitionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (GameStateService.GetTreasureRelicCollection(currentScreen) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<ActionResponsePayload> ExecuteChooseTreasureRelicAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseTreasureRelic(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_treasure_relic",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "choose_treasure_relic requires option_index.", new
            {
                action = "choose_treasure_relic"
            });
        }

        var relics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        if (relics == null || request.option_index < 0 || request.option_index >= relics.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "choose_treasure_relic",
                option_index = request.option_index,
                relic_count = relics?.Count ?? 0
            });
        }

        RunManager.Instance.TreasureRoomRelicSynchronizer.PickRelicLocally(request.option_index.Value);
        var stable = await WaitForRelicPickTransitionAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "choose_treasure_relic",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteChooseEventOptionAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseEventOption(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_event_option",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "choose_event_option requires option_index.", new
            {
                action = "choose_event_option"
            });
        }

        var eventModel = RunManager.Instance.EventSynchronizer.GetLocalEvent()
            ?? throw new ApiException(503, "state_unavailable", "Event state is unavailable.", new
            {
                action = "choose_event_option",
                screen
            }, retryable: true);

        if (eventModel.IsFinished)
        {
            // Finished events only have the synthetic proceed option at index 0
            if (request.option_index != 0)
            {
                throw new ApiException(409, "invalid_target", "Event is finished. Only option_index 0 (proceed) is valid.", new
                {
                    action = "choose_event_option",
                    option_index = request.option_index,
                    is_finished = true
                });
            }

            var proceedTimeout = TimeSpan.FromSeconds(10);
            var proceedTask = NEventRoom.Proceed();
            var completedProceedTask = await WaitForGameTaskAsync(proceedTask, proceedTimeout);
            var proceedOutcome = ClassifyGameTaskWait(proceedTask, completedProceedTask == null);
            if (proceedOutcome == GameTaskWaitOutcome.Failed)
            {
                throw new ApiException(409, "invalid_action", $"Event proceed failed: {DescribeGameTaskFailure(proceedTask)}.", new
                {
                    action = "choose_event_option",
                    screen,
                    option_index = request.option_index
                });
            }

            if (proceedOutcome == GameTaskWaitOutcome.TimedOut)
            {
                ObserveBackgroundTask(proceedTask, "choose_event_option");
            }

            var stable = await WaitForEventScreenTransitionAsync(proceedTimeout);

            return new ActionResponsePayload
            {
                action = "choose_event_option",
                status = stable ? "completed" : "pending",
                stable = stable,
                message = stable
                    ? "Event proceeded."
                    : proceedOutcome == GameTaskWaitOutcome.TimedOut
                        ? GameTaskWaitPolicy.DescribeTimeout("choose_event_option", proceedTimeout)
                        : "Proceed queued but state is still transitioning.",
                state = GameStateService.BuildStatePayload()
            };
        }

        // Non-finished event: choose an option
        var options = eventModel.CurrentOptions;
        if (request.option_index < 0 || request.option_index >= options.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "choose_event_option",
                option_index = request.option_index,
                option_count = options.Count
            });
        }

        if (options[request.option_index.Value].IsLocked)
        {
            throw new ApiException(409, "invalid_target", "The selected event option is locked.", new
            {
                action = "choose_event_option",
                option_index = request.option_index
            });
        }

        RunManager.Instance.EventSynchronizer.ChooseLocalOption(request.option_index.Value);
        var stableOption = await WaitForEventOptionTransitionAsync(
            eventModel.Id?.Entry,
            BuildEventOptionSignature(eventModel),
            options.Count,
            TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "choose_event_option",
            status = stableOption ? "completed" : "pending",
            stable = stableOption,
            message = stableOption ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    /// <summary>
    /// Waits for screen to leave NEventRoom (used after proceed).
    /// </summary>
    private static async Task<bool> WaitForEventScreenTransitionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is not NEventRoom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Waits for event state to change after choosing an option.
    /// Detects: screen change, IsFinished change, or options count change.
    /// </summary>
    private static async Task<bool> WaitForEventOptionTransitionAsync(
        string? previousEventId,
        string previousOptionSignature,
        int previousOptionCount,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();

            // Screen changed entirely (e.g. combat started from event)
            if (currentScreen is not NEventRoom)
            {
                return true;
            }

            var currentEventModel = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            if (currentEventModel == null)
            {
                continue;
            }

            if (currentEventModel.Id?.Entry != previousEventId)
            {
                return true;
            }

            if (currentEventModel.IsFinished)
            {
                return true;
            }

            if (currentEventModel.CurrentOptions.Count != previousOptionCount)
            {
                return true;
            }

            if (BuildEventOptionSignature(currentEventModel) != previousOptionSignature)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<ActionResponsePayload> ExecuteChooseCapstoneOptionAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseCapstoneOption(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_capstone_option",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "choose_capstone_option requires option_index.", new
            {
                action = "choose_capstone_option"
            });
        }

        var buttons = GameStateService.GetCapstoneButtons(currentScreen);
        if (request.option_index < 0 || request.option_index >= buttons.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "choose_capstone_option",
                option_index = request.option_index,
                button_count = buttons.Count
            });
        }

        var button = buttons[request.option_index.Value];
        button.EmitSignal(BaseButton.SignalName.Pressed);

        // Wait for screen transition
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var stable = false;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            var newScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (newScreen is not NCapstoneSubmenuStack)
            {
                stable = true;
                break;
            }
        }

        return new ActionResponsePayload
        {
            action = "choose_capstone_option",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    /// <summary>
    /// Builds the post-action state or fails honestly. An action that reports
    /// <c>completed</c> must never carry a fabricated empty state snapshot.
    /// </summary>
    private static GameStatePayload BuildActionState(string action, string? screen)
    {
        try
        {
            return GameStateService.BuildStatePayload();
        }
        catch (Exception)
        {
            throw new ApiException(503, "state_unavailable", "Game state is unavailable after the action.", new
            {
                action,
                screen
            }, retryable: true);
        }
    }

    private static async Task<ActionResponsePayload> ExecuteChooseBundleAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseBundle(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_bundle",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "choose_bundle requires option_index.", new
            {
                action = "choose_bundle"
            });
        }

        var bundles = GameStateService.GetBundleOptions(currentScreen);
        if (request.option_index < 0 || request.option_index >= bundles.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "choose_bundle",
                option_index = request.option_index,
                bundle_count = bundles.Count
            });
        }

        var bundle = bundles[request.option_index.Value];
        // Call the screen's OnBundleClicked method directly
        if (currentScreen is NChooseABundleSelectionScreen bundleScreen)
        {
            ((Node)bundleScreen).Call("OnBundleClicked", bundle);
        }

        // Wait for screen transition
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var stable = false;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            var newScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (newScreen is not NChooseABundleSelectionScreen)
            {
                stable = true;
                break;
            }
        }

        return new ActionResponsePayload
        {
            action = "choose_bundle",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = BuildActionState("choose_bundle", screen)
        };
    }

    private static async Task<ActionResponsePayload> ExecuteConfirmBundleAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanConfirmBundle(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "confirm_bundle",
                screen
            });
        }

        var buttons = GameStateService.GetBundleConfirmButtons(currentScreen);
        if (buttons.Count == 0)
        {
            throw new ApiException(409, "invalid_action", "No confirm button found.", new
            {
                action = "confirm_bundle",
                screen
            });
        }

        var confirmBtn = buttons[0];
        Log.Info($"[STS2AIAgent] confirm_bundle: clicking {confirmBtn.GetType().Name} '{confirmBtn.Name}'");

        // Try ForceClick first
        confirmBtn.ForceClick();
        await WaitForNextFrameAsync();

        // If still on bundle screen, try calling OnConfirmPressed on the screen
        var stable = false;
        if (ActiveScreenContext.Instance.GetCurrentScreen() is NChooseABundleSelectionScreen bundleScreen2)
        {
            try
            {
                Log.Info("[STS2AIAgent] confirm_bundle: trying OnConfirmPressed");
                ((Node)bundleScreen2).Call("OnConfirmPressed");
            }
            catch { }

            // Also try emitting the button's signal with no args
            try
            {
                confirmBtn.EmitSignal("pressed");
            }
            catch { }
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!stable && DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            var newScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (newScreen is not NChooseABundleSelectionScreen)
            {
                stable = true;
            }
        }

        return new ActionResponsePayload
        {
            action = "confirm_bundle",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = BuildActionState("confirm_bundle", screen)
        };
    }

    private static async Task<ActionResponsePayload> ExecuteChooseRestOptionAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanChooseRestOption(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "choose_rest_option",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "choose_rest_option requires option_index.", new
            {
                action = "choose_rest_option"
            });
        }

        var options = RunManager.Instance.RestSiteSynchronizer.GetLocalOptions();
        if (options == null || request.option_index < 0 || request.option_index >= options.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "choose_rest_option",
                option_index = request.option_index,
                option_count = options?.Count ?? 0
            });
        }

        if (!options[request.option_index.Value].IsEnabled)
        {
            throw new ApiException(409, "invalid_target", "The selected rest option is disabled.", new
            {
                action = "choose_rest_option",
                option_index = request.option_index
            });
        }

        var selectedOption = options[request.option_index.Value];
        var selectedOptionId = selectedOption.OptionId ?? string.Empty;
        var runState = RunManager.Instance.DebugOnlyGetState();
        var localPlayer = GameStateService.GetLocalPlayer(runState);
        var requiresTarget = GameStateService.RestOptionRequiresTarget(selectedOption, runState, localPlayer);
        Player? targetPlayer = null;
        if (requiresTarget)
        {
            targetPlayer = ResolveRestOptionTarget(request, runState, localPlayer, selectedOption);
        }

        var chooseTask = RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(request.option_index.Value);

        bool stable;
        string? timeoutMessage = null;
        if (requiresTarget)
        {
            stable = await CompleteRestOptionTargetSelectionAsync(chooseTask, targetPlayer!, TimeSpan.FromSeconds(10));
        }
        else if (selectedOptionId.Equals("SMITH", StringComparison.OrdinalIgnoreCase))
        {
            // SMITH keeps the task open until the follow-up card selection
            // completes. Return as soon as the transition into that screen is visible.
            ObserveBackgroundResult(chooseTask, "choose_rest_option");
            stable = await WaitForRestOptionTransitionAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            var chooseTimeout = TimeSpan.FromSeconds(10);
            var completedChooseTask = await WaitForGameTaskAsync(chooseTask, chooseTimeout);
            var chooseOutcome = ClassifyGameTaskWait(chooseTask, completedChooseTask == null);
            if (chooseOutcome == GameTaskWaitOutcome.Failed)
            {
                throw new ApiException(409, "invalid_action", $"Rest option failed: {DescribeGameTaskFailure(chooseTask)}.", new
                {
                    action = "choose_rest_option",
                    option_index = request.option_index,
                    option_id = selectedOption.OptionId
                });
            }

            stable = chooseOutcome == GameTaskWaitOutcome.Completed && (await completedChooseTask!);
            if (chooseOutcome == GameTaskWaitOutcome.TimedOut)
            {
                ObserveBackgroundResult(chooseTask, "choose_rest_option");
                timeoutMessage = GameTaskWaitPolicy.DescribeTimeout("choose_rest_option", chooseTimeout);
            }

            var transitionStable = await WaitForRestOptionTransitionAsync(TimeSpan.FromSeconds(stable ? 2 : 10));
            if (!stable)
            {
                stable = transitionStable;
            }
            else
            {
                stable = transitionStable || stable;
            }
        }

        return new ActionResponsePayload
        {
            action = "choose_rest_option",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : timeoutMessage ?? "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static Player ResolveRestOptionTarget(
        ActionRequest request,
        RunState? runState,
        Player? localPlayer,
        RestSiteOption selectedOption)
    {
        var targetIndexSpace = GameStateService.GetRestOptionTargetIndexSpace(selectedOption, runState, localPlayer) ?? "run.players";
        var validTargetIndices = GameStateService.GetRestOptionTargetIndices(runState, localPlayer, allowSelf: false);
        if (request.target_index == null)
        {
            throw new ApiException(409, "invalid_target", "This rest option requires target_index.", new
            {
                action = "choose_rest_option",
                option_index = request.option_index,
                option_id = selectedOption.OptionId,
                target_index_space = targetIndexSpace,
                valid_target_indices = validTargetIndices
            });
        }

        if (!validTargetIndices.Contains(request.target_index.Value))
        {
            throw new ApiException(409, "invalid_target", "target_index is out of range for run.players[].", new
            {
                action = "choose_rest_option",
                option_index = request.option_index,
                option_id = selectedOption.OptionId,
                target_index = request.target_index,
                target_index_space = targetIndexSpace,
                valid_target_indices = validTargetIndices
            });
        }

        return GameStateService.ResolveRunPlayerTarget(runState, request.target_index.Value)
            ?? throw new ApiException(409, "invalid_target", "target_index is out of range for run.players[].", new
            {
                action = "choose_rest_option",
                option_index = request.option_index,
                option_id = selectedOption.OptionId,
                target_index = request.target_index,
                target_index_space = targetIndexSpace,
                valid_target_indices = validTargetIndices
            });
    }

    private static async Task<bool> CompleteRestOptionTargetSelectionAsync(
        Task<bool> chooseTask,
        Player targetPlayer,
        TimeSpan timeout)
    {
        var targetManager = await WaitForTargetManagerSelectionAsync(timeout);
        if (targetManager == null)
        {
            ObserveBackgroundResult(chooseTask, "choose_rest_option");
            return false;
        }

        var targetNode = ResolveRestSiteTargetNode(targetPlayer)
            ?? throw new ApiException(503, "state_unavailable", "Rest-site target node is unavailable.", new
            {
                action = "choose_rest_option",
                target_player_id = targetPlayer.NetId.ToString()
            }, retryable: true);

        targetManager.OnNodeHovered(targetNode);
        targetManager.Call(NTargetManager.MethodName.FinishTargeting, false);

        var result = await WaitForTaskResultAsync(chooseTask, timeout);
        if (result != true)
        {
            if (result == null)
            {
                ObserveBackgroundResult(chooseTask, "choose_rest_option");
            }

            return false;
        }

        return await WaitForRestOptionTransitionAsync(TimeSpan.FromSeconds(2)) || result.Value;
    }

    private static async Task<NTargetManager?> WaitForTargetManagerSelectionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var targetManager = NTargetManager.Instance;
            if (targetManager != null && GodotObject.IsInstanceValid(targetManager) && targetManager.IsInSelection)
            {
                return targetManager;
            }

            await WaitForNextFrameAsync();
        }

        var finalTargetManager = NTargetManager.Instance;
        return finalTargetManager != null && GodotObject.IsInstanceValid(finalTargetManager) && finalTargetManager.IsInSelection
            ? finalTargetManager
            : null;
    }

    private static Node? ResolveRestSiteTargetNode(Player targetPlayer)
    {
        var restSiteRoom = NRestSiteRoom.Instance;
        if (restSiteRoom == null || !GodotObject.IsInstanceValid(restSiteRoom))
        {
            return null;
        }

        var character = restSiteRoom.GetCharacterForPlayer(targetPlayer)
            ?? restSiteRoom.Characters.FirstOrDefault(candidate => candidate.Player.NetId == targetPlayer.NetId);
        return character != null && GodotObject.IsInstanceValid(character) ? character : null;
    }

    /// <summary>
    /// Bounded wait on a game task. Returns the original task when it finished before the
    /// deadline, or <c>null</c> when the deadline passed while it was still running. The
    /// task object is handed back so the caller can keep observing it in the background.
    /// </summary>
    private static async Task<Task<T>?> WaitForGameTaskAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (completedTask == task)
        {
            return task;
        }

        // The delay can win the race by a hair even when the game task has already
        // finished. Reporting a finished task as a timeout would also make the
        // "await the completed task" sites dereference null, so treat a completed
        // task as completed.
        return task.IsCompleted ? task : null;
    }

    /// <summary>
    /// Bounded wait on a task without a result, mirroring the generic overload.
    /// </summary>
    private static async Task<Task?> WaitForGameTaskAsync(Task task, TimeSpan timeout)
    {
        var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (completedTask == task)
        {
            return task;
        }

        return task.IsCompleted ? task : null;
    }

    /// <summary>
    /// Classifies a wait that already returned. <paramref name="deadlineReached"/> is true
    /// when the bounded wait returned <c>null</c>.
    /// </summary>
    private static GameTaskWaitOutcome ClassifyGameTaskWait(Task task, bool deadlineReached)
    {
        return GameTaskWaitPolicy.Classify(
            taskCompleted: task.IsCompleted,
            taskFaulted: task.IsFaulted || task.IsCanceled,
            deadlineReached: deadlineReached);
    }

    /// <summary>
    /// Failure reason for a game task that already finished unsuccessfully, without re-reading
    /// <c>Task.Exception</c> at the call site. Kept neutral instead of reusing
    /// <c>BackgroundTaskOutcome.DescribeFailure</c>: that helper's wording is scoped to shop
    /// purchases, while these sites span save, rest, event, lobby, and console actions.
    /// </summary>
    private static string DescribeGameTaskFailure(Task task)
    {
        if (task.IsCanceled)
        {
            return "the game task was canceled";
        }

        return task.IsFaulted ? "the game task faulted" : "the game task failed";
    }

    /// <summary>
    /// Honest pending response for a game task that is still running after its deadline.
    /// </summary>
    private static ActionResponsePayload BuildGameTaskTimeoutResponse(string action, TimeSpan timeout)
    {
        return new ActionResponsePayload
        {
            action = action,
            status = "pending",
            stable = false,
            message = GameTaskWaitPolicy.DescribeTimeout(action, timeout),
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool?> WaitForTaskResultAsync(Task<bool> task, TimeSpan timeout)
    {
        var completedTask = await WaitForGameTaskAsync<bool>(task, timeout);
        if (completedTask == null)
        {
            return null;
        }

        return await completedTask;
    }

    /// <summary>
    /// Waits for rest site state to change after choosing an option.
    /// Detects: screen change (SMITH 闂?card selection), ProceedButton appearance
    /// (HEAL), or options list change.
    /// </summary>
    private static async Task<bool> WaitForRestOptionTransitionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();

            // Screen changed entirely (e.g. SMITH opened card selection)
            if (currentScreen is not NRestSiteRoom restSiteRoom)
            {
                return true;
            }

            // ProceedButton became available (e.g. after HEAL)
            var proceedButton = restSiteRoom.ProceedButton;
            if (proceedButton != null && GodotObject.IsInstanceValid(proceedButton) && proceedButton.IsEnabled)
            {
                return true;
            }

            var options = RunManager.Instance.RestSiteSynchronizer.GetLocalOptions();
            if (options.Count == 0 || options.All(static option => !option.IsEnabled))
            {
                restSiteRoom.Call(NRestSiteRoom.MethodName.ShowProceedButton);
                ActiveScreenContext.Instance.Update();

                await WaitForNextFrameAsync();
                proceedButton = restSiteRoom.ProceedButton;
                return proceedButton != null && GodotObject.IsInstanceValid(proceedButton) && proceedButton.IsEnabled;
            }
        }

        return false;
    }

    private static async Task<ActionResponsePayload> ExecuteOpenShopInventoryAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanOpenShopInventory(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "open_shop_inventory",
                screen
            });
        }

        var fakeMerchantButton = GameStateService.GetFakeMerchantButton(currentScreen);
        if (currentScreen is NMerchantRoom merchantRoom)
        {
            merchantRoom.OpenInventory();
        }
        else if (fakeMerchantButton != null)
        {
            fakeMerchantButton.ForceClick();
        }
        else
        {
            throw new ApiException(503, "state_unavailable", "Shop inventory control is unavailable.", new
            {
                action = "open_shop_inventory",
                screen
            }, retryable: true);
        }

        var stable = await WaitForShopInventoryOpenAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "open_shop_inventory",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteCloseShopInventoryAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanCloseShopInventory(currentScreen) || currentScreen is not NMerchantInventory inventoryScreen)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "close_shop_inventory",
                screen
            });
        }

        var backButton = inventoryScreen.GetNodeOrNull<NButton>("%BackButton")
            ?? throw new ApiException(503, "state_unavailable", "Shop back button not found.", new
            {
                action = "close_shop_inventory",
                screen
            }, retryable: true);

        backButton.ForceClick();
        var stable = await WaitForShopInventoryCloseAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "close_shop_inventory",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteBuyCardAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanBuyShopCard(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "buy_card",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "buy_card requires option_index.", new
            {
                action = "buy_card"
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "buy_card",
                screen
            }, retryable: true);

        var cards = GameStateService.GetMerchantCardEntries(currentScreen).ToList();
        if (request.option_index < 0 || request.option_index >= cards.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "buy_card",
                option_index = request.option_index,
                option_count = cards.Count
            });
        }

        var entry = cards[request.option_index.Value];
        if (!entry.IsStocked)
        {
            throw new ApiException(409, "invalid_target", "The selected card is out of stock.", new
            {
                action = "buy_card",
                option_index = request.option_index
            });
        }

        var previousGold = inventory.Player.Gold;
        var previousCardId = entry.CreationResult?.Card.Id.Entry;
        var purchaseTimeout = TimeSpan.FromSeconds(10);
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        var completedPurchaseTask = await WaitForGameTaskAsync(purchaseTask, purchaseTimeout);
        var purchaseOutcome = ClassifyGameTaskWait(purchaseTask, completedPurchaseTask == null);
        if (purchaseOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundResult(purchaseTask, "buy_card");
            return BuildGameTaskTimeoutResponse("buy_card", purchaseTimeout);
        }

        if (purchaseOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Card purchase failed: {DescribeGameTaskFailure(purchaseTask)}.", new
            {
                action = "buy_card",
                option_index = request.option_index
            });
        }

        var success = await completedPurchaseTask!;
        if (!success)
        {
            throw new ApiException(409, "invalid_action", "Card purchase failed in the current state.", new
            {
                action = "buy_card",
                option_index = request.option_index
            });
        }

        var stable = await WaitForMerchantCardPurchaseAsync(inventory.Player, entry, previousGold, previousCardId, purchaseTimeout);
        return new ActionResponsePayload
        {
            action = "buy_card",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteBuyRelicAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanBuyShopRelic(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "buy_relic",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "buy_relic requires option_index.", new
            {
                action = "buy_relic"
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "buy_relic",
                screen
            }, retryable: true);

        var relics = GameStateService.GetMerchantRelicEntries(currentScreen).ToList();
        if (request.option_index < 0 || request.option_index >= relics.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "buy_relic",
                option_index = request.option_index,
                option_count = relics.Count
            });
        }

        var entry = relics[request.option_index.Value];
        if (!entry.IsStocked)
        {
            throw new ApiException(409, "invalid_target", "The selected relic is out of stock.", new
            {
                action = "buy_relic",
                option_index = request.option_index
            });
        }

        var previousGold = inventory.Player.Gold;
        var previousRelicId = entry.Model?.Id.Entry;
        var purchaseTimeout = TimeSpan.FromSeconds(10);
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        var completedPurchaseTask = await WaitForGameTaskAsync(purchaseTask, purchaseTimeout);
        var purchaseOutcome = ClassifyGameTaskWait(purchaseTask, completedPurchaseTask == null);
        if (purchaseOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundResult(purchaseTask, "buy_relic");
            return BuildGameTaskTimeoutResponse("buy_relic", purchaseTimeout);
        }

        if (purchaseOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Relic purchase failed: {DescribeGameTaskFailure(purchaseTask)}.", new
            {
                action = "buy_relic",
                option_index = request.option_index
            });
        }

        var success = await completedPurchaseTask!;
        if (!success)
        {
            throw new ApiException(409, "invalid_action", "Relic purchase failed in the current state.", new
            {
                action = "buy_relic",
                option_index = request.option_index
            });
        }

        var stable = await WaitForMerchantRelicPurchaseAsync(inventory.Player, entry, previousGold, previousRelicId, purchaseTimeout);
        return new ActionResponsePayload
        {
            action = "buy_relic",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteBuyPotionAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanBuyShopPotion(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "buy_potion",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "buy_potion requires option_index.", new
            {
                action = "buy_potion"
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "buy_potion",
                screen
            }, retryable: true);

        var potions = GameStateService.GetMerchantPotionEntries(currentScreen).ToList();
        if (request.option_index < 0 || request.option_index >= potions.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "buy_potion",
                option_index = request.option_index,
                option_count = potions.Count
            });
        }

        var entry = potions[request.option_index.Value];
        if (!entry.IsStocked)
        {
            throw new ApiException(409, "invalid_target", "The selected potion is out of stock.", new
            {
                action = "buy_potion",
                option_index = request.option_index
            });
        }

        var previousGold = inventory.Player.Gold;
        var previousPotionId = entry.Model?.Id.Entry;
        var purchaseTimeout = TimeSpan.FromSeconds(10);
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        var completedPurchaseTask = await WaitForGameTaskAsync(purchaseTask, purchaseTimeout);
        var purchaseOutcome = ClassifyGameTaskWait(purchaseTask, completedPurchaseTask == null);
        if (purchaseOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundResult(purchaseTask, "buy_potion");
            return BuildGameTaskTimeoutResponse("buy_potion", purchaseTimeout);
        }

        if (purchaseOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Potion purchase failed: {DescribeGameTaskFailure(purchaseTask)}.", new
            {
                action = "buy_potion",
                option_index = request.option_index
            });
        }

        var success = await completedPurchaseTask!;
        if (!success)
        {
            throw new ApiException(409, "invalid_action", "Potion purchase failed in the current state.", new
            {
                action = "buy_potion",
                option_index = request.option_index
            });
        }

        var stable = await WaitForMerchantPotionPurchaseAsync(inventory.Player, entry, previousGold, previousPotionId, purchaseTimeout);
        return new ActionResponsePayload
        {
            action = "buy_potion",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteRemoveCardAtShopAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanRemoveCardAtShop(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "remove_card_at_shop",
                screen
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "remove_card_at_shop",
                screen
            }, retryable: true);

        var entry = GameStateService.GetMerchantCardRemovalEntry(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop card removal service is unavailable.", new
            {
                action = "remove_card_at_shop",
                screen
            }, retryable: true);

        // Fire-and-forget: merchant card removal opens deck selection and blocks
        // until the player confirms a card. Do not await the full task here, but a
        // purchase that already failed must not look like a pending transition.
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        ObserveBackgroundResult(purchaseTask, "remove_card_at_shop");
        var stable = await WaitForShopCardRemovalTransitionAsync(TimeSpan.FromSeconds(10));

        var purchaseFailure = BackgroundTaskOutcome.DescribeFailure(
            isCompleted: purchaseTask.IsCompleted,
            isFaulted: purchaseTask.IsFaulted,
            isCanceled: purchaseTask.IsCanceled,
            result: purchaseTask.Status == TaskStatus.RanToCompletion ? purchaseTask.Result : null);
        if (!stable && purchaseFailure != null)
        {
            throw new ApiException(409, "invalid_action", $"Card removal failed: {purchaseFailure}.", new
            {
                action = "remove_card_at_shop",
                screen
            });
        }

        return new ActionResponsePayload
        {
            action = "remove_card_at_shop",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteSelectCharacterAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var multiplayerTestScene = GameStateService.GetMultiplayerTestScene();

        if (multiplayerTestScene != null)
        {
            return await ExecuteSelectMultiplayerLobbyCharacterAsync(request, multiplayerTestScene, screen);
        }

        if (currentScreen is not NCharacterSelectScreen characterSelectScreen || !GameStateService.CanSelectCharacter(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "select_character",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "select_character requires option_index.", new
            {
                action = "select_character"
            });
        }

        var buttons = GameStateService.GetCharacterSelectButtons(currentScreen);
        if (request.option_index < 0 || request.option_index >= buttons.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "select_character",
                option_index = request.option_index,
                option_count = buttons.Count
            });
        }

        var button = buttons[request.option_index.Value];
        if (button.IsLocked)
        {
            throw new ApiException(409, "invalid_target", "The selected character is locked.", new
            {
                action = "select_character",
                option_index = request.option_index,
                character_id = button.Character.Id.Entry
            });
        }

        if (!button.IsEnabled || !button.IsVisibleInTree())
        {
            throw new ApiException(409, "invalid_target", "The selected character cannot be chosen right now.", new
            {
                action = "select_character",
                option_index = request.option_index,
                character_id = button.Character.Id.Entry
            });
        }

        var previousCharacterId = characterSelectScreen.Lobby.LocalPlayer.character.Id.Entry;
        button.Select();
        var stable = await WaitForCharacterSelectionTransitionAsync(characterSelectScreen, button.Character.Id.Entry, previousCharacterId, TimeSpan.FromSeconds(5));

        return new ActionResponsePayload
        {
            action = "select_character",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteSelectMultiplayerLobbyCharacterAsync(ActionRequest request, NMultiplayerTest scene, string screen)
    {
        if (!GameStateService.CanSelectCharacter(ActiveScreenContext.Instance.GetCurrentScreen()))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "select_character",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "select_character requires option_index.", new
            {
                action = "select_character"
            });
        }

        var characters = GameStateService.GetMultiplayerLobbyCharacters();
        if (request.option_index < 0 || request.option_index >= characters.Length)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "select_character",
                option_index = request.option_index,
                option_count = characters.Length
            });
        }

        var paginator = GameStateService.GetMultiplayerTestCharacterPaginator(scene)
            ?? throw new ApiException(503, "state_unavailable", "Multiplayer character selector is unavailable.", new
            {
                action = "select_character",
                screen
            }, retryable: true);

        var lobby = GameStateService.GetMultiplayerTestLobby(scene)
            ?? throw new ApiException(503, "state_unavailable", "Multiplayer lobby is unavailable.", new
            {
                action = "select_character",
                screen
            }, retryable: true);

        var previousCharacterId = lobby.LocalPlayer.character.Id.Entry;
        var currentCharacterId = characters[request.option_index.Value].Id.Entry;
        paginator.SetIndex(request.option_index.Value);
        var stable = await WaitForMultiplayerLobbyCharacterSelectionTransitionAsync(scene, currentCharacterId, previousCharacterId, TimeSpan.FromSeconds(5));

        return new ActionResponsePayload
        {
            action = "select_character",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteEmbarkAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (GameStateService.CanEmbark(currentScreen) && currentScreen is NMultiplayerLoadGameScreen loadScreen)
        {
            var loadEmbark = GameStateService.GetCharacterEmbarkButton(currentScreen)
                ?? throw new ApiException(503, "state_unavailable", "Embark button is unavailable.", new
                {
                    action = "embark",
                    screen
                }, retryable: true);

            loadEmbark.ForceClick();
            var loadStable = await WaitForLoadEmbarkTransitionAsync(loadScreen, TimeSpan.FromSeconds(10));

            return new ActionResponsePayload
            {
                action = "embark",
                status = loadStable ? "completed" : "pending",
                stable = loadStable,
                message = loadStable
                    ? "Action completed."
                    : MenuTransitionPolicy.DescribeUnsettled("embark", CurrentModalName()),
                state = GameStateService.BuildStatePayload()
            };
        }

        if (!GameStateService.CanEmbark(currentScreen) || currentScreen is not NCharacterSelectScreen characterSelectScreen)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "embark",
                screen
            });
        }

        var embarkButton = GameStateService.GetCharacterEmbarkButton(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Embark button is unavailable.", new
            {
                action = "embark",
                screen
            }, retryable: true);

        embarkButton.ForceClick();
        var stable = await WaitForEmbarkTransitionAsync(characterSelectScreen, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "embark",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable
                ? "Action completed."
                : MenuTransitionPolicy.DescribeUnsettled("embark", CurrentModalName()),
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteUnreadyAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var multiplayerTestScene = GameStateService.GetMultiplayerTestScene();

        if (multiplayerTestScene != null)
        {
            var multiplayerLobby = GameStateService.GetMultiplayerTestLobby(multiplayerTestScene)
                ?? throw new ApiException(503, "state_unavailable", "Multiplayer lobby is unavailable.", new
                {
                    action = "unready",
                    screen
                }, retryable: true);

            if (!GameStateService.CanUnready(currentScreen))
            {
                throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
                {
                    action = "unready",
                    screen
                });
            }

            multiplayerLobby.SetReady(ready: false);
            var multiplayerStable = await WaitForMultiplayerLobbyReadyTransitionAsync(multiplayerTestScene, ready: false, expectRunStart: false, TimeSpan.FromSeconds(5));

            return new ActionResponsePayload
            {
                action = "unready",
                status = multiplayerStable ? "completed" : "pending",
                stable = multiplayerStable,
                message = multiplayerStable ? "Action completed." : "Action queued but state is still transitioning.",
                state = GameStateService.BuildStatePayload()
            };
        }

        if (!GameStateService.CanUnready(currentScreen) || currentScreen is not NCharacterSelectScreen characterSelectScreen)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "unready",
                screen
            });
        }

        characterSelectScreen.Lobby.SetReady(ready: false);
        var stable = await WaitForLobbyReadyTransitionAsync(characterSelectScreen, ready: false, TimeSpan.FromSeconds(5));

        return new ActionResponsePayload
        {
            action = "unready",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteHostMultiplayerLobbyAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var scene = GameStateService.GetMultiplayerTestScene();

        if (!GameStateService.CanHostMultiplayerLobby(currentScreen) || scene == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "host_multiplayer_lobby",
                screen
            });
        }

        var startHostTask = InvokePrivateTask<bool>(scene, "StartHost", false)
            ?? throw new ApiException(503, "state_unavailable", "Multiplayer host entry point is unavailable.", new
            {
                action = "host_multiplayer_lobby",
                screen
            }, retryable: true);

        var hostTimeout = TimeSpan.FromSeconds(10);
        var completedHostTask = await WaitForGameTaskAsync(startHostTask, hostTimeout);
        var hostOutcome = ClassifyGameTaskWait(startHostTask, completedHostTask == null);
        if (hostOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundResult(startHostTask, "host_multiplayer_lobby");
            return BuildGameTaskTimeoutResponse("host_multiplayer_lobby", hostTimeout);
        }

        if (hostOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Failed to host the multiplayer lobby: {DescribeGameTaskFailure(startHostTask)}.", new
            {
                action = "host_multiplayer_lobby",
                screen
            });
        }

        var hostStarted = await completedHostTask!;
        if (!hostStarted)
        {
            throw new ApiException(409, "invalid_action", "Failed to host the multiplayer lobby.", new
            {
                action = "host_multiplayer_lobby",
                screen
            });
        }

        var stable = await WaitForMultiplayerLobbyHostTransitionAsync(scene, hostTimeout);

        return new ActionResponsePayload
        {
            action = "host_multiplayer_lobby",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteJoinMultiplayerLobbyAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var scene = GameStateService.GetMultiplayerTestScene();

        if (!GameStateService.CanJoinMultiplayerLobby(currentScreen) || scene == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "join_multiplayer_lobby",
                screen
            });
        }

        var joinHost = GameStateService.GetMultiplayerLobbyJoinHost();
        var joinPort = (ushort)GameStateService.GetMultiplayerLobbyJoinPort();
        var joinNetId = GameStateService.GetMultiplayerLobbyJoinNetIdHint();
        var initializer = new ENetClientConnectionInitializer(joinNetId, joinHost, joinPort);
        var joinTimeout = TimeSpan.FromSeconds(10);
        var joinTask = scene.JoinToHost(initializer);
        var completedJoinTask = await WaitForGameTaskAsync(joinTask, joinTimeout);
        var joinOutcome = ClassifyGameTaskWait(joinTask, completedJoinTask == null);
        if (joinOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundTask(joinTask, "join_multiplayer_lobby");
            return BuildGameTaskTimeoutResponse("join_multiplayer_lobby", joinTimeout);
        }

        if (joinOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Failed to join the multiplayer lobby: {DescribeGameTaskFailure(joinTask)}.", new
            {
                action = "join_multiplayer_lobby",
                screen,
                join_host = joinHost,
                join_port = joinPort,
                net_id = joinNetId
            });
        }

        if (GameStateService.GetMultiplayerTestLobby(scene) == null)
        {
            throw new ApiException(409, "invalid_action", "Failed to join the multiplayer lobby.", new
            {
                action = "join_multiplayer_lobby",
                screen,
                join_host = joinHost,
                join_port = joinPort,
                net_id = joinNetId
            });
        }

        var stable = await WaitForMultiplayerLobbyJoinTransitionAsync(scene, joinTimeout);

        return new ActionResponsePayload
        {
            action = "join_multiplayer_lobby",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteReadyMultiplayerLobbyAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var scene = GameStateService.GetMultiplayerTestScene();

        if (!GameStateService.CanReadyMultiplayerLobby(currentScreen) || scene == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "ready_multiplayer_lobby",
                screen
            });
        }

        var lobby = GameStateService.GetMultiplayerTestLobby(scene)
            ?? throw new ApiException(503, "state_unavailable", "Multiplayer lobby is unavailable.", new
            {
                action = "ready_multiplayer_lobby",
                screen
            }, retryable: true);
        var expectRunStart = lobby.Players.Count > 1 &&
            lobby.Players
                .Where(player => player.id != lobby.LocalPlayer.id)
                .All(player => player.isReady);

        InvokePrivateVoid(scene, "ReadyButtonPressed");
        var stable = await WaitForMultiplayerLobbyReadyTransitionAsync(scene, ready: true, expectRunStart, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "ready_multiplayer_lobby",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteDisconnectMultiplayerLobbyAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var scene = GameStateService.GetMultiplayerTestScene();

        if (!GameStateService.CanDisconnectMultiplayerLobby(currentScreen) || scene == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "disconnect_multiplayer_lobby",
                screen
            });
        }

        InvokePrivateVoid(scene, "Disconnect", NetError.Quit);
        var stable = await WaitForMultiplayerLobbyDisconnectTransitionAsync(scene, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "disconnect_multiplayer_lobby",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteAdjustAscensionAsync(int delta, string actionName)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var canAdjust = delta > 0
            ? GameStateService.CanIncreaseAscension(currentScreen)
            : GameStateService.CanDecreaseAscension(currentScreen);

        if (!canAdjust || currentScreen is not NCharacterSelectScreen characterSelectScreen)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = actionName,
                screen
            });
        }

        var targetAscension = characterSelectScreen.Lobby.Ascension + delta;
        characterSelectScreen.Lobby.SyncAscensionChange(targetAscension);
        var stable = await WaitForLobbyAscensionTransitionAsync(characterSelectScreen, targetAscension, TimeSpan.FromSeconds(5));

        return new ActionResponsePayload
        {
            action = actionName,
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteUsePotionAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "use_potion requires option_index.", new
            {
                action = "use_potion"
            });
        }

        if (!GameStateService.CanUsePotionAtIndex(currentScreen, combatState, runState, request.option_index.Value))
        {
            throw new ApiException(409, "invalid_action", "The selected potion cannot be used in the current state.", new
            {
                action = "use_potion",
                screen,
                option_index = request.option_index
            });
        }

        var player = GameStateService.GetLocalPlayer(runState)
            ?? throw new ApiException(503, "state_unavailable", "Local player is unavailable.", new
            {
                action = "use_potion",
                screen
            }, retryable: true);

        if (request.option_index < 0 || request.option_index >= player.PotionSlots.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "use_potion",
                option_index = request.option_index,
                option_count = player.PotionSlots.Count
            });
        }

        var potion = player.PotionSlots[request.option_index.Value]
            ?? throw new ApiException(409, "invalid_target", "The selected potion slot is empty.", new
            {
                action = "use_potion",
                option_index = request.option_index
            });

        var target = ResolvePotionTarget(request, combatState, potion);
        potion.EnqueueManualUse(target);
        var stable = await WaitForPotionUseTransitionAsync(player, request.option_index.Value, potion, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "use_potion",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteDiscardPotionAsync(ActionRequest request)
    {
        var runState = RunManager.Instance.DebugOnlyGetState();
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "discard_potion requires option_index.", new
            {
                action = "discard_potion"
            });
        }

        if (!GameStateService.CanDiscardPotionAtIndex(currentScreen, runState, request.option_index.Value))
        {
            throw new ApiException(409, "invalid_action", "The selected potion cannot be discarded in the current state.", new
            {
                action = "discard_potion",
                screen,
                option_index = request.option_index
            });
        }

        var player = GameStateService.GetLocalPlayer(runState)
            ?? throw new ApiException(503, "state_unavailable", "Local player is unavailable.", new
            {
                action = "discard_potion",
                screen
            }, retryable: true);

        if (request.option_index < 0 || request.option_index >= player.PotionSlots.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "discard_potion",
                option_index = request.option_index,
                option_count = player.PotionSlots.Count
            });
        }

        var potion = player.PotionSlots[request.option_index.Value]
            ?? throw new ApiException(409, "invalid_target", "The selected potion slot is empty.", new
            {
                action = "discard_potion",
                option_index = request.option_index
            });

        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new DiscardPotionGameAction(
            player,
            (uint)request.option_index.Value,
            CombatManager.Instance.IsInProgress));
        var stable = await WaitForPotionDiscardTransitionAsync(player, request.option_index.Value, potion, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "discard_potion",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static Task<ActionResponsePayload> ExecuteRunConsoleCommandAsync(ActionRequest request)
    {
        if (!AreDebugActionsEnabled())
        {
            throw new ApiException(409, "invalid_action", "run_console_command is disabled. Set STS2_ENABLE_DEBUG_ACTIONS=1 for development use.", new
            {
                action = "run_console_command"
            });
        }

        return ExecuteConsoleCommandCoreAsync(request.command);
    }

    internal static Task<ActionResponsePayload> ExecuteInternalConsoleCommandAsync(string command)
    {
        return ExecuteConsoleCommandCoreAsync(command);
    }

    internal static async Task<bool> StartLocalFourPlayerHostAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (currentScreen is not NMainMenu mainMenu)
        {
            throw new InvalidOperationException(Loc.T("请先回到主菜单，再邀请 AI 队友组队。"));
        }

        var submenu = mainMenu.SubmenuStack.GetSubmenuType<NMultiplayerSubmenu>();
        if (submenu == null)
        {
            mainMenu.Call("OpenMultiplayerSubmenu");
            await WaitForMainMenuSubmenuOpenAsync<NMultiplayerSubmenu>(mainMenu, TimeSpan.FromSeconds(5));
            submenu = mainMenu.SubmenuStack.GetSubmenuType<NMultiplayerSubmenu>()
                ?? throw new InvalidOperationException(Loc.T("找不到多人子菜单。"));
        }
        else
        {
            mainMenu.SubmenuStack.Push(submenu);
            await WaitForMainMenuSubmenuOpenAsync<NMultiplayerSubmenu>(mainMenu, TimeSpan.FromSeconds(5));
        }

        var opened = await InvokeFastHostAsync(submenu);
        if (!opened)
        {
            var screen = GameStateService.ResolveScreen(ActiveScreenContext.Instance.GetCurrentScreen());
            throw new TimeoutException("FastHost did not open character select. screen=" + screen + " methods=" + DescribeHostMethods());
        }

        return true;
    }

    private static async Task<bool> InvokeFastHostAsync(NMultiplayerSubmenu submenu)
    {
        var fastHostTimeout = TimeSpan.FromSeconds(10);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var method = typeof(NMultiplayerSubmenu).GetMethods(flags)
            .FirstOrDefault(candidate => candidate.Name == "FastHost");
        if (method == null)
        {
            throw new InvalidOperationException(Loc.T("找不到 FastHost。"));
        }

        var parameters = method.GetParameters();
        if (parameters.Length == 1)
        {
            foreach (var mode in EnumerateFastHostModes(parameters[0].ParameterType))
            {
                var result = method.Invoke(submenu, new[] { mode });
                if (result is Task task)
                {
                    var completedHostTask = await WaitForGameTaskAsync(task, fastHostTimeout);
                    if (ClassifyGameTaskWait(task, completedHostTask == null) != GameTaskWaitOutcome.Completed)
                    {
                        ObserveBackgroundTask(task, "invite_ai_teammate");
                        return false;
                    }
                }

                if (await WaitForCharacterSelectOpenAsync(TimeSpan.FromSeconds(8)))
                {
                    return true;
                }
            }
        }
        else
        {
            var result = method.Invoke(submenu, Array.Empty<object>());
            if (result is Task task)
            {
                var completedHostTask = await WaitForGameTaskAsync(task, fastHostTimeout);
                if (ClassifyGameTaskWait(task, completedHostTask == null) != GameTaskWaitOutcome.Completed)
                {
                    ObserveBackgroundTask(task, "invite_ai_teammate");
                    return false;
                }
            }

            return await WaitForCharacterSelectOpenAsync(TimeSpan.FromSeconds(8));
        }

        return false;
    }

    private static IEnumerable<object> EnumerateFastHostModes(Type modeType)
    {
        var values = new List<(string Name, object Value)>();
        if (modeType.IsEnum)
        {
            foreach (var value in Enum.GetValues(modeType))
            {
                if (value != null)
                {
                    values.Add((Enum.GetName(modeType, value) ?? string.Empty, value));
                }
            }
        }
        else
        {
            foreach (var field in modeType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = field.GetValue(null);
                if (value != null)
                {
                    values.Add((field.Name, value));
                }
            }
        }

        foreach (var item in values.OrderBy(value =>
                     value.Name.Contains("Standard", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            yield return item.Value;
        }
    }

    private static string DescribeHostMethods()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return string.Join("; ", typeof(NMultiplayerSubmenu).GetMethods(flags)
            .Where(method => method.Name.Contains("Host", StringComparison.OrdinalIgnoreCase)
                             || method.Name.Contains("Fast", StringComparison.OrdinalIgnoreCase)
                             || method.Name.Contains("Standard", StringComparison.OrdinalIgnoreCase))
            .Select(method => method.Name + "(" + string.Join(",", method.GetParameters()
                .Select(parameter => parameter.ParameterType.Name)) + ")"));
    }

    private static async Task<ActionResponsePayload> ExecuteConsoleCommandCoreAsync(string? rawCommand)
    {
        var command = rawCommand?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ApiException(400, "invalid_request", "command is required.", new
            {
                action = "run_console_command"
            });
        }

        NDevConsole console;
        try
        {
            console = NDevConsole.Instance;
        }
        catch (Exception ex)
        {
            throw new ApiException(503, "state_unavailable", $"Dev console is unavailable: {ex.Message}", new
            {
                action = "run_console_command",
                command
            }, retryable: true);
        }

        var devConsole = GetDevConsoleCore(console)
            ?? throw new ApiException(503, "state_unavailable", "Dev console backend is unavailable.", new
            {
                action = "run_console_command",
                command
            }, retryable: true);

        // A command implementation can throw (BestiaryConsoleCmd.Process does on a null model) and the
        // exception used to reach the HTTP layer as a bare 500 internal_error. Report it as a
        // client-visible, non-retryable invalid_action, keeping the original type and message so the
        // cause is still visible instead of being swallowed or dressed up as success.
        CmdResult result;
        try
        {
            var runState = RunManager.Instance.DebugOnlyGetState();
            var player = GameStateService.GetLocalPlayer(runState);
            result = devConsole.ProcessNetCommand(player, command);
        }
        catch (Exception ex)
        {
            throw new ApiException(409, "invalid_action", $"Console command failed: {ex.GetType().Name}: {ex.Message}", new
            {
                action = "run_console_command",
                command
            });
        }

        if (!result.success)
        {
            throw new ApiException(409, "invalid_action", string.IsNullOrWhiteSpace(result.msg) ? "Console command failed." : result.msg, new
            {
                action = "run_console_command",
                command
            });
        }

        var consoleTimeout = TimeSpan.FromSeconds(10);
        var consoleTimedOut = false;
        if (result.task != null)
        {
            var commandTask = result.task;
            var completedCommandTask = await WaitForGameTaskAsync(commandTask, consoleTimeout);
            var commandOutcome = ClassifyGameTaskWait(commandTask, completedCommandTask == null);
            if (commandOutcome == GameTaskWaitOutcome.Failed)
            {
                throw new ApiException(409, "invalid_action", $"Console command failed: {DescribeGameTaskFailure(commandTask)}.", new
                {
                    action = "run_console_command",
                    command
                });
            }

            consoleTimedOut = commandOutcome == GameTaskWaitOutcome.TimedOut;
            if (consoleTimedOut)
            {
                ObserveBackgroundTask(commandTask, "run_console_command");
            }
        }

        var screenStable = await WaitForConsoleCommandStabilityAsync(consoleTimeout);

        // A stable screen is not proof that a command the game handed back as a task finished: when
        // the task timed out it is still running in the background, so the response must not claim
        // completion - and it must not claim a stable state either.
        var completed = screenStable && !consoleTimedOut;

        return new ActionResponsePayload
        {
            action = "run_console_command",
            status = completed ? "completed" : "pending",
            stable = completed,
            message = completed
                ? string.IsNullOrWhiteSpace(result.msg) ? "Console command executed." : result.msg
                : consoleTimedOut
                    ? GameTaskWaitPolicy.DescribeTimeout("run_console_command", consoleTimeout)
                    : "Console command executed but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForConsoleCommandStabilityAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsStableScreenState(ActiveScreenContext.Instance.GetCurrentScreen(), allowMapScreen: true))
            {
                return true;
            }
        }

        return IsStableScreenState(ActiveScreenContext.Instance.GetCurrentScreen(), allowMapScreen: true);
    }

    private static DevConsole? GetDevConsoleCore(NDevConsole console)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(NDevConsole).GetField("_devConsole", flags);
        return field?.GetValue(console) as DevConsole;
    }

    private static bool AreDebugActionsEnabled()
    {
        var raw = ReadEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadEnvironmentVariable(string name)
    {
        var processValue = System.Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(processValue))
        {
            return processValue;
        }

        try
        {
            var godotValue = OS.GetEnvironment(name);
            if (!string.IsNullOrWhiteSpace(godotValue))
            {
                return godotValue;
            }
        }
        catch
        {
        }

        var userValue = System.Environment.GetEnvironmentVariable(name, System.EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(userValue))
        {
            return userValue;
        }

        return System.Environment.GetEnvironmentVariable(name, System.EnvironmentVariableTarget.Machine);
    }

    private static async Task<ActionResponsePayload> ExecuteConfirmModalAsync()
    {
        return await ExecuteModalButtonAsync("confirm_modal", GameStateService.GetModalConfirmButton);
    }

    private static async Task<ActionResponsePayload> ExecuteDismissModalAsync()
    {
        return await ExecuteModalButtonAsync("dismiss_modal", GameStateService.GetModalCancelButton);
    }

    /// <summary>
    /// Host side: reload the saved multiplayer run over local ENet (fastmp host_standard) and launch the companion so it rejoins as its saved player.
    /// </summary>
    private static async Task<ActionResponsePayload> ExecuteContinueAiTeammateAsync()
    {
        var payload = GameStateService.BuildStatePayload();
        var error = CoopLaunchPolicy.GetError(
            InstanceRole.IsCompanion,
            AgentRuntime.Instance.PlayRunning,
            payload.screen,
            AgentRuntime.Instance.Settings);
        if (error != null)
        {
            throw new ApiException(409, "invalid_action", error, new
            {
                action = "continue_ai_teammate",
                screen = payload.screen
            });
        }

        if (!GameStateService.CanContinueAiTeammate(ActiveScreenContext.Instance.GetCurrentScreen()))
        {
            throw new ApiException(409, "invalid_action", "No saved multiplayer run to continue.", new
            {
                action = "continue_ai_teammate",
                screen = payload.screen
            });
        }

        // The load path canonicalizes the save against this process's local player id and, on a
        // mismatch, renames current_run_mp.save and its .backup to *.VAL.corrupt without restoring
        // them. Check both ids against the save first: a mismatched --clientId must not be allowed to
        // destroy the only co-op save slot, and retrying it cannot succeed, so this is invalid_action.
        CoopSaveProbe.TryReadMultiplayerSaveNetIds(out var saveNetIds, out _);
        var localPlayerId = CoopSaveProbe.ResolveLoadLocalPlayerId();
        var hostMismatch = CoopSavePrecheckPolicy.DescribeHostMismatch(saveNetIds, localPlayerId);
        if (hostMismatch != null)
        {
            throw new ApiException(409, "invalid_action", hostMismatch, new
            {
                action = "continue_ai_teammate",
                save_player_net_ids = saveNetIds,
                local_player_id = localPlayerId
            });
        }

        if (!CoopSaveProbe.TryResolveCompanionClientId(out var companionClientId, out var companionIdError))
        {
            throw new ApiException(409, "invalid_action", companionIdError!, new
            {
                action = "continue_ai_teammate"
            });
        }

        var companionMismatch = CoopSavePrecheckPolicy.DescribeCompanionMismatch(saveNetIds, companionClientId);
        if (companionMismatch != null)
        {
            throw new ApiException(409, "invalid_action", companionMismatch, new
            {
                action = "continue_ai_teammate",
                save_player_net_ids = saveNetIds,
                companion_client_id = companionClientId
            });
        }

        await AgentRuntime.Instance.ContinueDualInstanceAsync(AgentRuntime.Instance.Settings, CancellationToken.None);
        // Same classification as invite_ai_teammate: read the structured outcome, never the localized text.
        var outcome = AgentRuntime.Instance.DualLaunchOutcome;
        var message = AgentRuntime.Instance.DualStatus;
        if (DualLaunchOutcomePolicy.IsInProgress(outcome))
        {
            return new ActionResponsePayload
            {
                action = "continue_ai_teammate",
                status = "pending",
                stable = false,
                message = message,
                state = GameStateService.BuildStatePayload()
            };
        }

        if (DualLaunchOutcomePolicy.IsFailure(outcome) || outcome == DualLaunchOutcome.Idle)
        {
            // Retryable: the usual cause is port 33771 still held by the previous run in this process,
            // which a game restart clears.
            throw new ApiException(409, "continue_failed", message, new
            {
                action = "continue_ai_teammate",
                screen = GameStateService.BuildStatePayload().screen,
                outcome = outcome.ToString()
            }, retryable: true);
        }

        return new ActionResponsePayload
        {
            action = "continue_ai_teammate",
            status = "completed",
            stable = true,
            message = message,
            state = GameStateService.BuildStatePayload()
        };
    }

    /// <summary>
    /// Opens the multiplayer submenu and presses its private "load run" button. With fastmp injected the game hosts the saved run on ENet:33771.
    /// </summary>
    internal static async Task<bool> StartLocalLoadAsync(CancellationToken cancellationToken = default)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (currentScreen is not NMainMenu mainMenu)
        {
            throw new InvalidOperationException(Loc.T("请先回到主菜单，再继续联机对局。"));
        }

        var submenu = mainMenu.SubmenuStack.GetSubmenuType<NMultiplayerSubmenu>();
        if (submenu == null)
        {
            mainMenu.Call("OpenMultiplayerSubmenu");
            await WaitForMainMenuSubmenuOpenAsync<NMultiplayerSubmenu>(mainMenu, TimeSpan.FromSeconds(5), cancellationToken);
            submenu = mainMenu.SubmenuStack.GetSubmenuType<NMultiplayerSubmenu>()
                ?? throw new InvalidOperationException(Loc.T("找不到多人子菜单。"));
        }
        else
        {
            mainMenu.SubmenuStack.Push(submenu);
            await WaitForMainMenuSubmenuOpenAsync<NMultiplayerSubmenu>(mainMenu, TimeSpan.FromSeconds(5), cancellationToken);
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var startLoad = typeof(NMultiplayerSubmenu).GetMethod("StartLoad", flags)
            ?? throw new InvalidOperationException(Loc.T("找不到读档方法 StartLoad。"));
        startLoad.Invoke(submenu, new object?[] { null });

        var opened = await WaitForMainMenuSubmenuOpenAsync<NMultiplayerLoadGameScreen>(mainMenu, TimeSpan.FromSeconds(10), cancellationToken);
        if (!opened)
        {
            var modal = GameStateService.GetOpenModal();
            if (modal != null)
            {
                // The open modal is what is actually observable here; the port is only the usual
                // cause, so it is reported as a possibility instead of being asserted as the reason.
                throw new InvalidOperationException(Loc.T(
                    "读档开房失败：当前弹窗是 {0}。常见原因是本地直连端口 33771 仍被上一局占着；可重启游戏后再试。",
                    modal.GetType().Name));
            }
            throw new TimeoutException(Loc.T("读档后没有进入多人读档界面。"));
        }

        return true;
    }

    private static async Task<ActionResponsePayload> ExecuteInviteAiTeammateAsync()
    {
        var payload = GameStateService.BuildStatePayload();
        var error = CoopLaunchPolicy.GetError(
            InstanceRole.IsCompanion,
            AgentRuntime.Instance.PlayRunning,
            payload.screen,
            AgentRuntime.Instance.Settings);
        if (error != null)
        {
            throw new ApiException(409, "invalid_action", error, new
            {
                action = "invite_ai_teammate",
                screen = payload.screen
            });
        }

        await AgentRuntime.Instance.LaunchDualInstanceAsync(AgentRuntime.Instance.Settings, CancellationToken.None);
        // Classify on the structured outcome. DualStatus is localized display text, so matching
        // substrings in it misreports every failure as success in a non-Chinese client.
        var outcome = AgentRuntime.Instance.DualLaunchOutcome;
        var message = AgentRuntime.Instance.DualStatus;
        if (DualLaunchOutcomePolicy.IsInProgress(outcome))
        {
            return new ActionResponsePayload
            {
                action = "invite_ai_teammate",
                status = "pending",
                stable = false,
                message = message,
                state = GameStateService.BuildStatePayload()
            };
        }

        if (DualLaunchOutcomePolicy.IsFailure(outcome) || outcome == DualLaunchOutcome.Idle)
        {
            throw new ApiException(409, "invite_failed", message, new
            {
                action = "invite_ai_teammate",
                screen = GameStateService.BuildStatePayload().screen,
                outcome = outcome.ToString()
            });
        }

        return new ActionResponsePayload
        {
            action = "invite_ai_teammate",
            status = "completed",
            stable = true,
            message = message,
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteReturnToMainMenuAsync()

    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (currentScreen is not NGameOverScreen || !GameStateService.CanReturnToMainMenu(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "return_to_main_menu",
                screen
            });
        }

        NReturnToMainMenuButton mainMenuButton = GameStateService.GetGameOverMainMenuButton(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Game-over main-menu button is unavailable.", new
            {
                action = "return_to_main_menu",
                screen
            }, retryable: true);

        mainMenuButton.ForceClick();
        var stable = await WaitForGameOverExitAsync(TimeSpan.FromSeconds(30));

        return new ActionResponsePayload
        {
            action = "return_to_main_menu",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteDismissGameOverWaitAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        if (currentScreen is not NGameOverScreen || !GameStateService.IsWaitingForOtherPlayers(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "dismiss_game_over_wait",
                screen
            });
        }

        GameStateService.HideWaitingForOtherPlayers(currentScreen);
        var stable = await WaitForGameOverContinueOrSummaryAsync(TimeSpan.FromSeconds(8));
        return new ActionResponsePayload
        {
            action = "dismiss_game_over_wait",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteContinueGameOverAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (currentScreen is NGameOverScreen && GameStateService.IsWaitingForOtherPlayers(currentScreen))
        {
            GameStateService.HideWaitingForOtherPlayers(currentScreen);
            await WaitForGameOverContinueOrSummaryAsync(TimeSpan.FromSeconds(8));
            currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            screen = GameStateService.ResolveScreen(currentScreen);
        }

        if (currentScreen is not NGameOverScreen gameOverScreen)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "continue_game_over",
                screen
            });
        }

        if (GameStateService.CanReturnToMainMenu(gameOverScreen))
        {
            return new ActionResponsePayload
            {
                action = "continue_game_over",
                status = "completed",
                stable = true,
                message = "Action completed.",
                state = GameStateService.BuildStatePayload()
            };
        }

        // Intro animation disables Continue for a second. Clicking then is a
        // no-op, and later retries refuse to click because they think summary
        // already started.
        if (!GameStateService.CanContinueGameOver(gameOverScreen)
            && !GameStateService.IsGameOverSummaryStarted(gameOverScreen))
        {
            await WaitForGameOverContinueOrSummaryAsync(TimeSpan.FromSeconds(8));
            currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is not NGameOverScreen gameOverAfterIntro)
            {
                throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
                {
                    action = "continue_game_over",
                    screen = GameStateService.ResolveScreen(currentScreen)
                });
            }

            gameOverScreen = gameOverAfterIntro;
            if (GameStateService.CanReturnToMainMenu(gameOverScreen))
            {
                return new ActionResponsePayload
                {
                    action = "continue_game_over",
                    status = "completed",
                    stable = true,
                    message = "Action completed.",
                    state = GameStateService.BuildStatePayload()
                };
            }
        }

        // Clicking Continue after the native summary has started re-runs
        // OpenSummaryScreen and restarts the animation, so the main-menu
        // button never appears. Only click once, then wait for native Return.
        if (!GameStateService.IsGameOverSummaryStarted(gameOverScreen))
        {
            NGameOverContinueButton continueButton = GameStateService.GetGameOverContinueButton(currentScreen)
                ?? throw new ApiException(503, "state_unavailable", "Game-over continue button is unavailable.", new
                {
                    action = "continue_game_over",
                    screen
                }, retryable: true);

            continueButton.Visible = true;
            continueButton.Set("disabled", false);
            TryInvokePressed(continueButton);
            continueButton.ForceClick();
        }

        // Native summary writes badges, score, unlocks, then enables Return.
        // Do not force-enable that button: it lets the player leave before
        // SaveProgressFile runs, so lifetime stats stay stale.
        var stable = await WaitForGameOverSummaryReadyAsync(gameOverScreen, TimeSpan.FromSeconds(60));

        return new ActionResponsePayload
        {
            action = "continue_game_over",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForShopInventoryOpenAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is NMerchantInventory inventory && inventory.IsOpen)
            {
                return true;
            }
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is NMerchantInventory openInventory && openInventory.IsOpen;
    }

    private static async Task<bool> WaitForShopInventoryCloseAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is not NMerchantInventory)
            {
                return true;
            }
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is not NMerchantInventory;
    }

    private static async Task<bool> WaitForMerchantCardPurchaseAsync(
        Player player,
        MerchantCardEntry entry,
        int previousGold,
        string? previousCardId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentGold = player.Gold;
            var currentCardId = entry.CreationResult?.Card.Id.Entry;
            if (currentGold != previousGold || currentCardId != previousCardId || !entry.IsStocked)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForMerchantRelicPurchaseAsync(
        Player player,
        MerchantRelicEntry entry,
        int previousGold,
        string? previousRelicId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentGold = player.Gold;
            var currentRelicId = entry.Model?.Id.Entry;
            if (currentGold != previousGold || currentRelicId != previousRelicId || !entry.IsStocked)
            {
                return true;
            }
        }

        return player.Gold != previousGold || entry.Model?.Id.Entry != previousRelicId || !entry.IsStocked;
    }

    private static async Task<bool> WaitForMerchantPotionPurchaseAsync(
        Player player,
        MerchantPotionEntry entry,
        int previousGold,
        string? previousPotionId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentGold = player.Gold;
            var currentPotionId = entry.Model?.Id.Entry;
            if (currentGold != previousGold || currentPotionId != previousPotionId || !entry.IsStocked)
            {
                return true;
            }
        }

        return player.Gold != previousGold || entry.Model?.Id.Entry != previousPotionId || !entry.IsStocked;
    }

    private static async Task<bool> WaitForShopCardRemovalTransitionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is NCardGridSelectionScreen || currentScreen is not NMerchantInventory)
            {
                return true;
            }
        }

        var finalScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        return finalScreen is NCardGridSelectionScreen || finalScreen is not NMerchantInventory;
    }

    private static Creature? ResolvePotionTarget(ActionRequest request, CombatState? combatState, PotionModel potion)
    {
        return potion.TargetType switch
        {
            TargetType.AnyEnemy => ResolvePotionEnemyTarget(request, combatState, potion),
            TargetType.AnyPlayer when GameStateService.PotionRequiresTarget(combatState, potion) => ResolvePotionPlayerTarget(request, combatState, potion),
            TargetType.TargetedNoCreature => null,
            // AoE / random-target potions resolve their targets inside the game.
            // Passing Owner.Creature here makes the game silently discard the use
            // (invalid target), so use_potion stays pending forever.
            TargetType.AllEnemies => null,
            TargetType.AllAllies => null,
            TargetType.RandomEnemy => null,
            _ => potion.Owner.Creature
        };
    }

    private static Creature ResolvePotionEnemyTarget(ActionRequest request, CombatState? combatState, PotionModel potion)
    {
        if (combatState == null)
        {
            throw new ApiException(503, "state_unavailable", "Combat state is unavailable.", new
            {
                action = "use_potion",
                potion_id = potion.Id.Entry
            }, retryable: true);
        }

        if (request.target_index == null)
        {
            throw new ApiException(409, "invalid_target", "This potion requires target_index.", new
            {
                action = "use_potion",
                potion_id = potion.Id.Entry,
                target_type = potion.TargetType.ToString(),
                target_index_space = "enemies"
            });
        }

        var enemy = GameStateService.ResolveEnemyTarget(combatState, request.target_index.Value);
        if (enemy == null)
        {
            throw new ApiException(409, "invalid_target", "target_index is out of range for combat.enemies[].", new
            {
                action = "use_potion",
                potion_id = potion.Id.Entry,
                target_index = request.target_index,
                target_index_space = "enemies"
            });
        }

        return enemy;
    }

    private static Creature ResolvePotionPlayerTarget(ActionRequest request, CombatState? combatState, PotionModel potion)
    {
        if (combatState == null)
        {
            throw new ApiException(503, "state_unavailable", "Combat state is unavailable.", new
            {
                action = "use_potion",
                potion_id = potion.Id.Entry
            }, retryable: true);
        }

        if (request.target_index == null)
        {
            throw new ApiException(409, "invalid_target", "This potion requires target_index.", new
            {
                action = "use_potion",
                potion_id = potion.Id.Entry,
                target_type = potion.TargetType.ToString(),
                target_index_space = "players"
            });
        }

        var playerTargetIndices = GameStateService.GetTargetablePlayerIndices(combatState, potion.Owner, allowSelf: true);
        if (!playerTargetIndices.Contains(request.target_index.Value))
        {
            throw new ApiException(409, "invalid_target", "target_index is out of range for combat.players[].", new
            {
                action = "use_potion",
                potion_id = potion.Id.Entry,
                target_index = request.target_index,
                target_index_space = "players"
            });
        }

        return GameStateService.ResolvePlayerTarget(combatState, request.target_index.Value)
            ?? throw new ApiException(409, "invalid_target", "target_index is out of range for combat.players[].", new
            {
                action = "use_potion",
                potion_id = potion.Id.Entry,
                target_index = request.target_index,
                target_index_space = "players"
            });
    }

    private static NEpochSlot ResolveTimelineSlot(IScreenContext? currentScreen, int optionIndex)
    {
        // The state exposes timeline.slots[].index / agent_view i against the full slot list, so the
        // executor must read the same space instead of its own filtered subset.
        var slots = GameStateService.GetTimelineSlots(currentScreen);

        if (optionIndex < 0 || optionIndex >= slots.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range for timeline.slots[].", new
            {
                action = "choose_timeline_epoch",
                option_index = optionIndex,
                option_index_space = "timeline.slots[].index",
                slot_count = slots.Count
            });
        }

        var slot = slots[optionIndex];
        if (slot.State is not (EpochSlotState.Obtained or EpochSlotState.Complete))
        {
            throw new ApiException(409, "invalid_target", "The requested timeline slot is not actionable in the current state.", new
            {
                action = "choose_timeline_epoch",
                option_index = optionIndex,
                option_index_space = "timeline.slots[].index",
                slot_state = slot.State.ToString().ToLowerInvariant(),
                is_actionable = false
            });
        }

        return slot;
    }

    private static async Task<bool> WaitForCharacterSelectOpenAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            if (IsCharacterSelectOpen())
            {
                return true;
            }
        }

        return IsCharacterSelectOpen();
    }

    private static bool IsCharacterSelectOpen()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        // An open modal is its own resolved screen (MODAL) with its own actions, so the
        // transition is not settled even when the character-select screen sits underneath it.
        return MenuTransitionPolicy.IsCharacterSelectSettled(
            characterSelectScreenVisible: currentScreen is NCharacterSelectScreen,
            modalOpen: GameStateService.GetOpenModal() != null);
    }

    private static void ClickSingleplayerStandardButton(NSingleplayerSubmenu submenu)
    {
        var standardButton = GameStateService.GetSingleplayerStandardButton(submenu);
        if (standardButton != null)
        {
            standardButton.ForceClick();
            return;
        }

        submenu.Call(NSingleplayerSubmenu.MethodName.OpenCharacterSelect);
    }

    private static async Task<bool> WaitForTimelineEpochTransitionAsync(
        NEpochSlot slot,
        EpochSlotState previousState,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is not NTimelineScreen)
            {
                return true;
            }

            if (GameStateService.CanConfirmTimelineOverlay(currentScreen))
            {
                return true;
            }

            if (GameStateService.GetTimelineInspectScreen(currentScreen) != null ||
                GameStateService.GetTimelineUnlockScreen(currentScreen) != null)
            {
                continue;
            }

            if (!GodotObject.IsInstanceValid(slot) || slot.State != previousState)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForMainMenuSubmenuOpenAsync<TSubmenu>(NMainMenu screen, TimeSpan timeout, CancellationToken cancellationToken = default)
        where TSubmenu : NSubmenu
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is TSubmenu)
            {
                return true;
            }

            // Losing the pushed submenu node (for example because the whole main menu was replaced
            // by another screen) only removes our observation point. It is not proof that the
            // requested submenu opened, so stop waiting and judge the active screen below.
            if (!GodotObject.IsInstanceValid(screen))
            {
                break;
            }
        }

        return MenuTransitionPolicy.IsSubmenuObserved(
            ActiveScreenContext.Instance.GetCurrentScreen()?.GetType(),
            typeof(TSubmenu));
    }

    private static async Task<bool> WaitForMainMenuExitAsync(NMainMenu screen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsMenuExitSettled(screen))
            {
                return true;
            }
        }

        return IsMenuExitSettled(screen);
    }

    private static bool IsMenuExitSettled(NMainMenu screen)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        return MenuTransitionPolicy.IsMenuExited(
            menuScreenStillCurrent: ReferenceEquals(currentScreen, screen),
            modalOpen: GameStateService.GetOpenModal() != null,
            resolvedScreenUnknown: GameStateService.ResolveScreen(currentScreen) == "UNKNOWN");
    }

    /// <summary>Type name of the open modal, when one is blocking a transition.</summary>
    private static string? CurrentModalName()
    {
        return GameStateService.GetOpenModal()?.GetType().Name;
    }

    private static async Task<bool> WaitForMainMenuModalAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (GameStateService.GetOpenModal() != null)
            {
                return true;
            }
        }

        return GameStateService.GetOpenModal() != null;
    }

    private static async Task<bool> WaitForTimelineTutorialClosedAsync(
        NTimelineTutorial tutorial,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (GameStateService.GetTimelineTutorial(currentScreen) == null)
            {
                return true;
            }

            if (!GodotObject.IsInstanceValid(tutorial) || !tutorial.IsVisibleInTree())
            {
                return true;
            }

            if (GameStateService.CanChooseTimelineEpoch(currentScreen) ||
                GameStateService.GetTimelineUnlockScreen(currentScreen) != null)
            {
                return true;
            }
        }

        var finalScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        return GameStateService.GetTimelineTutorial(finalScreen) == null;
    }

    private static async Task<bool> WaitForTimelineInspectCloseAsync(
        NEpochInspectScreen? inspectScreen,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is not NTimelineScreen)
            {
                return true;
            }

            var currentInspect = GameStateService.GetTimelineInspectScreen(currentScreen);
            if (currentInspect == null || (inspectScreen != null && !ReferenceEquals(currentInspect, inspectScreen)))
            {
                return true;
            }

            if (GameStateService.GetTimelineUnlockScreen(currentScreen) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForTimelineUnlockTransitionAsync(Type unlockScreenType, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is not NTimelineScreen)
            {
                return true;
            }

            var unlockScreen = GameStateService.GetTimelineUnlockScreen(currentScreen);
            if (unlockScreen == null || unlockScreen.GetType() != unlockScreenType)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForMainMenuSubmenuCloseAsync(
        NSubmenuStack submenuStack,
        NSubmenu submenu,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (!ReferenceEquals(currentScreen, submenu) || !submenuStack.SubmenusOpen)
            {
                return true;
            }
        }

        var finalScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        return !ReferenceEquals(finalScreen, submenu) || !submenuStack.SubmenusOpen;
    }

    /// <summary>
    /// A capstone container page is closed when the container's own stack no longer holds it. The screen
    /// context never changes here: the container stays up with the page below it (the pause menu) and only
    /// closes itself once the stack runs empty, so waiting on the screen would report settled too early.
    /// </summary>
    private static bool IsCapstonePageClosed(NSubmenuStack stack, NSubmenu page)
    {
        return !GodotObject.IsInstanceValid(page) || !ReferenceEquals(stack.Peek(), page);
    }

    private static async Task<bool> WaitForCapstonePageCloseAsync(
        NSubmenuStack stack,
        NSubmenu page,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsCapstonePageClosed(stack, page))
            {
                return true;
            }
        }

        return IsCapstonePageClosed(stack, page);
    }

    /// <summary>
    /// Patch notes are a main-menu submenu that is not an <see cref="NSubmenu"/>: closing it tween-fades
    /// the screen and then hides it, so "closed" means the screen is hidden or is no longer current.
    /// </summary>
    private static async Task<bool> WaitForPatchNotesCloseAsync(NPatchNotesScreen patchNotes, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsPatchNotesClosed(patchNotes))
            {
                return true;
            }
        }

        return IsPatchNotesClosed(patchNotes);
    }

    private static bool IsPatchNotesClosed(NPatchNotesScreen patchNotes)
    {
        return !GodotObject.IsInstanceValid(patchNotes) ||
            !patchNotes.IsVisibleInTree() ||
            !ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(), patchNotes);
    }

    private static async Task<bool> WaitForCharacterSelectionTransitionAsync(
        NCharacterSelectScreen screen,
        string currentCharacterId,
        string previousCharacterId,
        TimeSpan timeout)
    {
        if (currentCharacterId == previousCharacterId)
        {
            return true;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            // The character-select node disappearing (for example because the run started) removes
            // our ability to read the local character. It is not evidence that the requested
            // character was selected, so stop waiting and judge below.
            if (!GodotObject.IsInstanceValid(screen))
            {
                break;
            }

            if (screen.Lobby.LocalPlayer.character.Id.Entry == currentCharacterId)
            {
                return true;
            }
        }

        return GodotObject.IsInstanceValid(screen) &&
            screen.Lobby.LocalPlayer.character.Id.Entry == currentCharacterId;
    }

    /// <summary>
    /// The load screen disables its Embark button the moment the local player is marked ready and
    /// swaps it for Unready; leaving the screen (run started, modal, menu torn down) also counts.
    /// </summary>
    private static async Task<bool> WaitForLoadEmbarkTransitionAsync(NMultiplayerLoadGameScreen screen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsLoadEmbarkSettled(screen))
            {
                return true;
            }
        }

        return IsLoadEmbarkSettled(screen);
    }

    private static bool IsLoadEmbarkSettled(NMultiplayerLoadGameScreen screen)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (!ReferenceEquals(currentScreen, screen) || !GodotObject.IsInstanceValid(screen))
        {
            return true;
        }

        return GameStateService.GetOpenModal() != null || !GameStateService.CanEmbark(currentScreen);
    }

    private static async Task<bool> WaitForEmbarkTransitionAsync(NCharacterSelectScreen screen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsEmbarkSettled(screen))
            {
                return true;
            }
        }

        return IsEmbarkSettled(screen);
    }

    private static bool IsEmbarkSettled(NCharacterSelectScreen screen)
    {
        var modal = GameStateService.GetOpenModal();
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var multiplayerReady = false;
        if (modal == null && GodotObject.IsInstanceValid(screen))
        {
            multiplayerReady = screen.Lobby.NetService.Type.IsMultiplayer() && screen.Lobby.LocalPlayer.isReady;
        }

        return MenuTransitionPolicy.IsEmbarkSettled(
            multiplayerReady: multiplayerReady,
            menuScreenStillCurrent: ReferenceEquals(currentScreen, screen),
            modalOpen: modal != null,
            resolvedScreenUnknown: GameStateService.ResolveScreen(currentScreen) == "UNKNOWN");
    }

    private static async Task<bool> WaitForLobbyReadyTransitionAsync(NCharacterSelectScreen screen, bool ready, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            // A destroyed lobby node only removes our ability to read isReady. Reporting the last
            // requested value would be a fabricated success, so stop waiting and judge below.
            if (!GodotObject.IsInstanceValid(screen))
            {
                break;
            }

            if (screen.Lobby.LocalPlayer.isReady == ready)
            {
                return true;
            }
        }

        var sourceNodeValid = GodotObject.IsInstanceValid(screen);
        // isReady can only be read while the node is alive: touching a destroyed Godot object throws.
        var observedReady = sourceNodeValid && screen.Lobby.LocalPlayer.isReady;
        return MenuTransitionPolicy.IsFlagObserved(sourceNodeValid, observedReady, ready);
    }

    private static async Task<bool> WaitForLobbyAscensionTransitionAsync(NCharacterSelectScreen screen, int targetAscension, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (!GodotObject.IsInstanceValid(screen))
            {
                return false;
            }

            if (screen.Lobby.Ascension == targetAscension)
            {
                return true;
            }
        }

        return GodotObject.IsInstanceValid(screen) && screen.Lobby.Ascension == targetAscension;
    }

    private static async Task<bool> WaitForMultiplayerLobbyCharacterSelectionTransitionAsync(
        NMultiplayerTest scene,
        string currentCharacterId,
        string previousCharacterId,
        TimeSpan timeout)
    {
        if (currentCharacterId == previousCharacterId)
        {
            return true;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScene = GameStateService.GetMultiplayerTestScene();
            if (!ReferenceEquals(currentScene, scene))
            {
                return false;
            }

            var lobby = GameStateService.GetMultiplayerTestLobby(scene);
            if (lobby?.LocalPlayer.character?.Id.Entry == currentCharacterId)
            {
                return true;
            }
        }

        return GameStateService.GetMultiplayerTestLobby(scene)?.LocalPlayer.character?.Id.Entry == currentCharacterId;
    }

    private static async Task<bool> WaitForMultiplayerLobbyHostTransitionAsync(NMultiplayerTest scene, TimeSpan timeout)
    {
        return await WaitForMultiplayerLobbyTransitionAsync(scene, timeout, lobby =>
            lobby != null &&
            lobby.NetService.Type == NetGameType.Host &&
            lobby.Players.Count >= 1);
    }

    private static async Task<bool> WaitForMultiplayerLobbyJoinTransitionAsync(NMultiplayerTest scene, TimeSpan timeout)
    {
        return await WaitForMultiplayerLobbyTransitionAsync(scene, timeout, lobby =>
            lobby != null &&
            lobby.NetService.Type == NetGameType.Client &&
            lobby.Players.Count >= 2);
    }

    private static async Task<bool> WaitForMultiplayerLobbyTransitionAsync(
        NMultiplayerTest scene,
        TimeSpan timeout,
        Func<StartRunLobby?, bool> predicate)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScene = GameStateService.GetMultiplayerTestScene();
            if (!ReferenceEquals(currentScene, scene))
            {
                return false;
            }

            var lobby = GameStateService.GetMultiplayerTestLobby(scene);
            if (predicate(lobby))
            {
                return true;
            }
        }

        return predicate(GameStateService.GetMultiplayerTestLobby(scene));
    }

    private static async Task<bool> WaitForMultiplayerLobbyReadyTransitionAsync(NMultiplayerTest scene, bool ready, bool expectRunStart, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScene = GameStateService.GetMultiplayerTestScene();
            if (!ReferenceEquals(currentScene, scene))
            {
                return ready && expectRunStart;
            }

            var lobby = GameStateService.GetMultiplayerTestLobby(scene);
            if (ready && expectRunStart && lobby != null && lobby.LocalPlayer.isReady)
            {
                continue;
            }

            if (lobby != null && lobby.LocalPlayer.isReady == ready)
            {
                return true;
            }
        }

        var finalScene = GameStateService.GetMultiplayerTestScene();
        if (!ReferenceEquals(finalScene, scene))
        {
            return ready && expectRunStart;
        }

        return GameStateService.GetMultiplayerTestLobby(scene)?.LocalPlayer.isReady == ready;
    }

    private static async Task<bool> WaitForMultiplayerLobbyDisconnectTransitionAsync(NMultiplayerTest scene, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScene = GameStateService.GetMultiplayerTestScene();
            if (currentScene == null)
            {
                return true;
            }

            if (ReferenceEquals(currentScene, scene) && GameStateService.GetMultiplayerTestLobby(scene) == null)
            {
                return true;
            }
        }

        var finalScene = GameStateService.GetMultiplayerTestScene();
        return finalScene == null || (ReferenceEquals(finalScene, scene) && GameStateService.GetMultiplayerTestLobby(scene) == null);
    }

    private static async Task<bool> WaitForPotionUseTransitionAsync(Player player, int potionIndex, PotionModel potion, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (IsPotionUseAwaitingPlayerInput())
            {
                return false;
            }

            if (HasPotionUseSettled(player, potionIndex, potion))
            {
                return true;
            }
        }

        return HasPotionUseSettled(player, potionIndex, potion);
    }

    private static async Task<bool> WaitForPotionDiscardTransitionAsync(Player player, int potionIndex, PotionModel potion, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (potion.HasBeenRemovedFromState)
            {
                return true;
            }

            if (potionIndex >= player.PotionSlots.Count)
            {
                return true;
            }

            if (!ReferenceEquals(player.PotionSlots[potionIndex], potion))
            {
                return true;
            }
        }

        return potion.HasBeenRemovedFromState || !ReferenceEquals(player.PotionSlots[potionIndex], potion);
    }

    private static bool HasPotionUseSettled(Player player, int potionIndex, PotionModel potion)
    {
        if (!HasPotionSlotTransitioned(player, potionIndex, potion))
        {
            return false;
        }

        return ArePlayerDrivenActionsSettled();
    }

    private static bool HasPotionSlotTransitioned(Player player, int potionIndex, PotionModel potion)
    {
        if (potion.HasBeenRemovedFromState)
        {
            return true;
        }

        if (potionIndex >= player.PotionSlots.Count)
        {
            return true;
        }

        return !ReferenceEquals(player.PotionSlots[potionIndex], potion);
    }

    private static bool IsPotionUseAwaitingPlayerInput()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (currentScreen is NCardGridSelectionScreen or NChooseACardSelectionScreen)
        {
            return true;
        }

        return GameStateService.TryGetCombatHandSelection(currentScreen, out _);
    }

    private static async Task<ActionResponsePayload> ExecuteModalButtonAsync(
        string actionName,
        Func<IScreenContext?, NButton?> buttonResolver)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var previousModal = GameStateService.GetOpenModal();
        var button = buttonResolver(currentScreen);

        if (previousModal == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = actionName,
                screen
            });
        }

        if (button != null)
        {
            button.ForceClick();
        }
        else if (actionName == "confirm_modal" &&
                 FtueModalPolicy.CloseFtueDirectly(previousModal.GetType().Name, hasUsableConfirmButton: false) &&
                 GameStateService.TryCloseOpenFtue())
        {
        }
        else
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = actionName,
                screen
            });
        }

        var stable = await WaitForModalTransitionAsync(previousModal, TimeSpan.FromSeconds(2));
        if (!stable &&
            actionName == "confirm_modal" &&
            FtueModalPolicy.ForceCloseIfStuck(previousModal.GetType().Name))
        {
            GameStateService.TryCloseOpenFtue();
            stable = await WaitForModalTransitionAsync(previousModal, TimeSpan.FromSeconds(8));
        }

        return new ActionResponsePayload
        {
            action = actionName,
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForModalTransitionAsync(IScreenContext previousModal, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentModal = GameStateService.GetOpenModal();
            if (currentModal == null || !ReferenceEquals(currentModal, previousModal))
            {
                return true;
            }
        }

        var finalModal = GameStateService.GetOpenModal();
        return finalModal == null || !ReferenceEquals(finalModal, previousModal);
    }

    private static async Task<bool> WaitForGameOverExitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (ActiveScreenContext.Instance.GetCurrentScreen() is not NGameOverScreen)
            {
                return true;
            }
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is not NGameOverScreen;
    }

    private static async Task<bool> WaitForGameOverContinueOrSummaryAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();
            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (GameStateService.CanContinueGameOver(currentScreen)
                || GameStateService.CanReturnToMainMenu(currentScreen)
                || currentScreen is not NGameOverScreen)
            {
                return true;
            }
        }

        var last = ActiveScreenContext.Instance.GetCurrentScreen();
        return GameStateService.CanContinueGameOver(last)
            || GameStateService.CanReturnToMainMenu(last)
            || last is not NGameOverScreen;
    }

    private static async Task<bool> WaitForGameOverSummaryReadyAsync(
        NGameOverScreen gameOverScreen,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (!GodotObject.IsInstanceValid(gameOverScreen)
                || !ReferenceEquals(currentScreen, gameOverScreen))
            {
                return true;
            }

            if (GameStateService.CanReturnToMainMenu(currentScreen))
            {
                return true;
            }
        }

        return !GodotObject.IsInstanceValid(gameOverScreen)
            || !ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(), gameOverScreen)
            || GameStateService.CanReturnToMainMenu(gameOverScreen);
    }

    private static async Task<NPauseMenu?> WaitForPauseMenuAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var pauseMenu = FindPauseMenu();
            if (pauseMenu != null && pauseMenu.IsVisibleInTree())
            {
                return pauseMenu;
            }
        }

        return FindPauseMenu();
    }

    private static async Task<bool> WaitForMainMenuAfterSaveAndQuitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is NMainMenu)
            {
                return true;
            }
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is NMainMenu;
    }

    private static NPauseMenu? FindPauseMenu()
    {
        return FindFirstInGame<NPauseMenu>();
    }

    private static T? FindFirstInGame<T>() where T : Node
    {
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game))
        {
            return null;
        }

        Node? root = game.GetTree()?.Root;
        root ??= game;
        return FindFirstDescendant<T>(root);
    }

    private static T? FindFirstDescendant<T>(Node? node) where T : Node
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return null;
        }

        if (node is T typedNode)
        {
            return typedNode;
        }

        foreach (var child in node.GetChildren())
        {
            var found = FindFirstDescendant<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static string BuildEventOptionSignature(EventModel eventModel)
    {
        return string.Join(
            "|",
            eventModel.CurrentOptions.Select(option =>
                $"{option.TextKey}:{option.IsLocked}:{option.IsProceed}:{EventOptionLocalization.Format(
                    option.Title,
                    locString => eventModel.DynamicVars.AddTo(locString),
                    locString => locString.GetFormattedText())}:{EventOptionLocalization.Format(
                    option.Description,
                    locString => eventModel.DynamicVars.AddTo(locString),
                    locString => locString.GetFormattedText())}"));
    }

    private static void ObserveBackgroundResult(Task<bool> task, string actionName)
    {
        _ = ObserveBackgroundResultCore(task, actionName);
    }

    /// <summary>
    /// Keeps a game task that outlived its deadline observed so a later fault is logged
    /// instead of surfacing as an unobserved task exception.
    /// </summary>
    private static void ObserveBackgroundTask(Task task, string actionName)
    {
        _ = ObserveBackgroundTaskCore(task, actionName);
    }

    private static async Task ObserveBackgroundTaskCore(Task task, string actionName)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Log.Error($"[STS2AIAgent] Background task {actionName} failed: {ex}");
        }
    }

    private static Task<T>? InvokePrivateTask<T>(object target, string methodName, params object?[] args)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var method = target.GetType().GetMethod(methodName, flags);
        return method?.Invoke(target, args) as Task<T>;
    }

    private static Task? InvokePrivateTask(object target, string methodName, params object?[] args)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var method = target.GetType().GetMethod(methodName, flags);
        return method?.Invoke(target, args) as Task;
    }

    private static T? GetPrivateField<T>(object target, string fieldName) where T : class
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = target.GetType().GetField(fieldName, flags);
        return field?.GetValue(target) as T;
    }

    private static void TryInvokePressed(NButton button)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in new[] { "OnContinueButtonPressed", "OnPressed" })
        {
            try
            {
                var method = button.GetType().GetMethod(name, flags, binder: null, types: Type.EmptyTypes, modifiers: null);
                method?.Invoke(button, null);
            }
            catch
            {
            }
        }

        try
        {
            var asyncPressed = button.GetType().GetMethod("OnContinueButtonPressedAsync", flags);
            if (asyncPressed?.Invoke(button, null) is Task task)
            {
                ObserveBackgroundResult(task.ContinueWith(static completed =>
                {
                    if (completed.IsFaulted)
                    {
                        return false;
                    }

                    return true;
                }), "continue_game_over");
            }
        }
        catch
        {
        }

        try
        {
            button.EmitSignal(Button.SignalName.Pressed);
        }
        catch
        {
        }
    }

    private static void InvokePrivateVoid(object target, string methodName, params object?[] args)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var method = target.GetType().GetMethod(methodName, flags)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found on {target.GetType().FullName}.");
        method.Invoke(target, args);
    }

    private static async Task ObserveBackgroundResultCore(Task<bool> task, string actionName)
    {
        try
        {
            var success = await task;
            if (!success)
            {
                Log.Warn($"[STS2AIAgent] Background action {actionName} returned false.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[STS2AIAgent] Background action {actionName} failed: {ex}");
        }
    }

    private static async Task<bool> WaitForRelicPickTransitionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is NTreasureRoomRelicCollection)
            {
                continue;
            }

            if (currentScreen is NTreasureRoom)
            {
                if (GameStateService.GetProceedButton(currentScreen) != null)
                {
                    await WaitForNextFrameAsync();

                    var confirmedScreen = ActiveScreenContext.Instance.GetCurrentScreen();
                    return confirmedScreen is NTreasureRoom && GameStateService.GetProceedButton(confirmedScreen) != null;
                }

                continue;
            }

            if (IsStableScreenState(currentScreen, allowMapScreen: true))
            {
                return true;
            }
        }

        var screen = ActiveScreenContext.Instance.GetCurrentScreen();
        return screen is NTreasureRoom && GameStateService.GetProceedButton(screen) != null;
    }

    /// <summary>
    /// Waits for the next game frame via Godot's ProcessFrame signal.
    /// When NGame or SceneTree is unavailable (e.g. during shutdown),
    /// falls back to Task.Delay without ConfigureAwait(false) to preserve
    /// the game thread SynchronizationContext. Using ConfigureAwait(false)
    /// would resume on a thread-pool thread and break Godot object access.
    /// </summary>
    private static Task WaitForNextFrameAsync()
    {
        return GameThread.WaitForNextFrameAsync();
    }
}

internal sealed class ActionRequest
{
    public string? action { get; init; }

    public int? card_index { get; init; }

    public int? target_index { get; init; }

    public int? option_index { get; init; }

    public int? x { get; init; }

    public int? y { get; init; }

    public string? tool { get; init; }

    public string? command { get; init; }

    public string? player_id { get; init; }

    public object? client_context { get; init; }
}

internal sealed class ActionResponsePayload
{
    public string action { get; init; } = string.Empty;

    public string status { get; init; } = "failed";

    public bool stable { get; init; }

    public string message { get; init; } = string.Empty;

    public GameStatePayload state { get; init; } = new();
}
