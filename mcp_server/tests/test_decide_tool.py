"""`decide`: the three answers one decision needs, from a single call.

The documented loop is get_game_state -> get_available_actions -> act plus a guidance read, and each
of those rebuilt the whole state on the game thread. `decide` answers state, actions, and guidance
together so a step costs one call; the individual tools stay available.
"""

from __future__ import annotations

import asyncio
import unittest

from sts2_mcp.server import create_server


STATE = {
    "screen": "COMBAT",
    "run": {"character_id": "IRONCLAD", "floor": 3},
    "available_actions": ["play_card", "end_turn"],
    "combat": {"hand": [{"i": 0, "card_id": "STRIKE"}], "enemies": [{"i": 0}]},
    "agent_view": {
        "version": 11,
        "screen": "COMBAT",
        "available_actions": ["play_card", "end_turn"],
        "combat": {"hand": [{"i": 0, "card_id": "STRIKE"}], "enemies": [{"i": 0}]},
    },
}

DESCRIPTORS = [
    {"name": "play_card", "requires_index": True, "requires_target": False},
    {"name": "end_turn", "requires_index": False, "requires_target": False},
]


class RecordingClient:
    def __init__(self) -> None:
        self.state_calls = 0
        self.action_calls = 0

    def get_state(self) -> dict:
        self.state_calls += 1
        return STATE

    def get_available_actions(self) -> list[dict]:
        self.action_calls += 1
        return DESCRIPTORS


class DecideToolTests(unittest.TestCase):
    def _tool(self, client: RecordingClient):
        server = create_server(client=client, tool_profile="guided")  # type: ignore[arg-type]
        return asyncio.run(server.get_tool("decide"))

    def test_decide_answers_all_three_parts(self) -> None:
        client = RecordingClient()

        result = self._tool(client).fn()

        self.assertEqual(set(result), {"state", "available_actions", "scene_guidance"})
        self.assertEqual(result["state"]["screen"], "COMBAT")
        self.assertIs(result["state"]["compact_agent_view"], True)
        self.assertEqual(result["available_actions"], DESCRIPTORS)
        self.assertEqual(result["scene_guidance"]["screen"], "COMBAT")

    def test_decide_reads_the_state_once(self) -> None:
        client = RecordingClient()

        self._tool(client).fn()

        self.assertEqual(1, client.state_calls, "decide must not re-read the state per part")
        self.assertEqual(1, client.action_calls)

    def test_decide_state_is_the_get_game_state_shape(self) -> None:
        client = RecordingClient()
        server = create_server(client=client, tool_profile="guided")  # type: ignore[arg-type]

        decided = asyncio.run(server.get_tool("decide")).fn()
        read = asyncio.run(server.get_tool("get_game_state")).fn()

        self.assertEqual(read, decided["state"])

    def test_decide_guidance_is_the_get_scene_guidance_shape(self) -> None:
        client = RecordingClient()
        server = create_server(client=client, tool_profile="guided")  # type: ignore[arg-type]

        decided = asyncio.run(server.get_tool("decide")).fn()
        guidance = asyncio.run(server.get_tool("get_scene_guidance")).fn()

        self.assertEqual(guidance, decided["scene_guidance"])
        for key in ("screen", "scene", "guidance", "playbook"):
            self.assertIn(key, decided["scene_guidance"])

    def test_decide_takes_no_arguments(self) -> None:
        # The whole point is one call with nothing to assemble; an argument would make the cheapest
        # tool in the surface the one a client has to think about.
        schema = self._tool(RecordingClient()).parameters

        self.assertEqual({}, schema.get("properties", {}))

    def test_decide_is_documented_for_the_model(self) -> None:
        description = self._tool(RecordingClient()).fn.__doc__ or ""

        self.assertIn("get_game_state", description)
        self.assertIn("playbook", description)


if __name__ == "__main__":
    unittest.main()
