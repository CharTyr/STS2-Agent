#!/usr/bin/env python3
"""Load, validate, and score offline STS2 decision-quality benchmark cases.

The public module interface stays intentionally small:

* ``load_suite(path)`` validates one declarative JSON suite.
* ``score_suite(suite, answers)`` grades recorded candidate actions without a model, game, or network.
* ``render_report(report)`` produces deterministic JSON for CI or human comparison.

A case records a source-faithful state snapshot plus a bounded set of acceptable action shapes.
It scores *decision quality constraints* that can be checked from that snapshot; it does not claim
that an offline fixture simulates a whole combat or proves a model can win a run.  Live capture and
real-model sampling are separate, opt-in work that can emit the same input shape later.

Schema 2 adds *arithmetic* cases: a combat snapshot that also carries the compact view's
``combat.threat`` and ``hand[].vs`` numbers, plus ``plan`` expectations (``must_kill_enemy``,
``max_damage_taken``) that score the answered single action against those published numbers.  The
arithmetic is never simulated: the remaining damage is the frame's own ``threat.unblocked`` minus the
answered card's block preview, minus the incoming of an enemy that card's preview says it kills, plus
the frame's end-of-turn self damage.  ``docs/decision-benchmark.json`` marks those cases with
``"calculator": "combat_math"`` so the C# offline suite can re-derive the same numbers with
``CombatMathPolicy`` (``STS2AIAgent.Tests/DecisionBenchmarkMathTests.cs``) at zero cost.
Schema-1 cases keep scoring exactly as before.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping, Sequence
from urllib import error, request

SCHEMA_VERSION = 2
SUPPORTED_SCHEMA_VERSIONS = frozenset({1, 2})
DEFAULT_SUITE = Path("docs/decision-benchmark.json")

# These top-level names are part of the suite interface. The state remains an honest /state snapshot
# (additional fields are allowed for future mod versions), while missing contract fields make a case
# unscorable instead of letting an accidental toy fixture inflate an aggregate score.
REQUIRED_STATE_FIELDS = frozenset({"screen", "available_actions"})
ALLOWED_CASE_KINDS = frozenset({"combat", "event", "map", "rest", "shop", "reward"})
# A case with this marker carries numbers the local CombatMathPolicy must reproduce; only combat
# frames have an arithmetic calculator, and the marker is meaningless without combat.threat.
ALLOWED_CALCULATORS = frozenset({"combat_math"})
ALLOWED_EXPECTATION_KINDS = frozenset(
    {
        "action",
        "option_action",
        "target_action",
        "forbid_action",
        "state_path",
        "plan",
    }
)


class BenchmarkError(ValueError):
    """A suite or answer is structurally invalid and cannot yield a trustworthy score."""


@dataclass(frozen=True)
class Expectation:
    """One independently reportable decision constraint for a fixture."""

    id: str
    kind: str
    points: float
    action: str | None = None
    option_index: int | None = None
    target_index: int | None = None
    path: tuple[str, ...] | None = None
    equals: Any = None
    must_kill_enemy: int | None = None
    max_damage_taken: int | None = None


@dataclass(frozen=True)
class Case:
    """A stable decision snapshot and the constraints it can actually support."""

    id: str
    kind: str
    title: str
    state: Mapping[str, Any]
    expectations: tuple[Expectation, ...]
    evidence: tuple[str, ...]
    calculator: str | None = None


@dataclass(frozen=True)
class Suite:
    """A versioned set of offline cases, ready for a candidate answer set."""

    title: str
    cases: tuple[Case, ...]
    source: str
    schema_version: int = SCHEMA_VERSION


def _expect_object(value: Any, label: str) -> Mapping[str, Any]:
    if not isinstance(value, dict):
        raise BenchmarkError(f"{label} must be an object")
    return value


def _expect_string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise BenchmarkError(f"{label} must be a non-empty string")
    return value


def _expect_points(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(float(value)) or value <= 0:
        raise BenchmarkError(f"{label} must be a positive finite number")
    return float(value)


def _parse_path(value: Any, label: str) -> tuple[str, ...]:
    if not isinstance(value, str) or not value:
        raise BenchmarkError(f"{label} must be a non-empty dotted path")
    parts = tuple(value.split("."))
    if any(not part or not part.replace("_", "").isalnum() for part in parts):
        raise BenchmarkError(f"{label} contains an invalid path segment")
    return parts


def _state_path(state: Mapping[str, Any], path: Sequence[str]) -> Any:
    current: Any = state
    for segment in path:
        if not isinstance(current, Mapping) or segment not in current:
            raise BenchmarkError(f"state is missing required evidence path {'.'.join(path)!r}")
        current = current[segment]
    return current


def _entry_index(entry: Any) -> int | None:
    """One list entry's index: ``i`` in the compact view, ``index`` in raw ``/state``."""
    if not isinstance(entry, Mapping):
        return None
    for key in ("index", "i"):
        value = entry.get(key)
        if isinstance(value, int) and not isinstance(value, bool):
            return value
    return None


