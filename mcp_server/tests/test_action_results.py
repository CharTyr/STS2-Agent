"""The action-result contract: a compact state by default, and a correctable index error.

`act` used to hand back the whole raw `/state` payload (~4,000-9,500 tokens) on every step while the
native MCP `act` already answered with the compact `agent_view`, and a rejected index used to be a
bare message with no way to see what would have worked. Both are contract facts a client reads, so
both are pinned here: the default shape, the `raw_state` escape hatch, and the structured details of
an index rejection.
"""

from __future__ import annotations

import asyncio
import unittest
from typing import Any
from unittest.mock import patch

from sts2_mcp.action_results import compact_action_result, index_error_details, with_index_details
from sts2_mcp.client import Sts2ApiError, Sts2Client
from sts2_mcp.server import create_server


AGENT_VIEW = {
    "version": 11,
    "screen": "COMBAT",
    "available_actions": ["play_card", "end_turn"],
    "combat": {"hand": [{"i": 0, "card_id": "STRIKE"}]},
}

RAW_STATE = {
    "state_version": 16,
    "screen": "COMBAT",
    "available_actions": ["play_card", "end_turn"],
    "combat": {"hand": [{"i": 0, "card_id": "STRIKE", "line": "Deal 6 damage."}]},
    "agent_view": AGENT_VIEW,
}


def action_response(**overrides: Any) -> dict[str, Any]:
    response = {
        "action": "play_card",
        "status": "completed",
        "stable": True,
        "message": "Action completed.",
        "state": RAW_STATE,
    }
    response.update(overrides)
    return response


class CompactActionResultTests(unittest.TestCase):
    def test_state_becomes_the_agent_view_and_every_other_key_survives(self) -> None:
        result = compact_action_result(action_response())

        self.assertIs(result["state"], AGENT_VIEW)
        for key in ("action", "status", "stable", "message"):
            self.assertEqual(result[key], action_response()[key])

    def test_a_payload_without_agent_view_keeps_the_raw_state(self) -> None:
        raw_only = {"screen": "COMBAT", "available_actions": []}

        result = compact_action_result(action_response(state=raw_only))

        self.assertIs(result["state"], raw_only)

    def test_a_result_without_state_is_untouched(self) -> None:
        # The outcome-unknown path: there is no post-action snapshot to compact, and the response
        # must not gain a state key it never had.
        unknown = {"action": "end_turn", "status": "outcome_unknown", "reconciliation": {"state": {}}}

        self.assertIs(compact_action_result(unknown), unknown)


class ActToolStateTests(unittest.TestCase):
    """The guided tool end to end: the tool forwards, the client owns the response contract."""

    def _tool(self) -> tuple[Any, Sts2Client]:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        server = create_server(client=client, tool_profile="guided")
        return asyncio.run(server.get_tool("act")), client

    def test_act_returns_the_compact_state(self) -> None:
        tool, client = self._tool()

        with patch.object(client, "_request", return_value=action_response()):
            result = tool.fn(action="play_card", card_index=0)

        self.assertEqual(result["state"], AGENT_VIEW)
        self.assertEqual(result["status"], "completed")
        self.assertTrue(result["stable"])
        self.assertEqual(result["action"], "play_card")

    def test_act_raw_state_returns_the_full_payload(self) -> None:
        tool, client = self._tool()

        with patch.object(client, "_request", return_value=action_response()) as request_mock:
            result = tool.fn(action="play_card", card_index=0, raw_state=True)

        self.assertEqual(result["state"], RAW_STATE)
        # The flag is a tool-level choice, not a field the mod has to understand.
        self.assertNotIn("raw_state", request_mock.call_args.kwargs["payload"])

    def test_act_schema_documents_raw_state(self) -> None:
        tool, _ = self._tool()

        properties = tool.parameters["properties"]
        self.assertIn("raw_state", properties)

        description = tool.fn.__doc__ or ""
        self.assertIn("raw_state=True", description)
        self.assertIn("agent_view", description)
        self.assertIn("valid_indices", description)


