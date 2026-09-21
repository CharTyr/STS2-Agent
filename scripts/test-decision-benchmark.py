#!/usr/bin/env python3
"""No-network contracts for the decision-quality benchmark deep module."""
from __future__ import annotations

import contextlib
import copy
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parent.parent
MODULE_PATH = ROOT / "scripts" / "decision_benchmark.py"
SPEC = importlib.util.spec_from_file_location("sts2_decision_benchmark_test", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
benchmark = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = benchmark
SPEC.loader.exec_module(benchmark)

SUITE_PATH = ROOT / "docs" / "decision-benchmark.json"
ANSWER_PATH = ROOT / "docs" / "decision-benchmark.answers.example.json"


class DecisionBenchmarkTests(unittest.TestCase):
    def setUp(self) -> None:
        self.suite = benchmark.load_suite(SUITE_PATH)
        self.answers = json.loads(ANSWER_PATH.read_text(encoding="utf-8"))

    def test_suite_has_eleven_cross_screen_cases_and_source_evidence(self) -> None:
        self.assertEqual(len(self.suite.cases), 11)
        self.assertEqual(
            {case.kind for case in self.suite.cases},
            {"combat", "event", "map", "rest", "shop", "reward"},
        )
        for case in self.suite.cases:
            self.assertEqual(case.state["screen"], case.kind.upper())
            self.assertTrue(case.evidence)
            self.assertIn("available_actions", case.state)

    def test_reference_answers_score_all_recorded_constraints(self) -> None:
        report = benchmark.score_suite(self.suite, self.answers)
        self.assertEqual(report["answered_cases"], 11)
        self.assertEqual(report["earned_points"], report["available_points"])
        self.assertEqual(report["score_percent"], 100.0)
        self.assertIn("no claim of simulated combat outcome", report["scoring_scope"])
        self.assertEqual(json.loads(benchmark.render_report(report)), report)

    def test_missing_answers_are_denominator_zeroes_not_silently_removed(self) -> None:
        report = benchmark.score_suite(self.suite, {"answers": []})
        self.assertEqual(report["answered_cases"], 0)
        self.assertEqual(report["earned_points"], 0.0)
        self.assertEqual(report["score_percent"], 0.0)
        self.assertTrue(all(not case["answered"] for case in report["cases"]))

    def test_wrong_action_loses_the_relevant_constraints_only(self) -> None:
        answers = copy.deepcopy(self.answers)
        answers["answers"][0] = {
            "case_id": "combat-lethal-before-end-turn",
            "action": "end_turn",
        }
        report = benchmark.score_suite(self.suite, answers)
        first = report["cases"][0]
        by_id = {constraint["id"]: constraint for constraint in first["constraints"]}
        self.assertTrue(by_id["fixture-is-actionable"]["passed"])
        self.assertFalse(by_id["do-not-end-turn-into-lethal"]["passed"])
        self.assertFalse(by_id["use-a-combat-action"]["passed"])
        self.assertLess(report["score_percent"], 100.0)

    def test_target_action_scores_the_real_target_index(self) -> None:
        answers = copy.deepcopy(self.answers)
        answers["answers"][-1]["target_index"] = 0
        report = benchmark.score_suite(self.suite, answers)
        target_case = report["cases"][-1]
        target_constraint = next(item for item in target_case["constraints"] if item["id"] == "select-only-legal-enemy")
        self.assertFalse(target_constraint["passed"])
        self.assertLess(report["score_percent"], 100.0)

    def test_loader_rejects_an_action_or_index_not_exposed_by_the_snapshot(self) -> None:
        raw = json.loads(SUITE_PATH.read_text(encoding="utf-8"))
        raw["cases"][4]["expectations"][1]["option_index"] = 99
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "illegal-action.json"
            path.write_text(json.dumps(raw), encoding="utf-8")
            with self.assertRaisesRegex(benchmark.BenchmarkError, "absent from the captured state"):
                benchmark.load_suite(path)

    def test_loader_rejects_an_expectation_contradicted_by_its_state(self) -> None:
        raw = json.loads(SUITE_PATH.read_text(encoding="utf-8"))
        raw["cases"][0]["expectations"][0]["equals"] = False
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "contradiction.json"
            path.write_text(json.dumps(raw), encoding="utf-8")
            with self.assertRaisesRegex(benchmark.BenchmarkError, "but its snapshot has"):
                benchmark.load_suite(path)

    def test_loader_rejects_unsupported_contract_surface(self) -> None:
        raw = json.loads(SUITE_PATH.read_text(encoding="utf-8"))
        raw["cases"][0]["model_should_win"] = True
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "unsupported.json"
            path.write_text(json.dumps(raw), encoding="utf-8")
            with self.assertRaisesRegex(benchmark.BenchmarkError, "unsupported field"):
                benchmark.load_suite(path)

    def test_capture_reads_one_explicit_state_and_never_submits_an_action(self) -> None:
        class Response:
            def read(self) -> bytes:
                return b'{"ok":true,"data":{"screen":"MAP","available_actions":["choose_map_node"]}}'

            def __enter__(self) -> "Response":
                return self

            def __exit__(self, *args: object) -> None:
                return None

        with patch.object(benchmark.request, "urlopen", return_value=Response()) as urlopen:
            capture = benchmark.capture_template(self.suite, "http://127.0.0.1:8080/")

        self.assertEqual(capture["base_url"], "http://127.0.0.1:8080")
        self.assertEqual(capture["state"]["screen"], "MAP")
        self.assertIn("no model/API call and no game action", capture["capture_scope"])
        self.assertTrue(urlopen.call_args.args[0].full_url.endswith("/state"))
        self.assertEqual(urlopen.call_count, 1)

    def test_cli_validates_without_model_game_or_network(self) -> None:
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            exit_code = benchmark.main(["--suite", str(SUITE_PATH)])
        self.assertEqual(exit_code, 0)
        self.assertIn("11 cases validated; no model/game run requested", stdout.getvalue())


if __name__ == "__main__":
    unittest.main(verbosity=2)