def _find_entry(entries: Any, index: int) -> Mapping[str, Any] | None:
    if not isinstance(entries, list):
        return None
    for entry in entries:
        if _entry_index(entry) == index:
            return entry
    return None


def _entry_targets(entry: Any) -> list[int]:
    """A card's declared legal targets: raw ``valid_target_indices`` or compact ``targets``."""
    if not isinstance(entry, Mapping):
        return []
    for key in ("valid_target_indices", "targets"):
        value = entry.get(key)
        if isinstance(value, list):
            return [item for item in value if isinstance(item, int) and not isinstance(item, bool)]
    return []


def _card_block(entry: Any) -> int:
    """The card's block preview: the compact ``values[]`` entry whose name mentions block."""
    if not isinstance(entry, Mapping):
        return 0
    for value in entry.get("values") or []:
        if not isinstance(value, Mapping) or not isinstance(value.get("name"), str):
            continue
        if "block" not in value["name"].lower():
            continue
        for key in ("preview", "base_value"):
            number = value.get(key)
            if isinstance(number, int) and not isinstance(number, bool) and number > 0:
                return number
    return 0


def _vs_cell(card: Any, enemy: int) -> Mapping[str, Any] | None:
    """The compact damage preview of this card against one enemy, when the frame carries one."""
    if not isinstance(card, Mapping):
        return None
    for cell in card.get("vs") or []:
        if isinstance(cell, Mapping) and cell.get("enemy") == enemy:
            return cell
    return None


def _threat(state: Mapping[str, Any]) -> Mapping[str, Any]:
    threat = (state.get("combat") or {}).get("threat")
    return threat if isinstance(threat, Mapping) else {}


def _per_enemy_incoming(state: Mapping[str, Any], enemy: int) -> int:
    for entry in _threat(state).get("per_enemy") or []:
        if isinstance(entry, Mapping) and entry.get("i") == enemy:
            incoming = entry.get("incoming")
            if isinstance(incoming, int) and not isinstance(incoming, bool):
                return incoming
    return 0


def _non_negative_int(data: Mapping[str, Any], key: str, label: str) -> int:
    value = data.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise BenchmarkError(f"{label} must be a non-negative integer")
    return value


