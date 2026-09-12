from __future__ import annotations

import asyncio
import os
import tempfile
import unittest
from pathlib import Path
from typing import Any
from unittest.mock import patch

from sts2_mcp.server import create_server


PLANNER_CONTEXT_KEYS = {
    "screen",
    "session",
    "available_actions",
    "planner_note",
    "run_summary",
    "map",
    "route_options",
    "reward",
    "event",
    "rest",
    "shop",
    "event_knowledge",
    "reference_files",
}

COMBAT_CONTEXT_KEYS = {
    "screen",
    "turn",
    "session",
    "available_actions",
    "planner_note",
    "run_summary",
    "combat",
    "knowledge",
    "reference_files",
}

PLANNER_HANDOFF_KEYS = {
    "handoff_type",
    "reset_context",
    "system_prompt",
    "instructions",
    "planning_focus",
    "previous_combat_summary",
    "context",
}

COMBAT_HANDOFF_KEYS = {
    "handoff_type",
    "reset_context",
    "system_prompt",
    "instructions",
    "planner_message",
    "combat_objective",
    "combat_key",
    "enemy_ids",
    "context",
}

KNOWLEDGE_ENTRY_KEYS = {"category", "key", "path", "relative_path", "exists", "content"}


class DummyClient:
    """Offline client: replays one state and counts how often get_state is read."""

    def __init__(self, state: dict[str, Any]) -> None:
        self._state = state
        self.get_state_calls = 0

    def get_state(self) -> dict[str, Any]:
        self.get_state_calls += 1
        return self._state

    def get_health(self) -> dict[str, Any]:
        return {"ok": True}

    def get_available_actions(self) -> list[dict[str, Any]]:
        actions = self._state.get("available_actions")
        return list(actions) if isinstance(actions, list) else []


def _run_payload(floor: int = 3) -> dict[str, Any]:
    return {
        "character_id": "IRONCLAD",
        "character_name": "Ironclad",
        "floor": floor,
        "current_hp": 70,
        "max_hp": 80,
        "gold": 120,
        "max_energy": 3,
        "base_orb_slots": 3,
        "deck": [{"id": "STRIKE"}],
        "relics": [{"id": "BURNING_BLOOD"}],
        "potions": [],
    }


def planner_state() -> dict[str, Any]:
    """Non-event planner state: no event_id, so no event knowledge should be written."""
    return {
        "screen": "MAP",
        "run_id": "run_alpha",
        "session": {"multiplayer": False},
        "available_actions": [{"name": "choose_map_node"}],
        "run": _run_payload(),
        "map": {},
        "reward": None,
        "event": None,
        "rest": None,
        "shop": None,
    }


def event_state() -> dict[str, Any]:
    return {
        "screen": "EVENT",
        "run_id": "run_alpha",
        "session": {"multiplayer": False},
        "available_actions": [{"name": "choose_event_option"}],
        "run": _run_payload(floor=5),
        "map": {},
        "event": {"event_id": "Big Fish", "title": "Big Fish"},
    }


def combat_state(enemy_ids: tuple[str, ...] = ("Jaw Worm", "JAW_WORM", "Louse")) -> dict[str, Any]:
    return {
        "screen": "COMBAT",
        "turn": 2,
        "run_id": "run_alpha",
        "session": {"multiplayer": False},
        "available_actions": [{"name": "end_turn"}],
        "run": _run_payload(),
        "combat": {"enemies": [{"enemy_id": enemy_id} for enemy_id in enemy_ids]},
    }


class LayeredToolTestBase(unittest.TestCase):
    def setUp(self) -> None:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.knowledge_dir = Path(tmp.name).resolve()

    def build(self, state: dict[str, Any]) -> tuple[Any, DummyClient]:
        """Build a layered server whose knowledge root is pinned inside a temp directory."""
        client = DummyClient(state)
        with patch.dict(
            os.environ,
            {"STS2_AGENT_KNOWLEDGE_DIR": str(self.knowledge_dir)},
            clear=False,
        ):
            server = create_server(client=client, tool_profile="layered")
        return server, client

    def tool_fn(self, server: Any, name: str) -> Any:
        return asyncio.run(server.get_tool(name)).fn

    def assert_under_temp(self, value: Any) -> Path:
        path = Path(str(value))
        self.assertTrue(
            path.is_relative_to(self.knowledge_dir),
            f"{path} is not inside the scratch knowledge dir {self.knowledge_dir}",
        )
        return path

    def assert_no_writes(self) -> None:
        self.assertEqual(list(self.knowledge_dir.rglob("*")), [])

    def assert_relative_path(self, entry: dict[str, Any], expected: str) -> None:
        # relative_path is built with the host separator, so compare in posix form.
        self.assertEqual(Path(str(entry["relative_path"])).as_posix(), expected)


