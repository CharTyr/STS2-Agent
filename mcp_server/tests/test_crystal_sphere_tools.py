from __future__ import annotations

import asyncio
import unittest
from unittest.mock import patch

from sts2_mcp.client import Sts2Client
from sts2_mcp.server import create_server


class RecordingClient:
    def __init__(self, state: dict) -> None:
        self.state = state
        self.action_calls: list[tuple[str, dict]] = []

    def get_state(self) -> dict:
        return self.state

    def execute_action(self, action: str, **kwargs) -> dict:
        self.action_calls.append((action, kwargs))
        return {"action": action, "status": "completed", "stable": True}


class CrystalSphereToolTests(unittest.TestCase):
    def test_guided_get_game_state_exposes_crystal_sphere(self) -> None:
        crystal_sphere = {
            "divinations_left": 3,
            "grid_width": 11,
            "grid_height": 11,
            "hidden_cells": [[4, 5]],
            "items": [{"kind": "Relic", "hidden_cells": [[4, 5]]}],
        }
        client = RecordingClient(
            {
                "screen": "CRYSTAL_SPHERE",
                "crystal_sphere": crystal_sphere,
                "agent_view": {
                    "screen": "CRYSTAL_SPHERE",
                    "actions": ["crystal_clear_cell"],
                    "crystal_sphere": crystal_sphere,
                },
            }
        )
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("get_game_state"))

        result = tool.fn()

        self.assertEqual(result["screen"], "CRYSTAL_SPHERE")
        self.assertEqual(result["crystal_sphere"], crystal_sphere)
        self.assertEqual(result["available_actions"], ["crystal_clear_cell"])

    def test_guided_act_forwards_crystal_coordinates_and_tool(self) -> None:
        client = RecordingClient({"screen": "CRYSTAL_SPHERE"})
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("act"))

        tool_schema = tool.parameters["properties"]["tool"]
        string_schema = next(
            schema for schema in tool_schema["anyOf"] if schema.get("type") == "string"
        )
        self.assertEqual(string_schema["enum"], ["big", "small"])

        result = tool.fn(
            action="crystal_clear_cell",
            x=4,
            y=7,
            tool="small",
        )

        self.assertTrue(result["stable"])
        self.assertEqual(len(client.action_calls), 1)
        action, kwargs = client.action_calls[0]
        self.assertEqual(action, "crystal_clear_cell")
        self.assertEqual(kwargs["x"], 4)
        self.assertEqual(kwargs["y"], 7)
        self.assertEqual(kwargs["tool"], "small")

    def test_client_execute_action_posts_crystal_fields(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")

        with patch.object(client, "_request", return_value={"ok": True}) as request_mock:
            client.execute_action(
                "crystal_clear_cell",
                x=2,
                y=9,
                tool="big",
                client_context={"source": "test"},
            )

        request_mock.assert_called_once_with(
            "POST",
            "/action",
            payload={
                "action": "crystal_clear_cell",
                "card_index": None,
                "target_index": None,
                "option_index": None,
                "x": 2,
                "y": 9,
                "tool": "big",
                "command": None,
                "client_context": {"source": "test"},
            },
            is_action=True,
        )

    def test_full_profile_exposes_crystal_actions(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        with patch.object(
            client,
            "execute_action",
            return_value={"action": "crystal_clear_cell", "stable": True},
        ) as execute_mock:
            server = create_server(client=client, tool_profile="full")
            clear_tool = asyncio.run(server.get_tool("crystal_clear_cell"))
            set_tool = asyncio.run(server.get_tool("crystal_set_tool"))

            self.assertEqual(
                set_tool.parameters["properties"]["tool"]["enum"],
                ["big", "small"],
            )

            clear_tool.fn(x=5, y=6, tool="big")
            set_tool.fn(tool="small")

        self.assertEqual(execute_mock.call_count, 2)
        first = execute_mock.call_args_list[0]
        self.assertEqual(first.args, ("crystal_clear_cell",))
        self.assertEqual(first.kwargs["x"], 5)
        self.assertEqual(first.kwargs["y"], 6)
        self.assertEqual(first.kwargs["tool"], "big")
        second = execute_mock.call_args_list[1]
        self.assertEqual(second.args, ("crystal_set_tool",))
        self.assertEqual(second.kwargs["tool"], "small")


if __name__ == "__main__":
    unittest.main()