def _parse_expectation(raw: Any, *, case_id: str, index: int, state: Mapping[str, Any]) -> Expectation:
    data = _expect_object(raw, f"case {case_id!r} expectation {index}")
    expectation_id = _expect_string(data.get("id"), f"case {case_id!r} expectation {index}.id")
    kind = _expect_string(data.get("kind"), f"case {case_id!r} expectation {expectation_id!r}.kind")
    if kind not in ALLOWED_EXPECTATION_KINDS:
        raise BenchmarkError(
            f"case {case_id!r} expectation {expectation_id!r}.kind must be one of {sorted(ALLOWED_EXPECTATION_KINDS)}"
        )
    points = _expect_points(data.get("points"), f"case {case_id!r} expectation {expectation_id!r}.points")
    action = data.get("action")
    if kind in {"action", "option_action", "target_action", "forbid_action"}:
        action = _expect_string(action, f"case {case_id!r} expectation {expectation_id!r}.action")
    elif action is not None:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r} must not declare action")

    option_index = data.get("option_index")
    if kind == "option_action":
        if isinstance(option_index, bool) or not isinstance(option_index, int) or option_index < 0:
            raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r}.option_index must be a non-negative integer")
    elif option_index is not None:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r} must not declare option_index")

    target_index = data.get("target_index")
    if kind == "target_action":
        if isinstance(target_index, bool) or not isinstance(target_index, int) or target_index < 0:
            raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r}.target_index must be a non-negative integer")
    elif target_index is not None:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r} must not declare target_index")

    path: tuple[str, ...] | None = None
    equals: Any = None
    if kind == "state_path":
        path = _parse_path(data.get("path"), f"case {case_id!r} expectation {expectation_id!r}.path")
        if "equals" not in data:
            raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r}.equals is required")
        equals = data["equals"]
        actual = _state_path(state, path)
        if actual != equals:
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation_id!r} claims {'.'.join(path)!r} == {equals!r}, "
                f"but its snapshot has {actual!r}"
            )
    elif "path" in data or "equals" in data:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r} declares state_path fields for {kind!r}")

    must_kill_enemy: int | None = None
    max_damage_taken: int | None = None
    if kind == "plan":
        if "must_kill_enemy" in data:
            must_kill_enemy = _non_negative_int(
                data, "must_kill_enemy", f"case {case_id!r} expectation {expectation_id!r}.must_kill_enemy"
            )
        if "max_damage_taken" in data:
            max_damage_taken = _non_negative_int(
                data, "max_damage_taken", f"case {case_id!r} expectation {expectation_id!r}.max_damage_taken"
            )
        if must_kill_enemy is None and max_damage_taken is None:
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation_id!r} declares no plan constraint; "
                "at least one of must_kill_enemy / max_damage_taken is required"
            )
    elif "must_kill_enemy" in data or "max_damage_taken" in data:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r} declares plan fields for {kind!r}")

    known = {
        "id",
        "kind",
        "points",
        "action",
        "option_index",
        "target_index",
        "path",
        "equals",
        "must_kill_enemy",
        "max_damage_taken",
    }
    unexpected = sorted(set(data) - known)
    if unexpected:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r} has unsupported field(s): {', '.join(unexpected)}")
    return Expectation(
        expectation_id,
        kind,
        points,
        action,
        option_index,
        target_index,
        path,
        equals,
        must_kill_enemy,
        max_damage_taken,
    )


