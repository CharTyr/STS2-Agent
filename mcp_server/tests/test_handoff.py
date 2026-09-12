from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from sts2_mcp.handoff import Sts2HandoffService
from sts2_mcp.knowledge import Sts2KnowledgeBase


class HandoffTestCase(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name) / "agent_knowledge"
        self.knowledge = Sts2KnowledgeBase(root_dir=self.root)
        self.handoff = Sts2HandoffService(self.knowledge)

    def combat_state(self, enemies: list[dict] | None = None) -> dict:
        if enemies is None:
            enemies = [{"enemy_id": "cultist"}, {"enemy_id": "greater_gremlin"}]
        return {
            "run_id": "run_handoff",
            "screen": "COMBAT",
            "run": {"floor": 9, "character_id": "IRONCLAD"},
            "combat": {"enemies": enemies},
            "available_actions": ["play_card", "end_turn"],
        }

    def assertUnderTempRoot(self, path_text: str) -> None:
        self.assertTrue(
            Path(path_text).is_relative_to(self.root.resolve()),
            f"{path_text} escaped the temporary knowledge root",
        )


class PlannerHandoffTests(HandoffTestCase):
    def test_planner_packet_structure(self) -> None:
        state = {"run_id": "run_handoff", "screen": "MAP", "run": {"floor": 3}}

        packet = self.handoff.create_planner_handoff(
            state,
            planning_focus="Look for elite fights before the boss.",
            previous_combat_summary="Took 12 damage to cultists.",
        )

        self.assertEqual(packet["handoff_type"], "planner")
        self.assertIs(packet["reset_context"], True)
        self.assertEqual(len(packet["instructions"]), 5)
        self.assertEqual(packet["planning_focus"], "Look for elite fights before the boss.")
        self.assertEqual(packet["previous_combat_summary"], "Took 12 damage to cultists.")
        self.assertEqual(packet["context"]["screen"], "MAP")
        self.assertIsNone(packet["context"]["event_knowledge"])

    def test_planner_packet_links_event_knowledge(self) -> None:
        state = {
            "run_id": "run_handoff",
            "screen": "EVENT",
            "run": {"floor": 5},
            "event": {"event_id": "Cleric", "title": "The Cleric"},
        }

        packet = self.handoff.create_planner_handoff(state)

        entry = packet["context"]["event_knowledge"]
        self.assertIsNotNone(entry)
        self.assertEqual(entry["key"], "cleric")
        self.assertEqual(entry["relative_path"], str(Path("events") / "global" / "cleric.md"))
        self.assertUnderTempRoot(entry["path"])


class CombatHandoffTests(HandoffTestCase):
    def test_combat_packet_requires_combat_enemies(self) -> None:
        with self.assertRaises(ValueError):
            self.handoff.create_combat_handoff({"run_id": "run_handoff", "screen": "MAP"})

    def test_combat_packet_structure(self) -> None:
        packet = self.handoff.create_combat_handoff(
            self.combat_state(),
            planner_message="Gremlins first, then the cultist.",
            combat_objective="Finish the fight above 30 HP.",
        )

        self.assertEqual(packet["handoff_type"], "combat")
        self.assertIs(packet["reset_context"], True)
        self.assertEqual(len(packet["instructions"]), 5)
        self.assertEqual(packet["planner_message"], "Gremlins first, then the cultist.")
        self.assertEqual(packet["combat_objective"], "Finish the fight above 30 HP.")
        self.assertEqual(packet["combat_key"], "cultist_x1+greater_gremlin_x1")
        # The packet mirrors the combat payload's raw ids; the deduplicated
        # canonical identifier lives in combat_key / context.knowledge.key.
        self.assertEqual(packet["enemy_ids"], ["cultist", "greater_gremlin"])
        self.assertEqual(packet["context"]["knowledge"]["key"], "cultist_x1+greater_gremlin_x1")
        self.assertUnderTempRoot(packet["context"]["knowledge"]["path"])

    def test_combat_packet_deduplicates_mixed_case_enemy_ids_in_the_key(self) -> None:
        packet = self.handoff.create_combat_handoff(
            self.combat_state([{"enemy_id": "Cultist"}, {"enemy_id": "cultist"}])
        )

        self.assertEqual(packet["combat_key"], "cultist_x2")
        self.assertEqual(
            packet["context"]["knowledge"]["relative_path"],
            str(Path("combat") / "global" / "groups" / "cultist_x2.md"),
        )


