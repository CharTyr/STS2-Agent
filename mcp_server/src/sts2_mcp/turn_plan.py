"""Whole-turn plan rules, ported from ``STS2AIAgent/Agent/TurnPlanPolicy.cs``.

Same rules as the C# class: parse one model request into a whole combat turn (a
first action plus up to ``MAX_THEN_STEPS`` follow-ups), re-resolve each step
against fresh state, and report when the frame moved away from the plan.
Unreadable state counts as changed. Cards are named by ``card_cid`` because hand
positions shift. Both copies are pinned by the shared vectors in
``mcp_server/tests/fixtures/turn_plan_vectors.json``.
"""

import json
from dataclasses import dataclass

STOP_COMPLETED = "completed"
STOP_CARD_GONE = "card_gone"
STOP_CARD_UNPLAYABLE = "card_unplayable"
STOP_TARGET_GONE = "target_gone"
STOP_ILLEGAL_ACTION = "illegal_action"
STOP_SCREEN_CHANGED = "screen_changed"
STOP_TURN_CHANGED = "turn_changed"
STOP_NEW_CARDS = "new_cards"
STOP_TOOK_DAMAGE = "took_damage"
STOP_PENDING_CHOICE = "pending_choice"
STOP_UNSETTLED = "unsettled"
STOP_REJECTED = "rejected"
STOP_OUTCOME_UNKNOWN = "outcome_unknown"
STOP_CANCELED = "canceled"
STOP_PLAYER_INSTRUCTION = "player_instruction"
STOP_DISABLED = "disabled"

MAX_THEN_STEPS = 9

_UINT32_MASK, _UINT32_LIMIT = (1 << 32) - 1, 1 << 32
_INT32_MIN, _INT32_MAX = -(1 << 31), (1 << 31) - 1
_INT64_MIN, _INT64_MAX = -(1 << 63), (1 << 63) - 1
_STEP_KEYS = frozenset(
    ("action", "card_cid", "card_index", "target_index", "option_index", "reason")
)


@dataclass(frozen=True)
class PlanStep:
    """One action of a turn, its card named by its per-combat id."""

    action: str
    card_cid: int | None
    card_index: int | None
    target_index: int | None
    option_index: int | None
    reason: str | None


@dataclass(frozen=True)
class TurnPlan:
    """The first action from the act's own fields plus the ``then`` follow-ups."""

    first: PlanStep | None
    then: list[PlanStep]
    warnings: list[str]


@dataclass(frozen=True)
class StepResolution:
    """Concrete act arguments for one step, or the reason it cannot run."""

    action: str
    card_index: int | None
    target_index: int | None
    option_index: int | None
    stop_reason: str | None

    @property
    def ok(self) -> bool:
        """True when the step can be executed as resolved."""
        return self.stop_reason is None


def parse(arguments: str | dict | None) -> TurnPlan:
    """Parse a whole-turn request; unreadable arguments are an empty plan plus a warning."""
    warnings: list[str] = []
    try:
        root = _load(arguments)
    except (ValueError, TypeError):
        root = None
    if not isinstance(root, dict):
        return TurnPlan(None, [], ["arguments: not a JSON object"])

    first = _read_step(root, "", warnings, allow_nested=True)
    then: list[PlanStep] = []
    if "then" in root:
        listing = root["then"]
        if not isinstance(listing, list):
            warnings.append("then: not an array")
        else:
            for k, item in enumerate(listing):
                prefix = f"then[{k}]: "
                if not isinstance(item, dict):
                    warnings.append(prefix + "missing action")
                elif len(then) < MAX_THEN_STEPS:
                    step = _read_step(item, prefix, warnings, allow_nested=False)
                    if step is None:
                        warnings.append(prefix + "missing action")
                    else:
                        then.append(step)
            if len(listing) > MAX_THEN_STEPS and len(then) == MAX_THEN_STEPS:
                warnings.append(f"then: truncated to {MAX_THEN_STEPS} steps")
    return TurnPlan(first, then, warnings)


