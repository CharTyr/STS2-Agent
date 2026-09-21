from __future__ import annotations

import json
import unittest

from sts2_mcp.envelope import Envelope, Sts2ApiError, parse, parse_strict


def body(payload: object) -> bytes:
    return json.dumps(payload).encode("utf-8")


class LenientEnvelopeTests(unittest.TestCase):
    """Reads must keep working when the mod answers without a full envelope.

    A GET is not an action: a missing `ok` costs nothing, and a truncated body has to stay a decode
    error rather than becoming an API error a retry loop would act on.
    """

    def test_ok_envelope_returns_its_data(self) -> None:
        envelope = parse(body({"ok": True, "data": {"screen": "COMBAT"}}), status_code=200)

        self.assertTrue(envelope.ok)
        self.assertEqual(envelope.data, {"screen": "COMBAT"})
        self.assertIsNone(envelope.error)

    def test_missing_ok_fails_closed(self) -> None:
        """A read that does not claim success is not success; only the error fields are lenient."""
        envelope = parse(body({"data": {"screen": "COMBAT"}}), status_code=200)

        self.assertFalse(envelope.ok)
        assert envelope.error is not None
        self.assertEqual(envelope.error.code, "unknown_error")

    def test_error_defaults_fill_in_a_thin_error_object(self) -> None:
        envelope = parse(body({"ok": False, "error": {}}), status_code=500)

        self.assertFalse(envelope.ok)
        assert envelope.error is not None
        self.assertEqual(envelope.error.code, "unknown_error")
        self.assertEqual(envelope.error.message, "Request failed.")
        self.assertFalse(envelope.error.retryable)

    def test_error_reads_the_four_fields_callers_branch_on(self) -> None:
        envelope = parse(
            body(
                {
                    "ok": False,
                    "error": {
                        "code": "invalid_action",
                        "message": "Not legal right now.",
                        "retryable": True,
                        "details": {"action": "play_card"},
                    },
                }
            ),
            status_code=409,
        )

        assert envelope.error is not None
        self.assertEqual(envelope.error.code, "invalid_action")
        self.assertEqual(envelope.error.message, "Not legal right now.")
        self.assertTrue(envelope.error.retryable)
        self.assertEqual(envelope.error.details, {"action": "play_card"})

    def test_truncated_body_keeps_its_own_exception_type_when_asked(self) -> None:
        with self.assertRaises(json.JSONDecodeError):
            parse(b'{"ok":true,"data":', status_code=200, decode_errors="raise")

    def test_truncated_error_body_becomes_an_api_error(self) -> None:
        with self.assertRaises(Sts2ApiError) as caught:
            parse(b"not json at all", status_code=502)

        self.assertEqual(caught.exception.code, "invalid_response")
        self.assertEqual(caught.exception.status_code, 502)

    def test_json_that_is_not_an_object_is_refused(self) -> None:
        with self.assertRaises(Sts2ApiError) as caught:
            parse(body([1, 2, 3]), status_code=200)

        self.assertEqual(caught.exception.code, "invalid_response")
        self.assertEqual(caught.exception.details, {"response_type": "list"})

    def test_require_object_data_refuses_a_non_object_payload(self) -> None:
        envelope = parse(body({"ok": True, "data": [1, 2, 3]}), status_code=200)

        with self.assertRaises(Sts2ApiError) as caught:
            envelope.require_object_data()

        self.assertEqual(caught.exception.code, "invalid_response")


class StrictEnvelopeTests(unittest.TestCase):
    """An action envelope is the only thing between a lost response and a replayed card."""

    def test_ok_must_be_a_boolean(self) -> None:
        for payload in ({"data": {}}, {"ok": "true", "data": {}}, {"ok": 1, "data": {}}):
            with self.subTest(payload=payload):
                with self.assertRaises(Sts2ApiError) as caught:
                    parse_strict(body(payload), status_code=200)
                self.assertEqual(caught.exception.code, "invalid_response")
                self.assertFalse(caught.exception.retryable)

    def test_a_failure_must_carry_a_well_typed_error(self) -> None:
        cases = {
            "code": {"code": 7, "message": "m", "retryable": False},
            "message": {"code": "c", "message": None, "retryable": False},
            "retryable": {"code": "c", "message": "m", "retryable": "yes"},
        }
        for field, error in cases.items():
            with self.subTest(field=field):
                with self.assertRaises(Sts2ApiError) as caught:
                    parse_strict(body({"ok": False, "error": error}), status_code=200)
                self.assertEqual(caught.exception.details["field"], field)

    def test_a_well_formed_failure_keeps_its_retryable_flag(self) -> None:
        envelope = parse_strict(
            body(
                {
                    "ok": False,
                    "error": {"code": "outcome_unknown", "message": "Lost response.", "retryable": False},
                }
            ),
            status_code=200,
        )

        self.assertFalse(envelope.ok)
        assert envelope.error is not None
        self.assertEqual(envelope.error.code, "outcome_unknown")
        self.assertFalse(envelope.error.retryable)

    def test_the_raw_envelope_is_available_for_diagnostics(self) -> None:
        envelope = parse_strict(body({"ok": True, "data": {"status": "completed"}}), status_code=200)

        self.assertIsInstance(envelope, Envelope)
        self.assertEqual(envelope.raw["data"], {"status": "completed"})


if __name__ == "__main__":
    unittest.main()
