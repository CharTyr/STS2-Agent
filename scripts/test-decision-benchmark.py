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

EVAL_MODULE_PATH = ROOT / "scripts" / "run_decision_benchmark_eval.py"
EVAL_SPEC = importlib.util.spec_from_file_location("sts2_decision_benchmark_eval_test", EVAL_MODULE_PATH)
assert EVAL_SPEC is not None and EVAL_SPEC.loader is not None
eval_runner = importlib.util.module_from_spec(EVAL_SPEC)
sys.modules[EVAL_SPEC.name] = eval_runner
EVAL_SPEC.loader.exec_module(eval_runner)

SUITE_PATH = ROOT / "docs" / "decision-benchmark.json"
ANSWER_PATH = ROOT / "docs" / "decision-benchmark.answers.example.json"


class DecisionBenchmarkTests(unittest.TestCase):
    def setUp(self) -> None:
        self.suite = benchmark.load_suite(SUITE_PATH)
        self.answers = json.loads(ANSWER_PATH.read_text(encoding="utf-8"))

    def test_suite_has_twenty_four_cross_screen_cases_and_source_evidence(self) -> None:
        self.assertEqual(len(self.suite.cases), 24)
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
        self.assertEqual(report["answered_cases"], 24)
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
        for answer in answers["answers"]:
            if answer["case_id"] == "combat-legal-target-index":
                answer["target_index"] = 0
        report = benchmark.score_suite(self.suite, answers)
        target_case = next(item for item in report["cases"] if item["case_id"] == "combat-legal-target-index")
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

    def test_arithmetic_constraints_reject_a_missed_kill_and_damage(self) -> None:
        answers = copy.deepcopy(self.answers)
        for answer in answers["answers"]:
            if answer["case_id"] == "math-exact-lethal":
                answer["target_index"] = 1
            if answer["case_id"] == "math-block-exact":
                answer["action"] = "end_turn"
        report = benchmark.score_suite(self.suite, answers)
        cases = {case["case_id"]: case for case in report["cases"]}
        self.assertTrue(any(not row["passed"] for row in cases["math-exact-lethal"]["constraints"]))
        self.assertTrue(any(not row["passed"] for row in cases["math-block-exact"]["constraints"]))
        self.assertLess(report["score_percent"], 100.0)

    def test_cli_validates_without_model_game_or_network(self) -> None:
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            exit_code = benchmark.main(["--suite", str(SUITE_PATH)])
        self.assertEqual(exit_code, 0)
        self.assertIn("24 cases validated; no model/game run requested", stdout.getvalue())

    def test_eval_runner_dry_run_makes_zero_network_calls(self) -> None:
        report = eval_runner.evaluate_suite(self.suite, model="dry-model", dry_run=True)
        self.assertTrue(report["dry_run"])
        self.assertEqual(report["requests_sent"], 0)
        self.assertEqual(len(report["telemetry"]), 24)
        self.assertTrue(all(row["status"] == "dry_run" and row["prompt_chars"] > 100 for row in report["telemetry"]))

    def test_eval_runner_refuses_live_api_without_explicit_flag(self) -> None:
        with self.assertRaisesRegex(ValueError, "--allow-live-api"):
            eval_runner.evaluate_suite(self.suite, model="gpt-4o", base_url="https://api.example/v1")

    def test_eval_runner_enforces_request_budget_parses_tool_and_json_and_compares(self) -> None:
        ref_by_id = {a["case_id"]: a for a in self.answers["answers"]}

        def fake_model_a(case_id: str, _messages: list[dict[str, str]]) -> dict:
            ref = ref_by_id[case_id]
            args: dict = {"action": ref["action"], "reason": "from tool call"}
            if "option_index" in ref:
                if ref["action"] == "play_card":
                    args["card_index"] = ref["option_index"]
                else:
                    args["option_index"] = ref["option_index"]
            if "target_index" in ref:
                args["target_index"] = ref["target_index"]
            return {
                "choices": [
                    {
                        "message": {
                            "tool_calls": [{"function": {"name": "act", "arguments": json.dumps(args)}}]
                        }
                    }
                ],
                "usage": {"prompt_tokens": 500, "completion_tokens": 40},
            }

        def fake_model_b(case_id: str, _messages: list[dict[str, str]]) -> dict:
            if case_id == "combat-sandpit-counter-one":
                # Plain JSON fallback with a bad decision to trigger disagreement.
                return {"choices": [{"message": {"content": '{"action":"end_turn"}'}}], "usage": {"prompt_tokens": 400, "completion_tokens": 12}}
            return fake_model_a(case_id, _messages)

        capped = eval_runner.evaluate_suite(self.suite, model="capped-model", max_requests=3, requester=fake_model_a)
        self.assertEqual(capped["requests_sent"], 3)
        self.assertTrue(capped["budget_stopped"])
        self.assertEqual(capped["score"]["answered_cases"], 3)

        report_a = eval_runner.evaluate_suite(self.suite, model="model-a", max_requests=30, requester=fake_model_a)
        report_b = eval_runner.evaluate_suite(self.suite, model="model-b", max_requests=30, requester=fake_model_b)
        self.assertEqual(report_a["score"]["score_percent"], 100.0)
        self.assertLess(report_b["score"]["score_percent"], 100.0)

        cmp = eval_runner.compare_reports([report_b, report_a])
        self.assertEqual(cmp["models"][0]["model"], "model-a")
        self.assertEqual([d["case_id"] for d in cmp["disagreements"]], ["combat-sandpit-counter-one"])
        md = eval_runner.render_comparison_markdown(cmp)
        self.assertIn("combat-sandpit-counter-one", md)
        self.assertIn("model-a", md)

    def test_eval_runner_redacts_api_key_on_error(self) -> None:
        secret = "sk-super-secret-token-999"

        def boom(_case_id: str, _messages: list[dict[str, str]]) -> dict:
            raise RuntimeError(f"gateway echoed {secret}")

        report = eval_runner.evaluate_suite(self.suite, model="err-model", api_key=secret, max_requests=1, requester=boom)
        dumped = json.dumps(report)
        self.assertNotIn(secret, dumped)
        self.assertIn("[REDACTED]", dumped)


if __name__ == "__main__":
    unittest.main(verbosity=2)
