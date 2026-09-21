from __future__ import annotations

import os
import threading
import time
from typing import Any, Callable, Literal

from fastmcp import FastMCP

from .client import Sts2ApiError, Sts2Client
from .handoff import Sts2HandoffService
from .knowledge import Sts2KnowledgeBase
from .legacy_tools import (
    # Re-exported: callers and tests import these from here, and the table is one object either way.
    ActionToolSpec,
    LEGACY_ACTION_TOOLS as _LEGACY_ACTION_TOOLS,
)
from .scene_guidance import scene_guidance
from .state_views import MAX_DIFF_ENTRIES, diff_state, run_summary
from .game_data import (
    ITEM_IDS_SEPARATOR,
    GameDataUnavailableError,
    KNOWN_GAME_DATA_COLLECTIONS,
    SCENE_COMBAT,
    SCENE_EVENT,
    SCENE_MENU,
    SCENE_SHOP,
    _SCENE_FIELD_SETS,
    _build_game_data_tool_error,
    _configure_game_data_loader,
    _detect_scene_from_screen,
    _ensure_game_data_index,
    _load_game_data_collection,
    _lookup_game_data_item,
    _reset_game_data_cache,
    derive_relevant_item_ids,
    get_game_data_items_fields,
)

ToolHandler = Callable[..., dict[str, Any]]
CrystalSphereTool = Literal["big", "small"]


PASSIVE_ACTIONS = {"discard_potion", "save_and_quit"}


def _action_name(action: Any) -> str | None:
    if isinstance(action, str):
        return action
    if isinstance(action, dict):
        name = action.get("name")
        return name if isinstance(name, str) else None
    name = getattr(action, "name", None)
    return name if isinstance(name, str) else None


def _action_signature(actions: Any) -> str:
    if not isinstance(actions, list):
        return ""
    return "|".join(sorted(name for action in actions if (name := _action_name(action))))





def _env_flag(name: str, default: bool = False) -> bool:
    value = os.getenv(name, "")
    if not value:
        return default

    return value.strip().lower() in {"1", "true", "yes", "on"}


def _normalize_tool_profile(tool_profile: str | None) -> str:
    value = (tool_profile or os.getenv("STS2_MCP_TOOL_PROFILE") or "guided").strip().lower()
    if value in {"full", "legacy"}:
        return "full"
    if value in {"layered", "planner", "multi-agent"}:
        return "layered"

    return "guided"


def _debug_tools_enabled() -> bool:
    return _env_flag("STS2_ENABLE_DEBUG_ACTIONS")


# Actions that the mod gates behind STS2_ENABLE_DEBUG_ACTIONS. They are deliberately not legacy
# per-action tools: each is registered as its own tool when the flag is set, and the compact `act`
# refuses to forward them so a debug action cannot be reached through the ordinary surface.
_DEBUG_GATED_ACTIONS = {"run_console_command", "inject_event_churn"}


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


