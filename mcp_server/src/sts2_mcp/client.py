from __future__ import annotations

import http.client
import json
import logging
import os
import socket
import time
from dataclasses import dataclass
from typing import Any, Iterable, Iterator
from urllib import error, request

from .client_actions import Sts2ActionMethods
from .envelope import Envelope, EnvelopeError, Sts2ApiError, parse, parse_strict

logger = logging.getLogger("sts2_mcp")


def _set_socket_read_timeout(response: Any, timeout: float) -> None:
    fp = getattr(response, "fp", None)
    candidates = [
        getattr(getattr(fp, "raw", None), "_sock", None),
        getattr(fp, "_sock", None),
        getattr(getattr(getattr(fp, "fp", None), "raw", None), "_sock", None),
        getattr(response, "_sock", None),
        getattr(response, "sock", None),
    ]

    for candidate in candidates:
        if candidate is None or not hasattr(candidate, "settimeout"):
            continue

        try:
            candidate.settimeout(timeout)
            return
        except OSError:
            continue


_DEFAULT_READ_TIMEOUT = 10.0
# Actions wait for the game to settle before answering, and continue_game_over waits up to 60
# seconds for the native summary save. A shorter client timeout than that turned a normal slow
# path into a lost response.
_DEFAULT_ACTION_TIMEOUT = 75.0
_DEFAULT_MAX_RETRIES = 2
_RETRY_BACKOFF_BASE = 0.5
_ACTION_RESPONSE_READ_EXCEPTIONS = (OSError, ValueError, http.client.HTTPException)
_ACTION_TRANSPORT_EXCEPTIONS = (OSError, http.client.HTTPException)
# A refused or unresolvable endpoint means the request never reached the mod, so the action did
# not run and the caller may retry. Any other transport failure happened after the request went
# out, which stays uncertain.
_ACTION_UNREACHABLE_REASONS = (ConnectionRefusedError, socket.gaierror)


