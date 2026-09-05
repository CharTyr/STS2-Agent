from __future__ import annotations

import http.client
import io
import json
import socket
import unittest
from typing import Any, Callable
from unittest.mock import patch
from urllib import error

from sts2_mcp.client import Sts2ApiError, Sts2Client


class JsonResponse:
    def __init__(self, payload: Any) -> None:
        self._body = json.dumps(payload).encode("utf-8")

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb) -> bool:
        return False

    def read(self) -> bytes:
        return self._body


class RawResponse:
    def __init__(self, body: bytes) -> None:
        self._body = body

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb) -> bool:
        return False

    def read(self) -> bytes:
        return self._body


class ResponseReadFailure:
    def __init__(self, read_error: BaseException) -> None:
        self._read_error = read_error

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb) -> bool:
        return False

    def read(self) -> bytes:
        raise self._read_error


class ErrorBodyReadFailure(error.HTTPError):
    def __init__(self, url: str, body_error: BaseException, *, status_code: int = 503) -> None:
        super().__init__(url, status_code, "Error response", hdrs=None, fp=None)
        self._body_error = body_error

    def read(self, *args, **kwargs) -> bytes:
        raise self._body_error


class ActionReconciliationTransport:
    def __init__(
        self,
        action_handler: Callable[[Any, Any], Any],
        *,
        state_payload: dict[str, Any] | None = None,
        state_error: Exception | None = None,
    ) -> None:
        self._action_handler = action_handler
        self._state_payload = state_payload or {
            "screen": "UNLOCK",
            "phase": "reconciled",
        }
        self._state_error = state_error
        self.action_calls = 0
        self.state_calls = 0

    def urlopen(self, http_request, timeout=None):
        if http_request.get_method() == "GET" and http_request.full_url.endswith("/state"):
            self.state_calls += 1
            if self._state_error is not None:
                raise self._state_error
            return JsonResponse({"ok": True, "data": self._state_payload})

        self.action_calls += 1
        return self._action_handler(http_request, timeout)


