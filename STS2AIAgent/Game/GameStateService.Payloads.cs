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

// The payload types of GET /state, moved out of GameStateService.cs on 2026-09-17.
//
// They are declarations, not logic: 60 types whose property names are the wire format an agent
// reads, and which docs/api.md documents row for row. Keeping them beside the builders meant a
// 1,170-line tail that had to be scrolled past to reach any of them, and it made the builder file
// look bigger than the code in it. Adding a field is still two edits and a docs/api.md row -- the
// api-facts gate refuses a field that no table names -- but now the record is where records are.

internal sealed class GameStatePayload
{
    public int state_version { get; init; }

    public int native_profile_id { get; init; }

    public string run_id { get; init; } = "run_unknown";

    public string screen { get; init; } = "UNKNOWN";

    public SessionPayload session { get; init; } = new();

    public bool in_combat { get; init; }

    public int? turn { get; init; }

    public string[] available_actions { get; init; } = Array.Empty<string>();

    public CombatPayload? combat { get; init; }

    public RunPayload? run { get; init; }

    public MultiplayerPayload? multiplayer { get; init; }

    public MultiplayerLobbyPayload? multiplayer_lobby { get; init; }

    public MapPayload? map { get; init; }

    public SelectionPayload? selection { get; init; }

    public CharacterSelectPayload? character_select { get; init; }

    public TimelinePayload? timeline { get; init; }

    public UnlockPayload? unlock { get; init; }

    public ChestPayload? chest { get; init; }

    public EventPayload? @event { get; init; }

    public CrystalSpherePayload? crystal_sphere { get; init; }

    public ShopPayload? shop { get; init; }

    public RestPayload? rest { get; init; }

    public RewardPayload? reward { get; init; }

    public BundlePayload[]? bundles { get; init; }

    public object? capstone { get; init; }

    public ModalPayload? modal { get; init; }

    public GameOverPayload? game_over { get; init; }

    public object? agent_view { get; init; }

    /// <summary>
    /// The exact descriptors <see cref="GameStateService.BuildStatePayload"/>'s own action-surface
    /// walk produced for this frame.
    /// </summary>
    /// <remarks>
    /// Internal on purpose, and not a wire field: <c>System.Text.Json</c> writes public properties
    /// only, so this never reaches <c>GET /state</c> and the api-facts gate never reads it as a
    /// documented field. <c>/state</c> keeps reporting the name projection it always has; this is
    /// what <see cref="GameStateService.BuildDecisionSnapshotPayload"/> hands to a caller that needs
    /// the descriptors of the same frame, instead of that caller reading the action surface again and
    /// risking a different one.
    /// </remarks>
    internal ActionDescriptor[] AvailableActionDescriptors { get; init; } = Array.Empty<ActionDescriptor>();
}

internal sealed class SessionPayload
{
    public string mode { get; init; } = "singleplayer";

    public string phase { get; init; } = "menu";

    public string control_scope { get; init; } = "local_player";
}

internal sealed class AvailableActionsPayload
{
    public string screen { get; init; } = "UNKNOWN";

    public ActionDescriptor[] actions { get; init; } = Array.Empty<ActionDescriptor>();
}

/// <summary>
/// One decision's worth of state: the compact view plus the descriptors from the very same state
/// build. <c>GET /decision-snapshot</c> answers this under the ordinary envelope.
/// </summary>
/// <remarks>
/// It is deliberately not a second <c>/state</c>: <c>state</c> carries what the compact
/// <c>agent_view</c> carries (the raw payload when a build produced no view at all), and
/// <c>available_actions</c> is the descriptor list the same enumeration that produced
/// <c>/state.available_actions</c> emitted. Reading the two halves from one build is the whole
/// point -- two reads can straddle a frame boundary and describe different ones.
/// </remarks>
internal sealed class DecisionSnapshotPayload
{
    public object state { get; init; } = new();

    public ActionDescriptor[] available_actions { get; init; } = Array.Empty<ActionDescriptor>();
}

internal sealed class CombatPayload
{
    public CombatActionReadinessPayload action_readiness { get; init; } = new();

    public CombatPlayerPayload player { get; init; } = new();

    public CombatPlayerSummaryPayload[] players { get; init; } = Array.Empty<CombatPlayerSummaryPayload>();

    public CombatHandCardPayload[] hand { get; init; } = Array.Empty<CombatHandCardPayload>();

