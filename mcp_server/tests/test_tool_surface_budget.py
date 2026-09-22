"""A budget contract on the MCP tool surface an external client re-sends every request.

A client that advertises tools by `tools/list` puts every tool's name, description, and
`inputSchema` in the prompt on *every* model request, so the text here is a per-request cost, not a
one-time one. The guided surface had grown to 11,468 characters (~2,867 est. tokens) that way, of
which 8,239 were narrative prose the loadable skill already carries: the usage loop, the screen
routing, and how the profile is organised.

The C# side has the same guard (`STS2AIAgent.Tests/SourceShapeContractTests`) for the same reason.
Two rules keep this a ratchet rather than a formality:

* Facts belong in the `inputSchema` property descriptions, which is where a client renders
  per-argument help. What a caller needs in order to make the call stays; workflow narrative,
  persuasion, and internal implementation notes go to `skills/sts2-mcp-player/SKILL.md`.
* Budgets go down, never up. When a change needs more room, move the prose rather than raising the
  number -- raising one is a deliberate act that has to show up in the diff.
"""

from __future__ import annotations

import asyncio
import json
import os
import unittest
from typing import Any

from sts2_mcp.server import create_server

CHARS_PER_TOKEN = 4

# 17 tools: the guided profile (16 shared with the native mod surface + the sidecar-only
# `wait_for_event`). scripts/test-mcp-tool-profile.ps1 pins the same list as ESSENTIAL_TOOLS.
# Raised 15 -> 17 on 2026-10-05 when the dual-layer planner tools (`get_planner_briefing`,
# `update_play_strategy`) joined the guided surface: the native mod surface exposes them through
# `AgentTools.Mcp`, and the alignment contract keeps the two guided surfaces identical.
GUIDED_TOOL_BUDGET = 17


class _DummyClient:
    """Answers every read the tool surface performs at registration and on a bare call.

    No HTTP, no game, no sockets: the surface is what is under test, not the mod.
    """

    def get_state(self) -> dict[str, Any]:
        return {"screen": "MAIN_MENU", "available_actions": []}

    def get_health(self) -> dict[str, Any]:
        return {}

    def get_available_actions(self) -> list[Any]:
        return []

    def get_decisions(self, limit: int = 50) -> list[Any]:
        return []

    def get_game_data_collection(self, collection: str) -> list[Any]:
        return []

    def __getattr__(self, name: str) -> Any:
        def _stub(*args: Any, **kwargs: Any) -> dict[str, Any]:
            return {}

        return _stub


def _wire_chars(tool: Any) -> int:
    """Characters this tool costs in tools/list: name + description + inputSchema, as JSON.

    This is the same measure the surface was trimmed against, so a budget here is a budget on the
    thing the client actually re-sends.
    """
    schema = json.dumps(tool.parameters or {}, ensure_ascii=False, separators=(",", ":"))
    return len(tool.name) + len(tool.description or "") + len(schema)


def _envelope_chars(tools: list[Any]) -> int:
    """Characters of the whole tools/list result envelope."""
    payload = {
        "tools": [
            {"name": t.name, "description": t.description, "inputSchema": t.parameters}
            for t in tools
        ]
    }
    return len(json.dumps(payload, ensure_ascii=False, separators=(",", ":")))


def _tools(profile: str, *, debug: bool = False) -> list[Any]:
    if debug:
        os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = "1"
    else:
        os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)

    server = create_server(client=_DummyClient(), tool_profile=profile)  # type: ignore[arg-type]
    return asyncio.run(server.list_tools())


# Whole-envelope ceilings. Measured 2026-10-04: guided 11,468 until the docstrings were trimmed
# (8,239 of those chars were narrative prose the skill already carries), now guided 8,255,
# layered 11,411, full 22,375, guided+debug 8,912. The headroom is ~5-9%: enough for a reworded
# sentence, nowhere near enough for that prose to come back.
# Re-measured 2026-10-05 after the dual-layer planner tools joined guided: guided 9,559,
# layered 12,715, full 23,679, guided+debug 10,216. The ceilings below sit ~5% over those.
ENVELOPE_BUDGETS: dict[str, int] = {
    "guided": 10_000,
    "layered": 13_300,
    "full": 24_800,
    "guided+debug": 10_700,
}