class Sts2Client(Sts2ActionMethods):
    def __init__(
        self,
        base_url: str | None = None,
        read_timeout: float | None = None,
        action_timeout: float | None = None,
        max_retries: int | None = None,
    ) -> None:
        self._base_url = (base_url or os.getenv("STS2_API_BASE_URL") or "http://127.0.0.1:8080").rstrip("/")
        self._read_timeout = read_timeout or float(os.getenv("STS2_API_READ_TIMEOUT", str(_DEFAULT_READ_TIMEOUT)))
        self._action_timeout = action_timeout or float(os.getenv("STS2_API_ACTION_TIMEOUT", str(_DEFAULT_ACTION_TIMEOUT)))
        self._max_retries = max_retries if max_retries is not None else int(os.getenv("STS2_API_MAX_RETRIES", str(_DEFAULT_MAX_RETRIES)))

    @property
    def base_url(self) -> str:
        return self._base_url

    def get_health(self) -> dict[str, Any]:
        return self._request("GET", "/health")

    def get_state(self) -> dict[str, Any]:
        return self._request("GET", "/state")

    def get_available_actions(self) -> list[dict[str, Any]]:
        payload = self._request("GET", "/actions/available")
        return list(payload.get("actions", []))

    def get_decisions(self, limit: int = 50) -> Any:
        return self._request("GET", f"/decisions?limit={limit}", expect_object_data=False)

    def get_game_data_collection(self, collection: str) -> Any:
        return self._request("GET", f"/data/{collection}", expect_object_data=False)

    def iter_events(
        self,
        *,
        read_timeout: float | None = None,
        include_comments: bool = False,
        deadline: float | None = None,
    ) -> Iterator[dict[str, Any]]:
        timeout = read_timeout or float(os.getenv("STS2_EVENT_READ_TIMEOUT", "90"))
        http_request = request.Request(
            url=f"{self._base_url}/events/stream",
            method="GET",
            headers={
                "Accept": "text/event-stream",
                "Cache-Control": "no-cache",
            },
        )

        try:
            with request.urlopen(http_request, timeout=timeout) as response:
                event_id: str | None = None
                event_name: str | None = None
                data_lines: list[str] = []

                while True:
                    if deadline is not None:
                        remaining = deadline - time.monotonic()
                        if remaining <= 0:
                            raise socket.timeout("timed out")
                        _set_socket_read_timeout(response, max(remaining, 0.05))

                    raw_line = response.readline()
                    if not raw_line:
                        return

                    line = raw_line.decode("utf-8", errors="replace").rstrip("\r\n")

                    if not line:
                        if event_id is None and event_name is None and not data_lines:
                            continue

                        raw_data = "\n".join(data_lines)
                        parsed_data: Any = raw_data
                        if raw_data:
                            try:
                                parsed_data = json.loads(raw_data)
                            except json.JSONDecodeError:
                                parsed_data = raw_data

                        yield {
                            "id": event_id,
                            "event": event_name or "message",
                            "data": parsed_data,
                            "raw_data": raw_data,
                        }

                        event_id = None
                        event_name = None
                        data_lines = []
                        continue

                    if line.startswith(":"):
                        if include_comments:
                            yield {"comment": line[1:].strip()}
                        continue

                    field, _, value = line.partition(":")
                    if value.startswith(" "):
                        value = value[1:]

                    if field == "event":
                        event_name = value
                    elif field == "id":
                        event_id = value
                    elif field == "data":
                        data_lines.append(value)
                    elif field == "retry":
                        continue
        except error.HTTPError as exc:
            raise self._build_api_error(exc.code, exc.read()) from exc
        except error.URLError as exc:
            raise Sts2ApiError(
                status_code=0,
                code="connection_error",
                message=(
                    f"Cannot reach STS2 mod event stream at {self._base_url}. "
                    "Ensure the game is running and the mod is loaded."
                ),
                details={"reason": str(exc.reason), "path": "/events/stream"},
                retryable=True,
            ) from exc
        except (TimeoutError, socket.timeout) as exc:
            raise Sts2ApiError(
                status_code=0,
                code="connection_error",
                message=(
                    f"Timed out while reading the STS2 mod event stream at {self._base_url}. "
                    "The client will retry until the overall wait deadline expires."
                ),
                details={"reason": str(exc), "path": "/events/stream", "kind": "read_timeout"},
                retryable=True,
            ) from exc

    def wait_for_event(
        self,
        *,
        event_names: Iterable[str] | None = None,
        timeout: float = 30.0,
    ) -> dict[str, Any] | None:
        target_names = {name for name in (event_names or []) if name}
        deadline = time.monotonic() + timeout

        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                return None

            read_timeout = max(remaining, 0.05)
            try:
                for event in self.iter_events(read_timeout=read_timeout, deadline=deadline):
                    event_name = str(event.get("event", ""))
                    if not target_names or event_name in target_names:
                        return event
                # A bounded server queue closes a slow subscriber explicitly rather than
                # silently dropping events. Reconnect within the same overall deadline so the
                # next stream_ready/current state can resynchronize the caller.
                continue
            except Sts2ApiError as exc:
                if exc.code != "connection_error":
                    raise
                # Idle read/deadline timeouts on an opened stream stay inside this wait.
                # Transport failures never opened the stream and must surface immediately
                # so wait_until_actionable can poll /state.
                if (exc.details or {}).get("kind") != "read_timeout":
                    raise
                if time.monotonic() >= deadline:
                    return None

    def execute_action(
        self,
        action: str,
        *,
        card_index: int | None = None,
        target_index: int | None = None,
        option_index: int | None = None,
        x: int | None = None,
        y: int | None = None,
        tool: str | None = None,
        command: str | None = None,
        client_context: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        return self._request(
            "POST",
            "/action",
            payload={
                "action": action,
                "card_index": card_index,
                "target_index": target_index,
                "option_index": option_index,
                "x": x,
                "y": y,
                "tool": tool,
                "command": command,
                "client_context": client_context,
            },
            is_action=True,
        )

    def _request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
        *,
        is_action: bool = False,
        expect_object_data: bool = True,
    ) -> Any:
        action_post = is_action and method.upper() == "POST"
        timeout = self._action_timeout if action_post else self._read_timeout
        raw_payload = None
        headers: dict[str, str] = {
            "Accept": "application/json",
        }

        if payload is not None:
            raw_payload = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"

        last_error: Sts2ApiError | None = None
        retry_count = 0 if action_post else self._max_retries
        attempts = 1 + retry_count

        for attempt in range(attempts):
            if attempt > 0:
                delay = _RETRY_BACKOFF_BASE * (2 ** (attempt - 1))
                logger.info("Retry %d/%d for %s %s in %.1fs", attempt, retry_count, method, path, delay)
                time.sleep(delay)

            http_request = request.Request(
                url=f"{self._base_url}{path}",
                method=method,
                data=raw_payload,
                headers=headers,
            )

            try:
                with request.urlopen(http_request, timeout=timeout) as response:
                    if not action_post:
                        return self._decode_success(response.read(), expect_object_data=expect_object_data)

                    try:
                        response_body = response.read()
                    except _ACTION_RESPONSE_READ_EXCEPTIONS as read_exc:
                        return self._build_uncertain_action_result(path, payload, read_exc)

                    try:
                        return self._decode_action_success(
                            response_body,
                            expect_object_data=expect_object_data,
                        )
                    except Sts2ApiError as exc:
                        if exc.code == "invalid_response":
                            return self._build_uncertain_action_result(path, payload, exc)
                        exc.retryable = False
                        raise
            except error.HTTPError as exc:
                if not action_post:
                    last_error = self._build_api_error(exc.code, exc.read())
                    if not last_error.retryable or attempt >= retry_count:
                        raise last_error
                    continue

                try:
                    response_body = exc.read()
                except _ACTION_RESPONSE_READ_EXCEPTIONS as read_exc:
                    return self._build_uncertain_action_result(path, payload, read_exc)

                last_error = self._build_action_api_error(exc.code, response_body)
                if last_error.code == "invalid_response":
                    return self._build_uncertain_action_result(path, payload, last_error)
                last_error.retryable = False
                raise last_error
            except error.URLError as exc:
                if action_post:
                    if not isinstance(exc.reason, _ACTION_UNREACHABLE_REASONS):
                        return self._build_uncertain_action_result(path, payload, exc)

                last_error = Sts2ApiError(
                    status_code=0,
                    code="connection_error",
                    message=(
                        f"Cannot reach STS2 mod at {self._base_url}. "
                        "Ensure the game is running and the mod is loaded."
                    ),
                    details={"reason": str(exc.reason), "path": path},
                    retryable=True,
                )
                if attempt >= retry_count:
                    raise last_error
            except _ACTION_TRANSPORT_EXCEPTIONS as exc:
                if not action_post:
                    raise
                return self._build_uncertain_action_result(path, payload, exc)

        raise last_error or AssertionError("unreachable")

    def _build_uncertain_action_result(
        self,
        path: str,
        payload: dict[str, Any] | None,
        exc: BaseException,
    ) -> dict[str, Any]:
        submitted_action = {
            key: value
            for key, value in (payload or {}).items()
            if value is not None
        }
        action = submitted_action.get("action")
        reason = exc.reason if isinstance(exc, error.URLError) else exc
        reconciliation = self._reconcile_action_state_once(submitted_action)

        return {
            "action": action,
            "status": "outcome_unknown",
            "stable": False,
            "outcome_unknown": True,
            "retryable": False,
            "message": (
                "The action request may have completed, but its response was lost. "
                "A single state reconciliation was attempted; do not replay the action automatically."
            ),
            "error": {
                "code": "action_outcome_unknown",
                "reason": str(reason),
                "path": path,
                "retryable": False,
            },
            "reconciliation": reconciliation,
        }

    def _reconcile_action_state_once(self, submitted_action: dict[str, Any]) -> dict[str, Any]:
        http_request = request.Request(
            url=f"{self._base_url}/state",
            method="GET",
            headers={"Accept": "application/json"},
        )

        try:
            with request.urlopen(http_request, timeout=self._read_timeout) as response:
                state = self._decode_success(response.read())
        except Exception as exc:
            return {
                "required": True,
                "attempted": True,
                "succeeded": False,
                "status": "failed",
                "method": "GET",
                "path": "/state",
                "submitted_action": submitted_action,
                "error": self._serialize_reconciliation_error(exc),
            }

        return {
            "required": False,
            "attempted": True,
            "succeeded": True,
            "status": "succeeded",
            "method": "GET",
            "path": "/state",
            "submitted_action": submitted_action,
            "state": state,
        }

    @staticmethod
    def _serialize_reconciliation_error(exc: Exception) -> dict[str, Any]:
        if isinstance(exc, Sts2ApiError):
            return {
                "type": type(exc).__name__,
                "status_code": exc.status_code,
                "code": exc.code,
                "message": exc.message,
                "details": exc.details,
                "retryable": False,
            }

        result: dict[str, Any] = {
            "type": type(exc).__name__,
            "message": str(exc),
            "retryable": False,
        }
        status_code = getattr(exc, "code", None)
        if isinstance(status_code, int):
            result["status_code"] = status_code
        return result

    @staticmethod
    def _decode_success(response_body: bytes, *, expect_object_data: bool = True) -> Any:
        envelope = parse(response_body, status_code=200, decode_errors="raise")
        if not envelope.ok:
            assert envelope.error is not None
            raise Sts2Client._api_error_from_envelope(200, envelope.error)

        if not expect_object_data:
            return envelope.data
        return envelope.require_object_data()

    @staticmethod
    def _api_error_from_envelope(status_code: int, error: EnvelopeError) -> Sts2ApiError:
        return Sts2ApiError(
            status_code=status_code,
            code=error.code,
            message=error.message,
            details=error.details,
            retryable=error.retryable,
        )

    @staticmethod
    def _build_api_error(status_code: int, response_body: bytes) -> Sts2ApiError:
        envelope = parse(response_body, status_code=status_code)
        assert envelope.error is not None
        return Sts2Client._api_error_from_envelope(status_code, envelope.error)

    @staticmethod
    def _decode_action_success(response_body: bytes, *, expect_object_data: bool = True) -> Any:
        envelope = parse_strict(response_body, status_code=200)

        if not envelope.ok:
            assert envelope.error is not None
            raise Sts2Client._action_error_from_envelope(200, envelope.error)

        data = envelope.data
        if expect_object_data and not isinstance(data, dict):
            raise Sts2ApiError(
                status_code=200,
                code="invalid_response",
                message="Server response did not contain an object data payload.",
                details=envelope.raw,
            )

        # docs/api.md allows status "failed" alongside "completed" and "pending". Returning it as a
        # success would hide a failed action behind an ok envelope.
        if isinstance(data, dict) and data.get("status") == "failed":
            raise Sts2ApiError(
                status_code=200,
                code="action_failed",
                message=str(data.get("message") or "The action reported failure."),
                details=data,
            )

        return data

    @staticmethod
    def _build_action_api_error(status_code: int, response_body: bytes) -> Sts2ApiError:
        try:
            envelope = parse_strict(response_body, status_code=status_code)
        except Sts2ApiError as exc:
            return exc

        if envelope.ok:
            return Sts2ApiError(
                status_code=status_code,
                code="invalid_response",
                message="HTTP error response incorrectly declared ok=true.",
            )

        assert envelope.error is not None
        return Sts2Client._action_error_from_envelope(status_code, envelope.error)

    @staticmethod
    def _decode_action_response_envelope(
        response_body: bytes,
        *,
        status_code: int,
    ) -> tuple[dict[str, Any], dict[str, Any] | None]:
        """Kept for callers that still want the raw envelope and error object.

        The parsing, and the strictness that decides whether an action may be retried, lives in
        `envelope.parse_strict`.
        """
        envelope = parse_strict(response_body, status_code=status_code)
        raw_error = envelope.raw.get("error") if not envelope.ok else None
        return envelope.raw, raw_error

    @staticmethod
    def _action_error_from_envelope(status_code: int, error: EnvelopeError) -> Sts2ApiError:
        return Sts2ApiError(
            status_code=status_code,
            code=error.code,
            message=error.message,
            details=error.details,
            retryable=error.retryable,
        )
