"""The per-action methods of :class:`~sts2_mcp.client.Sts2Client`.

Fifty-eight methods, one per action the mod accepts, each forwarding to ``execute_action`` with
the action's name and its own parameters. They are written out rather than generated because the
signature *is* the documentation: ``play_card(card_index, target_index=None)`` says what the
action takes, and a caller, an editor and a test all read it the same way. Generating them would
save 500 lines and cost every one of those readers.

What that repetition does not have to do is share a file with the transport. ``client.py`` owns
the HTTP request, the event stream, error decoding and the reconciliation that follows an
uncertain action -- the parts with decisions in them. Keeping 535 lines of forwarding above that
made the interesting half of the file the half nobody scrolls to.

The mixin expects its host to provide ``execute_action``; :class:`Sts2Client` does.
"""

from __future__ import annotations

from typing import Any


class Sts2ActionMethods:
    """Per-action convenience wrappers around ``execute_action``."""

    def end_turn(self) -> dict[str, Any]:
        return self.execute_action(
            "end_turn",
            client_context={
                "source": "mcp",
                "tool_name": "end_turn",
            },
        )

    def play_card(self, card_index: int, target_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "play_card",
            card_index=card_index,
            target_index=target_index,
            client_context={
                "source": "mcp",
                "tool_name": "play_card",
            },
        )

    def continue_run(self) -> dict[str, Any]:
        return self.execute_action(
            "continue_run",
            client_context={
                "source": "mcp",
                "tool_name": "continue_run",
            },
        )

    def continue_game_over(self) -> dict[str, Any]:
        return self.execute_action(
            "continue_game_over",
            client_context={
                "source": "mcp",
                "tool_name": "continue_game_over",
            },
        )

    def abandon_run(self) -> dict[str, Any]:
        return self.execute_action(
            "abandon_run",
            client_context={
                "source": "mcp",
                "tool_name": "abandon_run",
            },
        )

    def save_and_quit(self) -> dict[str, Any]:
        return self.execute_action(
            "save_and_quit",
            client_context={
                "source": "mcp",
                "tool_name": "save_and_quit",
            },
        )

    def open_character_select(self) -> dict[str, Any]:
        return self.execute_action(
            "open_character_select",
            client_context={
                "source": "mcp",
                "tool_name": "open_character_select",
            },
        )

    def open_timeline(self) -> dict[str, Any]:
        return self.execute_action(
            "open_timeline",
            client_context={
                "source": "mcp",
                "tool_name": "open_timeline",
            },
        )

    def close_main_menu_submenu(self) -> dict[str, Any]:
        return self.execute_action(
            "close_main_menu_submenu",
            client_context={
                "source": "mcp",
                "tool_name": "close_main_menu_submenu",
            },
        )

    def switch_profile(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "switch_profile",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "switch_profile",
            },
        )

    def dismiss_game_over_wait(self) -> dict[str, Any]:
        return self.execute_action(
            "dismiss_game_over_wait",
            client_context={
                "source": "mcp",
                "tool_name": "dismiss_game_over_wait",
            },
        )

    def confirm_unlock(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_unlock",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_unlock",
            },
        )

    def close_cards_view(self) -> dict[str, Any]:
        return self.execute_action(
            "close_cards_view",
            client_context={
                "source": "mcp",
                "tool_name": "close_cards_view",
            },
        )

    def host_multiplayer_lobby(self) -> dict[str, Any]:
        return self.execute_action(
            "host_multiplayer_lobby",
            client_context={
                "source": "mcp",
                "tool_name": "host_multiplayer_lobby",
            },
        )

    def join_multiplayer_lobby(self) -> dict[str, Any]:
        return self.execute_action(
            "join_multiplayer_lobby",
            client_context={
                "source": "mcp",
                "tool_name": "join_multiplayer_lobby",
            },
        )

    def ready_multiplayer_lobby(self) -> dict[str, Any]:
        return self.execute_action(
            "ready_multiplayer_lobby",
            client_context={
                "source": "mcp",
                "tool_name": "ready_multiplayer_lobby",
            },
        )

    def disconnect_multiplayer_lobby(self) -> dict[str, Any]:
        return self.execute_action(
            "disconnect_multiplayer_lobby",
            client_context={
                "source": "mcp",
                "tool_name": "disconnect_multiplayer_lobby",
            },
        )

    def invite_ai_teammate(self) -> dict[str, Any]:
        return self.execute_action(
            "invite_ai_teammate",
            client_context={
                "source": "mcp",
                "tool_name": "invite_ai_teammate",
            },
        )

    def continue_ai_teammate(self) -> dict[str, Any]:
        return self.execute_action(
            "continue_ai_teammate",
            client_context={
                "source": "mcp",
                "tool_name": "continue_ai_teammate",
            },
        )

    def choose_timeline_epoch(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_timeline_epoch",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_timeline_epoch",
            },
        )

    def confirm_timeline_overlay(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_timeline_overlay",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_timeline_overlay",
            },
        )

    def choose_map_node(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_map_node",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_map_node",
            },
        )

    def collect_rewards_and_proceed(self) -> dict[str, Any]:
        return self.execute_action(
            "collect_rewards_and_proceed",
            client_context={
                "source": "mcp",
                "tool_name": "collect_rewards_and_proceed",
            },
        )

    def resolve_rewards(
        self,
        option_index: int | None = None,
        card_index: int | None = None,
    ) -> dict[str, Any]:
        return self.execute_action(
            "resolve_rewards",
            option_index=option_index,
            card_index=card_index,
            client_context={
                "source": "mcp",
                "tool_name": "resolve_rewards",
            },
        )

    def claim_reward(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "claim_reward",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "claim_reward",
            },
        )

    def choose_reward_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_reward_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_reward_card",
            },
        )

    def skip_reward_cards(self) -> dict[str, Any]:
        return self.execute_action(
            "skip_reward_cards",
            client_context={
                "source": "mcp",
                "tool_name": "skip_reward_cards",
            },
        )

    def select_deck_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "select_deck_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "select_deck_card",
            },
        )

    def confirm_selection(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_selection",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_selection",
            },
        )

    def proceed(self) -> dict[str, Any]:
        return self.execute_action(
            "proceed",
            client_context={
                "source": "mcp",
                "tool_name": "proceed",
            },
        )

    def open_chest(self) -> dict[str, Any]:
        return self.execute_action(
            "open_chest",
            client_context={
                "source": "mcp",
                "tool_name": "open_chest",
            },
        )

    def choose_treasure_relic(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_treasure_relic",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_treasure_relic",
            },
        )

    def choose_event_option(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_event_option",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_event_option",
            },
        )

    def crystal_set_tool(self, tool: str) -> dict[str, Any]:
        return self.execute_action(
            "crystal_set_tool",
            tool=tool,
            client_context={
                "source": "mcp",
                "tool_name": "crystal_set_tool",
            },
        )

    def crystal_clear_cell(self, x: int, y: int, tool: str | None = None) -> dict[str, Any]:
        return self.execute_action(
            "crystal_clear_cell",
            x=x,
            y=y,
            tool=tool,
            client_context={
                "source": "mcp",
                "tool_name": "crystal_clear_cell",
            },
        )

    def choose_capstone_option(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_capstone_option",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_capstone_option",
            },
        )

    def choose_bundle(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_bundle",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_bundle",
            },
        )

    def confirm_bundle(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_bundle",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_bundle",
            },
        )

    def choose_rest_option(self, option_index: int, target_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "choose_rest_option",
            option_index=option_index,
            target_index=target_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_rest_option",
            },
        )

    def open_shop_inventory(self) -> dict[str, Any]:
        return self.execute_action(
            "open_shop_inventory",
            client_context={
                "source": "mcp",
                "tool_name": "open_shop_inventory",
            },
        )

    def close_shop_inventory(self) -> dict[str, Any]:
        return self.execute_action(
            "close_shop_inventory",
            client_context={
                "source": "mcp",
                "tool_name": "close_shop_inventory",
            },
        )

    def buy_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "buy_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "buy_card",
            },
        )

    def buy_relic(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "buy_relic",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "buy_relic",
            },
        )

    def buy_potion(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "buy_potion",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "buy_potion",
            },
        )

    def remove_card_at_shop(self) -> dict[str, Any]:
        return self.execute_action(
            "remove_card_at_shop",
            client_context={
                "source": "mcp",
                "tool_name": "remove_card_at_shop",
            },
        )

    def select_character(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "select_character",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "select_character",
            },
        )

    def embark(self) -> dict[str, Any]:
        return self.execute_action(
            "embark",
            client_context={
                "source": "mcp",
                "tool_name": "embark",
            },
        )

    def unready(self) -> dict[str, Any]:
        return self.execute_action(
            "unready",
            client_context={
                "source": "mcp",
                "tool_name": "unready",
            },
        )

    def increase_ascension(self) -> dict[str, Any]:
        return self.execute_action(
            "increase_ascension",
            client_context={
                "source": "mcp",
                "tool_name": "increase_ascension",
            },
        )

    def decrease_ascension(self) -> dict[str, Any]:
        return self.execute_action(
            "decrease_ascension",
            client_context={
                "source": "mcp",
                "tool_name": "decrease_ascension",
            },
        )

    def use_potion(self, option_index: int, target_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "use_potion",
            option_index=option_index,
            target_index=target_index,
            client_context={
                "source": "mcp",
                "tool_name": "use_potion",
            },
        )

    def discard_potion(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "discard_potion",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "discard_potion",
            },
        )

    def run_console_command(self, command: str) -> dict[str, Any]:
        return self.execute_action(
            "run_console_command",
            command=command,
            client_context={
                "source": "mcp",
                "tool_name": "run_console_command",
            },
        )

    def confirm_modal(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_modal",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_modal",
            },
        )

    def dismiss_modal(self) -> dict[str, Any]:
        return self.execute_action(
            "dismiss_modal",
            client_context={
                "source": "mcp",
                "tool_name": "dismiss_modal",
            },
        )

    def return_to_main_menu(self) -> dict[str, Any]:
        return self.execute_action(
            "return_to_main_menu",
            client_context={
                "source": "mcp",
                "tool_name": "return_to_main_menu",
            },
        )