# What each guided tool cost on the wire before the trim, per the 2026-10-04 measurement that
# motivated this file: the sum of the name, the docstring, and the JSON schema. The ceilings below
# are checked against it, so a revert to the old prose fails the ceiling that tool owns as well as
# the aggregate envelope.
PRE_TRIM_WIRE_CHARS: dict[str, int] = {
    "act": 3_426,
    "diff_state": 702,
    "get_scene_guidance": 1_296,
    "decide": 952,
    "get_relevant_game_data": 728,
    "wait_until_actionable": 717,
    "get_game_state": 601,
    "get_decision_log": 483,
    "get_run_summary": 385,
    "wait_for_event": 376,
    "get_game_data_item": 304,
    "get_game_data_items": 242,
    "get_available_actions": 167,
    "get_raw_game_state": 151,
    "health_check": 134,
}

# Per-tool ceilings on name + description + inputSchema. `act` is the one tool allowed real room:
# it carries the per-index contract and the recoverable-error contract that a client calling it
# blind still needs. The three game-data tools are the only ones whose wire cost went *up*, because
# a per-argument description costs more JSON than the docstring line it replaced saved.
DEFAULT_TOOL_BUDGET = 700
_TOOL_BUDGETS: dict[str, int] = {
    # guided surface -- the tools this trim actually touched
    "act": 2_900,
    "get_scene_guidance": 620,
    "diff_state": 660,
    "decide": 500,
    "wait_until_actionable": 440,
    "get_game_state": 380,
    "get_relevant_game_data": 700,
    "get_game_data_item": 450,
    "get_game_data_items": 450,
    "wait_for_event": 370,
    "get_run_summary": 310,
    "get_decision_log": 310,
    # The dual-layer planner tools, added 2026-10-05. Measured 161 / 424 on the wire; the ceilings
    # sit a little over so a reworded sentence fits but the prose they replaced cannot come back.
    "get_planner_briefing": 250,
    "update_play_strategy": 550,
    # `get_available_actions`, `get_raw_game_state`, and `health_check` have no row: they were
    # already one line each before the trim, so their pre-trim cost (167 / 151 / 134) is lower than
    # any ceiling worth writing. `DEFAULT_TOOL_BUDGET` plus the guided envelope cover them.
    # layered surface: the two tools whose docstrings were not already one line. Their ceilings sit
    # below the untrimmed text, so a revert to the old wording fails here.
    "complete_combat_handoff": 640,
    "complete_event_handoff": 550,
}

# The tools whose wire cost went up rather than down: a per-argument `description` costs more JSON
# than the docstring line it replaced saved. For these two the ceiling cannot sit under the
# pre-trim cost, so the envelope plus the ceiling itself is what guards them.
_CEILINGS_ABOVE_PRE_TRIM = {"get_game_data_item", "get_game_data_items"}

# How far above its measured cost an explicit ceiling may sit before it stops being a ratchet.
DEFAULT_CEILING_SLACK = 250
# `act` gets more: it is the one tool allowed to grow a little, because a new argument contract or
# a new recoverable-error shape belongs in its schema rather than in a caller's guesswork.
_CEILING_SLACK: dict[str, int] = {"act": 320}

# The legacy per-action tools are one short line each by construction, and the full-profile
# envelope covers their sum; this is the per-tool ceiling they share.
LEGACY_TOOL_BUDGET = 420

_HOW_TO_FIX = (
    "Trim the docstring to telegraphic style, or move the fact into the inputSchema property "
    "description an MCP client renders as per-argument help. Workflow narrative belongs in "
    "skills/sts2-mcp-player/SKILL.md, which the caller has already loaded. Raising a budget is a "
    "deliberate act: justify it in the diff or leave the number alone."
)


def _budget_for(tool_name: str, profile: str) -> int:
    """The per-tool ceiling: an explicit one when the tool has it, else the profile's default.

    The default differs by surface because the two surfaces hold different kinds of tool: the
    guided set is 15 hand-written entries, while `full` adds ~63 one-line legacy per-action tools
    whose names are the whole contract.
    """
    if tool_name in _TOOL_BUDGETS:
        return _TOOL_BUDGETS[tool_name]
    return LEGACY_TOOL_BUDGET if profile == "full" else DEFAULT_TOOL_BUDGET


