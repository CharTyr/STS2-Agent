"""Opt-in plans execute through the existing action seam; ambiguous results never replay."""
import unittest
from copy import deepcopy
from typing import Any

from sts2_mcp.play_tools import execute_plan_act


class Client:
    def __init__(self, status: str = "completed") -> None:
        self.status = status
        self.calls: list[tuple[str, dict[str, Any]]] = []
        self.state: dict[str, Any] = {
            "screen": "COMBAT", "turn": 1, "available_actions": ["play_card", "end_turn"],
            "combat": {"player": {"hp": "40/80"}, "hand": [
                {"i": 0, "cid": 17, "playable": True, "targets": []}], "enemies": []}}

    def get_state(self) -> dict[str, Any]:
        return deepcopy(self.state)

    def execute_action(self, action: str, **kwargs: Any) -> dict[str, Any]:
        self.calls.append((action, kwargs))
        return {"status": self.status, "stable": self.status == "completed", "state": self.get_state()}


class PlayPlanTests(unittest.TestCase):
    def test_completed_pending_and_unknown(self) -> None:
        for status, count, stop in [("completed", 2, "completed"), ("pending", 1, "unsettled"),
                                     ("outcome_unknown", 1, "outcome_unknown")]:
            with self.subTest(status=status):
                client = Client(status)
                result = execute_plan_act(client, "guided", "play_card", card_cid=17, then=[{"action": "end_turn"}])
                self.assertEqual(len(client.calls), count)
                self.assertEqual(client.calls[0][1]["card_index"], 0)
                self.assertEqual(result["status"], status)
                self.assertEqual(result["plan"]["stop_reason"], stop)

    def test_debug_action_in_followup_is_not_dispatched(self) -> None:
        client = Client()
        result = execute_plan_act(client, "guided", "end_turn", then=[{"action": "run_console_command"}])
        self.assertEqual(len(client.calls), 1)
        self.assertEqual(result["plan"]["stop_reason"], "illegal_action")

    def test_missing_cid_never_dispatches(self) -> None:
        client = Client()
        result = execute_plan_act(client, "guided", "play_card", card_cid=99)
        self.assertIn("error", result)
        self.assertEqual(client.calls, [])

    def test_plain_act_retains_old_shape(self) -> None:
        client = Client()
        result = execute_plan_act(client, "guided", "end_turn", reason="done")
        self.assertNotIn("plan", result)
        self.assertEqual(client.calls[0][1]["client_context"]["decision_reason"], "done")
