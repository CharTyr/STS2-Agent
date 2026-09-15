from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
VALIDATION_SCRIPT = REPO_ROOT / "scripts" / "run_sts2_validation.py"


def load_validation_module() -> Any:
    spec = importlib.util.spec_from_file_location("sts2_validation_under_test", VALIDATION_SCRIPT)
    if spec is None or spec.loader is None:  # pragma: no cover - path is fixed in the repo
        raise RuntimeError(f"cannot import {VALIDATION_SCRIPT}")
    module = importlib.util.module_from_spec(spec)
    # Dataclass fields resolve their module through sys.modules, so the module has to be
    # registered before the script body runs.
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class LocalCombatActionWindowTests(unittest.TestCase):
    """state-invariants must demand play_card exactly where the executor accepts it.

    The check read the hand's playable flags and then required play_card whenever the local
    player's turn was active. A snapshot taken while a played card is still resolving satisfies
    that turn predicate but has no usable play surface, so the invariant reported a missing
    action that was never expected. The gate now follows the executor's whole readiness chain.
    """

    @classmethod
    def setUpClass(cls) -> None:
        cls.validation = load_validation_module()

    def window(self, readiness: Any) -> bool:
        return bool(self.validation.is_local_combat_action_window({"action_readiness": readiness}))

    def test_ready_chain_is_what_demands_play_card(self) -> None:
        self.assertTrue(
            self.window({"can_use_combat_actions": True, "player_action_phase": True})
        )

    def test_mid_resolution_snapshot_does_not_demand_play_card(self) -> None:
        # Player turn still active, action table not rebuilt yet -- the false-failure case.
        self.assertFalse(
            self.window({"can_use_combat_actions": False, "player_action_phase": True})
        )

    def test_readiness_beats_the_narrower_turn_predicate(self) -> None:
        # Precedence, not "either": a ready surface is decisive even if the narrower flag disagrees.
        self.assertTrue(
            self.window({"can_use_combat_actions": True, "player_action_phase": False})
        )

    def test_payloads_without_the_ready_flag_keep_the_old_predicate(self) -> None:
        self.assertTrue(self.window({"player_action_phase": True}))
        self.assertFalse(self.window({"player_action_phase": False}))

    def test_missing_readiness_still_demands_play_card(self) -> None:
        self.assertTrue(self.window({}))
        self.assertTrue(self.window(None))
        self.assertTrue(bool(self.validation.is_local_combat_action_window(None)))


if __name__ == "__main__":
    unittest.main()