def _register_reward_choice_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(option_index: int | None = None, card_index: int | None = None) -> dict[str, Any]:
        return handler(option_index=option_index, card_index=card_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_option_target_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(option_index: int, target_index: int | None = None) -> dict[str, Any]:
        return handler(option_index=option_index, target_index=target_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_crystal_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def action_tool(tool: CrystalSphereTool) -> dict[str, Any]:
        return handler(tool=tool)

    action_tool.__name__ = name
    action_tool.__doc__ = description
    mcp.tool(name=name, description=description)(action_tool)


def _register_crystal_cell_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def action_tool(x: int, y: int, tool: CrystalSphereTool | None = None) -> dict[str, Any]:
        return handler(x=x, y=y, tool=tool)

    action_tool.__name__ = name
    action_tool.__doc__ = description
    mcp.tool(name=name, description=description)(action_tool)


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

        if spec.kind == "reward_choice":
            _register_reward_choice_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "option_target":
            _register_option_target_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "crystal_tool":
            _register_crystal_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "crystal_cell":
            _register_crystal_cell_tool(mcp, spec.name, spec.description, handler)
            continue

        raise RuntimeError(f"Unsupported action tool kind: {spec.kind}")


def create_server(client: Sts2Client | None = None, tool_profile: str | None = None) -> FastMCP:
    sts2 = client or Sts2Client()
    knowledge = Sts2KnowledgeBase()
    handoff = Sts2HandoffService(knowledge)
    profile = _normalize_tool_profile(tool_profile)

    game_data_loader = getattr(sts2, "get_game_data_collection", None)
    if callable(game_data_loader):
        _configure_game_data_loader(game_data_loader)
    else:
        def _missing_game_data_loader(_: str) -> Any:
            raise RuntimeError("Game data loader is not available on this client.")

        _configure_game_data_loader(_missing_game_data_loader)

    _reset_game_data_cache()
    mcp = FastMCP("STS2 AI Agent")

    def _agent_state() -> dict[str, Any]:
        state = sts2.get_state()
        agent_view = state.get("agent_view")
        if isinstance(agent_view, dict):
            if "available_actions" not in agent_view and isinstance(agent_view.get("actions"), list):
                return {
                    **agent_view,
                    "available_actions": agent_view["actions"],
                    "compact_agent_view": True,
                }
            return {**agent_view, "compact_agent_view": True}
        return {**state, "compact_agent_view": False}

    def _state_actions(state: dict[str, Any]) -> list[Any] | None:
        actions = state.get("available_actions")
        if not isinstance(actions, list):
            actions = state.get("actions")
        return actions if isinstance(actions, list) else None

    def _is_actionable_state(state: dict[str, Any]) -> bool:
        actions = _state_actions(state)
        if actions is None:
            return False

        return any(
            name not in PASSIVE_ACTIONS
            for action in actions
            if (name := _action_name(action))
        )

    def _wait_until_actionable_impl(
        timeout_seconds: float,
        *,
        monotonic: Callable[[], float] = time.monotonic,
        sleep: Callable[[float], None] = time.sleep,
    ) -> dict[str, Any]:
        timeout = max(0.1, float(timeout_seconds))
        actionable_events = {
            "player_action_window_opened",
            "route_decision_required",
            "reward_decision_required",
            "available_actions_changed",
            "screen_changed",
        }

        state = sts2.get_state()
        if _is_actionable_state(state):
            return {
                "matched": False,
                "actionable": True,
                "event": None,
                "state": state,
                "actions": sts2.get_available_actions(),
                "timeout_seconds": timeout,
                "source": "state",
                "event_stream_error": None,
            }

        started_at = monotonic()
        event: dict[str, Any] | None = None
        source = "events"
        event_stream_error: dict[str, Any] | None = None

        try:
            event = sts2.wait_for_event(event_names=actionable_events, timeout=timeout)
        except Sts2ApiError as exc:
            # Two ways in: the mod answered and refused the stream, or the stream could not be
            # opened at all (connection refused, DNS, TLS). Only idle read timeouts stay inside
            # the client's wait loop; a transport failure surfaces immediately so this wait can
            # keep its deadline by polling /state. Either way the wait is still useful, so fall
            # back to polling and say so instead of reporting the same "no event yet" as a
            # healthy wait.
            event = None
            source = "polling"
            event_stream_error = {
                "code": exc.code,
                "status_code": exc.status_code,
                "retryable": exc.retryable,
                "message": exc.message,
            }
        except (OSError, TimeoutError) as exc:
            event = None
            source = "polling"
            event_stream_error = {
                "code": "event_stream_unavailable",
                "status_code": 0,
                "retryable": True,
                "message": str(exc),
            }

        remaining = max(0.0, timeout - (monotonic() - started_at))
        state = sts2.get_state()

        if not _is_actionable_state(state) and remaining > 0:
            source = "polling"
            interval = max(0.05, float(os.getenv("STS2_MCP_FALLBACK_POLL_SECONDS", "0.25")))
            deadline = monotonic() + remaining
            baseline_signature = _action_signature(_state_actions(state))

            while monotonic() < deadline:
                sleep(interval)
                state = sts2.get_state()
                if _is_actionable_state(state):
                    break

                signature = _action_signature(_state_actions(state))
                if signature != baseline_signature:
                    break

        return {
            "matched": event is not None,
            "actionable": _is_actionable_state(state),
            "event": event,
            "state": state,
            "actions": sts2.get_available_actions(),
            "timeout_seconds": timeout,
            "source": source,
            "event_stream_error": event_stream_error,
        }

    @mcp.tool
    def health_check() -> dict[str, Any]:
        """Check whether the STS2 AI Agent Mod is loaded and reachable."""
        return sts2.get_health()

    @mcp.tool
    def get_game_state() -> dict[str, Any]:
        """Read the compact agent-facing game state snapshot.

        Two shapes are possible and compact_agent_view tells them apart:

        - compact_agent_view: true - the mod exposed agent_view, so this is that
          compact view (plus available_actions when only actions was sent).
        - compact_agent_view: false - the mod did not expose agent_view and the
          full raw /state payload is returned as a fallback. Treat it as a
          degraded signal rather than the normal compact contract.

        Use get_raw_game_state when you deliberately want the full payload.
        """
        return _agent_state()

    @mcp.tool
    def get_raw_game_state() -> dict[str, Any]:
        """Read the full raw `/state` snapshot for debugging or schema inspection."""
        return sts2.get_state()

    @mcp.tool
    def get_available_actions() -> list[dict[str, Any]]:
        """List currently executable actions with `requires_index` and `requires_target` hints."""
        return sts2.get_available_actions()

    @mcp.tool
    def get_decision_log(limit: int = 50) -> list[dict[str, Any]]:
        """Read recent accepted decisions with the rationale each one carried.

        Entries are ordered oldest first and end at the most recent decision. Use it to
        review why the agent played the way it did, or to diff a run against another.
        """
        return list(sts2.get_decisions(limit=limit) or [])

    @mcp.tool
    def get_run_summary() -> dict[str, Any]:
        """Summarise the current run in one call.

        Character, floor, act, boss, HP, gold, energy, and the deck/relic/potion counts,
        plus the party block in co-op. Reading this is cheaper than walking run.deck,
        run.relics, run.potions and the party array on every decision. `run` is null when
        the payload carries no run.
        """
        return {"run": run_summary(sts2.get_state())}

    @mcp.tool
    def get_scene_guidance() -> dict[str, Any]:
        """Return the strategy guidance for the screen the game is on right now.

        `guidance` is the same text the mod injects into its own play loop: the route rules on
        `MAP`, the rest-site rules on `REST`, the shop rules on `SHOP` and the Fake Merchant, the
        combat and potion priority order on `COMBAT`, and the event-option rules on `EVENT`. It is
        empty on a screen with no strategic choice, which is an answer rather than a failure.

        On `EVENT` this also returns `event_options`: the offline index's per-option handler, cost,
        and risk grade (`lethal-possible`, `harmful`, `costly`, `none-detected`, `locked`,
        `unknown`) for the current `event_id`, in the order the event builds them. The mod does not
        ship that index, so the native MCP surface answers strategy only.
        """
        return scene_guidance(sts2.get_state())

    @mcp.tool
    def diff_state(
        before: dict[str, Any],
        after: dict[str, Any],
        limit: int = MAX_DIFF_ENTRIES,
    ) -> dict[str, Any]:
        """Report the paths that differ between two `/state` payloads.

        Pass the `data` object from two snapshots (not the whole envelope). Each change
        names the path, the value before, and the value after; a path present on one side
        only reports null for the other. `truncated` is true when the cap was reached, so
        an empty `changes` list always means "no difference".
        """
        return diff_state(before, after, limit=limit)

    if profile in {"full", "layered"}:
        @mcp.tool
        def get_planner_context(planner_note: str | None = None) -> dict[str, Any]:
            """Build a planner-focused snapshot with route branches and linked event knowledge."""
            return knowledge.build_planner_context(sts2.get_state(), planner_note=planner_note)

        @mcp.tool
        def create_planner_handoff(
            planning_focus: str | None = None,
            previous_combat_summary: str | None = None,
        ) -> dict[str, Any]:
            """Build a clean planner-agent packet for route, reward, event, and shop decisions."""
            return handoff.create_planner_handoff(
                sts2.get_state(),
                planning_focus=planning_focus,
                previous_combat_summary=previous_combat_summary,
            )

        @mcp.tool
        def get_combat_context(
            planner_note: str | None = None,
            include_knowledge: bool = True,
        ) -> dict[str, Any]:
            """Build a combat-focused snapshot and link it to the canonical combat knowledge entry."""
            return knowledge.build_combat_context(
                sts2.get_state(),
                planner_note=planner_note,
                include_knowledge=include_knowledge,
            )

        @mcp.tool
        def create_combat_handoff(
            planner_message: str | None = None,
            combat_objective: str | None = None,
        ) -> dict[str, Any]:
            """Build a clean combat-agent packet with linked combat knowledge and planner guidance."""
            return handoff.create_combat_handoff(
                sts2.get_state(),
                planner_message=planner_message,
                combat_objective=combat_objective,
            )

        @mcp.tool
        def complete_combat_handoff(
            combat_key: str,
            summary: str,
            planner_message: str | None = None,
            pattern_note: str | None = None,
            trait_note: str | None = None,
            tactical_note: str | None = None,
        ) -> dict[str, Any]:
            """Persist a combat-agent summary and optional enemy-pattern notes, then return a planner-facing brief."""
            return handoff.complete_combat_handoff(
                combat_key=combat_key,
                summary=summary,
                planner_message=planner_message,
                pattern_note=pattern_note,
                trait_note=trait_note,
                tactical_note=tactical_note,
            )

        @mcp.tool
        def append_combat_knowledge(note: str, section: str = "observations") -> dict[str, Any]:
            """Append a note to the active combat knowledge file."""
            return knowledge.append_combat_note(
                sts2.get_state(),
                note=note,
                section=section,
            )

        @mcp.tool
        def append_event_knowledge(
            note: str,
            section: str = "observations",
            option_index: int | None = None,
        ) -> dict[str, Any]:
            """Append a note to the active event knowledge file."""
            return knowledge.append_event_note(
                sts2.get_state(),
                note=note,
                section=section,
                option_index=option_index,
            )

        @mcp.tool
        def complete_event_handoff(
            event_id: str,
            summary: str,
            option_index: int | None = None,
            planning_note: str | None = None,
            outcome_note: str | None = None,
        ) -> dict[str, Any]:
            """Persist an event outcome summary and optional event notes, then return a planner-facing brief."""
            return handoff.complete_event_handoff(
                event_id=event_id,
                summary=summary,
                option_index=option_index,
                planning_note=planning_note,
                outcome_note=outcome_note,
            )

    @mcp.tool
    def get_game_data_item(collection: str, item_id: str) -> dict[str, Any] | None:
        """Return a single item from a game metadata collection by id.

        Example: `get_game_data_item(collection='cards', item_id='ABRASIVE')`
        """
        if not item_id:
            return None

        try:
            index = _ensure_game_data_index(collection)
            return _lookup_game_data_item(index=index, item_id=item_id)
        except (KeyError, RuntimeError, TypeError) as exc:
            return _build_game_data_tool_error(collection=collection, exc=exc)

    @mcp.tool
    def get_game_data_items(collection: str, item_ids: str) -> dict[str, Any]:
        """Return multiple items (by comma-separated ids) from a collection."""
        if not item_ids:
            return {}

        try:
            index = _ensure_game_data_index(collection)
            ids = [s.strip() for s in item_ids.split(ITEM_IDS_SEPARATOR) if s.strip()]
            result: dict[str, Any] = {}
            for i in ids:
                result[i] = _lookup_game_data_item(index=index, item_id=i)
            return result
        except (KeyError, RuntimeError, TypeError) as exc:
            return _build_game_data_tool_error(collection=collection, exc=exc)

    @mcp.tool
    def get_relevant_game_data(collection: str, item_ids: str = "") -> dict[str, Any]:
        """Return items with only the most relevant fields for the current game context.

        This automatically detects the current scene (combat/shop/event/menu) and returns
        only the fields most useful for AI decision-making in that context, minimizing token usage.

        - `collection`: e.g. `cards`, `relics`, `monsters`, `events`
        - `item_ids`: comma-separated ids. Omit to use the ids this screen is about (the cards in
          hand, the enemies in combat, the shop stock), which is the usual call.

        Recommended for most queries to save tokens and reduce uncertainty.
        """
        # Auto-detect current scene from game state
        state = sts2.get_state()
        screen = state.get("screen", "")
        scene = _detect_scene_from_screen(screen)
        resolved_ids = item_ids or ITEM_IDS_SEPARATOR.join(
            derive_relevant_item_ids(state, collection, screen)
        )
        try:
            suggested_fields = _SCENE_FIELD_SETS.get(scene, {}).get(collection)
            if not suggested_fields:
                # Fallback to basic query if no scene-specific fields defined
                return get_game_data_items(collection=collection, item_ids=resolved_ids)

            return get_game_data_items_fields(
                collection=collection,
                item_ids=resolved_ids,
                fields=",".join(suggested_fields),
            )
        except (KeyError, RuntimeError, TypeError) as exc:
            return _build_game_data_tool_error(collection=collection, exc=exc)

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

        Two boolean keys are returned:

        - matched: an SSE event matched. This keeps its original meaning.
        - actionable: the fresh state exposes at least one non-passive action.
          This is the portable key shared with the native server wait_until_actionable,
          so prefer it when you only need to know whether you can act now.
        """
        return _wait_until_actionable_impl(timeout_seconds)

    @mcp.tool
    def act(
        action: str,
        card_index: int | None = None,
        target_index: int | None = None,
        option_index: int | None = None,
        x: int | None = None,
        y: int | None = None,
        tool: CrystalSphereTool | None = None,
        reason: str | None = None,
    ) -> dict[str, Any]:
        """Execute one currently available game action through the compact tool surface.

        Usage loop:
            1. Call `get_game_state()` or `get_available_actions()`.
            2. Branch on `state.session.mode` and `state.session.phase`.
            3. Pick an action that is currently available.
            4. Pass only the indexes or Crystal Sphere coordinates required by
               that action from the latest state.
            5. Attach a one-sentence `reason` so the player and decision log can see why.
            6. Read state again after the action completes.

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
            - Use `target_index` when the latest state gives a card or potion a non-null
              `target` together with a non-empty `targets` list; `rest.options` instead carry the
              explicit `requires_target` / `target_index_space` / `valid_target_indices` triple.
            - Use `x` and `y` for `crystal_clear_cell`; optionally pass
              `tool="big"` or `tool="small"` atomically. Use `tool` alone
              with `crystal_set_tool`.
            - The compact `target` hint says which list `target_index` indexes into: `enemy` means
              `combat.enemies[]`, `player` means the local player list, and `targets` lists the exact
              indices that are legal right now. The full state spells the same thing out as
              `target_index_space` and `valid_target_indices`.
            - `run_console_command` and `inject_event_churn` are intentionally excluded from this
              compact tool; each has its own debug-gated tool.
        """
        normalized = action.strip().lower()
        if normalized in _DEBUG_GATED_ACTIONS:
            raise RuntimeError(
                f"{normalized} is gated separately and must use its own tool when enabled."
            )

        client_context: dict[str, Any] = {
            "source": "mcp",
            "tool_name": "act",
            "tool_profile": profile,
        }
        if reason and reason.strip():
            client_context["decision_reason"] = reason.strip()

        return sts2.execute_action(
            normalized,
            card_index=card_index,
            target_index=target_index,
            option_index=option_index,
            x=x,
            y=y,
            tool=tool,
            client_context=client_context,
        )

    if profile == "full":
        _register_legacy_action_tools(mcp, sts2)

    if _debug_tools_enabled():
        @mcp.tool
        def run_console_command(command: str) -> dict[str, Any]:
            """Run a game dev-console command for local validation or debugging."""
            return sts2.run_console_command(command=command)

        @mcp.tool
        def inject_event_churn(option_index: int = 0) -> dict[str, Any]:
            """Publish synthetic /events/stream events to exercise the slow-subscriber contract.

            Development tool: needs STS2_ENABLE_DEBUG_ACTIONS=1 on the mod. `option_index` is the
            number of events (0 uses the mod's default), and it must exceed the per-subscriber queue
            capacity -- a request that cannot fill a queue proves nothing.
            """
            return sts2.execute_action("inject_event_churn", option_index=option_index)

    return mcp


def main() -> None:
    create_server().run(transport="stdio", show_banner=False)


if __name__ == "__main__":
    main()
