from __future__ import annotations

import os
import threading
import time
from typing import Annotated, Any, Callable

from fastmcp import FastMCP
from pydantic import Field

from .action_results import project_agent_state
from .client import Sts2ApiError, Sts2Client
from .decision import read_decision
from .handoff import Sts2HandoffService
from .knowledge import Sts2KnowledgeBase
from .legacy_tools import (
    # Re-exported: callers and tests import these from here, and the table is one object either way.
    ActionToolSpec,
    CrystalSphereTool,
    LEGACY_ACTION_TOOLS as _LEGACY_ACTION_TOOLS,
    register_legacy_action_tools as _register_legacy_action_tools,
)
from .scene_guidance import scene_guidance
from .state_views import MAX_DIFF_ENTRIES, diff_state as build_state_diff, run_summary
from .game_data import (
    ITEM_IDS_SEPARATOR,
    GameDataUnavailableError,
    KNOWN_GAME_DATA_COLLECTIONS,
    SCENE_COMBAT,
    SCENE_EVENT,
    SCENE_MENU,
    SCENE_SHOP,
    _build_game_data_tool_error,
    _configure_game_data_loader,
    _detect_scene_from_screen,
    _ensure_game_data_index,
    _load_game_data_collection,
    _lookup_game_data_item,
    _reset_game_data_cache,
    derive_relevant_item_ids,
    get_game_data_items_fields,
    scene_field_set,
)

