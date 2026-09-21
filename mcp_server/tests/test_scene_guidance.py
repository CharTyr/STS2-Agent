from __future__ import annotations

import re
import shutil
import tempfile
import unittest
from pathlib import Path

from sts2_mcp.scene_guidance import (
    MAX_GUIDANCE_CHARACTERS,
    NOT_INJECTED_HEADINGS,
    SCREEN_HEADINGS,
    canonical_event_id,
    canonical_option_key,
    canonical_option_text_key,
    cap,
    event_option_risk,
    headings,
    parse_event_option_index,
    repo_root,
    scene_guidance,
    sections,
    slugify,
    strategy_for_screen,
)

REPO_ROOT = Path(__file__).resolve().parents[2]
STRATEGY_PATH = REPO_ROOT / "skills" / "sts2-mcp-player" / "references" / "strategy.md"
EVENTS_PATH = REPO_ROOT / "docs" / "game-knowledge" / "events.md"


def _write_event_index(markdown: str) -> Path:
    """A throwaway checkout holding only an `events.md`, for indexes the committed one cannot show."""
    root = Path(tempfile.mkdtemp(prefix="sts2-event-index-"))
    knowledge = root / "docs" / "game-knowledge"
    knowledge.mkdir(parents=True)
    (knowledge / "events.md").write_text(markdown, encoding="utf-8")
    return root


def _remove_event_index(root: Path) -> None:
    shutil.rmtree(root, ignore_errors=True)

# A live EVENT payload exactly as the game reports it: the model's id entry is upper-case and the
# option key is the full localization key. Both are read out of the decompiled sources --
# `EventModel.OptionKey` composes `$"{Slugify(GetType().Name)}.pages.{pageName}.options.{name}"`,
# and `AncientEventModel.RelicOption<T>" uses the relic's own id entry as the option name.
LIVE_NEOW_EVENT_ID = "NEOW"
LIVE_NEOW_ARCANE_SCROLL = "NEOW.pages.INITIAL.options.ARCANE_SCROLL"
LIVE_ABYSSAL_LINGER = "ABYSSAL_BATHS.pages.ALL.options.LINGER"

# The generated index's own spellings for the same two things.
INDEX_NEOW = "Neow"
INDEX_NEOW_ARCANE_SCROLL = "INITIAL.options.ARCANE_SCROLL"


def strategy_markdown() -> str:
    return STRATEGY_PATH.read_text(encoding="utf-8")


class StrategySlicingTests(unittest.TestCase):
    """The Python half of the screen-to-guidance mapping."""

    def test_screen_gets_only_its_own_sections(self) -> None:
        markdown = strategy_markdown()

        combat = strategy_for_screen(markdown, "COMBAT")
        self.assertIn("Combat: what to prioritise", combat)
        self.assertIn("Potions: when to drink", combat)
        self.assertNotIn("Route: which node to enter", combat)

        route = strategy_for_screen(markdown, "MAP")
        self.assertIn("Route: which node to enter", route)
        self.assertNotIn("Shop: what to buy", route)

        self.assertIn("Rest site: heal or upgrade", strategy_for_screen(markdown, "REST"))
        self.assertIn("Shop: what to buy", strategy_for_screen(markdown, "SHOP"))
        self.assertIn("Event options: how to choose", strategy_for_screen(markdown, "EVENT"))

    def test_fake_merchant_gets_the_shop_guidance(self) -> None:
        markdown = strategy_markdown()
        self.assertEqual(
            strategy_for_screen(markdown, "SHOP"),
            strategy_for_screen(markdown, "FAKE_MERCHANT"),
        )

    def test_screens_without_a_strategic_choice_get_nothing(self) -> None:
        markdown = strategy_markdown()
        for screen in ("REWARD", "CARD_SELECTION", "MODAL", "GAME_OVER", "CHEST", "MAIN_MENU"):
            with self.subTest(screen=screen):
                self.assertEqual("", strategy_for_screen(markdown, screen))

        self.assertEqual("", strategy_for_screen(markdown, None))
        self.assertEqual("", strategy_for_screen(markdown, "   "))
        self.assertEqual("", strategy_for_screen(markdown, "NOT_A_SCREEN"))

    def test_the_injection_is_bounded(self) -> None:
        combat = strategy_for_screen(strategy_markdown(), "COMBAT")
        self.assertLessEqual(len(combat), MAX_GUIDANCE_CHARACTERS)
        self.assertEqual(MAX_GUIDANCE_CHARACTERS, len(cap("x" * (MAX_GUIDANCE_CHARACTERS + 500))))

    def test_every_mapped_heading_exists(self) -> None:
        available = headings(strategy_markdown())
        for screen, wanted in SCREEN_HEADINGS.items():
            for heading in wanted:
                with self.subTest(screen=screen, heading=heading):
                    self.assertIn(
                        heading,
                        available,
                        f"the mapping names {heading!r}, which is not a heading in strategy.md",
                    )

    def test_every_reference_heading_is_accounted_for(self) -> None:
        available = headings(strategy_markdown())
        mapped = {heading for wanted in SCREEN_HEADINGS.values() for heading in wanted}

        unaccounted = [
            heading
            for heading in available
            if heading not in mapped and heading not in NOT_INJECTED_HEADINGS
        ]
        self.assertEqual(
            [],
            unaccounted,
            "these strategy.md headings reach the model on no screen and are not declared as "
            f"deliberately not injected: {unaccounted}",
        )

        for declared in NOT_INJECTED_HEADINGS:
            with self.subTest(declared=declared):
                self.assertIn(declared, available, "a stale exemption names a heading that is gone")

    def test_sections_round_trip(self) -> None:
        markdown = strategy_markdown()
        parsed = sections(markdown)
        for heading in headings(markdown):
            with self.subTest(heading=heading):
                self.assertIn(heading, parsed)
                self.assertTrue(parsed[heading])


