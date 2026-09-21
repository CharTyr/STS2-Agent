"""Contracts for the typed payload models and the client getters that expose them.

Two payloads have a fixed, mod-declared field set -- `/actions/available` and `/decisions` -- and
these tests pin the parser policy that makes typing them worth anything: a wrong type is an error
rather than a coerced value, an absent optional field is a documented default rather than
corruption, and an unknown field survives a round trip so a newer mod is not silently truncated.

`test_client_payloads` also proves the transport seam: a malformed body arrives at a caller as the
mod's own `invalid_response` error shape, non-retryable, with the offending path attached.
"""

from __future__ import annotations

import json
import unittest
from typing import Any
from unittest.mock import patch

from sts2_mcp.client import Sts2ApiError, Sts2Client
from sts2_mcp.payloads import (
    ActionDescriptor,
    AvailableActions,
    DecisionLogEntry,
    PayloadSchemaError,
    parse_action_descriptor,
    parse_available_actions,
    parse_decision_log,
    parse_decision_log_entry,
)


class JsonResponse:
    """The same minimal transport double `test_action_replay_safety` uses."""

    def __init__(self, payload: Any) -> None:
        self._body = json.dumps(payload).encode("utf-8")

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb) -> bool:
        return False

    def read(self) -> bytes:
        return self._body


class AvailableActionsTests(unittest.TestCase):
    def test_full_descriptor_keeps_its_flags(self) -> None:
        payload = {
            "screen": "COMBAT",
            "actions": [
                {
                    "name": "play_card",
                    "requires_target": True,
                    "requires_index": True,
                    "requires_coordinates": False,
                    "requires_tool": False,
                }
            ],
        }

        catalog = parse_available_actions(payload)

        self.assertEqual(catalog.screen, "COMBAT")
        self.assertEqual(catalog.names(), ("play_card",))
        descriptor = catalog.find("play_card")
        assert descriptor is not None
        self.assertTrue(descriptor.requires_target)
        self.assertTrue(descriptor.requires_index)
        self.assertFalse(descriptor.requires_coordinates)

    def test_absent_flags_default_to_false_for_older_builds(self) -> None:
        # An older mod not sending a flag is not corruption; the caller gets the documented default.
        descriptor = parse_action_descriptor({"name": "end_turn"})

        self.assertEqual(descriptor, ActionDescriptor(name="end_turn"))
        self.assertFalse(descriptor.requires_target)

    def test_a_mistyped_flag_is_refused_rather_than_truthy_coerced(self) -> None:
        with self.assertRaises(PayloadSchemaError) as caught:
            parse_action_descriptor({"name": "play_card", "requires_target": "yes"})

        self.assertEqual(caught.exception.path, "actions[].requires_target")
        self.assertEqual(caught.exception.expected, "a boolean")

    def test_a_missing_or_blank_name_is_refused(self) -> None:
        for payload in ({}, {"name": ""}, {"name": 7}, {"name": "   "}):
            with self.subTest(payload=payload):
                with self.assertRaises(PayloadSchemaError) as caught:
                    parse_action_descriptor(payload)
                self.assertEqual(caught.exception.path, "actions[].name")

    def test_a_non_list_actions_value_is_an_error_not_a_character_list(self) -> None:
        # The old `list(payload.get("actions", []))` turned this into five one-character actions.
        with self.assertRaises(PayloadSchemaError) as caught:
            parse_available_actions({"screen": "MAP", "actions": "nope"})

        self.assertEqual(caught.exception.path, "available_actions.actions")

    def test_a_scalar_action_entry_is_refused_with_its_index(self) -> None:
        with self.assertRaises(PayloadSchemaError) as caught:
            parse_available_actions({"actions": [{"name": "end_turn"}, "play_card"]})

        self.assertEqual(caught.exception.path, "available_actions.actions[1]")

    def test_a_thin_response_without_screen_still_parses(self) -> None:
        catalog = parse_available_actions({"actions": []})

        self.assertIsNone(catalog.screen)
        self.assertEqual(catalog.actions, ())
        self.assertEqual(catalog.to_wire(), {"actions": []})

    def test_unknown_fields_survive_a_round_trip(self) -> None:
        # Extensions are preserved; the four flags are canonicalised to the shape the current mod
        # always sends, so a client paired with a newer or older build still reads one descriptor
        # form. That normalisation is the point of typing the payload, not a loss.
        payload = {
            "screen": "COMBAT",
            "actions": [{"name": "end_turn", "requires_target": False, "future_hint": "x"}],
            "future_top_level": 3,
        }

        wire = parse_available_actions(payload).to_wire()

        self.assertEqual(wire["future_top_level"], 3)
        self.assertEqual(wire["screen"], "COMBAT")
        self.assertEqual(
            wire["actions"],
            [
                {
                    "name": "end_turn",
                    "requires_target": False,
                    "requires_index": False,
                    "requires_coordinates": False,
                    "requires_tool": False,
                    "future_hint": "x",
                }
            ],
        )

    def test_parsing_does_not_mutate_the_input(self) -> None:
        payload = {"actions": [{"name": "end_turn"}]}
        before = json.dumps(payload, sort_keys=True)

        parse_available_actions(payload)

        self.assertEqual(json.dumps(payload, sort_keys=True), before)


