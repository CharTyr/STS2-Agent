from __future__ import annotations

from typing import Any

from .knowledge import Sts2KnowledgeBase


def _combat_key_from_context(context: dict[str, Any]) -> str:
    knowledge = context.get("knowledge")
    if not isinstance(knowledge, dict):
        raise ValueError("Combat context is missing knowledge metadata.")

    combat_key = knowledge.get("key")
    if not isinstance(combat_key, str) or not combat_key.strip():
        raise ValueError("Combat context is missing a combat knowledge key.")

    return combat_key


def _planner_rules() -> list[str]:
    return [
        "Handle non-combat flow only: route planning, rewards, shops, rest sites, and event choices.",
        "Use `state.available_actions` as the hard action boundary. Do not invent actions.",
        "Treat combat as a handoff boundary. When combat starts, delegate using the combat packet.",
        "Prefer route and reward decisions that align with current HP, relics, potions, and deck direction.",
        "Use event knowledge files as prior observations, but still judge the live state before deciding.",
    ]


def _combat_rules() -> list[str]:
    return [
        "Handle combat only. Ignore route, rewards, shops, and non-combat planning.",
        "Re-read the latest combat payload every turn and recalculate indexes from fresh state.",
        "Use only combat-safe actions currently present in `available_actions`.",
        "Consult the linked combat knowledge file for opening patterns, traits, and tactical notes.",
        "When combat ends, emit a concise planner summary and append durable notes to the combat knowledge file.",
    ]


class Sts2HandoffService:
    def __init__(self, knowledge: Sts2KnowledgeBase | None = None) -> None:
        self._knowledge = knowledge or Sts2KnowledgeBase()

    def create_planner_handoff(
        self,
        state: dict[str, Any],
        planning_focus: str | None = None,
        previous_combat_summary: str | None = None,
    ) -> dict[str, Any]:
        context = self._knowledge.build_planner_context(state, planner_note=planning_focus)
        return {
            "handoff_type": "planner",
            "reset_context": True,
            "system_prompt": (
                "You are the Slay the Spire 2 planner agent. "
                "Own non-combat decisions and hand off combat cleanly."
            ),
            "instructions": _planner_rules(),
            "planning_focus": planning_focus,
            "previous_combat_summary": previous_combat_summary,
            "context": context,
        }

    def create_combat_handoff(
        self,
        state: dict[str, Any],
        planner_message: str | None = None,
        combat_objective: str | None = None,
    ) -> dict[str, Any]:
        context = self._knowledge.build_combat_context(state, planner_note=planner_message)
        combat = context.get("combat") if isinstance(context.get("combat"), dict) else {}
        enemies = combat.get("enemies") if isinstance(combat.get("enemies"), list) else []
        enemy_ids = [
            enemy.get("enemy_id")
            for enemy in enemies
            if isinstance(enemy, dict) and isinstance(enemy.get("enemy_id"), str)
        ]

        return {
            "handoff_type": "combat",
            "reset_context": True,
            "system_prompt": (
                "You are the Slay the Spire 2 combat sub-agent. "
                "Own combat tactics only and return a concise summary when the fight ends."
            ),
            "instructions": _combat_rules(),
            "planner_message": planner_message,
            "combat_objective": combat_objective,
            "combat_key": _combat_key_from_context(context),
            "enemy_ids": enemy_ids,
            "context": context,
        }

    def complete_combat_handoff(
        self,
        combat_key: str,
        summary: str,
        planner_message: str | None = None,
        pattern_note: str | None = None,
        trait_note: str | None = None,
        tactical_note: str | None = None,
    ) -> dict[str, Any]:
        summary = summary.strip()
        if not summary:
            raise ValueError("summary must not be empty.")

        updates = [
            self._knowledge.append_combat_note_by_key(
                combat_key,
                f"combat_summary | {summary}",
                section="observations",
            )
        ]

        if pattern_note and pattern_note.strip():
            updates.append(
                self._knowledge.append_combat_note_by_key(
                    combat_key,
                    pattern_note.strip(),
                    section="known_patterns",
                )
            )

        if trait_note and trait_note.strip():
            updates.append(
                self._knowledge.append_combat_note_by_key(
                    combat_key,
                    trait_note.strip(),
                    section="traits",
                )
            )

        if tactical_note and tactical_note.strip():
            updates.append(
                self._knowledge.append_combat_note_by_key(
                    combat_key,
                    tactical_note.strip(),
                    section="tactical_notes",
                )
            )

        entry = self._knowledge.resolve_combat_entry_by_key(combat_key, create_if_missing=False)
        return {
            "handoff_type": "combat_result",
            "combat_key": entry.key,
            "knowledge_entry": entry.to_payload(),
            "planner_summary": {
                "summary": summary,
                "planner_message": planner_message,
                "knowledge_path": entry.to_payload()["relative_path"],
            },
            "knowledge_updates": updates,
        }

    def complete_event_handoff(
        self,
        event_id: str,
        summary: str,
        option_index: int | None = None,
        planning_note: str | None = None,
        outcome_note: str | None = None,
    ) -> dict[str, Any]:
        summary = summary.strip()
        if not summary:
            raise ValueError("summary must not be empty.")

        prefix = f"option_index={option_index}" if option_index is not None else None
        updates = [
            self._knowledge.append_event_note_by_id(
                event_id,
                f"event_summary | {summary}",
                section="observations",
                prefix=prefix,
            )
        ]

        if planning_note and planning_note.strip():
            updates.append(
                self._knowledge.append_event_note_by_id(
                    event_id,
                    planning_note.strip(),
                    section="planning_notes",
                    prefix=prefix,
                )
            )

        if outcome_note and outcome_note.strip():
            updates.append(
                self._knowledge.append_event_note_by_id(
                    event_id,
                    outcome_note.strip(),
                    section="option_outcomes",
                    prefix=prefix,
                )
            )

        entry = self._knowledge.resolve_event_entry_by_id(event_id, create_if_missing=False)
        return {
            "handoff_type": "event_result",
            "event_id": entry.key,
            "knowledge_entry": entry.to_payload(),
            "planner_summary": {
                "summary": summary,
                "option_index": option_index,
                "knowledge_path": entry.to_payload()["relative_path"],
            },
            "knowledge_updates": updates,
        }


__all__ = [
    "Sts2HandoffService",
]