def _validate_action_expectation(case_id: str, expectation: Expectation, state: Mapping[str, Any]) -> None:
    """Reject a preferred/forbidden action that the captured snapshot cannot legally issue."""
    if expectation.kind not in {"action", "option_action", "target_action"}:
        return
    available_actions = state["available_actions"]
    if expectation.action not in available_actions:
        raise BenchmarkError(
            f"case {case_id!r} expectation {expectation.id!r} requires action {expectation.action!r}, "
            "but state.available_actions does not expose it"
        )

    if expectation.kind == "option_action":
        index = expectation.option_index
        if expectation.action == "play_card":
            entries = ((state.get("combat") or {}).get("hand") or [])
            index_name = "card_index"
        elif expectation.action == "choose_map_node":
            entries = ((state.get("map") or {}).get("available_nodes") or (state.get("map") or {}).get("options") or [])
            index_name = "option_index"
        elif expectation.action == "choose_rest_option":
            entries = ((state.get("rest") or {}).get("options") or [])
            index_name = "option_index"
        elif expectation.action == "choose_event_option":
            entries = ((state.get("event") or {}).get("options") or [])
            index_name = "option_index"
        elif expectation.action == "buy_card":
            entries = ((state.get("shop") or {}).get("cards") or [])
            index_name = "option_index"
        elif expectation.action == "choose_reward_card":
            reward = state.get("reward") or {}
            entries = reward.get("card_options") or reward.get("cards") or []
            index_name = "option_index"
        else:
            # A new action may have a valid index domain, but its domain must be made explicit in
            # code before its first benchmark case. Guessing turns a typo into an inflated score.
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} needs an explicit index-domain validator "
                f"for action {expectation.action!r}"
            )
        if _find_entry(entries, index) is None:
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} selects {index_name}={index}, "
                "but that index is absent from the captured state"
            )

    if expectation.kind == "target_action":
        if expectation.action != "play_card":
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} needs an explicit target-domain validator "
                f"for action {expectation.action!r}"
            )
        hand = ((state.get("combat") or {}).get("hand") or [])
        # Either snapshot shape declares its legal targets; an entry without a target list (a
        # no-target card) proves nothing about this index and is skipped rather than rejected.
        target_sets = [_entry_targets(entry) for entry in hand if isinstance(entry, Mapping)]
        if not any(expectation.target_index in indices for indices in target_sets):
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} selects target_index={expectation.target_index}, "
                "but no card advertises that target"
            )


def _parse_case(raw: Any, index: int, schema_version: int) -> Case:
    data = _expect_object(raw, f"case {index}")
    case_id = _expect_string(data.get("id"), f"case {index}.id")
    kind = _expect_string(data.get("kind"), f"case {case_id!r}.kind")
    if kind not in ALLOWED_CASE_KINDS:
        raise BenchmarkError(f"case {case_id!r}.kind must be one of {sorted(ALLOWED_CASE_KINDS)}")
    title = _expect_string(data.get("title"), f"case {case_id!r}.title")
    state = _expect_object(data.get("state"), f"case {case_id!r}.state")
    missing_state = sorted(REQUIRED_STATE_FIELDS - set(state))
    if missing_state:
        raise BenchmarkError(f"case {case_id!r}.state is missing required field(s): {', '.join(missing_state)}")
    if not isinstance(state["available_actions"], list) or not all(isinstance(item, str) for item in state["available_actions"]):
        raise BenchmarkError(f"case {case_id!r}.state.available_actions must be a string array")

    calculator: str | None = None
    if "calculator" in data:
        calculator = _expect_string(data.get("calculator"), f"case {case_id!r}.calculator")
        if calculator not in ALLOWED_CALCULATORS:
            raise BenchmarkError(f"case {case_id!r}.calculator must be one of {sorted(ALLOWED_CALCULATORS)}")
        if schema_version < 2:
            raise BenchmarkError(f"case {case_id!r}.calculator requires schema_version 2")
        if kind != "combat":
            raise BenchmarkError(f"case {case_id!r}.calculator is only meaningful for a combat case")
        if not isinstance((state.get("combat") or {}).get("threat"), Mapping):
            raise BenchmarkError(
                f"case {case_id!r} is marked {calculator!r} but its snapshot carries no combat.threat"
            )

    raw_expectations = data.get("expectations")
    if not isinstance(raw_expectations, list) or not raw_expectations:
        raise BenchmarkError(f"case {case_id!r}.expectations must be a non-empty array")
    expectations = tuple(_parse_expectation(item, case_id=case_id, index=item_index, state=state) for item_index, item in enumerate(raw_expectations))
    expectation_ids = [expectation.id for expectation in expectations]
    if len(expectation_ids) != len(set(expectation_ids)):
        raise BenchmarkError(f"case {case_id!r} repeats an expectation id")
    for expectation in expectations:
        _validate_action_expectation(case_id, expectation, state)
        _validate_plan_expectation(case_id, expectation, state)

    raw_evidence = data.get("evidence")
    if not isinstance(raw_evidence, list) or not raw_evidence:
        raise BenchmarkError(f"case {case_id!r}.evidence must be a non-empty string array")
    evidence = tuple(_expect_string(value, f"case {case_id!r}.evidence") for value in raw_evidence)

    known = {"id", "kind", "title", "state", "expectations", "evidence", "calculator"}
    unexpected = sorted(set(data) - known)
    if unexpected:
        raise BenchmarkError(f"case {case_id!r} has unsupported field(s): {', '.join(unexpected)}")
    return Case(case_id, kind, title, state, expectations, evidence, calculator)


