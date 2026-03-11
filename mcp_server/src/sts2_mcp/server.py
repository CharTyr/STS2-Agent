from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Any, Callable

from fastmcp import FastMCP

from .client import Sts2Client

ToolHandler = Callable[..., dict[str, Any]]


@dataclass(frozen=True, slots=True)
class ActionToolSpec:
    name: str
    kind: str
    description: str


_LEGACY_ACTION_TOOLS: tuple[ActionToolSpec, ...] = (
    ActionToolSpec("end_turn", "no_args", "End the player's turn during combat."),
    ActionToolSpec("play_card", "card_target", "Play a card from the current hand."),
    ActionToolSpec("choose_map_node", "option_index", "Travel to a map node."),
    ActionToolSpec("collect_rewards_and_proceed", "no_args", "Auto-collect rewards and advance."),
    ActionToolSpec("claim_reward", "option_index", "Claim a single reward item."),
    ActionToolSpec("choose_reward_card", "option_index", "Pick a card from a reward screen."),
    ActionToolSpec("skip_reward_cards", "no_args", "Skip the current card reward."),
    ActionToolSpec("select_deck_card", "option_index", "Select a card on a deck selection screen."),
    ActionToolSpec("confirm_selection", "no_args", "Confirm the current manual card-selection overlay."),
    ActionToolSpec("open_chest", "no_args", "Open the treasure chest in the current room."),
    ActionToolSpec("choose_treasure_relic", "option_index", "Choose a relic from an opened chest."),
    ActionToolSpec("choose_event_option", "option_index", "Choose an option in the current event room."),
    ActionToolSpec("choose_rest_option", "option_index", "Choose a rest-site option."),
    ActionToolSpec("open_shop_inventory", "no_args", "Open the merchant inventory."),
    ActionToolSpec("close_shop_inventory", "no_args", "Close the merchant inventory."),
    ActionToolSpec("buy_card", "option_index", "Buy a card from the open merchant inventory."),
    ActionToolSpec("buy_relic", "option_index", "Buy a relic from the open merchant inventory."),
    ActionToolSpec("buy_potion", "option_index", "Buy a potion from the open merchant inventory."),
    ActionToolSpec("remove_card_at_shop", "no_args", "Use the merchant card-removal service."),
    ActionToolSpec("continue_run", "no_args", "Continue the current run from the main menu."),
    ActionToolSpec("abandon_run", "no_args", "Open the abandon-run confirmation from the main menu."),
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
)


def _env_flag(name: str, default: bool = False) -> bool:
    value = os.getenv(name, "")
    if not value:
        return default

    return value.strip().lower() in {"1", "true", "yes", "on"}


def _normalize_tool_profile(tool_profile: str | None) -> str:
    value = (tool_profile or os.getenv("STS2_MCP_TOOL_PROFILE") or "guided").strip().lower()
    if value in {"full", "legacy"}:
        return "full"

    return "guided"


def _debug_tools_enabled() -> bool:
    return _env_flag("STS2_ENABLE_DEBUG_ACTIONS")


def _register_no_arg_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool() -> dict[str, Any]:
        return handler()

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_option_index_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(option_index: int) -> dict[str, Any]:
        return handler(option_index=option_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_card_target_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(card_index: int, target_index: int | None = None) -> dict[str, Any]:
        return handler(card_index=card_index, target_index=target_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_option_target_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(option_index: int, target_index: int | None = None) -> dict[str, Any]:
        return handler(option_index=option_index, target_index=target_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_legacy_action_tools(mcp: FastMCP, sts2: Sts2Client) -> None:
    for spec in _LEGACY_ACTION_TOOLS:
        handler = getattr(sts2, spec.name)
        if spec.kind == "no_args":
            _register_no_arg_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "option_index":
            _register_option_index_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "card_target":
            _register_card_target_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "option_target":
            _register_option_target_tool(mcp, spec.name, spec.description, handler)
            continue

        raise RuntimeError(f"Unsupported action tool kind: {spec.kind}")


def create_server(client: Sts2Client | None = None, tool_profile: str | None = None) -> FastMCP:
    sts2 = client or Sts2Client()
    profile = _normalize_tool_profile(tool_profile)
    mcp = FastMCP("STS2 AI Agent")

    @mcp.tool
    def health_check() -> dict[str, Any]:
        """Check whether the STS2 AI Agent Mod is loaded and reachable."""
        return sts2.get_health()

    @mcp.tool
    def get_game_state() -> dict[str, Any]:
        """Read a full snapshot of the current game state.

        Call this before making decisions. The payload includes the current
        screen, available action names, combat entities, reward state, map
        options, shop state, and run metadata.

        Defect-specific combat data is exposed through `combat.player`, which
        now includes `focus`, `base_orb_slots`, `orb_capacity`,
        `empty_orb_slots`, and `orbs[]`.
        """
        return sts2.get_state()

    @mcp.tool
    def get_available_actions() -> list[dict[str, Any]]:
        """List currently executable actions with `requires_index` and `requires_target` hints."""
        return sts2.get_available_actions()

    @mcp.tool
    def act(
        action: str,
        card_index: int | None = None,
        target_index: int | None = None,
        option_index: int | None = None,
    ) -> dict[str, Any]:
        """Execute one currently available game action through the compact tool surface.

        Usage loop:
            1. Call `get_game_state()` or `get_available_actions()`.
            2. Pick an action that is currently available.
            3. Pass only the indices required by that action from the latest state.
            4. Read state again after the action completes.

        Notes:
            - Use `card_index` for `play_card`.
            - Use `option_index` for map, reward, shop, event, rest, and selection actions.
            - Use `target_index` only for enemy-targeted cards or potions.
            - `run_console_command` is intentionally excluded from this compact tool.
        """
        normalized = action.strip().lower()
        if normalized == "run_console_command":
            raise RuntimeError("run_console_command is gated separately and must use its own tool when enabled.")

        return sts2.execute_action(
            normalized,
            card_index=card_index,
            target_index=target_index,
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "act",
                "tool_profile": profile,
            },
        )

    if profile == "full":
        _register_legacy_action_tools(mcp, sts2)

    if _debug_tools_enabled():
        @mcp.tool
        def run_console_command(command: str) -> dict[str, Any]:
            """Run a game dev-console command for local validation or debugging."""
            return sts2.run_console_command(command=command)

    return mcp


def main() -> None:
    create_server().run(transport="stdio", show_banner=False)


if __name__ == "__main__":
    main()