class PlannerToolTests(LayeredToolTestBase):
    def test_get_planner_context_without_event_stays_offline(self) -> None:
        server, client = self.build(planner_state())

        payload = self.tool_fn(server, "get_planner_context")()

        self.assertTrue(PLANNER_CONTEXT_KEYS.issubset(payload.keys()))
        self.assertIsNone(payload["event_knowledge"])
        self.assertIsNone(payload["event"])
        self.assertEqual(payload["route_options"], [])
        self.assertEqual(payload["available_actions"], [{"name": "choose_map_node"}])
        self.assertEqual(payload["run_summary"]["floor"], 3)
        self.assertEqual(client.get_state_calls, 1)
        self.assert_no_writes()

    def test_get_planner_context_links_event_knowledge(self) -> None:
        server, _ = self.build(event_state())

        payload = self.tool_fn(server, "get_planner_context")(planner_note="take the relic")

        self.assertEqual(payload["planner_note"], "take the relic")
        entry = payload["event_knowledge"]
        self.assertEqual(entry["category"], "event")
        self.assertEqual(entry["key"], "big_fish")
        self.assert_relative_path(entry, "events/global/big_fish.md")
        self.assertTrue(entry["exists"])
        self.assert_under_temp(entry["path"])

    def test_create_planner_handoff_packet_shape(self) -> None:
        server, _ = self.build(planner_state())

        payload = self.tool_fn(server, "create_planner_handoff")(
            planning_focus="pick the elite",
            previous_combat_summary="won the last fight",
        )

        self.assertTrue(PLANNER_HANDOFF_KEYS.issubset(payload.keys()))
        self.assertEqual(payload["handoff_type"], "planner")
        self.assertTrue(payload["reset_context"])
        self.assertTrue(payload["context"]["run_summary"])
        self.assertEqual(payload["planning_focus"], "pick the elite")
        self.assertEqual(payload["previous_combat_summary"], "won the last fight")
        self.assertEqual(payload["context"]["planner_note"], "pick the elite")
        self.assertIsNone(payload["context"]["event_knowledge"])

        instructions = payload["instructions"]
        self.assertEqual(len(instructions), 5)
        self.assertTrue(any("Handle non-combat flow only" in line for line in instructions))
        self.assertTrue(any("hard action boundary" in line for line in instructions))
        self.assert_no_writes()


