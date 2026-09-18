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
/// Everything between the main menu and a running fight: character select, the timeline and its unlocks, ascension, embark and unready.
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
                // Checked like the singleplayer submenu above: an unchecked null here reached the caller
                // as a 500 rather than as the transient state it is.
                var characterSelectScreen = mainMenu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>()
                    ?? throw new ApiException(503, "state_unavailable", "Character select screen is unavailable.", new
                    {
                        action = "open_character_select",
                        screen
                    }, retryable: true);
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
}