class CompleteCombatHandoffTests(HandoffTestCase):
    def test_blank_summary_raises(self) -> None:
        for blank in ("", "   "):
            with self.subTest(blank=blank):
                with self.assertRaises(ValueError):
                    self.handoff.complete_combat_handoff("cultist_x1", blank)

    def test_knowledge_updates_grow_with_optional_notes(self) -> None:
        baseline = self.handoff.complete_combat_handoff("cultist_x1", "Won on turn 4.")
        with_pattern = self.handoff.complete_combat_handoff(
            "cultist_x1", "Won on turn 4.", pattern_note="Opens with Ritual."
        )
        with_trait = self.handoff.complete_combat_handoff(
            "cultist_x1",
            "Won on turn 4.",
            pattern_note="Opens with Ritual.",
            trait_note="Gains strength every turn.",
        )
        with_all = self.handoff.complete_combat_handoff(
            "cultist_x1",
            "Won on turn 4.",
            pattern_note="Opens with Ritual.",
            trait_note="Gains strength every turn.",
            tactical_note="Kill it before turn 5.",
        )

        self.assertEqual(
            [len(packet["knowledge_updates"]) for packet in (baseline, with_pattern, with_trait, with_all)],
            [1, 2, 3, 4],
        )

    def test_combat_result_packet_structure(self) -> None:
        packet = self.handoff.complete_combat_handoff(
            "cultist_x1",
            "Won on turn 4.",
            planner_message="Reached floor 9.",
            pattern_note="Opens with Ritual.",
        )

        self.assertEqual(packet["handoff_type"], "combat_result")
        self.assertEqual(packet["combat_key"], "cultist_x1")
        self.assertEqual(packet["planner_summary"]["summary"], "Won on turn 4.")
        self.assertEqual(packet["planner_summary"]["planner_message"], "Reached floor 9.")
        self.assertEqual(
            packet["planner_summary"]["knowledge_path"],
            str(Path("combat") / "global" / "solo" / "cultist_x1.md"),
        )

        entry = packet["knowledge_entry"]
        self.assertUnderTempRoot(entry["path"])
        self.assertIn("combat_summary | Won on turn 4.", entry["content"])
        self.assertIn("Opens with Ritual.", entry["content"])
        self.assertIn("## Known Patterns", entry["content"])
        for update in packet["knowledge_updates"]:
            self.assertEqual(update["category"], "combat")
            self.assertUnderTempRoot(update["path"])


class CompleteEventHandlerTests(HandoffTestCase):
    def test_blank_summary_raises(self) -> None:
        for blank in ("", "   "):
            with self.subTest(blank=blank):
                with self.assertRaises(ValueError):
                    self.handoff.complete_event_handoff("cleric", blank)

    def test_blank_event_id_raises(self) -> None:
        with self.assertRaises(ValueError):
            self.handoff.complete_event_handoff("   ", "Paid gold.")

    def test_option_index_is_written_into_the_knowledge_file(self) -> None:
        packet = self.handoff.complete_event_handoff(
            "cleric",
            "Paid gold for a heal.",
            option_index=1,
            planning_note="Only worth it above 20 HP.",
            outcome_note="Healed 25 HP for 35 gold.",
        )

        self.assertEqual(packet["handoff_type"], "event_result")
        self.assertEqual(packet["event_id"], "cleric")
        self.assertEqual(packet["planner_summary"]["option_index"], 1)
        self.assertEqual(len(packet["knowledge_updates"]), 3)

        content = packet["knowledge_entry"]["content"]
        self.assertIn("option_index=1", content)
        self.assertIn("event_summary | Paid gold for a heal.", content)
        self.assertIn("Only worth it above 20 HP.", content)
        self.assertIn("Healed 25 HP for 35 gold.", content)
        self.assertIn("## Option Outcomes", content)
        self.assertIn("## Planning Notes", content)
        for update in packet["knowledge_updates"]:
            self.assertEqual(update["category"], "event")
            self.assertUnderTempRoot(update["path"])

    def test_event_result_without_option_index_omits_the_prefix(self) -> None:
        packet = self.handoff.complete_event_handoff("cleric", "Paid gold for a heal.")

        self.assertEqual(len(packet["knowledge_updates"]), 1)
        self.assertNotIn("option_index=", packet["knowledge_entry"]["content"])


if __name__ == "__main__":
    unittest.main()
