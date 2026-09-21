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

    private static SessionPayload BuildSessionPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (GetMultiplayerTestScene() != null)
        {
            return new SessionPayload
            {
                mode = "multiplayer",
                phase = "multiplayer_lobby",
                control_scope = "local_player"
            };
        }

        var characterSelectScreen = GetCharacterSelectScreen(currentScreen);
        if (characterSelectScreen != null)
        {
            return new SessionPayload
            {
                mode = characterSelectScreen.Lobby.NetService.Type.IsMultiplayer() ? "multiplayer" : "singleplayer",
                phase = "character_select",
                control_scope = "local_player"
            };
        }

        // The multiplayer load screen is a lobby over a saved run: players ready up before the run resumes.
        if (GetMultiplayerLoadScreen(currentScreen) != null)
        {
            return new SessionPayload
            {
                mode = "multiplayer",
                phase = "multiplayer_lobby",
                control_scope = "local_player"
            };
        }

        if (runState != null)
        {
            return new SessionPayload
            {
                mode = RunManager.Instance.NetService.Type.IsMultiplayer() ? "multiplayer" : "singleplayer",
                phase = "run",
                control_scope = "local_player"
            };
        }

        return new SessionPayload
        {
            mode = "singleplayer",
            phase = "menu",
            control_scope = "local_player"
        };
    }

    private static MultiplayerPayload? BuildMultiplayerPayload(IScreenContext? currentScreen, RunState? runState)
    {
        var multiplayerTestScene = GetMultiplayerTestScene();
        var multiplayerTestLobby = multiplayerTestScene != null ? GetMultiplayerTestLobby(multiplayerTestScene) : null;
        if (multiplayerTestLobby != null)
        {
            return new MultiplayerPayload
            {
                is_multiplayer = true,
                net_game_type = multiplayerTestLobby.NetService.Type.ToString(),
                local_player_id = NetIdToString(multiplayerTestLobby.LocalPlayer.id),
                player_count = multiplayerTestLobby.Players.Count,
                connected_player_ids = multiplayerTestLobby.Players
                    .OrderBy(player => player.slotId)
                    .Select(player => NetIdToString(player.id))
                    .ToArray()
            };
        }

        var characterSelectScreen = GetCharacterSelectScreen(currentScreen);
        if (characterSelectScreen != null)
        {
            var lobby = characterSelectScreen.Lobby;
            if (!lobby.NetService.Type.IsMultiplayer())
            {
                return null;
            }

            return new MultiplayerPayload
            {
                is_multiplayer = true,
                net_game_type = lobby.NetService.Type.ToString(),
                local_player_id = NetIdToString(lobby.LocalPlayer.id),
                player_count = lobby.Players.Count,
                connected_player_ids = lobby.Players
                    .OrderBy(player => player.slotId)
                    .Select(player => NetIdToString(player.id))
                    .ToArray()
            };
        }

        if (runState == null || !RunManager.Instance.NetService.Type.IsMultiplayer())
        {
            return null;
        }

        var localPlayer = GetLocalPlayer(runState);
        return new MultiplayerPayload
        {
            is_multiplayer = true,
            net_game_type = RunManager.Instance.NetService.Type.ToString(),
            local_player_id = localPlayer != null ? NetIdToString(localPlayer.NetId) : null,
            player_count = runState.Players.Count,
            connected_player_ids = GetConnectedPlayerIds(runState)
                .OrderBy(id => runState.GetPlayerSlotIndex(id))
                .Select(NetIdToString)
                .ToArray()
        };
    }

    private static MultiplayerLobbyPayload? BuildMultiplayerLobbyPayload(IScreenContext? currentScreen)
    {
        var scene = GetMultiplayerTestScene();
        if (scene == null)
        {
            return null;
        }

        var lobby = GetMultiplayerTestLobby(scene);
        var selectedCharacterId = lobby?.LocalPlayer.character?.Id.Entry
            ?? GetMultiplayerTestCharacterPaginator(scene)?.Character?.Id.Entry;
        var localPlayerId = lobby != null ? NetIdToString(lobby.LocalPlayer.id) : null;

        return new MultiplayerLobbyPayload
        {
            net_game_type = lobby?.NetService.Type.ToString() ?? NetGameType.Singleplayer.ToString(),
            join_host = GetMultiplayerLobbyJoinHost(),
            join_port = GetMultiplayerLobbyJoinPort(),
            local_net_id_hint = NetIdToString(GetMultiplayerLobbyJoinNetIdHint()),
            has_lobby = lobby != null,
            is_host = lobby?.NetService.Type == NetGameType.Host,
            is_client = lobby?.NetService.Type == NetGameType.Client,
            local_ready = lobby?.LocalPlayer.isReady ?? false,
            can_host = CanHostMultiplayerLobby(currentScreen),
            can_join = CanJoinMultiplayerLobby(currentScreen),
            can_ready = CanReadyMultiplayerLobby(currentScreen),
            can_disconnect = CanDisconnectMultiplayerLobby(currentScreen),
            can_unready = CanUnready(currentScreen),
            selected_character_id = selectedCharacterId,
            player_count = lobby?.Players.Count ?? 0,
            max_players = lobby != null
                ? GetStartRunLobbyMaxPlayers(lobby)
                : 4,
            players = lobby?.Players
                .OrderBy(player => player.slotId)
                .Select(player => BuildCharacterSelectPlayerPayload(player, lobby.LocalPlayer.id))
                .ToArray() ?? Array.Empty<CharacterSelectPlayerPayload>(),
            characters = GetMultiplayerLobbyCharacters()
                .Select((character, index) => new CharacterSelectOptionPayload
                {
                    index = index,
                    character_id = character.Id.Entry,
                    name = character.Title.GetFormattedText(),
                    is_locked = false,
                    is_selected = selectedCharacterId == character.Id.Entry,
                    is_random = false
                })
                .ToArray()
        };
    }

    private static CharacterSelectPayload? BuildCharacterSelectPayload(IScreenContext? currentScreen)
    {
        var screen = GetCharacterSelectScreen(currentScreen);
        if (screen == null)
        {
            return null;
        }

        var buttons = GetCharacterSelectButtons(currentScreen);
        try
        {
            var lobby = screen.Lobby;
            var localPlayer = lobby.LocalPlayer;
            var waitingPanel = screen.GetNodeOrNull<Control>("ReadyAndWaitingPanel");
            var selectedCharacterId = localPlayer.character?.Id.Entry;

            return new CharacterSelectPayload
            {
                selected_character_id = selectedCharacterId,
                is_multiplayer = lobby.NetService.Type.IsMultiplayer(),
                net_game_type = lobby.NetService.Type.ToString(),
                can_embark = CanEmbark(currentScreen),
                can_unready = CanUnready(currentScreen),
                can_increase_ascension = CanIncreaseAscension(currentScreen),
                can_decrease_ascension = CanDecreaseAscension(currentScreen),
                local_ready = localPlayer.isReady,
                is_waiting_for_players = waitingPanel?.Visible ?? false,
                player_count = lobby.Players.Count,
                max_players = GetStartRunLobbyMaxPlayers(lobby),
                ascension = lobby.Ascension,
                max_ascension = lobby.MaxAscension,
                seed = lobby.Seed,
                modifier_ids = lobby.Modifiers.Select(modifier => modifier.Id.Entry).ToArray(),
                players = lobby.Players
                    .OrderBy(player => player.slotId)
                    .Select(player => BuildCharacterSelectPlayerPayload(player, localPlayer.id))
                    .ToArray(),
                characters = buttons.Select((button, index) => new CharacterSelectOptionPayload
                {
                    index = index,
                    character_id = button.Character.Id.Entry,
                    name = button.Character.Title.GetFormattedText(),
                    is_locked = button.IsLocked,
                    is_selected = button.IsRandom
                        ? selectedCharacterId == button.Character.Id.Entry
                        : selectedCharacterId == button.Character.Id.Entry,
                    is_random = button.IsRandom
                }).ToArray()
            };
        }
        catch
        {
            return new CharacterSelectPayload
            {
                players = Array.Empty<CharacterSelectPlayerPayload>(),
                characters = buttons.Select((button, index) => new CharacterSelectOptionPayload
                {
                    index = index,
                    character_id = button.Character.Id.Entry,
                    name = button.Character.Title.GetFormattedText(),
                    is_locked = button.IsLocked,
                    is_selected = false,
                    is_random = button.IsRandom
                }).ToArray()
            };
        }
    }

    private static CharacterSelectPlayerPayload BuildCharacterSelectPlayerPayload(StartRunLobbyPlayer player, ulong localPlayerId)
    {
        return new CharacterSelectPlayerPayload
        {
            player_id = NetIdToString(player.id),
            slot_index = player.slotId,
            is_local = player.id == localPlayerId,
            character_id = player.character?.Id.Entry,
            character_name = player.character?.Title.GetFormattedText(),
            is_ready = player.isReady,
            max_multiplayer_ascension_unlocked = player.maxMultiplayerAscensionUnlocked
        };
    }

    private static TimelinePayload? BuildTimelinePayload(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        if (timelineScreen == null)
        {
            return null;
        }

        var slots = GetTimelineSlots(currentScreen)
            .Select((slot, index) => new TimelineSlotPayload
            {
                index = index,
                epoch_id = slot.model.Id,
                title = slot.model.Title.GetFormattedText() ?? slot.model.Id,
                state = slot.State.ToString().ToLowerInvariant(),
                is_actionable = slot.State is EpochSlotState.Obtained or EpochSlotState.Complete
            })
            .ToArray();

        return new TimelinePayload
        {
            back_enabled = GetTimelineBackButton(currentScreen)?.IsEnabled == true,
            inspect_open = GetTimelineInspectScreen(currentScreen)?.Visible == true,
            unlock_screen_open = GetTimelineUnlockScreen(currentScreen) != null,
            tutorial_open = GetTimelineTutorial(currentScreen) != null,
            can_choose_epoch = CanChooseTimelineEpoch(currentScreen),
            can_confirm_overlay = CanConfirmTimelineOverlay(currentScreen),
            slots = slots
        };
    }

    private static UnlockPayload? BuildUnlockPayload(IScreenContext? currentScreen)
    {
        var unlockScreen = GetActiveUnlockScreen(currentScreen);
        if (unlockScreen == null)
        {
            return null;
        }

        return new UnlockPayload
        {
            unlock_type = unlockScreen.GetType().Name,
            items = GetUnlockItemNames(unlockScreen),
            can_confirm = CanConfirmUnlock(currentScreen),
        };
    }

    public static NCharacterSelectScreen? GetCharacterSelectScreen(IScreenContext? currentScreen)
    {
        return currentScreen as NCharacterSelectScreen;
    }

    public static IReadOnlyList<NCharacterSelectButton> GetCharacterSelectButtons(IScreenContext? currentScreen)
    {
        var screen = GetCharacterSelectScreen(currentScreen);
        if (screen == null)
        {
            return Array.Empty<NCharacterSelectButton>();
        }

        return FindDescendants<NCharacterSelectButton>(screen)
            .Where(node => GodotObject.IsInstanceValid(node))
            .OrderBy(node => node.GlobalPosition.Y)
            .ThenBy(node => node.GlobalPosition.X)
            .ToArray();
    }

    public static NConfirmButton? GetCharacterEmbarkButton(IScreenContext? currentScreen)
    {
        var load = GetMultiplayerLoadScreen(currentScreen);
        if (load != null) return load.GetNodeOrNull<NConfirmButton>("ConfirmButton");
        return GetCharacterSelectScreen(currentScreen)?.GetNodeOrNull<NConfirmButton>("ConfirmButton");
    }

    // Only the character-select screen advertises unready: the executor has no load-screen path for it.
    public static NBackButton? GetCharacterUnreadyButton(IScreenContext? currentScreen)
    {
        return GetCharacterSelectScreen(currentScreen)?.GetNodeOrNull<NBackButton>("UnreadyButton");
    }

    public static NMultiplayerTest? GetMultiplayerTestScene()
    {
        var currentScene = NGame.Instance?.RootSceneContainer?.CurrentScene;
        return currentScene is NMultiplayerTest multiplayerTest && multiplayerTest.IsVisibleInTree()
            ? multiplayerTest
            : null;
    }

    public static StartRunLobby? GetMultiplayerTestLobby(NMultiplayerTest scene)
    {
        var field = ReflectedGameMembers.Field(typeof(NMultiplayerTest), "_lobby");
        return field?.GetValue(scene) as StartRunLobby;
    }

    private static int GetStartRunLobbyMaxPlayers(StartRunLobby lobby)
    {
        return StartRunLobbyMaxPlayersField?.GetValue(lobby) is int parsed ? parsed : 0;
    }

    public static void EnsureFourPlayerLobby()
    {
        var scene = GetMultiplayerTestScene();
        var lobby = scene != null ? GetMultiplayerTestLobby(scene) : GetCharacterSelectScreen(ActiveScreenContext.Instance.GetCurrentScreen())?.Lobby;
        if (lobby == null || StartRunLobbyMaxPlayersField == null)
        {
            return;
        }

        StartRunLobbyMaxPlayersField.SetValue(lobby, CoopLaunchPolicy.MaxLobbyPlayers);
    }

    public static NMultiplayerTestCharacterPaginator? GetMultiplayerTestCharacterPaginator(NMultiplayerTest scene)
    {
        return scene.GetNodeOrNull<NMultiplayerTestCharacterPaginator>("CharacterChooser");
    }

    public static string GetMultiplayerLobbyJoinHost()
    {
        var raw = System.Environment.GetEnvironmentVariable("STS2_MULTIPLAYER_HOST_IP");
        return string.IsNullOrWhiteSpace(raw) ? "127.0.0.1" : raw.Trim();
    }

    public static int GetMultiplayerLobbyJoinPort()
    {
        return 33771;
    }

    public static ulong GetMultiplayerLobbyJoinNetIdHint()
    {
        var raw = System.Environment.GetEnvironmentVariable("STS2_MULTIPLAYER_NET_ID");
        if (!string.IsNullOrWhiteSpace(raw) && ulong.TryParse(raw.Trim(), out var parsed))
        {
            return parsed;
        }

        return (ulong)System.Environment.ProcessId;
    }

    public static CharacterModel[] GetMultiplayerLobbyCharacters()
    {
        return
        [
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Regent>(),
            ModelDb.Character<Necrobinder>(),
            ModelDb.Character<Defect>()
        ];
    }

    public static NMultiplayerLoadGameScreen? GetMultiplayerLoadScreen(IScreenContext? currentScreen)
    {
        return currentScreen as NMultiplayerLoadGameScreen;
    }

    public static NTimelineScreen? GetTimelineScreen(IScreenContext? currentScreen)
    {
        if (currentScreen is NTimelineScreen currentTimeline && currentTimeline.IsVisibleInTree())
        {
            return currentTimeline;
        }

        try
        {
            var instance = NTimelineScreen.Instance;
            if (instance != null && GodotObject.IsInstanceValid(instance) && instance.IsVisibleInTree())
            {
                return instance;
            }
        }
        catch
        {
        }

        if (currentScreen is Node node)
        {
            return FindDescendants<NTimelineScreen>(node).FirstOrDefault(screen => screen.IsVisibleInTree());
        }

        return null;
    }

    public static NTimelineTutorial? GetTimelineTutorial(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        if (timelineScreen == null)
        {
            return null;
        }

        return FindDescendants<NTimelineTutorial>(timelineScreen)
            .FirstOrDefault(tutorial => GodotObject.IsInstanceValid(tutorial) && tutorial.IsVisibleInTree());
    }

    public static NButton? GetTimelineTutorialAcknowledgeButton(IScreenContext? currentScreen)
    {
        var tutorial = GetTimelineTutorial(currentScreen);
        if (tutorial == null)
        {
            return null;
        }

        var named = tutorial.GetNodeOrNull<NButton>("%AcknowledgeButton");
        if (named != null && GodotObject.IsInstanceValid(named))
        {
            return named;
        }

        return FindDescendants<NButton>(tutorial).FirstOrDefault(button =>
            GodotObject.IsInstanceValid(button) && button.IsVisibleInTree());
    }

    public static IReadOnlyList<NEpochSlot> GetTimelineSlots(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        if (timelineScreen == null)
        {
            return Array.Empty<NEpochSlot>();
        }

        return FindDescendants<NEpochSlot>(timelineScreen)
            .Where(slot => slot.IsVisibleInTree() && slot.model != null && slot.State != EpochSlotState.NotObtained)
            .OrderBy(slot => slot.GlobalPosition.X)
            .ThenBy(slot => slot.GlobalPosition.Y)
            .ToArray();
    }

    public static NEpochInspectScreen? GetTimelineInspectScreen(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        var inspectScreen = timelineScreen?.GetNodeOrNull<NEpochInspectScreen>("%EpochInspectScreen");
        return inspectScreen?.Visible == true ? inspectScreen : null;
    }

    public static NUnlockScreen? GetTimelineUnlockScreen(IScreenContext? currentScreen)
    {
        var timelineScreen = GetTimelineScreen(currentScreen);
        if (timelineScreen == null)
        {
            return null;
        }

        return FindDescendants<NUnlockScreen>(timelineScreen)
            .FirstOrDefault(screen => screen.IsVisibleInTree());
    }

    /// <summary>
    /// The post-run / milestone unlock showcase (e.g. "Relic unlocked!") takes over the
    /// screen context itself (not nested under the timeline screen), so it needs its own path.
    /// </summary>
    public static NUnlockScreen? GetActiveUnlockScreen(IScreenContext? currentScreen)
    {
        var unlockScreen = currentScreen as NUnlockScreen;
        return unlockScreen != null && unlockScreen.IsVisibleInTree() ? unlockScreen : null;
    }

    public static NButton? GetUnlockConfirmButton(IScreenContext? currentScreen)
    {
        var unlockScreen = GetActiveUnlockScreen(currentScreen);
        if (unlockScreen == null)
        {
            _lastUnlockConfirmProbeSignature = null;
            return null;
        }

        var confirmField = ReflectedGameMembers.Field(typeof(NUnlockScreen), "_unlockConfirmButton");
        var declaringType = confirmField?.DeclaringType;
        var reflected = confirmField?.GetValue(unlockScreen) as NButton;
        var memberSource = $"member:{declaringType?.FullName ?? "unknown"}";
        var reflectedValid = reflected != null && GodotObject.IsInstanceValid(reflected);
        var memberStatus = reflectedValid ? "unusable" : "unavailable";

        // Keep an exact-type node-tree fallback in case a future game build renames the field
        // or leaves a stale, non-actionable backing-field node behind.
        var descendants = FindDescendants<NUnlockConfirmButton>(unlockScreen);
        var selected = UnlockConfirmResolutionPolicy.SelectUsable(
            reflected, descendants, IsUnlockConfirmButtonUsable);
        if (selected != null)
        {
            var source = ReferenceEquals(selected, reflected)
                ? memberSource
                : $"descendant:NUnlockConfirmButton;{memberSource}:{memberStatus}";
            LogUnlockConfirmProbe(unlockScreen, selected, source);
            return selected;
        }

        // Emit one final-resolution probe per state instead of logging both the unusable
        // reflected candidate and the failed fallback, which would alternate signatures
        // and defeat deduplication on every state poll.
        var validDiagnosticDescendant = descendants.FirstOrDefault(GodotObject.IsInstanceValid);
        var diagnosticButton = reflectedValid ? reflected : validDiagnosticDescendant;
        var descendantStatus = descendants.Count == 0 ? "none" : "no-usable";
        var diagnosticSource = reflectedValid
            ? $"{memberSource}:unusable;descendant:{descendantStatus}"
            : validDiagnosticDescendant != null
                ? $"descendant:NUnlockConfirmButton:unusable;{memberSource}:{memberStatus}"
                : $"{memberSource}:{memberStatus};descendant:{descendantStatus}";
        LogUnlockConfirmProbe(unlockScreen, diagnosticButton, diagnosticSource);
        return null;
    }

    private static bool IsUnlockConfirmButtonUsable(NButton? button)
    {
        return button != null &&
               GodotObject.IsInstanceValid(button) &&
               button.IsVisibleInTree() &&
               button.IsEnabled;
    }

    private static void LogUnlockConfirmProbe(
        NUnlockScreen unlockScreen, NButton? button, string source)
    {
        var valid = button != null && GodotObject.IsInstanceValid(button);
        var visible = valid && button!.IsVisibleInTree();
        var enabled = valid && button!.IsEnabled;
        var screenType = unlockScreen.GetType().FullName ?? unlockScreen.GetType().Name;
        var screenInstanceId = unlockScreen.GetInstanceId();
        var buttonType = valid ? button!.GetType().FullName ?? button.GetType().Name : "null";
        var buttonPath = valid ? button!.GetPath().ToString() : "null";
        var signature = UnlockConfirmResolutionPolicy.BuildProbeSignature(
            screenType, screenInstanceId, source, buttonType, buttonPath, visible, enabled);
        if (signature == _lastUnlockConfirmProbeSignature)
        {
            return;
        }

        _lastUnlockConfirmProbeSignature = signature;
        Log.Info(
            $"[STS2AIAgent] Unlock confirm probe: screen={screenType}, instance={screenInstanceId}, " +
            $"source={source}, " +
            $"button={buttonType}, path={buttonPath}, visible={visible}, enabled={enabled}");
    }

    private static string[] GetUnlockItemNames(NUnlockScreen unlockScreen)
    {
        var names = new List<string>();
        foreach (var field in ReflectedGameMembers.UnlockItemFields)
        {
            // Each item field lives on its own concrete unlock screen; the others simply do not apply.
            if (field.DeclaringType?.IsInstanceOfType(unlockScreen) != true)
            {
                continue;
            }

            var value = field.GetValue(unlockScreen);
            switch (value)
            {
                case null:
                    continue;
                case AbstractModel model:
                    names.Add(SafeModelTitle(model));
                    break;
                case System.Collections.IEnumerable items:
                    foreach (var item in items)
                    {
                        if (item is AbstractModel itemModel)
                        {
                            names.Add(SafeModelTitle(itemModel));
                        }
                        else if (item != null)
                        {
                            names.Add(item.ToString() ?? "");
                        }
                    }
                    break;
            }
        }

        return names.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
    }

    public static NButton? GetTimelineBackButton(IScreenContext? currentScreen)
    {
        return GetTimelineScreen(currentScreen)?.GetNodeOrNull<NButton>("BackButton");
    }

    public static NButton? GetTimelineInspectCloseButton(IScreenContext? currentScreen)
    {
        return GetTimelineInspectScreen(currentScreen)?.GetNodeOrNull<NButton>("%CloseButton");
    }

    public static NButton? GetTimelineUnlockConfirmButton(IScreenContext? currentScreen)
    {
        return GetTimelineUnlockScreen(currentScreen)?.GetNodeOrNull<NButton>("ConfirmButton");
    }

    public static NMainMenuTextButton? GetMainMenuTimelineButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/TimelineButton");
    }
}