def resolve(
    step: PlanStep, state: dict | str | None, require_cid: bool = False
) -> StepResolution:
    """Resolve one step against fresh compact state.

    ``require_cid`` refuses a ``play_card`` that names its card only by position.
    """
    action = step.action
    try:
        root = _load(state)
    except (ValueError, TypeError):
        root = None
    if not isinstance(root, dict):
        return _stop(action, STOP_ILLEGAL_ACTION)

    names = root.get("available_actions")
    listed = isinstance(names, list) and any(
        isinstance(name, str) and name.casefold() == action.casefold() for name in names
    )
    if not listed:
        return _stop(action, STOP_ILLEGAL_ACTION)
    if action.casefold() != "play_card":
        return StepResolution(action, step.card_index, step.target_index, step.option_index, None)
    try:
        return _play_card(step, root, require_cid)
    except (TypeError, KeyError, AttributeError):
        return _stop(action, STOP_ILLEGAL_ACTION)


def _play_card(step: PlanStep, root: dict, require_cid: bool) -> StepResolution:
    action = step.action
    hand = _hand(root)
    if step.card_cid is not None:
        card = next((c for c in hand if _read_uint(c, "cid") == step.card_cid), None)
    elif require_cid:
        return _stop(action, STOP_ILLEGAL_ACTION)
    elif step.card_index is not None:
        card = next((c for c in hand if _read_int(c, "i") == step.card_index), None)
    else:
        return _stop(action, STOP_ILLEGAL_ACTION)

    index = None if card is None else _read_int(card, "i")
    if index is None:
        return _stop(action, STOP_CARD_GONE)
    if _read_bool(card, "playable") is not True:
        return _stop(action, STOP_CARD_UNPLAYABLE)

    raw_targets = card.get("targets")
    targets = [
        target
        for target in raw_targets
        if not isinstance(target, bool)
        and isinstance(target, int)
        and _INT32_MIN <= target <= _INT32_MAX
    ] if isinstance(raw_targets, list) else []
    if not targets:
        return StepResolution(action, index, None, None, None)
    standing = [target for target in targets if _enemy_standing(root, target)]
    if step.target_index is not None:
        if step.target_index in standing:
            return StepResolution(action, index, step.target_index, None, None)
        return _stop(action, STOP_TARGET_GONE)
    if len(standing) == 1:
        return StepResolution(action, index, standing[0], None, None)
    return _stop(action, STOP_TARGET_GONE)


def diverged(plan_time: dict | str | None, current: dict | str | None) -> str | None:
    """The first way the current frame left the plan's situation, or None."""
    try:
        plan, now = _load(plan_time), _load(current)
    except (ValueError, TypeError):
        return STOP_SCREEN_CHANGED
    if not isinstance(plan, dict) or not isinstance(now, dict):
        return STOP_SCREEN_CHANGED

    screen = _read_string(now, "screen")
    combat = now.get("combat")
    readiness = combat.get("action_readiness") if isinstance(combat, dict) else None
    choosing = isinstance(readiness, dict) and (
        _read_bool(readiness, "hand_in_card_selection") is True
    )
    if (isinstance(now.get("selection"), dict) or isinstance(now.get("modal"), dict)
            or choosing or screen in ("CARD_SELECTION", "MODAL")):
        return STOP_PENDING_CHOICE
    if screen != _read_string(plan, "screen"):
        return STOP_SCREEN_CHANGED

    plan_turn, current_turn = _read_int(plan, "turn"), _read_int(now, "turn")
    if plan_turn is not None and current_turn is not None and plan_turn != current_turn:
        return STOP_TURN_CHANGED

    plan_cids = {c for c in (_read_uint(card, "cid") for card in _hand(plan)) if c is not None}
    if plan_cids and any(
        (c := _read_uint(card, "cid")) is not None and c not in plan_cids for card in _hand(now)
    ):
        return STOP_NEW_CARDS

    before, after = _player_hp(plan), _player_hp(now)
    if before is not None and after is not None and after < before:
        return STOP_TOOK_DAMAGE
    return None


def find_card_index(state: dict | str | None, cid: int) -> int | None:
    """The hand position of the card with this per-combat id, or None."""
    try:
        root = _load(state)
    except (ValueError, TypeError):
        return None
    if isinstance(root, dict) and isinstance(root.get("state"), dict):
        root = root["state"]
    if not isinstance(root, dict):
        return None
    for card in _hand(root):
        if _read_uint(card, "cid") == cid:
            return _read_int(card, "i")
    return None


