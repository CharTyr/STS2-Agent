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
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping, Sequence

SCHEMA_VERSION = 1
DEFAULT_SUITE = Path("docs/decision-benchmark.json")

# These top-level names are part of the suite interface. The state remains an honest /state snapshot
# (additional fields are allowed for future mod versions), while missing contract fields make a case
# unscorable instead of letting an accidental toy fixture inflate an aggregate score.
REQUIRED_STATE_FIELDS = frozenset({"screen", "available_actions"})
ALLOWED_CASE_KINDS = frozenset({"combat", "event", "map", "rest", "shop", "reward"})
ALLOWED_EXPECTATION_KINDS = frozenset(
    {
        "action",
        "option_action",
        "target_action",
        "forbid_action",
        "state_path",
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


@dataclass(frozen=True)
class Case:
    """A stable decision snapshot and the constraints it can actually support."""

    id: str
    kind: str
    title: str
    state: Mapping[str, Any]
    expectations: tuple[Expectation, ...]
    evidence: tuple[str, ...]


@dataclass(frozen=True)
class Suite:
    """A versioned set of offline cases, ready for a candidate answer set."""

    title: str
    cases: tuple[Case, ...]
    source: str


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

    known = {"id", "kind", "points", "action", "option_index", "target_index", "path", "equals"}
    unexpected = sorted(set(data) - known)
    if unexpected:
        raise BenchmarkError(f"case {case_id!r} expectation {expectation_id!r} has unsupported field(s): {', '.join(unexpected)}")
    return Expectation(expectation_id, kind, points, action, option_index, target_index, path, equals)


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
            entries = ((state.get("map") or {}).get("available_nodes") or [])
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
            entries = ((state.get("reward") or {}).get("card_options") or [])
            index_name = "option_index"
        else:
            # A new action may have a valid index domain, but its domain must be made explicit in
            # code before its first benchmark case. Guessing turns a typo into an inflated score.
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} needs an explicit index-domain validator "
                f"for action {expectation.action!r}"
            )
        if not isinstance(entries, list) or not any(entry.get("index") == index for entry in entries if isinstance(entry, Mapping)):
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
        target_sets = [
            entry.get("valid_target_indices")
            for entry in hand
            if isinstance(entry, Mapping) and entry.get("requires_target") is True
        ]
        if not any(isinstance(indices, list) and expectation.target_index in indices for indices in target_sets):
            raise BenchmarkError(
                f"case {case_id!r} expectation {expectation.id!r} selects target_index={expectation.target_index}, "
                "but no target-required card advertises that target"
            )


def _parse_case(raw: Any, index: int) -> Case:
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

    raw_expectations = data.get("expectations")
    if not isinstance(raw_expectations, list) or not raw_expectations:
        raise BenchmarkError(f"case {case_id!r}.expectations must be a non-empty array")
    expectations = tuple(_parse_expectation(item, case_id=case_id, index=item_index, state=state) for item_index, item in enumerate(raw_expectations))
    expectation_ids = [expectation.id for expectation in expectations]
    if len(expectation_ids) != len(set(expectation_ids)):
        raise BenchmarkError(f"case {case_id!r} repeats an expectation id")
    for expectation in expectations:
        _validate_action_expectation(case_id, expectation, state)

    raw_evidence = data.get("evidence")
    if not isinstance(raw_evidence, list) or not raw_evidence:
        raise BenchmarkError(f"case {case_id!r}.evidence must be a non-empty string array")
    evidence = tuple(_expect_string(value, f"case {case_id!r}.evidence") for value in raw_evidence)

    known = {"id", "kind", "title", "state", "expectations", "evidence"}
    unexpected = sorted(set(data) - known)
    if unexpected:
        raise BenchmarkError(f"case {case_id!r} has unsupported field(s): {', '.join(unexpected)}")
    return Case(case_id, kind, title, state, expectations, evidence)


def load_suite(path: Path) -> Suite:
    """Load a declarative benchmark suite and reject fixtures that overstate their evidence."""
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        raise BenchmarkError(f"cannot read benchmark suite {path}: {exc}") from exc
    except json.JSONDecodeError as exc:
        raise BenchmarkError(f"benchmark suite {path} is not valid JSON: {exc.msg}") from exc
    data = _expect_object(raw, "benchmark suite")
    if data.get("schema_version") != SCHEMA_VERSION:
        raise BenchmarkError(f"benchmark suite schema_version must be {SCHEMA_VERSION}")
    title = _expect_string(data.get("title"), "benchmark suite.title")
    raw_cases = data.get("cases")
    if not isinstance(raw_cases, list) or not raw_cases:
        raise BenchmarkError("benchmark suite.cases must be a non-empty array")
    cases = tuple(_parse_case(item, index) for index, item in enumerate(raw_cases))
    case_ids = [case.id for case in cases]
    if len(case_ids) != len(set(case_ids)):
        raise BenchmarkError("benchmark suite repeats a case id")
    known = {"schema_version", "title", "cases"}
    unexpected = sorted(set(data) - known)
    if unexpected:
        raise BenchmarkError(f"benchmark suite has unsupported field(s): {', '.join(unexpected)}")
    return Suite(title=title, cases=cases, source=path.as_posix())


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


def _score_expectation(expectation: Expectation, answer: Mapping[str, Any] | None) -> tuple[bool, str]:
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
    raise AssertionError(f"unhandled expectation kind {expectation.kind}")


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
            passed, detail = _score_expectation(expectation, answer)
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


def _parse_args(argv: Sequence[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate and score an offline STS2 decision benchmark.")
    parser.add_argument("--suite", type=Path, default=DEFAULT_SUITE, help="versioned benchmark fixture JSON")
    parser.add_argument("--answers", type=Path, help="candidate answers JSON; omit to validate the suite only")
    parser.add_argument("--output", type=Path, help="write the deterministic score report here")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = _parse_args(argv)
    try:
        suite = load_suite(args.suite)
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
