from __future__ import annotations

import asyncio
import unittest
from unittest.mock import patch

import sts2_mcp.server as server_module
from sts2_mcp.server import _SCENE_FIELD_SETS, create_server, get_game_data_items_fields


class DummyClient:
    def __init__(self, screen: str = "MAIN_MENU") -> None:
        self._screen = screen

    def get_health(self) -> dict:
        return {"ok": True}

    def get_state(self) -> dict:
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
