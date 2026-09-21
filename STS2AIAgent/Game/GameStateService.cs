using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Game;

internal static partial class GameStateService
{

    private const int StateVersion = 16;

    private static readonly TimeSpan CombatActionSnapshotStableDelay = TimeSpan.FromMilliseconds(200);

    private static string? _lastCombatActionReadinessSignature;

    private static DateTime _lastCombatActionReadinessSinceUtc = DateTime.MinValue;

    private static string? _lastUnlockConfirmProbeSignature;

    private static bool _crystalSphereEntityLookupWarningLogged;

    private static bool _crystalSphereButtonLookupWarningLogged;

    private static FieldInfo? StartRunLobbyMaxPlayersField =>
        ReflectedGameMembers.Field(typeof(StartRunLobby), "_maxPlayers");

    public static GameStatePayload BuildStatePayload()
    {
        // Measured here rather than around the /state route: every action response and SSE refresh
        // builds this too, and all of it runs on the game thread.
        var buildTimer = System.Diagnostics.Stopwatch.StartNew();
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        var screen = ResolveScreen(currentScreen);
        var session = BuildSessionPayload(currentScreen, runState);
        // One state build evaluates the combat gate once and shares it. The gate advances a shared
        // 200ms stability sampler, so evaluating it per action let one response straddle that window
        // and contradict itself: the actions serialized first said "not yet" while the readiness
        // payload built later in the same response said "ready".
        var combatActionGate = EvaluateCombatActionGate(currentScreen, combatState);
        // One walk produces both action surfaces this response carries: the names /state reports and
        // the exact descriptors a decision snapshot hands to an index validator. The descriptors are
        // retained on the payload (internal, so they never serialize) rather than re-derived, which is
        // what keeps a state read and its action surface in the same frame.
        var availableActions = BuildAvailableActionNames(
            currentScreen,
            combatState,
            runState,
            combatActionGate,
            out var availableActionDescriptors);
        var combat = BuildCombatPayload(combatState, combatActionGate);
        var run = BuildRunPayload(currentScreen, combatState, runState, combatActionGate);
        var multiplayer = BuildMultiplayerPayload(currentScreen, runState);
        var multiplayerLobby = BuildMultiplayerLobbyPayload(currentScreen);
        var map = BuildMapPayload(currentScreen, runState);
        var selection = BuildSelectionPayload(currentScreen);
        var characterSelect = BuildCharacterSelectPayload(currentScreen);
        var timeline = BuildTimelinePayload(currentScreen);
        var unlock = BuildUnlockPayload(currentScreen);
        var chest = BuildChestPayload(currentScreen);
        var eventPayload = BuildEventPayload(currentScreen);
        var crystalSphere = BuildCrystalSpherePayload(currentScreen);
        var shop = BuildShopPayload(currentScreen);
        var rest = BuildRestPayload(currentScreen, runState);
        var reward = BuildRewardPayload(currentScreen);
        var bundles = BuildBundlePayload(currentScreen);
        var capstone = BuildCapstonePayload(currentScreen);
        var modal = BuildModalPayload(currentScreen);
        var gameOver = BuildGameOverPayload(currentScreen, runState);

        var payload = new GameStatePayload
        {
            state_version = StateVersion,
            native_profile_id = SaveManager.Instance.CurrentProfileId,
            run_id = runState?.Rng.StringSeed ?? "run_unknown",
            screen = screen,
            session = session,
            in_combat = CombatManager.Instance.IsInProgress,
            turn = combatState?.RoundNumber,
            available_actions = availableActions,
            combat = combat,
            run = run,
            multiplayer = multiplayer,
            multiplayer_lobby = multiplayerLobby,
            map = map,
            selection = selection,
            character_select = characterSelect,
            timeline = timeline,
            unlock = unlock,
            chest = chest,
            @event = eventPayload,
            crystal_sphere = crystalSphere,
            shop = shop,
            rest = rest,
            reward = reward,
            bundles = bundles,
            capstone = capstone,
            modal = modal,
            game_over = gameOver,
            AvailableActionDescriptors = availableActionDescriptors,
            agent_view = BuildAgentViewPayload(
                screen,
                session,
                SaveManager.Instance.CurrentProfileId,
                runState?.Rng.StringSeed ?? "run_unknown",
                combatState?.RoundNumber,
                availableActions,
                combatState,
                runState,
                combat,
                run,
                multiplayer,
                multiplayerLobby,
                map,
                selection,
                characterSelect,
                timeline,
                chest,
                eventPayload,
                crystalSphere,
                shop,
                rest,
                reward,
                bundles,
                capstone,
                unlock,
                modal,
                gameOver)
        };

        var slowBuild = StateBuildTiming.Instance.Record(buildTimer.Elapsed.TotalMilliseconds, screen, DateTime.UtcNow);
        if (slowBuild != null)
        {
            Log.Warn($"[STS2AIAgent] {slowBuild}");
        }

        return payload;
    }

