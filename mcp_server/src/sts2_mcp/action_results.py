"""The shape of an action result, stated once for the Python surface.

Two things about a `POST /action` answer cost a client real tokens or real guesses, and neither was
stated anywhere a client could read it:

- **`state` was the whole raw `/state` payload** -- roughly 4,000-9,500 tokens -- and `act` handed it
  back verbatim on every single step, while the native MCP `act` already answered with the compact
  `agent_view` the mod builds for exactly this purpose. The two surfaces are one tool face, so the
  sidecar now returns the same shape: the action's own post-action snapshot, projected to
  `agent_view` when the mod exposes one. `raw_state=True` on `act` keeps the old answer reachable.
  Every answer that carries a `/state` payload -- `get_game_state`, `act`, `wait_until_actionable`,
  and the reconciled read inside an uncertain action's answer -- goes through the one projection in
  :func:`project_agent_state`, so a client reads the same keys and the same `compact_agent_view`
  marker wherever the payload appears.
- **an index error was a bare message string.** `card_index 9 is not in the latest combat.hand.`
  says what was wrong and nothing about what would have been right, while the "action is not
  available" answer next to it lists `available_actions`. The mod's HTTP details carry a count
  (`hand_count`, `option_count`, ...) for exactly this failure, so the indices it would have accepted
  are derivable; :func:`index_error_details` names them in the same keys the native and in-game
  surfaces use.

`INDEX_PATHS_BY_ACTION` mirrors the path table in `STS2AIAgent/Agent/ActIndexValidator.cs`, and
`tests/test_index_error_alignment.py` compares the two as source, so an action added on one side
cannot quietly answer an index error without naming where the index lives.
"""

from __future__ import annotations

import re
from typing import Any

from .envelope import Sts2ApiError

# The request fields that index into a payload list, in the order an error message would name them.
INDEX_FIELDS: tuple[str, ...] = ("card_index", "option_index", "target_index")

# The compact-state path an action's index points into, mirroring the first path of each
# `ActIndexValidator.OptionPaths` entry. The value is reported as `valid_field`, which is what a
# client re-reads to recompute the index.
INDEX_PATHS_BY_ACTION: dict[str, str] = {
    "play_card": "combat.hand",
    "choose_map_node": "map.options",
    "choose_event_option": "event.options",
    "choose_reward_card": "reward.cards",
    "claim_reward": "reward.rewards",
    "resolve_rewards": "reward.rewards",
    "select_deck_card": "selection.cards",
    "select_character": "character_select.characters",
    "buy_card": "shop.cards",
    "buy_relic": "shop.relics",
    "buy_potion": "shop.potions",
    "choose_rest_option": "rest.options",
    "choose_treasure_relic": "chest.relics",
    "choose_capstone_option": "capstone.options",
    "choose_bundle": "bundles",
    "choose_timeline_epoch": "timeline.slots",
    "use_potion": "run.potions",
    "discard_potion": "run.potions",
}

# The two error codes an index rejection can carry: a required parameter was missing, or the
# submitted one was not in the payload.
INDEX_ERROR_CODES = frozenset({"invalid_request", "invalid_target"})

# The mod reports how many items a list held rather than the indices themselves.
_COUNT_SUFFIX = "_count"

# A count is only ever a small list in this game; the cap keeps a nonsense count from turning an
# error message into a context flood.
MAX_DERIVED_INDICES = 64

# "target_index is out of range for combat.enemies[]." -- the one mod message that already names the
# payload path it read.
_TARGET_SPACE = re.compile(r"\bfor ([a-z_]+(?:\.[a-z_]+)*\[\])")

_MESSAGE_FIELD = re.compile(r"\b(" + "|".join(INDEX_FIELDS) + r")\b")


def project_agent_state(state: Any, *, raw_state: bool = False) -> Any:
    """The one raw-`/state` -> agent-facing projection: the compact view plus its marker.

    `get_game_state`, `act`, `wait_until_actionable`, and the reconciled read inside an uncertain
    action's answer all carry a `/state` payload, and a client that has learned to read one of them
    has to be able to read the other three: the same keys, and the same `compact_agent_view` marker
    saying which projection it got. Three call sites projecting the same payload three ways is how
    `act` ended up handing back the raw 4,000-9,500-token payload while `get_game_state` answered
    compact, and how `wait_until_actionable` did it again afterwards. So it happens here, once.

    `state` comes back unchanged, with no marker, when `raw_state=True` asks for the raw payload:
    that is the deliberate escape hatch, and it must stay the raw payload rather than gain a key the
    raw route does not return. Otherwise the `agent_view` comes back with
    `compact_agent_view: True`; when the mod exposes no `agent_view` the raw payload comes back with
    `compact_agent_view: False`, the degraded fallback `get_game_state` already reports rather than
    a silent empty answer.
    """
    if raw_state or not isinstance(state, dict):
        return state

    agent_view = state.get("agent_view")
    if not isinstance(agent_view, dict) or agent_view is state:
        return {**state, "compact_agent_view": False}

    if "available_actions" not in agent_view and isinstance(agent_view.get("actions"), list):
        # The compact view names its action list `actions`; every reader of this projection looks it
        # up as `available_actions`, so the alias is added here rather than re-derived per caller.
        return {
            **agent_view,
            "available_actions": agent_view["actions"],
            "compact_agent_view": True,
        }

    return {**agent_view, "compact_agent_view": True}


