from __future__ import annotations

import asyncio
import json
import os
import tempfile
import unittest
from copy import deepcopy
from pathlib import Path
from unittest.mock import patch

from sts2_mcp.server import create_server


def _combat_state(turn: int) -> dict:
    return {
        "run_id": "RUN123",
        "screen": "COMBAT",
        "turn": turn,
        "available_actions": ["end_turn", "play_card"],
        "session": {
            "mode": "singleplayer",
            "phase": "run",
            "control_scope": "local_player",
        },
        "run": {
            "character_id": "SILENT",
            "character_name": "静默猎手",
            "floor": 7,
            "current_hp": 60,
            "max_hp": 70,
            "gold": 123,
            "max_energy": 3,
            "deck": [{"card_id": "NEUTRALIZE"}],
            "relics": [{"relic_id": "RING_OF_THE_SNAKE"}],
            "potions": [],
        },
        "combat": {
            "player": {
                "current_hp": 60,
                "max_hp": 70,
                "block": 0,
                "energy": 3,
                "powers": [],
            },
            "hand": [
                {
                    "index": 0,
                    "card_id": "NEUTRALIZE",
                    "name": "中和",
                    "energy_cost": 0,
                    "playable": True,
                    "requires_target": True,
                    "target_index_space": "enemies",
                    "valid_target_indices": [0],
                }
            ],
            "draw": [],
            "discard": [],
            "exhaust": [],
            "enemies": [
                {
                    "index": 0,
                    "enemy_id": "CULTIST",
                    "name": "邪教徒",
                    "current_hp": 48,
                    "max_hp": 48,
                    "block": 0,
                    "intent": "INCANTATION",
                    "move_id": "INCANTATION",
                    "powers": [],
                    "intents": [{"intent_type": "Buff"}],
                }
            ],
        },
    }


def _reward_state() -> dict:
    return {
        "run_id": "RUN123",
        "screen": "CARD_SELECTION",
        "turn": 10,
        "available_actions": ["choose_reward_card", "skip_reward_cards"],
        "session": {
            "mode": "singleplayer",
            "phase": "run",
            "control_scope": "local_player",
        },
        "run": {
            "character_id": "SILENT",
            "character_name": "静默猎手",
            "floor": 28,
            "current_hp": 52,
            "max_hp": 75,
            "gold": 936,
            "max_energy": 3,
            "deck": [{"card_id": "NOXIOUS_FUMES"}],
            "relics": [{"relic_id": "GORGET"}],
            "potions": [],
        },
        "reward": {
            "pending_card_choice": True,
            "can_proceed": False,
            "rewards": [],
            "card_options": [
                {
                    "index": 0,
                    "card_id": "FOOTWORK",
                    "name": "灵动步法",
                    "upgraded": False,
                    "rules_text": "获得2点敏捷。",
                }
            ],
            "alternatives": [{"index": 0, "label": "跳过"}],
        },
    }


def _map_state() -> dict:
    return {
        "run_id": "RUN123",
        "screen": "MAP",
        "turn": 10,
        "available_actions": ["choose_map_node"],
        "session": {
            "mode": "singleplayer",
            "phase": "run",
            "control_scope": "local_player",
        },
        "run": {
            "character_id": "SILENT",
            "character_name": "静默猎手",
            "floor": 28,
            "current_hp": 52,
            "max_hp": 75,
            "gold": 936,
            "max_energy": 3,
            "deck": [{"card_id": "NOXIOUS_FUMES"}, {"card_id": "FOOTWORK"}],
            "relics": [{"relic_id": "GORGET"}],
            "potions": [],
        },
        "map": {
            "current_node": {"row": 9, "col": 3, "node_type": "Elite"},
            "available_nodes": [
                {"index": 0, "row": 10, "col": 2, "node_type": "RestSite", "state": "Travelable"}
            ],
        },
    }


class LoggingDummyClient:
    def __init__(self, states: list[dict], action_response: dict | None = None) -> None:
        self._states = [deepcopy(state) for state in states]
        self._action_response = deepcopy(action_response) if action_response is not None else None

    def get_health(self) -> dict:
        return {"ok": True}

    def get_state(self) -> dict:
        if len(self._states) > 1:
            return self._states.pop(0)
        return deepcopy(self._states[0])

    def get_available_actions(self) -> list[dict]:
        return []

    def wait_for_event(self, *, event_names=None, timeout=0.0) -> dict | None:
        return None

    def execute_action(self, *args, **kwargs) -> dict:
        if self._action_response is None:
            return {"status": "completed"}
        return deepcopy(self._action_response)


class RunLogTests(unittest.TestCase):
    def test_get_game_state_logs_each_new_turn_once(self) -> None:
        with tempfile.TemporaryDirectory() as tempdir:
            client = LoggingDummyClient(
                states=[
                    _combat_state(turn=1),
                    _combat_state(turn=1),
                    _combat_state(turn=2),
                ]
            )

            with patch.dict(os.environ, {"STS2_AGENT_KNOWLEDGE_DIR": tempdir}, clear=False):
                server = create_server(client=client)
                tool = asyncio.run(server.get_tool("get_game_state"))

                tool.fn()
                tool.fn()
                tool.fn()

            entries = self._read_entries(Path(tempdir) / "logs" / "runs" / "RUN123.jsonl")
            state_entries = [entry for entry in entries if entry["type"] == "state"]

            self.assertEqual(len(state_entries), 2)
            self.assertEqual([entry["summary"]["turn"] for entry in state_entries], [1, 2])
            self.assertTrue(all(entry["category"] == "combat_turn" for entry in state_entries))

    def test_reward_action_logs_reward_state_and_post_action_state(self) -> None:
        with tempfile.TemporaryDirectory() as tempdir:
            client = LoggingDummyClient(
                states=[_reward_state()],
                action_response={
                    "action": "choose_reward_card",
                    "status": "completed",
                    "stable": True,
                    "message": "Action completed.",
                    "state": _map_state(),
                },
            )

            with patch.dict(os.environ, {"STS2_AGENT_KNOWLEDGE_DIR": tempdir}, clear=False):
                server = create_server(client=client)
                state_tool = asyncio.run(server.get_tool("get_game_state"))
                act_tool = asyncio.run(server.get_tool("act"))

                state_tool.fn()
                act_tool.fn(action="choose_reward_card", option_index=0)

            entries = self._read_entries(Path(tempdir) / "logs" / "runs" / "RUN123.jsonl")
            reward_entries = [entry for entry in entries if entry["type"] == "state" and entry["category"] == "reward"]
            action_entries = [entry for entry in entries if entry["type"] == "action"]
            map_entries = [
                entry
                for entry in entries
                if entry["type"] == "state" and entry["summary"].get("screen") == "MAP"
            ]

            self.assertEqual(reward_entries[0]["summary"]["reward"]["card_options"][0]["card_id"], "FOOTWORK")
            self.assertEqual(len(action_entries), 1)
            self.assertEqual(action_entries[0]["action"], "choose_reward_card")
            self.assertEqual(action_entries[0]["params"]["option_index"], 0)
            self.assertEqual(action_entries[0]["after_state"]["screen"], "MAP")
            self.assertEqual(len(map_entries), 1)

    @staticmethod
    def _read_entries(path: Path) -> list[dict]:
        with path.open("r", encoding="utf-8") as handle:
            return [json.loads(line) for line in handle if line.strip()]


if __name__ == "__main__":
    unittest.main()