def _validate_plan_expectation(case_id: str, expectation: Expectation, state: Mapping[str, Any]) -> None:
    """Reject a plan constraint the snapshot's own arithmetic cannot satisfy in one answered action.

    The scorer grades a single recorded action (see ``_score_plan``), so a constraint that no single
    card can reach would be an unwinnable point dressed up as evidence. This is the loader's job: the
    per-case numbers are proved against ``combat.threat`` / ``hand[].vs`` before a candidate ever
    answers, and the C# suite re-derives the same numbers with ``CombatMathPolicy``.
    """
    if expectation.kind != "plan":
        return
    combat = state.get("combat")
    if not isinstance(combat, Mapping) or not combat:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation.id!r} needs a combat snapshot")
    hand = combat.get("hand") or []
    threat = _threat(state)

    if expectation.must_kill_enemy is not None:
        enemy = expectation.must_kill_enemy
        enemies = [_entry_index(entry) for entry in combat.get("enemies") or []]
        if enemy not in enemies:
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} names enemy {enemy}, "
                "but combat.enemies does not contain that index"
            )
        killers = [
            _entry_index(card)
            for card in hand
            if isinstance(card, Mapping)
            and (cell := _vs_cell(card, enemy)) is not None
            and cell.get("kills") is True
        ]
        if not killers:
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} asks for a kill of enemy {enemy}, "
                "but no hand card's damage preview marks it as a kill"
            )

    if expectation.max_damage_taken is not None:
        unblocked = threat.get("unblocked")
        if isinstance(unblocked, bool) or not isinstance(unblocked, int):
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} needs combat.threat.unblocked, "
                "which the snapshot does not carry"
            )
        self_damage = threat.get("end_of_turn_self_damage") or 0
        if isinstance(self_damage, bool) or not isinstance(self_damage, int):
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} has a non-integer "
                "combat.threat.end_of_turn_self_damage"
            )
        best_saving = 0
        for card in hand:
            if not isinstance(card, Mapping):
                continue
            best_saving = max(best_saving, _card_block(card))
            for cell in card.get("vs") or []:
                if isinstance(cell, Mapping) and cell.get("kills") is True:
                    best_saving = max(best_saving, _per_enemy_incoming(state, cell.get("enemy")))
        achievable = max(0, unblocked - best_saving) + max(0, self_damage)
        if achievable > expectation.max_damage_taken:
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} asks for at most "
                f"{expectation.max_damage_taken} damage, but the best single answered action still takes "
                f"{achievable}"
            )


def load_suite(path: Path) -> Suite:
    """Load a declarative benchmark suite and reject fixtures that overstate their evidence."""
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        raise BenchmarkError(f"cannot read benchmark suite {path}: {exc}") from exc
    except json.JSONDecodeError as exc:
        raise BenchmarkError(f"benchmark suite {path} is not valid JSON: {exc.msg}") from exc
    data = _expect_object(raw, "benchmark suite")
    schema_version = data.get("schema_version")
    if schema_version not in SUPPORTED_SCHEMA_VERSIONS:
        raise BenchmarkError(
            f"benchmark suite schema_version must be one of {sorted(SUPPORTED_SCHEMA_VERSIONS)}"
        )
    title = _expect_string(data.get("title"), "benchmark suite.title")
    raw_cases = data.get("cases")
    if not isinstance(raw_cases, list) or not raw_cases:
        raise BenchmarkError("benchmark suite.cases must be a non-empty array")
    cases = tuple(_parse_case(item, index, schema_version) for index, item in enumerate(raw_cases))
    case_ids = [case.id for case in cases]
    if len(case_ids) != len(set(case_ids)):
        raise BenchmarkError("benchmark suite repeats a case id")
    known = {"schema_version", "title", "cases"}
    unexpected = sorted(set(data) - known)
    if unexpected:
        raise BenchmarkError(f"benchmark suite has unsupported field(s): {', '.join(unexpected)}")
    return Suite(title=title, cases=cases, source=path.as_posix(), schema_version=schema_version)