class DecisionLogEntryTests(unittest.TestCase):
    ENTRY = {
        "id": 12,
        "timestamp": "2026-09-20T15:36:22.0000000+00:00",
        "source": "agent_loop",
        "action": "play_card",
        "reason": "Strike first.",
        "state_fingerprint": "abc123",
        "requests_spent": 4,
        "total_tokens": 900,
        "run_id": "run_alpha",
    }

    def test_a_full_entry_keeps_every_field(self) -> None:
        entry = parse_decision_log_entry(self.ENTRY)

        self.assertEqual(entry.id, 12)
        self.assertEqual(entry.source, "agent_loop")
        self.assertEqual(entry.action, "play_card")
        self.assertEqual(entry.requests_spent, 4)
        self.assertEqual(entry.total_tokens, 900)
        self.assertEqual(entry.to_wire(), self.ENTRY)

    def test_absent_optional_fields_stay_absent_on_the_wire(self) -> None:
        # The mod omits null fields (WhenWritingNull), so a round trip must not invent them.
        entry = parse_decision_log_entry(
            {
                "id": 1,
                "timestamp": "t",
                "source": "http_api",
                "action": "end_turn",
                "requests_spent": 0,
            }
        )

        self.assertIsNone(entry.reason)
        self.assertIsNone(entry.total_tokens)
        self.assertEqual(
            entry.to_wire(),
            {"id": 1, "timestamp": "t", "source": "http_api", "action": "end_turn", "requests_spent": 0},
        )

    def test_unknown_usage_is_not_flattened_into_zero(self) -> None:
        entry = parse_decision_log_entry(
            {**self.ENTRY, "total_tokens": None, "run_id": None},
        )

        self.assertIsNone(entry.total_tokens)
        self.assertIsNone(entry.run_id)

    def test_mistyped_core_fields_are_refused_with_their_path(self) -> None:
        cases = {
            "id": "12",
            "timestamp": 5,
            "source": None,
            "action": "",
            "requests_spent": "4",
            "total_tokens": "900",
            "run_id": 7,
        }
        for field_name, bad_value in cases.items():
            with self.subTest(field=field_name):
                with self.assertRaises(PayloadSchemaError) as caught:
                    parse_decision_log_entry({**self.ENTRY, field_name: bad_value})
                self.assertEqual(caught.exception.path, f"decisions[].{field_name}")

    def test_a_boolean_is_not_accepted_where_a_count_belongs(self) -> None:
        with self.assertRaises(PayloadSchemaError) as caught:
            parse_decision_log_entry({**self.ENTRY, "requests_spent": True})

        self.assertEqual(caught.exception.path, "decisions[].requests_spent")

    def test_future_fields_survive(self) -> None:
        entry = parse_decision_log_entry({**self.ENTRY, "plan_id": "p1"})

        self.assertEqual(entry.to_wire()["plan_id"], "p1")

    def test_a_missing_or_non_list_log_is_refused(self) -> None:
        for payload in (None, {"entries": []}, "nope"):
            with self.subTest(payload=payload):
                with self.assertRaises(PayloadSchemaError) as caught:
                    parse_decision_log(payload)
                self.assertEqual(caught.exception.path, "decisions")

    def test_a_list_is_parsed_in_order_and_can_be_empty(self) -> None:
        entries = parse_decision_log([self.ENTRY, {**self.ENTRY, "id": 13}])

        self.assertEqual([entry.id for entry in entries], [12, 13])
        self.assertEqual(parse_decision_log([]), ())


