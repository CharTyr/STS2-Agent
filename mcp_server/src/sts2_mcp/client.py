from __future__ import annotations

import json
import logging
import os
import time
from dataclasses import dataclass
from typing import Any
from urllib import error, request

logger = logging.getLogger("sts2_mcp")

_DEFAULT_READ_TIMEOUT = 10.0
_DEFAULT_ACTION_TIMEOUT = 30.0
_DEFAULT_MAX_RETRIES = 2
_RETRY_BACKOFF_BASE = 0.5


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

    def abandon_run(self) -> dict[str, Any]:
        return self.execute_action(
            "abandon_run",
            client_context={
                "source": "mcp",
                "tool_name": "abandon_run",
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

    def choose_rest_option(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_rest_option",
            option_index=option_index,
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
    ) -> dict[str, Any]:
        timeout = self._action_timeout if is_action else self._read_timeout
        raw_payload = None
        headers: dict[str, str] = {
            "Accept": "application/json",
        }

        if payload is not None:
            raw_payload = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"

        last_error: Sts2ApiError | None = None
        attempts = 1 + self._max_retries

        for attempt in range(attempts):
            if attempt > 0:
                delay = _RETRY_BACKOFF_BASE * (2 ** (attempt - 1))
                logger.info("Retry %d/%d for %s %s in %.1fs", attempt, self._max_retries, method, path, delay)
                time.sleep(delay)

            http_request = request.Request(
                url=f"{self._base_url}{path}",
                method=method,
                data=raw_payload,
                headers=headers,
            )

            try:
                with request.urlopen(http_request, timeout=timeout) as response:
                    return self._decode_success(response.read())
            except error.HTTPError as exc:
                last_error = self._build_api_error(exc.code, exc.read())
                if not last_error.retryable or attempt >= self._max_retries:
                    raise last_error
            except error.URLError as exc:
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
                if attempt >= self._max_retries:
                    raise last_error

        raise last_error or AssertionError("unreachable")

    @staticmethod
    def _decode_success(response_body: bytes) -> dict[str, Any]:
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
        if not isinstance(data, dict):
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
