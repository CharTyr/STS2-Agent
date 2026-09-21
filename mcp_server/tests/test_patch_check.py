from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
VALIDATION_SCRIPT = REPO_ROOT / "scripts" / "run_sts2_validation.py"


def load_validation_module() -> Any:
    spec = importlib.util.spec_from_file_location("sts2_validation_patch_check", VALIDATION_SCRIPT)
    if spec is None or spec.loader is None:  # pragma: no cover - path is fixed in the repo
        raise RuntimeError(f"cannot import {VALIDATION_SCRIPT}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class FakeClient:
    def __init__(self, screen: str, descriptors: list[dict[str, Any]]) -> None:
        self.screen = screen
        self.descriptors = descriptors

    def get_state(self) -> dict[str, Any]:
        return {"screen": self.screen}

    def get_available_actions(self) -> list[dict[str, Any]]:
        return self.descriptors


def descriptor(name: str, **flags: Any) -> dict[str, Any]:
    payload = {
        "name": name,
        "requires_index": False,
        "requires_target": False,
        "requires_coordinates": False,
        "requires_tool": False,
    }
    payload.update(flags)
    return payload


class ActionSurfaceBaselineReplayTests(unittest.TestCase):
    """The patch-check replay must compare call shape, not run state.

    A baseline sample is taken in whatever run state the game was in, so its action *set* moves
    with the player's save: an active run adds actions to MAIN_MENU. Treating a missing action as a
    regression would fail every patch check on a different save, so only a flag that disagrees for
    an action present on both sides is allowed to fail.
    """

    @classmethod
    def setUpClass(cls) -> None:
        cls.validation = load_validation_module()

    def setUp(self) -> None:
        self._directory = tempfile.TemporaryDirectory()
        self.addCleanup(self._directory.cleanup)
        self.baseline_path = Path(self._directory.name) / "action-surface-baseline.jsonl"

    def write_baseline(self, records: list[dict[str, Any]]) -> None:
        self.baseline_path.write_text(
            "\n".join(json.dumps(record) for record in records) + "\n", encoding="utf-8"
        )

    def test_identical_surface_replays_without_mismatch(self) -> None:
        self.write_baseline(
            [
                {
                    "screen": "COMBAT",
                    "descriptors": [descriptor("play_card", requires_index=True, requires_target=True)],
                }
            ]
        )
        client = FakeClient("COMBAT", [descriptor("play_card", requires_index=True, requires_target=True)])

        replay = self.validation.replay_action_surface_baseline(client, self.baseline_path)

        self.assertTrue(replay["baseline_screen_present"])
        self.assertEqual(replay["mismatches"], [])
        self.assertEqual(replay["compared_actions"], ["play_card"])

    def test_changed_call_shape_is_a_mismatch(self) -> None:
        self.write_baseline(
            [{"screen": "COMBAT", "descriptors": [descriptor("play_card", requires_index=True)]}]
        )
        client = FakeClient("COMBAT", [descriptor("play_card", requires_index=False)])

        replay = self.validation.replay_action_surface_baseline(client, self.baseline_path)

        self.assertEqual(
            replay["mismatches"],
            [{"action": "play_card", "flag": "requires_index", "baseline": True, "live": False}],
        )

    def test_different_run_state_is_reported_not_failed(self) -> None:
        self.write_baseline(
            [
                {
                    "screen": "MAIN_MENU",
                    "descriptors": [descriptor("open_character_select"), descriptor("continue_run")],
                }
            ]
        )
        client = FakeClient("MAIN_MENU", [descriptor("open_character_select"), descriptor("switch_profile")])

        replay = self.validation.replay_action_surface_baseline(client, self.baseline_path)

        self.assertEqual(replay["mismatches"], [])
        self.assertEqual(replay["only_in_state"], ["switch_profile"])
        self.assertEqual(replay["only_in_baseline"], ["continue_run"])

    def test_screen_without_a_sample_is_not_a_pass(self) -> None:
        self.write_baseline([{"screen": "COMBAT", "descriptors": [descriptor("play_card")]}])
        client = FakeClient("SHOP", [descriptor("buy_card")])

        replay = self.validation.replay_action_surface_baseline(client, self.baseline_path)

        self.assertFalse(replay["baseline_screen_present"])
        self.assertFalse(replay["comparable"])
        self.assertIn("must not be reported as a pass", replay["note"])

    def test_same_screen_with_zero_shared_actions_is_not_a_pass(self) -> None:
        self.write_baseline(
            [{"screen": "COMBAT", "descriptors": [descriptor("old_play_card")]}]
        )
        client = FakeClient("COMBAT", [descriptor("renamed_play_card")])

        replay = self.validation.replay_action_surface_baseline(client, self.baseline_path)

        self.assertTrue(replay["baseline_screen_present"])
        self.assertFalse(replay["comparable"])
        self.assertEqual(replay["compared_actions"], [])
        self.assertEqual(replay["mismatches"], [])
        self.assertIn("must not be reported as a pass", replay["note"])

    def test_a_flag_unstable_across_samples_is_not_used_as_a_contract(self) -> None:
        self.write_baseline(
            [
                {"screen": "COMBAT", "descriptors": [descriptor("play_card", requires_target=False)]},
                {"screen": "COMBAT", "descriptors": [descriptor("play_card", requires_target=True)]},
            ]
        )
        client = FakeClient("COMBAT", [descriptor("play_card", requires_target=True)])

        replay = self.validation.replay_action_surface_baseline(client, self.baseline_path)

        self.assertEqual(replay["mismatches"], [])

    def test_malformed_lines_are_skipped_rather_than_fatal(self) -> None:
        self.baseline_path.write_text(
            "not json\n"
            + json.dumps({"screen": "COMBAT", "descriptors": [descriptor("play_card")]})
            + "\n"
            + json.dumps({"screen": "COMBAT"})
            + "\n",
            encoding="utf-8",
        )
        client = FakeClient("COMBAT", [descriptor("play_card")])

        replay = self.validation.replay_action_surface_baseline(client, self.baseline_path)

        self.assertTrue(replay["baseline_screen_present"])
        self.assertEqual(replay["compared_actions"], ["play_card"])

    def test_the_subcommand_is_registered(self) -> None:
        parser = self.validation.build_parser()
        args = parser.parse_args(["patch-check", "--base-url", "http://127.0.0.1:9999"])

        self.assertEqual(args.func, self.validation.suite_patch_check)
        self.assertEqual(args.base_url, "http://127.0.0.1:9999")
        self.assertIsNone(args.baseline)


if __name__ == "__main__":
    unittest.main()
