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
/// Run-level control: continue, abandon, save and quit, map movement, the game-over chain and the return to the main menu.
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
        var closeTask = ReflectedGameMembers.Method(typeof(NPauseMenu), "CloseToMenu")?.Invoke(pauseMenu, null) as Task;
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
            var saveAndQuitButton = ReflectedGameMembers.Field(typeof(NPauseMenu), "_saveAndQuitButton")?.GetValue(pauseMenu) as NButton;
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

            // ForceClick is the whole of the click. Two steps used to precede it and neither did
            // anything: Set("disabled", false) and emitting Button.SignalName.Pressed both assume a
            // Godot BaseButton, and NGameOverContinueButton is an NButton -- NClickableControl ->
            // Control -- with no "disabled" property and no "pressed" signal. Before that the helper
            // also tried three handler names that no button declares.
            continueButton.Visible = true;
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
}
