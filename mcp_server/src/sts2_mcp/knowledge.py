from __future__ import annotations

import os
import re
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _default_knowledge_root() -> Path:
    configured = os.getenv("STS2_AGENT_KNOWLEDGE_DIR", "").strip()
    if configured:
        return Path(configured).expanduser().resolve()

    return _repo_root() / "agent_knowledge"


def _utc_timestamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _normalize_segment(value: str | None, fallback: str) -> str:
    raw = (value or "").strip().lower()
    if not raw:
        return fallback

    cleaned = re.sub(r"[^a-z0-9._+-]+", "_", raw)
    cleaned = cleaned.strip("._+-")
    return cleaned or fallback


def _run_floor(state: dict[str, Any]) -> int | None:
    run_payload = state.get("run")
    if not isinstance(run_payload, dict):
        return None

    floor = run_payload.get("floor")
    return floor if isinstance(floor, int) else None


def _run_summary(state: dict[str, Any]) -> dict[str, Any] | None:
    run_payload = state.get("run")
    if not isinstance(run_payload, dict):
        return None

    deck = run_payload.get("deck") if isinstance(run_payload.get("deck"), list) else []
    relics = run_payload.get("relics") if isinstance(run_payload.get("relics"), list) else []
    potions = run_payload.get("potions") if isinstance(run_payload.get("potions"), list) else []

    return {
        "character_id": run_payload.get("character_id"),
        "character_name": run_payload.get("character_name"),
        "floor": _run_floor(state),
        "current_hp": run_payload.get("current_hp"),
        "max_hp": run_payload.get("max_hp"),
        "gold": run_payload.get("gold"),
        "max_energy": run_payload.get("max_energy"),
        "base_orb_slots": run_payload.get("base_orb_slots"),
        "deck_size": len(deck),
        "relics": relics,
        "potions": potions,
    }


def _combat_payload(state: dict[str, Any]) -> dict[str, Any]:
    combat = state.get("combat")
    return combat if isinstance(combat, dict) else {}


def _event_payload(state: dict[str, Any]) -> dict[str, Any]:
    event = state.get("event")
    return event if isinstance(event, dict) else {}


def _map_payload(state: dict[str, Any]) -> dict[str, Any]:
    map_payload = state.get("map")
    return map_payload if isinstance(map_payload, dict) else {}


def _combat_key(enemies: list[dict[str, Any]]) -> str:
    ids = [
        _normalize_segment(enemy.get("enemy_id"), "unknown_enemy")
        for enemy in enemies
        if isinstance(enemy, dict)
    ]
    counts = Counter(ids)
    if not counts:
        return "unknown_enemy"

    parts = [f"{enemy_id}_x{count}" for enemy_id, count in sorted(counts.items())]
    return "+".join(parts)


def _combat_group_kind(enemies: list[dict[str, Any]]) -> str:
    if len(enemies) == 1:
        return "solo"

    return "groups"


def _combat_group_kind_from_key(combat_key: str) -> str:
    total = 0
    for token in combat_key.split("+"):
        _, count = _parse_combat_key_part(token)
        try:
            total += count
        except ValueError:
            total += 1

    return "solo" if total <= 1 else "groups"


def _enemy_ids_from_combat_key(combat_key: str) -> list[str]:
    enemy_ids: list[str] = []
    for token in combat_key.split("+"):
        enemy_id, _ = _parse_combat_key_part(token)
        normalized = _normalize_segment(enemy_id, "unknown_enemy")
        if normalized not in enemy_ids:
            enemy_ids.append(normalized)

    return enemy_ids or ["unknown_enemy"]


def _parse_combat_key_part(token: str) -> tuple[str, int]:
    normalized = token.strip()
    match = re.match(r"^(?P<enemy_id>.+?)(?:(?:\*|_x)(?P<count>\d+))?$", normalized)
    if not match:
        return normalized or "unknown_enemy", 1

    enemy_id = match.group("enemy_id") or "unknown_enemy"
    count_text = match.group("count")
    return enemy_id, int(count_text) if count_text else 1


def _section_heading(category: str, section: str) -> str:
    section_maps = {
        "combat": {
            "known_patterns": "Known Patterns",
            "traits": "Traits",
            "tactical_notes": "Tactical Notes",
            "observations": "Observations",
        },
        "event": {
            "option_outcomes": "Option Outcomes",
            "planning_notes": "Planning Notes",
            "observations": "Observations",
        },
    }

    category_map = section_maps.get(category, {})
    heading = category_map.get(section)
    if heading is None:
        allowed = ", ".join(sorted(category_map))
        raise ValueError(f"Unsupported section '{section}' for {category}. Allowed: {allowed}")

    return heading


