using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

/// <summary>
/// The timeline: opening it, revealing epoch slots, confirming its tutorial/inspect/unlock overlays
/// and the standalone unlock confirm.
/// </summary>
/// <remarks>
/// Part of <see cref="GameActionService"/>, split out of <c>GameActionService.Embark.cs</c> when the
/// run-lobby modes of game v0.113.0 pushed that file over its line budget. Timeline and embark were
/// already two concerns; the split makes it official.
/// </remarks>
internal static partial class GameActionService
{
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
}
