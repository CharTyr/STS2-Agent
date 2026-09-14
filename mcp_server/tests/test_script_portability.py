"""Machine-specific values must not live in a script that runs on someone else's machine.

Two validation scripts used to carry one: the budget proxy wrote its last-completion dump to an
absolute path under the author's own checkout (which also meant the dump failed silently on any
other machine, because the call site swallows the error), and the coop acceptance script protected
a hardcoded SteamID64. Both are now inputs or detected. These tests read the sources, because
neither script is part of the offline compile.

The property checks are functions over source text so the destructive cases can hand them the code
they replaced and watch them fail, instead of asserting against a string the test built itself.

They are source-level tripwires, not proofs: they pin the shapes the scripts have to have, and a
rewrite that keeps every needle while changing what the code does would still pass. Behavior is
covered by the live check the operations spec lists next to the resolver, not here.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COOP_PATH = ROOT / "scripts" / "sts2-coop-full-run-acceptance.ps1"
PROXY_PATH = ROOT / "scripts" / "sts2-model-budget-proxy.py"
RESOLVER_PATH = ROOT / "scripts" / "lib-sts2-paths.ps1"
GAME_SCRIPTS = (
    "build-mod.ps1",
    "start-game-session.ps1",
    "test-mod-load.ps1",
    "test-debug-console-gating.ps1",
)
STEAM_ID64 = "76561198420578597"

_SEP = re.escape(chr(92))
# Any drive-absolute path into a user profile. Matching the shape rather than one spelling is what
# makes the guard catch the form the code actually had, instead of only a needle the test made up.
ABSOLUTE_USER_PATH = re.compile("[A-Za-z]:" + _SEP + "(?:Users|home|Documents)" + _SEP)

# A drive-absolute path that is not the "p:/" inside a URL; the lookbehind keeps schemes out. The
# game may sit on any drive, so no drive-lettered path belongs in the four scripts.
ABSOLUTE_DRIVE_PATH = re.compile("(?<![A-Za-z])[A-Za-z]:(?:" + re.escape(chr(92)) + "|/)")

# Words that only show up when a script knows the shape of a Steam install. Once the resolver owns
# that knowledge, neither belongs in the four scripts.
INSTALL_TOKENS = ("Program Files", "steamapps")

# Resolver function -> the environment variable it has to consult after the explicit argument.
RESOLVER_FUNCTIONS = (
    ("Resolve-Sts2GameRoot", "$env:STS2_GAME_ROOT"),
    ("Resolve-Sts2Executable", "$env:STS2_EXE_PATH"),
    ("Resolve-Sts2AppManifest", "$env:STS2_APP_MANIFEST"),
    ("Get-Sts2SteamExe", "$env:STS2_STEAM_EXE"),
)


def function_body(source: str, name: str) -> str:
    """One top-level PowerShell function, up to the closing brace in column zero."""
    start = source.index("function " + name + " {")
    return source[start : source.index(chr(10) + "}" + chr(10), start)]


def uncommented_lines(source: str) -> list[str]:
    """The lines a reader would run, with indentation stripped."""
    return [
        line.strip()
        for line in source.splitlines()
        if line.strip() and not line.strip().startswith("#")
    ]


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


def assert_script_uses_the_shared_resolver(test: unittest.TestCase, name: str, source: str) -> None:
    for token in INSTALL_TOKENS:
        test.assertNotIn(
            token,
            source,
            name
            + " must not carry an install location of its own ("
            + token
            + "); the resolver owns that knowledge",
        )
    test.assertIsNone(
        ABSOLUTE_DRIVE_PATH.search(source),
        name + " must not hardcode a drive-absolute path either",
    )
    # The file name alone is not the contract: a line that only mentions it, an commented-out copy
    # of the real line, or the name kept in a comment would all satisfy a plain substring check.
    test.assertTrue(
        [line for line in uncommented_lines(source) if "lib-sts2-paths.ps1" in line],
        name + " has to dot-source the shared resolver, not merely mention it",
    )
    test.assertIn("Resolve-Sts2", source, name + " has to resolve the path it needs")


def assert_resolver_has_the_whole_chain(test: unittest.TestCase, lib: str) -> None:
    for variable in ("STS2_GAME_ROOT", "STS2_EXE_PATH", "STS2_APP_MANIFEST", "STS2_STEAM_EXE"):
        test.assertIn("$env:" + variable, lib, variable + " is part of the documented contract")
    for function, variable in RESOLVER_FUNCTIONS:
        body = function_body(lib, function)
        test.assertIn(variable, body, function + " has to read " + variable)
        test.assertIn(
            "@($Explicit, " + variable + ")",
            body,
            function + " has to consult the explicit argument before " + variable,
        )
    test.assertIn(
        "libraryfolders.vdf",
        lib,
        "a game in a second Steam library has to be found by detection, not by editing this file",
    )
    test.assertIn(
        "[string]::IsNullOrEmpty($text)",
        lib,
        "Steam rewrites libraryfolders.vdf in place; a truncated read must not throw in the resolver",
    )
    test.assertEqual(
        lib.count("C:/Program Files (x86)/Steam"),
        1,
        "the conventional path is a last resort, so it belongs in exactly one place",
    )


def assert_build_mod_guards_the_install_target(test: unittest.TestCase, source: str) -> None:
    guarded = [
        line
        for line in uncommented_lines(source)
        if "-not $SkipInstall" in line and "Test-Path -LiteralPath $GameRoot" in line
    ]
    test.assertTrue(
        guarded,
        "New-Item -Force used to absorb a wrong path and create an empty tree instead",
    )


def assert_debug_gating_takes_its_port(test: unittest.TestCase, source: str) -> None:
    test.assertIn("[int]$ApiPort = 8080", source)
    test.assertNotIn("127.0.0.1:8080", source, "the port has to come from the parameter")
    test.assertIn(
        '$startInfo.EnvironmentVariables["STS2_API_PORT"]',
        source,
        "the mod reads its port from this variable, so the parameter has to be passed on",
    )


class GamePathResolutionTests(unittest.TestCase):
    def test_every_game_script_resolves_instead_of_assuming(self) -> None:
        for name in GAME_SCRIPTS:
            with self.subTest(script=name):
                assert_script_uses_the_shared_resolver(
                    self, name, (ROOT / "scripts" / name).read_text(encoding="utf-8")
                )

    def test_the_resolver_offers_argument_env_detection_then_default(self) -> None:
        assert_resolver_has_the_whole_chain(self, RESOLVER_PATH.read_text(encoding="utf-8"))

    def test_build_mod_refuses_an_install_path_that_is_not_there(self) -> None:
        assert_build_mod_guards_the_install_target(
            self, (ROOT / "scripts" / "build-mod.ps1").read_text(encoding="utf-8")
        )

    def test_debug_gating_script_takes_its_port(self) -> None:
        assert_debug_gating_takes_its_port(
            self,
            (ROOT / "scripts" / "test-debug-console-gating.ps1").read_text(encoding="utf-8"),
        )


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
    def test_the_check_catches_a_script_that_hardcodes_the_install_path(self) -> None:
        """Putting the vendor default back into one of the scripts has to be rejected."""
        name = "build-mod.ps1"
        source = (ROOT / "scripts" / name).read_text(encoding="utf-8")
        reverted = source.replace(
            '[string]$GameRoot = "",',
            '[string]$GameRoot = "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2",',
        )
        self.assertNotEqual(reverted, source, "the fixture has to change the param default")
        with self.assertRaises(AssertionError):
            assert_script_uses_the_shared_resolver(self, name, reverted)

    def test_the_check_catches_a_resolver_with_no_detection(self) -> None:
        lib = RESOLVER_PATH.read_text(encoding="utf-8")
        reverted = lib.replace("libraryfolders.vdf", "unused")
        self.assertNotEqual(reverted, lib, "the fixture has to change something")
        with self.assertRaises(AssertionError):
            assert_resolver_has_the_whole_chain(self, reverted)

    def test_the_check_catches_a_hardcoded_api_port(self) -> None:
        source = (ROOT / "scripts" / "test-debug-console-gating.ps1").read_text(encoding="utf-8")
        reverted = source.replace("[int]$ApiPort = 8080,", "").replace("$ApiPort/", "8080/")
        self.assertNotEqual(reverted, source, "the fixture has to change something")
        self.assertIn("127.0.0.1:8080", reverted)
        with self.assertRaises(AssertionError):
            assert_debug_gating_takes_its_port(self, reverted)

    def test_the_check_catches_a_second_drive_literal(self) -> None:
        """A game installed on another drive is the shape the guard has to catch, not just C:."""
        name = "build-mod.ps1"
        source = (ROOT / "scripts" / name).read_text(encoding="utf-8")
        reverted = source.replace(
            '[string]$GameRoot = "",',
            '[string]$GameRoot = "D:/SteamLibrary/steamapps/common/Slay the Spire 2",',
        )
        self.assertNotEqual(reverted, source, "the fixture has to change the param default")
        with self.assertRaises(AssertionError):
            assert_script_uses_the_shared_resolver(self, name, reverted)

    def test_the_check_catches_a_mention_instead_of_a_dot_source(self) -> None:
        """Commenting the dot-source out while keeping the file name has to be rejected."""
        name = "test-mod-load.ps1"
        source = (ROOT / "scripts" / name).read_text(encoding="utf-8")
        reverted = source.replace(
            '. (Join-Path $PSScriptRoot "lib-sts2-paths.ps1")',
            '# . (Join-Path $PSScriptRoot "lib-sts2-paths.ps1")',
        )
        self.assertNotEqual(reverted, source, "the fixture has to comment the dot-source out")
        self.assertIn("lib-sts2-paths.ps1", reverted)
        with self.assertRaises(AssertionError):
            assert_script_uses_the_shared_resolver(self, name, reverted)

    def test_the_check_catches_an_environment_variable_that_outranks_the_argument(self) -> None:
        """The documented order is argument first; the guard has to pin the order, not the names."""
        lib = RESOLVER_PATH.read_text(encoding="utf-8")
        reverted = lib.replace(
            "@($Explicit, $env:STS2_GAME_ROOT)",
            "@($env:STS2_GAME_ROOT, $Explicit)",
        )
        self.assertNotEqual(reverted, lib, "the fixture has to swap the order")
        with self.assertRaises(AssertionError):
            assert_resolver_has_the_whole_chain(self, reverted)

    def test_the_check_catches_a_commented_out_install_guard(self) -> None:
        """Leaving the guard in place as a comment is the regression the guard has to catch."""
        source = (ROOT / "scripts" / "build-mod.ps1").read_text(encoding="utf-8")
        reverted = source.replace(
            "if (-not $SkipInstall -and -not (Test-Path -LiteralPath $GameRoot)) {",
            "# if (-not $SkipInstall -and -not (Test-Path -LiteralPath $GameRoot)) {",
        )
        self.assertNotEqual(reverted, source, "the fixture has to comment the guard out")
        self.assertIn("-not (Test-Path -LiteralPath $GameRoot)", reverted)
        with self.assertRaises(AssertionError):
            assert_build_mod_guards_the_install_target(self, reverted)

    def test_the_check_catches_a_resolver_without_the_truncated_read_guard(self) -> None:
        lib = RESOLVER_PATH.read_text(encoding="utf-8")
        reverted = lib.replace("[string]::IsNullOrEmpty($text)", "unused")
        self.assertNotEqual(reverted, lib, "the fixture has to remove the guard")
        with self.assertRaises(AssertionError):
            assert_resolver_has_the_whole_chain(self, reverted)

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