def _append_section_line(content: str, heading: str, line: str) -> str:
    marker = f"## {heading}"
    if marker not in content:
        content = content.rstrip() + f"\n\n## {heading}\n"

    marker_index = content.index(marker)
    section_start = content.find("\n", marker_index)
    if section_start == -1:
        section_start = len(content)
    else:
        section_start += 1

    next_section_match = re.search(r"^##\s+", content[section_start:], re.MULTILINE)
    section_end = section_start + next_section_match.start() if next_section_match else len(content)

    existing = content[section_start:section_end].rstrip("\n")
    updated_section = f"{existing}\n- {line}\n" if existing else f"- {line}\n"
    return content[:section_start] + updated_section + content[section_end:].lstrip("\n")


def _combat_template(combat_key: str, enemy_ids: list[str]) -> str:
    enemy_list = ", ".join(f'"{enemy_id}"' for enemy_id in enemy_ids)
    created_at = _utc_timestamp()
    return (
        "---\n"
        "type: combat\n"
        f"combat_key: {combat_key}\n"
        f"enemy_ids: [{enemy_list}]\n"
        f"created_at: {created_at}\n"
        "---\n\n"
        "## Known Patterns\n\n"
        "## Traits\n\n"
        "## Tactical Notes\n\n"
        "## Observations\n"
    )


def _event_template(event_id: str, title: str | None) -> str:
    created_at = _utc_timestamp()
    title_line = title or ""
    return (
        "---\n"
        "type: event\n"
        f"event_id: {event_id}\n"
        f"title: {title_line}\n"
        f"created_at: {created_at}\n"
        "---\n\n"
        "## Option Outcomes\n\n"
        "## Planning Notes\n\n"
        "## Observations\n"
    )


def _coord_key(node: dict[str, Any]) -> tuple[int, int] | None:
    row = node.get("row")
    col = node.get("col")
    if not isinstance(row, int) or not isinstance(col, int):
        return None

    return row, col


def _children_keys(node: dict[str, Any]) -> list[tuple[int, int]]:
    children = node.get("children")
    if not isinstance(children, list):
        return []

    result: list[tuple[int, int]] = []
    for child in children:
        if not isinstance(child, dict):
            continue
        coord = _coord_key(child)
        if coord is not None:
            result.append(coord)
    return result


def _path_summary(path: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "length": len(path),
        "coords": [f"{node.get('row')},{node.get('col')}" for node in path],
        "node_types": [node.get("node_type") for node in path],
        "terminal_node_type": path[-1].get("node_type") if path else None,
    }


def _enumerate_paths(
    nodes_by_coord: dict[tuple[int, int], dict[str, Any]],
    start: tuple[int, int],
    seen: set[tuple[int, int]] | None = None,
) -> list[list[dict[str, Any]]]:
    seen = set() if seen is None else set(seen)
    if start in seen:
        return []

    node = nodes_by_coord.get(start)
    if node is None:
        return []

    children = [child for child in _children_keys(node) if child in nodes_by_coord]
    if not children:
        return [[node]]

    paths: list[list[dict[str, Any]]] = []
    next_seen = seen | {start}
    for child in children:
        for child_path in _enumerate_paths(nodes_by_coord, child, next_seen):
            paths.append([node, *child_path])

    return paths or [[node]]


@dataclass(frozen=True, slots=True)
class KnowledgeEntry:
    category: str
    key: str
    path: Path
    root_dir: Path
    exists: bool
    content: str

    def to_payload(self) -> dict[str, Any]:
        return {
            "category": self.category,
            "key": self.key,
            "path": str(self.path),
            "relative_path": str(self.path.relative_to(self.root_dir)),
            "exists": self.exists,
            "content": self.content,
        }


