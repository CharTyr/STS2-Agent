from __future__ import annotations

import contextlib
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from sts2_mcp import knowledge
from sts2_mcp.knowledge import (
    Sts2KnowledgeBase,
    _append_section_line,
    _combat_key,
    _combat_template,
    _enumerate_paths,
    _parse_combat_key_part,
    _section_heading,
)

KNOWLEDGE_ENV_KEYS = ("STS2_AGENT_REPO_ROOT", "STS2_AGENT_KNOWLEDGE_DIR")


def cleared_knowledge_env():
    """Disable both knowledge env overrides without touching the rest of os.environ."""
    return patch.dict(os.environ, {key: "" for key in KNOWLEDGE_ENV_KEYS}, clear=False)


def make_fake_checkout(root: Path) -> Path:
    """Create a fake checkout layout and return a module path inside it."""
    package_dir = root / "mcp_server" / "src" / "sts2_mcp"
    package_dir.mkdir(parents=True, exist_ok=True)
    (root / "mcp_server" / "pyproject.toml").write_text('[project]\nname = "fake"\n', encoding="utf-8")
    module_path = package_dir / "knowledge.py"
    module_path.write_text("", encoding="utf-8")
    return module_path


def make_installed_module(root: Path) -> Path:
    """Create a site-packages-like module path with no checkout marker above it."""
    package_dir = root / "venv" / "Lib" / "site-packages" / "sts2_mcp"
    package_dir.mkdir(parents=True, exist_ok=True)
    module_path = package_dir / "knowledge.py"
    module_path.write_text("", encoding="utf-8")
    return module_path


def combat_state(enemies: list[dict], floor: int = 7, run_id: str = "run_alpha") -> dict:
    return {
        "run_id": run_id,
        "screen": "COMBAT",
        "run": {"floor": floor},
        "combat": {"enemies": enemies},
    }


