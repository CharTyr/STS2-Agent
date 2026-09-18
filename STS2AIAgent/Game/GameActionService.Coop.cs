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
/// Multiplayer lobbies and the AI teammate.
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

        var startHostTask = ReflectedGameMembers.Method(typeof(NMultiplayerTest), "StartHost")
            ?.Invoke(scene, new object?[] { false }) as Task<bool>
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

        InvokeLobbyMethod(
            ReflectedGameMembers.Method(typeof(NMultiplayerTest), "ReadyButtonPressed"),
            scene, "ready_multiplayer_lobby", screen);
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

        InvokeLobbyMethod(
            ReflectedGameMembers.Method(typeof(NMultiplayerTest), "Disconnect"),
            scene, "disconnect_multiplayer_lobby", screen, NetError.Quit);
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

    /// <summary>
    /// Host side: reload the saved multiplayer run over local ENet (fastmp host_standard) and launch the companion so it rejoins as its saved player.
    /// </summary>
    private static Task<ActionResponsePayload> ExecuteContinueAiTeammateAsync()
    {
        // A launch that already owns the gate is authoritative. Answer pending up front: re-running
        // the menu and save probes below would hand a concurrent caller a misleading
        // "No saved multiplayer run to continue." 409 while the first attempt is still working.
        if (AgentRuntime.Instance.DualLaunching)
        {
            return Task.FromResult(new ActionResponsePayload
            {
                action = "continue_ai_teammate",
                status = "pending",
                stable = false,
                message = Loc.T("正在读档接回队友…"),
                state = GameStateService.BuildStatePayload()
            });
        }

        var payload = GameStateService.BuildStatePayload();
        var settings = AgentRuntime.Instance.Settings;
        // Same route switch as invite_ai_teammate: an unverified play model no longer blocks the
        // launch, it only means the teammate comes up paused for an external agent.
        var companionAutoPlay = FirstRunSetup.Evaluate(settings).ReadyToInvite;
        var error = CoopLaunchPolicy.GetError(
            InstanceRole.IsCompanion,
            AgentRuntime.Instance.PlayRunning,
            payload.screen,
            settings,
            requireVerifiedPlayModel: companionAutoPlay);
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

        var launch = AgentRuntime.Instance.TryContinueDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None);
        // Null means another attempt already owns the gate; that attempt owns the outcome, so this
        // call answers pending instead of classifying on a result it did not produce.
        if (launch == null || !launch.IsCompleted)
        {
            if (launch != null)
            {
                ObserveBackgroundTask(launch, "continue_ai_teammate");
            }

            return Task.FromResult(new ActionResponsePayload
            {
                action = "continue_ai_teammate",
                status = "pending",
                stable = false,
                message = Loc.T("正在读档接回队友…"),
                state = GameStateService.BuildStatePayload()
            });
        }
        // Same classification as invite_ai_teammate: read the structured outcome, never the localized text.
        var outcome = AgentRuntime.Instance.DualLaunchOutcome;
        if (DualLaunchOutcomePolicy.IsInProgress(outcome))
        {
            return Task.FromResult(new ActionResponsePayload
            {
                action = "continue_ai_teammate",
                status = "pending",
                stable = false,
                message = Loc.T("正在读档接回队友…"),
                state = GameStateService.BuildStatePayload()
            });
        }

        var message = AgentRuntime.Instance.DualStatus;
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

        return Task.FromResult(new ActionResponsePayload
        {
            action = "continue_ai_teammate",
            status = "completed",
            stable = true,
            message = message,
            state = GameStateService.BuildStatePayload()
        });
    }

    private static Task<ActionResponsePayload> ExecuteInviteAiTeammateAsync()
    {
        var payload = GameStateService.BuildStatePayload();
        var settings = AgentRuntime.Instance.Settings;
        // A play model that passed 测试连接 is what makes auto-play possible, so it also picks the
        // route: without one the teammate still launches, it just waits for an external agent
        // instead of starting the loop. See the two routes in docs/api.md.
        var companionAutoPlay = FirstRunSetup.Evaluate(settings).ReadyToInvite;
        var error = CoopLaunchPolicy.GetError(
            InstanceRole.IsCompanion,
            AgentRuntime.Instance.PlayRunning,
            payload.screen,
            settings,
            requireVerifiedPlayModel: companionAutoPlay);
        if (error != null)
        {
            throw new ApiException(409, "invalid_action", error, new
            {
                action = "invite_ai_teammate",
                screen = payload.screen
            });
        }

        var launch = AgentRuntime.Instance.TryLaunchDualInstanceAsync(settings, companionAutoPlay, CancellationToken.None);
        // Null means another attempt already owns the gate; that attempt owns the outcome, so this
        // call answers pending instead of classifying on a result it did not produce.
        if (launch == null || !launch.IsCompleted)
        {
            if (launch != null)
            {
                ObserveBackgroundTask(launch, "invite_ai_teammate");
            }

            return Task.FromResult(new ActionResponsePayload
            {
                action = "invite_ai_teammate",
                status = "pending",
                stable = false,
                message = Loc.T("正在邀请队友…"),
                state = GameStateService.BuildStatePayload()
            });
        }
        // Classify on the structured outcome. DualStatus is localized display text, so matching
        // substrings in it misreports every failure as success in a non-Chinese client.
        var outcome = AgentRuntime.Instance.DualLaunchOutcome;
        if (DualLaunchOutcomePolicy.IsInProgress(outcome))
        {
            return Task.FromResult(new ActionResponsePayload
            {
                action = "invite_ai_teammate",
                status = "pending",
                stable = false,
                message = Loc.T("正在邀请队友…"),
                state = GameStateService.BuildStatePayload()
            });
        }

        var message = AgentRuntime.Instance.DualStatus;
        if (DualLaunchOutcomePolicy.IsFailure(outcome) || outcome == DualLaunchOutcome.Idle)
        {
            throw new ApiException(409, "invite_failed", message, new
            {
                action = "invite_ai_teammate",
                screen = GameStateService.BuildStatePayload().screen,
                outcome = outcome.ToString()
            });
        }

        return Task.FromResult(new ActionResponsePayload
        {
            action = "invite_ai_teammate",
            status = "completed",
            stable = true,
            message = message,
            state = GameStateService.BuildStatePayload()
        });
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

    /// <summary>Calls one of the lobby scene's private handlers, as resolved by the registry.</summary>
    /// <remarks>
    /// The caller names the member at the call site, so the registry contract can see which entry is
    /// asked for. A missing handler used to throw InvalidOperationException, which reached the caller
    /// as a 500 with no hint that the game had renamed a method; it is now the same 503 the host entry
    /// point answers, and the startup probe has already named the member in the log.
    /// </remarks>
    private static void InvokeLobbyMethod(MethodInfo? method, NMultiplayerTest scene, string action, string screen, params object?[] args)
    {
        if (method == null)
        {
            throw new ApiException(503, "state_unavailable", "This lobby handler is missing in this game build.", new
            {
                action,
                screen
            });
        }

        method.Invoke(scene, args);
    }
}