def _normalize_answers(raw: Any, suite: Suite) -> Mapping[str, Mapping[str, Any]]:
    data = _expect_object(raw, "answer document")
    answers = data.get("answers")
    if not isinstance(answers, list):
        raise BenchmarkError("answer document.answers must be an array")
    known_cases = {case.id for case in suite.cases}
    result: dict[str, Mapping[str, Any]] = {}
    for index, raw_answer in enumerate(answers):
        answer = _expect_object(raw_answer, f"answer {index}")
        case_id = _expect_string(answer.get("case_id"), f"answer {index}.case_id")
        if case_id not in known_cases:
            raise BenchmarkError(f"answer {index} names unknown case {case_id!r}")
        if case_id in result:
            raise BenchmarkError(f"answer document repeats case {case_id!r}")
        action = _expect_string(answer.get("action"), f"answer {index}.action")
        option_index = answer.get("option_index")
        if option_index is not None and (isinstance(option_index, bool) or not isinstance(option_index, int) or option_index < 0):
            raise BenchmarkError(f"answer {index}.option_index must be a non-negative integer when supplied")
        target_index = answer.get("target_index")
        if target_index is not None and (isinstance(target_index, bool) or not isinstance(target_index, int) or target_index < 0):
            raise BenchmarkError(f"answer {index}.target_index must be a non-negative integer when supplied")
        known = {"case_id", "action", "option_index", "target_index", "reason"}
        unexpected = sorted(set(answer) - known)
        if unexpected:
            raise BenchmarkError(f"answer {index} has unsupported field(s): {', '.join(unexpected)}")
        result[case_id] = answer
    return result


def _score_expectation(expectation: Expectation, answer: Mapping[str, Any] | None, state: Mapping[str, Any]) -> tuple[bool, str]:
    if expectation.kind == "state_path":
        return True, "fixture evidence verified"
    if answer is None:
        return False, "no answer supplied"
    action = answer["action"]
    if expectation.kind == "action":
        return action == expectation.action, f"expected action {expectation.action!r}, received {action!r}"
    if expectation.kind == "option_action":
        actual_option = answer.get("option_index")
        passed = action == expectation.action and actual_option == expectation.option_index
        return passed, (
            f"expected {expectation.action!r} option_index={expectation.option_index}, "
            f"received {action!r} option_index={actual_option!r}"
        )
    if expectation.kind == "target_action":
        actual_target = answer.get("target_index")
        passed = action == expectation.action and actual_target == expectation.target_index
        return passed, (
            f"expected {expectation.action!r} target_index={expectation.target_index}, "
            f"received {action!r} target_index={actual_target!r}"
        )
    if expectation.kind == "forbid_action":
        return action != expectation.action, f"forbidden action {expectation.action!r}, received {action!r}"
    if expectation.kind == "plan":
        return _score_plan(expectation, answer, state)
    raise AssertionError(f"unhandled expectation kind {expectation.kind}")


def _answered_card(answer: Mapping[str, Any], state: Mapping[str, Any]) -> Mapping[str, Any] | None:
    """The hand entry the answer plays, or None when it plays no card."""
    if answer["action"] != "play_card":
        return None
    option_index = answer.get("option_index")
    if isinstance(option_index, bool) or not isinstance(option_index, int):
        return None
    return _find_entry((state.get("combat") or {}).get("hand"), option_index)


