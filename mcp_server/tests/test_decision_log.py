from __future__ import annotations

import asyncio
import unittest

from sts2_mcp.server import create_server


class RecordingClient:
    """A client double that answers /decisions and records the limit it was asked for."""

    def __init__(self, decisions: list[dict]) -> None:
        self.decisions = decisions
        self.limits: list[int] = []

    def get_decisions(self, limit: int = 50) -> list[dict]:
        self.limits.append(limit)
        return self.decisions


class DecisionLogToolTests(unittest.TestCase):
    def test_guided_exposes_decision_log_with_a_limit_argument(self) -> None:
        client = RecordingClient([])
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("get_decision_log"))

        self.assertIsNotNone(tool)
        self.assertIn("limit", tool.parameters["properties"])

    def test_guided_decision_log_passes_the_limit_and_returns_entries(self) -> None:
        entries = [
            {"id": 1, "source": "agent_loop", "action": "play_card", "reason": "Strike first."},
            {"id": 2, "source": "agent_loop", "action": "end_turn", "reason": "No cards worth playing."},
        ]
        client = RecordingClient(entries)
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("get_decision_log"))

        result = tool.fn(limit=7)

        self.assertEqual(client.limits, [7])
        # The tool returns the same {"decisions": [...]} envelope as the native MCP surface.
        self.assertEqual(result, {"decisions": entries})

    def test_guided_decision_log_tolerates_an_empty_answer(self) -> None:
        # A mod that predates the route answers nothing useful; the tool must return an
        # empty envelope rather than leaking a None into the model's context.
        class Empty:
            def get_decisions(self, limit: int = 50):
                return None

        server = create_server(client=Empty(), tool_profile="guided")  # type: ignore[arg-type]
        tool = asyncio.run(server.get_tool("get_decision_log"))

        self.assertEqual(tool.fn(), {"decisions": []})


if __name__ == "__main__":
    unittest.main()
