from __future__ import annotations

import os
import time
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

    def _is_actionable_state(state: dict[str, Any]) -> bool:
        actions = state.get("available_actions")
        return isinstance(actions, list) and len(actions) > 0

    @mcp.tool
    def health_check() -> dict[str, Any]:
        """Check whether the STS2 AI Agent Mod is loaded and reachable."""
        return sts2.get_health()

    @mcp.tool
    def get_game_state() -> dict[str, Any]:
        """Read a full snapshot of the current game state.

        Call this before making decisions. The payload includes the current
        screen, normalized `session` metadata, available action names, combat
        entities, reward state, map options, shop state, and run metadata.

        The top-level `session` payload is the AI-facing branch point:
            - `session.mode`: `singleplayer` or `multiplayer`
            - `session.phase`: `menu`, `character_select`, `multiplayer_lobby`, or `run`
            - `session.control_scope`: always `local_player`

        Use `session` plus `available_actions` to decide what to do next. Do
        not infer multiplayer control from tool names.

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
    def wait_for_event(event_names: str = "", timeout_seconds: float = 20.0) -> dict[str, Any]:
        """Wait for one matching game event from `/events/stream`.

        - `event_names`: comma-separated event names. Empty means accept any event.
        - `timeout_seconds`: maximum wait time before returning `matched=false`.
        """
        timeout = max(0.1, float(timeout_seconds))
        target_names = [name.strip() for name in event_names.split(",") if name.strip()]
        event = sts2.wait_for_event(
            event_names=target_names or None,
            timeout=timeout,
        )
        if event is None:
            return {
                "matched": False,
                "event": None,
                "event_names": target_names,
                "timeout_seconds": timeout,
            }

        return {
            "matched": True,
            "event": event,
            "event_names": target_names,
            "timeout_seconds": timeout,
        }

    @mcp.tool
    def wait_until_actionable(timeout_seconds: float = 20.0) -> dict[str, Any]:
        """Wait until a new actionable phase is reported, then return fresh state.

        This reduces high-frequency polling between enemy turns, map transitions,
        and reward animations. Falls back to basic polling when SSE events are
        unavailable or no matching event arrives in time.
        """
        timeout = max(0.1, float(timeout_seconds))
        actionable_events = {
            "player_action_window_opened",
            "route_decision_required",
            "reward_decision_required",
            "available_actions_changed",
            "screen_changed",
        }

        started_at = time.monotonic()
        event: dict[str, Any] | None = None
        source = "events"

        try:
            event = sts2.wait_for_event(event_names=actionable_events, timeout=timeout)
        except Exception:
            event = None
            source = "polling"

        remaining = max(0.0, timeout - (time.monotonic() - started_at))
        state = sts2.get_state()

        if event is None and not _is_actionable_state(state) and remaining > 0:
            source = "polling"
            interval = max(0.05, float(os.getenv("STS2_MCP_FALLBACK_POLL_SECONDS", "0.25")))
            deadline = time.monotonic() + remaining
            baseline_signature = "|".join(sorted(str(name) for name in (state.get("available_actions") or [])))

            while time.monotonic() < deadline:
                time.sleep(interval)
                state = sts2.get_state()
                if _is_actionable_state(state):
                    break

                signature = "|".join(sorted(str(name) for name in (state.get("available_actions") or [])))
                if signature != baseline_signature:
                    break

        actions = sts2.get_available_actions()
        return {
            "matched": event is not None,
            "event": event,
            "state": state,
            "actions": actions,
            "timeout_seconds": timeout,
            "source": source,
        }

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
            2. Branch on `state.session.mode` and `state.session.phase`.
            3. Pick an action that is currently available.
            4. Pass only the indices required by that action from the latest state.
            5. Read state again after the action completes.

        Compact-tool rules:
            - Guided mode intentionally keeps the tool surface small: use this
              single `act` tool for both singleplayer and multiplayer actions.
            - Multiplayer never changes the control scope; you only control the
              local player exposed by the latest state.
            - Never guess actions from screen names alone. Only call names that
              are present in `state.available_actions`.

        Notes:
            - Use `card_index` for `play_card`.
            - Use `option_index` for map, reward, shop, event, rest, selection,
              and multiplayer-lobby actions.
            - Use `target_index` only when the latest state marks a card or potion as `requires_target=true`.
            - Read `target_index_space` and `valid_target_indices` from state to know whether `target_index`
              refers to `combat.enemies[]` or `combat.players[]`.
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
