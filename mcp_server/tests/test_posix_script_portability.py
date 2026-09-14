"""The POSIX scripts must resolve the game location instead of assuming it.

The Windows side of this guard lives in test_script_portability.py. This is its mirror for the
bash half of the repository, which has no compiler and no test runner of its own: reading the
sources is the only thing that works on a machine without the game.

Two of these checks are the reason the file exists. The install layout tokens (steamapps/common
and the .app bundle names) belong to scripts/lib-sts2-paths.sh alone -- a second copy is how the
scripts drifted apart in the first place. And the offline test that actually exercises the
resolver has to be wired into a gate: an unrunnable test is worse than no test, because it looks
like coverage.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
RESOLVER_PATH = SCRIPTS / "lib-sts2-paths.sh"
OFFLINE_TEST_PATH = SCRIPTS / "test-lib-sts2-paths.sh"
GATES_PATH = SCRIPTS / "check_verification_gates.py"
VALIDATE_WORKFLOW = ROOT / ".github" / "workflows" / "validate.yml"

# What a script has to name in order to be looking for the game itself. Any of these in a script
# other than the resolver means the install layout has been copied again.
INSTALL_TOKENS = ("steamapps/common", "Slay the Spire 2.app", "SlayTheSpire2.app")

# Resolver function -> the variable it has to consult after the explicit argument.
RESOLVER_FUNCTIONS = (
    ("sts2_resolve_game_root", "$" + "{STS2_GAME_ROOT:-}"),
    ("sts2_resolve_app_manifest", "$" + "{STS2_APP_MANIFEST:-}"),
)

CONVENTIONAL_ROOTS = (
    "Library/Application Support/Steam",
    ".steam/steam",
    ".local/share/Steam",
)

# Two files are allowed to spell out an install layout: the resolver, which owns it, and the
# offline test, which has to build fixture install trees to exercise the resolver at all.
KNOWS_THE_LAYOUT = (RESOLVER_PATH.name, OFFLINE_TEST_PATH.name)

# The resolver asks "is this a real value" with sts2_has_text, because Windows answers that same
# question with [string]::IsNullOrWhiteSpace. Whichever spelling is used, the argument has to be
# tested before the variable -- that is the part this guard pins.
EXPLICIT_TEST = 'sts2_has_text "$explicit"'


def runnable_lines(source: str) -> list:
    """The lines a reader would run: comments and blanks dropped, indentation stripped."""
    return [
        line.strip()
        for line in source.splitlines()
        if line.strip() and not line.strip().startswith("#")
    ]


def function_body(source: str, name: str) -> str:
    """One top-level bash function, up to the closing brace in column zero."""
    start = source.index(name + "() {")
    return source[start : source.index(chr(10) + "}" + chr(10), start)]


def assert_install_knowledge_is_in_one_place(test: unittest.TestCase, sources: dict) -> None:
    for name, source in sorted(sources.items()):
        if name in KNOWS_THE_LAYOUT:
            continue
        for token in INSTALL_TOKENS:
            test.assertNotIn(
                token,
                source,
                name + " must not know the install layout (" + token + "); the resolver owns it",
            )


def assert_scripts_share_the_resolver(test: unittest.TestCase, sources: dict) -> None:
    for name, source in sorted(sources.items()):
        if name == RESOLVER_PATH.name:
            # The resolver is the library; it has nothing to source.
            continue
        # The file name alone is not the contract: a mention in a comment, or a commented-out
        # copy of the real line, would satisfy a plain substring check.
        test.assertTrue(
            [
                line
                for line in runnable_lines(source)
                if "lib-sts2-paths.sh" in line or "lib-sts2.sh" in line
            ],
            name + " has to source the shared library, not merely mention it",
        )


def assert_resolver_has_the_whole_chain(test: unittest.TestCase, lib: str) -> None:
    for variable in ("STS2_GAME_ROOT", "STS2_APP_MANIFEST"):
        test.assertIn("$" + "{" + variable + ":-}", lib, variable + " is part of the contract")
    for function, variable in RESOLVER_FUNCTIONS:
        body = function_body(lib, function)
        test.assertIn(variable, body, function + " has to read " + variable)
        test.assertIn(EXPLICIT_TEST, body, function + " has to test the explicit argument")
        test.assertLess(
            body.index(EXPLICIT_TEST),
            body.index(variable),
            function + " has to consult the argument before " + variable,
        )
    test.assertIn(
        "libraryfolders.vdf",
        lib,
        "a game in a second library has to be found by detection, not by editing this file",
    )
    for root in CONVENTIONAL_ROOTS:
        test.assertIn(root, lib, root + " is a conventional Steam root on macOS or Linux")


def assert_offline_test_runs_where_it_can_fail(
    test: unittest.TestCase, gates: str, workflow: str
) -> None:
    test.assertIn(
        OFFLINE_TEST_PATH.name,
        gates,
        "the offline resolver test has to be part of a gate, or nothing keeps it honest",
    )
    # Naming the file is not running it: the gate has to be a registered entry, or --only and the
    # CI invocation below would both miss it.
    test.assertIn(
        '"sh-syntax": check_sh_syntax,',
        gates,
        "the gate has to be registered in the GATES map, not merely defined",
    )
    test.assertIn(
        GATES_PATH.name,
        workflow,
        "the gates have to run in CI for the line above to mean anything",
    )


def assert_process_fallback_can_stand_in(test: unittest.TestCase, lib: str) -> None:
    """The process fallback has to have something to match on when ps is unavailable.

    Regression this pins: computing the pgrep pattern only in the branch where the caller gave no
    executable meant that a caller who did give one had no fallback at all -- the function returned
    an empty list on any machine without a usable ps, and said nothing about it.
    """
    test.assertIn(
        'fallback_pattern = pattern or "|".join(re.escape(target) for target in sorted(targets))',
        lib,
        "the process fallback has to build a pattern when the caller named the executable",
    )
    test.assertIn(
        '["pgrep", "-f", fallback_pattern]',
        lib,
        "the fallback and the pattern it uses have to be the same thing",
    )


class PosixPathResolutionTests(unittest.TestCase):
    def sources(self) -> dict:
        return {
            path.name: path.read_text(encoding="utf-8")
            for path in sorted(SCRIPTS.glob("*.sh"))
        }

    def test_only_the_resolver_knows_the_install_layout(self) -> None:
        sources = self.sources()
        self.assertIn(RESOLVER_PATH.name, sources, "the resolver has to exist")
        assert_install_knowledge_is_in_one_place(self, sources)

    def test_every_script_sources_the_shared_library(self) -> None:
        assert_scripts_share_the_resolver(self, self.sources())

    def test_the_resolver_offers_argument_env_detection_then_default(self) -> None:
        assert_resolver_has_the_whole_chain(self, RESOLVER_PATH.read_text(encoding="utf-8"))

    def test_the_offline_test_exists_and_is_wired(self) -> None:
        self.assertTrue(
            OFFLINE_TEST_PATH.exists(), "the offline resolver test has to exist as a file"
        )
        assert_offline_test_runs_where_it_can_fail(
            self,
            GATES_PATH.read_text(encoding="utf-8"),
            VALIDATE_WORKFLOW.read_text(encoding="utf-8"),
        )

    def test_the_process_fallback_can_stand_in_for_ps(self) -> None:
        assert_process_fallback_can_stand_in(
            self, (ROOT / "scripts" / "lib-sts2.sh").read_text(encoding="utf-8")
        )


class PosixPathDestructiveTests(unittest.TestCase):
    def test_the_check_catches_a_second_copy_of_the_macos_path(self) -> None:
        script = (SCRIPTS / "build-mod.sh").read_text(encoding="utf-8")
        reverted = script + chr(10).join(
            (
                "",
                "detect_game_root() {",
                '  printf "%s" "$HOME/Library/Application Support/Steam/steamapps/common/game"',
                "}",
                "",
            )
        )
        self.assertNotEqual(reverted, script, "the fixture has to change something")
        with self.assertRaises(AssertionError):
            assert_install_knowledge_is_in_one_place(self, {"build-mod.sh": reverted})

    def test_the_check_catches_a_commented_out_source_line(self) -> None:
        script = (SCRIPTS / "build-mod.sh").read_text(encoding="utf-8")
        reverted = script.replace(
            '. "$script_dir/lib-sts2-paths.sh"',
            '# . "$script_dir/lib-sts2-paths.sh"',
        )
        self.assertNotEqual(reverted, script, "the fixture has to comment the source out")
        self.assertIn("lib-sts2-paths.sh", reverted, "the mention has to survive")
        with self.assertRaises(AssertionError):
            assert_scripts_share_the_resolver(self, {"build-mod.sh": reverted})

    def test_the_check_catches_a_resolver_without_the_library_list(self) -> None:
        lib = RESOLVER_PATH.read_text(encoding="utf-8")
        reverted = lib.replace("libraryfolders.vdf", "unused")
        self.assertNotEqual(reverted, lib, "the fixture has to remove the lookup")
        with self.assertRaises(AssertionError):
            assert_resolver_has_the_whole_chain(self, reverted)

    def test_the_check_catches_a_fallback_with_nothing_to_match(self) -> None:
        """Dropping the pattern the pgrep fallback needs is the regression that slipped through."""
        lib = (ROOT / "scripts" / "lib-sts2.sh").read_text(encoding="utf-8")
        reverted = lib.replace(
            'fallback_pattern = pattern or "|".join(re.escape(target) for target in sorted(targets))',
            "fallback_pattern = pattern",
        )
        self.assertNotEqual(reverted, lib, "the fixture has to weaken the fallback")
        with self.assertRaises(AssertionError):
            assert_process_fallback_can_stand_in(self, reverted)

    def test_the_check_catches_a_variable_that_outranks_the_argument(self) -> None:
        lib = RESOLVER_PATH.read_text(encoding="utf-8")
        # The regression: the explicit argument stops being what wins.
        reverted = lib.replace(EXPLICIT_TEST, "false")
        self.assertNotEqual(reverted, lib, "the fixture has to change the order")
        with self.assertRaises(AssertionError):
            assert_resolver_has_the_whole_chain(self, reverted)


if __name__ == "__main__":
    unittest.main()