def _score_plan(expectation: Expectation, answer: Mapping[str, Any], state: Mapping[str, Any]) -> tuple[bool, str]:
    """Score one plan constraint against the answered single action and the frame's own numbers."""
    card = _answered_card(answer, state)
    target = answer.get("target_index")
    threat = _threat(state)

    if expectation.must_kill_enemy is not None:
        enemy = expectation.must_kill_enemy
        if card is None:
            return False, f"the answer plays no card, so it cannot kill enemy {enemy}"
        if target != enemy:
            return False, f"the answer targets {target!r}, so it cannot kill enemy {enemy}"
        cell = _vs_cell(card, enemy)
        if cell is None:
            return False, f"card {expectation.id!r}'s answer has no damage preview for enemy {enemy}"
        if cell.get("kills") is not True:
            return False, (
                f"the answered card deals {cell.get('damage')!r} to enemy {enemy}, "
                "which its preview does not mark as a kill"
            )

    if expectation.max_damage_taken is not None:
        unblocked = threat.get("unblocked")
        if isinstance(unblocked, bool) or not isinstance(unblocked, int):
            return False, "the fixture has no combat.threat.unblocked to check against"
        saved = 0
        if card is not None and target is not None:
            cell = _vs_cell(card, target)
            if cell is not None and cell.get("kills") is True:
                saved = _per_enemy_incoming(state, target)
        self_damage = threat.get("end_of_turn_self_damage") or 0
        if isinstance(self_damage, bool) or not isinstance(self_damage, int):
            self_damage = 0
        remaining = max(0, unblocked - _card_block(card) - saved) + max(0, self_damage)
        passed = remaining <= expectation.max_damage_taken
        return passed, (
            f"the answered action leaves {remaining} damage, limit {expectation.max_damage_taken}"
        )
    if expectation.must_kill_enemy is not None:
        return True, f"the answered preview kills enemy {expectation.must_kill_enemy}"
    raise AssertionError("a plan expectation declared neither constraint")


def score_suite(suite: Suite, answers: Any) -> dict[str, Any]:
    """Score supplied action decisions; omissions score zero instead of being silently excluded."""
    normalized_answers = _normalize_answers(answers, suite)
    case_reports: list[dict[str, Any]] = []
    total_available = 0.0
    total_earned = 0.0
    for case in suite.cases:
        answer = normalized_answers.get(case.id)
        earned = 0.0
        constraints: list[dict[str, Any]] = []
        for expectation in case.expectations:
            passed, detail = _score_expectation(expectation, answer, case.state)
            # Fixture-evidence checks prove the case is honest but are not a free point for a runner
            # that supplied no decision. Once it does answer a case, they remain in its denominator
            # and numerator so a contradictory fixture cannot quietly produce a valid score.
            earned_pass = passed and answer is not None
            total_available += expectation.points
            if earned_pass:
                earned += expectation.points
                total_earned += expectation.points
            constraints.append(
                {
                    "id": expectation.id,
                    "kind": expectation.kind,
                    "points": expectation.points,
                    "passed": earned_pass,
                    "detail": detail,
                }
            )
        available = sum(expectation.points for expectation in case.expectations)
        case_reports.append(
            {
                "case_id": case.id,
                "kind": case.kind,
                "title": case.title,
                "answered": answer is not None,
                "answer": dict(answer) if answer is not None else None,
                "earned_points": earned,
                "available_points": available,
                "constraints": constraints,
            }
        )
    return {
        "schema_version": SCHEMA_VERSION,
        "suite": suite.title,
        "source": suite.source,
        "scoring_scope": "Recorded action decisions only; no claim of simulated combat outcome or live-model quality.",
        "answered_cases": len(normalized_answers),
        "total_cases": len(suite.cases),
        "earned_points": total_earned,
        "available_points": total_available,
        "score_percent": round((100.0 * total_earned / total_available) if total_available else 0.0, 2),
        "cases": case_reports,
    }


