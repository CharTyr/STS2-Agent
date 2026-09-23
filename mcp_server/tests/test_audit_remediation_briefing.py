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


class StrategyUpdateTests(unittest.TestCase):
    """An external planner can set the macro goal, and an omitted field is never sent as a reset."""

    def test_client_sends_only_the_named_fields_including_goal(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        with patch.object(client, "_request", return_value={}) as request:
            client.update_strategy(posture="defensive", goal="hold potions for the elite")
        request.assert_called_once_with(
            "POST", "/strategy", {"strategy": {"posture": "defensive", "goal": "hold potions for the elite"}}
        )

    def test_positional_callers_keep_their_meaning(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        with patch.object(client, "_request", return_value={}) as request:
            client.update_strategy("aggressive", "strip block first", {"play_card": "prefer attacks"})
        request.assert_called_once_with(
            "POST",
            "/strategy",
            {"strategy": {
                "posture": "aggressive",
                "instructions": "strip block first",
                "option_hints": {"play_card": "prefer attacks"},
            }},
        )

    def test_tool_forwards_goal(self) -> None:
        calls: list[dict[str, Any]] = []

        class _Client(BriefingClient):
            def update_strategy(self, **kwargs: Any) -> dict[str, Any]:
                calls.append(kwargs)
                return {"strategy": kwargs}

        server = create_server(client=_Client({}), tool_profile="guided")  # type: ignore[arg-type]
        tool = asyncio.run(server.get_tool("update_play_strategy"))
        tool.fn(goal="kill the weakest enemy first")
        self.assertEqual(calls[0]["goal"], "kill the weakest enemy first")
        self.assertIsNone(calls[0]["posture"])


if __name__ == "__main__":
    unittest.main()
