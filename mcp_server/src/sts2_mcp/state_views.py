"""Two read-only projections over a `/state` payload: a run summary and a state diff.

Both answer a question an agent asks constantly and had to re-derive itself:

- **`run_summary`** is "where am I in this run" in one call, instead of walking `run.deck`,
  `run.relics`, `run.potions`, and the party block on every decision.
- **`diff_state`** is "what changed" between two snapshots, which is the first thing anyone needs
  when an action's result is not what they expected.

Both are pure functions over the raw `/state` shape, so they are exercised without a game and the
C# native surface can mirror them field for field -- the raw payload's names are documented in
`docs/api.md` and do not move, while the compact `agent_view` renames many of them (see the rename
table there), which is exactly the kind of drift a mirror should not have to track.

Neither function invents a value. A field the payload does not carry is reported as `None`, and a
run that is not present answers `None` rather than an empty summary, so a caller can tell "not in a
run" from "in a run with nothing in it".
"""

from __future__ import annotations

from typing import Any

# A diff of two whole `/state` payloads can be enormous (a combat payload with a full hand, every
# enemy intent, the entire map graph). The cap keeps one call from flooding a model's context; the
# caller reads `truncated` and narrows the comparison instead of silently receiving a partial diff.
MAX_DIFF_ENTRIES = 200

# Depth is capped for the same reason: the map's `nodes[].children[]` nests unbounded in principle,
# and an unbounded walk would turn a debugging tool into a denial of service.
MAX_DIFF_DEPTH = 12


def _as_mapping(value: Any) -> dict[str, Any] | None:
    return value if isinstance(value, dict) else None


def _count(value: Any) -> int:
    return len(value) if isinstance(value, list) else 0


def _occupied_potions(potions: Any) -> int:
    if not isinstance(potions, list):
        return 0
    return sum(1 for potion in potions if isinstance(potion, dict) and potion.get("occupied"))


def run_summary(state: Any) -> dict[str, Any] | None:
    """A one-call summary of the current run, or None when the payload carries no run.

    Field names are the raw `/state` ones, so they can be looked up in `docs/api.md` rather than
    guessed. Counts are counts of what the payload actually holds: a deck of 0 is a real answer for
    a run whose deck payload is empty, and an absent `run` is not.
    """
    state_mapping = _as_mapping(state)
    run = _as_mapping(state_mapping.get("run")) if state_mapping is not None else None
    if run is None:
        return None

    party = run.get("players") if isinstance(run.get("players"), list) else []
    return {
        "character_id": run.get("character_id"),
        "character_name": run.get("character_name"),
        "floor": run.get("floor"),
        "act_id": run.get("act_id"),
        "boss_id": run.get("boss_id"),
        "ascension": run.get("ascension"),
        "current_hp": run.get("current_hp"),
        "max_hp": run.get("max_hp"),
        "gold": run.get("gold"),
        "max_energy": run.get("max_energy"),
        "deck_size": _count(run.get("deck")),
        "relic_count": _count(run.get("relics")),
        "potion_count": _count(run.get("potions")),
        "potions_occupied": _occupied_potions(run.get("potions")),
        "party": [
            {
                "player_id": member.get("player_id"),
                "is_local": member.get("is_local"),
                "is_alive": member.get("is_alive"),
                "current_hp": member.get("current_hp"),
                "max_hp": member.get("max_hp"),
                "gold": member.get("gold"),
            }
            for member in party
            if isinstance(member, dict)
        ],
    }


def _leaf(value: Any) -> tuple[str, Any]:
    """A scalar as (kind, value).

    The kind is part of the comparison on purpose. JSON `"12"` and `12` are different facts about a
    payload, and in Python `True == 1`, so comparing bare values would report "no change" for a
    boolean that turned into a number. The C# mirror tags leaves the same way.
    """
    if value is None:
        return ("null", None)
    if isinstance(value, bool):
        return ("bool", value)
    if isinstance(value, (int, float)):
        return ("number", value)
    return ("string", str(value))


def _flatten(value: Any, path: str, depth: int, out: dict[str, tuple[str, Any]]) -> None:
    if depth > MAX_DIFF_DEPTH:
        out[path] = ("string", "<max depth>")
        return

    if isinstance(value, dict):
        if not value:
            out[path] = ("string", "{}")
            return
        for key in value:
            child = f"{path}.{key}" if path else str(key)
            _flatten(value[key], child, depth + 1, out)
        return

    if isinstance(value, list):
        # Lists are compared by length and by element, so an index that appeared or vanished is a
        # change of its own rather than a reshuffle of every later index.
        out[f"{path}[]"] = ("string", f"len={len(value)}")
        for index, item in enumerate(value):
            _flatten(item, f"{path}[{index}]", depth + 1, out)
        return

    out[path] = _leaf(value)


def _leaves(value: Any) -> dict[str, tuple[str, Any]]:
    flat: dict[str, tuple[str, Any]] = {}
    _flatten(value, "", 0, flat)
    return flat


def diff_state(before: Any, after: Any, *, limit: int = MAX_DIFF_ENTRIES) -> dict[str, Any]:
    """The paths that differ between two `/state` payloads, oldest value first.

    Each entry names the path, the value before, and the value after; a path on one side only is
    reported with the missing side as None. `truncated` says the cap was reached, so an empty
    `changes` list means "no difference" and a truncated one never reads as one.
    """
    cap = max(1, int(limit))
    before_leaves = _leaves(before)
    after_leaves = _leaves(after)

    changes: list[dict[str, Any]] = []
    truncated = False
    paths = sorted(set(before_leaves) | set(after_leaves))
    for path in paths:
        has_before = path in before_leaves
        has_after = path in after_leaves
        if has_before and has_after and before_leaves[path] == after_leaves[path]:
            continue

        changes.append(
            {
                "path": path,
                "before": before_leaves[path][1] if has_before else None,
                "after": after_leaves[path][1] if has_after else None,
            }
        )

        if len(changes) >= cap:
            truncated = len(changes) < len(paths)
            break

    return {
        "changes": changes,
        "change_count": len(changes),
        "truncated": truncated,
        "limit": cap,
    }
