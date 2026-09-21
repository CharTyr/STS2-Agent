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

Three things are compared, all of them source text on the C# side:

1. `SceneFieldSets` -> which fields a scene projects;
2. `SceneItemSources` / `FallbackItemSources` -> which state paths a scene reads its
   ids from;
3. the screen -> scene mapping, through the expectation table in
   `STS2AIAgent.Tests/AgentLoopTests.cs` (`DetectScene_MatchesGuidedMcpRules`), which
   the C# suite asserts against the real `GameDataFilter.DetectScene` and this module
   reads back and asserts against `_detect_scene_from_screen`. A screen that one side
   calls combat and the other calls reward would otherwise return different ids for
   the same call, which is exactly the defect the offered-choice screens had.

Parallel to all of that, `SceneDerivedIdTests` pins what each supported screen
actually derives, with fixtures that carry the owned ids *and* the offered ones so a
silent fall-through to the deck cannot pass.

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

from sts2_mcp.game_data import (
    _FALLBACK_ITEM_SOURCES,
    _SCENE_FIELD_SETS,
    _SCENE_ITEM_SOURCES,
    _detect_scene_from_screen,
    derive_relevant_item_ids,
)

_CSHARP_FILTER = "STS2AIAgent/Agent/GameDataFilter.cs"
_CSHARP_EXPORT_SCHEMA = "STS2AIAgent/Agent/GameDataExportSchema.cs"
_CSHARP_DETECT_SCENE_TEST = "STS2AIAgent.Tests/AgentLoopTests.cs"

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


# Assert.Equal("reward", GameDataFilter.DetectScene("REWARD"));
# One screen per line, so the C# suite's expectation table can be read back here verbatim. Group 1
# is the scene, group 2 the screen; the caller flips them into screen -> scene.
_DETECT_SCENE_EXPECTATION = re.compile(
    r'^\s*Assert\.Equal\("([a-z_]+)",\s*GameDataFilter\.DetectScene\("([A-Za-z0-9_]+)"\)\);\s*$',
    re.MULTILINE,
)


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
        expected_scenes = {
            "combat",
            "shop",
            "event",
            "reward",
            "card_selection",
            "chest",
            "bundle_selection",
        }
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


class SceneDetectionAlignmentTests(unittest.TestCase):
    """The screen -> scene mapping, pinned once and read back from both languages.

    `GameDataFilterTests.DetectScene_MatchesGuidedMcpRules` (C#) asserts the real
    `GameDataFilter.DetectScene` against a one-screen-per-line expectation table. This class
    parses those lines and asserts the Python mirror agrees, so the two sides cannot classify
    the same screen differently -- which is what let the offered-choice screens answer with the
    deck the player already owned while the C# side (had it been asked) would have said the same.
    """

    # Mirrors the C# expectation table; the assertions below compare the two in both directions.
    _EXPECTED: dict[str, str] = {
        "SHOP": "shop",
        "FAKE_MERCHANT": "shop",
        "EVENT": "event",
        "COMBAT": "combat",
        "COMBAT_REWARD": "combat",
        "REWARD": "reward",
        "CARD_SELECTION": "card_selection",
        "CHEST": "chest",
        "BUNDLE_SELECTION": "bundle_selection",
        "MAP": "menu",
        "MAIN_MENU": "menu",
    }

    @classmethod
    def setUpClass(cls) -> None:
        cls.source_root = _find_source_root()

    def _csharp_expectations(self) -> dict[str, str]:
        source = (self.source_root / _CSHARP_DETECT_SCENE_TEST).read_text(encoding="utf-8")
        pairs = {screen: scene for scene, screen in _DETECT_SCENE_EXPECTATION.findall(source)}
        if len(pairs) < 8:
            raise AssertionError(
                "GameDataFilterTests.DetectScene_MatchesGuidedMcpRules parsed to "
                f"{len(pairs)} expectation(s); the table shape changed or the method was renamed. "
                "The parse must never come back nearly empty, or this comparison checks nothing."
            )
        return pairs

    def test_the_csharp_expectation_table_is_the_one_this_test_mirrors(self) -> None:
        self.assertEqual(
            self._EXPECTED,
            self._csharp_expectations(),
            "the C# screen -> scene expectation table and the Python mirror disagree; both are "
            "listed one screen per line so a change has to be made on both sides",
        )

    def test_python_classifies_every_screen_the_csharp_side_pins(self) -> None:
        mismatches = [
            f"{screen}: python={_detect_scene_from_screen(screen)} expected={scene}"
            for screen, scene in sorted(self._csharp_expectations().items())
            if _detect_scene_from_screen(screen) != scene
        ]
        self.assertEqual(
            [],
            mismatches,
            "Python _detect_scene_from_screen disagrees with the pinned table:\n  - "
            + "\n  - ".join(mismatches),
        )

    def test_every_scene_the_tables_use_is_pinned_by_a_screen(self) -> None:
        # A scene added to SceneFieldSets/SceneItemSources without a screen that classifies into it
        # is dead weight: nothing would ever read it, and the C# table could add it alone.
        table_scenes = set(_SCENE_FIELD_SETS) | set(_SCENE_ITEM_SOURCES)
        pinned = set(self._csharp_expectations().values())
        self.assertEqual(
            set(),
            table_scenes - pinned,
            "these scenes have tables but no screen in the pinned mapping classifies into them: "
            + ", ".join(sorted(table_scenes - pinned)),
        )