class ActionReplaySafetyTests(unittest.TestCase):
    def _execute_uncertain_action(
        self,
        action_handler: Callable[[Any, Any], Any],
        *,
        action: str = "confirm_unlock",
        invoke: Callable[[Sts2Client], dict[str, Any]] | None = None,
        state_error: Exception | None = None,
    ) -> tuple[dict[str, Any], ActionReconciliationTransport]:
        client = Sts2Client(
            base_url="http://127.0.0.1:8080",
            max_retries=5,
        )
        transport = ActionReconciliationTransport(
            action_handler,
            state_error=state_error,
        )

        with patch("sts2_mcp.client.request.urlopen", new=transport.urlopen):
            with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                result = invoke(client) if invoke is not None else client.execute_action(action)

        self.assertEqual(transport.action_calls, 1)
        self.assertEqual(transport.state_calls, 1)
        sleep_mock.assert_not_called()
        self.assertEqual(result["action"], action)
        self.assertEqual(result["status"], "outcome_unknown")
        self.assertTrue(result["outcome_unknown"])
        self.assertFalse(result["stable"])
        self.assertFalse(result["retryable"])
        self.assertEqual(result["error"]["code"], "action_outcome_unknown")
        self.assertFalse(result["error"]["retryable"])

        reconciliation = result["reconciliation"]
        self.assertTrue(reconciliation["attempted"])
        self.assertEqual(reconciliation["method"], "GET")
        self.assertEqual(reconciliation["path"], "/state")
        self.assertEqual(reconciliation["submitted_action"]["action"], action)
        if state_error is None:
            self.assertTrue(reconciliation["succeeded"])
            self.assertEqual(reconciliation["status"], "succeeded")
            self.assertFalse(reconciliation["required"])
            self.assertEqual(reconciliation["state"]["phase"], "reconciled")
        else:
            self.assertFalse(reconciliation["succeeded"])
            self.assertEqual(reconciliation["status"], "failed")
            self.assertTrue(reconciliation["required"])
            self.assertFalse(reconciliation["error"]["retryable"])

        return result, transport

    @staticmethod
    def _lost_response(http_request, timeout=None):
        return ResponseReadFailure(
            socket.timeout("response lost after server completed the action")
        )

    def test_continue_game_over_lost_response_posts_once_and_reconciles(self) -> None:
        self._execute_uncertain_action(
            self._lost_response,
            action="continue_game_over",
            invoke=lambda client: client.continue_game_over(),
        )

    def test_return_to_main_menu_lost_response_posts_once_and_reconciles(self) -> None:
        self._execute_uncertain_action(
            self._lost_response,
            action="return_to_main_menu",
            invoke=lambda client: client.return_to_main_menu(),
        )

    def test_confirm_unlock_lost_response_does_not_close_second_unlock(self) -> None:
        unlocks_remaining = 2

        def confirm_then_lose_response(http_request, timeout=None):
            nonlocal unlocks_remaining
            payload = json.loads(http_request.data.decode("utf-8"))
            self.assertEqual(payload["action"], "confirm_unlock")
            unlocks_remaining -= 1
            return ResponseReadFailure(socket.timeout("response lost"))

        self._execute_uncertain_action(confirm_then_lose_response)
        self.assertEqual(unlocks_remaining, 1)

    def test_action_transport_failures_post_once_and_reconcile(self) -> None:
        failures = (
            TimeoutError("action timed out"),
            ConnectionResetError("connection reset after request write"),
            http.client.RemoteDisconnected("server closed without a response"),
        )

        for failure in failures:
            with self.subTest(failure=type(failure).__name__):
                def fail_action(http_request, timeout=None):
                    raise failure

                self._execute_uncertain_action(fail_action)

    def test_action_truncated_response_posts_once_and_reconciles(self) -> None:
        self._execute_uncertain_action(
            lambda http_request, timeout=None: RawResponse(b'{"ok":true,"data":')
        )

    def test_action_non_object_json_matrix_posts_once_and_reconciles(self) -> None:
        for response_payload in ([], ["ok"], None, True, 7, "ok"):
            with self.subTest(response_type=type(response_payload).__name__):
                self._execute_uncertain_action(
                    lambda http_request, timeout=None, value=response_payload: RawResponse(
                        json.dumps(value).encode("utf-8")
                    )
                )

    def test_action_http_error_body_read_failures_post_once_and_reconcile(self) -> None:
        failures = (
            http.client.IncompleteRead(b'{"ok":false'),
            ConnectionResetError("connection reset while reading error body"),
            http.client.RemoteDisconnected("server closed during error body"),
        )

        for failure in failures:
            with self.subTest(failure=type(failure).__name__):
                def unreadable_error(http_request, timeout=None):
                    raise ErrorBodyReadFailure(http_request.full_url, failure)

                self._execute_uncertain_action(unreadable_error)

    def test_action_response_read_failure_permanent_matrix(self) -> None:
        error_factories = (
            ("oserror", lambda: OSError("response stream failed")),
            ("valueerror", lambda: ValueError("read of closed file")),
            ("httpexception", lambda: http.client.HTTPException("stream protocol failed")),
        )

        for status_code in (200, 400, 503):
            for error_name, error_factory in error_factories:
                with self.subTest(status_code=status_code, error=error_name):
                    read_error = error_factory()

                    def unreadable_response(http_request, timeout=None):
                        if status_code == 200:
                            return ResponseReadFailure(read_error)
                        raise ErrorBodyReadFailure(
                            http_request.full_url,
                            read_error,
                            status_code=status_code,
                        )

                    self._execute_uncertain_action(unreadable_response)

    def test_action_response_read_does_not_swallow_control_flow_exceptions(self) -> None:
        for status_code in (200, 503):
            for control_flow_error in (KeyboardInterrupt(), SystemExit(2)):
                with self.subTest(
                    status_code=status_code,
                    error=type(control_flow_error).__name__,
                ):
                    def interrupted_response(http_request, timeout=None):
                        if status_code == 200:
                            return ResponseReadFailure(control_flow_error)
                        raise ErrorBodyReadFailure(
                            http_request.full_url,
                            control_flow_error,
                            status_code=status_code,
                        )

                    client = Sts2Client(
                        base_url="http://127.0.0.1:8080",
                        max_retries=5,
                    )
                    transport = ActionReconciliationTransport(interrupted_response)
                    with patch("sts2_mcp.client.request.urlopen", new=transport.urlopen):
                        with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                            with self.assertRaises(type(control_flow_error)):
                                client.execute_action("confirm_unlock")

                    self.assertEqual(transport.action_calls, 1)
                    self.assertEqual(transport.state_calls, 0)
                    sleep_mock.assert_not_called()

    def test_action_http_error_non_object_json_matrix_reconciles(self) -> None:
        for response_payload in ([], None, True, 503, "service unavailable"):
            with self.subTest(response_type=type(response_payload).__name__):
                response_body = json.dumps(response_payload).encode("utf-8")

                def non_object_error(http_request, timeout=None):
                    raise error.HTTPError(
                        http_request.full_url,
                        503,
                        "Service Unavailable",
                        hdrs=None,
                        fp=io.BytesIO(response_body),
                    )

                self._execute_uncertain_action(non_object_error)

    def test_action_http_200_error_field_non_object_matrix_reconciles(self) -> None:
        invalid_error_fields = (
            ("array", []),
            ("null", None),
            ("boolean", False),
            ("number", 503),
            ("string", "service unavailable"),
        )

        for case_name, error_payload in invalid_error_fields:
            with self.subTest(case=case_name):
                self._execute_uncertain_action(
                    lambda http_request, timeout=None, value=error_payload: JsonResponse(
                        {"ok": False, "error": value}
                    )
                )

    def test_action_invalid_envelope_schema_matrix_reconciles(self) -> None:
        invalid_envelopes = (
            ("missing_ok", {"data": {}}),
            ("ok_not_boolean", {"ok": "true", "data": {}}),
            ("missing_error", {"ok": False}),
            (
                "missing_error_code",
                {"ok": False, "error": {"message": "failed", "retryable": False}},
            ),
            (
                "error_code_not_string",
                {"ok": False, "error": {"code": [], "message": "failed", "retryable": False}},
            ),
            (
                "error_message_not_string",
                {"ok": False, "error": {"code": "failed", "message": [], "retryable": False}},
            ),
            (
                "error_retryable_not_boolean",
                {"ok": False, "error": {"code": "failed", "message": "failed", "retryable": "false"}},
            ),
            ("missing_success_data", {"ok": True}),
        )

        for case_name, response_payload in invalid_envelopes:
            with self.subTest(case=case_name):
                self._execute_uncertain_action(
                    lambda http_request, timeout=None, value=response_payload: JsonResponse(value)
                )

    def test_reconciliation_failure_is_structured_and_does_not_replay_post(self) -> None:
        state_error = error.URLError(ConnectionResetError("state unavailable"))
        result, transport = self._execute_uncertain_action(
            self._lost_response,
            state_error=state_error,
        )

        self.assertEqual(transport.action_calls, 1)
        self.assertEqual(transport.state_calls, 1)
        reconciliation_error = result["reconciliation"]["error"]
        self.assertEqual(reconciliation_error["type"], "URLError")
        self.assertIn("state unavailable", reconciliation_error["message"])
        self.assertFalse(reconciliation_error["retryable"])

    def test_retryable_http_action_error_is_not_replayed_or_reconciled(self) -> None:
        response_body = json.dumps(
            {
                "ok": False,
                "error": {
                    "code": "temporarily_unavailable",
                    "message": "retry later",
                    "retryable": True,
                },
            }
        ).encode("utf-8")

        def retryable_action_error(http_request, timeout=None):
            raise error.HTTPError(
                http_request.full_url,
                503,
                "Service Unavailable",
                hdrs=None,
                fp=io.BytesIO(response_body),
            )

        client = Sts2Client(base_url="http://127.0.0.1:8080", max_retries=5)
        transport = ActionReconciliationTransport(retryable_action_error)
        with patch("sts2_mcp.client.request.urlopen", new=transport.urlopen):
            with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                with self.assertRaisesRegex(Sts2ApiError, "temporarily_unavailable") as caught:
                    client.execute_action("confirm_unlock")

        self.assertEqual(transport.action_calls, 1)
        self.assertEqual(transport.state_calls, 0)
        sleep_mock.assert_not_called()
        self.assertFalse(caught.exception.retryable)

    def test_non_action_get_and_head_preserve_transport_retry_count(self) -> None:
        for method in ("GET", "HEAD"):
            with self.subTest(method=method):
                client = Sts2Client(
                    base_url="http://127.0.0.1:8080",
                    max_retries=2,
                )
                calls = 0

                def flaky_transport(http_request, timeout=None):
                    nonlocal calls
                    calls += 1
                    if calls < 3:
                        raise error.URLError(socket.timeout("temporary read failure"))
                    return JsonResponse({"ok": True, "data": {"status": "ready"}})

                with patch("sts2_mcp.client.request.urlopen", new=flaky_transport):
                    with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                        result = client._request(method, "/health")

                self.assertEqual(calls, 3)
                self.assertEqual(sleep_mock.call_count, 2)
                self.assertEqual(result, {"status": "ready"})

    def test_non_action_get_and_head_preserve_retryable_http_error_semantics(self) -> None:
        error_body = json.dumps(
            {
                "ok": False,
                "error": {
                    "code": "busy",
                    "message": "retry later",
                    "retryable": True,
                },
            }
        ).encode("utf-8")

        for method in ("GET", "HEAD"):
            with self.subTest(method=method):
                client = Sts2Client(
                    base_url="http://127.0.0.1:8080",
                    max_retries=2,
                )
                calls = 0

                def flaky_http_error(http_request, timeout=None):
                    nonlocal calls
                    calls += 1
                    if calls < 3:
                        raise error.HTTPError(
                            http_request.full_url,
                            503,
                            "Service Unavailable",
                            hdrs=None,
                            fp=io.BytesIO(error_body),
                        )
                    return JsonResponse({"ok": True, "data": {"status": "ready"}})

                with patch("sts2_mcp.client.request.urlopen", new=flaky_http_error):
                    with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                        result = client._request(method, "/health")

                self.assertEqual(calls, 3)
                self.assertEqual(sleep_mock.call_count, 2)
                self.assertEqual(result, {"status": "ready"})

    def test_non_action_get_and_head_preserve_response_read_exception_types(self) -> None:
        error_factories = (
            lambda: OSError("response stream failed"),
            lambda: ValueError("read of closed file"),
            lambda: http.client.HTTPException("stream protocol failed"),
        )

        for method in ("GET", "HEAD"):
            for error_factory in error_factories:
                read_error = error_factory()
                with self.subTest(method=method, error=type(read_error).__name__):
                    client = Sts2Client(
                        base_url="http://127.0.0.1:8080",
                        max_retries=5,
                    )
                    calls = 0

                    def unreadable_response(http_request, timeout=None):
                        nonlocal calls
                        calls += 1
                        return ResponseReadFailure(read_error)

                    with patch("sts2_mcp.client.request.urlopen", new=unreadable_response):
                        with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                            with self.assertRaises(type(read_error)) as caught:
                                client._request(method, "/health")

                    self.assertIs(caught.exception, read_error)
                    self.assertEqual(calls, 1)
                    sleep_mock.assert_not_called()

    def test_non_action_get_and_head_preserve_http_error_read_exception_types(self) -> None:
        error_factories = (
            lambda: OSError("error stream failed"),
            lambda: ValueError("read of closed file"),
            lambda: http.client.HTTPException("stream protocol failed"),
        )

        for method in ("GET", "HEAD"):
            for error_factory in error_factories:
                read_error = error_factory()
                with self.subTest(method=method, error=type(read_error).__name__):
                    client = Sts2Client(
                        base_url="http://127.0.0.1:8080",
                        max_retries=5,
                    )
                    calls = 0

                    def unreadable_error(http_request, timeout=None):
                        nonlocal calls
                        calls += 1
                        raise ErrorBodyReadFailure(http_request.full_url, read_error)

                    with patch("sts2_mcp.client.request.urlopen", new=unreadable_error):
                        with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                            with self.assertRaises(type(read_error)) as caught:
                                client._request(method, "/health")

                    self.assertIs(caught.exception, read_error)
                    self.assertEqual(calls, 1)
                    sleep_mock.assert_not_called()

    def test_non_action_get_and_head_preserve_json_decode_error_type(self) -> None:
        for method in ("GET", "HEAD"):
            with self.subTest(method=method):
                client = Sts2Client(
                    base_url="http://127.0.0.1:8080",
                    max_retries=5,
                )
                calls = 0

                def truncated_response(http_request, timeout=None):
                    nonlocal calls
                    calls += 1
                    return RawResponse(b'{"ok":true,"data":')

                with patch("sts2_mcp.client.request.urlopen", new=truncated_response):
                    with patch("sts2_mcp.client.time.sleep") as sleep_mock:
                        with self.assertRaises(json.JSONDecodeError):
                            client._request(method, "/health")

                self.assertEqual(calls, 1)
                sleep_mock.assert_not_called()


if __name__ == "__main__":
    unittest.main()
