"""The Python planner tool forwards the mod's single-snapshot briefing unchanged."""

from __future__ import annotations

import asyncio
import unittest
from pathlib import Path
from typing import Any
from unittest.mock import patch

from sts2_mcp.client import Sts2Client
from sts2_mcp.server import create_server


class BriefingClient:
    def __init__(self, payload: dict[str, Any]) -> None:
        self.payload = payload
        self.calls = 0

    def get_strategy(self) -> dict[str, Any]:
        self.calls += 1
        return self.payload

    def get_state(self) -> dict[str, Any]:
        raise AssertionError("briefing must not re-read the game state")

    def get_decisions(self, limit: int = 50) -> list[dict[str, Any]]:
        raise AssertionError("briefing must not re-read the decision log")


class BriefingToolTests(unittest.TestCase):
    def test_complete_briefing_is_one_pass_through_with_no_extra_reads(self) -> None:
        payload = {
            "strategy": {"posture": "defensive", "instructions": "Block", "option_hints": {}},
            "dual_layer": True,
            "jev_configured": True,
            "run_summary": {"floor": 5},
            "screen": "COMBAT",
            "recent_jev_decisions": [{"id": 3, "action": "end_turn", "confidence": 0.6}],
            "confidence_trend": {"count": 1, "average": 0.6, "latest": 0.6, "direction": "insufficient_data"},
        }
        client = BriefingClient(payload)
        server = create_server(client=client, tool_profile="guided")  # type: ignore[arg-type]
        tool = asyncio.run(server.get_tool("get_planner_briefing"))
        self.assertIsNotNone(tool)
        self.assertEqual(tool.fn(), payload)
        self.assertEqual(client.calls, 1)

    def test_old_mod_fields_are_not_invented(self) -> None:
        payload = {"strategy": {}, "dual_layer": False, "jev_configured": False}
        client = BriefingClient(payload)
        server = create_server(client=client, tool_profile="guided")  # type: ignore[arg-type]
        tool = asyncio.run(server.get_tool("get_planner_briefing"))
        self.assertEqual(tool.fn(), payload)
        self.assertEqual(client.calls, 1)

    def test_native_and_python_briefing_use_the_same_projection_source(self) -> None:
        root = Path(__file__).resolve().parents[2]
        native = (root / "STS2AIAgent/Server/NativeMcpServer.Tools.cs").read_text(encoding="utf-8")
        router = (root / "STS2AIAgent/Server/Router.cs").read_text(encoding="utf-8")
        self.assertIn("PlannerBriefingProjection.Build(", native)
        self.assertIn("PlannerBriefingProjection.Build(", router)

    def test_client_get_strategy_returns_raw_envelope_data_unchanged(self) -> None:
        payload = {
            "strategy": {}, "dual_layer": True, "jev_configured": False,
            "run_summary": None, "screen": "MAIN_MENU", "recent_jev_decisions": [],
            "confidence_trend": None,
        }
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        with patch.object(client, "_request", return_value=payload) as request:
            self.assertEqual(client.get_strategy(), payload)
        request.assert_called_once_with("GET", "/strategy")


if __name__ == "__main__":
    unittest.main()
