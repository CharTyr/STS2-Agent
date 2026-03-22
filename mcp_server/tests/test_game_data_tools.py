from __future__ import annotations

import asyncio
import unittest
from unittest.mock import patch

import sts2_mcp.server as server_module
from sts2_mcp.knowledge import Sts2KnowledgeBase
from sts2_mcp.server import (
    _SCENE_FIELD_SETS,
    _build_agent_state_payload,
    _card_profile,
    create_server,
    get_game_data_items_fields,
)


class DummyClient:
    def __init__(self, screen: str = "MAIN_MENU", state: dict | None = None) -> None:
        self._screen = screen
        self._state = state

    def get_health(self) -> dict:
        return {"ok": True}

    def get_state(self) -> dict:
        if self._state is not None:
            return self._state
        return {"screen": self._screen, "available_actions": []}

    def get_available_actions(self) -> list[dict]:
        return []

    def wait_for_event(self, *, event_names=None, timeout=0.0) -> dict | None:
        return None

    def execute_action(self, *args, **kwargs) -> dict:
        return {"ok": True}


class GameDataToolsTests(unittest.TestCase):
    def test_get_game_data_item_returns_none_for_empty_item_id(self) -> None:
        client = DummyClient()
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_game_data_item"))

        result = tool.fn(collection="cards", item_id="")

        self.assertIsNone(result)

    def test_get_game_data_item_supports_case_insensitive_lookup(self) -> None:
        client = DummyClient()
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_game_data_item"))
        abrasive = {"id": "ABRASIVE", "name": "Abrasive"}

        with patch("sts2_mcp.server._ensure_game_data_index", return_value={"ABRASIVE": abrasive}):
            result = tool.fn(collection="cards", item_id="abrasive")

        self.assertEqual(result, abrasive)

    def test_get_game_data_items_returns_batch_result(self) -> None:
        client = DummyClient()
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_game_data_items"))
        abrasive = {"id": "ABRASIVE", "name": "Abrasive"}
        jolt = {"id": "JOLT", "name": "Jolt"}

        with patch("sts2_mcp.server._ensure_game_data_index", return_value={"ABRASIVE": abrasive, "JOLT": jolt}):
            result = tool.fn(collection="cards", item_ids="abrasive, jolt, unknown")

        self.assertEqual(result["abrasive"], abrasive)
        self.assertEqual(result["jolt"], jolt)
        self.assertIsNone(result["unknown"])

    def test_get_game_data_items_returns_empty_when_item_ids_is_empty(self) -> None:
        client = DummyClient()
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_game_data_items"))

        result = tool.fn(collection="cards", item_ids="")

        self.assertEqual(result, {})

    def test_get_game_data_items_returns_structured_error_for_unknown_collection(self) -> None:
        client = DummyClient()
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_game_data_items"))

        with patch(
            "sts2_mcp.server._ensure_game_data_index",
            side_effect=KeyError("Unknown game data collection: unknown"),
        ):
            result = tool.fn(collection="unknown", item_ids="ABRASIVE")

        self.assertIn("error", result)
        self.assertEqual(result["error"]["type"], "unknown_collection")
        self.assertEqual(result["error"]["collection"], "unknown")

    def test_get_game_data_item_returns_structured_error_for_unknown_collection(self) -> None:
        client = DummyClient()
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_game_data_item"))

        with patch(
            "sts2_mcp.server._ensure_game_data_index",
            side_effect=KeyError("Unknown game data collection: unknown"),
        ):
            result = tool.fn(collection="unknown", item_id="ABRASIVE")

        self.assertIn("error", result)
        self.assertEqual(result["error"]["type"], "unknown_collection")
        self.assertEqual(result["error"]["collection"], "unknown")

    def test_get_relevant_game_data_uses_scene_fields_for_combat(self) -> None:
        client = DummyClient(screen="COMBAT_REWARD")
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_relevant_game_data"))
        expected = {"ABRASIVE": {"id": "ABRASIVE"}}
        expected_fields = ",".join(_SCENE_FIELD_SETS["combat"]["cards"])

        with patch(
            "sts2_mcp.server.get_game_data_items_fields",
            return_value=expected,
        ) as get_game_data_items_fields_mock:
            result = tool.fn(collection="cards", item_ids="ABRASIVE")

        self.assertEqual(result, expected)
        get_game_data_items_fields_mock.assert_called_once_with(
            collection="cards",
            item_ids="ABRASIVE",
            fields=expected_fields,
        )

    def test_get_relevant_game_data_uses_scene_fields_for_shop(self) -> None:
        client = DummyClient(screen="SHOP")
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_relevant_game_data"))
        expected = {"JOLT": {"id": "JOLT"}}
        expected_fields = ",".join(_SCENE_FIELD_SETS["shop"]["cards"])

        with patch(
            "sts2_mcp.server.get_game_data_items_fields",
            return_value=expected,
        ) as get_game_data_items_fields_mock:
            result = tool.fn(collection="cards", item_ids="JOLT")

        self.assertEqual(result, expected)
        get_game_data_items_fields_mock.assert_called_once_with(
            collection="cards",
            item_ids="JOLT",
            fields=expected_fields,
        )

    def test_get_relevant_game_data_uses_scene_fields_for_event(self) -> None:
        client = DummyClient(screen="EVENT_ROOM")
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_relevant_game_data"))
        expected = {"MYSTERY": {"id": "MYSTERY"}}
        expected_fields = ",".join(_SCENE_FIELD_SETS["event"]["events"])

        with patch(
            "sts2_mcp.server.get_game_data_items_fields",
            return_value=expected,
        ) as get_game_data_items_fields_mock:
            result = tool.fn(collection="events", item_ids="MYSTERY")

        self.assertEqual(result, expected)
        get_game_data_items_fields_mock.assert_called_once_with(
            collection="events",
            item_ids="MYSTERY",
            fields=expected_fields,
        )

    def test_get_relevant_game_data_falls_back_when_scene_has_no_field_set(self) -> None:
        client = DummyClient(screen="MAIN_MENU")
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_relevant_game_data"))
        event_item = {"id": "MYSTERY", "name": "Mystery Event"}

        with patch("sts2_mcp.server._ensure_game_data_index", return_value={"MYSTERY": event_item}):
            with patch("sts2_mcp.server.get_game_data_items_fields") as get_game_data_items_fields_mock:
                result = tool.fn(collection="events", item_ids="MYSTERY")

        self.assertEqual(result, {"MYSTERY": event_item})
        get_game_data_items_fields_mock.assert_not_called()

    def test_get_relevant_game_data_falls_back_when_collection_has_no_scene_field_set(self) -> None:
        client = DummyClient(screen="COMBAT_REWARD")
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_relevant_game_data"))
        event_item = {"id": "MYSTERY", "name": "Mystery Event"}

        with patch("sts2_mcp.server._ensure_game_data_index", return_value={"MYSTERY": event_item}):
            with patch("sts2_mcp.server.get_game_data_items_fields") as get_game_data_items_fields_mock:
                result = tool.fn(collection="events", item_ids="MYSTERY")

        self.assertEqual(result, {"MYSTERY": event_item})
        get_game_data_items_fields_mock.assert_not_called()

    def test_get_relevant_game_data_event_scene_keeps_name_field_from_real_schema(self) -> None:
        client = DummyClient(screen="EVENT_ROOM")
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("get_relevant_game_data"))
        event_item = {
            "id": "MYSTERY",
            "name": "Mystery Event",
            "description": "A strange encounter.",
            "options": [{"id": "LEAVE"}],
            "type": "Event",
        }

        with patch("sts2_mcp.server._ensure_game_data_index", return_value={"MYSTERY": event_item}):
            result = tool.fn(collection="events", item_ids="MYSTERY")

        self.assertEqual(
            result["MYSTERY"],
            {
                "id": "MYSTERY",
                "name": "Mystery Event",
                "description": "A strange encounter.",
                "options": [{"id": "LEAVE"}],
            },
        )
        self.assertNotIn("title", result["MYSTERY"])

    def test_build_agent_state_payload_adds_route_options_to_compact_map(self) -> None:
        state = {
            "screen": "MAP",
            "available_actions": ["choose_map_node"],
            "map": {
                "nodes": [
                    {
                        "row": 0,
                        "col": 0,
                        "node_type": "Start",
                        "children": [{"row": 1, "col": 0}],
                    },
                    {
                        "row": 1,
                        "col": 0,
                        "node_type": "Elite",
                        "children": [{"row": 2, "col": 0}],
                    },
                    {
                        "row": 2,
                        "col": 0,
                        "node_type": "Rest",
                        "children": [],
                    },
                ],
                "available_nodes": [
                    {"index": 0, "row": 0, "col": 0, "node_type": "Start", "state": "Travelable"},
                ],
            },
            "agent_view": {
                "screen": "MAP",
                "actions": ["choose_map_node"],
                "map": {
                    "current": None,
                    "options": [{"i": 0, "line": "Start (0,0)"}],
                },
            },
        }

        result = _build_agent_state_payload(state, Sts2KnowledgeBase())

        self.assertEqual(result["available_actions"], ["choose_map_node"])
        self.assertIn("route_options", result["map"])
        self.assertEqual(len(result["map"]["route_options"]), 1)
        self.assertEqual(
            result["map"]["route_options"][0]["paths"][0]["node_types"],
            ["Start", "Elite", "Rest"],
        )

    def test_build_agent_state_payload_keeps_existing_route_options(self) -> None:
        state = {
            "screen": "MAP",
            "map": {
                "nodes": [],
                "available_nodes": [],
            },
            "agent_view": {
                "screen": "MAP",
                "available_actions": ["choose_map_node"],
                "map": {
                    "route_options": [{"start_node": {"row": 1, "col": 1}, "path_count": 1, "paths": []}],
                },
            },
        }

        result = _build_agent_state_payload(state, Sts2KnowledgeBase())

        self.assertEqual(
            result["map"]["route_options"],
            [{"start_node": {"row": 1, "col": 1}, "path_count": 1, "paths": []}],
        )

    def test_build_agent_state_payload_backfills_missing_sections_from_raw_state(self) -> None:
        state = {
            "screen": "COMBAT_REWARD",
            "available_actions": ["choose_reward_card"],
            "run": {
                "floor": 9,
                "current_hp": 38,
                "max_hp": 70,
            },
            "reward": {
                "cards": [{"i": 0, "card_id": "BACKFLIP", "name": "Backflip"}],
            },
            "agent_view": {
                "screen": "COMBAT_REWARD",
                "actions": ["choose_reward_card"],
            },
        }

        result = _build_agent_state_payload(state, Sts2KnowledgeBase())

        self.assertEqual(result["available_actions"], ["choose_reward_card"])
        self.assertEqual(result["run"]["current_hp"], 38)
        self.assertEqual(result["reward"]["cards"][0]["card_id"], "BACKFLIP")

    def test_evaluate_card_rewards_prioritizes_missing_aoe_in_act2(self) -> None:
        state = {
            "screen": "COMBAT_REWARD",
            "agent_view": {
                "screen": "COMBAT_REWARD",
                "run": {
                    "floor": 22,
                    "current_hp": 42,
                    "max_hp": 70,
                    "gold": 142,
                    "max_energy": 3,
                    "deck": [
                        {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5, "energy_cost": 1, "rules_text": "Deal 6 damage."},
                        {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5, "energy_cost": 1, "rules_text": "Gain 5 Block."},
                        {"card_id": "BACKFLIP", "name": "Backflip", "count": 2, "energy_cost": 1, "rules_text": "Gain 5 Block.\nDraw 2 cards."},
                        {"card_id": "FOOTWORK", "name": "Footwork", "count": 2, "energy_cost": 1, "rules_text": "Gain 2 Dexterity."},
                        {"card_id": "PREDATOR", "name": "Predator", "count": 1, "energy_cost": 2, "rules_text": "Deal 15 damage. Draw 2 cards next turn."},
                    ],
                    "potions": [
                        {"i": 0, "potion_id": "ASHWATER", "name": "Ashwater", "occupied": True},
                    ],
                    "relic_items": [{"i": 0, "relic_id": "AKABEKO", "name": "Akabeko"}],
                },
                "reward": {
                    "pending_card_choice": True,
                    "cards": [
                        {"i": 0, "card_id": "BACKFLIP", "name": "Backflip", "energy_cost": 1, "rules_text": "Gain 5 Block.\nDraw 2 cards."},
                        {"i": 1, "card_id": "DAGGER_SPRAY", "name": "Dagger Spray", "energy_cost": 1, "rules_text": "Deal 4 damage to ALL enemies twice."},
                        {"i": 2, "card_id": "FOOTWORK", "name": "Footwork", "energy_cost": 1, "rules_text": "Gain 2 Dexterity."},
                    ],
                },
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("evaluate_card_rewards"))

        result = tool.fn()

        self.assertEqual(result["best_index"], 1)
        self.assertEqual(result["reward_cards"][1]["card_id"], "DAGGER_SPRAY")
        self.assertEqual(result["reward_cards"][1]["recommendation"], "take")
        self.assertGreater(result["reward_cards"][1]["score"], result["reward_cards"][0]["score"])

    def test_evaluate_card_rewards_backfills_raw_run_and_reward_sections(self) -> None:
        state = {
            "screen": "COMBAT_REWARD",
            "available_actions": ["choose_reward_card"],
            "run": {
                "floor": 20,
                "current_hp": 41,
                "max_hp": 70,
                "gold": 120,
                "max_energy": 3,
                "deck": [
                    {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5, "energy_cost": 1},
                    {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5, "energy_cost": 1},
                ],
                "potions": [],
            },
            "reward": {
                "cards": [
                    {"i": 0, "card_id": "BACKFLIP", "name": "Backflip"},
                    {"i": 1, "card_id": "DAGGER_SPRAY", "name": "Dagger Spray"},
                ],
            },
            "agent_view": {
                "screen": "COMBAT_REWARD",
                "actions": ["choose_reward_card"],
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("evaluate_card_rewards"))

        result = tool.fn()

        self.assertEqual(result["best_index"], 1)
        self.assertEqual(result["reward_cards"][1]["card_id"], "DAGGER_SPRAY")

    def test_assess_elite_risk_flags_low_hp_weak_deck_as_avoid(self) -> None:
        state = {
            "screen": "MAP",
            "map": {
                "nodes": [
                    {"row": 1, "col": 0, "node_type": "Monster", "children": [{"row": 2, "col": 0}]},
                    {"row": 2, "col": 0, "node_type": "Elite", "children": [{"row": 3, "col": 0}]},
                    {"row": 3, "col": 0, "node_type": "Rest", "children": []},
                ],
                "available_nodes": [
                    {"index": 0, "row": 1, "col": 0, "node_type": "Monster", "state": "Travelable"},
                ],
            },
            "agent_view": {
                "screen": "MAP",
                "run": {
                    "floor": 21,
                    "current_hp": 18,
                    "max_hp": 70,
                    "gold": 96,
                    "max_energy": 3,
                    "deck": [
                        {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5, "energy_cost": 1, "rules_text": "Deal 6 damage."},
                        {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5, "energy_cost": 1, "rules_text": "Gain 5 Block."},
                        {"card_id": "SURVIVOR", "name": "Survivor", "count": 1, "energy_cost": 1, "rules_text": "Gain 8 Block. Discard 1 card."},
                        {"card_id": "NEUTRALIZE", "name": "Neutralize", "count": 1, "energy_cost": 0, "rules_text": "Deal 3 damage. Apply 1 Weak."},
                    ],
                    "potions": [],
                    "relic_items": [],
                },
                "map": {
                    "current": None,
                    "options": [{"i": 0, "row": 1, "col": 0, "type": "Monster", "line": "Monster (1,0)"}],
                },
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("assess_elite_risk"))

        result = tool.fn()

        self.assertEqual(result["recommendation"], "AVOID")
        self.assertGreaterEqual(result["risk_score"], 66)
        self.assertIn("当前血量过低", result["factors"]["negative"])

    def test_assess_elite_risk_parses_hp_summary_when_numeric_hp_missing(self) -> None:
        state = {
            "screen": "MAP",
            "agent_view": {
                "screen": "MAP",
                "run": {
                    "floor": 21,
                    "hp": "18/72",
                    "gold": 90,
                    "max_energy": 3,
                    "deck": [
                        {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5},
                        {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5},
                    ],
                    "potions": [],
                },
                "map": {
                    "route_options": [
                        {
                            "start_node": {"index": 0, "row": 1, "col": 0, "node_type": "Monster"},
                            "path_count": 1,
                            "paths": [{"node_types": ["Monster", "Elite", "Rest"]}],
                        },
                    ],
                },
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("assess_elite_risk"))

        result = tool.fn()

        self.assertAlmostEqual(result["hp_ratio"], 0.25, places=2)
        self.assertEqual(result["deck_summary"]["current_hp"], 18)

    def test_check_boss_readiness_reports_missing_core_checks(self) -> None:
        state = {
            "screen": "MAP",
            "agent_view": {
                "screen": "MAP",
                "run": {
                    "floor": 16,
                    "current_hp": 24,
                    "max_hp": 72,
                    "gold": 88,
                    "max_energy": 3,
                    "deck": [
                        {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5, "energy_cost": 1, "rules_text": "Deal 6 damage."},
                        {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5, "energy_cost": 1, "rules_text": "Gain 5 Block."},
                        {"card_id": "SURVIVOR", "name": "Survivor", "count": 1, "energy_cost": 1, "rules_text": "Gain 8 Block. Discard 1 card."},
                        {"card_id": "NEUTRALIZE", "name": "Neutralize", "count": 1, "energy_cost": 0, "rules_text": "Deal 3 damage. Apply 1 Weak."},
                    ],
                    "potions": [],
                    "relic_items": [],
                },
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("check_boss_readiness"))

        result = tool.fn()

        self.assertEqual(result["recommendation"], "NOT_READY")
        self.assertEqual(result["checks"]["hp"]["status"], "fail")
        self.assertEqual(result["checks"]["scaling"]["status"], "fail")
        self.assertEqual(result["checks"]["potions"]["status"], "fail")

    def test_card_profile_falls_back_to_metadata_cost(self) -> None:
        profile = _card_profile(
            {"card_id": "WHIRLWIND", "name": "Whirlwind"},
            {
                "id": "WHIRLWIND",
                "name": "Whirlwind",
                "cost": 0,
                "is_x_cost": False,
                "type": "Attack",
                "target": "AllEnemies",
                "damage": 5,
                "hit_count": None,
            },
        )

        self.assertEqual(profile["cost"], 0)
        self.assertFalse(profile["high_cost"])

    def test_evaluate_shop_options_prefers_card_removal_for_bloated_deck(self) -> None:
        state = {
            "screen": "SHOP",
            "agent_view": {
                "screen": "SHOP",
                "run": {
                    "floor": 23,
                    "current_hp": 44,
                    "max_hp": 70,
                    "gold": 95,
                    "max_energy": 3,
                    "deck": [
                        {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5, "energy_cost": 1, "rules_text": "Deal 6 damage."},
                        {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5, "energy_cost": 1, "rules_text": "Gain 5 Block."},
                        {"card_id": "SURVIVOR", "name": "Survivor", "count": 1, "energy_cost": 1, "rules_text": "Gain 8 Block. Discard 1 card."},
                        {"card_id": "NEUTRALIZE", "name": "Neutralize", "count": 1, "energy_cost": 0, "rules_text": "Deal 3 damage. Apply 1 Weak."},
                    ],
                    "potions": [],
                    "relic_items": [],
                },
                "shop": {
                    "open": True,
                    "cards": [
                        {
                            "i": 0,
                            "card_id": "BACKFLIP",
                            "name": "Backflip",
                            "rarity": "Common",
                            "energy_cost": 1,
                            "price": 82,
                            "affordable": True,
                            "stocked": True,
                            "rules_text": "Gain 5 Block.\nDraw 2 cards.",
                        },
                    ],
                    "relics": [],
                    "potions": [
                        {
                            "i": 0,
                            "potion_id": "BLOCK_POTION",
                            "name": "Block Potion",
                            "rarity": "Common",
                            "usage": "Gain 12 Block.",
                            "price": 45,
                            "affordable": True,
                            "stocked": True,
                        },
                    ],
                    "remove": {
                        "price": 75,
                        "affordable": True,
                        "available": True,
                        "used": False,
                    },
                },
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("evaluate_shop_options"))

        result = tool.fn()

        self.assertEqual(result["recommended_action"]["kind"], "remove_card_at_shop")
        self.assertIn(result["remove"]["candidate_cards"][0]["name"], {"Strike", "Defend"})
        self.assertGreaterEqual(result["remove"]["score"], 68)

    def test_assess_rest_site_prefers_rest_on_low_hp(self) -> None:
        state = {
            "screen": "REST",
            "agent_view": {
                "screen": "REST",
                "run": {
                    "floor": 24,
                    "current_hp": 19,
                    "max_hp": 72,
                    "gold": 120,
                    "max_energy": 3,
                    "deck": [
                        {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5, "energy_cost": 1, "rules_text": "Deal 6 damage."},
                        {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5, "energy_cost": 1, "rules_text": "Gain 5 Block."},
                        {"card_id": "DAGGER_SPRAY", "name": "Dagger Spray", "count": 1, "energy_cost": 1, "rules_text": "Deal 4 damage to ALL enemies twice."},
                        {"card_id": "FOOTWORK", "name": "Footwork", "count": 1, "energy_cost": 1, "rules_text": "Gain 2 Dexterity."},
                    ],
                    "potions": [],
                    "relic_items": [],
                },
                "rest": {
                    "options": [
                        {"i": 0, "option_id": "REST", "line": "Rest: Recover HP", "enabled": True},
                        {"i": 1, "option_id": "SMITH", "line": "Smith: Upgrade a card", "enabled": True},
                    ],
                },
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("assess_rest_site"))

        result = tool.fn()

        self.assertEqual(result["best_index"], 0)
        self.assertEqual(result["options"][0]["kind"], "rest")
        self.assertEqual(result["options"][0]["recommendation"], "take")

    def test_evaluate_potions_recommends_buying_aoe_when_slot_open(self) -> None:
        state = {
            "screen": "SHOP",
            "agent_view": {
                "screen": "SHOP",
                "run": {
                    "floor": 22,
                    "current_hp": 28,
                    "max_hp": 70,
                    "gold": 80,
                    "max_energy": 3,
                    "deck": [
                        {"card_id": "STRIKE_SILENT", "name": "Strike", "count": 5, "energy_cost": 1, "rules_text": "Deal 6 damage."},
                        {"card_id": "DEFEND_SILENT", "name": "Defend", "count": 5, "energy_cost": 1, "rules_text": "Gain 5 Block."},
                        {"card_id": "SURVIVOR", "name": "Survivor", "count": 1, "energy_cost": 1, "rules_text": "Gain 8 Block. Discard 1 card."},
                    ],
                    "potions": [
                        {"i": 0, "potion_id": "BLOCK_POTION", "name": "Block Potion", "occupied": True, "rarity": "Common"},
                        {"i": 1, "occupied": False},
                    ],
                    "relic_items": [],
                },
                "shop": {
                    "potions": [
                        {
                            "i": 0,
                            "potion_id": "EXPLOSIVE_AMPOULE",
                            "name": "Explosive Ampoule",
                            "rarity": "Common",
                            "usage": "Deal 10 damage to ALL enemies.",
                            "price": 42,
                            "affordable": True,
                            "stocked": True,
                        },
                    ],
                },
            },
        }
        client = DummyClient(state=state)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("evaluate_potions"))

        result = tool.fn()

        self.assertEqual(result["recommended_purchase"]["option_index"], 0)
        self.assertEqual(result["recommended_purchase"]["potion_id"], "EXPLOSIVE_AMPOULE")
        self.assertIsNone(result["recommended_purchase"]["replace_index"])

    def test_get_game_data_items_fields_filters_fields(self) -> None:
        with patch(
            "sts2_mcp.server._ensure_game_data_index",
            return_value={
                "ABRASIVE": {"id": "ABRASIVE", "name": "Abrasive", "cost": 2},
                "JOLT": {"id": "JOLT", "name": "Jolt", "cost": 1},
            },
        ):
            result = get_game_data_items_fields(
                collection="cards",
                item_ids="ABRASIVE, JOLT, UNKNOWN",
                fields="id,name",
            )

        self.assertEqual(result["ABRASIVE"], {"id": "ABRASIVE", "name": "Abrasive"})
        self.assertEqual(result["JOLT"], {"id": "JOLT", "name": "Jolt"})
        self.assertIsNone(result["UNKNOWN"])

    def test_get_game_data_items_fields_returns_full_item_when_fields_empty_or_none(self) -> None:
        payload = {
            "ABRASIVE": {"id": "ABRASIVE", "name": "Abrasive", "cost": 2},
        }
        with patch("sts2_mcp.server._ensure_game_data_index", return_value=payload):
            result_with_empty_fields = get_game_data_items_fields(
                collection="cards",
                item_ids="ABRASIVE",
                fields="",
            )
            result_with_none_fields = get_game_data_items_fields(
                collection="cards",
                item_ids="ABRASIVE",
                fields=None,
            )

        self.assertEqual(result_with_empty_fields["ABRASIVE"], payload["ABRASIVE"])
        self.assertEqual(result_with_none_fields["ABRASIVE"], payload["ABRASIVE"])

    def test_ensure_game_data_index_supports_case_insensitive_lookup_for_dict_collection(self) -> None:
        with patch.object(server_module, "_GAME_DATA_INDEXES", {}):
            with patch(
                "sts2_mcp.server._load_game_data",
                return_value={"cards": {"ABRASIVE": {"id": "ABRASIVE", "name": "Abrasive"}}},
            ):
                index = server_module._ensure_game_data_index("cards")

        self.assertEqual(index["ABRASIVE"]["id"], "ABRASIVE")
        self.assertEqual(index["abrasive"]["id"], "ABRASIVE")
        self.assertEqual(server_module._lookup_game_data_item(index=index, item_id="Abrasive")["id"], "ABRASIVE")


if __name__ == "__main__":
    unittest.main()