    public CombatEnemyPayload[] enemies { get; init; } = Array.Empty<CombatEnemyPayload>();

    public bool end_turn_will_kill_player { get; init; }

    public CombatLethalRiskPayload[] lethal_risks { get; init; } = Array.Empty<CombatLethalRiskPayload>();
}

internal sealed class CombatActionReadinessPayload
{
    public bool can_use_combat_actions { get; init; }

    public string reason { get; init; } = string.Empty;

    public bool actions_settled { get; init; }

    public string? running_action_type { get; init; }

    public string? ready_action_type { get; init; }

    public bool modal_open { get; init; }

    public string? modal_type { get; init; }

    public bool player_actions_disabled { get; init; }

    public bool is_paused { get; init; }

    public bool local_ready_to_end_turn { get; init; }

    public bool all_players_ready_to_end_turn { get; init; }

    public bool ending_turn_phase_one { get; init; }

    public bool ending_turn_phase_two { get; init; }

    public string? end_turn_kick { get; init; }

    public bool combat_in_progress { get; init; }

    public bool combat_over_or_ending { get; init; }

    public string? combat_room_mode { get; init; }

    public bool? hand_in_card_play { get; init; }

    public bool? hand_in_card_selection { get; init; }

    public string? hand_mode { get; init; }

    public bool local_turn_ready { get; init; }

    public bool snapshot_stable { get; init; }

    public bool player_action_phase { get; init; }
}

internal sealed class RunPayload
{
    public string character_id { get; init; } = string.Empty;

    public string character_name { get; init; } = string.Empty;

    public int ascension { get; init; }

    public AscensionEffectPayload[] ascension_effects { get; init; } = Array.Empty<AscensionEffectPayload>();

    public int floor { get; init; }

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int gold { get; init; }

    public int max_energy { get; init; }

    public int base_orb_slots { get; init; }

    public string? act_id { get; init; }

    public string? boss_id { get; init; }

    public DeckCardPayload[] deck { get; init; } = Array.Empty<DeckCardPayload>();

    public RunRelicPayload[] relics { get; init; } = Array.Empty<RunRelicPayload>();

    public RunPlayerSummaryPayload[] players { get; init; } = Array.Empty<RunPlayerSummaryPayload>();

    public RunPotionPayload[] potions { get; init; } = Array.Empty<RunPotionPayload>();
}

internal sealed class AscensionEffectPayload
{
    public string id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;
}

internal sealed class MultiplayerPayload
{
    public bool is_multiplayer { get; init; }

    public string net_game_type { get; init; } = string.Empty;

    public string? local_player_id { get; init; }

    public int player_count { get; init; }

    public string[] connected_player_ids { get; init; } = Array.Empty<string>();
}

internal sealed class MultiplayerLobbyPayload
{
    public string net_game_type { get; init; } = string.Empty;

    public string join_host { get; init; } = "127.0.0.1";

    public int join_port { get; init; }

    public string? local_net_id_hint { get; init; }

    public bool has_lobby { get; init; }

    public bool is_host { get; init; }

    public bool is_client { get; init; }

    public bool local_ready { get; init; }

    public bool can_host { get; init; }

    public bool can_join { get; init; }

    public bool can_ready { get; init; }

    public bool can_disconnect { get; init; }

    public bool can_unready { get; init; }

    public string? selected_character_id { get; init; }

    public int player_count { get; init; }

    public int max_players { get; init; }

    public CharacterSelectPlayerPayload[] players { get; init; } = Array.Empty<CharacterSelectPlayerPayload>();

    public CharacterSelectOptionPayload[] characters { get; init; } = Array.Empty<CharacterSelectOptionPayload>();
}

internal sealed class MapPayload
{
    public MapCoordPayload? current_node { get; init; }

    public bool is_travel_enabled { get; init; }

    public bool is_traveling { get; init; }

    public int map_generation_count { get; init; }

    public int rows { get; init; }

    public int cols { get; init; }

    public MapCoordPayload? starting_node { get; init; }

    public MapCoordPayload? boss_node { get; init; }

    public MapCoordPayload? second_boss_node { get; init; }

    public MapGraphNodePayload[] nodes { get; init; } = Array.Empty<MapGraphNodePayload>();

    public MapNodePayload[] available_nodes { get; init; } = Array.Empty<MapNodePayload>();

    public MapCoordPayload? local_vote { get; init; }

