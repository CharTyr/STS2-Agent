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
/// Chests, events, rest sites, the crystal sphere, capstones and bundles: the rooms a map node can open, plus the generic proceed that closes them.
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
        if (!GameStateService.CanPlayCrystalSphere(currentScreen))
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

            // NEventRoom.Proceed dereferences NMapScreen.Instance synchronously; during an act
            // transition it can be null, which surfaced as a bare 500 instead of a retryable state.
            if (NMapScreen.Instance == null)
            {
                throw new ApiException(503, "state_unavailable", "The map screen is not ready yet; retry the event proceed shortly.", new
                {
                    action = "choose_event_option",
                    screen,
                    option_index = request.option_index
                }, retryable: true);
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

        // ForceClick, not the BaseButton pressed signal this used to emit: NButton derives from
        // NClickableControl -> Control, not from Godot's BaseButton, so it has no "pressed" signal and
        // emitting one did nothing. The constant compiled only because it names BaseButton, not the
        // button's own type.
        var button = buttons[request.option_index.Value];
        button.ForceClick();

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
            ((Node)bundleScreen).Call(NChooseABundleSelectionScreen.MethodName.OnBundleClicked, bundle);
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

        // There used to be two fallbacks here for a screen that had not moved on: calling the
        // screen's OnConfirmPressed and emitting "pressed" on the button. Neither could work in the
        // installed game -- NChooseABundleSelectionScreen declares no OnConfirmPressed, and NButton is
        // a Control, not a Godot BaseButton, so it has no "pressed" signal. The click above is the
        // only thing this handler has ever done; the wait below is what tells the caller whether it
        // landed.
        var stable = false;

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
}