def render_report(report: Mapping[str, Any]) -> str:
    """Render deterministic report bytes, suitable for a checked-in sample or CI artifact."""
    return json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def capture_state(base_url: str, *, timeout: float = 5.0) -> Mapping[str, Any]:
    """Read exactly one local `/state` envelope for manual fixture capture.

    This is deliberately not implicit in validation/scoring: the caller must supply the URL, run
    the game themselves, and decide when its state is stable. It sends no action and has no model
    configuration or credential path, so it cannot spend a provider credit.
    """
    url = base_url.rstrip("/") + "/state"
    http_request = request.Request(url=url, headers={"Accept": "application/json"})
    try:
        with request.urlopen(http_request, timeout=timeout) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except (error.URLError, OSError, TimeoutError) as exc:
        raise BenchmarkError(f"cannot capture /state from {url}: {exc}") from exc
    except UnicodeDecodeError as exc:
        raise BenchmarkError(f"capture /state from {url} was not UTF-8") from exc
    except json.JSONDecodeError as exc:
        raise BenchmarkError(f"capture /state from {url} was not JSON: {exc.msg}") from exc
    if not isinstance(payload, Mapping) or payload.get("ok") is not True or not isinstance(payload.get("data"), Mapping):
        raise BenchmarkError(f"capture /state from {url} did not return an ok object envelope")
    return payload["data"]


def capture_template(suite: Suite, base_url: str) -> dict[str, Any]:
    """Return a no-action, no-model capture document shaped for manual fixture authoring."""
    state = capture_state(base_url)
    return {
        "schema_version": SCHEMA_VERSION,
        "capture_scope": "Single explicit GET /state only; no model/API call and no game action.",
        "base_url": base_url.rstrip("/"),
        "state": state,
        "suite": suite.title,
        "next_step": "Review this state manually, redact identifiers if needed, then add only snapshot-evidenced cases to the versioned suite.",
    }


def _parse_args(argv: Sequence[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate and score an offline STS2 decision benchmark.")
    parser.add_argument("--suite", type=Path, default=DEFAULT_SUITE, help="versioned benchmark fixture JSON")
    parser.add_argument("--answers", type=Path, help="candidate answers JSON; omit to validate the suite only")
    parser.add_argument("--capture-base-url", help="explicit local Mod URL; captures the suite's raw states into candidate answers without any model call")
    parser.add_argument("--output", type=Path, help="write the deterministic score report or capture document here")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = _parse_args(argv)
    try:
        suite = load_suite(args.suite)
        if args.answers is not None and args.capture_base_url is not None:
            raise BenchmarkError("--answers and --capture-base-url are mutually exclusive")
        if args.capture_base_url is not None:
            rendered = render_report(capture_template(suite, args.capture_base_url))
            if args.output is None:
                sys.stdout.write(rendered)
            else:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(rendered, encoding="utf-8")
                print(f"[decision-benchmark] wrote capture {args.output}")
            return 0
        if args.answers is None:
            print(f"[decision-benchmark] {suite.title}: {len(suite.cases)} cases validated; no model/game run requested")
            return 0
        try:
            answers = json.loads(args.answers.read_text(encoding="utf-8"))
        except OSError as exc:
            raise BenchmarkError(f"cannot read answer document {args.answers}: {exc}") from exc
        except json.JSONDecodeError as exc:
            raise BenchmarkError(f"answer document {args.answers} is not valid JSON: {exc.msg}") from exc
        rendered = render_report(score_suite(suite, answers))
        if args.output is None:
            sys.stdout.write(rendered)
        else:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(rendered, encoding="utf-8")
            print(f"[decision-benchmark] wrote {args.output}")
        return 0
    except BenchmarkError as exc:
        print(f"decision benchmark failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