    public MapPlayerVotePayload[] player_votes { get; init; } = Array.Empty<MapPlayerVotePayload>();
}

internal sealed class SelectionPayload
{
    public string kind { get; init; } = string.Empty;

    public string prompt { get; init; } = string.Empty;

    public int min_select { get; init; } = 1;

    public int max_select { get; init; } = 1;

    public int selected_count { get; init; }

    public bool requires_confirmation { get; init; }

    public bool can_confirm { get; init; }

    public SelectionCardPayload[] cards { get; init; } = Array.Empty<SelectionCardPayload>();
}

internal readonly record struct CombatHandSelectionMetadata(
    int MinSelect,
    int MaxSelect,
    int SelectedCount,
    bool RequiresConfirmation,
    bool CanConfirm);

internal readonly record struct CardGridSelectionMetadata(
    int MinSelect,
    int MaxSelect,
    int SelectedCount,
    bool RequiresConfirmation,
    bool CanConfirm);

internal sealed class CharacterSelectPayload
{
    public string? selected_character_id { get; init; }

    public bool is_multiplayer { get; init; }

    public string net_game_type { get; init; } = string.Empty;

    public bool can_embark { get; init; }

    public bool can_unready { get; init; }

    public bool can_increase_ascension { get; init; }

    public bool can_decrease_ascension { get; init; }

    public bool local_ready { get; init; }

    public bool is_waiting_for_players { get; init; }

    public int player_count { get; init; }

    public int max_players { get; init; }

    public int ascension { get; init; }

    public int max_ascension { get; init; }

    public string? seed { get; init; }

    public string[] modifier_ids { get; init; } = Array.Empty<string>();

    public CharacterSelectPlayerPayload[] players { get; init; } = Array.Empty<CharacterSelectPlayerPayload>();

    public CharacterSelectOptionPayload[] characters { get; init; } = Array.Empty<CharacterSelectOptionPayload>();
}

internal sealed class CharacterSelectPlayerPayload
{
    public string player_id { get; init; } = string.Empty;

    public int slot_index { get; init; }

    public bool is_local { get; init; }

    public string? character_id { get; init; }

    public string? character_name { get; init; }

    public bool is_ready { get; init; }

    public int max_multiplayer_ascension_unlocked { get; init; }
}

internal sealed class CharacterSelectOptionPayload
{
    public int index { get; init; }

    public string character_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool is_locked { get; init; }

    public bool is_selected { get; init; }

    public bool is_random { get; init; }
}

internal sealed class TimelinePayload
{
    public bool back_enabled { get; init; }

    public bool inspect_open { get; init; }

    public bool unlock_screen_open { get; init; }

    public bool tutorial_open { get; init; }

    public bool can_choose_epoch { get; init; }

    public bool can_confirm_overlay { get; init; }

    public TimelineSlotPayload[] slots { get; init; } = Array.Empty<TimelineSlotPayload>();
}

internal sealed class TimelineSlotPayload
{
    public int index { get; init; }

    public string epoch_id { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string state { get; init; } = string.Empty;

    public bool is_actionable { get; init; }
}

internal sealed class ChestPayload
{
    public bool is_opened { get; init; }

    public bool has_relic_been_claimed { get; init; }

    public ChestRelicOptionPayload[] relic_options { get; init; } = Array.Empty<ChestRelicOptionPayload>();
}

internal sealed class ChestRelicOptionPayload
{
    public int index { get; init; }

    public string relic_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;
}

internal sealed class EventPayload
{
    public string event_id { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool is_finished { get; init; }

    public EventOptionPayload[] options { get; init; } = Array.Empty<EventOptionPayload>();
}

internal sealed class EventOptionPayload
{
    public int index { get; init; }

    public string text_key { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool is_locked { get; init; }

    public bool is_proceed { get; init; }

    public bool will_kill_player { get; init; }

    public bool has_relic_preview { get; init; }
}

internal sealed class RestPayload
{
    public RestOptionPayload[] options { get; init; } = Array.Empty<RestOptionPayload>();
}

internal sealed class RestOptionPayload
{
    public int index { get; init; }

    public string option_id { get; init; } = string.Empty;

    public string title { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool is_enabled { get; init; }

    public bool requires_target { get; init; }

    public string? target_index_space { get; init; }

    public int[] valid_target_indices { get; init; } = Array.Empty<int>();

    public string[] valid_target_player_ids { get; init; } = Array.Empty<string>();
}

internal sealed class CrystalSpherePayload
{
    public int divinations_left { get; init; }