def _run_block(
    deck: list[str] | None = None,
    relics: list[str] | None = None,
    potions: list[str] | None = None,
) -> dict[str, object]:
    """A `run` block carrying the ids the player already owns."""
    return {
        "deck": [{"card_id": card_id} for card_id in deck or []],
        "relics": [{"relic_id": relic_id} for relic_id in relics or []],
        "potions": [{"potion_id": potion_id} for potion_id in potions or []],
    }


class SceneDerivedIdTests(unittest.TestCase):
    """The ids each supported screen derives, per collection.

    Every fixture carries the ids the player already owns *and* the ones the screen offers, and
    asserts the owned ones are absent: a path that silently fell through to the run-level fallback
    still returns something, so a test with an empty run block would pass while the defect lives on.
    """

    OWNED = _run_block(deck=["OWNED_CARD_A", "OWNED_CARD_B"], relics=["OWNED_RELIC"], potions=["OWNED_POTION"])

    def test_reward_screen_derives_the_offered_cards_not_the_deck(self) -> None:
        state = {
            "screen": "REWARD",
            "reward": {
                "card_options": [
                    {"index": 0, "card_id": "OFFER_ALPHA"},
                    {"index": 1, "card_id": "OFFER_BETA"},
                ]
            },
            "run": self.OWNED,
        }

        ids = derive_relevant_item_ids(state, "cards", "REWARD")

        self.assertEqual(["OFFER_ALPHA", "OFFER_BETA"], ids)
        self.assertNotIn("OWNED_CARD_A", ids)

    def test_reward_screen_derives_from_the_compact_view_when_the_raw_list_is_absent(self) -> None:
        # One /state response carries both views and they name the offered cards differently; the
        # compact one alone has to answer, so the tool works for a caller holding only that.
        state = {
            "screen": "REWARD",
            "reward": None,
            "agent_view": {
                "reward": {"cards": [{"i": 0, "card_id": "OFFER_ALPHA"}, {"i": 1, "card_id": "OFFER_BETA"}]}
            },
            "run": self.OWNED,
        }

        self.assertEqual(
            ["OFFER_ALPHA", "OFFER_BETA"],
            derive_relevant_item_ids(state, "cards", "REWARD"),
        )

    def test_reward_screen_dedups_the_offered_cards_in_surface_order(self) -> None:
        state = {
            "screen": "REWARD",
            "reward": {
                "card_options": [
                    {"index": 0, "card_id": "OFFER_BETA"},
                    {"index": 1, "card_id": "OFFER_ALPHA"},
                    {"index": 2, "card_id": "OFFER_BETA"},
                ]
            },
            "run": self.OWNED,
        }

        self.assertEqual(
            ["OFFER_BETA", "OFFER_ALPHA"],
            derive_relevant_item_ids(state, "cards", "REWARD"),
        )

    def test_reward_relic_query_never_returns_the_offered_cards(self) -> None:
        # The policy is collection-aware: the reward screen offers cards, so a relic question is not
        # answered with card ids. Nothing on the screen names a relic, so the owned relics answer.
        state = {
            "screen": "REWARD",
            "reward": {"card_options": [{"index": 0, "card_id": "OFFER_ALPHA"}]},
            "run": self.OWNED,
        }

        ids = derive_relevant_item_ids(state, "relics", "REWARD")

        self.assertEqual(["OWNED_RELIC"], ids)
        self.assertNotIn("OFFER_ALPHA", ids)

    def test_reward_potion_row_carries_no_id_so_the_run_potions_answer(self) -> None:
        # A reward row has a type and a description and no stable id; inventing one would be worse
        # than the run-level fallback, so the table declares no potion source for this scene.
        state = {
            "screen": "REWARD",
            "reward": {
                "rewards": [
                    {"index": 0, "reward_type": "Potion", "description": "Fire Potion", "claimable": True}
                ]
            },
            "run": self.OWNED,
        }

        self.assertEqual(["OWNED_POTION"], derive_relevant_item_ids(state, "potions", "REWARD"))

    def test_card_selection_derives_the_offered_cards_not_the_deck(self) -> None:
        state = {
            "screen": "CARD_SELECTION",
            "selection": {
                "cards": [
                    {"index": 0, "card_id": "SELECT_ALPHA"},
                    {"index": 1, "card_id": "SELECT_ALPHA"},
                    {"index": 2, "card_id": "SELECT_BETA"},
                ]
            },
            "run": self.OWNED,
        }

        ids = derive_relevant_item_ids(state, "cards", "CARD_SELECTION")

        self.assertEqual(["SELECT_ALPHA", "SELECT_BETA"], ids)
        self.assertNotIn("OWNED_CARD_A", ids)

    def test_card_selection_relic_query_never_returns_the_offered_cards(self) -> None:
        state = {
            "screen": "CARD_SELECTION",
            "selection": {"cards": [{"index": 0, "card_id": "SELECT_ALPHA"}]},
            "run": self.OWNED,
        }

        ids = derive_relevant_item_ids(state, "relics", "CARD_SELECTION")

        self.assertEqual(["OWNED_RELIC"], ids)
        self.assertNotIn("SELECT_ALPHA", ids)

    def test_chest_derives_the_offered_relics_not_the_owned_ones(self) -> None:
        state = {
            "screen": "CHEST",
            "chest": {
                "is_opened": True,
                "relic_options": [
                    {"index": 0, "relic_id": "OFFER_RELIC_ALPHA"},
                    {"index": 1, "relic_id": "OFFER_RELIC_BETA"},
                ],
            },
            "run": self.OWNED,
        }

        ids = derive_relevant_item_ids(state, "relics", "CHEST")

        self.assertEqual(["OFFER_RELIC_ALPHA", "OFFER_RELIC_BETA"], ids)
        self.assertNotIn("OWNED_RELIC", ids)

    def test_chest_derives_from_the_compact_view_when_the_raw_list_is_absent(self) -> None:
        state = {
            "screen": "CHEST",
            "chest": None,
            "agent_view": {"chest": {"opened": True, "relics": [{"i": 0, "relic_id": "OFFER_RELIC_ALPHA"}]}},
            "run": self.OWNED,
        }

        self.assertEqual(
            ["OFFER_RELIC_ALPHA"],
            derive_relevant_item_ids(state, "relics", "CHEST"),
        )

    def test_chest_card_query_falls_back_to_the_owned_deck(self) -> None:
        state = {
            "screen": "CHEST",
            "chest": {"relic_options": [{"index": 0, "relic_id": "OFFER_RELIC_ALPHA"}]},
            "run": self.OWNED,
        }

        ids = derive_relevant_item_ids(state, "cards", "CHEST")

        self.assertEqual(["OWNED_CARD_A", "OWNED_CARD_B"], ids)
        self.assertNotIn("OFFER_RELIC_ALPHA", ids)

    def test_bundle_selection_derives_every_card_of_every_bundle(self) -> None:
        state = {
            "screen": "BUNDLE_SELECTION",
            "bundles": [
                {"index": 0, "cards": [{"index": 0, "card_id": "BUNDLE_A_ONE"}, {"index": 1, "card_id": "BUNDLE_A_TWO"}]},
                {"index": 1, "cards": [{"index": 0, "card_id": "BUNDLE_B_ONE"}]},
            ],
            "run": self.OWNED,
        }

        ids = derive_relevant_item_ids(state, "cards", "BUNDLE_SELECTION")

        self.assertEqual(["BUNDLE_A_ONE", "BUNDLE_A_TWO", "BUNDLE_B_ONE"], ids)
        self.assertNotIn("OWNED_CARD_A", ids)

    def test_shop_derives_the_offer_ids_for_cards_relics_and_potions(self) -> None:
        state = {
            "screen": "SHOP",
            "shop": {
                "cards": [{"index": 0, "card_id": "STOCK_CARD"}],
                "relics": [{"index": 0, "relic_id": "STOCK_RELIC"}],
                "potions": [{"index": 0, "potion_id": "STOCK_POTION"}],
            },
            "run": self.OWNED,
        }

        self.assertEqual(["STOCK_CARD"], derive_relevant_item_ids(state, "cards", "SHOP"))
        self.assertEqual(["STOCK_RELIC"], derive_relevant_item_ids(state, "relics", "SHOP"))
        self.assertEqual(["STOCK_POTION"], derive_relevant_item_ids(state, "potions", "SHOP"))

    def test_collection_names_are_matched_case_insensitively(self) -> None:
        # The C# dictionaries are OrdinalIgnoreCase and the tool is the one place a caller spells
        # the collection by hand, so "Cards" has to reach the same source as "cards".
        state = {
            "screen": "REWARD",
            "reward": {"card_options": [{"index": 0, "card_id": "OFFER_ALPHA"}]},
            "run": self.OWNED,
        }

        self.assertEqual(
            ["OFFER_ALPHA"],
            derive_relevant_item_ids(state, "Cards", "reward"),
        )

    def test_a_screen_with_no_offer_ids_still_falls_back_to_the_owned_ids(self) -> None:
        # "Only then" in the policy: the fallback answers exactly when the screen's own source for
        # this collection yields nothing, so an unaffected screen keeps its previous behaviour.
        state = {"screen": "MAP", "run": self.OWNED}

        self.assertEqual(["OWNED_CARD_A", "OWNED_CARD_B"], derive_relevant_item_ids(state, "cards", "MAP"))
        self.assertEqual([], derive_relevant_item_ids(state, "events", "MAP"))


if __name__ == "__main__":
    unittest.main()
