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

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
RESOLVER_PATH = SCRIPTS / "lib-sts2-paths.sh"
OFFLINE_TEST_PATH = SCRIPTS / "test-lib-sts2-paths.sh"
GATES_PATH = SCRIPTS / "check_verification_gates.py"
VALIDATE_WORKFLOW = ROOT / ".github" / "workflows" / "validate.yml"
START_SESSION_PATH = SCRIPTS / "start-game-session.sh"
BASH_CANDIDATES = (
    "C:/Program Files/Git/bin/bash.exe",
    "C:/Program Files (x86)/Git/bin/bash.exe",
    "bash",
)

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


def usable_bash() -> str | None:
    probe = START_SESSION_PATH.as_posix()
    for candidate in BASH_CANDIDATES:
        resolved = shutil.which(candidate) or (candidate if Path(candidate).is_file() else None)
        if resolved is None:
            continue
        try:
            probe_run = subprocess.run([resolved, "-n", probe], capture_output=True, timeout=60)
        except (OSError, subprocess.SubprocessError):
            continue
        if probe_run.returncode == 0:
            return resolved
    return None


def source_start_session(bash: str, command: str) -> subprocess.CompletedProcess:
    script = START_SESSION_PATH.as_posix()
    return subprocess.run(
        [bash, "-c", '. "' + script + '"; ' + command],
        capture_output=True,
        text=True,
        timeout=30,
    )


def assert_start_session_collects_extra_args(test: unittest.TestCase, script: str) -> None:
    test.assertNotIn(
        "Unknown argument:",
        script,
        "unknown game args have to be collected, not treated as usage errors",
    )
    test.assertIn('extra_args+=("$1")', script)
    test.assertIn("    --)", script)
    test.assertIn('exec "$exe_path" "${extra_args[@]}"', script)


def assert_start_session_parses_client_id(test: unittest.TestCase, script: str) -> None:
    test.assertIn("sts2_client_id_from_args", script)
    test.assertIn('"$arg" == "--clientId"', script)
    test.assertIn("--clientId=*", script)


def assert_start_session_seeds_isolated_settings(test: unittest.TestCase, script: str) -> None:
    test.assertIn("sts2_initialize_isolated_client_settings", script)
    test.assertIn("settings.save", script)
    test.assertIn("mods_enabled", script)
    test.assertIn("STS2AIAgent", script)
    test.assertIn("sts2_settings_agent_ready", script)
    test.assertIn("is_enabled", script)
    test.assertIn("default/$client_id", script)
    test.assertIn('export STS2_API_PORT="$api_port"', script)
    test.assertIn("export STS2_ENABLE_DEBUG_ACTIONS=1", script)
    test.assertIn('"$user_root/steam"', script)
    test.assertIn("aspect_ratio", script)
    test.assertIn("from steam template", script)


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


class PosixBuildModContractTests(unittest.TestCase):
    def test_build_mod_sh_stages_mod_id_and_supports_skip_install(self) -> None:
        script = (SCRIPTS / "build-mod.sh").read_text(encoding="utf-8")
        self.assertIn("--skip-install", script)
        self.assertIn('mod_id_source="$repo_root/STS2AIAgent/mod_id.json"', script)
        self.assertIn('cp -f "$mod_id_target" "$mods_dir/mod_id.json"', script)
        self.assertIn('Install location not found', script)
        self.assertNotIn('mkdir -p "$mods_dir"\n\necho "[build-mod] Building', script)

    def test_missing_install_parent_is_rejected_without_creating_it(self) -> None:
        script = (SCRIPTS / "build-mod.sh").read_text(encoding="utf-8")
        self.assertIn("Install location not found", script)
        self.assertIn("skip_install", script)