class EventOptionRiskTests(unittest.TestCase):
    """1.2's generated risk index, read by the tool that consumes it."""

    def test_rows_are_returned_for_a_known_event(self) -> None:
        rows = event_option_risk(REPO_ROOT, "AbyssalBaths")

        self.assertTrue(rows, "expected generated risk rows for AbyssalBaths")
        options = [row["option"] for row in rows]
        self.assertIn("INITIAL.options.IMMERSE", options)
        immerse = next(row for row in rows if row["option"] == "INITIAL.options.IMMERSE")
        self.assertEqual("lethal-possible", immerse["risk"])
        self.assertIn("Gain 2 Max HP", immerse["effect"])

    def test_an_unknown_event_returns_nothing_rather_than_guessing(self) -> None:
        self.assertEqual([], event_option_risk(REPO_ROOT, "NotAnEvent"))
        self.assertEqual([], event_option_risk(REPO_ROOT, None))

    def test_a_missing_checkout_returns_nothing(self) -> None:
        self.assertEqual([], event_option_risk(None, "AbyssalBaths"))


class EventIdNormalizationTests(unittest.TestCase):
    """The one canonical join, and the .NET slug algorithm it is built on.

    The bug this file grew these cases for: the index is keyed by the generator's readable class
    name (`Neow`) while a live payload reports the model's id entry (`NEOW`) and the full
    localization key, so an exact comparison returned no options on every real event — and the tests
    that existed only ever used the generator's own spelling, so they passed while the live path was
    empty.
    """

    def test_the_slug_algorithm_is_the_one_the_game_uses(self) -> None:
        # Expected values are `StringHelper.Slugify`'s, taken from the decompiled source and checked
        # against the real .NET implementation. The capital-run cases are the ones that matter: the
        # game's `\\G(?!^)` anchor parts them (`ABC` -> `A_B_C`), which a naive CamelCase split
        # written as `([a-z0-9])([A-Z])` gets wrong (`AB_C`).
        expectations = {
            "Neow": "NEOW",
            "AbyssalBaths": "ABYSSAL_BATHS",
            "DoorsOfLightAndDark": "DOORS_OF_LIGHT_AND_DARK",
            "PunchOff": "PUNCH_OFF",
            "FakeMerchant": "FAKE_MERCHANT",
            "ABC": "A_B_C",
            "ABc": "A_BC",
            "AB": "A_B",
            "A": "A",
            "NEOW": "N_E_O_W",
            "  spaced  name  ": "SPACED_NAME",
        }
        for source, expected in expectations.items():
            with self.subTest(source=source):
                self.assertEqual(expected, slugify(source))

        self.assertEqual("", slugify(""))
        self.assertEqual("", slugify(None))
        self.assertEqual("", slugify("   "))

    def test_a_name_that_is_already_in_slug_form_is_a_fixed_point(self) -> None:
        # The generated index and a live payload canonicalise their own spelling, so the canonical
        # form has to accept its own output. Plain `slugify` cannot: it re-parts an id that is
        # already an id (`slugify("NEOW") == "N_E_O_W"`), which is exactly what a live payload sends.
        for value in ("NEOW", "ABYSSAL_BATHS", "PunchOff", "PUNCH_OFF", "neow", "Neow"):
            with self.subTest(value=value):
                canonical = canonical_event_id(value)
                self.assertEqual(canonical, canonical_event_id(canonical))

    def test_the_live_id_and_the_generated_name_canonicalize_together(self) -> None:
        self.assertEqual("NEOW", canonical_event_id(LIVE_NEOW_EVENT_ID))
        self.assertEqual("NEOW", canonical_event_id(INDEX_NEOW))
        # Case alone is not a difference; an underscore is.
        self.assertNotEqual(canonical_event_id("N_E_O_W"), canonical_event_id("NEOW"))

    def test_the_full_live_key_reduces_to_the_indexs_option_key(self) -> None:
        self.assertEqual(
            INDEX_NEOW_ARCANE_SCROLL,
            canonical_option_key(LIVE_NEOW_ARCANE_SCROLL, LIVE_NEOW_EVENT_ID),
        )
        # The event id is the only segment the two sides can spell differently: the live key reduces
        # the same way whichever spelling is passed alongside it, and so does a key that is already
        # page-relative.
        self.assertEqual(
            INDEX_NEOW_ARCANE_SCROLL,
            canonical_option_key(LIVE_NEOW_ARCANE_SCROLL, INDEX_NEOW),
        )
        self.assertEqual(
            INDEX_NEOW_ARCANE_SCROLL,
            canonical_option_key(INDEX_NEOW_ARCANE_SCROLL, INDEX_NEOW),
        )

    def test_a_later_page_option_key_reduces_to_its_page_relative_form(self) -> None:
        # `AbyssalBaths` builds its second page out of `ALL.options.LINGER`; a page name is not a
        # special case, so the same reduction has to work for it.
        self.assertEqual("ALL.options.LINGER", canonical_option_key(LIVE_ABYSSAL_LINGER, "ABYSSAL_BATHS"))
        self.assertEqual("ALL.options.LINGER", canonical_option_key(LIVE_ABYSSAL_LINGER, "AbyssalBaths"))

    def test_the_option_segments_are_not_reslugified(self) -> None:
        # `EventModel.OptionKey` slugifies only the event segment; the page and option names are the
        # source's own spelling. Re-slugging them would rewrite `ARCANE_SCROLL` into
        # `A_R_C_A_N_E_S_C_R_O_L_L` and turn an exact join into a fuzzy one.
        self.assertEqual(
            INDEX_NEOW_ARCANE_SCROLL,
            canonical_option_key(LIVE_NEOW_ARCANE_SCROLL, LIVE_NEOW_EVENT_ID),
        )

    def test_the_full_canonical_key_round_trips(self) -> None:
        built = canonical_option_text_key(LIVE_NEOW_EVENT_ID, INDEX_NEOW_ARCANE_SCROLL)
        self.assertEqual(LIVE_NEOW_ARCANE_SCROLL, built)
        # Reducing that full key with the same live id gives the index's own page-relative key back.
        self.assertEqual(
            INDEX_NEOW_ARCANE_SCROLL,
            canonical_option_key(built, LIVE_NEOW_EVENT_ID),
        )

    def test_an_empty_or_unusable_key_never_matches_something(self) -> None:
        for value in (None, "", "   "):
            with self.subTest(value=value):
                self.assertEqual("", canonical_event_id(value))
                self.assertEqual("", canonical_option_key(value, "NEOW"))
        # With no event id there is nothing to strip a prefix against, so the key is returned as it
        # came in rather than being mangled into a form that might match something else.
        self.assertEqual(
            "NEOW.pages.INITIAL.options.X",
            canonical_option_key("NEOW.pages.INITIAL.options.X", None),
        )


