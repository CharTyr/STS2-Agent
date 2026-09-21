"""One decision's three answers -- state, actions, guidance -- from one frame.

The documented loop reads `GET /state` and then `GET /actions/available`. Two requests are two
frames: the game advances between them, and a screen that changed mid-decision leaves the action
descriptors the caller is about to index into describing a state the payload it read never showed.
`GET /decision-snapshot` answers both halves from one state build, and this module is the one place
the sidecar turns that into the `decide` tool's result.

An older mod has no such route. It answers `404 not_found`, and the two reads it *does* have are
still individually correct, so that one case falls back to them. The fallback is deliberately
narrow: any other failure is a real failure and is raised, because a second read would turn a broken
mod into a slower answer instead of an error.
"""

from __future__ import annotations

from typing import Any

from .action_results import project_agent_state
from .client import Sts2ApiError
from .scene_guidance import scene_guidance


def read_decision(sts2: Any) -> dict[str, Any]:
    """The three answers `decide` returns, from one read of the mod.

    The public keys are `state`, `available_actions`, and `scene_guidance`, and each is the object
    the matching single-purpose tool returns, so a caller can keep one shape whichever it uses.
    """
    try:
        snapshot = sts2.get_decision_snapshot()
    except Sts2ApiError as exc:
        if (exc.status_code, exc.code) != (404, "not_found"):
            raise
        # A mod that predates the route. Keep the tool usable rather than failing a caller that only
        # wanted to play; this is the one degraded path, and it is the one that costs two reads.
        raw = sts2.get_state()
        return {
            "state": project_agent_state(raw),
            "available_actions": sts2.get_available_actions(),
            "scene_guidance": scene_guidance(raw),
        }

    state = snapshot.get("state")
    state = state if isinstance(state, dict) else {}
    return {
        "state": _snapshot_state(state),
        "available_actions": list(snapshot.get("available_actions") or []),
        "scene_guidance": scene_guidance(_guidance_view(state)),
    }


def _snapshot_state(state: dict[str, Any]) -> dict[str, Any]:
    """`/decision-snapshot`'s `state` in the shape `get_game_state` answers with.

    The route sends the mod's `agent_view`, which is already the compact shape, so the marker is
    true. A build that produced no `agent_view` falls back to the raw payload -- detected by the key
    still being there -- and goes through the one projection every other state answer uses rather
    than being told a shape it did not come back in.
    """
    if "agent_view" in state:
        projected = project_agent_state(state)
        return projected if isinstance(projected, dict) else {}
    return {**state, "compact_agent_view": True}


def _guidance_view(state: dict[str, Any]) -> dict[str, Any]:
    """The compact state with the one key `scene_guidance` reads under its raw-payload name.

    `scene_guidance` is written against `/state`, where an event's identifier is
    `event.event_id`; the compact view renames it to `event.id` (see the rename table in
    `docs/api.md`). Only the event id is renamed back, and only for the guidance call: it is what
    the sidecar's per-option event risk index joins on, so without it a decision taken on an event
    screen would report no options where `get_scene_guidance` reports them.
    """
    event = state.get("event")
    if not isinstance(event, dict) or "event_id" in event:
        return state
    event_id = event.get("id")
    if not isinstance(event_id, str):
        return state
    return {**state, "event": {**event, "event_id": event_id}}