class PosixStartGameSessionContractTests(unittest.TestCase):
    def script(self) -> str:
        return START_SESSION_PATH.read_text(encoding="utf-8")

    def test_unknown_args_are_collected_and_forwarded(self) -> None:
        assert_start_session_collects_extra_args(self, self.script())

    def test_client_id_space_and_equals_forms_are_parsed(self) -> None:
        assert_start_session_parses_client_id(self, self.script())

    def test_isolated_settings_seed_and_env_exports_are_present(self) -> None:
        assert_start_session_seeds_isolated_settings(self, self.script())

    def test_the_check_catches_unknown_argument_usage_exit(self) -> None:
        script = self.script()
        reverted = script.replace(
            'extra_args+=("$1")',
            'echo "Unknown argument: $1" >&2; usage >&2; exit 1',
        )
        self.assertNotEqual(reverted, script, "the fixture has to restore the usage exit")
        with self.assertRaises(AssertionError):
            assert_start_session_collects_extra_args(self, reverted)

    def test_slay_user_root_follows_windows_appdata_in_git_bash(self) -> None:
        bash = usable_bash()
        if bash is None:
            self.skipTest("no usable bash on this machine")
        run = source_start_session(
            bash,
            "uname() { echo MINGW64_NT-10.0; }; "
            "unset STS2_SLAY_USER_ROOT; "
            "APPDATA='C:\\Users\\probe\\AppData\\Roaming'; "
            "sts2_slay_user_root",
        )
        self.assertEqual(run.returncode, 0, run.stderr)
        root = run.stdout.strip()
        self.assertTrue(root.endswith("/SlayTheSpire2"), root)
        self.assertIn("AppData/Roaming", root)
        self.assertNotIn(".local/share", root)

    def test_slay_user_root_honours_the_env_override(self) -> None:
        bash = usable_bash()
        if bash is None:
            self.skipTest("no usable bash on this machine")
        run = source_start_session(
            bash,
            "uname() { echo MINGW64_NT-10.0; }; "
            "APPDATA='C:\\Users\\probe\\AppData\\Roaming'; "
            "STS2_SLAY_USER_ROOT=/tmp/probe-root sts2_slay_user_root",
        )
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertEqual(run.stdout.strip(), "/tmp/probe-root")

    def test_slay_user_root_keeps_the_posix_defaults(self) -> None:
        bash = usable_bash()
        if bash is None:
            self.skipTest("no usable bash on this machine")
        mac = source_start_session(
            bash,
            "unset STS2_SLAY_USER_ROOT; uname() { echo Darwin; }; "
            "HOME=/Users/probe sts2_slay_user_root",
        )
        self.assertEqual(mac.returncode, 0, mac.stderr)
        self.assertEqual(mac.stdout.strip(), "/Users/probe/Library/Application Support/SlayTheSpire2")
        linux = source_start_session(
            bash,
            "unset STS2_SLAY_USER_ROOT; uname() { echo Linux; }; "
            "HOME=/home/probe XDG_DATA_HOME=/xdg sts2_slay_user_root",
        )
        self.assertEqual(linux.returncode, 0, linux.stderr)
        self.assertEqual(linux.stdout.strip(), "/xdg/SlayTheSpire2")

    def test_client_id_forms_seed_and_skip_enabled_settings(self) -> None:
        bash = usable_bash()
        if bash is None:
            self.skipTest("no usable bash on this machine")
        space = source_start_session(
            bash,
            "sts2_client_id_from_args --windowed --clientId 2026091501 --force-steam off",
        )
        self.assertEqual(space.returncode, 0, space.stderr)
        self.assertEqual(space.stdout.strip(), "2026091501")
        equals = source_start_session(bash, "sts2_client_id_from_args --clientId=abc")
        self.assertEqual(equals.returncode, 0, equals.stderr)
        self.assertEqual(equals.stdout.strip(), "abc")
        with tempfile.TemporaryDirectory() as tmp:
            user_root = (tmp + "/SlayTheSpire2").replace("\\", "/")
            steam_dir = Path(tmp) / "SlayTheSpire2" / "steam" / "76561198000000000"
            steam_dir.mkdir(parents=True)
            (steam_dir / "settings.save").write_text(
                json.dumps(
                    {
                        "schema_version": 8,
                        "language": "zhs",
                        "aspect_ratio": "sixteen_by_nine",
                        "fps_limit": 60,
                        "msaa": 2,
                        "volume_master": 0.5,
                        "fullscreen": True,
                        "mod_settings": {
                            "mods_enabled": False,
                            "mod_list": [{"id": "OtherMod", "is_enabled": True}],
                        },
                        "seen_ea_disclaimer": False,
                        "skip_intro_logo": False,
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )
            seed = source_start_session(
                bash,
                'sts2_initialize_isolated_client_settings "2026091501" "' + user_root + '"',
            )
            self.assertEqual(seed.returncode, 0, seed.stderr)
            settings_path = Path(tmp) / "SlayTheSpire2" / "default" / "2026091501" / "settings.save"
            self.assertFalse(settings_path.read_bytes().startswith(b"\xef\xbb\xbf"))
            data = json.loads(settings_path.read_text(encoding="utf-8"))
            self.assertTrue(data["mod_settings"]["mods_enabled"])
            self.assertEqual(data["language"], "zhs")
            self.assertEqual(data["aspect_ratio"], "sixteen_by_nine")
            self.assertEqual(data["fps_limit"], 60)
            seeded_ids = [item["id"] for item in data["mod_settings"]["mod_list"]]
            self.assertIn("STS2AIAgent", seeded_ids)
            self.assertIn("OtherMod", seeded_ids)
            self.assertGreater(len(data.keys()), 8)
            settings_path.write_text(
                '{"mod_settings":{"mods_enabled":true,"keep":1}}',
                encoding="utf-8",
            )
            again = source_start_session(
                bash,
                'sts2_initialize_isolated_client_settings "2026091501" "' + user_root + '"',
            )
            self.assertEqual(again.returncode, 0, again.stderr)
            kept = json.loads(settings_path.read_text(encoding="utf-8"))
            self.assertEqual(kept["mod_settings"]["keep"], 1)
            self.assertTrue(kept["mod_settings"]["mods_enabled"])
            self.assertIn("STS2AIAgent", [item["id"] for item in kept["mod_settings"]["mod_list"]])
            settings_path.write_text(
                '{"schema_version": 8, "mod_settings": null, "language": "keep-me"}',
                encoding="utf-8",
            )
            patched = source_start_session(
                bash,
                'sts2_initialize_isolated_client_settings "2026091501" "' + user_root + '"',
            )
            self.assertEqual(patched.returncode, 0, patched.stderr)
            patched_data = json.loads(settings_path.read_text(encoding="utf-8"))
            self.assertTrue(patched_data["mod_settings"]["mods_enabled"])
            self.assertEqual(patched_data["schema_version"], 8)
            self.assertEqual(patched_data["language"], "keep-me")

        with tempfile.TemporaryDirectory() as tmp:
            user_root = (tmp + "/SlayTheSpire2").replace("\\", "/")
            fallback = source_start_session(
                bash,
                'sts2_initialize_isolated_client_settings "2026091501" "' + user_root + '"',
            )
            self.assertEqual(fallback.returncode, 0, fallback.stderr)
            fallback_path = Path(tmp) / "SlayTheSpire2" / "default" / "2026091501" / "settings.save"
            fallback_data = json.loads(fallback_path.read_text(encoding="utf-8"))
            self.assertTrue(fallback_data["mod_settings"]["mods_enabled"])
            self.assertEqual(fallback_data["mod_settings"]["mod_list"][0]["id"], "STS2AIAgent")
            self.assertEqual(fallback_data["language"], "en")
            self.assertIn("aspect_ratio", fallback_data)
            sparse_keys = {
                "schema_version",
                "mod_settings",
                "seen_ea_disclaimer",
                "skip_intro_logo",
                "fullscreen",
                "limit_fps_in_background",
                "vsync",
                "window_size",
            }
            self.assertTrue(set(fallback_data.keys()) - sparse_keys)

    def _bash(self) -> str:
        bash = usable_bash()
        if bash is None:
            self.skipTest("no usable bash on this machine")
        return bash

    def _init_isolated(self, bash: str, user_root: str, client_id: str = "2026091502"):
        return source_start_session(
            bash,
            'sts2_initialize_isolated_client_settings "' + client_id + '" "' + user_root + '"',
        )

    def test_ready_check_requires_enabled_agent_not_just_mods_enabled(self) -> None:
        script = self.script()
        body = function_body(script, "sts2_initialize_isolated_client_settings")
        self.assertIn("sts2_settings_agent_ready", body)
        self.assertNotIn("sts2_settings_mods_already_enabled", body)
        patched_at = body.index("patched isolated settings")
        template_at = body.index("from steam template")
        self.assertLess(patched_at, template_at)
        self.assertIn("return 0", body[patched_at:template_at])

    def test_mods_enabled_true_without_agent_is_patched(self) -> None:
        bash = self._bash()
        with tempfile.TemporaryDirectory() as tmp:
            user_root = (tmp + "/SlayTheSpire2").replace("\\", "/")
            settings_path = Path(tmp) / "SlayTheSpire2" / "default" / "2026091502" / "settings.save"
            settings_path.parent.mkdir(parents=True)
            settings_path.write_text(
                json.dumps(
                    {
                        "language": "keep-lang",
                        "volume_master": 0.25,
                        "mod_settings": {
                            "mods_enabled": True,
                            "keep": 1,
                            "mod_list": [{"id": "OtherMod", "is_enabled": True}],
                        },
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )
            original = settings_path.read_bytes()
            result = self._init_isolated(bash, user_root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertNotEqual(settings_path.read_bytes(), original)
            data = json.loads(settings_path.read_text(encoding="utf-8"))
            self.assertEqual(data["language"], "keep-lang")
            self.assertEqual(data["volume_master"], 0.25)
            self.assertEqual(data["mod_settings"]["keep"], 1)
            self.assertTrue(data["mod_settings"]["mods_enabled"])
            ids = [item["id"] for item in data["mod_settings"]["mod_list"]]
            self.assertIn("STS2AIAgent", ids)
            self.assertIn("OtherMod", ids)

    def test_disabled_agent_is_reenabled(self) -> None:
        bash = self._bash()
        with tempfile.TemporaryDirectory() as tmp:
            user_root = (tmp + "/SlayTheSpire2").replace("\\", "/")
            settings_path = Path(tmp) / "SlayTheSpire2" / "default" / "2026091502" / "settings.save"
            settings_path.parent.mkdir(parents=True)
            settings_path.write_text(
                json.dumps(
                    {
                        "language": "keep-lang",
                        "mod_settings": {
                            "mods_enabled": True,
                            "mod_list": [
                                {"id": "OtherMod", "is_enabled": True},
                                {
                                    "id": "STS2AIAgent",
                                    "is_enabled": False,
                                    "source": "mods_directory",
                                },
                            ],
                        },
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )
            result = self._init_isolated(bash, user_root)
            self.assertEqual(result.returncode, 0, result.stderr)
            data = json.loads(settings_path.read_text(encoding="utf-8"))
            self.assertEqual(data["language"], "keep-lang")
            self.assertTrue(data["mod_settings"]["mods_enabled"])
            by_id = {item["id"]: item for item in data["mod_settings"]["mod_list"]}
            self.assertTrue(by_id["STS2AIAgent"]["is_enabled"])
            self.assertTrue(by_id["OtherMod"]["is_enabled"])

    def test_existing_incomplete_file_is_not_replaced_by_steam_template(self) -> None:
        bash = self._bash()
        with tempfile.TemporaryDirectory() as tmp:
            user_root = (tmp + "/SlayTheSpire2").replace("\\", "/")
            steam_dir = Path(tmp) / "SlayTheSpire2" / "steam" / "76561198000000000"
            steam_dir.mkdir(parents=True)
            (steam_dir / "settings.save").write_text(
                json.dumps(
                    {
                        "language": "from-steam",
                        "aspect_ratio": "sixteen_by_nine",
                        "favorite": "steam-only",
                        "mod_settings": {"mods_enabled": False},
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )
            settings_path = Path(tmp) / "SlayTheSpire2" / "default" / "2026091502" / "settings.save"
            settings_path.parent.mkdir(parents=True)
            settings_path.write_text(
                json.dumps(
                    {
                        "schema_version": 8,
                        "language": "keep-isolated",
                        "favorite": "alpha",
                        "mod_settings": {"mods_enabled": False, "mod_list": []},
                    },
                    indent=2,
                )
                + "\n",
                encoding="utf-8",
            )
            result = self._init_isolated(bash, user_root)
            self.assertEqual(result.returncode, 0, result.stderr)
            data = json.loads(settings_path.read_text(encoding="utf-8"))
            self.assertEqual(data["language"], "keep-isolated")
            self.assertEqual(data["favorite"], "alpha")
            self.assertNotEqual(data["language"], "from-steam")
            self.assertNotIn("steam-only", json.dumps(data))
            self.assertTrue(data["mod_settings"]["mods_enabled"])
            self.assertIn("STS2AIAgent", [item["id"] for item in data["mod_settings"]["mod_list"]])

    def test_ready_file_is_left_byte_identical(self) -> None:
        bash = self._bash()
        with tempfile.TemporaryDirectory() as tmp:
            user_root = (tmp + "/SlayTheSpire2").replace("\\", "/")
            settings_path = Path(tmp) / "SlayTheSpire2" / "default" / "2026091502" / "settings.save"
            settings_path.parent.mkdir(parents=True)
            payload = (
                json.dumps(
                    {
                        "language": "keep-lang",
                        "keep": "untouched",
                        "mod_settings": {
                            "mods_enabled": True,
                            "mod_list": [
                                {
                                    "id": "STS2AIAgent",
                                    "is_enabled": True,
                                    "source": "mods_directory",
                                }
                            ],
                        },
                    },
                    indent=2,
                )
                + "\n"
            )
            settings_path.write_text(payload, encoding="utf-8")
            original = settings_path.read_bytes()
            result = self._init_isolated(bash, user_root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(settings_path.read_bytes(), original)


class WindowsSeedScopingTests(unittest.TestCase):
    """The PowerShell seeder must decide is_enabled inside the agent's own entry.

    A fixed character window around the agent id reaches the next entry: the Steam template's
    list continues with DamageMeter, whose is_enabled is false, so a windowed check called a
    perfectly good clone unready, sent it to the repair path, and made the repair flip the wrong
    mod before the caller fell back to the sparse settings file the game will not accept.
    """

    def ps_script(self) -> str:
        return (SCRIPTS / "start-game-session.ps1").read_text(encoding="utf-8")

    def test_agent_entry_is_scoped_to_its_own_object(self) -> None:
        script = self.ps_script()
        self.assertIn("function Get-IsolatedAgentEntryBody", script)
        # definition plus the readiness probe and the repair
        self.assertGreaterEqual(script.count("Get-IsolatedAgentEntryBody"), 3)

    def test_no_fixed_character_window_decides_is_enabled(self) -> None:
        script = self.ps_script()
        self.assertNotIn(r'\"id\"\s*:\s*\"STS2AIAgent\"[\s\S]{0,200}', script)
        self.assertNotIn(r'\"is_enabled\"\s*:\s*false[\s\S]{0,200}', script)


if __name__ == "__main__":
    unittest.main()