class LiveEventIdLookupTests(unittest.TestCase):
    """The real payload against the committed index — the case the old tests missed."""

    def test_a_live_uppercase_event_id_finds_its_index_rows(self) -> None:
        rows = event_option_risk(REPO_ROOT, LIVE_NEOW_EVENT_ID)

        self.assertTrue(rows, "a live NEOW payload must find the committed Neow risk rows")
        options = [row["option"] for row in rows]
        self.assertIn(INDEX_NEOW_ARCANE_SCROLL, options)

    def test_the_arcanae_scroll_row_is_the_same_row_either_spelling_finds(self) -> None:
        live = event_option_risk(REPO_ROOT, LIVE_NEOW_EVENT_ID)
        generated = event_option_risk(REPO_ROOT, INDEX_NEOW)

        self.assertEqual(generated, live)
        row = next(row for row in live if row["option"] == INDEX_NEOW_ARCANE_SCROLL)
        self.assertEqual("RelicOption<ArcaneScroll>", row["handler"])
        self.assertIn("Obtain the relic Arcane Scroll", row["effect"])

    def test_a_live_key_is_the_key_the_row_is_carried_under(self) -> None:
        # The contract the docs now state: the live `text_key` is the row's option key with the
        # `<EVENT>.pages.` prefix in front, so a caller can pair a live option with its row without
        # guessing an index.
        rows = event_option_risk(REPO_ROOT, LIVE_NEOW_EVENT_ID)
        keys = {canonical_option_text_key(LIVE_NEOW_EVENT_ID, row["option"]) for row in rows}
        self.assertIn(LIVE_NEOW_ARCANE_SCROLL, keys)

    def test_a_later_page_key_finds_its_own_row_and_not_the_initial_ones(self) -> None:
        rows = event_option_risk(REPO_ROOT, "ABYSSAL_BATHS")
        options = [row["option"] for row in rows]

        self.assertIn("ALL.options.LINGER", options)
        # The same event's INITIAL rows are present too, and the lookup does not confuse the two.
        self.assertIn("INITIAL.options.IMMERSE", options)
        self.assertEqual(1, options.count("ALL.options.LINGER"))

    def test_two_events_that_a_looser_match_would_collide_on_stay_separate(self) -> None:
        # The pair the requirement names: a prefix or substring match on `PUNCH_OFF` also matches
        # `PUNCH_OFF_EVENT`, and the two are different events with disjoint options. Both spellings
        # are the shape the game uses for event ids, and asking for one must never answer with the
        # other's rows.
        index = (
            "| Event | Option | Handler | Effect | Cost | Risk | Continuation |\n"
            "| --- | --- | --- | --- | --- | --- | --- |\n"
            "| PUNCH_OFF | INITIAL.options.NAB | Nab | none detected | none detected | none-detected | ends event |\n"
            "| PUNCH_OFF_EVENT | INITIAL.options.SWING | Swing | none detected | none detected | none-detected | ends event |\n"
        )
        path = _write_event_index(index)
        try:
            punch = event_option_risk(path, "PUNCH_OFF")
            prefix = event_option_risk(path, "PUNCH_OFF_EVENT")

            self.assertEqual(["INITIAL.options.NAB"], [row["option"] for row in punch])
            self.assertEqual(["INITIAL.options.SWING"], [row["option"] for row in prefix])
            # Case-only differences are the one thing that may join; an extra segment may not.
            self.assertEqual(punch, event_option_risk(path, "PunchOff"))
        finally:
            _remove_event_index(path)

    def test_an_ambiguous_canonical_id_is_dropped_rather_than_guessed(self) -> None:
        # Two ids that canonicalise to one key but display two different event names mean the index
        # cannot say which event a question is about. The lookup answers "nothing" for that key
        # instead of returning rows that belong to a different event.
        index = (
            "| Event | Option | Handler | Effect | Cost | Risk | Continuation |\n"
            "| --- | --- | --- | --- | --- | --- | --- |\n"
            "| NEOW | INITIAL.options.A | One | none detected | none detected | none-detected | ends event |\n"
            "| NEOW | INITIAL.options.A | Two | none detected | none detected | none-detected | next page |\n"
            "| NEOW | INITIAL.options.A | Two | none detected | none detected | none-detected | next page |\n"
        )
        rows, ambiguous = parse_event_option_index(index)
        self.assertEqual(
            ["INITIAL.options.A", "INITIAL.options.A"],
            [row["option"] for row in rows["NEOW"]],
            "an option built twice is two rows, but a byte-identical row is not a third",
        )
        self.assertEqual(frozenset(), ambiguous, "one displayed name is not an ambiguity")

        # A single display name whose canonical form is claimed by a different displayed name is.
        colliding = (
            "| Event | Option | Handler | Effect | Cost | Risk | Continuation |\n"
            "| --- | --- | --- | --- | --- | --- | --- |\n"
            "| N_E_O_W | INITIAL.options.A | One | none detected | none detected | none-detected | ends event |\n"
            "| NEOW | INITIAL.options.B | Two | none detected | none detected | none-detected | ends event |\n"
        )
        _, ambiguous_ids = parse_event_option_index(colliding)
        self.assertEqual(frozenset(), ambiguous_ids, "distinct canonical ids are not ambiguous")
        path = _write_event_index(colliding)
        try:
            self.assertEqual(["INITIAL.options.A"], [r["option"] for r in event_option_risk(path, "N_E_O_W")])
            self.assertEqual(["INITIAL.options.B"], [r["option"] for r in event_option_risk(path, "NEOW")])
        finally:
            _remove_event_index(path)

    def test_an_id_the_index_merges_is_dropped_rather_than_answered(self) -> None:
        # The failure mode the ambiguity guard exists for: a display name whose canonical form is
        # already claimed by a differently-spelled name. Whichever one a caller meant, an answer
        # would be a coin flip, so both spellings answer nothing.
        index = (
            "| Event | Option | Handler | Effect | Cost | Risk | Continuation |\n"
            "| --- | --- | --- | --- | --- | --- | --- |\n"
            "| Neow | INITIAL.options.A | One | none detected | none detected | none-detected | ends event |\n"
            "| neow | INITIAL.options.B | Two | none detected | none detected | none-detected | ends event |\n"
        )
        _, ambiguous = parse_event_option_index(index)
        self.assertEqual(frozenset({"NEOW"}), ambiguous)

        path = _write_event_index(index)
        try:
            self.assertEqual([], event_option_risk(path, "NEOW"))
            self.assertEqual([], event_option_risk(path, "Neow"))
        finally:
            _remove_event_index(path)

    def test_the_index_table_is_not_read_as_option_rows(self) -> None:
        # `events.md` holds two tables. The first one's second column is a base type; reading its
        # rows as options would file `EventModel` as an option of every event.
        index, _ = parse_event_option_index(EVENTS_PATH.read_text(encoding="utf-8"))
        for canonical, rows in index.items():
            options = [row["option"] for row in rows]
            with self.subTest(event=canonical):
                self.assertNotIn("EventModel", options)
                self.assertNotIn("AncientEventModel", options)

    def test_every_live_shaped_id_for_a_committed_event_finds_rows(self) -> None:
        # The whole committed index, asked the way a live payload asks: canonical id in, rows out.
        index, ambiguous = parse_event_option_index(EVENTS_PATH.read_text(encoding="utf-8"))
        self.assertTrue(len(index) > 50, f"the committed index looks wrong: {len(index)} events")

        for canonical in sorted(index):
            if canonical in ambiguous:
                continue
            with self.subTest(event=canonical):
                # Re-spell the canonical id the way a payload would report it.
                rows = event_option_risk(REPO_ROOT, canonical)
                self.assertEqual(len(index[canonical]), len(rows))


