from __future__ import annotations

import asyncio
import http.client
import unittest
from unittest.mock import MagicMock, patch
from urllib.error import HTTPError

from sts2_mcp.client import Sts2ApiError, Sts2Client
from sts2_mcp.state_views import diff_state
from sts2_mcp.server import create_server


class DevAuditRegressionTests(unittest.TestCase):
    def test_registered_diff_tool_executes_in_every_profile(self) -> None:
        for profile in ("guided", "layered", "full"):
            with self.subTest(profile=profile):
                server = create_server(tool_profile=profile)
                tool = asyncio.run(server.get_tool("diff_state"))
                result = tool.fn(before={"hp": 5}, after={"hp": 4})
                self.assertEqual(result["changes"], [{"path": "hp", "before": 5, "after": 4}])

    def test_diff_exact_cap_is_complete(self) -> None:
        self.assertFalse(diff_state({"a": 0, "z": 3}, {"a": 1, "z": 3}, limit=1)["truncated"])

    def test_diff_depth_limit_is_explicit(self) -> None:
        before, after = 0, 1
        for _ in range(15):
            before, after = {"child": before}, {"child": after}
        self.assertTrue(diff_state(before, after)["truncated"])

    def test_diff_empty_object_is_not_string(self) -> None:
        self.assertEqual(diff_state({"a": {}}, {"a": "{}"})["change_count"], 1)

    def test_diff_literal_keys_do_not_collide(self) -> None:
        result = diff_state({"a.b": 1, "a": {"b": 2}}, {"a.b": 3, "a": {"b": 2}})
        self.assertEqual(result["change_count"], 1)
        self.assertEqual(result["changes"][0]["path"], '["a.b"]')

    def test_diff_unicode_paths_match_native(self) -> None:
        pairs = [('茶', '["\\u8336"]'), ('🙂', '["\\ud83d\\ude42"]'), ('\u2028', '["\\u2028"]'), ('\\uABCD', '["\\\\uABCD"]'), ('a\nb', '["a\\nb"]')]
        for name, expected in pairs:
            with self.subTest(name=name):
                self.assertEqual(diff_state({name: 0}, {name: 1})["changes"][0]["path"], expected)

    def test_stream_read_transport_errors_are_structured(self) -> None:
        for error in (ConnectionResetError("reset"), http.client.IncompleteRead(b"partial", 20)):
            with self.subTest(error=type(error).__name__):
                response = MagicMock()
                response.__enter__.return_value = response
                response.readline.side_effect = error
                with patch("sts2_mcp.client.request.urlopen", return_value=response):
                    with self.assertRaises(Sts2ApiError) as raised:
                        list(Sts2Client().iter_events())
                self.assertEqual(raised.exception.code, "connection_error")
                self.assertTrue(raised.exception.retryable)

    def test_connect_timeout_surfaces_for_polling_fallback(self) -> None:
        with patch("sts2_mcp.client.request.urlopen", side_effect=TimeoutError("connect timed out")) as opened:
            with self.assertRaises(Sts2ApiError) as raised:
                Sts2Client().wait_for_event(timeout=1.0)
        self.assertEqual(opened.call_count, 1)
        self.assertEqual(raised.exception.details["kind"], "connect_timeout")

    def test_http_error_with_unreadable_body_stays_structured(self) -> None:
        body = MagicMock()
        body.read.side_effect = ConnectionResetError("body reset")
        failure = HTTPError("http://127.0.0.1/events/stream", 503, "unavailable", {}, body)
        with patch("sts2_mcp.client.request.urlopen", side_effect=failure):
            with self.assertRaises(Sts2ApiError) as raised:
                list(Sts2Client().iter_events())
        self.assertEqual(raised.exception.status_code, 503)

    def test_open_timeout_is_capped_by_overall_deadline(self) -> None:
        response = MagicMock()
        response.__enter__.return_value = response
        response.readline.return_value = b""
        with patch("sts2_mcp.client.time.monotonic", return_value=10.0), \
             patch("sts2_mcp.client.request.urlopen", return_value=response) as opened:
            self.assertEqual(list(Sts2Client().iter_events(read_timeout=90.0, deadline=10.25)), [])
        self.assertEqual(opened.call_args.kwargs["timeout"], 0.25)

    def test_repeated_eof_reconnects_back_off(self) -> None:
        # A clock that also progresses per read makes the old busy-loop test terminate.
        now = [0.0]
        sleeps = []
        response = MagicMock()
        response.__enter__.return_value = response
        def eof():
            now[0] += 0.01
            return b""
        response.readline.side_effect = eof
        def sleep(seconds):
            sleeps.append(seconds)
            now[0] += seconds
        with patch("sts2_mcp.client.request.urlopen", return_value=response) as opened, \
             patch("sts2_mcp.client.time.monotonic", side_effect=lambda: now[0]), \
             patch("sts2_mcp.client.time.sleep", side_effect=sleep):
            self.assertIsNone(Sts2Client().wait_for_event(timeout=0.3))
        self.assertGreater(len(sleeps), 0)
        self.assertLessEqual(opened.call_count, 4)
        self.assertLessEqual(now[0], 0.31)


if __name__ == "__main__":
    unittest.main()
