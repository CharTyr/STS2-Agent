"""Typed models for the two stable read-only HTTP payloads.

Most of what the mod returns is deliberately open-ended: a `/state` snapshot carries a screen-shaped
block that the game owns, a game-data collection is heterogeneous by design, an SSE frame is
whatever the mod published, and an action result may be a recovery payload after a lost response.
Forcing those into classes would either infect every consumer or erase information the client
needs. Two payloads are different: they have a fixed, documented field set the mod itself declares,
and a caller that reads a corrupted one today fails later, somewhere that has no idea which response
was wrong.

This module owns those two. `parse_available_actions` and `parse_decision_log` return frozen
dataclasses carrying both the typed fields and the original wire mapping, so a tool can hand plain
JSON back to a model (`to_wire()`) without the client layer having to keep two shapes in step.

Field policy, stated once here rather than at each call site:

- A field the mod always sends is required and type-checked; a value of the wrong type is a schema
  error, not a value to coerce. A model reading `requests_spent: "3"` is reading a corrupted audit
  fact, and hiding that would make the log untrustworthy in exactly the case it exists for.
- A field that older mod builds may not send is optional with a documented default. The current mod
  always emits all four `requires_*` flags, but a client paired with an older build should see
  `False` rather than an error -- absence is not corruption.
- Unknown fields are retained and re-emitted. The public contract is additive (see
  `docs/openapi.json`), so a newer mod's extra field must survive a round trip instead of being
  silently dropped by the sidecar.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Mapping

# The four descriptor flags an action may require. Spelled out rather than derived from a set
# comprehension so a rename here is visible as a wire-contract change.
_DESCRIPTOR_FLAGS = ("requires_target", "requires_index", "requires_coordinates", "requires_tool")

# Decision-log fields whose absence is allowed. Each is `T | None` on the wire; the mod omits null
# values (`JsonIgnoreCondition.WhenWritingNull`), so "missing" and "null" mean the same thing here.
_OPTIONAL_ENTRY_TEXT_FIELDS = ("reason", "state_fingerprint", "run_id")

# Fields this module owns on a decision entry. Anything else is preserved as an extension.
_KNOWN_ENTRY_FIELDS = frozenset(
    {
        "id",
        "timestamp",
        "source",
        "action",
        "reason",
        "state_fingerprint",
        "requests_spent",
        "total_tokens",
        "run_id",
    }
)


class PayloadSchemaError(ValueError):
    """A response carried the right envelope but the wrong field types inside it.

    `path` names the offending field in dotted form so the message points at one place, and
    `expected`/`actual` keep the two facts a caller needs to report it without re-parsing.
    """

    def __init__(self, path: str, expected: str, actual: Any) -> None:
        self.path = path
        self.expected = expected
        self.actual = actual
        super().__init__(f"{path} must be {expected}, got {type(actual).__name__}")

    def as_details(self) -> dict[str, Any]:
        """The error body a transport-level failure reports, matching the mod's own error shape."""
        return {"path": self.path, "expected": self.expected, "actual_type": type(self.actual).__name__}