class CombatToolTests(LayeredToolTestBase):
    def test_get_combat_context_dedupes_and_sorts_enemy_key(self) -> None:
        server, _ = self.build(combat_state())

        payload = self.tool_fn(server, "get_combat_context")()

        self.assertTrue(COMBAT_CONTEXT_KEYS.issubset(payload.keys()))
        knowledge = payload["knowledge"]
        self.assertTrue(KNOWLEDGE_ENTRY_KEYS.issubset(knowledge.keys()))
        self.assertEqual(knowledge["category"], "combat")
        self.assertEqual(knowledge["key"], "jaw_worm_x2+louse_x1")
        self.assert_relative_path(knowledge, "combat/global/groups/jaw_worm_x2+louse_x1.md")
        self.assertTrue(knowledge["exists"])
        self.assert_under_temp(knowledge["path"])
        self.assertIn("# Known Patterns", knowledge["content"])

    def test_get_combat_context_can_omit_knowledge_body(self) -> None:
        server, _ = self.build(combat_state())

        payload = self.tool_fn(server, "get_combat_context")(include_knowledge=False)

        self.assertEqual(payload["knowledge"]["content"], "")
        self.assertTrue(payload["knowledge"]["exists"])

    def test_get_combat_context_uses_solo_directory_for_single_enemy(self) -> None:
        server, _ = self.build(combat_state(enemy_ids=("Cultist",)))

        payload = self.tool_fn(server, "get_combat_context")()

        self.assertEqual(payload["knowledge"]["key"], "cultist_x1")
        self.assert_relative_path(payload["knowledge"], "combat/global/solo/cultist_x1.md")

    def test_get_combat_context_requires_enemies(self) -> None:
        server, _ = self.build({"screen": "COMBAT", "combat": {}})

        with self.assertRaises(ValueError):
            self.tool_fn(server, "get_combat_context")()

    def test_create_combat_handoff_packet_shape(self) -> None:
        server, _ = self.build(combat_state())

        payload = self.tool_fn(server, "create_combat_handoff")(
            planner_message="focus the louse",
            combat_objective="kill adds first",
        )

        self.assertTrue(COMBAT_HANDOFF_KEYS.issubset(payload.keys()))
        self.assertEqual(payload["handoff_type"], "combat")
        self.assertTrue(payload["reset_context"])
        self.assertEqual(payload["combat_key"], "jaw_worm_x2+louse_x1")
        self.assertEqual(payload["enemy_ids"], ["Jaw Worm", "JAW_WORM", "Louse"])
        self.assertEqual(payload["context"]["planner_note"], "focus the louse")
        self.assertEqual(payload["combat_objective"], "kill adds first")

        instructions = payload["instructions"]
        self.assertEqual(len(instructions), 5)
        self.assertTrue(any("Handle combat only" in line for line in instructions))

    def test_complete_combat_handoff_does_not_touch_sts2(self) -> None:
        server, client = self.build(combat_state())

        payload = self.tool_fn(server, "complete_combat_handoff")(
            combat_key="jaw_worm_x2+louse_x1",
            summary="Enemy died before acting",
            pattern_note="opens with a block move",
        )

        self.assertEqual(client.get_state_calls, 0)
        self.assertEqual(payload["handoff_type"], "combat_result")
        self.assertEqual(payload["combat_key"], "jaw_worm_x2+louse_x1")
        self.assertTrue({"knowledge_entry", "planner_summary", "knowledge_updates"}.issubset(payload.keys()))
        self.assertEqual(len(payload["knowledge_updates"]), 2)

        entry = payload["knowledge_entry"]
        self.assert_relative_path(entry, "combat/global/groups/jaw_worm_x2+louse_x1.md")
        self.assertTrue(entry["exists"])
        self.assert_under_temp(entry["path"])
        self.assertIn("combat_summary | Enemy died before acting", entry["content"])
        self.assertIn("opens with a block move", entry["content"])


class EventToolTests(LayeredToolTestBase):
    def test_append_event_knowledge_writes_under_temp_dir(self) -> None:
        server, client = self.build(event_state())

        payload = self.tool_fn(server, "append_event_knowledge")(
            note="took the relic",
            section="option_outcomes",
            option_index=2,
        )

        self.assertEqual(client.get_state_calls, 1)
        self.assertEqual(payload["category"], "event")
        self.assertEqual(payload["key"], "big_fish")
        self.assert_relative_path(payload, "events/global/big_fish.md")
        self.assert_under_temp(payload["path"])
        self.assertIn("## Option Outcomes", payload["content"])
        self.assertIn("option_index=2 | took the relic", payload["content"])

    def test_complete_event_handoff_does_not_touch_sts2(self) -> None:
        server, client = self.build(event_state())

        payload = self.tool_fn(server, "complete_event_handoff")(
            event_id="Big Fish",
            summary="Took the gold",
            option_index=1,
            planning_note="gold was the safer pick",
        )

        self.assertEqual(client.get_state_calls, 0)
        self.assertEqual(payload["handoff_type"], "event_result")
        self.assertEqual(payload["event_id"], "big_fish")
        self.assertEqual(len(payload["knowledge_updates"]), 2)

        entry = payload["knowledge_entry"]
        self.assert_relative_path(entry, "events/global/big_fish.md")
        self.assertTrue(entry["exists"])
        self.assert_under_temp(entry["path"])
        self.assertIn("event_summary | Took the gold", entry["content"])
        self.assertIn("option_index=1", entry["content"])


class CombatKnowledgeToolTests(LayeredToolTestBase):
    def test_append_combat_knowledge_writes_under_temp_dir(self) -> None:
        server, client = self.build(combat_state())

        payload = self.tool_fn(server, "append_combat_knowledge")(
            note="watch the debuff",
            section="tactical_notes",
        )

        self.assertEqual(client.get_state_calls, 1)
        self.assertEqual(payload["category"], "combat")
        self.assert_relative_path(payload, "combat/global/groups/jaw_worm_x2+louse_x1.md")
        self.assert_under_temp(payload["path"])
        self.assertIn("## Tactical Notes", payload["content"])
        self.assertIn("watch the debuff", payload["content"])


if __name__ == "__main__":
    unittest.main()
