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
/// Profile switching, main-menu submenus, modal confirm/dismiss and the debug console.
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
            var backButton = ReflectedGameMembers.Field(typeof(NPatchNotesScreen), "_backButton")?.GetValue(patchNotes) as NButton;
            if (backButton != null &&
                GodotObject.IsInstanceValid(backButton) &&
                backButton.IsVisibleInTree() &&
                backButton.IsEnabled)
            {
                backButton.ForceClick();
            }
            else
            {
                ((Node)patchNotes).Call(NPatchNotesScreen.MethodName.Close);
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

    private static Task<ActionResponsePayload> ExecuteInjectEventChurnAsync(ActionRequest request)
    {
        if (!AreDebugActionsEnabled())
        {
            throw new ApiException(409, "invalid_action", "inject_event_churn is disabled. Set STS2_ENABLE_DEBUG_ACTIONS=1 for development use.", new
            {
                action = "inject_event_churn"
            });
        }

        if (!EventChurnPolicy.TryResolveCount(request.option_index, out var count, out var error))
        {
            throw new ApiException(400, "invalid_request", error ?? "count is invalid.", new
            {
                action = "inject_event_churn"
            });
        }

        // Deliberately not a state mutation: the releases exist to push a subscriber's bounded queue
        // past its capacity, so a live run can observe the slow-subscriber contract end to end. One
        // state build serves both the events and the response instead of paying for two.
        var state = GameStateService.BuildStatePayload();
        var published = GameEventService.Instance.PublishDebugChurn(state, count);
        return Task.FromResult(new ActionResponsePayload
        {
            action = "inject_event_churn",
            status = "completed",
            stable = true,
            message = $"Published {published} synthetic {EventChurnPolicy.EventType} event(s).",
            state = state
        });
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
        catch (Exception ex)
        {
            Log.Warn($"[STS2AIAgent] reading environment variable {name} through Godot failed: {ex}");
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

        var message = "Action completed.";
        if (!stable)
        {
            message = FtueModalPolicy.IsMultiPageFtue(previousModal.GetType().Name)
                ? "Tutorial page advanced; the modal is still open. Call confirm_modal again."
                : "Action queued but state is still transitioning.";
        }

        return new ActionResponsePayload
        {
            action = actionName,
            status = stable ? "completed" : "pending",
            stable = stable,
            message = message,
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
}
