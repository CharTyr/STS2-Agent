"""Machine-specific values must not live in a script that runs on someone else's machine.

Two validation scripts used to carry one: the budget proxy wrote its last-completion dump to an
absolute path under the author's own checkout (which also meant the dump failed silently on any
other machine, because the call site swallows the error), and the coop acceptance script protected
a hardcoded SteamID64. Both are now inputs or detected. These tests read the sources, because
neither script is part of the offline compile.

The property checks are functions over source text so the destructive cases can hand them the code
they replaced and watch them fail, instead of asserting against a string the test built itself.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COOP_PATH = ROOT / "scripts" / "sts2-coop-full-run-acceptance.ps1"
PROXY_PATH = ROOT / "scripts" / "sts2-model-budget-proxy.py"
STEAM_ID64 = "76561198420578597"

_SEP = re.escape(chr(92))
# Any drive-absolute path into a user profile. Matching the shape rather than one spelling is what
# makes the guard catch the form the code actually had, instead of only a needle the test made up.
ABSOLUTE_USER_PATH = re.compile("[A-Za-z]:" + _SEP + "(?:Users|home|Documents)" + _SEP)


def assert_proxy_dump_target_is_an_option(test: unittest.TestCase, source: str) -> None:
    test.assertIsNone(
        ABSOLUTE_USER_PATH.search(source),
        "no absolute user path may reappear in the proxy",
    )
    test.assertIn("--dump-last-completion", source)
    test.assertIn(
        'getattr(self.server, "dump_last_completion", None)',
        source,
        "an unset dump target must mean no write at all",
    )
    # A target whose parent does not exist yet is the shape that failed silently before.
    test.assertIn("path.parent.mkdir(parents=True, exist_ok=True)", source)


def assert_coop_does_not_pin_a_steam_account(test: unittest.TestCase, source: str) -> None:
    test.assertIn("$SteamAccountId", source, "the run has to be able to name its profile")
    test.assertNotIn(
        STEAM_ID64,
        source,
        "a Steam account id belongs to the machine that runs the validation, not to the repository",
    )
    # Detection, not a silent empty result: the operator has to be told how to resolve ambiguity.
    test.assertIn("Pass -SteamAccountId", source)


class ScriptPortabilityTests(unittest.TestCase):
    def test_budget_proxy_dump_target_is_an_option(self) -> None:
        assert_proxy_dump_target_is_an_option(self, PROXY_PATH.read_text(encoding="utf-8"))

    def test_coop_acceptance_does_not_pin_a_steam_account(self) -> None:
        assert_coop_does_not_pin_a_steam_account(self, COOP_PATH.read_text(encoding="utf-8"))

    def test_the_acceptance_run_still_asks_the_proxy_for_the_dump(self) -> None:
        """The option is only useful if the run that needs the dump passes it."""
        coop = COOP_PATH.read_text(encoding="utf-8")
        self.assertIn("--dump-last-completion", coop)
        self.assertIn("Join-Path $Evidence 'last-llm.json'", coop)


class ScriptPortabilityDestructiveTests(unittest.TestCase):
    def test_the_check_catches_a_hardcoded_steam_account(self) -> None:
        """Pinning the id as the param default is the regression the guard has to catch."""
        source = COOP_PATH.read_text(encoding="utf-8")
        reverted = source.replace(
            "$SteamAccountId = '',",
            "$SteamAccountId = '" + STEAM_ID64 + "',",
        )
        self.assertNotEqual(reverted, source, "the fixture has to change the param default")
        self.assertIn(STEAM_ID64, reverted)
        with self.assertRaises(AssertionError):
            assert_coop_does_not_pin_a_steam_account(self, reverted)

    def test_the_check_catches_a_hardcoded_dump_path(self) -> None:
        """An absolute dump path is the regression the guard has to catch."""
        source = PROXY_PATH.read_text(encoding="utf-8")
        hardcoded = "Path(r" + '"' + "C:" + chr(92) + "Users" + chr(92) + "someone" + chr(92) + 'checkout")'
        reverted = source.replace(
            'getattr(self.server, "dump_last_completion", None)',
            hardcoded,
        )
        self.assertNotEqual(reverted, source, "the fixture has to change something")
        with self.assertRaises(AssertionError):
            assert_proxy_dump_target_is_an_option(self, reverted)


if __name__ == "__main__":
    unittest.main()