    public string tool { get; init; } = "none";

    public bool is_finished { get; init; }

    public int grid_width { get; init; }

    public int grid_height { get; init; }

    public int[][] hidden_cells { get; init; } = Array.Empty<int[]>();

    public CrystalSphereItemPayload[] items { get; init; } = Array.Empty<CrystalSphereItemPayload>();
}

internal sealed class CrystalSphereItemPayload
{
    /// <summary>
    /// What the item is, or <c>null</c> while any of its cells is still hidden.
    /// </summary>
    /// <remarks>
    /// Identity is what a divination buys, so it is withheld until <c>revealed</c> is true; the
    /// serializer writes nulls rather than omitting keys, so the field is always present on the wire.
    /// </remarks>
    public string? kind { get; init; }

    /// <summary>
    /// Whether the item is a reward (<c>false</c> is a curse), or <c>null</c> while hidden.
    /// </summary>
    public bool? is_good { get; init; }

    public int x { get; init; }

    public int y { get; init; }

    public int width { get; init; }

    public int height { get; init; }

    public bool revealed { get; init; }

    public int[][] cells { get; init; } = Array.Empty<int[]>();

    public int[][] hidden_cells { get; init; } = Array.Empty<int[]>();
}

internal sealed class ShopPayload
{
    public bool is_open { get; init; }

    public bool can_open { get; init; }

    public bool can_close { get; init; }

    public ShopCardPayload[] cards { get; init; } = Array.Empty<ShopCardPayload>();

    public ShopRelicPayload[] relics { get; init; } = Array.Empty<ShopRelicPayload>();

    public ShopPotionPayload[] potions { get; init; } = Array.Empty<ShopPotionPayload>();

    public ShopCardRemovalPayload? card_removal { get; init; }
}

internal sealed class ShopCardPayload
{
    public int index { get; init; }

    public string category { get; init; } = string.Empty;

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;

    public string resolved_rules_text { get; init; } = string.Empty;

    public CardDynamicValuePayload[] dynamic_values { get; init; } = Array.Empty<CardDynamicValuePayload>();

    public int price { get; init; }

    public bool on_sale { get; init; }

    public bool is_stocked { get; init; }

    public bool enough_gold { get; init; }
}

internal sealed class ShopRelicPayload
{
    public int index { get; init; }

    public string relic_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public int price { get; init; }

    public bool is_stocked { get; init; }

    public bool enough_gold { get; init; }
}

internal sealed class ShopPotionPayload
{
    public int index { get; init; }

    public string? potion_id { get; init; }

    public string? name { get; init; }

    public string? rarity { get; init; }

    public string? usage { get; init; }

    public int price { get; init; }

    public bool is_stocked { get; init; }

    public bool enough_gold { get; init; }
}

internal sealed class ShopCardRemovalPayload
{
    public int price { get; init; }

    public bool available { get; init; }

    public bool used { get; init; }

    public bool enough_gold { get; init; }
}

internal sealed class MapCoordPayload
{
    public int row { get; init; }

    public int col { get; init; }
}

internal sealed class MapNodePayload
{
    public int index { get; init; }

    public int row { get; init; }

    public int col { get; init; }

    public string node_type { get; init; } = string.Empty;

    public string state { get; init; } = string.Empty;

    public int vote_count { get; init; }

    public bool has_local_vote { get; init; }

    public string[] voted_player_ids { get; init; } = Array.Empty<string>();
}

internal sealed class MapPlayerVotePayload
{
    public string player_id { get; init; } = string.Empty;

    public int slot_index { get; init; }

    public bool is_local { get; init; }

    public MapCoordPayload? coord { get; init; }
}

internal sealed class MapGraphNodePayload
{
    public int row { get; init; }

    public int col { get; init; }

    public string node_type { get; init; } = string.Empty;

    public string state { get; init; } = string.Empty;

    public bool visited { get; init; }

    public bool is_current { get; init; }

    public bool is_available { get; init; }

    public bool is_start { get; init; }

    public bool is_boss { get; init; }

    public bool is_second_boss { get; init; }

    public MapCoordPayload[] parents { get; init; } = Array.Empty<MapCoordPayload>();

    public MapCoordPayload[] children { get; init; } = Array.Empty<MapCoordPayload>();
}

internal sealed class CombatPlayerPayload
{
    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int block { get; init; }

    public int energy { get; init; }

