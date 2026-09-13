"""Cross-language contract: the C# and Python "scene -> relevant fields" tables.

The same knowledge ("which fields does scene X need from collection Y") is written
twice: once in C# for the in-game agent and the native MCP server, once in Python for
the sidecar MCP server that is the default path. Nothing compared the two, so they
drifted silently and the same get_relevant_game_data call returned different fields
depending on which side answered it.

This module is that comparison. It reads both tables as source text (C# is never
imported and no game is needed) and asserts the (scene, collection) -> field-set
mapping is equal in both directions, so a field added to only one side fails here
instead of quietly changing what the tool returns.

C# is the reference side: GameDataFilter.SceneFieldSets is the copy that
GameDataExportSchemaTests (STS2AIAgent.Tests/AgentLoopTests.cs) already keeps aligned
with the export contract in GameDataExportSchema.cs, in both directions. When the two
tables disagree, Python follows C#.

Only the field *sets* are compared, not their order: get_game_data_items_fields
filters by membership, so order does not change which keys an item keeps.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

from sts2_mcp.server import (
    _FALLBACK_ITEM_SOURCES,
    _SCENE_FIELD_SETS,
    _SCENE_ITEM_SOURCES,
    derive_relevant_item_ids,
)

_CSHARP_FILTER = "STS2AIAgent/Agent/GameDataFilter.cs"
_CSHARP_EXPORT_SCHEMA = "STS2AIAgent/Agent/GameDataExportSchema.cs"

# private static readonly Dictionary<...> SceneFieldSets = new(StringComparer...)
_FILTER_TABLE = re.compile(r"SceneFieldSets\s*=\s*new\(")
# ["combat"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
_SCENE_ENTRY = re.compile(r'\["(\w+)"\]\s*=\s*new\s+Dictionary<string,\s*string\[\]>')
# ["cards"] = new[] { "id", "name", ... }
_FIELD_ENTRY = re.compile(r'\["(\w+)"\]\s*=\s*new\[\]\s*\{([^}]*)\}')
_QUOTED = re.compile(r'"([^"]*)"')

# public static readonly IReadOnlyDictionary<string, string[]> Collections =
_EXPORT_TABLE = re.compile(r"\bCollections\s*=\s*new\s+Dictionary<string,\s*string\[\]>")

# public static readonly IReadOnlyDictionary<...> SceneItemSources = new(...)
# The declaration wraps onto the next line and the type carries generics, so the marker stops at
# `new` and _initializer_block finds the brace that opens the dictionary.
_CSHARP_ITEM_SOURCES = re.compile(r"SceneItemSources\s*=\s*new")
_CSHARP_FALLBACK_SOURCES = re.compile(r"FallbackItemSources\s*=\s*new")
# ["cards"] = new[] { "combat.hand[].card_id" },
_PATH_ENTRY = re.compile(r'\["(\w+)"\]\s*=\s*new\[\]\s*\{([^}]*)\}')


def _find_source_root() -> Path:
    """Locate the repository root the way the other source-level tests do."""
    candidates = [Path(__file__).resolve().parents[2], Path.cwd()]
    for candidate in candidates:
        if (candidate / _CSHARP_FILTER).is_file() and (candidate / _CSHARP_EXPORT_SCHEMA).is_file():
            return candidate
    raise AssertionError(
        "Could not locate the C# source contract required for scene field alignment: "
        f"{_CSHARP_FILTER} and {_CSHARP_EXPORT_SCHEMA}"
    )


def _initializer_block(source: str, marker: re.Pattern[str], label: str) -> str:
    """Return the brace-delimited initializer that follows marker in source."""
    match = marker.search(source)
    if match is None:
        raise AssertionError(f"{label}: could not locate the initializer marker {marker.pattern!r}")

    start = source.find("{", match.end())
    if start < 0:
        raise AssertionError(f"{label}: initializer has no opening brace")

    depth = 0
    for index in range(start, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1 : index]

    raise AssertionError(f"{label}: initializer is unterminated")


def _parse_csharp_scene_field_sets(source_root: Path) -> dict[tuple[str, str], list[str]]:
    """Parse GameDataFilter.SceneFieldSets into {(scene, collection): [fields]}."""
    source = (source_root / _CSHARP_FILTER).read_text(encoding="utf-8")
    block = _initializer_block(source, _FILTER_TABLE, "GameDataFilter.SceneFieldSets")

    scene_matches = list(_SCENE_ENTRY.finditer(block))
    if not scene_matches:
        raise AssertionError(
            "GameDataFilter.cs SceneFieldSets parsed to no scenes; the table shape changed"
        )

    pairs: dict[tuple[str, str], list[str]] = {}
    for index, match in enumerate(scene_matches):
        scene = match.group(1)
        end = scene_matches[index + 1].start() if index + 1 < len(scene_matches) else len(block)
        entries = _FIELD_ENTRY.findall(block[match.end() : end])
        if not entries:
            raise AssertionError(f"GameDataFilter.cs scene {scene!r} parsed to no collections")
        for collection, raw in entries:
            fields = _QUOTED.findall(raw)
            if not fields:
                raise AssertionError(
                    f"GameDataFilter.cs {scene}/{collection} parsed to no field names"
                )
            pairs[(scene, collection)] = fields

    return pairs


def _parse_csharp_item_sources(source_root: Path) -> tuple[dict[tuple[str, str], list[str]], dict[str, list[str]]]:
    """Parse GameDataFilter.SceneItemSources and FallbackItemSources into path lists.

    Returns ((scene, collection) -> paths, collection -> paths). The paths themselves are compared,
    not just their keys: a source that drifts to a different field is a different answer.
    """
    source = (source_root / _CSHARP_FILTER).read_text(encoding="utf-8")
    block_text = _initializer_block(source, _CSHARP_ITEM_SOURCES, "GameDataFilter.SceneItemSources")

    scene_matches = list(_SCENE_ENTRY.finditer(block_text))
    if not scene_matches:
        raise AssertionError(
            "GameDataFilter.cs SceneItemSources parsed to no scenes; the table shape changed"
        )

    scenes: dict[tuple[str, str], list[str]] = {}
    for index, match in enumerate(scene_matches):
        scene = match.group(1)
        end = scene_matches[index + 1].start() if index + 1 < len(scene_matches) else len(block_text)
        entries = _PATH_ENTRY.findall(block_text[match.end() : end])
        if not entries:
            raise AssertionError(f"GameDataFilter.cs scene {scene!r} parsed to no id sources")
        for collection, raw in entries:
            paths = _QUOTED.findall(raw)
            if not paths:
                raise AssertionError(f"GameDataFilter.cs {scene}/{collection} parsed to no paths")
            scenes[(scene, collection)] = paths

    fallback_block = _initializer_block(source, _CSHARP_FALLBACK_SOURCES, "GameDataFilter.FallbackItemSources")
    fallback = {name: _QUOTED.findall(raw) for name, raw in _PATH_ENTRY.findall(fallback_block)}
    if not fallback:
        raise AssertionError("GameDataFilter.cs FallbackItemSources parsed to no collections")

    return scenes, fallback


def _python_item_source_pairs() -> tuple[dict[tuple[str, str], list[str]], dict[str, list[str]]]:
    scenes = {
        (scene, collection): list(paths)
        for scene, collections in _SCENE_ITEM_SOURCES.items()
        for collection, paths in collections.items()
    }
    fallback = {collection: list(paths) for collection, paths in _FALLBACK_ITEM_SOURCES.items()}
    return scenes, fallback


def _parse_csharp_export_schema(source_root: Path) -> dict[str, list[str]]:
    """Parse GameDataExportSchema.Collections into {collection: [exported fields]}."""
    source = (source_root / _CSHARP_EXPORT_SCHEMA).read_text(encoding="utf-8")
    block = _initializer_block(source, _EXPORT_TABLE, "GameDataExportSchema.Collections")

    schema = {name: _QUOTED.findall(raw) for name, raw in _FIELD_ENTRY.findall(block)}
    if len(schema) < 5:
        raise AssertionError(
            "GameDataExportSchema.cs Collections parse looks wrong: " + ", ".join(sorted(schema))
        )
    return schema


def _python_pairs() -> dict[tuple[str, str], list[str]]:
    return {
        (scene, collection): list(fields)
        for scene, collections in _SCENE_FIELD_SETS.items()
        for collection, fields in collections.items()
    }


class SceneFieldAlignmentTests(unittest.TestCase):
    """GameDataFilter.SceneFieldSets (C#) and _SCENE_FIELD_SETS (Python) must agree."""

    @classmethod
    def setUpClass(cls) -> None:
        cls.source_root = _find_source_root()
        cls.csharp = _parse_csharp_scene_field_sets(cls.source_root)
        cls.export_schema = _parse_csharp_export_schema(cls.source_root)
        cls.python = _python_pairs()

    def _export_hint(self, collection: str) -> str:
        exported = self.export_schema.get(collection)
        if exported is None:
            return f"{collection!r} is not an exported /data collection"
        return f"exported {collection} fields: {sorted(exported)}"

    def test_parsers_see_the_real_tables(self) -> None:
        # A parse that silently returns {} would make every other assertion vacuous.
        # Only a subset (not equality) is pinned, so adding a scene to both tables is
        # not blocked here while a parse that lost the real table still fails.
        expected_scenes = {"combat", "shop", "event"}
        self.assertEqual(
            set(),
            expected_scenes - {scene for scene, _ in self.csharp},
            "GameDataFilter.SceneFieldSets parse lost a known scene",
        )
        self.assertEqual(
            set(),
            expected_scenes - {scene for scene, _ in self.python},
            "_SCENE_FIELD_SETS lost a known scene",
        )
        self.assertGreaterEqual(len(self.csharp), 8)
        self.assertGreaterEqual(len(self.export_schema), 5)
        for pair, fields in self.csharp.items():
            self.assertTrue(fields, f"C# table parsed an empty field list for {pair}")

    def test_scene_names_match_in_both_directions(self) -> None:
        csharp_scenes = {scene for scene, _ in self.csharp}
        python_scenes = {scene for scene, _ in self.python}
        self.assertEqual(
            set(),
            csharp_scenes - python_scenes,
            "Scenes defined in GameDataFilter.SceneFieldSets but not in _SCENE_FIELD_SETS: "
            + ", ".join(sorted(csharp_scenes - python_scenes)),
        )
        self.assertEqual(
            set(),
            python_scenes - csharp_scenes,
            "Scenes defined in _SCENE_FIELD_SETS but not in GameDataFilter.SceneFieldSets: "
            + ", ".join(sorted(python_scenes - csharp_scenes)),
        )

    def test_scene_collection_pairs_match_in_both_directions(self) -> None:
        only_in_csharp = set(self.csharp) - set(self.python)
        only_in_python = set(self.python) - set(self.csharp)
        self.assertEqual(
            set(),
            only_in_csharp,
            "(scene, collection) pairs defined in C# but missing from Python "
            "_SCENE_FIELD_SETS: " + ", ".join(f"{s}/{c}" for s, c in sorted(only_in_csharp)),
        )
        self.assertEqual(
            set(),
            only_in_python,
            "(scene, collection) pairs defined in Python _SCENE_FIELD_SETS but absent "
            "from C# GameDataFilter.SceneFieldSets: "
            + ", ".join(f"{s}/{c}" for s, c in sorted(only_in_python)),
        )

    def test_scene_field_sets_match_in_both_directions(self) -> None:
        diffs: list[str] = []
        for scene, collection in sorted(set(self.csharp) & set(self.python)):
            csharp_fields = set(self.csharp[(scene, collection)])
            python_fields = set(self.python[(scene, collection)])
            only_in_csharp = csharp_fields - python_fields
            only_in_python = python_fields - csharp_fields
            if not only_in_csharp and not only_in_python:
                continue
            diffs.append(
                f"{scene}/{collection}: "
                f"only_in_csharp={sorted(only_in_csharp)} "
                f"only_in_python={sorted(only_in_python)}; "
                + self._export_hint(collection)
            )

        self.assertEqual(
            [],
            diffs,
            "Scene field sets diverge between GameDataFilter.SceneFieldSets (C#) and "
            "_SCENE_FIELD_SETS (Python):\n  - " + "\n  - ".join(diffs),
        )

    def test_item_sources_match_in_both_directions(self) -> None:
        csharp_scenes, csharp_fallback = _parse_csharp_item_sources(self.source_root)
        python_scenes, python_fallback = _python_item_source_pairs()

        only_in_csharp = set(csharp_scenes) - set(python_scenes)
        only_in_python = set(python_scenes) - set(csharp_scenes)
        self.assertEqual(
            set(),
            only_in_csharp,
            "(scene, collection) id sources defined in C# but missing from Python "
            "_SCENE_ITEM_SOURCES: " + ", ".join(f"{s}/{c}" for s, c in sorted(only_in_csharp)),
        )
        self.assertEqual(
            set(),
            only_in_python,
            "(scene, collection) id sources defined in Python _SCENE_ITEM_SOURCES but absent "
            "from C# GameDataFilter.SceneItemSources: "
            + ", ".join(f"{s}/{c}" for s, c in sorted(only_in_python)),
        )

        # The paths are the answer, so a pair whose paths differ is a divergence too.
        diffs = [
            f"{scene}/{collection}: csharp={csharp_scenes[(scene, collection)]} "
            f"python={python_scenes[(scene, collection)]}"
            for scene, collection in sorted(set(csharp_scenes) & set(python_scenes))
            if csharp_scenes[(scene, collection)] != python_scenes[(scene, collection)]
        ]
        self.assertEqual(
            [],
            diffs,
            "Scene item sources diverge between GameDataFilter.SceneItemSources (C#) and "
            "_SCENE_ITEM_SOURCES (Python):\n  - " + "\n  - ".join(diffs),
        )

        self.assertEqual(
            python_fallback,
            csharp_fallback,
            "FallbackItemSources diverges between GameDataFilter.cs and _FALLBACK_ITEM_SOURCES",
        )

        # Both tables must actually carry the ids the earlier tests rely on, so a parse that
        # silently returned {} cannot make the comparisons above vacuous.
        self.assertTrue(csharp_scenes, "GameDataFilter.SceneItemSources parsed to nothing")
        self.assertTrue(python_scenes, "_SCENE_ITEM_SOURCES is empty")
        self.assertTrue(csharp_fallback, "GameDataFilter.FallbackItemSources parsed to nothing")

    def test_derived_ids_come_from_live_state(self) -> None:
        # The scene-aware call has to read the ids off the state payload, not off a constant:
        # the same collection answers differently on the map and in a fight.
        combat_state = {
            "screen": "COMBAT",
            "combat": {
                "hand": [{"card_id": "STRIKE_IRONCLAD"}, {"card_id": "DEFEND_IRONCLAD"}],
                "enemies": [{"enemy_id": "FUZZY_WURM_CRAWLER"}, {"enemy_id": "SHRINKER_BEETLE"}],
                "player": {"powers": []},
            },
            "run": {"deck": [{"card_id": "BASH"}], "potions": [{"potion_id": None}]},
            "shop": None,
            "event": None,
        }
        self.assertEqual(
            ["STRIKE_IRONCLAD", "DEFEND_IRONCLAD"],
            derive_relevant_item_ids(combat_state, "cards", "COMBAT"),
        )
        self.assertEqual(
            ["FUZZY_WURM_CRAWLER", "SHRINKER_BEETLE"],
            derive_relevant_item_ids(combat_state, "monsters", "COMBAT"),
        )
        # No potion ids in the state means no ids, not an error.
        self.assertEqual([], derive_relevant_item_ids(combat_state, "potions", "COMBAT"))
        # A deck lookup still answers on a screen that is about something else.
        self.assertEqual(
            ["BASH"],
            derive_relevant_item_ids(combat_state, "cards", "MAP"),
        )
        # An unknown collection derives nothing rather than guessing.
        self.assertEqual([], derive_relevant_item_ids(combat_state, "nonsense", "COMBAT"))

        # A screen can classify into a scene whose payload is absent on that screen: FAKE_MERCHANT
        # reads as shop, but there is no shop block, so the answer must fall back to the run-level
        # ids instead of coming back empty.
        shop_less_state = {
            "screen": "FAKE_MERCHANT",
            "shop": None,
            "combat": None,
            "run": {"deck": [{"card_id": "BASH"}], "relics": [{"relic_id": "BURNING_BLOOD"}]},
        }
        self.assertEqual(
            ["BASH"],
            derive_relevant_item_ids(shop_less_state, "cards", "FAKE_MERCHANT"),
        )
        self.assertEqual(
            ["BURNING_BLOOD"],
            derive_relevant_item_ids(shop_less_state, "relics", "FAKE_MERCHANT"),
        )

    def test_python_scene_fields_are_exported_by_the_mod(self) -> None:
        # Mirrors GameDataExportSchemaTests.SceneFieldsExistInTheExportSchema so a bogus
        # field name is reported here as well, not only by the C# test suite.
        violations: list[str] = []
        for (scene, collection), fields in sorted(self.python.items()):
            exported = self.export_schema.get(collection)
            if exported is None:
                violations.append(f"{scene}/{collection}: not an exported /data collection")
                continue
            unknown = [field for field in fields if field not in set(exported)]
            if unknown:
                violations.append(
                    f"{scene}/{collection}: {sorted(unknown)} are not exported; "
                    + self._export_hint(collection)
                )

        self.assertEqual(
            [],
            violations,
            "Python scene field sets reference fields the mod never exports:\n  - "
            + "\n  - ".join(violations),
        )


if __name__ == "__main__":
    unittest.main()