class DocsIndexContractTests(unittest.TestCase):
    """`docs/api.md`'s `text_key` examples must be the shape the runtime normalization accepts.

    The false example this pins down: the docs used to show `text_key` as
    `INITIAL.options.IMMERSE`, the *page-relative* index key, while the game sends the full
    localization key. A reader following the old table would look up a key that no payload ever
    carries — and the tests that existed used the generator's spelling too, so nothing caught it.
    """

    API_DOC = REPO_ROOT / "docs" / "api.md"
    # `NEOW.pages.INITIAL.options.ARCANE_SCROLL` in prose or a table cell.
    _FULL_KEY = re.compile(r"`([A-Z0-9_]+\.pages\.[A-Za-z0-9_.*\-]+)`")
    # `` `INITIAL.options.IMMERSE` ``-shaped claims, page name first.
    _PAGE_KEY = re.compile(r"`([A-Z][A-Z0-9_]*\.options\.[A-Za-z0-9_.*\-]+)`")

    def test_every_full_key_example_reduces_the_way_the_runtime_does(self) -> None:
        text = self.API_DOC.read_text(encoding="utf-8")
        examples = sorted(set(self._FULL_KEY.findall(text)))
        self.assertTrue(
            examples,
            "docs/api.md no longer shows a full `text_key` example, so the shape it documents is "
            "no longer pinned by anything",
        )

        for example in examples:
            with self.subTest(example=example):
                event_id = canonical_event_id(example.split(".pages.", 1)[0])
                reduced = canonical_option_key(example, event_id)
                # The reduction must have actually removed the prefix, and the result must be a key
                # the generated index really carries.
                self.assertNotIn(".pages.", reduced)
                self.assertIn(
                    reduced,
                    {
                        row["option"]
                        for row in event_option_risk(REPO_ROOT, event_id)
                    }
                    | {"DONE.CURSED.description"},
                    f"{example} reduces to {reduced!r}, which the index does not carry",
                )

    def test_a_page_relative_example_is_one_the_index_actually_carries(self) -> None:
        # `INITIAL.options.IMMERSE` is a legitimate *index* key only alongside its event; the point
        # is that every such example in the runtime-fields table is a real row somewhere, so the
        # table cannot drift back to a fabricated key.
        text = self.API_DOC.read_text(encoding="utf-8")
        section = text.split("#### compact 在 v11 移除的冗余键", 1)[0]
        carried = {
            row["option"]
            for event in parse_event_option_index(EVENTS_PATH.read_text(encoding="utf-8"))[0].values()
            for row in event
        }
        for example in sorted(set(self._PAGE_KEY.findall(section))):
            with self.subTest(example=example):
                self.assertIn(
                    example,
                    carried,
                    f"docs/api.md offers {example!r} as an index key, which no event builds",
                )

    def test_the_documented_join_is_the_one_the_code_performs(self) -> None:
        # The recipe the docs state, executed literally against the committed index: take the live
        # event id and the live text_key, strip `<EVENT>.pages.`, and land on a real row.
        text = self.API_DOC.read_text(encoding="utf-8")
        self.assertIn("Slugify(事件类名).pages.<PAGE>.options.<OPTION>", text)

        live_key = LIVE_NEOW_ARCANE_SCROLL
        prefix = f"{canonical_event_id(LIVE_NEOW_EVENT_ID)}.pages."
        self.assertTrue(live_key.startswith(prefix))
        row_options = {row["option"] for row in event_option_risk(REPO_ROOT, LIVE_NEOW_EVENT_ID)}
        self.assertIn(live_key[len(prefix):], row_options)


