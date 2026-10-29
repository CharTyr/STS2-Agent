"""Opt-in plans execute through the existing action seam; ambiguous results never replay."""
import unittest
from copy import deepcopy
from typing import Any

from sts2_mcp.client import Sts2ApiError
from sts2_mcp.play_tools import execute_plan_act


class Client:
    def __init__(self, status: str = "completed", errors: dict[int, BaseException] | None = None) -> None:
        self.status = status
        # Call number -> exception, so a refusal can be scripted for the primary act or a follow-up.
        self.errors = errors or {}
        self.calls: list[tuple[str, dict[str, Any]]] = []
        self.state: dict[str, Any] = {
            "screen": "COMBAT", "turn": 1, "available_actions": ["play_card", "end_turn"],
            "combat": {"player": {"hp": "40/80"}, "hand": [
                {"i": 0, "cid": 17, "playable": True, "targets": []}], "enemies": []}}

    def get_state(self) -> dict[str, Any]:
        return deepcopy(self.state)

    def execute_action(self, action: str, **kwargs: Any) -> dict[str, Any]:
        self.calls.append((action, kwargs))
        if len(self.calls) in self.errors:
            raise self.errors[len(self.calls)]
        return {"status": self.status, "stable": self.status == "completed", "state": self.get_state()}


def stale_rejection() -> Sts2ApiError:
    return Sts2ApiError(
        status_code=409,
        code="invalid_target",
        message="The card at card_index is no longer the card named by card_cid; read the state again.",
        details={"action": "play_card", "card_index": 0, "card_cid": 17, "actual_card_cid": 18, "stale": True},
    )


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

    def test_identity_travels_with_the_http_action(self) -> None:
        """The cid is forwarded, so the mod can verify it against its own live hand.

        Resolving the position here is only what the wire still needs (the request carries both);
        the identity is what makes the request self-checking under the action lease.
        """
        client = Client()
        execute_plan_act(client, "guided", "play_card", card_cid=17,
                         then=[{"action": "play_card", "card_cid": 17}, {"action": "end_turn"}])

        self.assertEqual([call[0] for call in client.calls], ["play_card", "play_card", "end_turn"])
        self.assertEqual(client.calls[0][1]["card_cid"], 17)
        self.assertEqual(client.calls[0][1]["card_index"], 0)
        # A follow-up play_card carries its identity too; a non-card step carries none.
        self.assertEqual(client.calls[1][1]["card_cid"], 17)
        self.assertEqual(client.calls[1][1]["card_index"], 0)
        self.assertIsNone(client.calls[2][1]["card_cid"])

    def test_followup_waits_for_the_previous_card_to_resolve(self) -> None:
        """Live regression (2026-10-29): a frame read mid-animation has its combat gate closed."""

        class Animating(Client):
            def __init__(self) -> None:
                super().__init__()
                self.closed_reads = 0

            def execute_action(self, action: str, **kwargs: Any) -> dict[str, Any]:
                self.closed_reads = 2  # the next two reads catch the card still resolving
                return super().execute_action(action, **kwargs)

            def get_state(self) -> dict[str, Any]:
                state = super().get_state()
                if self.closed_reads > 0:
                    self.closed_reads -= 1
                    state["available_actions"] = []
                    state["combat"]["action_readiness"] = {"can_use_combat_actions": False}
                return state

        client = Animating()
        result = execute_plan_act(client, "guided", "play_card", card_cid=17,
                                  then=[{"action": "play_card", "card_cid": 17}, {"action": "end_turn"}])
        self.assertEqual(result["plan"]["stop_reason"], "completed")
        self.assertEqual([call[0] for call in client.calls], ["play_card", "play_card", "end_turn"])

    def test_non_play_card_never_sends_a_card_identity(self) -> None:
        client = Client()
        execute_plan_act(client, "guided", "end_turn")
        self.assertIsNone(client.calls[0][1]["card_cid"])

    def test_stale_primary_identity_is_rejected_without_a_retry(self) -> None:
        client = Client(errors={1: stale_rejection()})
        with self.assertRaises(Sts2ApiError) as raised:
            execute_plan_act(client, "guided", "play_card", card_cid=17, then=[{"action": "end_turn"}])

        self.assertEqual(raised.exception.code, "invalid_target")
        # Nothing was replayed and no follow-up ran: the refusal came before any mutation.
        self.assertEqual(len(client.calls), 1)

    def test_stale_followup_stops_as_rejected_not_unknown(self) -> None:
        client = Client(errors={2: stale_rejection()})
        result = execute_plan_act(client, "guided", "play_card", card_cid=17,
                                  then=[{"action": "play_card", "card_cid": 17}, {"action": "end_turn"}])

        # The mod answered 409, so the step was refused rather than lost; calling that
        # "outcome_unknown" would tell the caller an action may have landed that never did.
        self.assertEqual(result["plan"]["stop_reason"], "rejected")
        self.assertEqual(result["plan"]["executed"], 2)
        self.assertEqual(result["plan"]["steps"][-1]["error"]["code"], "invalid_target")
        self.assertEqual(len(client.calls), 2)

    def test_transport_loss_after_dispatch_stays_outcome_unknown(self) -> None:
        client = Client(errors={2: TimeoutError("response lost")})
        result = execute_plan_act(client, "guided", "play_card", card_cid=17,
                                  then=[{"action": "play_card", "card_cid": 17}, {"action": "end_turn"}])

        self.assertEqual(result["plan"]["stop_reason"], "outcome_unknown")
        self.assertEqual(len(client.calls), 2)

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
