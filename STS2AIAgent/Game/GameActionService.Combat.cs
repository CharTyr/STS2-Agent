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

/// <summary>
/// Playing cards, spending potions and ending the turn -- the only actions whose availability the combat stability gate governs.
/// </summary>
/// <remarks>
/// Part of <see cref="GameActionService"/>, split out of <c>GameActionService.cs</c> on
/// 2026-09-17. Every handler here follows the one pattern the whole service follows: validate
/// the screen, act on the game thread, wait for the transition with a deadline. The file
/// boundary is not a design -- which members live here was computed, by asking which ones
/// nothing outside this room references. Anything two rooms both reach stayed in the base file.
/// </remarks>
internal static partial class GameActionService
{
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
        catch (Exception ex)
        {
            Log.Warn($"[STS2AIAgent] TryCancelRunningPlayerAction: running.Cancel() failed: {ex}");
        }

        try
        {
            // ActionExecutor has Cancel(), not CancelAction(). Cancel() trips the
            // per-frame wait so the executor can drop a stuck Execute() task.
            executor.Cancel();
            canceled = true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[STS2AIAgent] TryCancelRunningPlayerAction: executor.Cancel() failed: {ex}");
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
            catch (Exception ex)
            {
                Log.Warn($"[STS2AIAgent] TryCancelRunningPlayerAction: clearing CurrentlyRunningAction failed: {ex}");
            }
        }

        return canceled;
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
}