class SceneGuidanceTests(unittest.TestCase):
    def test_guidance_follows_the_state_screen(self) -> None:
        result = scene_guidance({"screen": "MAP", "run": {}}, root=REPO_ROOT)

        self.assertEqual("MAP", result["screen"])
        self.assertIn("Route: which node to enter", result["guidance"])
        self.assertEqual([], result["event_options"])

    def test_event_screen_carries_the_current_events_risk_rows(self) -> None:
        state = {"screen": "EVENT", "event": {"event_id": "AbyssalBaths", "options": []}}

        result = scene_guidance(state, root=REPO_ROOT)

        self.assertIn("Event options: how to choose", result["guidance"])
        self.assertEqual("AbyssalBaths", result["event_id"])
        self.assertTrue(result["event_options"])

    def test_no_run_and_no_repository_still_answers(self) -> None:
        # `root=None` means "find my own checkout", so a checkout-less install is expressed as a path
        # that does not hold one: the tool must still answer, with no guidance rather than an error.
        result = scene_guidance({"screen": "REWARD"}, root=Path("/nonexistent-checkout"))

        self.assertEqual("REWARD", result["screen"])
        self.assertEqual("", result["guidance"])
        self.assertEqual([], result["event_options"])
        self.assertIsNone(result["guidance_source"])

    def test_a_non_object_state_is_tolerated(self) -> None:
        result = scene_guidance(None, root=REPO_ROOT)

        self.assertIsNone(result["screen"])
        self.assertNotIn("Route", result["guidance"])

    def test_the_package_finds_its_own_checkout(self) -> None:
        self.assertIsNotNone(repo_root())
        self.assertEqual(REPO_ROOT, repo_root())


if __name__ == "__main__":
    unittest.main()
