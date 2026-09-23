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

internal static partial class GameActionService
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

    /// <summary>
    /// Executes one action, whichever transport asked for it.
    /// </summary>
    /// <remarks>
    /// One mutating action runs at a time. The lease that enforces it is taken here rather than at any
    /// single transport because this is the only place every transport converges -- <c>POST /action</c>,
    /// the in-game agent through <see cref="GameBridge"/>, the native MCP <c>act</c> tool, the teammate
    /// coordinator, the overlay. It is held until the dispatched task completes, every transition wait
    /// included, and released in <c>finally</c> so an action that throws or is canceled cannot leave the
    /// mod refusing every later request. <see cref="ActionExecutionGate"/> refuses a concurrent caller
    /// immediately instead of queueing it behind a snapshot the first action has already invalidated.
    /// </remarks>
    public static async Task<ActionResponsePayload> ExecuteAsync(ActionRequest request)
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

        var lease = ActionExecutionGate.Shared.Acquire(actionName);
        try
        {
            return await DispatchAsync(actionName, request);
        }
        finally
        {
            lease.Release();
        }
    }

    /// <summary>
    /// The action dispatch table: one arm per accepted action name. Only this table stays in the base
    /// file; each handler lives in the partial for its room.
    /// </summary>
    private static Task<ActionResponsePayload> DispatchAsync(string? actionName, ActionRequest request)
    {
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
            "inject_event_churn" => ExecuteInjectEventChurnAsync(request),
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

    private static int _endTurnKickRound = int.MinValue;

    internal static string EndTurnKickDetail { get; } = string.Empty;

    internal static void EnsureEndTurnPhaseStarts()
    {
        // Intentionally empty: GET /state and end_turn must not reflectively mutate CombatManager.
        _ = _endTurnKickRound;
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
                rewardsScreen.Call(NRewardsScreen.MethodName.TryEnableProceedButton);
            }
            catch (Exception ex)
            {
                Log.Warn($"[STS2AIAgent] DrainRewardFlowAsync: TryEnableProceedButton failed: {ex}");
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
            catch (Exception ex)
            {
                Log.Warn($"[STS2AIAgent] DrainRewardFlowAsync: NOverlayStack.Remove failed: {ex}");
            }

            if (await WaitForRewardFlowExitAsync(rewardsScreen, deadline))
            {
                return true;
            }

            try
            {
                _ = RunManager.Instance.ProceedFromTerminalRewardsScreen();
            }
            catch (Exception ex)
            {
                Log.Warn($"[STS2AIAgent] DrainRewardFlowAsync: ProceedFromTerminalRewardsScreen failed: {ex}");
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

        // A card reward is a deck-building decision, not a cleanup step: when the caller gave no
        // explicit choice, the drain stops here with the selection screen still open instead of
        // silently taking the first card. The state it returns carries reward.pending_card_choice
        // = true, and the next decision picks choose_reward_card / skip_reward_cards on its own
        // merits. Hands-off callers pass an explicit option_index (or -1 to skip).
        if (resolution.Kind == RewardChoiceKind.Auto)
        {
            return false;
        }

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
            // The first *enabled* alternative, like skip_reward_cards: with a reroll relic the list is
            // [Skip, Reroll] and the unfiltered First() could press a disabled button. With nothing
            // enabled, stop instead of spinning out the whole drain budget waiting for a skip that
            // was never clicked.
            NCardRewardAlternativeButton? skip = null;
            foreach (var button in GameStateService.GetCardRewardAlternativeButtons(cardRewardScreen))
            {
                if (button.IsEnabled)
                {
                    skip = button;
                    break;
                }
            }

            if (skip == null)
            {
                return false;
            }

            skip.ForceClick();
            CardRewardSkips.MarkSkipped(GameStateService.GetRewardSetId(cardRewardScreen));
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
    /// Failure reason for a game task that already finished unsuccessfully, read here once so no
    /// call site has to touch <c>Task.Exception</c> itself. Kept separate from
    /// <c>BackgroundTaskOutcome.DescribeFailure</c>: that helper's wording is scoped to shop
    /// purchases, while these sites span save, rest, event, lobby, and console actions.
    /// </summary>
    /// <remarks>
    /// The fault's own exception is named, because "the game task faulted" is the same sentence for
    /// a command the game rejected and a command that broke the mod. A 2026-09-17 live pass hit
    /// exactly that: <c>run_console_command room Treasure</c>, issued while already standing in a
    /// treasure room, answered 409 <c>Console command failed: the game task faulted.</c> with
    /// nothing to act on. That handler's *synchronous* branch already names its exception -- the
    /// same dishonesty was fixed there once, for <c>bestiary</c>, and the asynchronous branch that
    /// reaches this helper was missed.
    ///
    /// Reading <c>Task.Exception</c> also marks the fault observed, which is what
    /// <c>ObserveBackgroundTask</c> does for the tasks that outlive their request.
    /// </remarks>
    private static string DescribeGameTaskFailure(Task task)
    {
        if (task.IsCanceled)
        {
            return "the game task was canceled";
        }

        if (!task.IsFaulted)
        {
            return "the game task failed";
        }

        return "the game task faulted" + DescribeTaskFault(task);
    }

    /// <summary>
    /// The exception behind a faulted task, as a suffix to append to a failure sentence, or an empty
    /// string when the task did not fault or carries nothing to name.
    /// </summary>
    /// <remarks>
    /// Shared so the pure-decision path keeps its shape: <c>BackgroundTaskOutcome.DescribeFailure</c>
    /// takes booleans on purpose and cannot see an exception, but its one call site holds the task
    /// and can say what faulted. Appending only on a fault keeps the wording right for the outcomes
    /// that have no exception -- a cancel, or a purchase the game simply refused.
    /// </remarks>
    private static string DescribeTaskFault(Task task)
    {
        if (!task.IsFaulted)
        {
            return string.Empty;
        }

        var failure = task.Exception?.InnerException ?? task.Exception?.GetBaseException();
        if (failure == null)
        {
            return string.Empty;
        }

        // Every call site closes its sentence with a period, and a game exception often ends in its
        // own punctuation -- live, "...while one was already occurring!" arrived and rendered as
        // "occurring!.". This is the one place foreign text enters those sentences, so it is where
        // the seam is smoothed.
        var message = failure.Message.TrimEnd().TrimEnd('.', '!', '?');
        return $": {failure.GetType().Name}: {message}";
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
            mainMenu.OpenMultiplayerSubmenu();
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
        return ReflectedGameMembers.Field(typeof(NDevConsole), "_devConsole")?.GetValue(console) as DevConsole;
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
            mainMenu.OpenMultiplayerSubmenu();
            await WaitForMainMenuSubmenuOpenAsync<NMultiplayerSubmenu>(mainMenu, TimeSpan.FromSeconds(5), cancellationToken);
            submenu = mainMenu.SubmenuStack.GetSubmenuType<NMultiplayerSubmenu>()
                ?? throw new InvalidOperationException(Loc.T("找不到多人子菜单。"));
        }
        else
        {
            mainMenu.SubmenuStack.Push(submenu);
            await WaitForMainMenuSubmenuOpenAsync<NMultiplayerSubmenu>(mainMenu, TimeSpan.FromSeconds(5), cancellationToken);
        }

        var startLoad = ReflectedGameMembers.Method(typeof(NMultiplayerSubmenu), "StartLoad")
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

    /// <summary>Type name of the open modal, when one is blocking a transition.</summary>
    private static string? CurrentModalName()
    {
        return GameStateService.GetOpenModal()?.GetType().Name;
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
