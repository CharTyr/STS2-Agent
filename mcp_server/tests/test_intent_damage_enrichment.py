from __future__ import annotations

import asyncio
import unittest
from typing import Any

from sts2_mcp.server import create_server


class DummyClient:
    def __init__(self, state: dict[str, Any], monsters: list[dict[str, Any]]) -> None:
        self._state = state
        self._monsters = monsters

    def get_health(self) -> dict[str, Any]:
        return {"ok": True}

    def get_state(self) -> dict[str, Any]:
        return self._state

    def get_available_actions(self) -> list[dict[str, Any]]:
        return []

    def wait_for_event(self, *, event_names=None, timeout=0.0) -> dict[str, Any] | None:
        return None

    def get_game_data_collection(self, collection: str) -> object:
        if collection == "monsters":
            return self._monsters
        return []


class IntentDamageEnrichmentTests(unittest.TestCase):
    def test_get_game_state_enriches_known_monster_moves_with_intent_damage(self) -> None:
        client = DummyClient(
            state={
                "agent_view": {
                    "screen": "COMBAT",
                    "available_actions": [],
                    "combat": {
                        "enemies": [
                            {
                                "enemy_id": "CORPSE_SLUG",
                                "name": "Corpse Slug",
                                "move_id": "GLOMP_MOVE",
                                "intent": "GLOMP_MOVE",
                            },
                            {
                                "enemy_id": "CORPSE_SLUG",
                                "name": "Corpse Slug",
                                "move_id": "GOOP_MOVE",
                                "intent": "GOOP_MOVE",
                            },
                        ]
                    },
                }
            },
            monsters=[
                {
                    "id": "CORPSE_SLUG",
                    "name": "Corpse Slug",
                    "moves": [
                        {"id": "GLOMP", "name": "Glomp"},
                        {"id": "GOOP", "name": "Goop"},
                    ],
                    "damage_values": {"Glomp": {"normal": 8}},
                }
            ],
        )
        state_tool = asyncio.run(create_server(client=client).get_tool("get_game_state"))

        state = state_tool.fn()

        glomp, goop = state["combat"]["enemies"]
        self.assertEqual(glomp.get("intent_damage"), 8)
        self.assertIs(glomp.get("intent_damage_known"), True)
        self.assertEqual(glomp.get("intent_damage_source"), "monster_data")
        self.assertEqual(goop.get("intent_damage"), 0)
        self.assertIs(goop.get("intent_damage_known"), True)
        self.assertEqual(goop.get("intent_damage_source"), "known_non_attack_move")

    def test_get_game_state_marks_ambiguous_monster_damage_unknown(self) -> None:
        client = DummyClient(
            state={
                "agent_view": {
                    "screen": "COMBAT",
                    "available_actions": [],
                    "combat": {
                        "enemies": [
                            {
                                "enemy_id": "CUBEX_CONSTRUCT",
                                "name": "Cubex Construct",
                                "move_id": "EXPEL_BLAST_MOVE",
                                "intent": "EXPEL_BLAST_MOVE",
                            }
                        ]
                    },
                }
            },
            monsters=[
                {
                    "id": "CUBEX_CONSTRUCT",
                    "name": "Cubex Construct",
                    "moves": [{"id": "EXPEL_BLAST", "name": "Expel Blast"}],
                    "damage_values": {
                        "Blast": {"normal": 7},
                        "Expel": {"normal": 5},
                    },
                }
            ],
        )
        state_tool = asyncio.run(create_server(client=client).get_tool("get_game_state"))

        state = state_tool.fn()

        enemy = state["combat"]["enemies"][0]
        self.assertNotIn("intent_damage", enemy)
        self.assertIs(enemy.get("intent_damage_known"), False)
        self.assertEqual(enemy.get("intent_damage_source"), "ambiguous_monster_data")

    def test_get_game_state_enriches_seapunk_attack_moves_from_damage_values(self) -> None:
        client = DummyClient(
            state={
                "agent_view": {
                    "screen": "COMBAT",
                    "available_actions": [],
                    "combat": {
                        "enemies": [
                            {
                                "enemy_id": "SEAPUNK",
                                "name": "Seapunk",
                                "move_id": "SEA_KICK_MOVE",
                                "intent": "SEA_KICK_MOVE",
                            },
                            {
                                "enemy_id": "SEAPUNK",
                                "name": "Seapunk",
                                "move_id": "SPINNING_KICK_MOVE",
                                "intent": "SPINNING_KICK_MOVE",
                            },
                        ]
                    },
                }
            },
            monsters=[
                {
                    "id": "SEAPUNK",
                    "name": "Seapunk",
                    "moves": [
                        {"id": "SEA_KICK", "name": "Sea Kick"},
                        {"id": "SPINNING_KICK", "name": "Spinning Kick"},
                    ],
                    "damage_values": {
                        "Sea Kick": {"normal": 11},
                        "Spinning Kick": {"normal": 2},
                    },
                }
            ],
        )
        state_tool = asyncio.run(create_server(client=client).get_tool("get_game_state"))

        state = state_tool.fn()

        sea_kick, spinning_kick = state["combat"]["enemies"]
        self.assertEqual(sea_kick.get("intent_damage"), 11)
        self.assertIs(sea_kick.get("intent_damage_known"), True)
        self.assertEqual(sea_kick.get("intent_damage_source"), "monster_data")
        self.assertEqual(spinning_kick.get("intent_damage"), 2)
        self.assertIs(spinning_kick.get("intent_damage_known"), True)
        self.assertEqual(spinning_kick.get("intent_damage_source"), "monster_data")

    def test_get_game_state_does_not_mark_known_move_without_damage_values_non_attack(
        self,
    ) -> None:
        client = DummyClient(
            state={
                "agent_view": {
                    "screen": "COMBAT",
                    "available_actions": [],
                    "combat": {
                        "enemies": [
                            {
                                "enemy_id": "SEAPUNK",
                                "name": "Seapunk",
                                "move_id": "SEA_KICK_MOVE",
                                "intent": "SEA_KICK_MOVE",
                            }
                        ]
                    },
                }
            },
            monsters=[
                {
                    "id": "SEAPUNK",
                    "name": "Seapunk",
                    "moves": [{"id": "SEA_KICK", "name": "Sea Kick"}],
                }
            ],
        )
        state_tool = asyncio.run(create_server(client=client).get_tool("get_game_state"))

        state = state_tool.fn()

        enemy = state["combat"]["enemies"][0]
        self.assertNotIn("intent_damage", enemy)
        self.assertIs(enemy.get("intent_damage_known"), False)
        self.assertEqual(enemy.get("intent_damage_source"), "unknown_monster_move_damage")

    def test_get_game_state_overrides_stale_zero_when_damage_values_identify_attack(
        self,
    ) -> None:
        client = DummyClient(
            state={
                "agent_view": {
                    "screen": "COMBAT",
                    "available_actions": [],
                    "combat": {
                        "enemies": [
                            {
                                "enemy_id": "SEAPUNK",
                                "name": "Seapunk",
                                "move_id": "SEA_KICK_MOVE",
                                "intent": "SEA_KICK_MOVE",
                                "intent_damage": 0,
                                "intent_damage_known": True,
                                "intent_damage_source": "known_non_attack_move",
                            }
                        ]
                    },
                }
            },
            monsters=[
                {
                    "id": "SEAPUNK",
                    "name": "Seapunk",
                    "moves": [{"id": "SEA_KICK", "name": "Sea Kick"}],
                    "damage_values": {"Sea Kick": {"normal": 11}},
                }
            ],
        )
        state_tool = asyncio.run(create_server(client=client).get_tool("get_game_state"))

        state = state_tool.fn()

        enemy = state["combat"]["enemies"][0]
        self.assertEqual(enemy.get("intent_damage"), 11)
        self.assertIs(enemy.get("intent_damage_known"), True)
        self.assertEqual(enemy.get("intent_damage_source"), "monster_data")


if __name__ == "__main__":
    unittest.main()
