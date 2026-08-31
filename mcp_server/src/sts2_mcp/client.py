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
_DEFAULT_ACTION_TIMEOUT = 30.0
_DEFAULT_MAX_RETRIES = 2
_RETRY_BACKOFF_BASE = 0.5
_ACTION_RESPONSE_READ_EXCEPTIONS = (OSError, ValueError, http.client.HTTPException)
_ACTION_TRANSPORT_EXCEPTIONS = (OSError, http.client.HTTPException)


@dataclass(slots=True)
class Sts2ApiError(RuntimeError):
    status_code: int
    code: str
    message: str
    details: Any = None
    retryable: bool = False

    def __str__(self) -> str:
        parts = [f"{self.code}: {self.message}", f"http={self.status_code}"]
        if self.retryable:
            parts.append("retryable=true")
        if self.details is not None:
            parts.append(f"details={json.dumps(self.details, ensure_ascii=False)}")
        return " | ".join(parts)


class Sts2Client:
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
                details={"reason": str(exc), "path": "/events/stream"},
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
                return None
            except Sts2ApiError as exc:
                if exc.code != "connection_error":
                    raise
                if time.monotonic() >= deadline:
                    return None

    def end_turn(self) -> dict[str, Any]:
        return self.execute_action(
            "end_turn",
            client_context={
                "source": "mcp",
                "tool_name": "end_turn",
            },
        )

    def play_card(self, card_index: int, target_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "play_card",
            card_index=card_index,
            target_index=target_index,
            client_context={
                "source": "mcp",
                "tool_name": "play_card",
            },
        )

    def continue_run(self) -> dict[str, Any]:
        return self.execute_action(
            "continue_run",
            client_context={
                "source": "mcp",
                "tool_name": "continue_run",
            },
        )

    def continue_game_over(self) -> dict[str, Any]:
        return self.execute_action(
            "continue_game_over",
            client_context={
                "source": "mcp",
                "tool_name": "continue_game_over",
            },
        )

    def abandon_run(self) -> dict[str, Any]:
        return self.execute_action(
            "abandon_run",
            client_context={
                "source": "mcp",
                "tool_name": "abandon_run",
            },
        )

    def save_and_quit(self) -> dict[str, Any]:
        return self.execute_action(
            "save_and_quit",
            client_context={
                "source": "mcp",
                "tool_name": "save_and_quit",
            },
        )

    def open_character_select(self) -> dict[str, Any]:
        return self.execute_action(
            "open_character_select",
            client_context={
                "source": "mcp",
                "tool_name": "open_character_select",
            },
        )

    def open_timeline(self) -> dict[str, Any]:
        return self.execute_action(
            "open_timeline",
            client_context={
                "source": "mcp",
                "tool_name": "open_timeline",
            },
        )

    def close_main_menu_submenu(self) -> dict[str, Any]:
        return self.execute_action(
            "close_main_menu_submenu",
            client_context={
                "source": "mcp",
                "tool_name": "close_main_menu_submenu",
            },
        )

    def choose_timeline_epoch(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_timeline_epoch",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_timeline_epoch",
            },
        )

    def confirm_timeline_overlay(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_timeline_overlay",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_timeline_overlay",
            },
        )

    def choose_map_node(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_map_node",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_map_node",
            },
        )

    def collect_rewards_and_proceed(self) -> dict[str, Any]:
        return self.execute_action(
            "collect_rewards_and_proceed",
            client_context={
                "source": "mcp",
                "tool_name": "collect_rewards_and_proceed",
            },
        )

    def resolve_rewards(self, option_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "resolve_rewards",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "resolve_rewards",
            },
        )

    def claim_reward(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "claim_reward",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "claim_reward",
            },
        )

    def choose_reward_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_reward_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_reward_card",
            },
        )

    def skip_reward_cards(self) -> dict[str, Any]:
        return self.execute_action(
            "skip_reward_cards",
            client_context={
                "source": "mcp",
                "tool_name": "skip_reward_cards",
            },
        )

    def select_deck_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "select_deck_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "select_deck_card",
            },
        )

    def confirm_selection(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_selection",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_selection",
            },
        )

    def proceed(self) -> dict[str, Any]:
        return self.execute_action(
            "proceed",
            client_context={
                "source": "mcp",
                "tool_name": "proceed",
            },
        )

    def open_chest(self) -> dict[str, Any]:
        return self.execute_action(
            "open_chest",
            client_context={
                "source": "mcp",
                "tool_name": "open_chest",
            },
        )

    def choose_treasure_relic(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_treasure_relic",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_treasure_relic",
            },
        )

    def choose_event_option(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_event_option",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_event_option",
            },
        )

    def crystal_set_tool(self, tool: str) -> dict[str, Any]:
        return self.execute_action(
            "crystal_set_tool",
            tool=tool,
            client_context={
                "source": "mcp",
                "tool_name": "crystal_set_tool",
            },
        )

    def crystal_clear_cell(self, x: int, y: int, tool: str | None = None) -> dict[str, Any]:
        return self.execute_action(
            "crystal_clear_cell",
            x=x,
            y=y,
            tool=tool,
            client_context={
                "source": "mcp",
                "tool_name": "crystal_clear_cell",
            },
        )

    def choose_capstone_option(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_capstone_option",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_capstone_option",
            },
        )

    def choose_bundle(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_bundle",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_bundle",
            },
        )

    def confirm_bundle(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_bundle",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_bundle",
            },
        )

    def choose_rest_option(self, option_index: int, target_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "choose_rest_option",
            option_index=option_index,
            target_index=target_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_rest_option",
            },
        )

    def open_shop_inventory(self) -> dict[str, Any]:
        return self.execute_action(
            "open_shop_inventory",
            client_context={
                "source": "mcp",
                "tool_name": "open_shop_inventory",
            },
        )

    def close_shop_inventory(self) -> dict[str, Any]:
        return self.execute_action(
            "close_shop_inventory",
            client_context={
                "source": "mcp",
                "tool_name": "close_shop_inventory",
            },
        )

    def buy_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "buy_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "buy_card",
            },
        )

    def buy_relic(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "buy_relic",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "buy_relic",
            },
        )

    def buy_potion(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "buy_potion",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "buy_potion",
            },
        )

    def remove_card_at_shop(self) -> dict[str, Any]:
        return self.execute_action(
            "remove_card_at_shop",
            client_context={
                "source": "mcp",
                "tool_name": "remove_card_at_shop",
            },
        )

    def select_character(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "select_character",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "select_character",
            },
        )

    def embark(self) -> dict[str, Any]:
        return self.execute_action(
            "embark",
            client_context={
                "source": "mcp",
                "tool_name": "embark",
            },
        )

    def unready(self) -> dict[str, Any]:
        return self.execute_action(
            "unready",
            client_context={
                "source": "mcp",
                "tool_name": "unready",
            },
        )

    def increase_ascension(self) -> dict[str, Any]:
        return self.execute_action(
            "increase_ascension",
            client_context={
                "source": "mcp",
                "tool_name": "increase_ascension",
            },
        )

    def decrease_ascension(self) -> dict[str, Any]:
        return self.execute_action(
            "decrease_ascension",
            client_context={
                "source": "mcp",
                "tool_name": "decrease_ascension",
            },
        )

    def use_potion(self, option_index: int, target_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "use_potion",
            option_index=option_index,
            target_index=target_index,
            client_context={
                "source": "mcp",
                "tool_name": "use_potion",
            },
        )

    def discard_potion(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "discard_potion",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "discard_potion",
            },
        )

    def run_console_command(self, command: str) -> dict[str, Any]:
        return self.execute_action(
            "run_console_command",
            command=command,
            client_context={
                "source": "mcp",
                "tool_name": "run_console_command",
            },
        )

    def confirm_modal(self) -> dict[str, Any]:
        return self.execute_action(
            "confirm_modal",
            client_context={
                "source": "mcp",
                "tool_name": "confirm_modal",
            },
        )

    def dismiss_modal(self) -> dict[str, Any]:
        return self.execute_action(
            "dismiss_modal",
            client_context={
                "source": "mcp",
                "tool_name": "dismiss_modal",
            },
        )

    def return_to_main_menu(self) -> dict[str, Any]:
        return self.execute_action(
            "return_to_main_menu",
            client_context={
                "source": "mcp",
                "tool_name": "return_to_main_menu",
            },
        )

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
        payload = json.loads(response_body.decode("utf-8"))
        if not payload.get("ok", False):
            error_payload = payload.get("error", {})
            raise Sts2ApiError(
                status_code=200,
                code=error_payload.get("code", "unknown_error"),
                message=error_payload.get("message", "Request failed."),
                details=error_payload.get("details"),
                retryable=bool(error_payload.get("retryable", False)),
            )

        data = payload.get("data")
        if expect_object_data and not isinstance(data, dict):
            raise Sts2ApiError(
                status_code=200,
                code="invalid_response",
                message="Server response did not contain an object data payload.",
                details=payload,
            )

        return data

    @staticmethod
    def _build_api_error(status_code: int, response_body: bytes) -> Sts2ApiError:
        try:
            payload = json.loads(response_body.decode("utf-8"))
        except json.JSONDecodeError:
            return Sts2ApiError(
                status_code=status_code,
                code="invalid_response",
                message="Server returned a non-JSON error response.",
            )

        error_payload = payload.get("error", {})
        return Sts2ApiError(
            status_code=status_code,
            code=error_payload.get("code", "unknown_error"),
            message=error_payload.get("message", "Request failed."),
            details=error_payload.get("details"),
            retryable=bool(error_payload.get("retryable", False)),
        )

    @staticmethod
    def _decode_action_success(response_body: bytes, *, expect_object_data: bool = True) -> Any:
        payload, error_payload = Sts2Client._decode_action_response_envelope(response_body, status_code=200)

        if not payload["ok"]:
            assert error_payload is not None
            raise Sts2Client._action_error_from_payload(200, error_payload)

        data = payload.get("data")
        if expect_object_data and not isinstance(data, dict):
            raise Sts2ApiError(
                status_code=200,
                code="invalid_response",
                message="Server response did not contain an object data payload.",
                details=payload,
            )

        return data

    @staticmethod
    def _build_action_api_error(status_code: int, response_body: bytes) -> Sts2ApiError:
        try:
            payload, error_payload = Sts2Client._decode_action_response_envelope(
                response_body,
                status_code=status_code,
            )
        except Sts2ApiError as exc:
            return exc

        if payload["ok"]:
            return Sts2ApiError(
                status_code=status_code,
                code="invalid_response",
                message="HTTP error response incorrectly declared ok=true.",
            )

        assert error_payload is not None
        return Sts2Client._action_error_from_payload(status_code, error_payload)

    @staticmethod
    def _decode_action_response_envelope(
        response_body: bytes,
        *,
        status_code: int,
    ) -> tuple[dict[str, Any], dict[str, Any] | None]:
        try:
            payload = json.loads(response_body.decode("utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            raise Sts2ApiError(
                status_code=status_code,
                code="invalid_response",
                message="Server returned a non-JSON response.",
            )

        if not isinstance(payload, dict):
            raise Sts2ApiError(
                status_code=status_code,
                code="invalid_response",
                message="Server returned a JSON response that was not an object.",
                details={"response_type": type(payload).__name__},
            )

        ok = payload.get("ok")
        if not isinstance(ok, bool):
            raise Sts2ApiError(
                status_code=status_code,
                code="invalid_response",
                message="Server response field 'ok' was missing or was not a boolean.",
                details={"ok_type": type(ok).__name__},
            )

        error_payload: dict[str, Any] | None = None
        if not ok:
            raw_error = payload.get("error")
            if not isinstance(raw_error, dict):
                raise Sts2ApiError(
                    status_code=status_code,
                    code="invalid_response",
                    message="Server response field 'error' was missing or was not an object.",
                    details={"error_type": type(raw_error).__name__},
                )

            code = raw_error.get("code")
            message = raw_error.get("message")
            retryable = raw_error.get("retryable")
            invalid_field: tuple[str, Any] | None = None
            if not isinstance(code, str) or not code:
                invalid_field = ("code", code)
            elif not isinstance(message, str):
                invalid_field = ("message", message)
            elif not isinstance(retryable, bool):
                invalid_field = ("retryable", retryable)

            if invalid_field is not None:
                field_name, field_value = invalid_field
                raise Sts2ApiError(
                    status_code=status_code,
                    code="invalid_response",
                    message=f"Server response error field '{field_name}' had an invalid schema.",
                    details={
                        "field": field_name,
                        "field_type": type(field_value).__name__,
                    },
                )
            error_payload = raw_error

        return payload, error_payload

    @staticmethod
    def _action_error_from_payload(status_code: int, error_payload: dict[str, Any]) -> Sts2ApiError:
        return Sts2ApiError(
            status_code=status_code,
            code=error_payload["code"],
            message=error_payload["message"],
            details=error_payload.get("details"),
            retryable=error_payload["retryable"],
        )