def _read_step(
    element: dict, prefix: str, warnings: list[str], allow_nested: bool
) -> PlanStep | None:
    """One parsed step, or None when the object names no action."""
    raw_action = _read_string(element, "action")
    action = raw_action.strip().lower() if raw_action is not None else None
    if not allow_nested:
        for name in element:
            if not isinstance(name, str):
                continue
            if name == "then":
                warnings.append(prefix + "nested then ignored")
            elif name not in _STEP_KEYS:
                warnings.append(prefix + "unknown key " + name + " ignored")
    if not action:
        return None

    card_cid: int | None = None
    raw_cid = _read_lenient_int(element, "card_cid")
    if raw_cid is not None:
        if raw_cid < 0:
            warnings.append(prefix + "card_cid must be >= 0")
        else:
            card_cid = raw_cid & _UINT32_MASK  # C#: unchecked long -> uint
    return PlanStep(action, card_cid, _int_field(element, "card_index"),
                    _int_field(element, "target_index"), _int_field(element, "option_index"),
                    _read_string(element, "reason"))


def _stop(action: str, reason: str) -> StepResolution:
    return StepResolution(action, None, None, None, reason)


def _load(value: dict | str | None) -> object:
    """Dicts pass through; strings are parsed, blank meaning JSON null."""
    if isinstance(value, str):
        return json.loads(value) if value.strip() else None
    return value


def _int_field(element: dict, name: str) -> int | None:
    raw = _read_lenient_int(element, name)
    if raw is None:
        return None
    low = raw & _UINT32_MASK  # C# unchecked (int)long
    return low - _UINT32_LIMIT if low > _INT32_MAX else low


def _hand(root: dict) -> list[dict]:
    combat = root.get("combat")
    hand = combat.get("hand") if isinstance(combat, dict) else None
    return [card for card in hand if isinstance(card, dict)] if isinstance(hand, list) else []


def _enemy_standing(root: dict, index: int) -> bool:
    combat = root.get("combat")
    enemies = combat.get("enemies") if isinstance(combat, dict) else None
    if not isinstance(enemies, list):
        return False
    for enemy in enemies:
        if isinstance(enemy, dict) and _read_int(enemy, "i") == index:
            return _read_bool(enemy, "alive") is True and _read_bool(enemy, "hittable") is True
    return False


def _player_hp(root: dict) -> int | None:
    combat = root.get("combat")
    player = combat.get("player") if isinstance(combat, dict) else None
    hp = _read_string(player, "hp") if isinstance(player, dict) else None
    slash = -1 if hp is None else hp.find("/")
    return _parse_int_text(hp[:slash], _INT32_MIN, _INT32_MAX) if slash > 0 else None


def _read_string(parent: dict, name: str) -> str | None:
    value = parent.get(name)
    return value if isinstance(value, str) else None


def _read_bool(parent: dict, name: str) -> bool | None:
    value = parent.get(name)
    return value if isinstance(value, bool) else None


def _int(value: object, low: int, high: int) -> int | None:
    if isinstance(value, bool) or not isinstance(value, int):
        return None
    return value if low <= value <= high else None


def _read_int(parent: dict, name: str) -> int | None:
    return _int(parent.get(name), _INT32_MIN, _INT32_MAX)


def _read_uint(parent: dict, name: str) -> int | None:
    return _int(parent.get(name), 0, _UINT32_LIMIT - 1)


def _parse_int_text(text: str, low: int, high: int) -> int | None:
    """``long.TryParse`` for an integer literal: optional sign, ASCII digits only."""
    stripped = text.strip()
    digits = stripped[1:] if stripped[:1] in ("+", "-") else stripped
    if not digits or not digits.isascii() or not digits.isdigit():
        return None
    value = int(stripped)
    return value if low <= value <= high else None


def _read_lenient_int(parent: dict, name: str) -> int | None:
    """A number, or a numeric string -- models write ``"3"`` as often as ``3``."""
    value = parent.get(name)
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return _int(value, _INT64_MIN, _INT64_MAX)
    return _parse_int_text(value, _INT64_MIN, _INT64_MAX) if isinstance(value, str) else None