# The guided profile, which every profile carries. A tool here that falls through to the profile
# default is still covered by the envelope and by that default, just not by a per-tool ratchet of
# its own -- so the pre-trim invariant below only has to hold for the tools this trim rewrote.
_TRIMMED_GUIDED_TOOLS = {
    "health_check",
    "get_game_state",
    "get_raw_game_state",
    "get_available_actions",
    "get_decision_log",
    "get_run_summary",
    "get_scene_guidance",
    "diff_state",
    "get_game_data_item",
    "get_game_data_items",
    "get_relevant_game_data",
    "wait_for_event",
    "wait_until_actionable",
    "decide",
    "act",
}


def _format_overages(overages: list[tuple[str, str, int, int]]) -> str:
    return "\n  ".join(
        f"{name} ({profile}) is {size} chars against a {budget} budget"
        for name, profile, size, budget in overages
    )


class ToolSurfaceBudgetTests(unittest.TestCase):
    def test_guided_surface_fits_its_envelope(self) -> None:
        tools = _tools("guided")
        size = _envelope_chars(tools)

        self.assertLessEqual(
            size,
            ENVELOPE_BUDGETS["guided"],
            f"the guided tools/list envelope is {size} chars "
            f"(~{size // CHARS_PER_TOKEN} est. tokens) against a {ENVELOPE_BUDGETS['guided']} "
            f"budget, spent on every model request.\n{_HOW_TO_FIX}",
        )

    def test_layered_and_full_surfaces_fit_their_envelopes(self) -> None:
        overages = []
        for key, profile, debug in (
            ("layered", "layered", False),
            ("full", "full", False),
            ("guided+debug", "guided", True),
        ):
            size = _envelope_chars(_tools(profile, debug=debug))
            budget = ENVELOPE_BUDGETS[key]
            if size > budget:
                overages.append((key, key, size, budget))

        self.assertEqual(
            overages,
            [],
            "these tool profiles are over their tools/list budget:\n  "
            + _format_overages(overages)
            + f"\n{_HOW_TO_FIX}",
        )

    def test_no_single_tool_grows_past_its_ceiling(self) -> None:
        overages = []
        for key, profile, debug in (
            ("guided", "guided", False),
            ("layered", "layered", False),
            ("full", "full", False),
            ("guided+debug", "guided", True),
        ):
            for tool in _tools(profile, debug=debug):
                budget = _budget_for(tool.name, profile)
                size = _wire_chars(tool)
                if size > budget:
                    overages.append((tool.name, key, size, budget))

        self.assertEqual(
            overages,
            [],
            "these tools are over their per-tool wire budget:\n  "
            + _format_overages(overages)
            + f"\n{_HOW_TO_FIX}",
        )

    def test_the_guided_surface_is_the_tool_set_this_budget_describes(self) -> None:
        """A budget over a surface that silently changed shape guards nothing."""
        tools = _tools("guided")

        self.assertEqual(
            GUIDED_TOOL_BUDGET,
            len(tools),
            f"the guided profile registers {len(tools)} tools, not {GUIDED_TOOL_BUDGET}; "
            "this budget table and scripts/test-mcp-tool-profile.ps1 were written for that set. "
            "Adding a tool is fine -- re-measure the envelope and update the table deliberately.",
        )

    def test_the_failure_the_budget_exists_to_catch_still_fails_it(self) -> None:
        """A ceiling too loose to catch the regression it was written for is decoration.

        The regression is the one this guard was built from: the guided surface drifting back to
        narrative prose, which cost 11,468 chars (11,590 once the envelope is JSON-wrapped).
        """
        prose_surface = 11_590

        self.assertLess(
            ENVELOPE_BUDGETS["guided"],
            prose_surface,
            "the guided envelope budget no longer catches the narrative-prose regression it exists "
            "for; bring it back under the pre-trim cost",
        )

    def test_every_explicit_ceiling_is_tighter_than_what_it_replaced(self) -> None:
        """A ceiling above the pre-trim cost would let the old prose straight back in.

        `PRE_TRIM_WIRE_CHARS` is the measurement this trim started from. A tool whose explicit
        ceiling sits above its own pre-trim cost is not a ratchet on that tool -- the two game-data
        tools are the deliberate exceptions: their wire cost went *up*, because a per-argument
        description costs more JSON than the docstring line it replaced saved, so no ceiling can be
        both above today's cost and below the old one.
        """
        regressions = []
        for name, ceiling in _TOOL_BUDGETS.items():
            pre_trim = PRE_TRIM_WIRE_CHARS.get(name)
            if pre_trim is None or name in _CEILINGS_ABOVE_PRE_TRIM:
                continue
            if ceiling > pre_trim:
                regressions.append(
                    f"{name} has a {ceiling} budget against a pre-trim cost of {pre_trim} chars"
                )

        self.assertEqual(
            regressions,
            [],
            "these ceilings cannot catch a revert to the pre-trim prose:\n  "
            + "\n  ".join(regressions)
            + "\nLower the ceiling under the pre-trim cost, or list the tool in "
            "_CEILINGS_ABOVE_PRE_TRIM with the reason.",
        )

    def test_the_ceiling_exception_list_stays_honest(self) -> None:
        """An exemption for a tool that no longer needs one is how a guard rots."""
        stale = [
            name
            for name in _CEILINGS_ABOVE_PRE_TRIM
            if name not in _TOOL_BUDGETS
            or _TOOL_BUDGETS[name] <= PRE_TRIM_WIRE_CHARS.get(name, 0)
        ]

        self.assertEqual(
            stale,
            [],
            "these tools are listed as needing a ceiling above their pre-trim cost but do not:\n  "
            + "\n  ".join(stale)
            + "\nRemove the exemption.",
        )

    def test_the_trim_reached_every_tool_the_pre_trim_measurement_covers(self) -> None:
        """The pre-trim table is evidence; a measurement no ceiling uses is a stale claim.

        Every tool in `PRE_TRIM_WIRE_CHARS` must be one the trim rewrote -- which is to say a
        guided tool, since those are the only docstrings it touched.
        """
        stale = [name for name in PRE_TRIM_WIRE_CHARS if name not in _TRIMMED_GUIDED_TOOLS]

        self.assertEqual(
            stale,
            [],
            "these measured tools are not part of the guided surface the trim rewrote, so their "
            "pre-trim numbers say nothing about a ceiling:\n  "
            + "\n  ".join(stale)
            + "\nDrop the entry, or add the tool to _TRIMMED_GUIDED_TOOLS if the trim did reach it.",
        )

    def test_every_explicit_ceiling_still_binds(self) -> None:
        """A ceiling that has drifted far above its tool is decoration; drop it to the measurement.

        Only the explicit rows are checked: the profile defaults exist to catch a runaway, not to
        ratchet a tool, so slack under a default is expected rather than a defect.
        """
        slack = []
        for key, profile, debug in (
            ("guided", "guided", False),
            ("layered", "layered", False),
            ("full", "full", False),
            ("guided+debug", "guided", True),
        ):
            for tool in _tools(profile, debug=debug):
                if tool.name not in _TOOL_BUDGETS:
                    continue
                size = _wire_chars(tool)
                allowance = _CEILING_SLACK.get(tool.name, DEFAULT_CEILING_SLACK)
                if _TOOL_BUDGETS[tool.name] > size + allowance:
                    slack.append(
                        f"{tool.name} ({key}) is {size} chars against a "
                        f"{_TOOL_BUDGETS[tool.name]} budget"
                    )

        self.assertEqual(
            slack,
            [],
            "these ceilings have drifted away from their tools; lower them to the measurement:\n  "
            + "\n  ".join(slack),
        )

    def test_every_guided_tool_is_covered_by_a_ceiling(self) -> None:
        """A guided tool with no explicit ceiling should be one the default can actually hold."""
        uncovered = []
        for tool in _tools("guided"):
            if tool.name in _TOOL_BUDGETS:
                continue
            size = _wire_chars(tool)
            if size > DEFAULT_TOOL_BUDGET:
                uncovered.append(
                    f"{tool.name} costs {size} chars and has only the "
                    f"{DEFAULT_TOOL_BUDGET}-char default"
                )

        self.assertEqual(
            uncovered,
            [],
            "these guided tools need an explicit ceiling:\n  "
            + "\n  ".join(uncovered)
            + "\nMeasure the tool and add a row to _TOOL_BUDGETS.",
        )


if __name__ == "__main__":
    unittest.main()