class TypedClientGetterTests(unittest.TestCase):
    """The typed getters cross the same transport seam as the existing dict-returning ones."""

    def _client_with(self, payload: Any) -> Sts2Client:
        client = Sts2Client(base_url="http://127.0.0.1:8080", max_retries=0)
        self.addCleanup(patch.stopall)
        patcher = patch("sts2_mcp.client.request.urlopen", return_value=JsonResponse(payload))
        patcher.start()
        return client

    def test_get_decision_snapshot_decodes_the_envelope_from_its_own_path(self) -> None:
        """One path, one read: the whole point of the route is that the halves share a frame."""
        client = Sts2Client(base_url="http://127.0.0.1:8080", max_retries=0)
        self.addCleanup(patch.stopall)
        opener = patch(
            "sts2_mcp.client.request.urlopen",
            return_value=JsonResponse(
                {
                    "ok": True,
                    "data": {
                        "state": {"screen": "COMBAT", "available_actions": ["end_turn"]},
                        "available_actions": [
                            {"name": "end_turn", "requires_index": False, "requires_target": False}
                        ],
                    },
                }
            ),
        )
        mocked = opener.start()

        snapshot = client.get_decision_snapshot()

        self.assertEqual(snapshot["state"]["screen"], "COMBAT")
        self.assertEqual([item["name"] for item in snapshot["available_actions"]], ["end_turn"])
        self.assertEqual(len(mocked.call_args_list), 1)
        self.assertEqual(
            mocked.call_args[0][0].full_url,
            "http://127.0.0.1:8080/decision-snapshot",
        )

    def test_a_404_from_the_snapshot_route_arrives_as_the_not_found_the_fallback_keys_on(self) -> None:
        """An older mod answers 404 `not_found`; any other failure must not look like that pair."""
        from urllib import error as urlerror

        client = Sts2Client(base_url="http://127.0.0.1:8080", max_retries=0)
        body = json.dumps(
            {"ok": False, "error": {"code": "not_found", "message": "Route not found.", "retryable": False}}
        ).encode("utf-8")
        http_error = urlerror.HTTPError(
            client.base_url + "/decision-snapshot", 404, "Not Found", None, None
        )
        http_error.read = lambda *args, **kwargs: body  # type: ignore[method-assign]

        with patch("sts2_mcp.client.request.urlopen", side_effect=http_error):
            with self.assertRaises(Sts2ApiError) as caught:
                client.get_decision_snapshot()

        self.assertEqual(caught.exception.status_code, 404)
        self.assertEqual(caught.exception.code, "not_found")

    def test_get_action_catalog_reads_a_well_formed_catalog(self) -> None:
        client = self._client_with(
            {
                "ok": True,
                "data": {"screen": "REST", "actions": [{"name": "choose_rest_option", "requires_index": True}]},
            }
        )

        catalog = client.get_action_catalog()

        self.assertIsInstance(catalog, AvailableActions)
        self.assertEqual(catalog.screen, "REST")
        descriptor = catalog.find("choose_rest_option")
        assert descriptor is not None
        self.assertTrue(descriptor.requires_index)

    def test_get_action_catalog_maps_malformed_data_to_a_non_retryable_invalid_response(self) -> None:
        client = self._client_with({"ok": True, "data": {"actions": [{"name": "end_turn", "requires_index": 1}]}})

        with self.assertRaises(Sts2ApiError) as caught:
            client.get_action_catalog()

        self.assertEqual(caught.exception.code, "invalid_response")
        self.assertEqual(caught.exception.status_code, 200)
        self.assertFalse(caught.exception.retryable)
        self.assertEqual(caught.exception.details["path"], "available_actions.actions[0].requires_index")

    def test_get_decision_entries_reads_a_well_formed_log(self) -> None:
        client = self._client_with(
            {
                "ok": True,
                "data": [
                    {
                        "id": 1,
                        "timestamp": "t",
                        "source": "native_mcp",
                        "action": "end_turn",
                        "requests_spent": 1,
                    }
                ],
            }
        )

        entries = client.get_decision_entries(limit=5)

        self.assertEqual(len(entries), 1)
        self.assertIsInstance(entries[0], DecisionLogEntry)

    def test_get_decision_entries_refuses_a_null_log_instead_of_reporting_an_empty_one(self) -> None:
        client = self._client_with({"ok": True, "data": None})

        with self.assertRaises(Sts2ApiError) as caught:
            client.get_decision_entries()

        self.assertEqual(caught.exception.code, "invalid_response")
        self.assertEqual(caught.exception.details["path"], "decisions")

    def test_an_error_envelope_still_reaches_the_caller_as_the_mod_error(self) -> None:
        from urllib import error as urlerror

        client = Sts2Client(base_url="http://127.0.0.1:8080", max_retries=0)
        body = json.dumps(
            {"ok": False, "error": {"code": "not_found", "message": "Route not found.", "retryable": False}}
        ).encode("utf-8")
        http_error = urlerror.HTTPError(client.base_url + "/actions/available", 404, "Not Found", None, None)
        http_error.read = lambda *args, **kwargs: body  # type: ignore[method-assign]

        with patch("sts2_mcp.client.request.urlopen", side_effect=http_error):
            with self.assertRaises(Sts2ApiError) as caught:
                client.get_action_catalog()

        self.assertEqual(caught.exception.code, "not_found")
        self.assertEqual(caught.exception.status_code, 404)

    def test_the_legacy_dict_getters_are_untouched(self) -> None:
        client = self._client_with({"ok": True, "data": {"screen": "MAP", "actions": [{"name": "choose_map_node"}]}})

        # Same response, old shape: the compatibility promise of this slice.
        self.assertEqual(
            client.get_available_actions(),
            [{"name": "choose_map_node"}],
        )


if __name__ == "__main__":
    unittest.main()