def compact_action_result(result: Any) -> Any:
    """An action response whose embedded state is compact, wherever the response keeps it.

    Mirrors `GameBridge.ActAsync`: the action's own post-action snapshot, only re-projected. Every
    other key (`action`, `status`, `stable`, `message`, and the echo of what was submitted) is
    untouched, and a response that carries no state at all is returned as it is.

    The outcome-unknown path keeps no top-level `state`; the one state it did read sits under
    `reconciliation.state`, and it is projected the same way. Without that, the reliability path
    handed back the very payload the success path had just stopped sending -- and the raw payload
    embeds its own `agent_view`, so a single unprojected copy cost the tokens twice.
    """
    if not isinstance(result, dict):
        return result

    projected = result

    state = result.get("state")
    if isinstance(state, dict):
        projected = {**projected, "state": project_agent_state(state)}

    reconciliation = projected.get("reconciliation")
    if isinstance(reconciliation, dict) and isinstance(reconciliation.get("state"), dict):
        projected = {
            **projected,
            "reconciliation": {
                **reconciliation,
                "state": project_agent_state(reconciliation["state"]),
            },
        }

    return projected


# `action_outcome` carries this value on every reconciliation block: the read that followed a lost
# response cannot tell a completed action from one the mod never ran.
ACTION_OUTCOME_UNKNOWN = "unknown"


def with_reconciliation_semantics(reconciliation: Any) -> Any:
    """The reconciliation block with what it is -- and is not -- stated in explicit keys.

    `succeeded: true` and `status: "succeeded"` describe the `/state` read that followed a lost
    response, not the action: reading state successfully compares nothing, so a client that read
    them as "the action worked" was reading them correctly by their own light. The block now says
    each half out loud, additively, next to the keys that were already there:

    - `state_read`: whether that one read happened (`succeeded` in the terms of the read itself).
    - `action_effect_compared`: always false -- nothing diffed the state against the action.
    - `action_outcome`: always `"unknown"` -- the top-level `status` stays `outcome_unknown`.
    - `required`: true, because an unknown outcome always needs a decision; the failed-read branch
      already said so and the successful-read branch used to say the opposite.

    Nothing is removed or renamed, so a reader of the old keys is unaffected.
    """
    if not isinstance(reconciliation, dict):
        return reconciliation

    return {
        **reconciliation,
        "required": True,
        "state_read": reconciliation.get("succeeded") is True,
        "action_effect_compared": False,
        "action_outcome": ACTION_OUTCOME_UNKNOWN,
    }


def index_error_details(exc: Sts2ApiError, *, available_actions: list[str] | None = None) -> dict[str, Any] | None:
    """The structured detail block for an index rejection, or None when this is a different failure.

    The keys are the ones the native surface answers with -- `field`, `submitted`, `valid_indices`,
    `valid_field` -- so a client parses one shape whichever surface served it. A key is omitted when
    the mod's answer does not carry it: an empty `valid_indices` would read as "no index is legal",
    which is a different claim from "unknown".
    """
    if exc.code not in INDEX_ERROR_CODES:
        return None

    raw = exc.details if isinstance(exc.details, dict) else {}
    field = _index_field(exc.message, raw)
    if field is None:
        return None

    action = raw.get("action")
    details: dict[str, Any] = {
        "action": action,
        "field": field,
        "submitted": raw.get(field),
    }

    valid_field = _valid_field(exc.message, raw, action)
    if valid_field:
        details["valid_field"] = valid_field

    valid_indices = _valid_indices(raw)
    if valid_indices is not None:
        details["valid_indices"] = valid_indices

    if available_actions is not None:
        details["available_actions"] = available_actions

    return details


def with_index_details(exc: Sts2ApiError) -> Sts2ApiError:
    """The same failure with the structured index details merged in, or the failure unchanged.

    The human-readable message is not touched: `str(error)` still starts with the mod's own sentence,
    and the details it already carried (`hand_count`, `option_count`, ...) stay in place next to the
    derived ones.
    """
    details = index_error_details(exc)
    if details is None:
        return exc

    existing = exc.details if isinstance(exc.details, dict) else {}
    return Sts2ApiError(
        status_code=exc.status_code,
        code=exc.code,
        message=exc.message,
        details={**existing, **details},
        retryable=exc.retryable,
    )


def _index_field(message: str, raw: dict[str, Any]) -> str | None:
    """Which request field the failure is about.

    The message wins because it is the mod's own statement of what it rejected
    ("card_index is out of range."); the details are the fallback for a message that does not name
    one. Both are read from the same error, so neither can invent a field the mod never mentioned
    unless the payload itself carried it.
    """
    match = _MESSAGE_FIELD.search(message or "")
    if match:
        return match.group(1)

    for name in INDEX_FIELDS:
        if name in raw:
            return name
    return None


def _valid_field(message: str, raw: dict[str, Any], action: Any) -> str | None:
    if isinstance(raw.get("valid_field"), str) and raw["valid_field"]:
        return raw["valid_field"]

    match = _TARGET_SPACE.search(message or "")
    if match:
        return match.group(1)

    return INDEX_PATHS_BY_ACTION.get(action) if isinstance(action, str) else None


def _valid_indices(raw: dict[str, Any]) -> list[int] | None:
    """The indices the mod would have accepted, or None when its answer does not say.

    An explicit `valid_indices` list is passed through; otherwise the count the mod reports for the
    list it rejected (`hand_count`, `option_count`, `node_count`, ...) is the same fact in a shorter
    form, and every list a `*_index` addresses here is zero-based and contiguous.
    """
    explicit = raw.get("valid_indices")
    if isinstance(explicit, list) and all(
        isinstance(index, int) and not isinstance(index, bool) for index in explicit
    ):
        return list(explicit)

    for key, value in raw.items():
        if key.endswith(_COUNT_SUFFIX) and isinstance(value, int) and not isinstance(value, bool):
            return list(range(max(0, min(value, MAX_DERIVED_INDICES))))
    return None
