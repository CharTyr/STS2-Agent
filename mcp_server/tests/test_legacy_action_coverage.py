"""Every mod action must have a legacy tool in the full profile.

The full profile exists for compatibility and validation, and docs/api.md claims it exposes
an independent tool per action. run_console_command is the one deliberate exception: it is
debug-gated and registered separately.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

from sts2_mcp.client import Sts2Client
from sts2_mcp.server import _LEGACY_ACTION_TOOLS

_ACTION_BRANCH = re.compile(r'\"([a-z_]+)\"\s*=>')
_README_TOOL = re.compile(r"^- `([a-z][a-z0-9_]*)`", re.MULTILINE)


def _find_source_root() -> Path:
    candidates = [Path(__file__).resolve().parents[2], Path.cwd()]
    for candidate in candidates:
        if (candidate / "STS2AIAgent/Game/GameActionService.cs").is_file():
            return candidate
    raise AssertionError("Could not locate STS2AIAgent/Game/GameActionService.cs")


def _mod_actions() -> set[str]:
    source = (_find_source_root() / "STS2AIAgent/Game/GameActionService.cs").read_text(encoding="utf-8")
    start = source.index("switch")
    return set(_ACTION_BRANCH.findall(source[start : start + 12000]))


class LegacyActionCoverageTests(unittest.TestCase):
    def test_every_mod_action_has_a_legacy_tool(self) -> None:
        documented = _mod_actions()
        self.assertGreater(len(documented), 40, "action switch parse looks wrong")

        have = {spec.name for spec in _LEGACY_ACTION_TOOLS}
        self.assertEqual(
            {"run_console_command"},
            documented - have,
            "full profile is missing per-action tools: " + ", ".join(sorted(documented - have)),
        )
        self.assertEqual(set(), have - documented, "legacy tools without a mod action")

    def test_every_legacy_tool_has_a_client_method(self) -> None:
        client = Sts2Client()
        missing = [spec.name for spec in _LEGACY_ACTION_TOOLS if not hasattr(client, spec.name)]
        self.assertEqual([], missing, "client methods missing for: " + ", ".join(missing))

    def test_readme_legacy_tool_list_matches_server(self) -> None:
        """The README's full-profile inventory is a contract, not prose.

        It is written for developers who read the file instead of registering a server, so a
        tool added to _LEGACY_ACTION_TOOLS but left out of the README (or the reverse) is a
        silent doc drift. The list is delimited by HTML comment markers so this stays a
        targeted comparison rather than a scrape of every backticked word in the file.
        """
        readme = (_find_source_root() / "mcp_server/README.md").read_text(encoding="utf-8")
        begin = readme.find("<!-- BEGIN LEGACY ACTION TOOLS -->")
        end = readme.find("<!-- END LEGACY ACTION TOOLS -->")
        self.assertTrue(
            begin >= 0 and end > begin,
            "mcp_server/README.md lost the legacy-tool contract markers that scope this test",
        )

        block = readme[begin:end]
        documented = set(_README_TOOL.findall(block))
        self.assertGreater(len(documented), 40, "README legacy-tool block parse looks wrong")

        shipped = {spec.name for spec in _LEGACY_ACTION_TOOLS}
        self.assertEqual(
            set(),
            shipped - documented,
            "mcp_server/README.md does not list these legacy tools: "
            + ", ".join(sorted(shipped - documented)),
        )
        self.assertEqual(
            set(),
            documented - shipped,
            "mcp_server/README.md lists names that are not legacy tools: "
            + ", ".join(sorted(documented - shipped)),
        )