class Sts2KnowledgeBase:
    def __init__(self, root_dir: str | Path | None = None) -> None:
        self._root_dir = Path(root_dir) if root_dir is not None else _default_knowledge_root()
        self._root_dir = self._root_dir.expanduser().resolve()

    @property
    def root_dir(self) -> Path:
        return self._root_dir

    def build_planner_context(self, state: dict[str, Any], planner_note: str | None = None) -> dict[str, Any]:
        event_entry = None
        event = _event_payload(state)
        if event.get("event_id"):
            event_entry = self.resolve_event_entry(state, create_if_missing=True).to_payload()

        return {
            "screen": state.get("screen"),
            "session": state.get("session"),
            "available_actions": state.get("available_actions", []),
            "planner_note": planner_note,
            "run_summary": _run_summary(state),
            "map": _map_payload(state),
            "route_options": self._build_route_options(state),
            "reward": state.get("reward"),
            "event": event or None,
            "rest": state.get("rest"),
            "shop": state.get("shop"),
            "event_knowledge": event_entry,
            "reference_files": {
                "playbook": str(_repo_root() / "docs" / "game-knowledge" / "playbook.md"),
                "events": str(_repo_root() / "docs" / "game-knowledge" / "events.md"),
                "cards": str(_repo_root() / "docs" / "game-knowledge" / "cards.md"),
                "characters": str(_repo_root() / "docs" / "game-knowledge" / "characters.md"),
            },
        }

    def build_combat_context(
        self,
        state: dict[str, Any],
        planner_note: str | None = None,
        include_knowledge: bool = True,
    ) -> dict[str, Any]:
        combat = _combat_payload(state)
        enemies = combat.get("enemies")
        if not isinstance(enemies, list) or not enemies:
            raise ValueError("Combat context is only available while a combat enemy list is present.")

        entry = self.resolve_combat_entry(state, create_if_missing=True)
        return {
            "screen": state.get("screen"),
            "turn": state.get("turn"),
            "session": state.get("session"),
            "available_actions": state.get("available_actions", []),
            "planner_note": planner_note,
            "run_summary": _run_summary(state),
            "combat": combat,
            "knowledge": {
                **entry.to_payload(),
                "content": entry.content if include_knowledge else "",
            },
            "reference_files": {
                "playbook": str(_repo_root() / "docs" / "game-knowledge" / "playbook.md"),
                "monsters": str(_repo_root() / "docs" / "game-knowledge" / "monsters.md"),
                "monster_behaviors": str(_repo_root() / "docs" / "game-knowledge" / "monster-behaviors.md"),
                "potions": str(_repo_root() / "docs" / "game-knowledge" / "potions.md"),
            },
        }

    def append_combat_note(self, state: dict[str, Any], note: str, section: str = "observations") -> dict[str, Any]:
        note = note.strip()
        if not note:
            raise ValueError("note must not be empty.")

        entry = self.resolve_combat_entry(state, create_if_missing=True)
        heading = _section_heading("combat", section)
        line = self._format_note_line(state, note)
        updated = _append_section_line(entry.content, heading, line)
        entry.path.write_text(updated, encoding="utf-8")
        return self.resolve_combat_entry(state, create_if_missing=False).to_payload()

    def append_combat_note_by_key(
        self,
        combat_key: str,
        note: str,
        section: str = "observations",
    ) -> dict[str, Any]:
        note = note.strip()
        if not note:
            raise ValueError("note must not be empty.")

        entry = self.resolve_combat_entry_by_key(combat_key, create_if_missing=True)
        heading = _section_heading("combat", section)
        line = f"{_utc_timestamp()} | {note}"
        updated = _append_section_line(entry.content, heading, line)
        entry.path.write_text(updated, encoding="utf-8")
        return self.resolve_combat_entry_by_key(combat_key, create_if_missing=False).to_payload()

    def append_event_note(
        self,
        state: dict[str, Any],
        note: str,
        section: str = "observations",
        option_index: int | None = None,
    ) -> dict[str, Any]:
        note = note.strip()
        if not note:
            raise ValueError("note must not be empty.")

        entry = self.resolve_event_entry(state, create_if_missing=True)
        heading = _section_heading("event", section)
        prefix = f"option_index={option_index} | " if option_index is not None else ""
        line = self._format_note_line(state, prefix + note)
        updated = _append_section_line(entry.content, heading, line)
        entry.path.write_text(updated, encoding="utf-8")
        return self.resolve_event_entry(state, create_if_missing=False).to_payload()

    def append_event_note_by_id(
        self,
        event_id: str,
        note: str,
        section: str = "observations",
        prefix: str | None = None,
    ) -> dict[str, Any]:
        note = note.strip()
        if not note:
            raise ValueError("note must not be empty.")

        entry = self.resolve_event_entry_by_id(event_id, create_if_missing=True)
        heading = _section_heading("event", section)
        line = f"{_utc_timestamp()} | {(prefix + ' | ') if prefix else ''}{note}"
        updated = _append_section_line(entry.content, heading, line)
        entry.path.write_text(updated, encoding="utf-8")
        return self.resolve_event_entry_by_id(event_id, create_if_missing=False).to_payload()

    def resolve_combat_entry(self, state: dict[str, Any], create_if_missing: bool) -> KnowledgeEntry:
        combat = _combat_payload(state)
        enemies = combat.get("enemies")
        if not isinstance(enemies, list) or not enemies:
            raise ValueError("Combat knowledge is only available while combat enemies are present.")

        combat_key = _combat_key(enemies)
        enemy_ids = sorted({
            _normalize_segment(enemy.get("enemy_id"), "unknown_enemy")
            for enemy in enemies
            if isinstance(enemy, dict)
        })
        return self.resolve_combat_entry_by_key(
            combat_key,
            create_if_missing=create_if_missing,
            enemy_ids=enemy_ids,
        )

    def resolve_combat_entry_by_key(
        self,
        combat_key: str,
        create_if_missing: bool,
        enemy_ids: list[str] | None = None,
    ) -> KnowledgeEntry:
        normalized_key = _normalize_segment(combat_key, "unknown_enemy")
        path = self._root_dir / "combat" / "global" / _combat_group_kind_from_key(normalized_key) / f"{normalized_key}.md"
        resolved_enemy_ids = enemy_ids or _enemy_ids_from_combat_key(normalized_key)
        if create_if_missing and not path.exists():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(_combat_template(normalized_key, sorted(resolved_enemy_ids)), encoding="utf-8")

        content = path.read_text(encoding="utf-8") if path.exists() else ""
        return KnowledgeEntry("combat", normalized_key, path, self._root_dir, path.exists(), content)

    def resolve_event_entry(self, state: dict[str, Any], create_if_missing: bool) -> KnowledgeEntry:
        event = _event_payload(state)
        event_id = _normalize_segment(event.get("event_id"), "unknown_event")
        if event_id == "unknown_event":
            raise ValueError("Event knowledge is only available while an event_id is present.")

        return self.resolve_event_entry_by_id(event_id, create_if_missing=create_if_missing, title=event.get("title"))

    def resolve_event_entry_by_id(
        self,
        event_id: str,
        create_if_missing: bool,
        title: str | None = None,
    ) -> KnowledgeEntry:
        normalized_event_id = _normalize_segment(event_id, "unknown_event")
        if normalized_event_id == "unknown_event":
            raise ValueError("event_id must not be empty.")

        path = self._root_dir / "events" / "global" / f"{normalized_event_id}.md"
        if create_if_missing and not path.exists():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(_event_template(normalized_event_id, title), encoding="utf-8")

        content = path.read_text(encoding="utf-8") if path.exists() else ""
        return KnowledgeEntry("event", normalized_event_id, path, self._root_dir, path.exists(), content)

    def _build_route_options(self, state: dict[str, Any]) -> list[dict[str, Any]]:
        map_payload = _map_payload(state)
        nodes = map_payload.get("nodes")
        available_nodes = map_payload.get("available_nodes")
        if not isinstance(nodes, list) or not isinstance(available_nodes, list):
            return []

        nodes_by_coord = {
            coord: node
            for node in nodes
            if isinstance(node, dict)
            for coord in [_coord_key(node)]
            if coord is not None
        }

        route_options: list[dict[str, Any]] = []
        for option in available_nodes:
            if not isinstance(option, dict):
                continue

            start = _coord_key(option)
            if start is None:
                continue

            paths = _enumerate_paths(nodes_by_coord, start)
            route_options.append(
                {
                    "start_node": option,
                    "path_count": len(paths),
                    "paths": [_path_summary(path) for path in paths],
                }
            )

        return route_options

    @staticmethod
    def _format_note_line(state: dict[str, Any], note: str) -> str:
        floor = _run_floor(state)
        run_id = state.get("run_id", "run_unknown")
        screen = state.get("screen", "UNKNOWN")
        floor_part = f"floor={floor}" if floor is not None else "floor=unknown"
        return f"{_utc_timestamp()} | run_id={run_id} | {floor_part} | screen={screen} | {note}"