def _require_mapping(value: Any, path: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise PayloadSchemaError(path, "an object", value)
    return value


def _require_text(value: Any, path: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise PayloadSchemaError(path, "a non-empty string", value)
    return value


def _require_int(value: Any, path: str) -> int:
    # `bool` is an `int` in Python, and a JSON `true` where a count belongs is a corrupted fact
    # rather than the number 1.
    if isinstance(value, bool) or not isinstance(value, int):
        raise PayloadSchemaError(path, "an integer", value)
    return value


def _optional_text(value: Any, path: str) -> str | None:
    if value is None:
        return None
    if not isinstance(value, str):
        raise PayloadSchemaError(path, "a string or null", value)
    return value


def _optional_int(value: Any, path: str) -> int | None:
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, int):
        raise PayloadSchemaError(path, "an integer or null", value)
    return value


def _extensions(source: Mapping[str, Any], known: frozenset[str]) -> dict[str, Any]:
    return {key: value for key, value in source.items() if key not in known}


@dataclass(frozen=True, slots=True)
class ActionDescriptor:
    """One action the current screen accepts, with the arguments it needs.

    The four `requires_*` flags are the contract `docs/api.md` documents: they say which of
    `option_index` / `target_index` / `x`+`y` / `tool` the action takes, so an agent can build a
    legal request without guessing.
    """

    name: str
    requires_target: bool = False
    requires_index: bool = False
    requires_coordinates: bool = False
    requires_tool: bool = False
    extensions: Mapping[str, Any] = field(default_factory=dict)

    def to_wire(self) -> dict[str, Any]:
        """The descriptor as the mod sent it, typed fields first and extensions preserved."""
        wire: dict[str, Any] = {"name": self.name}
        wire.update({flag: getattr(self, flag) for flag in _DESCRIPTOR_FLAGS})
        wire.update(self.extensions)
        return wire


@dataclass(frozen=True, slots=True)
class AvailableActions:
    """The `/actions/available` payload: which screen it describes and what may be done there."""

    screen: str | None
    actions: tuple[ActionDescriptor, ...]
    extensions: Mapping[str, Any] = field(default_factory=dict)

    def names(self) -> tuple[str, ...]:
        """Just the action names, in the order the mod advertised them."""
        return tuple(action.name for action in self.actions)

    def find(self, name: str) -> ActionDescriptor | None:
        """The descriptor for one action name, or None when this screen does not offer it."""
        for action in self.actions:
            if action.name == name:
                return action
        return None

    def to_wire(self) -> dict[str, Any]:
        wire: dict[str, Any] = {"actions": [action.to_wire() for action in self.actions]}
        if self.screen is not None:
            wire["screen"] = self.screen
        wire.update(self.extensions)
        return wire


@dataclass(frozen=True, slots=True)
class DecisionLogEntry:
    """One accepted decision, as `DecisionLogEntry` declares it in C#.

    `total_tokens` stays `None` when the model never reported usage: the mod distinguishes "spent
    nothing" from "nobody said", and collapsing those here would undo that.
    """

    id: int
    timestamp: str
    source: str
    action: str
    requests_spent: int
    reason: str | None = None
    state_fingerprint: str | None = None
    total_tokens: int | None = None
    run_id: str | None = None
    extensions: Mapping[str, Any] = field(default_factory=dict)

    def to_wire(self) -> dict[str, Any]:
        """The entry as the mod sent it.

        Optional fields are emitted only when set, matching the mod's own
        `WhenWritingNull` serializer: a reader that branches on presence sees the same shape from
        either surface.
        """
        wire: dict[str, Any] = {
            "id": self.id,
            "timestamp": self.timestamp,
            "source": self.source,
            "action": self.action,
            "requests_spent": self.requests_spent,
        }
        for name in _OPTIONAL_ENTRY_TEXT_FIELDS:
            value = getattr(self, name)
            if value is not None:
                wire[name] = value
        if self.total_tokens is not None:
            wire["total_tokens"] = self.total_tokens
        wire.update(self.extensions)
        return wire


def parse_action_descriptor(value: Any, *, path: str = "actions[]") -> ActionDescriptor:
    """Parse one `ActionDescriptor`, defaulting absent flags and refusing mistyped ones."""
    source = _require_mapping(value, path)
    name = _require_text(source.get("name"), f"{path}.name")

    parsed_flags: dict[str, bool] = {}
    for flag in _DESCRIPTOR_FLAGS:
        raw = source.get(flag)
        if raw is None:
            # An older mod may not send a flag at all; absent is not corruption.
            parsed_flags[flag] = False
            continue
        if not isinstance(raw, bool):
            raise PayloadSchemaError(f"{path}.{flag}", "a boolean", raw)
        parsed_flags[flag] = raw

    return ActionDescriptor(
        name=name,
        extensions=_extensions(source, frozenset({"name", *_DESCRIPTOR_FLAGS})),
        **parsed_flags,
    )


def parse_available_actions(value: Any) -> AvailableActions:
    """Parse the `/actions/available` payload.

    Unlike `Sts2Client.get_available_actions`, a malformed `actions` value is an error rather than
    an empty list: `list("nope")` would hand a caller five single-character action names, and a
    model that trusts that list will call an action the game never advertised.
    """
    source = _require_mapping(value, "available_actions")

    screen_raw = source.get("screen")
    screen = None if screen_raw is None else _require_text(screen_raw, "available_actions.screen")

    actions_raw = source.get("actions")
    if not isinstance(actions_raw, list):
        raise PayloadSchemaError("available_actions.actions", "a list", actions_raw)

    actions = tuple(
        parse_action_descriptor(item, path=f"available_actions.actions[{index}]")
        for index, item in enumerate(actions_raw)
    )
    return AvailableActions(
        screen=screen,
        actions=actions,
        extensions=_extensions(source, frozenset({"screen", "actions"})),
    )


def parse_decision_log_entry(value: Any, *, path: str = "decisions[]") -> DecisionLogEntry:
    """Parse one `DecisionLogEntry`."""
    source = _require_mapping(value, path)
    return DecisionLogEntry(
        id=_require_int(source.get("id"), f"{path}.id"),
        timestamp=_require_text(source.get("timestamp"), f"{path}.timestamp"),
        source=_require_text(source.get("source"), f"{path}.source"),
        action=_require_text(source.get("action"), f"{path}.action"),
        requests_spent=_require_int(source.get("requests_spent"), f"{path}.requests_spent"),
        reason=_optional_text(source.get("reason"), f"{path}.reason"),
        state_fingerprint=_optional_text(source.get("state_fingerprint"), f"{path}.state_fingerprint"),
        total_tokens=_optional_int(source.get("total_tokens"), f"{path}.total_tokens"),
        run_id=_optional_text(source.get("run_id"), f"{path}.run_id"),
        extensions=_extensions(source, _KNOWN_ENTRY_FIELDS),
    )


def parse_decision_log(value: Any) -> tuple[DecisionLogEntry, ...]:
    """Parse the `/decisions` payload.

    A null body is an error rather than an empty log: the tool that tolerates a `None` return is
    answering "no decisions were recorded", which is a different claim from "the response was
    unusable", and only one of them is true here.
    """
    if not isinstance(value, list):
        raise PayloadSchemaError("decisions", "a list", value)
    return tuple(
        parse_decision_log_entry(item, path=f"decisions[{index}]") for index, item in enumerate(value)
    )
