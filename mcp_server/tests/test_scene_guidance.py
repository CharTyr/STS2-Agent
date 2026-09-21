from __future__ import annotations

import unittest
from pathlib import Path

from sts2_mcp.scene_guidance import (
    MAX_GUIDANCE_CHARACTERS,
    NOT_INJECTED_HEADINGS,
    SCREEN_HEADINGS,
    cap,
    event_option_risk,
    headings,
    repo_root,
    scene_guidance,
    sections,
    strategy_for_screen,
)

REPO_ROOT = Path(__file__).resolve().parents[2]
STRATEGY_PATH = REPO_ROOT / "skills" / "sts2-mcp-player" / "references" / "strategy.md"


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