    public static AvailableActionsPayload BuildAvailableActionsPayload()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var runState = RunManager.Instance.DebugOnlyGetState();
        // This endpoint is its own request, so it evaluates the gate itself; a /state build hands in
        // the one it already evaluated.
        var combatActionGate = EvaluateCombatActionGate(currentScreen, combatState);

        return new AvailableActionsPayload
        {
            screen = ResolveScreen(currentScreen),
            actions = EnumerateAvailableActions(currentScreen, combatState, runState, combatActionGate).ToArray()
        };
    }

    /// <summary>
    /// Every action the executor would accept right now, with the parameters each one needs.
    /// </summary>
    /// <remarks>
    /// The single source for both action surfaces. <c>GET /state</c> reports the names from here and
    /// <c>GET /actions/available</c> reports these descriptors, so the two cannot disagree about what
    /// is offered -- they used to be 301 and 609 hand-written lines consulting the same 50 <c>Can*</c>
    /// predicates to emit the same 55 names, kept in step by nothing but care and a contract test.
    /// See docs/adr/0001-single-action-surface.md.
    ///
    /// The gate is passed in rather than evaluated here: it advances a 200 ms stability sampler, and
    /// one state build has to share a single evaluation across its action list, its combat payload
    /// and its potion flags or the response can contradict itself.
    /// </remarks>
    private static List<ActionDescriptor> EnumerateAvailableActions(
        IScreenContext? currentScreen,
        CombatState? combatState,
        RunState? runState,
        CombatActionGate combatActionGate)
    {
        var descriptors = new List<ActionDescriptor>();

        if (GetOpenModal() != null)
        {
            if (CanConfirmModal(currentScreen))
            {
                descriptors.Add(new ActionDescriptor
                {
                    name = "confirm_modal",
                    requires_target = false,
                    requires_index = false
                });
            }

            if (CanDismissModal(currentScreen))
            {
                descriptors.Add(new ActionDescriptor
                {
                    name = "dismiss_modal",
                    requires_target = false,
                    requires_index = false
                });
            }

            return descriptors;
        }

        // The container's pages are human menus over a frozen run: nothing in the run is actionable
        // while one is up, so the descriptor list does not advertise the actions the pause swallows.
        // Backing out one level is the page's own BackButton, and the only action an agent may take
        // here; the pause menu itself has none, because resuming is the person's call, not the agent's.
        if (IsCapstonePageOverlay(currentScreen))
        {
            if (CanCloseMainMenuSubmenu(currentScreen))
            {
                descriptors.Add(new ActionDescriptor
                {
                    name = "close_main_menu_submenu",
                    requires_target = false,
                    requires_index = false
                });
            }

            return descriptors;
        }

        if (currentScreen is NUnlockScreen)
        {
            if (CanConfirmUnlock(currentScreen))
            {
                descriptors.Add(new ActionDescriptor
                {
                    name = "confirm_unlock",
                    requires_target = false,
                    requires_index = false
                });
            }

            return descriptors;
        }

        if (CanEndTurn(currentScreen, combatState, requireButtonReady: false, combatActionGate: combatActionGate))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "end_turn",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanPlayAnyCard(currentScreen, combatState, combatActionGate))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "play_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanSwitchProfile(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "switch_profile",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanContinueRun(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "continue_run",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanAbandonRun(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "abandon_run",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanSaveAndQuit(currentScreen, runState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "save_and_quit",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanOpenCharacterSelect(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_character_select",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanOpenTimeline(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_timeline",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanCloseMainMenuSubmenu(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "close_main_menu_submenu",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanInviteAiTeammate(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "invite_ai_teammate",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanContinueAiTeammate(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "continue_ai_teammate",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanChooseTimelineEpoch(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_timeline_epoch",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanConfirmTimelineOverlay(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "confirm_timeline_overlay",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanChooseMapNode(currentScreen, runState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_map_node",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanCollectRewardsAndProceed(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "resolve_rewards",
                requires_target = false,
                requires_index = false
            });

            descriptors.Add(new ActionDescriptor
            {
                name = "collect_rewards_and_proceed",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanClaimReward(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "claim_reward",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanChooseRewardCard(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_reward_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanSkipRewardCards(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "skip_reward_cards",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanSelectDeckCard(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "select_deck_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanCloseCardsView(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "close_cards_view",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanConfirmSelection(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "confirm_selection",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanProceed(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "proceed",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanOpenChest(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_chest",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanChooseTreasureRelic(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_treasure_relic",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanChooseEventOption(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_event_option",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanChooseCapstoneOption(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_capstone_option",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanPlayCrystalSphere(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "crystal_set_tool",
                requires_target = false,
                requires_index = false,
                requires_tool = true
            });
            descriptors.Add(new ActionDescriptor
            {
                name = "crystal_clear_cell",
                requires_target = false,
                requires_index = false,
                requires_coordinates = true
            });
        }

        if (CanChooseBundle(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_bundle",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanConfirmBundle(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "confirm_bundle",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanChooseRestOption(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "choose_rest_option",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanOpenShopInventory(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "open_shop_inventory",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanCloseShopInventory(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "close_shop_inventory",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanBuyShopCard(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "buy_card",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanBuyShopRelic(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "buy_relic",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanBuyShopPotion(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "buy_potion",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanRemoveCardAtShop(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "remove_card_at_shop",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanSelectCharacter(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "select_character",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanEmbark(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "embark",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanUnready(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "unready",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanHostMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "host_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanJoinMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "join_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanReadyMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "ready_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanDisconnectMultiplayerLobby(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "disconnect_multiplayer_lobby",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanIncreaseAscension(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "increase_ascension",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanDecreaseAscension(currentScreen))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "decrease_ascension",
                requires_target = false,
                requires_index = false
            });
        }

        if (CanUsePotion(currentScreen, combatState, runState, combatActionGate))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "use_potion",
                requires_target = false,
                requires_index = true
            });
        }

        if (CanDiscardPotion(currentScreen, runState))
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "discard_potion",
                requires_target = false,
                requires_index = true
            });
        }

        var gameOver = BuildGameOverPayload(currentScreen, runState) ?? new GameOverPayload();
        if (gameOver.waiting_for_other_players)
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "dismiss_game_over_wait",
                requires_target = false,
                requires_index = false
            });
        }

        if (gameOver.can_continue)
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "continue_game_over",
                requires_target = false,
                requires_index = false
            });
        }
        else if (GetGameOverContinueButton(currentScreen) != null && !gameOver.can_return_to_main_menu)
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "continue_game_over",
                requires_target = false,
                requires_index = false
            });
        }

        if (gameOver.can_return_to_main_menu)
        {
            descriptors.Add(new ActionDescriptor
            {
                name = "return_to_main_menu",
                requires_target = false,
                requires_index = false
            });
        }

        return descriptors;
    }

    public static string ResolveScreen(IScreenContext? currentScreen)
    {
        if (GetOpenModal() != null)
        {
            return "MODAL";
        }

        var screen = ResolveNonModalScreen(currentScreen);
        if (screen == "UNKNOWN" && currentScreen != null)
        {
            Log.Warn($"[STS2AIAgent] Unhandled screen type: {currentScreen.GetType().FullName}");
        }

        return screen;
    }

    public static NCombatRoom? FindActiveCombatRoom(IScreenContext? currentScreen)
    {
        if (currentScreen is NCombatRoom current &&
            GodotObject.IsInstanceValid(current) &&
            current.Mode == CombatRoomMode.ActiveCombat)
        {
            return current;
        }

        if (!CombatManager.Instance.IsInProgress)
        {
            return null;
        }

        try
        {
            var game = NGame.Instance;
            if (game == null || !GodotObject.IsInstanceValid(game))
            {
                return null;
            }

            return FindDescendants<NCombatRoom>(game)
                .FirstOrDefault(room =>
                    GodotObject.IsInstanceValid(room) &&
                    room.IsVisibleInTree() &&
                    room.Mode == CombatRoomMode.ActiveCombat);
        }
        catch
        {
            return null;
        }
    }

    public static Player? GetLocalPlayer(CombatState? combatState)
    {
        return combatState == null ? null : LocalContext.GetMe((ICombatState)combatState);
    }

    public static Player? GetLocalPlayer(RunState? runState)
    {
        return runState == null ? null : LocalContext.GetMe((IPlayerCollection)runState);
    }

    /// <summary>
    /// The names <c>/state.available_actions</c> reports, and the descriptors the same walk produced.
    /// </summary>
    /// <remarks>
    /// The walk is the only source either surface reads, so both cannot advertise different actions;
    /// handing the descriptor list back through <paramref name="descriptors"/> is what lets one state
    /// build answer <c>GET /decision-snapshot</c> from the frame it already enumerated instead of
    /// enumerating again.
    /// </remarks>
    private static string[] BuildAvailableActionNames(
        IScreenContext? currentScreen,
        CombatState? combatState,
        RunState? runState,
        CombatActionGate combatActionGate,
        out ActionDescriptor[] descriptors)
    {
        // A projection of EnumerateAvailableActions, never a second opinion: /state.available_actions
        // and /actions/available answer from one walk so they cannot advertise different actions.
        descriptors = EnumerateAvailableActions(currentScreen, combatState, runState, combatActionGate).ToArray();
        var names = new string[descriptors.Length];
        for (var index = 0; index < descriptors.Length; index++)
        {
            names[index] = descriptors[index].name;
        }

        return names;
    }

    /// <summary>
    /// Everything one decision needs from the game, from a single state build.
    /// </summary>
    /// <remarks>
    /// A caller that reads <c>/state</c> and then <c>/actions/available</c> makes two requests, and
    /// between them the game advances: the state it read and the action indices it was handed can
    /// describe two different frames, which is how an index validator came to accept an action
    /// against a hand the payload it validated never showed. This answers both halves from one
    /// enumeration inside one build. The two individual endpoints stay for callers that want one of
    /// them; this is the one to read when both are needed.
    /// </remarks>
    public static DecisionSnapshotPayload BuildDecisionSnapshotPayload()
    {
        var state = BuildStatePayload();
        return new DecisionSnapshotPayload
        {
            // The compact projection is what a decision reads; the raw payload is the fallback for a
            // build that produced no agent_view, matching what GameBridge has always returned here.
            state = state.agent_view ?? state,
            available_actions = state.AvailableActionDescriptors
        };
    }

    private static string ResolveNonModalScreen(IScreenContext? currentScreen)
    {
        // The capstone container hosts the in-run human menus and keeps its own Type while a page is
        // pushed on top of the one that opened it, so the page on its stack is what has to be named.
        // Naming the container instead reported the run underneath -- browsing the card library from a
        // pause menu read as COMBAT, and the compendium hub as whatever room the run stood in -- while
        // the combat and visible-grid branches below handed the model a live action list and a page's
        // own furniture as capstone options. This has to run before both of them.
        if (currentScreen is NCapstoneSubmenuStack container)
        {
            return container.Stack?.Peek() switch
            {
                NPauseMenu => "PAUSE_MENU",
                NSettingsScreen => "SETTINGS",
                NCompendiumSubmenu => "COMPENDIUM",
                NCardLibrary => "CARD_LIBRARY",
                NRelicCollection => "RELIC_COLLECTION",
                NPotionLab => "POTION_LAB",
                NBestiary => "BESTIARY",
                NStatsScreen => "STATS",
                NRunHistory => "RUN_HISTORY",
                _ => "CAPSTONE_SELECTION",
            };
        }

        if (currentScreen is NUnlockScreen)
        {
            return "UNLOCK";
        }

        if (currentScreen != null &&
            TryGetCombatHandSelection(currentScreen, out _))
        {
            return "CARD_SELECTION";
        }

        if (currentScreen is NCardsViewScreen)
        {
            return "CARDS_VIEW";
        }

        // Both of these carry visible grid card holders, so the generic grid branch below would call them
        // CARD_SELECTION and send the model after select_deck_card, an action they deliberately do not offer.
        if (currentScreen is NCardLibrary)
        {
            return "CARD_LIBRARY";
        }

        if (currentScreen is NCardPileScreen)
        {
            return "CARD_PILE";
        }

        // The reward-card overlay carries visible grid card holders, so the generic grid branch below
        // would name it CARD_SELECTION and send the model after select_deck_card, an action this screen
        // deliberately does not offer. Its own switch arm (REWARD) can only be reached from here, the
        // same way NUnlockScreen claims UNLOCK ahead of the same generic branch.
        if (currentScreen is NCardRewardSelectionScreen)
        {
            return "REWARD";
        }

        if (currentScreen is Node rootNode &&
            currentScreen is not NChooseABundleSelectionScreen &&
            GetVisibleGridCardHolders(rootNode).Count > 0)
        {
            return "CARD_SELECTION";
        }

        if (GetMultiplayerTestScene() != null)
        {
            return "MULTIPLAYER_LOBBY";
        }

        // Death leaves the combat room active, so the combat branch below claimed every game-over
        // screen and its own switch arm was unreachable: a 2026-09-17 live pass caught eight samples
        // whose only offered action was continue_game_over, and all eight reported COMBAT. That name
        // is documented in docs/api.md, the play skill routes on it, and run_sts2_validation.py
        // branches on it, so an agent following the contract waited for a screen it would never see.
        // This has to run before the combat branch for the same reason the capstone branch above does.
        if (currentScreen is NGameOverScreen)
        {
            return "GAME_OVER";
        }

        if (FindActiveCombatRoom(currentScreen) != null)
        {
            return "COMBAT";
        }

        return currentScreen switch
        {
            NGameOverScreen => "GAME_OVER",
            NCardRewardSelectionScreen => "REWARD",
            NChooseACardSelectionScreen => "CARD_SELECTION",
            NDeckCardSelectScreen or NDeckUpgradeSelectScreen or NDeckTransformSelectScreen or NDeckEnchantSelectScreen => "CARD_SELECTION",
            NCardGridSelectionScreen => "CARD_SELECTION",
            NRewardsScreen => "REWARD",
            NTreasureRoom or NTreasureRoomRelicCollection => "CHEST",
            NRestSiteRoom => "REST",
            NMerchantRoom or NMerchantInventory => "SHOP",
            NEventRoom => "EVENT",
            NCombatRoom => "COMBAT",
            NMapScreen or NMapRoom => "MAP",
            NCharacterSelectScreen => "CHARACTER_SELECT",
            NMultiplayerLoadGameScreen => "MULTIPLAYER_LOAD",
            NChooseABundleSelectionScreen => "BUNDLE_SELECTION",
            NCapstoneSubmenuStack => "CAPSTONE_SELECTION",
            NCrystalSphereScreen => "CRYSTAL_SPHERE",
            NTimelineScreen => "TIMELINE",
            NFakeMerchant => "FAKE_MERCHANT",
            NPatchNotesScreen => "PATCH_NOTES",
            NInspectCardScreen => "CARD_INSPECT",
            NInspectRelicScreen => "RELIC_INSPECT",
            NSendFeedbackScreen => "FEEDBACK",
            NSubmenu => "MAIN_MENU",
            NLogoAnimation => "MAIN_MENU",
            NMainMenu => "MAIN_MENU",
            _ => "UNKNOWN"
        };
    }

    private static string? ResolveUnderlyingScreen(Node modalNode)
    {
        var parent = modalNode.GetParent();
        while (parent != null)
        {
            if (parent is IScreenContext screenContext && !ReferenceEquals(parent, modalNode))
            {
                return ResolveNonModalScreen(screenContext);
            }

            parent = parent.GetParent();
        }

        return null;
    }

    public static NSubmenuStack? GetSubmenuStack(Node? node)
    {
        var current = node;
        while (current != null)
        {
            if (current is NSubmenuStack submenuStack)
            {
                return submenuStack;
            }

            current = current.GetParent();
        }

        return null;
    }

    private static List<T> FindDescendants<T>(Node root) where T : Node
    {
        var found = new List<T>();
        FindDescendantsRecursive(root, found);
        return found;
    }

    private static void FindDescendantsRecursive<T>(Node node, List<T> found) where T : Node
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (node is T typedNode)
        {
            found.Add(typedNode);
        }

        foreach (Node child in node.GetChildren())
        {
            FindDescendantsRecursive(child, found);
        }
    }

    private static string? GetButtonLabel(NButton? button)
    {
        if (button == null)
        {
            return null;
        }

        return button.GetNodeOrNull<MegaLabel>("Label")?.Text ?? button.Name.ToString();
    }

    private static IReadOnlyList<NGridCardHolder> GetVisibleGridCardHolders(Node root)
    {
        return FindDescendants<NGridCardHolder>(root)
            .Where(node => GodotObject.IsInstanceValid(node) && node.IsVisibleInTree() && node.CardModel != null)
            .OrderBy(node => node.GlobalPosition.Y)
            .ThenBy(node => node.GlobalPosition.X)
            .ToArray();
    }

    // Shared with the raw /state builders (the combat payload, the turn-end button read, the
    // game-over payload) and with the action services, so these stay beside the builders rather
    // than in GameStateService.Predicates.cs -- the same rule that kept CanPurchaseShopPotion
    // and the IsPotion* probes in this file.

    public static bool IsPlayerActionPhase(CombatState? combatState)
    {
        var me = GetLocalPlayer(combatState);
        return IsPlayerActionPhase(combatState, me);
    }

    private static bool IsPlayerActionPhase(CombatState? combatState, Player? me)
    {
        if (combatState == null ||
            me == null ||
            combatState.CurrentSide != CombatSide.Player)
        {
            return false;
        }

        return CombatManager.Instance.IsPartOfPlayerTurn(me);
    }

    public static bool IsWaitingForOtherPlayers(IScreenContext? currentScreen)
    {
        var overlay = GetWaitingForOtherPlayersOverlay(currentScreen);
        return overlay != null && overlay.IsVisibleInTree();
    }

    private static string SafeModelTitle(AbstractModel model)
    {
        try
        {
            var title = TryGetMemberValue(model, "Title");
            switch (title)
            {
                case string s when !string.IsNullOrWhiteSpace(s):
                    return s;
                case MegaCrit.Sts2.Core.Localization.LocString loc:
                    var text = loc.GetFormattedText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // fall through to id
        }

        return model.Id.Entry;
    }

    /// <summary>The raw text of a localized string, or null when it is empty or cannot be read.</summary>
    /// <remarks>
    /// Replaces a by-name lookup that tried DynamicDescription and then Description. Description's
    /// getter is private, and that lookup only saw public properties, so the second name never
    /// resolved; DynamicDescription is public and read directly.
    /// </remarks>
    private static string? RawTextOrNull(Func<MegaCrit.Sts2.Core.Localization.LocString?> read)
    {
        try
        {
            var text = read()?.GetRawText();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    private static string SafeReadString(Func<string?> getter, string fallback = "")
    {
        try
        {
            var value = getter();
            return value == null ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool SafeReadBool(Func<bool> getter, bool fallback = false)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private static int? SafeReadNullableInt(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }

    private static string TryCoerceText(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var valueType = value.GetType();

        try
        {
            var getRawText = valueType.GetMethod("GetRawText", flags, null, Type.EmptyTypes, null);
            if (getRawText != null && getRawText.ReturnType == typeof(string))
            {
                return getRawText.Invoke(value, null) as string ?? string.Empty;
            }
        }
        catch
        {
        }

        try
        {
            var textProperty = valueType.GetProperty("Text", flags);
            if (textProperty != null && textProperty.PropertyType == typeof(string))
            {
                return textProperty.GetValue(value) as string ?? string.Empty;
            }
        }
        catch
        {
        }

        return value.ToString() ?? string.Empty;
    }

    private static string NormalizeCardRulesText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(value, @"\[(?:/?[^\]]+)\]", string.Empty);
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

    private static object? TryGetMemberValue(object instance, string memberName)
    {
        return ReflectionMemberAccessor.TryGetValue(instance, memberName);
    }

    private static MethodInfo? FindInstanceMethod(Type type, string name, params Type[] parameterTypes)
    {
        var types = parameterTypes.Length == 0 ? Type.EmptyTypes : parameterTypes;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current != null; current = current.BaseType)
        {
            var method = current.GetMethod(name, flags, binder: null, types: types, modifiers: null);
            if (method != null)
            {
                return method;
            }
        }

        return null;
    }

    private static string? GetModelIdEntry(AbstractModel? model)
    {
        if (model == null)
        {
            return null;
        }

        var value = SafeReadString(() =>
        {
            var id = model.GetType().GetProperty("Id")?.GetValue(model);
            return id?.GetType().GetProperty("Entry")?.GetValue(id)?.ToString();
        });
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string NetIdToString(ulong netId)
    {
        return netId.ToString();
    }

    private static bool TryGetMapScreen(IScreenContext? currentScreen, RunState? runState, out NMapScreen? mapScreen)
    {
        mapScreen = currentScreen as NMapScreen ?? NMapScreen.Instance;
        if (runState == null || currentScreen is not (NMapScreen or NMapRoom))
        {
            return false;
        }

        if (mapScreen == null || !GodotObject.IsInstanceValid(mapScreen))
        {
            return false;
        }

        return mapScreen.IsVisibleInTree() && mapScreen.IsOpen;
    }

    private static IReadOnlyCollection<ulong> GetConnectedPlayerIds(RunState? runState)
    {
        if (runState == null)
        {
            return Array.Empty<ulong>();
        }

        var connectedPlayerIds = RunManager.Instance.RunLobby?.PlayerIds?.ToArray();
        if (connectedPlayerIds != null && connectedPlayerIds.Length > 0)
        {
            return connectedPlayerIds;
        }

        return runState.Players.Select(player => player.NetId).ToArray();
    }
}
