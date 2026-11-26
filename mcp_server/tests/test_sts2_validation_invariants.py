from __future__ import annotations

import importlib.util
import hashlib
import json
import sys
import unittest
import zipfile
from argparse import Namespace
from io import BytesIO
from pathlib import Path
from typing import Any
from unittest.mock import Mock, patch

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


class ReleaseSmokeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.validation = load_validation_module()

    def test_identity_checks_versions_and_owned_pid(self) -> None:
        args = Namespace(expected_game_version="0.114.0", expected_mod_version="0.19.5", expected_process_id=42)
        expected = {"game_version": "v0.114.0", "mod_version": "0.19.5", "process_id": 42}
        self.validation.check_release_identity(expected, args)
        for key, wrong in (("game_version", "v0.113.1"), ("mod_version", "0.19.4"), ("process_id", 43)):
            with self.subTest(key=key), self.assertRaisesRegex(self.validation.ValidationError, key):
                self.validation.check_release_identity({**expected, key: wrong}, args)

    def test_upgrade_preview_requires_real_first_upgrade(self) -> None:
        client = Mock()
        card = {"id": "STRIKE_IRONCLAD", "upgrade": {"status": "available", "upgrade_level": 1}}
        client.request.return_value = {"data": [card]}
        self.assertEqual(self.validation.check_card_upgrade_preview(client), {"card": "STRIKE_IRONCLAD", "upgrade_level": 1})
        card["upgrade"] = {"status": "unavailable", "reason": "upgrade_preview_failed"}
        with self.assertRaisesRegex(self.validation.ValidationError, "upgrade_preview_failed"):
            self.validation.check_card_upgrade_preview(client)

    def test_native_mcp_fails_when_raw_tool_returns_error(self) -> None:
        def respond(req: Any, **_: Any) -> BytesIO:
            call = json.loads(req.data)
            if call["method"] == "initialize":
                result = {"protocolVersion": "2025-03-26"}
            else:
                self.assertEqual(req.get_header("Mcp-session-id"), "release-session")
                if call["method"] == "notifications/initialized":
                    return BytesIO()
                if call["params"]["name"] == "get_raw_game_state":
                    result = {"isError": True}
                else:
                    result = {"content": [{"text": '{"screen":"MAIN_MENU"}'}]}
            response = BytesIO(json.dumps({"id": call["id"], "result": result}).encode())
            response.headers = {"Mcp-Session-Id": "release-session"}
            return response

        client = self.validation.ApiClient()
        with patch.object(self.validation.request, "urlopen", side_effect=respond):
            with self.assertRaisesRegex(self.validation.ValidationError, "get_raw_game_state"):
                self.validation.check_native_mcp_state(client, "MAIN_MENU")

    def test_packaged_dll_must_match_the_live_tested_bytes(self) -> None:
        path = REPO_ROOT / "scripts" / "check_release_package.py"
        spec = importlib.util.spec_from_file_location("release_package_under_test", path)
        self.assertIsNotNone(spec)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        tested_dll = b"tested DLL"
        artifact = BytesIO()
        with zipfile.ZipFile(artifact, "w") as archive:
            archive.writestr("mod/STS2AIAgent.dll", tested_dll)
        with zipfile.ZipFile(artifact) as archive:
            reader = module._ZipReader(archive)
            module._check_release_dll(reader, hashlib.sha256(tested_dll).hexdigest().upper())
            with self.assertRaisesRegex(module.PackageCheckError, "differs from the tested DLL"):
                module._check_release_dll(reader, hashlib.sha256(b"other DLL").hexdigest())


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
