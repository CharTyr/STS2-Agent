"""Runs the shared whole-turn plan vectors against the Python port.

The vectors file is shared with the C# copy (``TurnPlanVectorTests.cs``): every
case here has an identical case there, so a drift in either language fails.
"""

import json
import unittest
from pathlib import Path

from sts2_mcp.turn_plan import PlanStep, diverged, parse, resolve

VECTORS_PATH = Path(__file__).parent / "fixtures" / "turn_plan_vectors.json"


def _step_dict(step: PlanStep | None) -> dict | None:
    if step is None:
        return None
    return {
        "action": step.action,
        "card_cid": step.card_cid,
        "card_index": step.card_index,
        "target_index": step.target_index,
        "option_index": step.option_index,
        "reason": step.reason,
    }


def _plan_step(raw: dict) -> PlanStep:
    return PlanStep(
        action=raw.get("action", ""),
        card_cid=raw.get("card_cid"),
        card_index=raw.get("card_index"),
        target_index=raw.get("target_index"),
        option_index=raw.get("option_index"),
        reason=raw.get("reason"),
    )


class TurnPlanVectorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.vectors = json.loads(VECTORS_PATH.read_text(encoding="utf-8"))

    def test_parse_vectors(self) -> None:
        for vector in self.vectors["parse"]:
            with self.subTest(name=vector["name"]):
                # A string arg goes through the JSON path; an object is used directly.
                plan = parse(vector["args"])
                self.assertEqual(_step_dict(plan.first), vector["first"])
                if "then_count" in vector:
                    self.assertEqual(len(plan.then), vector["then_count"])
                else:
                    self.assertEqual(
                        [_step_dict(step) for step in plan.then], vector["then"]
                    )
                self.assertEqual(plan.warnings, vector["warnings"])

    def test_resolve_vectors(self) -> None:
        state = self.vectors["resolve_state"]
        for vector in self.vectors["resolve"]:
            with self.subTest(name=vector["name"]):
                expect = vector["expect"]
                resolution = resolve(
                    _plan_step(vector["step"]),
                    state,
                    require_cid=vector.get("require_cid", False),
                )
                self.assertEqual(resolution.stop_reason, expect["stop_reason"])
                self.assertEqual(resolution.ok, expect["stop_reason"] is None)
                if resolution.ok:
                    self.assertEqual(resolution.card_index, expect.get("card_index"))
                    self.assertEqual(
                        resolution.target_index, expect.get("target_index")
                    )
                    self.assertEqual(
                        resolution.option_index, expect.get("option_index")
                    )

    def test_diverged_vectors(self) -> None:
        for vector in self.vectors["diverged"]:
            with self.subTest(name=vector["name"]):
                # plan_time/current may be raw JSON strings; they go in as-is.
                self.assertEqual(
                    diverged(vector["plan_time"], vector["current"]),
                    vector["expect"],
                )


if __name__ == "__main__":
    unittest.main()
