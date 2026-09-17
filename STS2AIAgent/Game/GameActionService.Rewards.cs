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
/// Claiming and skipping rewards, and the card-grid selections that several rooms route into.
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
}
