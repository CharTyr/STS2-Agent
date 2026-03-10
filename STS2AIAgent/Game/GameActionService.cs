using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

internal static class GameActionService
{
    public static Task<ActionResponsePayload> ExecuteAsync(ActionRequest request)
    {
        var actionName = request.action?.Trim().ToLowerInvariant();

        return actionName switch
        {
            "end_turn" => ExecuteEndTurnAsync(),
            "play_card" => ExecutePlayCardAsync(request),
            "choose_map_node" => ExecuteChooseMapNodeAsync(request),
            "collect_rewards_and_proceed" => ExecuteCollectRewardsAndProceedAsync(),
            "claim_reward" => ExecuteClaimRewardAsync(request),
            "choose_reward_card" => ExecuteChooseRewardCardAsync(request),
            "skip_reward_cards" => ExecuteSkipRewardCardsAsync(),
            "select_deck_card" => ExecuteSelectDeckCardAsync(request),
            "proceed" => ExecuteProceedAsync(),
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

        if (!GameStateService.CanEndTurn(currentScreen, combatState))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "end_turn",
                screen
            });
        }

        var me = LocalContext.GetMe(combatState)
            ?? throw new ApiException(503, "state_unavailable", "Local player is unavailable.", new
            {
                action = "end_turn",
                screen
            }, retryable: true);

        var playerCombatState = me.Creature.CombatState
            ?? throw new ApiException(503, "state_unavailable", "Combat state is unavailable.", new
            {
                action = "end_turn",
                screen
            }, retryable: true);

        var roundNumber = playerCombatState.RoundNumber;
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, roundNumber));

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

    private static async Task<bool> WaitForEndTurnTransitionAsync(int previousRound, TimeSpan timeout)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);

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

        return !CombatManager.Instance.IsPlayPhase;
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

        var stable = await WaitForPlayCardTransitionAsync(card, TimeSpan.FromSeconds(5));

        return new ActionResponsePayload
        {
            action = "play_card",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
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

        if (card.TargetType == TargetType.AnyEnemy)
        {
            if (request.target_index == null)
            {
                throw new ApiException(409, "invalid_target", "This card requires target_index.", new
                {
                    action = "play_card",
                    card_id = card.Id.Entry,
                    target_type = card.TargetType.ToString()
                });
            }

            var enemy = GameStateService.ResolveEnemyTarget(combatState, request.target_index.Value);
            if (enemy == null)
            {
                throw new ApiException(409, "invalid_target", "target_index is out of range.", new
                {
                    action = "play_card",
                    card_id = card.Id.Entry,
                    target_index = request.target_index
                });
            }

            return enemy;
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
            await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);

            if (IsPlayCardStable(card))
            {
                return true;
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

    private static bool ArePlayerDrivenActionsSettled()
    {
        var runningAction = RunManager.Instance.ActionExecutor.CurrentlyRunningAction;
        if (runningAction != null && ActionQueueSet.IsGameActionPlayerDriven(runningAction))
        {
            return false;
        }

        var readyAction = RunManager.Instance.ActionQueueSet.GetReadyAction();
        if (readyAction != null && ActionQueueSet.IsGameActionPlayerDriven(readyAction))
        {
            return false;
        }

        return true;
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
        var previousCoord = runState?.CurrentMapCoord;
        var roomEntered = false;

        void OnRoomEntered()
        {
            roomEntered = true;
        }

        RunManager.Instance.RoomEntered += OnRoomEntered;
        try
        {
            selectedNode.ForceClick();
            var stable = await WaitForMapTransitionAsync(previousCoord, TimeSpan.FromSeconds(10), () => roomEntered);

            return new ActionResponsePayload
            {
                action = "choose_map_node",
                status = stable ? "completed" : "pending",
                stable = stable,
                message = stable ? "Action completed." : "Action queued but state is still transitioning.",
                state = GameStateService.BuildStatePayload()
            };
        }
        finally
        {
            RunManager.Instance.RoomEntered -= OnRoomEntered;
        }
    }

    private static async Task<bool> WaitForMapTransitionAsync(MapCoord? previousCoord, TimeSpan timeout, Func<bool> roomEntered)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);

            if (roomEntered() || IsMapTransitionStable(previousCoord))
            {
                return true;
            }
        }

        return roomEntered() || IsMapTransitionStable(previousCoord);
    }

    private static bool IsMapTransitionStable(MapCoord? previousCoord)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (GameStateService.ResolveScreen(currentScreen) != "MAP")
        {
            return true;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return false;
        }

        if (runState.CurrentRoom is not MapRoom)
        {
            return true;
        }

        var currentCoord = runState.CurrentMapCoord;
        if (!previousCoord.HasValue)
        {
            return currentCoord.HasValue;
        }

        return currentCoord.HasValue && !currentCoord.Value.Equals(previousCoord.Value);
    }

    private static async Task<ActionResponsePayload> ExecuteProceedAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);
        var proceedButton = GameStateService.GetProceedButton(currentScreen);

        if (proceedButton == null)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "proceed",
                screen
            });
        }

        proceedButton.ForceClick();
        var stable = await WaitForProceedTransitionAsync(currentScreen, proceedButton, TimeSpan.FromSeconds(10));

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
        NProceedButton previousButton,
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

            if (IsProceedStable(previousScreen, previousButton))
            {
                return true;
            }
        }

        return IsProceedStable(previousScreen, previousButton);
    }

    private static bool IsProceedStable(IScreenContext? previousScreen, NProceedButton previousButton)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (!ReferenceEquals(currentScreen, previousScreen))
        {
            return true;
        }

        if (!GodotObject.IsInstanceValid(previousButton))
        {
            return true;
        }

        return !previousButton.IsVisibleInTree() || !previousButton.IsEnabled;
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

        var stable = await DrainRewardFlowAsync(TimeSpan.FromSeconds(20));

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

        var rewardButtons = GameStateService.GetRewardButtons(currentScreen)
            .Where(button => button.IsEnabled)
            .ToList();

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
        var previousRewardCount = rewardButtons.Count;
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
        var selected = alternatives.First();
        selected.ForceClick();
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

        if (currentScreen is not NDeckCardSelectScreen deckCardSelectScreen || !GameStateService.CanSelectDeckCard(currentScreen))
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

        var selected = options[request.option_index.Value];
        selected.EmitSignal(NCardHolder.SignalName.Pressed, selected);
        var stable = await ConfirmDeckSelectionAsync(deckCardSelectScreen, TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "select_deck_card",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> DrainRewardFlowAsync(TimeSpan timeout)
    {
        if (NGame.Instance == null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow + timeout;
        var attemptedRewardButtons = new HashSet<ulong>();

        while (DateTime.UtcNow < deadline)
        {
            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();

            if (currentScreen is NCardRewardSelectionScreen cardRewardScreen)
            {
                if (!await TryResolveCardRewardAsync(cardRewardScreen, deadline))
                {
                    return false;
                }

                continue;
            }

            if (currentScreen is not NRewardsScreen rewardsScreen)
            {
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

            return IsRewardFlowStable();
        }

        return IsRewardFlowStable();
    }

    private static bool TryGetNextClaimableRewardButton(
        NRewardsScreen rewardsScreen,
        HashSet<ulong> attemptedRewardButtons,
        out NRewardButton? rewardButton)
    {
        var hasPotionSlots = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())?.HasOpenPotionSlots ?? false;
        rewardButton = GameStateService
            .GetRewardButtons(rewardsScreen)
            .FirstOrDefault(button =>
                button.IsEnabled &&
                !attemptedRewardButtons.Contains(button.GetInstanceId()) &&
                (button.Reward is not PotionReward || hasPotionSlots));

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

    private static async Task<bool> TryResolveCardRewardAsync(NCardRewardSelectionScreen cardRewardScreen, DateTime deadline)
    {
        for (var i = 0; i < 24 && DateTime.UtcNow < deadline; i++)
        {
            await WaitForNextFrameAsync();
        }

        var options = GameStateService.GetCardRewardOptions(cardRewardScreen);
        var selected = options.FirstOrDefault();
        if (selected == null)
        {
            return false;
        }

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

    private static async Task<bool> ConfirmDeckSelectionAsync(NDeckCardSelectScreen screen, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (!GodotObject.IsInstanceValid(screen))
            {
                return true;
            }

            var previewContainer = screen.GetNodeOrNull<Control>("%PreviewContainer");
            var previewConfirm = screen.GetNodeOrNull<NConfirmButton>("%PreviewConfirm");
            if (previewContainer?.Visible == true && previewConfirm?.IsEnabled == true)
            {
                previewConfirm.ForceClick();
                return await WaitForDeckSelectionResolutionAsync(screen, deadline);
            }

            var confirmButton = screen.GetNodeOrNull<NConfirmButton>("%Confirm");
            if (confirmButton?.IsEnabled == true)
            {
                confirmButton.ForceClick();
            }
        }

        return false;
    }

    private static async Task<bool> WaitForDeckSelectionResolutionAsync(NDeckCardSelectScreen screen, DateTime deadline)
    {
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            if (!GodotObject.IsInstanceValid(screen) ||
                ActiveScreenContext.Instance.GetCurrentScreen() is not NDeckCardSelectScreen)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WaitForNextFrameAsync()
    {
        await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}

internal sealed class ActionRequest
{
    public string? action { get; init; }

    public int? card_index { get; init; }

    public int? target_index { get; init; }

    public int? option_index { get; init; }

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
