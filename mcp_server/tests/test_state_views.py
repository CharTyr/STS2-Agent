from __future__ import annotations

import unittest

from sts2_mcp.state_views import MAX_DIFF_ENTRIES, diff_state, run_summary


def state_with_run(**overrides: object) -> dict:
    run = {
        "character_id": "SILENT",
        "character_name": "Silent",
        "floor": 7,
        "act_id": "1",
        "boss_id": "HEXAGHOST",
        "ascension": 3,
        "current_hp": 41,
        "max_hp": 70,
        "gold": 212,
        "max_energy": 3,
        "deck": [{"card_id": "STRIKE"}, {"card_id": "DEFEND"}],
        "relics": [{"relic_id": "BURNING_BLOOD"}],
        "potions": [{"occupied": True, "potion_id": "FIRE_POTION"}, {"occupied": False}],
        "players": [
            {
                "player_id": "p1",
                "is_local": True,
                "is_alive": True,
                "current_hp": 41,
                "max_hp": 70,
                "gold": 212,
            }
        ],
    }
    run.update(overrides)
    return {"screen": "COMBAT", "run": run}


class RunSummaryTests(unittest.TestCase):
    def test_summary_reads_the_documented_run_fields(self) -> None:
        summary = run_summary(state_with_run())

        assert summary is not None
        self.assertEqual(summary["character_id"], "SILENT")
        self.assertEqual(summary["floor"], 7)
        self.assertEqual(summary["boss_id"], "HEXAGHOST")
        self.assertEqual(summary["current_hp"], 41)
        self.assertEqual(summary["gold"], 212)

    def test_counts_are_counts_of_what_the_payload_holds(self) -> None:
        summary = run_summary(state_with_run())

        assert summary is not None
        self.assertEqual(summary["deck_size"], 2)
        self.assertEqual(summary["relic_count"], 1)
        # Two slots, one of them filled: the two counts are different facts.
        self.assertEqual(summary["potion_count"], 2)
        self.assertEqual(summary["potions_occupied"], 1)

    def test_party_block_carries_the_coop_fields(self) -> None:
        summary = run_summary(state_with_run())

        assert summary is not None
        self.assertEqual(len(summary["party"]), 1)
        self.assertEqual(summary["party"][0]["player_id"], "p1")
        self.assertTrue(summary["party"][0]["is_local"])

    def test_no_run_answers_none_rather_than_an_empty_summary(self) -> None:
        self.assertIsNone(run_summary({"screen": "MAIN_MENU"}))
        self.assertIsNone(run_summary({"run": None}))
        self.assertIsNone(run_summary(None))
        self.assertIsNone(run_summary("not a payload"))

    def test_missing_fields_stay_none_instead_of_being_invented(self) -> None:
        summary = run_summary({"run": {"floor": 2}})

        assert summary is not None
        self.assertEqual(summary["floor"], 2)
        self.assertIsNone(summary["gold"])
        self.assertEqual(summary["deck_size"], 0)


class DiffStateTests(unittest.TestCase):
    def test_reports_only_the_paths_that_changed(self) -> None:
        before = {"run": {"current_hp": 41, "gold": 212}, "screen": "COMBAT"}
        after = {"run": {"current_hp": 38, "gold": 212}, "screen": "COMBAT"}

        result = diff_state(before, after)

        self.assertEqual(result["change_count"], 1)
        self.assertFalse(result["truncated"])
        self.assertEqual(
            result["changes"],
            [{"path": "run.current_hp", "before": 41, "after": 38}],
        )

    def test_a_path_on_one_side_only_reports_null_for_the_other(self) -> None:
        result = diff_state({"a": 1}, {"b": 2})

        self.assertIn({"path": "a", "before": 1, "after": None}, result["changes"])
        self.assertIn({"path": "b", "before": None, "after": 2}, result["changes"])

    def test_identical_payloads_report_no_changes_and_are_not_truncated(self) -> None:
        payload = {"run": {"deck": [{"card_id": "STRIKE"}]}}

        result = diff_state(payload, payload)

        self.assertEqual(result["changes"], [])
        self.assertEqual(result["change_count"], 0)
        self.assertFalse(result["truncated"])

    def test_a_type_change_is_a_change(self) -> None:
        # JSON "12" and 12 are different facts, and in Python True == 1.
        result = diff_state({"gold": 12}, {"gold": "12"})
        self.assertEqual(result["change_count"], 1)

        result = diff_state({"flag": True}, {"flag": 1})
        self.assertEqual(result["change_count"], 1)

    def test_list_length_is_its_own_change_and_indexes_are_compared(self) -> None:
        before = {"combat": {"hand": [{"card_id": "STRIKE"}]}}
        after = {"combat": {"hand": [{"card_id": "DEFEND"}, {"card_id": "STRIKE"}]}}

        result = diff_state(before, after)
        paths = [change["path"] for change in result["changes"]]

        self.assertIn("combat.hand[]", paths)
        self.assertIn("combat.hand[0].card_id", paths)

    def test_the_cap_is_reported_so_a_partial_diff_never_reads_as_empty(self) -> None:
        before = {f"k{index}": 0 for index in range(MAX_DIFF_ENTRIES + 20)}
        after = {f"k{index}": 1 for index in range(MAX_DIFF_ENTRIES + 20)}

        result = diff_state(before, after, limit=10)

        self.assertEqual(result["change_count"], 10)
        self.assertEqual(result["limit"], 10)
        self.assertTrue(result["truncated"])

    def test_an_empty_object_is_a_leaf_not_a_silent_no_op(self) -> None:
        result = diff_state({"shop": {}}, {"shop": {"open": True}})

        paths = [change["path"] for change in result["changes"]]
        self.assertIn("shop", paths)


if __name__ == "__main__":
    unittest.main()