class RepoRootResolutionTests(unittest.TestCase):
    def test_env_repo_root_wins_over_checkout_marker(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            module_path = make_fake_checkout(tmp_path / "checkout")
            override = tmp_path / "override"
            with patch.dict(os.environ, {"STS2_AGENT_REPO_ROOT": str(override)}, clear=False):
                self.assertEqual(knowledge._repo_root(start=module_path), override.resolve())

    def test_repo_root_walks_up_to_checkout_marker(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            checkout = Path(tmp) / "checkout"
            module_path = make_fake_checkout(checkout)
            with cleared_knowledge_env():
                self.assertEqual(knowledge._repo_root(start=module_path), checkout.resolve())

    def test_repo_root_is_none_when_no_marker_exists(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            module_path = make_installed_module(Path(tmp))
            with cleared_knowledge_env():
                self.assertIsNone(knowledge._repo_root(start=module_path))

    def test_default_root_prefers_knowledge_dir_env(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / "pinned-knowledge"
            with patch.dict(
                os.environ,
                {"STS2_AGENT_KNOWLEDGE_DIR": str(target), "STS2_AGENT_REPO_ROOT": ""},
                clear=False,
            ):
                self.assertEqual(knowledge._default_knowledge_root(), target.resolve())

    def test_default_root_uses_checkout_agent_knowledge(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            checkout = Path(tmp) / "checkout"
            module_path = make_fake_checkout(checkout)
            with cleared_knowledge_env(), patch.object(knowledge, "__file__", str(module_path)):
                self.assertEqual(
                    knowledge._default_knowledge_root(),
                    (checkout / "agent_knowledge").resolve(),
                )

    def test_default_root_falls_back_to_cwd_outside_a_checkout(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            module_path = make_installed_module(tmp_path)
            install_dir = module_path.parent
            workdir = tmp_path / "workspace"
            workdir.mkdir()

            with cleared_knowledge_env(), patch.object(
                knowledge, "__file__", str(module_path)
            ), contextlib.chdir(workdir):
                install_root = module_path.resolve().parent
                with self.assertLogs("sts2_mcp.knowledge", level="WARNING") as captured:
                    resolved = knowledge._default_knowledge_root()

            self.assertEqual(resolved, (workdir / "agent_knowledge").resolve())
            self.assertFalse(
                resolved.is_relative_to(install_root),
                f"knowledge root {resolved} must not live inside the install directory",
            )
            self.assertFalse(resolved.is_relative_to(install_dir.resolve()))
            self.assertTrue(any("STS2_AGENT_KNOWLEDGE_DIR" in message for message in captured.output))


class ReferenceFileTests(unittest.TestCase):
    def _knowledge(self, root: Path) -> Sts2KnowledgeBase:
        return Sts2KnowledgeBase(root_dir=root)

    def test_planner_reference_files_empty_without_checkout(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            module_path = make_installed_module(tmp_path)
            knowledge_base = self._knowledge(tmp_path / "kb")
            with cleared_knowledge_env(), patch.object(knowledge, "__file__", str(module_path)):
                context = knowledge_base.build_planner_context({"run_id": "run_1", "screen": "MAP"})

            self.assertEqual(context["reference_files"], [])

    def test_combat_reference_files_empty_without_checkout(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            module_path = make_installed_module(tmp_path)
            knowledge_base = self._knowledge(tmp_path / "kb")
            with cleared_knowledge_env(), patch.object(knowledge, "__file__", str(module_path)):
                context = knowledge_base.build_combat_context(
                    combat_state([{"enemy_id": "cultist"}])
                )

            self.assertEqual(context["reference_files"], [])

    def test_reference_files_point_into_checkout_when_found(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            checkout = Path(tmp) / "checkout"
            module_path = make_fake_checkout(checkout)
            knowledge_base = self._knowledge(Path(tmp) / "kb")
            with cleared_knowledge_env(), patch.object(knowledge, "__file__", str(module_path)):
                planner = knowledge_base.build_planner_context({"run_id": "run_1", "screen": "MAP"})
                combat = knowledge_base.build_combat_context(combat_state([{"enemy_id": "cultist"}]))

            docs = checkout / "docs" / "game-knowledge"
            self.assertEqual(planner["reference_files"]["playbook"], str(docs / "playbook.md"))
            self.assertEqual(combat["reference_files"]["monsters"], str(docs / "monsters.md"))


class CombatKeyTests(unittest.TestCase):
    def test_combat_key_normalizes_and_counts_enemies(self) -> None:
        self.assertEqual(
            _combat_key([{"enemy_id": "Cultist"}, {"enemy_id": "cultist"}]),
            "cultist_x2",
        )
        self.assertEqual(
            _combat_key([{"enemy_id": "Slime Large"}, {"enemy_id": "Cultist"}]),
            "cultist_x1+slime_large_x1",
        )

    def test_combat_key_defaults_for_missing_enemy_ids(self) -> None:
        self.assertEqual(_combat_key([]), "unknown_enemy")
        self.assertEqual(_combat_key([{}]), "unknown_enemy_x1")

    def test_parse_combat_key_part_reads_optional_counts(self) -> None:
        self.assertEqual(_parse_combat_key_part("cultist_x3"), ("cultist", 3))
        self.assertEqual(_parse_combat_key_part("cultist*4"), ("cultist", 4))
        self.assertEqual(_parse_combat_key_part("cultist"), ("cultist", 1))
        self.assertEqual(_parse_combat_key_part("   "), ("unknown_enemy", 1))


class AppendSectionLineTests(unittest.TestCase):
    def test_identical_line_is_not_appended_twice(self) -> None:
        content = _combat_template("cultist_x1", ["cultist"])
        once = _append_section_line(content, "Observations", "2026-01-01T00:00:00Z | note")
        twice = _append_section_line(once, "Observations", "2026-01-01T00:00:00Z | note")

        self.assertEqual(twice, once)
        self.assertEqual(once.count("- 2026-01-01T00:00:00Z | note"), 1)

    def test_distinct_lines_accumulate_in_order(self) -> None:
        content = _combat_template("cultist_x1", ["cultist"])
        first = _append_section_line(content, "Observations", "first")
        second = _append_section_line(first, "Observations", "second")

        self.assertLess(second.index("- first"), second.index("- second"))

    def test_line_stays_inside_the_target_section(self) -> None:
        content = _combat_template("cultist_x1", ["cultist"])
        updated = _append_section_line(content, "Traits", "always opens with Ritual")

        traits_index = updated.index("## Traits")
        note_index = updated.index("- always opens with Ritual")
        tactical_index = updated.index("## Tactical Notes")
        self.assertLess(traits_index, note_index)
        self.assertLess(note_index, tactical_index)
        self.assertNotIn("- always opens with Ritual", updated[:traits_index])

    def test_missing_section_heading_is_created(self) -> None:
        updated = _append_section_line("# notes\n", "Observations", "first")

        self.assertIn("## Observations", updated)
        self.assertIn("- first", updated)

    def test_unsupported_section_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            _section_heading("combat", "not_a_section")


class KnowledgeEntryLifecycleTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name) / "agent_knowledge"
        self.knowledge = Sts2KnowledgeBase(root_dir=self.root)

    def test_solo_combat_entry_create_append_payload(self) -> None:
        state = combat_state([{"enemy_id": "Cultist"}])
        entry = self.knowledge.resolve_combat_entry(state, create_if_missing=True)

        self.assertTrue(entry.exists)
        self.assertEqual(entry.key, "cultist_x1")
        self.assertIn("combat_key: cultist_x1", entry.content)
        self.assertEqual(
            entry.to_payload()["relative_path"],
            str(Path("combat") / "global" / "solo" / "cultist_x1.md"),
        )

        payload = self.knowledge.append_combat_note(state, "Doubles strength on turn 3.")

        self.assertIn("- ", payload["content"])
        self.assertIn("run_id=run_alpha", payload["content"])
        self.assertIn("floor=7", payload["content"])
        self.assertIn("screen=COMBAT", payload["content"])
        self.assertIn("Doubles strength on turn 3.", payload["content"])
        self.assertEqual(Path(payload["path"]).read_text(encoding="utf-8"), payload["content"])
        self.assertTrue(Path(payload["path"]).is_relative_to(self.root.resolve()))

    def test_group_combat_entry_lands_in_groups_directory(self) -> None:
        entry = self.knowledge.resolve_combat_entry_by_key(
            "cultist_x2+slime_large_x1", create_if_missing=True
        )

        self.assertEqual(
            entry.to_payload()["relative_path"],
            str(Path("combat") / "global" / "groups" / "cultist_x2+slime_large_x1.md"),
        )

    def test_append_combat_note_rejects_blank_note(self) -> None:
        with self.assertRaises(ValueError):
            self.knowledge.append_combat_note(combat_state([{"enemy_id": "cultist"}]), "   ")

    def test_event_entry_lifecycle_with_option_index(self) -> None:
        state = {
            "run_id": "run_beta",
            "screen": "EVENT",
            "run": {"floor": 4},
            "event": {"event_id": "Cleric", "title": "The Cleric"},
        }
        payload = self.knowledge.append_event_note(
            state, "Paid gold for a heal.", section="option_outcomes", option_index=1
        )

        self.assertEqual(
            payload["relative_path"],
            str(Path("events") / "global" / "cleric.md"),
        )
        self.assertIn("title: The Cleric", payload["content"])
        self.assertIn("run_id=run_beta", payload["content"])
        self.assertIn("option_index=1", payload["content"])

    def test_blank_event_id_raises(self) -> None:
        for blank in ("", "   ", "!!!"):
            with self.subTest(blank=blank):
                with self.assertRaises(ValueError):
                    self.knowledge.resolve_event_entry_by_id(blank, create_if_missing=True)

    def test_event_state_without_id_raises(self) -> None:
        with self.assertRaises(ValueError):
            self.knowledge.resolve_event_entry({"event": {}}, create_if_missing=True)

    def test_append_event_note_rejects_blank_note(self) -> None:
        with self.assertRaises(ValueError):
            self.knowledge.append_event_note_by_id("cleric", "  ")


class EnumeratePathsTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.tmp_path = Path(self._tmp.name)

    def test_branches_are_enumerated_and_cycles_stop(self) -> None:
        nodes = {
            (0, 0): {
                "row": 0,
                "col": 0,
                "children": [{"row": 1, "col": 0}, {"row": 1, "col": 1}],
            },
            (1, 0): {"row": 1, "col": 0, "children": [{"row": 0, "col": 0}]},
            (1, 1): {"row": 1, "col": 1, "children": []},
        }

        paths = _enumerate_paths(nodes, (0, 0))

        self.assertEqual(len(paths), 2)
        self.assertEqual([[node["col"] for node in path] for path in paths], [[0, 0], [0, 1]])

    def test_missing_start_node_returns_no_paths(self) -> None:
        self.assertEqual(_enumerate_paths({}, (0, 0)), [])

    def test_route_options_count_branches(self) -> None:
        knowledge_base = Sts2KnowledgeBase(root_dir=self.tmp_path / "kb")
        state = {
            "run_id": "run_1",
            "screen": "MAP",
            "map": {
                "nodes": [
                    {"row": 0, "col": 0, "children": [{"row": 1, "col": 0}, {"row": 1, "col": 1}]},
                    {"row": 1, "col": 0, "children": []},
                    {"row": 1, "col": 1, "children": []},
                ],
                "available_nodes": [{"row": 0, "col": 0}],
            },
        }

        context = knowledge_base.build_planner_context(state)

        self.assertEqual(len(context["route_options"]), 1)
        self.assertEqual(context["route_options"][0]["path_count"], 2)


if __name__ == "__main__":
    unittest.main()
