"""A ratchet on file size for the MCP sidecar.

The C# side has the same guard (`SourceShapeContractTests`) for the same reason: two files there
hold 49% of the mod, and neither got that way by a decision. This keeps the sidecar from repeating
it while it is still small enough to matter.

Budgets go down, never up. When a change needs more room than its budget allows, move something out
rather than raising the number -- raising one is a deliberate act that has to show up in the diff.
"""

from __future__ import annotations

import unittest
from pathlib import Path

# Any module not listed below stays under this.
DEFAULT_BUDGET = 700

# Modules already past the default, with the headroom they are allowed. Empty is the shape to aim
# for: server.py held 1,103 lines until the game-data concern moved to game_data.py, then 750 until
# the legacy tool table *and its registration* moved to legacy_tools.py (2026-10-02), and the entry
# came down with the file both times rather than staying behind as room to regrow.
BUDGETS: dict[str, int] = {}
# client.py held 1,156 lines under a 1,200 budget until its 58 per-action wrappers moved to
# client_actions.py on 2026-09-17. Both halves now fit the default, so neither has an entry --
# which is the shape to aim for, and the reason the second test below fails a budget that has
# stopped binding rather than letting it sit there as decoration.

MINIMUM_MODULES = 4


def _source_root() -> Path:
    for candidate in (Path(__file__).resolve().parents[1], Path.cwd()):
        root = candidate / "src" / "sts2_mcp"
        if root.is_dir():
            return root
    raise AssertionError("Could not locate mcp_server/src/sts2_mcp")


class SourceShapeTests(unittest.TestCase):
    def test_no_module_grows_past_its_budget(self) -> None:
        root = _source_root()
        modules = sorted(p for p in root.rglob("*.py") if "__pycache__" not in p.parts)
        # A broken walk would pass this test while checking nothing.
        self.assertGreaterEqual(
            len(modules),
            MINIMUM_MODULES,
            f"only {len(modules)} modules found under {root}; the walk no longer sees the package",
        )

        offenders = []
        for module in modules:
            lines = len(module.read_text(encoding="utf-8").splitlines())
            budget = BUDGETS.get(module.name, DEFAULT_BUDGET)
            if lines > budget:
                offenders.append(f"{module.name} is {lines} lines, over its {budget}-line budget")

        self.assertEqual(
            offenders,
            [],
            "these modules are over budget:\n  "
            + "\n  ".join(offenders)
            + "\n\nMove something out rather than raising the budget.",
        )

    def test_budgets_track_the_modules_they_guard(self) -> None:
        """A budget that no longer binds is not a ratchet."""
        root = _source_root()
        slack = []
        for name, budget in BUDGETS.items():
            module = root / name
            self.assertTrue(module.is_file(), f"{name} has a budget but no longer exists; remove the entry")
            lines = len(module.read_text(encoding="utf-8").splitlines())
            if lines <= DEFAULT_BUDGET:
                slack.append(f"{name} is down to {lines} lines and fits the default; drop its entry")
            elif budget - lines > 300:
                slack.append(f"{name} is {lines} lines against a {budget}-line budget; lower the budget")
        self.assertEqual(slack, [], "these budgets drifted away from their modules:\n  " + "\n  ".join(slack))


if __name__ == "__main__":
    unittest.main()
