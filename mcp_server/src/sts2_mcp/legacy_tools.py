"""The legacy per-action tool surface: one MCP tool per game action.

These exist for the `full` profile only, where a harness wants tool-by-tool coverage instead of the
single compact `act`. The data lives here rather than in `server.py` because registration is what
that module is for: the 59-entry table was the bulk of its remaining weight, and moving it out let
`server.py` come back under the default module budget instead of raising its own.

`server.py` re-exports both names so callers and tests that already import them from there keep
working; the table is one object either way.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class ActionToolSpec:
    name: str
    kind: str
    description: str


LEGACY_ACTION_TOOLS: tuple[ActionToolSpec, ...] = (
    ActionToolSpec("end_turn", "no_args", "End the player's turn during combat."),
    ActionToolSpec("play_card", "card_target", "Play a card from the current hand."),
    ActionToolSpec("choose_map_node", "option_index", "Travel to a map node."),
    ActionToolSpec(
        "resolve_rewards",
        "reward_choice",
        "Resolve all rewards. Omit option_index to take the first card reward, or pass card_index instead.",
    ),
    ActionToolSpec("collect_rewards_and_proceed", "no_args", "Auto-collect rewards and advance."),
    ActionToolSpec("claim_reward", "option_index", "Claim a single reward item."),
    ActionToolSpec("choose_reward_card", "option_index", "Pick a card from a reward screen."),
    ActionToolSpec("skip_reward_cards", "no_args", "Skip the current card reward."),
    ActionToolSpec("select_deck_card", "option_index", "Select a card on a deck selection screen."),
    ActionToolSpec("confirm_selection", "no_args", "Confirm the current manual card-selection overlay."),
    ActionToolSpec("open_chest", "no_args", "Open the treasure chest in the current room."),
    ActionToolSpec("choose_treasure_relic", "option_index", "Choose a relic from an opened chest."),
    ActionToolSpec("choose_event_option", "option_index", "Choose an option in the current event room."),
    ActionToolSpec("crystal_set_tool", "crystal_tool", "Choose the big or small Crystal Sphere divination tool."),
    ActionToolSpec("crystal_clear_cell", "crystal_cell", "Spend one divination at a Crystal Sphere grid coordinate."),
    ActionToolSpec("choose_capstone_option", "option_index", "Choose an option on the capstone selection screen."),
    ActionToolSpec("choose_bundle", "option_index", "Choose a card bundle on the bundle selection screen."),
    ActionToolSpec("confirm_bundle", "no_args", "Confirm the selected card bundle."),
    ActionToolSpec("choose_rest_option", "option_target", "Choose a rest-site option. Some multiplayer rest options also require target_index."),
    ActionToolSpec("open_shop_inventory", "no_args", "Open the merchant inventory."),
    ActionToolSpec("close_shop_inventory", "no_args", "Close the merchant inventory."),
    ActionToolSpec("buy_card", "option_index", "Buy a card from the open merchant inventory."),
    ActionToolSpec("buy_relic", "option_index", "Buy a relic from the open merchant inventory."),
    ActionToolSpec("buy_potion", "option_index", "Buy a potion from the open merchant inventory."),
    ActionToolSpec("remove_card_at_shop", "no_args", "Use the merchant card-removal service."),
    ActionToolSpec("continue_run", "no_args", "Continue the current run from the main menu."),
    ActionToolSpec("continue_game_over", "no_args", "Confirm the game-over intro and run the native score, unlock, and save summary."),
    ActionToolSpec("abandon_run", "no_args", "Open the abandon-run confirmation from the main menu."),
    ActionToolSpec("save_and_quit", "no_args", "Save the active singleplayer run and return to the main menu."),
    ActionToolSpec("open_character_select", "no_args", "Open the character select screen."),
    ActionToolSpec("open_timeline", "no_args", "Open the timeline screen."),
    ActionToolSpec("close_main_menu_submenu", "no_args", "Close the current main-menu submenu."),
    ActionToolSpec("choose_timeline_epoch", "option_index", "Choose a visible epoch on the timeline screen."),
    ActionToolSpec("confirm_timeline_overlay", "no_args", "Confirm the current timeline inspect or unlock overlay."),
    ActionToolSpec("select_character", "option_index", "Pick a character on the character select screen."),
    ActionToolSpec("embark", "no_args", "Start the run from character select."),
    ActionToolSpec("unready", "no_args", "Cancel local ready status in a multiplayer character-select lobby."),
    ActionToolSpec("increase_ascension", "no_args", "Increase the lobby ascension level when the local player is allowed to change it."),
    ActionToolSpec("decrease_ascension", "no_args", "Decrease the lobby ascension level when the local player is allowed to change it."),
    ActionToolSpec("use_potion", "option_target", "Use a potion from the player's belt."),
    ActionToolSpec("discard_potion", "option_index", "Discard a potion from the player's belt."),
    ActionToolSpec("confirm_modal", "no_args", "Confirm the currently open modal."),
    ActionToolSpec("dismiss_modal", "no_args", "Dismiss or cancel the currently open modal."),
    ActionToolSpec("return_to_main_menu", "no_args", "Leave the game over screen and return to the main menu."),
    ActionToolSpec("proceed", "no_args", "Click the current Proceed or Continue button."),
    ActionToolSpec("switch_profile", "option_index", "Switch the active native profile (1-3)."),
    ActionToolSpec("dismiss_game_over_wait", "no_args", "Stop waiting on the native game-over summary and continue."),
    ActionToolSpec("confirm_unlock", "no_args", "Confirm the unlock reveal screen."),
    ActionToolSpec("close_cards_view", "no_args", "Close the open card list view."),
    ActionToolSpec("host_multiplayer_lobby", "no_args", "Host a local multiplayer lobby."),
    ActionToolSpec("join_multiplayer_lobby", "no_args", "Join the available local multiplayer lobby."),
    ActionToolSpec("ready_multiplayer_lobby", "no_args", "Mark the local player ready in the multiplayer lobby."),
    ActionToolSpec("disconnect_multiplayer_lobby", "no_args", "Leave the multiplayer lobby."),
    ActionToolSpec("invite_ai_teammate", "no_args", "Invite the AI teammate and launch the companion instance."),
    ActionToolSpec("continue_ai_teammate", "no_args", "Continue the saved multiplayer run from the main menu and relaunch the AI teammate to rejoin it."),
)