class ClientCompactStateTests(unittest.TestCase):
    def test_execute_action_compacts_the_state_by_default(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")

        with patch.object(client, "_request", return_value=action_response()):
            result = client.execute_action("play_card", card_index=0)

        self.assertEqual(result["state"], AGENT_VIEW)

    def test_execute_action_raw_state_returns_the_full_payload(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")

        with patch.object(client, "_request", return_value=action_response()):
            result = client.execute_action("play_card", card_index=0, raw_state=True)

        self.assertEqual(result["state"], RAW_STATE)

    def test_raw_state_is_not_a_request_field(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")

        with patch.object(client, "_request", return_value={"ok": True}) as request_mock:
            client.execute_action("end_turn", raw_state=True)

        payload = request_mock.call_args.kwargs["payload"]
        self.assertNotIn("raw_state", payload)


class IndexErrorDetailsTests(unittest.TestCase):
    def _error(self, message: str, details: dict[str, Any], code: str = "invalid_target") -> Sts2ApiError:
        return Sts2ApiError(
            status_code=409,
            code=code,
            message=message,
            details=details,
            retryable=False,
        )

    def test_card_index_names_the_field_and_the_indices_the_count_implies(self) -> None:
        error = self._error(
            "card_index is out of range.",
            {"action": "play_card", "card_index": 9, "hand_count": 5},
        )

        details = index_error_details(error)

        self.assertEqual(details["action"], "play_card")
        self.assertEqual(details["field"], "card_index")
        self.assertEqual(details["submitted"], 9)
        self.assertEqual(details["valid_indices"], [0, 1, 2, 3, 4])
        self.assertEqual(details["valid_field"], "combat.hand")

    def test_option_index_reads_its_payload_path_from_the_action(self) -> None:
        error = self._error(
            "option_index is out of range.",
            {"action": "choose_rest_option", "option_index": 4, "option_count": 3},
        )

        details = index_error_details(error)

        self.assertEqual(details["field"], "option_index")
        self.assertEqual(details["valid_indices"], [0, 1, 2])
        self.assertEqual(details["valid_field"], "rest.options")

    def test_a_target_message_already_names_the_space_it_read(self) -> None:
        error = self._error(
            "target_index is out of range for combat.enemies[].",
            {"action": "play_card", "target_index": 3, "card_index": 0},
        )

        details = index_error_details(error)

        self.assertEqual(details["field"], "target_index")
        self.assertEqual(details["valid_field"], "combat.enemies[]")

    def test_an_unknown_count_omits_valid_indices_rather_than_claiming_none(self) -> None:
        error = self._error("card_index is out of range.", {"action": "play_card", "card_index": 9})

        details = index_error_details(error)

        self.assertNotIn("valid_indices", details)

    def test_available_actions_are_included_only_when_the_caller_has_them(self) -> None:
        error = self._error(
            "card_index is out of range.",
            {"action": "play_card", "card_index": 9, "hand_count": 2},
        )

        without = index_error_details(error)
        with_actions = index_error_details(error, available_actions=["end_turn"])

        self.assertNotIn("available_actions", without)
        self.assertEqual(with_actions["available_actions"], ["end_turn"])

    def test_a_non_index_failure_is_not_claimed_as_one(self) -> None:
        error = self._error(
            "Local player is unavailable.",
            {"action": "play_card", "screen": "COMBAT"},
            code="state_unavailable",
        )

        self.assertIsNone(index_error_details(error))

    def test_the_mods_own_details_survive_and_the_message_is_unchanged(self) -> None:
        error = self._error(
            "card_index is out of range.",
            {"action": "play_card", "card_index": 9, "hand_count": 5},
        )

        enriched = with_index_details(error)

        self.assertIsNot(enriched, error)
        self.assertEqual(enriched.message, error.message)
        self.assertEqual(enriched.code, "invalid_target")
        self.assertEqual(enriched.status_code, 409)
        self.assertFalse(enriched.retryable)
        self.assertEqual(enriched.details["hand_count"], 5)
        self.assertEqual(enriched.details["valid_indices"], [0, 1, 2, 3, 4])
        # The reader sees the structure in the same string the mod's sentence starts.
        self.assertTrue(str(enriched).startswith("invalid_target: card_index is out of range."))

    def test_an_unrelated_failure_is_returned_untouched(self) -> None:
        error = self._error("Local player is unavailable.", {"action": "play_card"}, code="state_unavailable")

        self.assertIs(with_index_details(error), error)

    def test_the_client_enriches_a_rejected_action_index(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        error = self._error(
            "card_index is out of range.",
            {"action": "play_card", "card_index": 9, "hand_count": 5},
        )

        with patch.object(client, "_request", side_effect=error):
            with self.assertRaises(Sts2ApiError) as caught:
                client.execute_action("play_card", card_index=9)

        details = caught.exception.details
        self.assertEqual(details["field"], "card_index")
        self.assertEqual(details["submitted"], 9)
        self.assertEqual(details["valid_indices"], [0, 1, 2, 3, 4])
        self.assertEqual(details["valid_field"], "combat.hand")


if __name__ == "__main__":
    unittest.main()