    public int stars { get; init; }

    public int focus { get; init; }

    public CombatPowerPayload[] powers { get; init; } = Array.Empty<CombatPowerPayload>();

    public int base_orb_slots { get; init; }

    public int orb_capacity { get; init; }

    public int empty_orb_slots { get; init; }

    public CombatOrbPayload[] orbs { get; init; } = Array.Empty<CombatOrbPayload>();

    public CombatPetPayload[] pets { get; init; } = Array.Empty<CombatPetPayload>();

    public bool pet_missing { get; init; }

    public int cards_played_this_turn { get; init; }

    public int attacks_played_this_turn { get; init; }

    public int skills_played_this_turn { get; init; }
}

internal sealed class CombatPlayerSummaryPayload
{
    public string player_id { get; init; } = string.Empty;

    public int slot_index { get; init; }

    public bool is_local { get; init; }

    public bool is_connected { get; init; }

    public string character_id { get; init; } = string.Empty;

    public string character_name { get; init; } = string.Empty;

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int block { get; init; }

    public int energy { get; init; }

    public int stars { get; init; }

    public int focus { get; init; }

    public bool is_alive { get; init; }
}

internal sealed class RunPlayerSummaryPayload
{
    public string player_id { get; init; } = string.Empty;

    public int slot_index { get; init; }

    public bool is_local { get; init; }

    public bool is_connected { get; init; }

    public string character_id { get; init; } = string.Empty;

    public string character_name { get; init; } = string.Empty;

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int gold { get; init; }

    public bool is_alive { get; init; }
}

internal sealed class CombatPetPayload
{
    public int index { get; init; }

    public string pet_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int block { get; init; }

    public CombatPowerPayload[] powers { get; init; } = Array.Empty<CombatPowerPayload>();
}

internal sealed class CombatOrbPayload
{
    public int slot_index { get; init; }

    public string orb_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public decimal passive_value { get; init; }

    public decimal evoke_value { get; init; }

    public bool is_front { get; init; }
}

internal sealed class CombatHandCardPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string target_type { get; init; } = string.Empty;

    public bool requires_target { get; init; }

    public string? target_index_space { get; init; }

    public int[] valid_target_indices { get; init; } = Array.Empty<int>();

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;

    public string resolved_rules_text { get; init; } = string.Empty;

    public CardDynamicValuePayload[] dynamic_values { get; init; } = Array.Empty<CardDynamicValuePayload>();

    public bool playable { get; init; }

    public bool can_play_result { get; init; }

    public string? unplayable_reason { get; init; }

    public string? unplayable_reason_raw { get; init; }

    public string? unplayable_preventer_id { get; init; }

    public string? unplayable_preventer_type { get; init; }
}

internal sealed class CombatEnemyPayload
{
    public int index { get; init; }

    public string enemy_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public int current_hp { get; init; }

    public int max_hp { get; init; }

    public int? base_max_hp { get; init; }

    public int block { get; init; }

    public bool is_alive { get; init; }

    public bool is_hittable { get; init; }

    public CombatPowerPayload[] powers { get; init; } = Array.Empty<CombatPowerPayload>();

    public string? intent { get; init; }

    public string? move_id { get; init; }

    public CombatEnemyIntentPayload[] intents { get; init; } = Array.Empty<CombatEnemyIntentPayload>();
}

internal sealed class CombatEnemyIntentPayload
{
    public int index { get; init; }

    public string intent_type { get; init; } = string.Empty;

    public string? label { get; init; }

    public int? damage { get; init; }

    public int? hits { get; init; }

    public int? total_damage { get; init; }

    public int? status_card_count { get; init; }
}

internal sealed class CombatLethalRiskPayload
{
    public string risk_id { get; init; } = string.Empty;

    public string source { get; init; } = string.Empty;

    public bool will_kill_player { get; init; }

    public string reason { get; init; } = string.Empty;

    public int? incoming_damage { get; init; }

    public int? damage_after_block { get; init; }

    public int? player_hp { get; init; }

    public int? player_block { get; init; }

    public string? power_id { get; init; }

    public int? power_amount { get; init; }
}

internal sealed class CombatPowerPayload
{
    public int index { get; init; }

    public string power_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public int? amount { get; init; }

    public bool is_debuff { get; init; }
}

internal sealed class RewardPayload
{
    public bool pending_card_choice { get; init; }

    public bool can_proceed { get; init; }

