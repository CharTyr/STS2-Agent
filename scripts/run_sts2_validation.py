from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
import time
from dataclasses import dataclass
from typing import Any, Callable
from urllib import error, request


class ValidationError(RuntimeError):
    pass


Predicate = Callable[[dict[str, Any]], bool]


@dataclass(slots=True)
class ApiClient:
    base_url: str = "http://127.0.0.1:8080"
    timeout: float = 5.0
    retries: int = 0
    retry_delay_ms: int = 500

    def request(self, method: str, path: str, body: dict[str, Any] | None = None) -> dict[str, Any]:
        url = self.base_url.rstrip("/") + path
        headers = {"Accept": "application/json"}
        payload = None
        if body is not None:
            payload = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"

        last_error: Exception | None = None
        for attempt in range(self.retries + 1):
            if attempt > 0:
                time.sleep(self.retry_delay_ms / 1000.0)

            http_request = request.Request(url=url, method=method, data=payload, headers=headers)
            try:
                with request.urlopen(http_request, timeout=self.timeout) as response:
                    return self._decode_json(response.read(), f"{method} {path}")
            except error.HTTPError as exc:
                decoded = self._decode_json(exc.read(), f"{method} {path}")
                if isinstance(decoded, dict):
                    return decoded
                last_error = ValidationError(f"{method} {path} returned HTTP {exc.code}")
            except error.URLError as exc:
                last_error = ValidationError(f"{method} {path} failed: {exc}")

        raise last_error or ValidationError(f"{method} {path} failed")

    def get_state(self) -> dict[str, Any]:
        payload = self.request("GET", "/state")
        self._require_ok(payload, "GET /state")
        return payload["data"]

    def get_available_actions(self) -> list[dict[str, Any]]:
        payload = self.request("GET", "/actions/available")
        self._require_ok(payload, "GET /actions/available")
        return list(payload["data"]["actions"])

    def action(self, action_name: str, **kwargs: Any) -> dict[str, Any]:
        payload = {"action": action_name}
        payload.update(kwargs)
        return self.request("POST", "/action", payload)

    def wait_for_state(
        self,
        description: str,
        predicate: Predicate,
        *,
        attempts: int,
        delay_ms: int,
    ) -> dict[str, Any]:
        for _ in range(attempts):
            state = self.get_state()
            if predicate(state):
                return state
            time.sleep(delay_ms / 1000.0)

        raise ValidationError(f"Timed out waiting for state: {description}")

    @staticmethod
    def _require_ok(payload: dict[str, Any], label: str) -> None:
        if not payload.get("ok"):
            raise ValidationError(f"{label} failed: {json.dumps(payload, ensure_ascii=False)}")

    @staticmethod
    def _decode_json(raw_body: bytes, label: str) -> dict[str, Any]:
        try:
            return json.loads(raw_body.decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise ValidationError(f"{label} returned non-JSON content") from exc


def assert_action_available(state: dict[str, Any], action_name: str) -> None:
    if action_name not in list(state.get("available_actions") or []):
        raise ValidationError(
            f"Expected action '{action_name}' to be available, but state was: {json.dumps(state, ensure_ascii=False)}"
        )


def ensure_action_ok(response: dict[str, Any], label: str) -> dict[str, Any]:
    if not response.get("ok"):
        raise ValidationError(f"{label} failed: {json.dumps(response, ensure_ascii=False)}")
    return response


def continue_from_main_menu_if_needed(client: ApiClient, state: dict[str, Any], *, attempts: int, delay_ms: int) -> dict[str, Any]:
    if state.get("screen") != "MAIN_MENU":
        return state

    assert_action_available(state, "continue_run")
    ensure_action_ok(client.action("continue_run"), "continue_run")
    return client.wait_for_state(
        "leave MAIN_MENU",
        lambda current: current.get("screen") != "MAIN_MENU",
        attempts=attempts,
        delay_ms=delay_ms,
    )


def collect_rewards_if_needed(client: ApiClient, state: dict[str, Any], *, attempts: int, delay_ms: int) -> dict[str, Any]:
    current = state
    while current.get("screen") == "REWARD":
        assert_action_available(current, "collect_rewards_and_proceed")
        ensure_action_ok(client.action("collect_rewards_and_proceed"), "collect_rewards_and_proceed")
        current = client.wait_for_state(
            "leave REWARD",
            lambda candidate: candidate.get("screen") != "REWARD",
            attempts=attempts,
            delay_ms=delay_ms,
        )

    return current


def run_debug_command(client: ApiClient, command: str) -> dict[str, Any]:
    response = client.action("run_console_command", command=command)
    return ensure_action_ok(response, f"run_console_command({command})")


def ensure_combat(client: ApiClient, state: dict[str, Any], *, attempts: int, delay_ms: int) -> dict[str, Any]:
    if state.get("in_combat") and state.get("screen") == "COMBAT":
        return state

    run_debug_command(client, "room Monster")
    return client.wait_for_state(
        "enter COMBAT",
        lambda current: bool(current.get("in_combat")) and current.get("screen") == "COMBAT",
        attempts=attempts,
        delay_ms=delay_ms,
    )


def first_unlocked_character(state: dict[str, Any]) -> dict[str, Any]:
    characters = [item for item in list(state["character_select"]["characters"]) if not item.get("is_locked")]
    if not characters:
        raise ValidationError("Expected at least one unlocked character.")
    return characters[0]


def suite_mod_load(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    health = client.request("GET", "/health")
    client._require_ok(health, "GET /health")

    if not args.deep_check:
        return health["data"]

    state = client.get_state()
    actions = client.get_available_actions()
    return {
        "health_ok": True,
        "state_ok": True,
        "actions_ok": True,
        "screen": state.get("screen"),
        "available_action_count": len(actions),
    }


def suite_state_summary(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    state = client.get_state()
    return {
        "screen": state.get("screen"),
        "in_combat": bool(state.get("in_combat")),
        "available_actions": list(state.get("available_actions") or []),
    }


def suite_assert_active_run_main_menu(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    client.request("GET", "/health")
    state = client.wait_for_state(
        "active-run MAIN_MENU",
        lambda current: current.get("screen") == "MAIN_MENU" and "continue_run" in list(current.get("available_actions") or []),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )
    assert_action_available(state, "abandon_run")
    assert_action_available(state, "open_timeline")
    return {
        "screen": state.get("screen"),
        "available_actions": list(state.get("available_actions") or []),
    }


def suite_bootstrap_active_run(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec, retries=args.request_retries, retry_delay_ms=args.retry_delay_ms)
    client.request("GET", "/health")

    state = client.wait_for_state(
        "stable startup state",
        lambda current: current.get("screen") != "UNKNOWN"
        and (current.get("screen") != "MAIN_MENU" or len(list(current.get("available_actions") or [])) > 0),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    if state.get("screen") == "MAIN_MENU" and "continue_run" in list(state.get("available_actions") or []):
        return {"already_active_run": True, "screen": state.get("screen")}

    if state.get("screen") != "MAIN_MENU" or "open_character_select" not in list(state.get("available_actions") or []):
        raise ValidationError(f"Unable to bootstrap active run from state: {json.dumps(state, ensure_ascii=False)}")

    ensure_action_ok(client.action("open_character_select"), "open_character_select")
    character_select_state = client.wait_for_state(
        "CHARACTER_SELECT while bootstrapping active run",
        lambda current: current.get("screen") == "CHARACTER_SELECT" and current.get("character_select") is not None,
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    selected_character = first_unlocked_character(character_select_state)
    ensure_action_ok(
        client.action("select_character", option_index=int(selected_character["index"])),
        "select_character",
    )

    client.wait_for_state(
        "embarkable CHARACTER_SELECT",
        lambda current: current.get("screen") == "CHARACTER_SELECT"
        and current.get("character_select") is not None
        and bool(current["character_select"].get("can_embark")),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    ensure_action_ok(client.action("embark"), "embark")
    run_state = client.wait_for_state(
        "leave CHARACTER_SELECT while bootstrapping active run",
        lambda current: current.get("screen") != "CHARACTER_SELECT",
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    while run_state.get("screen") == "MODAL":
        available_actions = list(run_state.get("available_actions") or [])
        modal_action = "confirm_modal" if "confirm_modal" in available_actions else "dismiss_modal"
        ensure_action_ok(client.action(modal_action), modal_action)
        run_state = client.wait_for_state(
            "leave embark modal while bootstrapping active run",
            lambda current: current.get("screen") != "MODAL",
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

    if run_state.get("screen") == "MAIN_MENU":
        raise ValidationError("Embark returned to MAIN_MENU instead of entering a run.")

    return {
        "selected_character_id": selected_character["character_id"],
        "screen": run_state.get("screen"),
    }


async def _list_tool_names(server: Any) -> list[str]:
    return sorted(tool.name for tool in await server.list_tools())


def suite_mcp_tool_profile(_: argparse.Namespace) -> dict[str, Any]:
    from sts2_mcp.server import create_server

    essential_tools = {
        "health_check",
        "get_game_state",
        "get_available_actions",
        "wait_for_event",
        "wait_until_actionable",
        "act",
    }
    guided_debug_tools = essential_tools | {"run_console_command"}
    legacy_action_tools = {
        "play_card",
        "choose_map_node",
        "claim_reward",
        "proceed",
        "confirm_selection",
        "unready",
        "increase_ascension",
        "decrease_ascension",
    }

    previous_env = os.environ.get("STS2_ENABLE_DEBUG_ACTIONS")
    try:
        os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)
        guided = asyncio.run(_list_tool_names(create_server()))
        full = asyncio.run(_list_tool_names(create_server(tool_profile="full")))
        os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = "1"
        guided_debug = asyncio.run(_list_tool_names(create_server()))
    finally:
        if previous_env is None:
            os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)
        else:
            os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = previous_env

    failures: list[str] = []
    if not essential_tools.issubset(set(guided)):
        failures.append("guided profile is missing one or more essential tools")
    if set(guided) != essential_tools:
        failures.append(f"guided profile should expose exactly the essential tool set, but exposed {guided}")
    if any(name in guided for name in legacy_action_tools):
        failures.append("guided profile should not expose legacy per-action tools")
    if "run_console_command" in guided:
        failures.append("guided profile should hide run_console_command while debug actions are disabled")
    if set(guided_debug) != guided_debug_tools:
        failures.append(f"guided debug profile should only add run_console_command, but exposed {guided_debug}")
    if not legacy_action_tools.issubset(set(full)):
        failures.append("full profile should expose legacy action wrappers")
    if len(full) <= len(guided):
        failures.append("full profile should expose more tools than guided profile")

    if failures:
        raise ValidationError("; ".join(failures))

    return {
        "guided_count": len(guided),
        "guided_tools": guided,
        "guided_debug_count": len(guided_debug),
        "full_count": len(full),
        "failures": failures,
    }


def suite_debug_console_gating(args: argparse.Namespace) -> dict[str, Any]:
    from sts2_mcp.client import Sts2Client
    from sts2_mcp.server import create_server

    expected_enabled = bool(args.enable_debug_actions)
    previous_env = os.environ.get("STS2_ENABLE_DEBUG_ACTIONS")
    if expected_enabled:
        os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = "1"
    else:
        os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)

    class CapturingClient(Sts2Client):
        def __init__(self) -> None:
            super().__init__(base_url=args.base_url)
            self.last_request: dict[str, Any] | None = None

        def _request(self, method: str, path: str, payload: dict[str, Any] | None = None, *, is_action: bool = False) -> dict[str, Any]:
            self.last_request = {
                "method": method,
                "path": path,
                "payload": payload,
                "is_action": is_action,
            }
            return {"ok": True}

    try:
        tools = asyncio.run(_list_tool_names(create_server()))
        client = CapturingClient()
        client_error: dict[str, Any] | None = None
        try:
            client.run_console_command(args.command)
        except Exception as exc:
            client_error = {"type": type(exc).__name__, "message": str(exc)}
    finally:
        if previous_env is None:
            os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)
        else:
            os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = previous_env

    tool_registered = "run_console_command" in tools
    if expected_enabled and not tool_registered:
        raise ValidationError("Expected MCP debug tool to be registered when debug actions are enabled.")
    if not expected_enabled and tool_registered:
        raise ValidationError("Expected MCP debug tool to stay hidden while debug actions are disabled.")
    if client_error is not None:
        raise ValidationError(f"Expected MCP client run_console_command wiring to succeed, but received: {client_error}")

    client_request = client.last_request or {}
    payload = client_request.get("payload") or {}
    if payload.get("action") != "run_console_command" or payload.get("command") != args.command:
        raise ValidationError(
            f"Expected MCP client payload to contain action=run_console_command and the requested command, but received: {client_request}"
        )

    api_client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    api_client.request("GET", "/health")
    result = api_client.action("run_console_command", command=args.command)

    if expected_enabled:
        if not result.get("ok") or result.get("data", {}).get("status") != "completed":
            raise ValidationError(f"Expected debug command to succeed, but received: {json.dumps(result, ensure_ascii=False)}")
    else:
        error_payload = result.get("error") or {}
        if result.get("ok") or error_payload.get("code") != "invalid_action":
            raise ValidationError(f"Expected invalid_action while debug actions are disabled, but received: {json.dumps(result, ensure_ascii=False)}")

    return {
        "debug_actions_enabled": expected_enabled,
        "ok": bool(result.get("ok")),
        "status": (result.get("data") or {}).get("status"),
        "error_code": (result.get("error") or {}).get("code"),
        "message": (result.get("data") or {}).get("message") or (result.get("error") or {}).get("message"),
        "mcp_tool_registered": tool_registered,
        "mcp_client_payload_ok": client_error is None,
    }


def suite_main_menu_active_run(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    client.request("GET", "/health")

    state = client.wait_for_state(
        "active-run MAIN_MENU",
        lambda current: current.get("screen") == "MAIN_MENU" and "continue_run" in list(current.get("available_actions") or []),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    assert_action_available(state, "abandon_run")
    assert_action_available(state, "open_timeline")

    abandon_response = ensure_action_ok(client.action("abandon_run"), "abandon_run")
    modal_state = abandon_response["data"]["state"]
    if modal_state.get("screen") != "MODAL":
        raise ValidationError(f"Expected abandon_run to open MODAL, but received: {json.dumps(abandon_response, ensure_ascii=False)}")

    assert_action_available(modal_state, "confirm_modal")
    assert_action_available(modal_state, "dismiss_modal")
    ensure_action_ok(client.action("dismiss_modal"), "dismiss_modal")

    client.wait_for_state(
        "return to MAIN_MENU after dismiss_modal",
        lambda current: current.get("screen") == "MAIN_MENU" and "open_timeline" in list(current.get("available_actions") or []),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    timeline_response = ensure_action_ok(client.action("open_timeline"), "open_timeline")
    timeline_state = timeline_response["data"]["state"]
    assert_action_available(timeline_state, "choose_timeline_epoch")
    assert_action_available(timeline_state, "close_main_menu_submenu")

    choose_epoch_response = ensure_action_ok(client.action("choose_timeline_epoch", option_index=0), "choose_timeline_epoch")
    epoch_state = choose_epoch_response["data"]["state"]
    timeline = epoch_state.get("timeline") or {}
    if not timeline.get("inspect_open") and not timeline.get("unlock_screen_open"):
        raise ValidationError(
            f"Expected choose_timeline_epoch to open an inspect or unlock overlay, but received: {json.dumps(choose_epoch_response, ensure_ascii=False)}"
        )
    if not timeline.get("can_confirm_overlay"):
        raise ValidationError(
            f"Expected choose_timeline_epoch response state to expose timeline.can_confirm_overlay=true, but received: {json.dumps(choose_epoch_response, ensure_ascii=False)}"
        )

    assert_action_available(epoch_state, "confirm_timeline_overlay")
    ensure_action_ok(client.action("confirm_timeline_overlay"), "confirm_timeline_overlay")
    timeline_after_confirm = client.wait_for_state(
        "timeline overlay close",
        lambda current: current.get("screen") == "MAIN_MENU"
        and current.get("timeline") is not None
        and not current["timeline"].get("inspect_open")
        and not current["timeline"].get("unlock_screen_open"),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    assert_action_available(timeline_after_confirm, "close_main_menu_submenu")
    ensure_action_ok(client.action("close_main_menu_submenu"), "close_main_menu_submenu")
    client.wait_for_state(
        "return to MAIN_MENU after closing timeline",
        lambda current: current.get("screen") == "MAIN_MENU" and "continue_run" in list(current.get("available_actions") or []),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    ensure_action_ok(client.action("continue_run"), "continue_run")
    run_state = client.wait_for_state(
        "leave MAIN_MENU via continue_run",
        lambda current: current.get("screen") != "MAIN_MENU",
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    return {
        "initial_menu_actions": list(state.get("available_actions") or []),
        "timeline_epoch_state": "inspect" if timeline.get("inspect_open") else "unlock",
        "continue_run_destination": run_state.get("screen"),
        "final_available_actions": list(run_state.get("available_actions") or []),
    }


def suite_new_run_lifecycle(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(
        base_url=args.base_url,
        timeout=args.timeout_sec,
        retries=args.request_retries,
        retry_delay_ms=args.retry_delay_ms,
    )
    client.request("GET", "/health")

    state = client.wait_for_state(
        "active-run MAIN_MENU",
        lambda current: current.get("screen") == "MAIN_MENU" and "abandon_run" in list(current.get("available_actions") or []),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    abandon_response = ensure_action_ok(client.action("abandon_run"), "abandon_run")
    modal_state = abandon_response["data"]["state"]
    assert_action_available(modal_state, "confirm_modal")
    ensure_action_ok(client.action("confirm_modal"), "confirm_modal")

    client.wait_for_state(
        "MAIN_MENU without active run",
        lambda current: current.get("screen") == "MAIN_MENU" and "open_character_select" in list(current.get("available_actions") or []),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    ensure_action_ok(client.action("open_character_select"), "open_character_select")
    character_select_state = client.wait_for_state(
        "CHARACTER_SELECT",
        lambda current: current.get("screen") == "CHARACTER_SELECT" and current.get("character_select") is not None,
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    selected_character = first_unlocked_character(character_select_state)
    ensure_action_ok(client.action("select_character", option_index=int(selected_character["index"])), "select_character")

    client.wait_for_state(
        "character select can embark",
        lambda current: current.get("screen") == "CHARACTER_SELECT"
        and current.get("character_select") is not None
        and bool(current["character_select"].get("can_embark")),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    ensure_action_ok(client.action("embark"), "embark")
    run_state = client.wait_for_state(
        "leave CHARACTER_SELECT into a run",
        lambda current: current.get("screen") != "CHARACTER_SELECT",
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    while run_state.get("screen") == "MODAL":
        available_actions = list(run_state.get("available_actions") or [])
        modal_action = "confirm_modal" if "confirm_modal" in available_actions else "dismiss_modal"
        ensure_action_ok(client.action(modal_action), modal_action)
        run_state = client.wait_for_state(
            "leave embark modal",
            lambda current: current.get("screen") != "MODAL",
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

    run_debug_command(client, "die")
    game_over_state = client.wait_for_state(
        "GAME_OVER",
        lambda current: current.get("screen") == "GAME_OVER"
        and current.get("game_over") is not None
        and bool(current["game_over"].get("can_return_to_main_menu")),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    ensure_action_ok(client.action("return_to_main_menu"), "return_to_main_menu")
    final_menu_state = client.wait_for_state(
        "MAIN_MENU after game over",
        lambda current: current.get("screen") == "MAIN_MENU" and "open_character_select" in list(current.get("available_actions") or []),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    return {
        "selected_character_id": selected_character["character_id"],
        "embark_destination": run_state.get("screen"),
        "game_over_actions": list(game_over_state.get("available_actions") or []),
        "final_menu_actions": list(final_menu_state.get("available_actions") or []),
    }


def suite_combat_hand_confirm_flow(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    client.request("GET", "/health")
    state = client.get_state()

    if state.get("screen") == "CARD_SELECTION":
        raise ValidationError("combat hand confirm flow expects a stable starting screen, but current screen is CARD_SELECTION.")

    state = continue_from_main_menu_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)
    state = collect_rewards_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)
    state = ensure_combat(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)

    run_debug_command(client, "card CLAW hand")
    run_debug_command(client, "card PURITY hand")

    state = client.get_state()
    purity_card = next((card for card in list(state["combat"]["hand"]) if card.get("card_id") == "PURITY"), None)
    if purity_card is None:
        raise ValidationError("Failed to inject PURITY into the current combat hand.")

    play_response = ensure_action_ok(client.action("play_card", card_index=int(purity_card["index"])), "play_card(PURITY)")
    if play_response["data"]["status"] != "pending" or bool(play_response["data"]["stable"]):
        raise ValidationError(
            f"Expected PURITY play_card to return pending while awaiting manual selection, but received: {json.dumps(play_response, ensure_ascii=False)}"
        )

    selection_state = play_response["data"]["state"]
    if selection_state.get("screen") != "CARD_SELECTION":
        raise ValidationError(f"Expected PURITY selection to report screen=CARD_SELECTION, but received: {json.dumps(play_response, ensure_ascii=False)}")

    assert_action_available(selection_state, "select_deck_card")
    assert_action_available(selection_state, "confirm_selection")
    selection_payload = selection_state.get("selection") or {}
    if not selection_payload.get("requires_confirmation") or not selection_payload.get("can_confirm"):
        raise ValidationError(f"Expected PURITY selection to require confirmation, but received: {json.dumps(play_response, ensure_ascii=False)}")

    selection_cards = list(selection_payload.get("cards") or [])
    target_card = next((card for card in selection_cards if card.get("card_id") == "CLAW"), None) or (selection_cards[0] if selection_cards else None)
    if target_card is None:
        raise ValidationError(f"Expected PURITY selection to expose at least one selectable card, but received: {json.dumps(play_response, ensure_ascii=False)}")

    select_response = ensure_action_ok(
        client.action("select_deck_card", option_index=int(target_card["index"])),
        "select_deck_card",
    )
    if select_response["data"]["status"] != "pending" or bool(select_response["data"]["stable"]):
        raise ValidationError(
            f"Expected PURITY select_deck_card to stay pending until confirmation, but received: {json.dumps(select_response, ensure_ascii=False)}"
        )

    after_select_state = select_response["data"]["state"]
    if after_select_state.get("screen") != "CARD_SELECTION":
        raise ValidationError(f"Expected PURITY flow to remain in CARD_SELECTION after selecting a card, but received: {json.dumps(select_response, ensure_ascii=False)}")

    assert_action_available(after_select_state, "confirm_selection")
    if int(after_select_state.get("selection", {}).get("selected_count") or 0) < 1:
        raise ValidationError(f"Expected selected_count to increase after choosing a card, but received: {json.dumps(select_response, ensure_ascii=False)}")

    confirm_response = ensure_action_ok(client.action("confirm_selection"), "confirm_selection")
    if confirm_response["data"]["state"].get("in_combat") and confirm_response["data"]["state"].get("screen") == "COMBAT":
        final_state = confirm_response["data"]["state"]
    else:
        final_state = client.wait_for_state(
            "resolve PURITY selection back to COMBAT",
            lambda current: bool(current.get("in_combat")) and current.get("screen") == "COMBAT",
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

    if any(card.get("card_id") == target_card.get("card_id") for card in list(final_state["combat"]["hand"])):
        raise ValidationError(
            f"Expected selected card '{target_card.get('card_id')}' to be exhausted by PURITY, but final state was: {json.dumps(final_state, ensure_ascii=False)}"
        )

    return {
        "action": "PURITY",
        "selected_card_id": target_card.get("card_id"),
        "initial_status": play_response["data"]["status"],
        "post_select_status": select_response["data"]["status"],
        "confirm_status": confirm_response["data"]["status"],
        "final_screen": final_state.get("screen"),
        "final_hand_count": len(list(final_state["combat"]["hand"])),
    }


def suite_deferred_potion_flow(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    client.request("GET", "/health")
    state = client.get_state()

    if state.get("screen") == "CARD_SELECTION":
        raise ValidationError("deferred potion flow expects a stable starting screen, but current screen is CARD_SELECTION.")

    state = continue_from_main_menu_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)
    state = collect_rewards_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)
    state = ensure_combat(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)

    run_debug_command(client, "card STRIKE_DEFECT discard")
    run_debug_command(client, "card DEFEND_DEFECT discard")
    run_debug_command(client, "potion LIQUID_MEMORIES")

    state = client.get_state()
    liquid_memories = next((p for p in list(state["run"]["potions"]) if p.get("occupied") and p.get("potion_id") == "LIQUID_MEMORIES"), None)
    if liquid_memories is None:
        raise ValidationError("Failed to inject LIQUID_MEMORIES potion into the current run state.")

    use_response = ensure_action_ok(client.action("use_potion", option_index=int(liquid_memories["index"])), "use_potion")
    if use_response["data"]["status"] != "pending" or bool(use_response["data"]["stable"]):
        raise ValidationError(f"Expected LIQUID_MEMORIES to return pending while awaiting selection, but received: {json.dumps(use_response, ensure_ascii=False)}")

    selection_state = use_response["data"]["state"]
    if selection_state.get("screen") != "CARD_SELECTION":
        raise ValidationError(f"Expected use_potion state.screen=CARD_SELECTION, but received: {json.dumps(use_response, ensure_ascii=False)}")
    assert_action_available(selection_state, "select_deck_card")

    selection_cards = list(selection_state.get("selection", {}).get("cards") or [])
    if len(selection_cards) < 2:
        raise ValidationError(f"Expected LIQUID_MEMORIES to expose at least two discard options, but received: {json.dumps(use_response, ensure_ascii=False)}")

    selected_card = selection_cards[0]
    select_response = ensure_action_ok(client.action("select_deck_card", option_index=0), "select_deck_card")
    if select_response["data"]["state"].get("in_combat") and select_response["data"]["state"].get("screen") == "COMBAT":
        final_state = select_response["data"]["state"]
    else:
        final_state = client.wait_for_state(
            "resolve LIQUID_MEMORIES selection back to COMBAT",
            lambda current: bool(current.get("in_combat")) and current.get("screen") == "COMBAT",
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

    zero_cost_matches = [
        card for card in list(final_state["combat"]["hand"])
        if card.get("card_id") == selected_card.get("card_id") and int(card.get("energy_cost") or -1) == 0
    ]
    if not zero_cost_matches:
        raise ValidationError(
            f"Expected selected card '{selected_card.get('card_id')}' to return to hand at 0 cost, but final state was: {json.dumps(final_state, ensure_ascii=False)}"
        )

    return {
        "screen": final_state.get("screen"),
        "selected_card_id": selected_card.get("card_id"),
        "selected_card_zero_cost": True,
        "initial_status": use_response["data"]["status"],
        "initial_screen": selection_state.get("screen"),
        "selection_count": len(selection_cards),
    }


def suite_target_index_contract(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    client.request("GET", "/health")
    state = client.get_state()

    if state.get("screen") == "CARD_SELECTION":
        raise ValidationError("target index contract test expects a stable starting screen, but current screen is CARD_SELECTION.")

    state = continue_from_main_menu_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)
    state = collect_rewards_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)
    state = ensure_combat(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)

    empty_slots = [slot for slot in list(state["run"]["potions"]) if not slot.get("occupied")]
    if not empty_slots:
        discardable = next((p for p in list(state["run"]["potions"]) if p.get("occupied") and p.get("can_discard")), None)
        if discardable is None:
            raise ValidationError("Expected at least one discardable potion slot before injecting BLOCK_POTION.")

        ensure_action_ok(client.action("discard_potion", option_index=int(discardable["index"])), "discard_potion")
        state = client.wait_for_state(
            "free potion slot",
            lambda current: any(not potion.get("occupied") for potion in list(current["run"]["potions"])),
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

    run_debug_command(client, "card BELIEVE_IN_YOU hand")
    run_debug_command(client, "potion BLOCK_POTION")
    state = client.wait_for_state(
        "BELIEVE_IN_YOU and BLOCK_POTION injection",
        lambda current: current.get("screen") == "COMBAT"
        and bool(current.get("in_combat"))
        and any(card.get("card_id") == "BELIEVE_IN_YOU" for card in list(current["combat"]["hand"]))
        and any(p.get("occupied") and p.get("potion_id") == "BLOCK_POTION" for p in list(current["run"]["potions"])),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    card = next((item for item in list(state["combat"]["hand"]) if item.get("card_id") == "BELIEVE_IN_YOU"), None)
    if card is None:
        raise ValidationError("Failed to inject BELIEVE_IN_YOU into the current hand.")
    if card.get("target_type") != "AnyAlly":
        raise ValidationError(f"Expected BELIEVE_IN_YOU target_type=AnyAlly, but received: {json.dumps(card, ensure_ascii=False)}")
    if not card.get("requires_target") or card.get("target_index_space") != "players":
        raise ValidationError(f"Expected BELIEVE_IN_YOU to require combat.players[] targeting, but received: {json.dumps(card, ensure_ascii=False)}")
    card_target_indices = [int(index) for index in list(card.get("valid_target_indices") or []) if index is not None]
    if card_target_indices:
        raise ValidationError(f"Expected BELIEVE_IN_YOU to expose no valid_target_indices in singleplayer combat, but received: {json.dumps(card, ensure_ascii=False)}")
    if card.get("playable") or card.get("unplayable_reason") != "no_living_allies":
        raise ValidationError(f"Expected BELIEVE_IN_YOU to be unplayable with no_living_allies, but received: {json.dumps(card, ensure_ascii=False)}")

    block_potion = next((item for item in list(state["run"]["potions"]) if item.get("occupied") and item.get("potion_id") == "BLOCK_POTION"), None)
    if block_potion is None:
        raise ValidationError("Failed to inject BLOCK_POTION into the current run state.")
    if block_potion.get("target_type") != "AnyPlayer":
        raise ValidationError(f"Expected BLOCK_POTION target_type=AnyPlayer, but received: {json.dumps(block_potion, ensure_ascii=False)}")
    block_target_indices = [int(index) for index in list(block_potion.get("valid_target_indices") or []) if index is not None]
    if block_potion.get("requires_target") or str(block_potion.get("target_index_space") or "") or block_target_indices:
        raise ValidationError(f"Expected BLOCK_POTION to stay self-targeted in singleplayer combat, but received: {json.dumps(block_potion, ensure_ascii=False)}")

    block_before = int(state["combat"]["player"]["block"])
    use_potion_response = ensure_action_ok(client.action("use_potion", option_index=int(block_potion["index"])), "use_potion")
    if use_potion_response["data"]["status"] != "completed" or not bool(use_potion_response["data"]["stable"]):
        raise ValidationError(f"Expected BLOCK_POTION to complete immediately without target_index, but received: {json.dumps(use_potion_response, ensure_ascii=False)}")

    final_state = use_potion_response["data"]["state"]
    if int(final_state["combat"]["player"]["block"]) <= block_before:
        raise ValidationError(f"Expected BLOCK_POTION to increase player block without target_index, but final state was: {json.dumps(final_state, ensure_ascii=False)}")

    return {
        "screen": final_state.get("screen"),
        "any_ally_card": {
            "card_id": card.get("card_id"),
            "requires_target": bool(card.get("requires_target")),
            "target_index_space": card.get("target_index_space"),
            "valid_target_count": len(card_target_indices),
            "playable": bool(card.get("playable")),
            "unplayable_reason": card.get("unplayable_reason"),
        },
        "any_player_potion": {
            "potion_id": block_potion.get("potion_id"),
            "requires_target": bool(block_potion.get("requires_target")),
            "target_index_space": block_potion.get("target_index_space"),
            "final_block": int(final_state["combat"]["player"]["block"]),
        },
    }


def suite_enemy_intents_payload(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    client.request("GET", "/health")
    state = client.get_state()

    if state.get("screen") == "CARD_SELECTION":
        raise ValidationError("enemy intents payload test expects a stable starting screen, but current screen is CARD_SELECTION.")

    state = continue_from_main_menu_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)
    state = collect_rewards_if_needed(client, state, attempts=args.poll_attempts, delay_ms=args.poll_delay_ms)

    run_debug_command(client, "fight BYRDONIS_ELITE")
    state = client.wait_for_state(
        "enter BYRDONIS combat",
        lambda current: bool(current.get("in_combat"))
        and current.get("screen") == "COMBAT"
        and any(enemy.get("enemy_id") == "BYRDONIS" for enemy in list(current["combat"]["enemies"])),
        attempts=args.poll_attempts,
        delay_ms=args.poll_delay_ms,
    )

    enemy = next((item for item in list(state["combat"]["enemies"]) if item.get("enemy_id") == "BYRDONIS"), None)
    if enemy is None:
        raise ValidationError(f"Expected BYRDONIS enemy in encounter, but received: {json.dumps(state['combat']['enemies'], ensure_ascii=False)}")
    if not str(enemy.get("move_id") or "").strip():
        raise ValidationError(f"Expected combat enemy payload to expose move_id, but received: {json.dumps(enemy, ensure_ascii=False)}")
    if str(enemy.get("intent")) != str(enemy.get("move_id")):
        raise ValidationError(f"Expected legacy intent field to stay aligned with move_id, but received: {json.dumps(enemy, ensure_ascii=False)}")

    intents = list(enemy.get("intents") or [])
    if not intents:
        raise ValidationError(f"Expected BYRDONIS to expose at least one concrete intent payload, but received: {json.dumps(enemy, ensure_ascii=False)}")

    attack_intent = next((intent for intent in intents if str(intent.get("intent_type")) in {"Attack", "DeathBlow"}), None)
    if attack_intent is None:
        raise ValidationError(f"Expected BYRDONIS to expose an attack intent payload, but received: {json.dumps(enemy, ensure_ascii=False)}")
    if not str(attack_intent.get("label") or "").strip():
        raise ValidationError(f"Expected attack intent label to be populated, but received: {json.dumps(attack_intent, ensure_ascii=False)}")

    damage = attack_intent.get("damage")
    hits = attack_intent.get("hits")
    total_damage = attack_intent.get("total_damage")
    if damage is None or hits is None or total_damage is None:
        raise ValidationError(f"Expected attack intent damage fields to be populated, but received: {json.dumps(attack_intent, ensure_ascii=False)}")

    damage = int(damage)
    hits = int(hits)
    total_damage = int(total_damage)
    if damage <= 0 or hits < 1 or total_damage != damage * hits:
        raise ValidationError(f"Expected attack intent total_damage to equal damage * hits, but received: {json.dumps(attack_intent, ensure_ascii=False)}")

    return {
        "enemy_id": enemy.get("enemy_id"),
        "move_id": enemy.get("move_id"),
        "intent_count": len(intents),
        "attack_intent": {
            "intent_type": attack_intent.get("intent_type"),
            "label": attack_intent.get("label"),
            "damage": damage,
            "hits": hits,
            "total_damage": total_damage,
        },
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Shared validation entrypoint for STS2 macOS/Linux scripts.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    common_api: dict[str, tuple[Any, dict[str, Any]]] = {
        "--base-url": (str, {"default": "http://127.0.0.1:8080"}),
        "--timeout-sec": (float, {"default": 5.0}),
        "--poll-attempts": (int, {"default": 60}),
        "--poll-delay-ms": (int, {"default": 200}),
    }

    mod_load = subparsers.add_parser("mod-load")
    mod_load.add_argument("--base-url", default="http://127.0.0.1:8080")
    mod_load.add_argument("--timeout-sec", type=float, default=5.0)
    mod_load.add_argument("--deep-check", action="store_true")
    mod_load.set_defaults(func=suite_mod_load)

    state_summary = subparsers.add_parser("state-summary")
    state_summary.add_argument("--base-url", default="http://127.0.0.1:8080")
    state_summary.add_argument("--timeout-sec", type=float, default=5.0)
    state_summary.set_defaults(func=suite_state_summary)

    assert_active = subparsers.add_parser("assert-active-run-main-menu")
    for name, (arg_type, kwargs) in common_api.items():
        assert_active.add_argument(name, type=arg_type, **kwargs)
    assert_active.set_defaults(func=suite_assert_active_run_main_menu)

    bootstrap = subparsers.add_parser("bootstrap-active-run")
    for name, (arg_type, kwargs) in common_api.items():
        bootstrap.add_argument(name, type=arg_type, **kwargs)
    bootstrap.add_argument("--request-retries", type=int, default=3)
    bootstrap.add_argument("--retry-delay-ms", type=int, default=500)
    bootstrap.set_defaults(func=suite_bootstrap_active_run)

    tool_profile = subparsers.add_parser("mcp-tool-profile")
    tool_profile.set_defaults(func=suite_mcp_tool_profile)

    debug_gating = subparsers.add_parser("debug-console-gating")
    debug_gating.add_argument("--base-url", default="http://127.0.0.1:8080")
    debug_gating.add_argument("--timeout-sec", type=float, default=5.0)
    debug_gating.add_argument("--command", default="help")
    debug_gating.add_argument("--enable-debug-actions", action="store_true")
    debug_gating.set_defaults(func=suite_debug_console_gating)

    main_menu = subparsers.add_parser("main-menu-active-run")
    for name, (arg_type, kwargs) in common_api.items():
        main_menu.add_argument(name, type=arg_type, **kwargs)
    main_menu.set_defaults(func=suite_main_menu_active_run)

    new_run = subparsers.add_parser("new-run-lifecycle")
    for name, (arg_type, kwargs) in common_api.items():
        new_run.add_argument(name, type=arg_type, **kwargs)
    new_run.add_argument("--request-retries", type=int, default=3)
    new_run.add_argument("--retry-delay-ms", type=int, default=500)
    new_run.set_defaults(func=suite_new_run_lifecycle)

    combat_hand = subparsers.add_parser("combat-hand-confirm-flow")
    for name, (arg_type, kwargs) in common_api.items():
        combat_hand.add_argument(name, type=arg_type, **kwargs)
    combat_hand.set_defaults(func=suite_combat_hand_confirm_flow)

    deferred_potion = subparsers.add_parser("deferred-potion-flow")
    for name, (arg_type, kwargs) in common_api.items():
        deferred_potion.add_argument(name, type=arg_type, **kwargs)
    deferred_potion.set_defaults(func=suite_deferred_potion_flow)

    target_index = subparsers.add_parser("target-index-contract")
    for name, (arg_type, kwargs) in common_api.items():
        target_index.add_argument(name, type=arg_type, **kwargs)
    target_index.set_defaults(func=suite_target_index_contract)

    enemy_intents = subparsers.add_parser("enemy-intents-payload")
    for name, (arg_type, kwargs) in common_api.items():
        enemy_intents.add_argument(name, type=arg_type, **kwargs)
    enemy_intents.set_defaults(func=suite_enemy_intents_payload)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        payload = args.func(args)
    except ValidationError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    print(json.dumps(payload, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