ToolHandler = Callable[..., dict[str, Any]]


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

    def _agent_state(state: dict[str, Any] | None = None, *, raw_state: bool = False) -> dict[str, Any]:
        # One projection for every answer that carries a `/state` payload -- see
        # `action_results.project_agent_state` for what the marker means and when raw comes back.
        return project_agent_state(sts2.get_state() if state is None else state, raw_state=raw_state)

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
        raw_state: bool = False,
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
                "state": _agent_state(state, raw_state=raw_state),
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
            "state": _agent_state(state, raw_state=raw_state),
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

        `compact_agent_view` is true when this is the mod's `agent_view`, and false when the full
        raw payload came back as a degraded fallback instead -- read it before trusting the shape.
        `get_raw_game_state` is the deliberate route to the full payload.
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
    def get_decision_log(limit: int = 50) -> dict[str, Any]:
        """Read recent accepted decisions with the rationale each one carried, oldest first.

        Returns `{"decisions": [...]}`.
        """
        return {"decisions": list(sts2.get_decisions(limit=limit) or [])}

    @mcp.tool
    def get_run_summary() -> dict[str, Any]:
        """Summarise the current run in one call: character, floor, act, boss, HP, gold, and the
        deck/relic/potion counts, plus the party block in co-op. `run` is null when the payload
        carries no run.
        """
        return {"run": run_summary(sts2.get_state())}

    @mcp.tool
    def get_scene_guidance() -> dict[str, Any]:
        """Return the strategy and the playbook for the screen the game is on right now.

        Answers `screen`, `scene`, `guidance`, and `playbook`; `guidance` is empty on a screen with
        no real choice, which is an answer rather than a failure. On `EVENT` it also adds `event_id`
        and `event_options`: the offline index's per-option handler, cost, and risk grade.
        """
        return scene_guidance(sts2.get_state())

    @mcp.tool
    def get_planner_briefing() -> dict[str, Any]:
        """Forward /strategy: live strategy, current run and same-run Jev trend."""
        return sts2.get_strategy()

    @mcp.tool
    def update_play_strategy(
        posture: str | None = None,
        instructions: str | None = None,
        option_hints: dict[str, str] | None = None,
    ) -> dict[str, Any]:
        """Write a new dual-layer play strategy; omitted fields keep their current values."""
        return sts2.update_strategy(
            posture=posture, instructions=instructions, option_hints=option_hints
        )

    @mcp.tool
    def decide() -> dict[str, Any]:
        """Read everything one decision needs in a single call.

        Answers `state` (the compact `agent_view`, the same shape `get_game_state` returns),
        `available_actions` (the descriptors `get_available_actions` returns, with their
        `requires_index` / `requires_target` / target hints), and `scene_guidance` (the same object
        `get_scene_guidance` returns, `playbook` included). The individual read tools stay available.
        """
        return read_decision(sts2)

    @mcp.tool
    def diff_state(
        before: dict[str, Any],
        after: dict[str, Any],
        limit: int = MAX_DIFF_ENTRIES,
    ) -> dict[str, Any]:
        """Report the paths that differ between two `/state` payloads.

        Pass the `data` object from two snapshots, not the whole envelope. Each change names the
        path, the value before, and the value after; a path present on one side only reports null
        for the other. `truncated` is true when changes were omitted or the depth limit stopped a
        full comparison, so only an empty, non-truncated result means "no difference".
        """
        return build_state_diff(before, after, limit=limit)

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
    def get_game_data_item(
        collection: Annotated[
            str,
            Field(description="cards, relics, monsters, potions, events, powers, or characters."),
        ],
        item_id: Annotated[
            str,
            Field(description="Entity id, for example ABRASIVE."),
        ],
    ) -> dict[str, Any] | None:
        """Return a single item from a game metadata collection by id."""
        if not item_id:
            return None

        try:
            index = _ensure_game_data_index(collection)
            return _lookup_game_data_item(index=index, item_id=item_id)
        except (KeyError, RuntimeError, TypeError) as exc:
            return _build_game_data_tool_error(collection=collection, exc=exc)

    @mcp.tool
    def get_game_data_items(
        collection: Annotated[
            str,
            Field(description="cards, relics, monsters, potions, events, powers, or characters."),
        ],
        item_ids: Annotated[
            str,
            Field(description="Comma-separated entity ids."),
        ],
    ) -> dict[str, Any]:
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
    def get_relevant_game_data(
        collection: Annotated[
            str,
            Field(description="cards, relics, monsters, potions, events, powers, or characters."),
        ],
        item_ids: Annotated[
            str,
            Field(
                description=(
                    "Comma-separated ids. Omit to use the ids the current screen is about: the hand "
                    "in a fight, the shop stock, the cards a reward or selection screen offers, the "
                    "relics a chest offers."
                )
            ),
        ] = "",
    ) -> dict[str, Any]:
        """Return items with only the most relevant fields for the current context.

        The scene decides which fields come back: combat, shop, event, reward, card_selection,
        chest, bundle_selection, else menu.
        """
        # Auto-detect current scene from game state
        state = sts2.get_state()
        screen = state.get("screen", "")
        scene = _detect_scene_from_screen(screen)
        resolved_ids = item_ids or ITEM_IDS_SEPARATOR.join(
            derive_relevant_item_ids(state, collection, screen)
        )
        try:
            # Case-insensitive on both names, the way the C# mirror's dictionaries are: the caller
            # spells the collection by hand, so "Cards" has to project exactly like "cards".
            suggested_fields = scene_field_set(scene, collection)
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
    def wait_for_event(
        event_names: Annotated[
            str,
            Field(description="Comma-separated event names; empty accepts any event."),
        ] = "",
        timeout_seconds: Annotated[
            float,
            Field(description="Maximum wait in seconds."),
        ] = 20.0,
    ) -> dict[str, Any]:
        """Wait for one matching game event from `/events/stream`; `matched=false` on timeout."""
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
    def wait_until_actionable(
        timeout_seconds: float = 20.0,
        raw_state: Annotated[
            bool,
            Field(description="Raw /state payload instead of the compact agent_view."),
        ] = False,
    ) -> dict[str, Any]:
        """Wait for a new actionable phase, then return the fresh compact state.

        `state` is the compact shape `get_game_state` returns; `actionable` says you can act now.
        """
        return _wait_until_actionable_impl(timeout_seconds, raw_state=raw_state)

    @mcp.tool
    def act(
        action: Annotated[
            str,
            Field(description="Action name from the latest state's available_actions; never guessed from the screen name."),
        ],
        card_index: Annotated[
            int | None,
            Field(description="Hand card index for play_card."),
        ] = None,
        target_index: Annotated[
            int | None,
            Field(
                description=(
                    "Target index when the latest state gives a card or potion a non-null `target` "
                    "and a non-empty `targets` list. The compact `target` hint names the list this "
                    "indexes into (`enemy` = `combat.enemies[]`, `player` = the local player list); "
                    "the full state spells it `target_index_space` / `valid_target_indices`."
                )
            ),
        ] = None,
        option_index: Annotated[
            int | None,
            Field(
                description=(
                    "Option index for map, reward, shop, event, rest, selection, and "
                    "multiplayer-lobby actions. `rest.options` carry `requires_target` / "
                    "`target_index_space` / `valid_target_indices` themselves."
                )
            ),
        ] = None,
        x: Annotated[
            int | None,
            Field(description="Crystal Sphere grid x-coordinate for crystal_clear_cell."),
        ] = None,
        y: Annotated[
            int | None,
            Field(description="Crystal Sphere grid y-coordinate for crystal_clear_cell."),
        ] = None,
        tool: Annotated[
            CrystalSphereTool | None,
            Field(
                description=(
                    "Crystal Sphere tool. Pass with x/y on crystal_clear_cell to select and clear "
                    "atomically, or alone on crystal_set_tool."
                )
            ),
        ] = None,
        reason: Annotated[
            str | None,
            Field(
                description=(
                    "One-sentence rationale; recorded in the decision log and shown to the player "
                    "as this decision's reason."
                )
            ),
        ] = None,
        raw_state: Annotated[
            bool,
            Field(
                description=(
                    "Return the full raw /state payload instead of the compact agent_view. Default "
                    "false; thousands of tokens, so only for a field the compact view lacks."
                )
            ),
        ] = False,
    ) -> dict[str, Any]:
        """Execute one game action that is currently in `available_actions`.

        `state` in the result is the compact `agent_view` the action left behind: the same shape
        `get_game_state` returns, so read it as the next decision instead of calling again.
        `raw_state=True` returns the full payload. `action`, `status`, `stable`, and `message` are
        unchanged.

        `status: "outcome_unknown"` means the response was lost: one state reconciliation was
        attempted and its compact state sits under `reconciliation.state`, but nothing compared the
        action against it, so never replay the action automatically.

        A rejected index answers with `error.code`, `error.message`, and the `error.details` keys
        `field`, `submitted`, `valid_indices`, and `valid_field` (the payload path to re-read).
        Recompute from those rather than resending the same index.

        `run_console_command` and `inject_event_churn` are excluded; each has its own debug-gated
        tool.
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
            raw_state=raw_state,
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

            Needs STS2_ENABLE_DEBUG_ACTIONS=1 on the mod. `option_index` is the number of events (0
            uses the mod's default) and must exceed the per-subscriber queue capacity.
            """
            return sts2.execute_action("inject_event_churn", option_index=option_index)

    return mcp


def main() -> None:
    create_server().run(transport="stdio", show_banner=False)


if __name__ == "__main__":
    main()