    public RewardOptionPayload[] rewards { get; init; } = Array.Empty<RewardOptionPayload>();

    public RewardCardOptionPayload[] card_options { get; init; } = Array.Empty<RewardCardOptionPayload>();

    public RewardAlternativePayload[] alternatives { get; init; } = Array.Empty<RewardAlternativePayload>();
}

internal sealed class ModalPayload
{
    public string type_name { get; init; } = string.Empty;

    public string? underlying_screen { get; init; }

    public bool can_confirm { get; init; }

    public bool can_dismiss { get; init; }

    public string? confirm_label { get; init; }

    public string? dismiss_label { get; init; }
}

internal sealed class UnlockPayload
{
    public string unlock_type { get; init; } = string.Empty;

    public string[] items { get; init; } = Array.Empty<string>();

    public bool can_confirm { get; init; }
}

internal sealed class GameOverPayload
{
    public bool is_victory { get; init; }

    public int? floor { get; init; }

    public string? character_id { get; init; }

    public string phase { get; init; } = "intro";

    public bool can_continue { get; init; }

    public bool can_return_to_main_menu { get; init; }

    public bool showing_summary { get; init; }

    public bool waiting_for_other_players { get; init; }

    public string save_status { get; init; } = "pending";

    public bool save_verified { get; init; }

    public string? save_error { get; init; }
}

internal sealed class RewardOptionPayload
{
    public int index { get; init; }

    public string reward_type { get; init; } = string.Empty;

    public string description { get; init; } = string.Empty;

    public bool claimable { get; init; }
}

internal sealed class RewardCardOptionPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public int energy_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;

    public string resolved_rules_text { get; init; } = string.Empty;

    public CardDynamicValuePayload[] dynamic_values { get; init; } = Array.Empty<CardDynamicValuePayload>();
}

internal sealed class RewardAlternativePayload
{
    public int index { get; init; }

    public string label { get; init; } = string.Empty;
}

internal sealed class BundlePayload
{
    public int index { get; init; }

    public RewardCardOptionPayload[] cards { get; init; } = Array.Empty<RewardCardOptionPayload>();
}

internal sealed class DeckCardPayload
{
    public int index { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;

    public string resolved_rules_text { get; init; } = string.Empty;

    public CardDynamicValuePayload[] dynamic_values { get; init; } = Array.Empty<CardDynamicValuePayload>();
}

internal sealed class SelectionCardPayload
{
    public int index { get; init; }

    public bool selected { get; init; }

    public string card_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public bool upgraded { get; init; }

    public string card_type { get; init; } = string.Empty;

    public string rarity { get; init; } = string.Empty;

    public bool costs_x { get; init; }

    public bool star_costs_x { get; init; }

    public int energy_cost { get; init; }

    public int star_cost { get; init; }

    public string rules_text { get; init; } = string.Empty;

    public string resolved_rules_text { get; init; } = string.Empty;

    public CardDynamicValuePayload[] dynamic_values { get; init; } = Array.Empty<CardDynamicValuePayload>();
}

internal sealed class CardDynamicValuePayload
{
    public string name { get; init; } = string.Empty;

    public int base_value { get; init; }

    public int current_value { get; init; }

    public int enchanted_value { get; init; }

    public bool is_modified { get; init; }

    public bool was_just_upgraded { get; init; }
}

internal sealed class RunRelicPayload
{
    public int index { get; init; }

    public string relic_id { get; init; } = string.Empty;

    public string name { get; init; } = string.Empty;

    public string? description { get; init; }

    public int? stack { get; init; }

    public bool is_melted { get; init; }
}

internal sealed class RunPotionPayload
{
    public int index { get; init; }

    public string? potion_id { get; init; }

    public string? name { get; init; }

    public string? description { get; init; }

    public string? rarity { get; init; }

    public bool occupied { get; init; }

    public string? usage { get; init; }

    public string? target_type { get; init; }

    public bool is_queued { get; init; }

    public bool requires_target { get; init; }

    public string? target_index_space { get; init; }

    public int[] valid_target_indices { get; init; } = Array.Empty<int>();

    public bool can_use { get; init; }

    public bool can_discard { get; init; }
}

internal sealed class ActionDescriptor
{
    public string name { get; init; } = string.Empty;

    public bool requires_target { get; init; }

    public bool requires_index { get; init; }

    public bool requires_coordinates { get; init; }

    public bool requires_tool { get; init; }
}
