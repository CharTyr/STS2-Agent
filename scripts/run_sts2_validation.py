from __future__ import annotations

import argparse
import asyncio
import json
import os
import signal
import socket
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable
from urllib import error, request


class ValidationError(RuntimeError):
    pass


class DeadlineExceeded(ValidationError):
    pass


class ApiRequestError(ValidationError):
    def __init__(
        self,
        label: str,
        *,
        status_code: int,
        code: str,
        message: str,
        details: Any = None,
        retryable: bool = False,
    ) -> None:
        self.label = label
        self.status_code = status_code
        self.code = code
        self.message = message
        self.details = details
        self.retryable = retryable
        summary = f"{label} failed: {code}: {message}"
        if details is not None:
            summary = f"{summary} | details={json.dumps(details, ensure_ascii=False)}"
        super().__init__(summary)


Predicate = Callable[[dict[str, Any]], bool]


def remaining_seconds(deadline: float | None) -> float | None:
    if deadline is None:
        return None
    return deadline - time.monotonic()


def sleep_with_deadline(delay_ms: int, deadline: float | None) -> None:
    delay_seconds = delay_ms / 1000.0
    if deadline is None:
        time.sleep(delay_seconds)
        return

    remaining = remaining_seconds(deadline)
    if remaining is None or remaining <= 0:
        return

    time.sleep(min(delay_seconds, remaining))


@dataclass(slots=True)
class ApiClient:
    base_url: str = "http://127.0.0.1:8080"
    timeout: float = 5.0
    retries: int = 2
    retry_delay_ms: int = 500

    def request(self, method: str, path: str, body: dict[str, Any] | None = None) -> dict[str, Any]:
        url = self.base_url.rstrip("/") + path
        headers = {"Accept": "application/json"}
        payload = None
        label = f"{method} {path}"
        if body is not None:
            payload = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"

        last_error: ValidationError | None = None
        for attempt in range(self.retries + 1):
            if attempt > 0:
                time.sleep(self.retry_delay_ms / 1000.0)

            http_request = request.Request(url=url, method=method, data=payload, headers=headers)
            try:
                with request.urlopen(http_request, timeout=self.timeout) as response:
                    return self._decode_json(response.read(), label)
            except error.HTTPError as exc:
                decoded = self._decode_json(exc.read(), label)
                last_error = self._build_api_error(decoded, label, status_code=exc.code)
                if isinstance(last_error, ApiRequestError) and last_error.retryable and attempt < self.retries:
                    continue
            except error.URLError as exc:
                last_error = ApiRequestError(
                    label,
                    status_code=0,
                    code="connection_error",
                    message=f"Cannot reach STS2 mod at {self.base_url}.",
                    details={"reason": str(exc.reason), "path": path},
                    retryable=True,
                )
                if attempt < self.retries:
                    continue

        raise last_error or ValidationError(f"{label} failed")

    def get_state(self) -> dict[str, Any]:
        payload = self.request("GET", "/state")
        self._require_ok(payload, "GET /state")
        return payload["data"]

    def get_available_actions_payload(self) -> dict[str, Any]:
        payload = self.request("GET", "/actions/available")
        self._require_ok(payload, "GET /actions/available")
        data = payload["data"]
        if not isinstance(data, dict):
            raise ValidationError(f"GET /actions/available returned an invalid data payload: {json.dumps(payload, ensure_ascii=False)}")
        return data

    def get_available_actions(self) -> list[dict[str, Any]]:
        payload = self.get_available_actions_payload()
        return list(payload["actions"])

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
        deadline: float | None = None,
    ) -> dict[str, Any]:
        last_error: ValidationError | None = None
        for _ in range(attempts):
            try:
                state = run_with_deadline_budget(self, self.get_state, deadline)
            except DeadlineExceeded:
                break
            except ApiRequestError as exc:
                if not exc.retryable:
                    raise
                last_error = exc
                sleep_with_deadline(delay_ms, deadline)
                continue

            if predicate(state):
                return state
            sleep_with_deadline(delay_ms, deadline)

        if last_error is not None:
            raise ValidationError(f"Timed out waiting for state: {description}. Last retryable error: {last_error}")
        raise ValidationError(f"Timed out waiting for state: {description}")

    @staticmethod
    def _require_ok(payload: dict[str, Any], label: str) -> None:
        if not payload.get("ok"):
            raise ApiClient._build_api_error(payload, label, status_code=200)

    @staticmethod
    def _decode_json(raw_body: bytes, label: str) -> dict[str, Any]:
        try:
            return json.loads(raw_body.decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise ValidationError(f"{label} returned non-JSON content") from exc

    @staticmethod
    def _build_api_error(payload: dict[str, Any], label: str, *, status_code: int) -> ValidationError:
        error_payload = payload.get("error")
        if isinstance(error_payload, dict):
            return ApiRequestError(
                label,
                status_code=status_code,
                code=str(error_payload.get("code") or "unknown_error"),
                message=str(error_payload.get("message") or "Request failed."),
                details=error_payload.get("details"),
                retryable=bool(error_payload.get("retryable", False)),
            )
        return ValidationError(f"{label} failed: {json.dumps(payload, ensure_ascii=False)}")


def run_with_deadline_budget(client: ApiClient, operation: Callable[[], Any], deadline: float | None) -> Any:
    original_timeout = client.timeout
    original_retries = client.retries

    try:
        if deadline is not None:
            remaining = remaining_seconds(deadline)
            if remaining is None or remaining <= 0:
                raise DeadlineExceeded("overall deadline exceeded")

            client.timeout = max(0.1, min(original_timeout, remaining))
            client.retries = 0

        return operation()
    finally:
        client.timeout = original_timeout
        client.retries = original_retries


def has_text(value: Any) -> bool:
    return bool(str(value or "").strip())


def to_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def add_missing_action_failure(failures: list[str], action_set: set[str], action_name: str, reason: str) -> None:
    if action_name not in action_set:
        failures.append(f"missing action '{action_name}': {reason}")


def add_forbidden_action_failure(failures: list[str], action_set: set[str], action_name: str, reason: str) -> None:
    if action_name in action_set:
        failures.append(f"unexpected action '{action_name}': {reason}")


def test_player_summaries(
    failures: list[str],
    players: list[dict[str, Any]],
    label: str,
    *,
    expected_count: int | None = None,
) -> None:
    if expected_count is not None and len(players) != expected_count:
        failures.append(f"{label} count should be {expected_count} but was {len(players)}")

    if not players:
        failures.append(f"{label} should not be empty when the payload exists")
        return

    local_players = [player for player in players if player.get("is_local")]
    if len(local_players) != 1:
        failures.append(f"{label} should contain exactly one local player entry")

    player_ids = [str(player.get("player_id")).strip() for player in players if has_text(player.get("player_id"))]
    if len(player_ids) != len(players):
        failures.append(f"{label} entries must expose non-empty player_id values")
    elif len(set(player_ids)) != len(player_ids):
        failures.append(f"{label} player_id values must be unique")


def test_indexed_target_contract(
    failures: list[str],
    payload: dict[str, Any],
    label: str,
    *,
    enemy_count: int,
    player_count: int,
    should_have_targets_when_usable: bool,
) -> None:
    scope = str(payload.get("target_index_space") or "")
    indices = [to_int(index) for index in list(payload.get("valid_target_indices") or []) if index is not None]

    if payload.get("requires_target"):
        if not scope:
            failures.append(f"{label} requires_target=true but target_index_space is missing")
            return

        if len(set(indices)) != len(indices):
            failures.append(f"{label} valid_target_indices must not contain duplicates")

        if scope == "enemies":
            if any(index < 0 or index >= enemy_count for index in indices):
                failures.append(f"{label} valid_target_indices contains an out-of-range combat.enemies[] index")
        elif scope == "players":
            if any(index < 0 or index >= player_count for index in indices):
                failures.append(f"{label} valid_target_indices contains an out-of-range combat.players[] index")
        else:
            failures.append(f"{label} target_index_space should be 'enemies' or 'players'")

        if should_have_targets_when_usable and not indices:
            failures.append(f"{label} requires_target=true but valid_target_indices is empty")
    else:
        if scope:
            failures.append(f"{label} requires_target=false but target_index_space is populated")
        if indices:
            failures.append(f"{label} requires_target=false but valid_target_indices is populated")


def extract_action_name_set(actions: list[dict[str, Any]]) -> set[str]:
    return {
        str(action.get("name"))
        for action in actions
        if isinstance(action, dict) and has_text(action.get("name"))
    }


def get_invariant_snapshot(
    client: ApiClient,
    *,
    attempts: int = 3,
    delay_ms: int = 50,
    deadline: float | None = None,
) -> tuple[dict[str, Any], list[dict[str, Any]], set[str], set[str]]:
    last_state: dict[str, Any] | None = None
    last_actions: list[dict[str, Any]] = []
    last_action_set: set[str] = set()
    last_state_action_set: set[str] = set()
    last_error: ApiRequestError | None = None

    for attempt in range(attempts):
        try:
            state_before = run_with_deadline_budget(client, client.get_state, deadline)
            actions_payload = run_with_deadline_budget(client, client.get_available_actions_payload, deadline)
        except DeadlineExceeded:
            break
        except ApiRequestError as exc:
            if not exc.retryable:
                raise
            last_error = exc
            if attempt < attempts - 1:
                sleep_with_deadline(delay_ms, deadline)
            continue

        actions = list(actions_payload.get("actions") or [])
        action_set = extract_action_name_set(actions)
        before_action_set = {str(action_name) for action_name in list(state_before.get("available_actions") or []) if has_text(action_name)}
        actions_screen = str(actions_payload.get("screen") or "")

        if action_set == before_action_set and state_before.get("screen") == actions_screen:
            return state_before, actions, action_set, before_action_set

        try:
            state_after = run_with_deadline_budget(client, client.get_state, deadline)
        except DeadlineExceeded:
            break
        except ApiRequestError as exc:
            if not exc.retryable:
                raise
            last_error = exc
            if attempt < attempts - 1:
                sleep_with_deadline(delay_ms, deadline)
            continue

        after_action_set = {
            str(action_name) for action_name in list(state_after.get("available_actions") or []) if has_text(action_name)
        }
        if action_set == after_action_set and state_after.get("screen") == actions_screen:
            return state_after, actions, action_set, after_action_set

        last_state = state_after
        last_actions = actions
        last_action_set = action_set
        last_state_action_set = after_action_set

        if attempt < attempts - 1:
            sleep_with_deadline(delay_ms, deadline)

    if last_state is None:
        if last_error is not None:
            raise ValidationError(f"Timed out waiting for a readable invariant snapshot. Last retryable error: {last_error}")
        last_state = run_with_deadline_budget(client, client.get_state, deadline)
        last_state_action_set = {str(action_name) for action_name in list(last_state.get("available_actions") or []) if has_text(action_name)}

    try:
        final_state = run_with_deadline_budget(client, client.get_state, deadline)
        final_actions_payload = run_with_deadline_budget(client, client.get_available_actions_payload, deadline)
        final_actions = list(final_actions_payload.get("actions") or [])
        final_action_set = extract_action_name_set(final_actions)
        final_state_action_set = {str(action_name) for action_name in list(final_state.get("available_actions") or []) if has_text(action_name)}
        return final_state, final_actions, final_action_set, final_state_action_set
    except (ApiRequestError, DeadlineExceeded):
        pass

    return last_state, last_actions, last_action_set, last_state_action_set


def evaluate_state_invariants(client: ApiClient) -> dict[str, Any]:
    state, actions, action_set, state_action_set = get_invariant_snapshot(client)

    failures: list[str] = []
    warnings: list[str] = []
    session = state.get("session") or {}
    session_mode = str(session.get("mode") or "")
    session_phase = str(session.get("phase") or "")
    control_scope = str(session.get("control_scope") or "")

    if not session:
        failures.append("state.session should always be populated")
    else:
        if session_mode not in {"singleplayer", "multiplayer"}:
            failures.append("state.session.mode should be 'singleplayer' or 'multiplayer'")
        if session_phase not in {"menu", "character_select", "multiplayer_lobby", "run"}:
            failures.append("state.session.phase should be one of menu, character_select, multiplayer_lobby, run")
        if control_scope != "local_player":
            failures.append("state.session.control_scope should stay 'local_player'")

    for action_name in state_action_set:
        if action_name not in action_set:
            failures.append(f"state.available_actions contains '{action_name}' but /actions/available does not")

    for action_name in action_set:
        if action_name not in state_action_set:
            failures.append(f"/actions/available contains '{action_name}' but state.available_actions does not")

    selection = state.get("selection")
    if selection is not None and list(selection.get("cards") or []):
        add_missing_action_failure(failures, action_set, "select_deck_card", "selection.cards[] is populated")
        add_forbidden_action_failure(
            failures,
            action_set,
            "proceed",
            "card selection should not expose proceed while selection.cards[] is populated",
        )
        if state.get("screen") != "CARD_SELECTION":
            failures.append(
                f"selection.cards[] is populated but state.screen is '{state.get('screen')}' instead of 'CARD_SELECTION'"
            )

    if selection is not None:
        min_select = to_int(selection.get("min_select"), 0)
        max_select = to_int(selection.get("max_select"), 0)
        selected_count = to_int(selection.get("selected_count"), 0)
        requires_confirmation = bool(selection.get("requires_confirmation"))
        can_confirm = bool(selection.get("can_confirm"))

        if max_select < min_select:
            failures.append("selection.max_select should be >= selection.min_select")
        if selected_count < 0:
            failures.append("selection.selected_count should never be negative")
        if selected_count > max_select:
            failures.append("selection.selected_count should never exceed selection.max_select")
        if can_confirm and not requires_confirmation:
            failures.append("selection.can_confirm should only be true when selection.requires_confirmation is true")
        if requires_confirmation and can_confirm and selected_count < min_select:
            failures.append(
                "selection.can_confirm should stay false until selection.selected_count reaches selection.min_select"
            )

        if requires_confirmation:
            if can_confirm:
                add_missing_action_failure(
                    failures,
                    action_set,
                    "confirm_selection",
                    "selection.requires_confirmation=true and selection.can_confirm=true",
                )
            else:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "confirm_selection",
                    "selection.requires_confirmation=true but selection.can_confirm=false",
                )
        else:
            add_forbidden_action_failure(
                failures,
                action_set,
                "confirm_selection",
                "selection does not require manual confirmation",
            )
    else:
        add_forbidden_action_failure(failures, action_set, "confirm_selection", "selection payload is absent")

    reward = state.get("reward")
    if reward is not None:
        add_missing_action_failure(failures, action_set, "collect_rewards_and_proceed", "reward payload is present")
        add_forbidden_action_failure(
            failures,
            action_set,
            "proceed",
            "reward flows should use reward-specific actions instead of proceed",
        )

        if reward.get("pending_card_choice"):
            if list(reward.get("card_options") or []):
                add_missing_action_failure(failures, action_set, "choose_reward_card", "reward.card_options[] is populated")
            if list(reward.get("alternatives") or []):
                add_missing_action_failure(failures, action_set, "skip_reward_cards", "reward.alternatives[] is populated")
        elif any(item.get("claimable") for item in list(reward.get("rewards") or [])):
            add_missing_action_failure(
                failures,
                action_set,
                "claim_reward",
                "reward.rewards[] still contains claimable items",
            )

    map_payload = state.get("map")
    if map_payload is not None and list(map_payload.get("available_nodes") or []):
        add_missing_action_failure(failures, action_set, "choose_map_node", "map.available_nodes[] is populated")
    elif map_payload is not None:
        add_forbidden_action_failure(failures, action_set, "choose_map_node", "map.available_nodes[] is empty")

    chest = state.get("chest")
    if chest is not None:
        if not chest.get("is_opened"):
            add_missing_action_failure(failures, action_set, "open_chest", "chest is present and not yet opened")
        if list(chest.get("relic_options") or []) and not chest.get("has_relic_been_claimed"):
            add_missing_action_failure(
                failures,
                action_set,
                "choose_treasure_relic",
                "chest.relic_options[] is populated",
            )
        if "proceed" in action_set and not chest.get("has_relic_been_claimed"):
            failures.append("chest.has_relic_been_claimed should be true before proceed is exposed")
        if chest.get("has_relic_been_claimed"):
            add_forbidden_action_failure(
                failures,
                action_set,
                "choose_treasure_relic",
                "chest relic has already been claimed",
            )

    event = state.get("event")
    if event is not None:
        if any(not option.get("is_locked") for option in list(event.get("options") or [])):
            add_missing_action_failure(failures, action_set, "choose_event_option", "event has unlocked options")

        add_forbidden_action_failure(
            failures,
            action_set,
            "proceed",
            "event flows should use choose_event_option, including finished synthetic proceed",
        )

        proceed_options = [option for option in list(event.get("options") or []) if option.get("is_proceed")]
        if event.get("is_finished"):
            if len(list(event.get("options") or [])) != 1:
                failures.append("finished events should only expose one synthetic proceed option")
            if len(proceed_options) != 1:
                failures.append("finished events should expose exactly one synthetic proceed option")
        elif proceed_options:
            failures.append("unfinished events should not expose synthetic proceed options")

    rest = state.get("rest")
    if rest is not None and any(option.get("is_enabled") for option in list(rest.get("options") or [])):
        add_missing_action_failure(failures, action_set, "choose_rest_option", "rest.options[] has enabled entries")

    shop = state.get("shop")
    if shop is not None:
        if shop.get("can_open"):
            add_missing_action_failure(failures, action_set, "open_shop_inventory", "shop.can_open=true")
        else:
            add_forbidden_action_failure(failures, action_set, "open_shop_inventory", "shop.can_open=false")

        if shop.get("can_close"):
            add_missing_action_failure(failures, action_set, "close_shop_inventory", "shop.can_close=true")
        else:
            add_forbidden_action_failure(failures, action_set, "close_shop_inventory", "shop.can_close=false")

        if shop.get("is_open"):
            add_forbidden_action_failure(failures, action_set, "proceed", "open shop inventory should not expose proceed")

            if any(item.get("is_stocked") and item.get("enough_gold") for item in list(shop.get("cards") or [])):
                add_missing_action_failure(
                    failures,
                    action_set,
                    "buy_card",
                    "shop.is_open=true and shop.cards[] has purchasable entries",
                )
            else:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "buy_card",
                    "shop.is_open=true but no shop.cards[] entries are purchasable",
                )

            if any(item.get("is_stocked") and item.get("enough_gold") for item in list(shop.get("relics") or [])):
                add_missing_action_failure(
                    failures,
                    action_set,
                    "buy_relic",
                    "shop.is_open=true and shop.relics[] has purchasable entries",
                )
            else:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "buy_relic",
                    "shop.is_open=true but no shop.relics[] entries are purchasable",
                )

            if any(item.get("is_stocked") and item.get("enough_gold") for item in list(shop.get("potions") or [])):
                add_missing_action_failure(
                    failures,
                    action_set,
                    "buy_potion",
                    "shop.is_open=true and shop.potions[] has purchasable entries",
                )
            else:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "buy_potion",
                    "shop.is_open=true but no shop.potions[] entries are purchasable",
                )

            card_removal = shop.get("card_removal") or {}
            if (
                card_removal
                and card_removal.get("available")
                and card_removal.get("enough_gold")
                and not card_removal.get("used")
            ):
                add_missing_action_failure(
                    failures,
                    action_set,
                    "remove_card_at_shop",
                    "shop.is_open=true and shop.card_removal is available and affordable",
                )
            else:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "remove_card_at_shop",
                    "shop.is_open=true but shop.card_removal is not currently purchasable",
                )
        else:
            add_forbidden_action_failure(failures, action_set, "buy_card", "shop inventory is closed")
            add_forbidden_action_failure(failures, action_set, "buy_relic", "shop inventory is closed")
            add_forbidden_action_failure(failures, action_set, "buy_potion", "shop inventory is closed")
            add_forbidden_action_failure(failures, action_set, "remove_card_at_shop", "shop inventory is closed")

    character_select = state.get("character_select")
    if character_select is not None:
        if session_phase != "character_select":
            failures.append(
                f"state.character_select is populated but state.session.phase is '{session_phase}' instead of 'character_select'"
            )

        expected_character_select_mode = "multiplayer" if character_select.get("is_multiplayer") else "singleplayer"
        if session_mode != expected_character_select_mode:
            failures.append("state.session.mode should match character_select.is_multiplayer")

        if any(not item.get("is_locked") for item in list(character_select.get("characters") or [])):
            add_missing_action_failure(failures, action_set, "select_character", "character_select has unlocked choices")
        if character_select.get("can_embark"):
            add_missing_action_failure(failures, action_set, "embark", "character_select.can_embark=true")
        if character_select.get("can_unready"):
            add_missing_action_failure(failures, action_set, "unready", "character_select.can_unready=true")
        else:
            add_forbidden_action_failure(failures, action_set, "unready", "character_select.can_unready=false")
        if character_select.get("can_increase_ascension"):
            add_missing_action_failure(
                failures,
                action_set,
                "increase_ascension",
                "character_select.can_increase_ascension=true",
            )
        else:
            add_forbidden_action_failure(
                failures,
                action_set,
                "increase_ascension",
                "character_select.can_increase_ascension=false",
            )
        if character_select.get("can_decrease_ascension"):
            add_missing_action_failure(
                failures,
                action_set,
                "decrease_ascension",
                "character_select.can_decrease_ascension=true",
            )
        else:
            add_forbidden_action_failure(
                failures,
                action_set,
                "decrease_ascension",
                "character_select.can_decrease_ascension=false",
            )

        if to_int(character_select.get("max_players"), 0) > 0 and to_int(character_select.get("player_count"), 0) > to_int(
            character_select.get("max_players"),
            0,
        ):
            failures.append("character_select.player_count should not exceed character_select.max_players")

        character_players = list(character_select.get("players") or [])
        test_player_summaries(
            failures,
            character_players,
            "character_select.players",
            expected_count=to_int(character_select.get("player_count"), 0),
        )

        local_character_players = [player for player in character_players if player.get("is_local")]
        if len(local_character_players) == 1 and bool(character_select.get("local_ready")) != bool(
            local_character_players[0].get("is_ready")
        ):
            failures.append("character_select.local_ready should match the local player roster entry")

    multiplayer_lobby = state.get("multiplayer_lobby")
    if multiplayer_lobby is not None:
        if session_mode != "multiplayer":
            failures.append("multiplayer_lobby payload is populated but state.session.mode is not 'multiplayer'")
        if session_phase != "multiplayer_lobby":
            failures.append(
                f"multiplayer_lobby payload is populated but state.session.phase is '{session_phase}' instead of 'multiplayer_lobby'"
            )
        if state.get("screen") != "MULTIPLAYER_LOBBY":
            failures.append(
                f"multiplayer_lobby payload is populated but state.screen is '{state.get('screen')}' instead of 'MULTIPLAYER_LOBBY'"
            )
        if not has_text(multiplayer_lobby.get("net_game_type")):
            failures.append("multiplayer_lobby.net_game_type should be populated")
        if to_int(multiplayer_lobby.get("join_port"), 0) <= 0:
            failures.append("multiplayer_lobby.join_port should be a positive integer")
        if not list(multiplayer_lobby.get("characters") or []):
            failures.append("multiplayer_lobby.characters should not be empty")

        if multiplayer_lobby.get("has_lobby"):
            players = list(multiplayer_lobby.get("players") or [])
            if to_int(multiplayer_lobby.get("player_count"), 0) != len(players):
                failures.append("multiplayer_lobby.player_count should match multiplayer_lobby.players.Count when has_lobby=true")
            if to_int(multiplayer_lobby.get("max_players"), 0) > 0 and to_int(multiplayer_lobby.get("player_count"), 0) > to_int(
                multiplayer_lobby.get("max_players"),
                0,
            ):
                failures.append("multiplayer_lobby.player_count should not exceed multiplayer_lobby.max_players")
            if players:
                test_player_summaries(
                    failures,
                    players,
                    "multiplayer_lobby.players",
                    expected_count=to_int(multiplayer_lobby.get("player_count"), 0),
                )

            local_lobby_players = [player for player in players if player.get("is_local")]
            if len(local_lobby_players) == 1 and bool(multiplayer_lobby.get("local_ready")) != bool(
                local_lobby_players[0].get("is_ready")
            ):
                failures.append("multiplayer_lobby.local_ready should match the local multiplayer_lobby.players entry")

            add_missing_action_failure(failures, action_set, "select_character", "multiplayer_lobby has active character options")

            if multiplayer_lobby.get("can_ready"):
                add_missing_action_failure(failures, action_set, "ready_multiplayer_lobby", "multiplayer_lobby.can_ready=true")
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "unready",
                    "multiplayer_lobby.can_ready=true should suppress unready",
                )
            elif multiplayer_lobby.get("can_unready"):
                add_missing_action_failure(failures, action_set, "unready", "multiplayer_lobby.can_unready=true")
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "ready_multiplayer_lobby",
                    "multiplayer_lobby.can_unready=true should suppress ready_multiplayer_lobby",
                )
            else:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "ready_multiplayer_lobby",
                    "multiplayer_lobby cannot ready right now",
                )

            if multiplayer_lobby.get("can_disconnect"):
                add_missing_action_failure(
                    failures,
                    action_set,
                    "disconnect_multiplayer_lobby",
                    "multiplayer_lobby.can_disconnect=true",
                )
            else:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "disconnect_multiplayer_lobby",
                    "multiplayer_lobby.can_disconnect=false",
                )

            add_forbidden_action_failure(
                failures,
                action_set,
                "host_multiplayer_lobby",
                "multiplayer_lobby.has_lobby=true should suppress host action",
            )
            add_forbidden_action_failure(
                failures,
                action_set,
                "join_multiplayer_lobby",
                "multiplayer_lobby.has_lobby=true should suppress join action",
            )
        else:
            if to_int(multiplayer_lobby.get("player_count"), 0) != 0:
                failures.append("multiplayer_lobby.player_count should be 0 when has_lobby=false")
            if list(multiplayer_lobby.get("players") or []):
                failures.append("multiplayer_lobby.players should be empty when has_lobby=false")
            if multiplayer_lobby.get("can_host"):
                add_missing_action_failure(failures, action_set, "host_multiplayer_lobby", "multiplayer_lobby.can_host=true")
            if multiplayer_lobby.get("can_join"):
                add_missing_action_failure(failures, action_set, "join_multiplayer_lobby", "multiplayer_lobby.can_join=true")

            add_forbidden_action_failure(failures, action_set, "ready_multiplayer_lobby", "multiplayer_lobby.has_lobby=false")
            add_forbidden_action_failure(
                failures,
                action_set,
                "disconnect_multiplayer_lobby",
                "multiplayer_lobby.has_lobby=false",
            )
            add_forbidden_action_failure(failures, action_set, "unready", "multiplayer_lobby.has_lobby=false")
            add_forbidden_action_failure(failures, action_set, "select_character", "multiplayer_lobby.has_lobby=false")
    else:
        add_forbidden_action_failure(failures, action_set, "host_multiplayer_lobby", "multiplayer_lobby payload is absent")
        add_forbidden_action_failure(failures, action_set, "join_multiplayer_lobby", "multiplayer_lobby payload is absent")
        add_forbidden_action_failure(
            failures,
            action_set,
            "ready_multiplayer_lobby",
            "multiplayer_lobby payload is absent",
        )
        add_forbidden_action_failure(
            failures,
            action_set,
            "disconnect_multiplayer_lobby",
            "multiplayer_lobby payload is absent",
        )

    timeline = state.get("timeline")
    if timeline is not None:
        if timeline.get("can_choose_epoch"):
            add_missing_action_failure(failures, action_set, "choose_timeline_epoch", "timeline.can_choose_epoch=true")
        if timeline.get("can_confirm_overlay"):
            add_missing_action_failure(
                failures,
                action_set,
                "confirm_timeline_overlay",
                "timeline.can_confirm_overlay=true",
            )
        if timeline.get("back_enabled"):
            add_missing_action_failure(failures, action_set, "close_main_menu_submenu", "timeline.back_enabled=true")

    modal = state.get("modal")
    if modal is not None:
        if modal.get("can_confirm"):
            add_missing_action_failure(failures, action_set, "confirm_modal", "modal.can_confirm=true")
        if modal.get("can_dismiss"):
            add_missing_action_failure(failures, action_set, "dismiss_modal", "modal.can_dismiss=true")

    game_over = state.get("game_over")
    if game_over is not None and game_over.get("can_return_to_main_menu"):
        add_missing_action_failure(
            failures,
            action_set,
            "return_to_main_menu",
            "game_over.can_return_to_main_menu=true",
        )

    combat = state.get("combat") or {}
    run_payload = state.get("run") or {}
    run_potions = list(run_payload.get("potions") or [])

    if state.get("in_combat") and state.get("combat") is not None:
        combat_selection_active = state.get("screen") == "CARD_SELECTION" and selection is not None
        combat_enemies = list(combat.get("enemies") or [])
        combat_players = list(combat.get("players") or [])
        combat_enemy_count = len(combat_enemies)
        combat_player_count = len(combat_players)

        for enemy in combat_enemies:
            if enemy is None:
                continue

            if has_text(enemy.get("intent")) and has_text(enemy.get("move_id")) and str(enemy.get("intent")) != str(
                enemy.get("move_id")
            ):
                failures.append(
                    f"combat.enemies[{to_int(enemy.get('index'), 0)}] intent should stay aligned with move_id for backward compatibility"
                )

            intents = enemy.get("intents")
            if intents is None:
                failures.append(f"combat.enemies[{to_int(enemy.get('index'), 0)}] should expose intents[]")
                continue

            for intent in list(intents):
                if intent is None:
                    continue

                intent_type = str(intent.get("intent_type") or "")
                if not intent_type:
                    failures.append(
                        f"combat.enemies[{to_int(enemy.get('index'), 0)}].intents[] entries must expose intent_type"
                    )
                    continue

                if intent_type in {"Attack", "DeathBlow"}:
                    damage = intent.get("damage")
                    hits = intent.get("hits")
                    total_damage = intent.get("total_damage")
                    if damage is None or hits is None or total_damage is None:
                        failures.append(
                            f"combat.enemies[{to_int(enemy.get('index'), 0)}].intents[] attack payloads must expose damage, hits, and total_damage"
                        )
                    else:
                        damage_value = to_int(damage)
                        hits_value = to_int(hits)
                        total_damage_value = to_int(total_damage)
                        if damage_value < 0:
                            failures.append(
                                f"combat.enemies[{to_int(enemy.get('index'), 0)}].intents[] attack damage must not be negative"
                            )
                        if hits_value < 1:
                            failures.append(
                                f"combat.enemies[{to_int(enemy.get('index'), 0)}].intents[] attack hits must be at least 1"
                            )
                        if total_damage_value != damage_value * hits_value:
                            failures.append(
                                f"combat.enemies[{to_int(enemy.get('index'), 0)}].intents[] attack total_damage must equal damage * hits"
                            )

                if intent_type == "StatusCard" and to_int(intent.get("status_card_count"), 0) <= 0:
                    failures.append(
                        f"combat.enemies[{to_int(enemy.get('index'), 0)}].intents[] StatusCard payloads must expose a positive status_card_count"
                    )

        if combat_players:
            test_player_summaries(failures, combat_players, "combat.players")

        hand = list(combat.get("hand") or [])
        if any(card.get("playable") for card in hand):
            if combat_selection_active:
                add_forbidden_action_failure(
                    failures,
                    action_set,
                    "play_card",
                    "combat card-selection overlay should suspend play_card",
                )
            else:
                add_missing_action_failure(failures, action_set, "play_card", "combat.hand[] has playable cards")
        elif not combat_selection_active:
            add_forbidden_action_failure(failures, action_set, "play_card", "combat.hand[] has no playable cards")

        if combat_selection_active:
            add_forbidden_action_failure(
                failures,
                action_set,
                "end_turn",
                "combat card-selection overlay should suspend end_turn",
            )

        combat_player = combat.get("player")
        if combat_player is not None:
            orbs = list(combat_player.get("orbs") or [])
            orb_count = len(orbs)
            orb_capacity = to_int(combat_player.get("orb_capacity"), 0)
            empty_orb_slots = to_int(combat_player.get("empty_orb_slots"), 0)

            if orb_capacity < 0:
                failures.append("combat.player.orb_capacity should never be negative")
            if orb_count > orb_capacity:
                failures.append("combat.player.orbs[] count exceeds combat.player.orb_capacity")
            if empty_orb_slots != orb_capacity - orb_count:
                failures.append("combat.player.empty_orb_slots does not match orb_capacity - orbs.Count")

            expected_slot_index = 0
            for orb in orbs:
                if orb.get("slot_index") != expected_slot_index:
                    failures.append("combat.player.orbs[] slot_index values must stay contiguous and zero-based")
                    break
                if not has_text(orb.get("orb_id")):
                    failures.append("combat.player.orbs[] entries must expose orb_id")
                    break
                expected_slot_index += 1

        local_combat_players = [player for player in combat_players if player.get("is_local")]
        local_combat_player_index = to_int(local_combat_players[0].get("slot_index"), -1) if len(local_combat_players) == 1 else -1

        for card in hand:
            if card is None:
                continue

            test_indexed_target_contract(
                failures,
                card,
                f"combat.hand[{to_int(card.get('index'), 0)}]",
                enemy_count=combat_enemy_count,
                player_count=combat_player_count,
                should_have_targets_when_usable=bool(card.get("playable")),
            )

            if card.get("target_type") == "AnyEnemy" and not card.get("requires_target"):
                failures.append(
                    f"combat.hand[{to_int(card.get('index'), 0)}] should require target_index when target_type=AnyEnemy"
                )

            if card.get("target_type") == "AnyAlly":
                if not card.get("requires_target"):
                    failures.append(
                        f"combat.hand[{to_int(card.get('index'), 0)}] should require target_index when target_type=AnyAlly"
                    )
                if card.get("target_index_space") != "players":
                    failures.append(
                        f"combat.hand[{to_int(card.get('index'), 0)}] should target combat.players[] when target_type=AnyAlly"
                    )

                card_target_indices = [
                    to_int(index)
                    for index in list(card.get("valid_target_indices") or [])
                    if index is not None
                ]
                if local_combat_player_index >= 0 and local_combat_player_index in card_target_indices:
                    failures.append(
                        f"combat.hand[{to_int(card.get('index'), 0)}] AnyAlly targets should not include the local player slot"
                    )

    if any(potion.get("can_use") for potion in run_potions):
        add_missing_action_failure(failures, action_set, "use_potion", "run.potions[] has usable entries")
    else:
        add_forbidden_action_failure(failures, action_set, "use_potion", "run.potions[] has no usable entries")

    if any(potion.get("can_discard") for potion in run_potions):
        add_missing_action_failure(failures, action_set, "discard_potion", "run.potions[] has discardable entries")
    else:
        add_forbidden_action_failure(failures, action_set, "discard_potion", "run.potions[] has no discardable entries")

    for potion in run_potions:
        if potion is None or not potion.get("occupied"):
            continue

        potion_id = str(potion.get("potion_id") or "")
        if potion.get("target_type") == "TargetedNoCreature" and potion.get("requires_target"):
            failures.append(
                f"potion '{potion_id}' should not require target_index when target_type=TargetedNoCreature"
            )
        if potion.get("target_type") == "AnyEnemy" and not potion.get("requires_target"):
            failures.append(f"potion '{potion_id}' should require target_index when target_type=AnyEnemy")

        if potion.get("requires_target"):
            test_indexed_target_contract(
                failures,
                potion,
                f"run.potions[{to_int(potion.get('index'), 0)}]",
                enemy_count=len(list(combat.get("enemies") or [])) if combat else 0,
                player_count=len(list(combat.get("players") or [])) if combat else 0,
                should_have_targets_when_usable=bool(potion.get("can_use")),
            )
        else:
            potion_target_indices = [
                to_int(index)
                for index in list(potion.get("valid_target_indices") or [])
                if index is not None
            ]
            if has_text(potion.get("target_index_space")):
                failures.append(f"potion '{potion_id}' should leave target_index_space empty when requires_target=false")
            if potion_target_indices:
                failures.append(f"potion '{potion_id}' should leave valid_target_indices empty when requires_target=false")

        if potion.get("target_type") == "AnyEnemy" and potion.get("target_index_space") != "enemies":
            failures.append(f"potion '{potion_id}' should target combat.enemies[] when target_type=AnyEnemy")
        if potion.get("target_type") == "AnyPlayer" and potion.get("requires_target") and potion.get("target_index_space") != "players":
            failures.append(
                f"potion '{potion_id}' should target combat.players[] when target_type=AnyPlayer and requires_target=true"
            )

    if state.get("run") is not None:
        if not has_text(run_payload.get("character_id")):
            failures.append("run.character_id should always be populated when run payload exists")
        if not has_text(run_payload.get("character_name")):
            failures.append("run.character_name should always be populated when run payload exists")
        if to_int(run_payload.get("base_orb_slots"), 0) < 0:
            failures.append("run.base_orb_slots should never be negative")

        run_players = list(run_payload.get("players") or [])
        if run_players:
            test_player_summaries(failures, run_players, "run.players")

    multiplayer = state.get("multiplayer")
    if multiplayer is not None:
        if session_mode != "multiplayer":
            failures.append("multiplayer payload is populated but state.session.mode is not 'multiplayer'")
        if not multiplayer.get("is_multiplayer"):
            failures.append("multiplayer payload should only be present when is_multiplayer=true")
        if not has_text(multiplayer.get("net_game_type")):
            failures.append("multiplayer.net_game_type should be populated")
        if to_int(multiplayer.get("player_count"), 0) < len(list(multiplayer.get("connected_player_ids") or [])):
            failures.append("multiplayer.connected_player_ids cannot exceed multiplayer.player_count")
        if not has_text(multiplayer.get("local_player_id")):
            failures.append("multiplayer.local_player_id should be populated")
    elif session_mode != "singleplayer" and session_phase != "multiplayer_lobby":
        failures.append("state.session.mode should only stay 'multiplayer' without multiplayer payload during the multiplayer_lobby phase")

    return {
        "screen": state.get("screen"),
        "checked_actions": len(actions),
        "failure_count": len(failures),
        "warning_count": len(warnings),
        "failures": failures,
        "warnings": warnings,
    }


def assert_state_invariants(client: ApiClient) -> dict[str, Any]:
    summary = evaluate_state_invariants(client)
    if summary["failure_count"] > 0:
        raise ValidationError(json.dumps(summary, ensure_ascii=False))
    return summary


def assert_action_available(state: dict[str, Any], action_name: str) -> None:
    if action_name not in list(state.get("available_actions") or []):
        raise ValidationError(
            f"Expected action '{action_name}' to be available, but state was: {json.dumps(state, ensure_ascii=False)}"
        )


def ensure_action_ok(response: dict[str, Any], label: str) -> dict[str, Any]:
    if not response.get("ok"):
        raise ValidationError(f"{label} failed: {json.dumps(response, ensure_ascii=False)}")
    if "data" not in response or not isinstance(response["data"], dict):
        raise ValidationError(f"{label} returned ok=true but missing data payload: {json.dumps(response, ensure_ascii=False)}")
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


def wait_for_readable_snapshot(
    client: ApiClient,
    description: str,
    *,
    attempts: int,
    delay_ms: int,
    deadline: float | None = None,
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    try:
        state, actions, _action_set, _state_action_set = get_invariant_snapshot(
            client,
            attempts=attempts,
            delay_ms=delay_ms,
            deadline=deadline,
        )
        return state, actions
    except ApiRequestError:
        raise
    except ValidationError as exc:
        raise ValidationError(f"Timed out waiting for {description}. {exc}") from exc


def suite_mod_load(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec, retries=2, retry_delay_ms=250)
    health = client.request("GET", "/health")
    client._require_ok(health, "GET /health")

    if not args.deep_check:
        return health["data"]

    startup_attempts = max(10, int(max(args.timeout_sec, 1.0) * 4))
    deadline = time.monotonic() + max(float(args.timeout_sec), 0.1)
    state = client.wait_for_state(
        "stable startup state",
        lambda current: current.get("screen") not in (None, "", "UNKNOWN")
        and len(list(current.get("available_actions") or [])) > 0,
        attempts=startup_attempts,
        delay_ms=250,
        deadline=deadline,
    )
    state, actions = wait_for_readable_snapshot(
        client,
        "stable startup state snapshot",
        attempts=max(startup_attempts, 6),
        delay_ms=250,
        deadline=deadline,
    )
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


def suite_state_invariants(args: argparse.Namespace) -> dict[str, Any]:
    client = ApiClient(base_url=args.base_url, timeout=args.timeout_sec)
    client.request("GET", "/health")
    return assert_state_invariants(client)


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
    try:
        result = api_client.action("run_console_command", command=args.command)
    except ApiRequestError as exc:
        if expected_enabled:
            raise
        result = {
            "ok": False,
            "error": {
                "code": exc.code,
                "message": exc.message,
                "details": exc.details,
                "retryable": exc.retryable,
            },
        }

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


def add_startup_argument_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--exe-path")
    parser.add_argument("--game-root")
    parser.add_argument("--app-manifest")
    parser.add_argument("--app-id")
    parser.add_argument("--skip-steam-app-id-file", action="store_true")


def repo_root() -> Path:
    return Path(__file__).resolve().parent.parent


def run_start_game_session(*args: str) -> subprocess.CompletedProcess[str]:
    script_name = "start-game-session.sh"
    script_path = repo_root() / "scripts" / script_name
    repo_dir = repo_root()
    if not script_path.is_file():
        raise ValidationError(f"Required script was not found: {script_path}")
    try:
        # nosemgrep: python.lang.security.audit.dangerous-subprocess-use-audit
        # Executes a fixed repo-local script with shell=False; caller data is passed as literal argv entries.
        completed = subprocess.run(
            ["./scripts/start-game-session.sh", *args],
            capture_output=True,
            text=True,
            check=False,
            shell=False,
            cwd=str(repo_dir),
        )
    except OSError as exc:
        raise ValidationError(f"Failed to execute {script_name}: {exc}") from exc

    if completed.returncode == 0:
        return completed

    details = "\n".join(part for part in (completed.stdout.strip(), completed.stderr.strip()) if part)
    if details:
        raise ValidationError(f"{script_name} failed.\n{details}")
    raise ValidationError(f"{script_name} failed with exit code {completed.returncode}.")


def append_startup_args(
    argv: list[str],
    *,
    exe_path: str | None = None,
    game_root: str | None = None,
    app_manifest: str | None = None,
    app_id: str | None = None,
    skip_steam_app_id_file: bool = False,
) -> None:
    if exe_path:
        argv.extend(["--exe-path", exe_path])
    if game_root:
        argv.extend(["--game-root", game_root])
    if app_manifest:
        argv.extend(["--app-manifest", app_manifest])
    if app_id:
        argv.extend(["--app-id", app_id])
    if skip_steam_app_id_file:
        argv.append("--skip-steam-app-id-file")


def start_debug_session(
    api_port: int,
    *,
    keep_existing_processes: bool,
    attempts: int,
    delay_seconds: float,
    exe_path: str | None = None,
    game_root: str | None = None,
    app_manifest: str | None = None,
    app_id: str | None = None,
    skip_steam_app_id_file: bool = False,
) -> dict[str, Any]:
    args = [
        "--enable-debug-actions",
        "--api-port",
        str(api_port),
        "--attempts",
        str(attempts),
        "--delay-seconds",
        str(delay_seconds),
    ]
    if keep_existing_processes:
        args.append("--keep-existing-processes")
    append_startup_args(
        args,
        exe_path=exe_path,
        game_root=game_root,
        app_manifest=app_manifest,
        app_id=app_id,
        skip_steam_app_id_file=skip_steam_app_id_file,
    )

    completed = run_start_game_session(*args)
    stdout = completed.stdout.strip()
    if not stdout:
        raise ValidationError(f"start-game-session.sh returned no JSON payload for port {api_port}.")

    try:
        payload = json.loads(stdout)
    except json.JSONDecodeError as exc:
        raise ValidationError(
            f"start-game-session.sh returned non-JSON output for port {api_port}: {stdout}"
        ) from exc

    if not isinstance(payload, dict):
        raise ValidationError(f"start-game-session.sh returned an unexpected payload for port {api_port}: {stdout}")
    return payload


def stop_pid(pid: Any) -> None:
    target_pid = to_int(pid, 0)
    if target_pid <= 0:
        return

    try:
        os.kill(target_pid, 0)
    except OSError:
        return

    try:
        os.kill(target_pid, signal.SIGTERM)
    except OSError:
        return

    deadline = time.monotonic() + 5.0
    while time.monotonic() < deadline:
        try:
            os.kill(target_pid, 0)
        except OSError:
            return
        time.sleep(1.0)

    try:
        os.kill(target_pid, signal.SIGKILL)
    except OSError:
        pass


def wait_for_port_release(port: int, *, attempts: int = 20, delay_ms: int = 500) -> None:
    for _ in range(attempts):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.settimeout(0.25)
            if sock.connect_ex(("127.0.0.1", port)) != 0:
                return
        time.sleep(delay_ms / 1000.0)


def choose_selectable_character(
    options: list[dict[str, Any]],
    *,
    excluded_ids: set[str] | None = None,
) -> dict[str, Any]:
    selectable = [
        candidate
        for candidate in options
        if not candidate.get("is_locked") and to_int(candidate.get("index"), -1) >= 0 and has_text(candidate.get("character_id"))
    ]
    if not selectable:
        raise ValidationError(f"Expected at least one selectable character option, but received: {json.dumps(options, ensure_ascii=False)}")

    excluded_ids = excluded_ids or set()
    for candidate in selectable:
        if str(candidate.get("character_id")) not in excluded_ids:
            return candidate

    return selectable[0]


def suite_multiplayer_lobby_flow(args: argparse.Namespace) -> dict[str, Any]:
    host_base_url = f"http://127.0.0.1:{args.host_api_port}"
    client_base_url = f"http://127.0.0.1:{args.client_api_port}"
    host_session: dict[str, Any] | None = None
    client_session: dict[str, Any] | None = None

    try:
        host_session = start_debug_session(
            args.host_api_port,
            keep_existing_processes=False,
            attempts=args.start_attempts,
            delay_seconds=args.start_delay_seconds,
            exe_path=args.exe_path,
            game_root=args.game_root,
            app_manifest=args.app_manifest,
            app_id=args.app_id,
            skip_steam_app_id_file=args.skip_steam_app_id_file,
        )
        host_client = ApiClient(
            base_url=host_base_url,
            timeout=args.timeout_sec,
            retries=args.request_retries,
            retry_delay_ms=args.retry_delay_ms,
        )
        host_client.request("GET", "/health")

        run_debug_command(host_client, "multiplayer test")
        host_open_state = host_client.wait_for_state(
            "host MULTIPLAYER_LOBBY without active lobby",
            lambda current: current.get("screen") == "MULTIPLAYER_LOBBY"
            and current.get("multiplayer_lobby") is not None
            and not current["multiplayer_lobby"].get("has_lobby"),
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        assert_state_invariants(host_client)
        assert_action_available(host_open_state, "host_multiplayer_lobby")
        assert_action_available(host_open_state, "join_multiplayer_lobby")

        ensure_action_ok(host_client.action("host_multiplayer_lobby"), "host_multiplayer_lobby")
        host_lobby_state = host_client.wait_for_state(
            "host lobby ready",
            lambda current: current.get("screen") == "MULTIPLAYER_LOBBY"
            and current.get("multiplayer_lobby") is not None
            and current["multiplayer_lobby"].get("has_lobby")
            and current["multiplayer_lobby"].get("is_host")
            and to_int(current["multiplayer_lobby"].get("player_count"), 0) == 1,
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        assert_state_invariants(host_client)

        host_lobby = host_lobby_state.get("multiplayer_lobby") or {}
        host_character = choose_selectable_character(
            list(host_lobby.get("characters") or []),
            excluded_ids={str(host_lobby.get("selected_character_id") or "")} if has_text(host_lobby.get("selected_character_id")) else None,
        )
        host_character_id = str(host_character.get("character_id"))
        ensure_action_ok(
            host_client.action("select_character", option_index=to_int(host_character.get("index"), -1)),
            f"select_character({host_character_id})",
        )
        host_client.wait_for_state(
            f"host selected {host_character_id}",
            lambda current: (current.get("multiplayer_lobby") or {}).get("selected_character_id") == host_character_id,
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        client_session = start_debug_session(
            args.client_api_port,
            keep_existing_processes=True,
            attempts=args.start_attempts,
            delay_seconds=args.start_delay_seconds,
            exe_path=args.exe_path,
            game_root=args.game_root,
            app_manifest=args.app_manifest,
            app_id=args.app_id,
            skip_steam_app_id_file=args.skip_steam_app_id_file,
        )
        client_client = ApiClient(
            base_url=client_base_url,
            timeout=args.timeout_sec,
            retries=args.request_retries,
            retry_delay_ms=args.retry_delay_ms,
        )
        client_client.request("GET", "/health")

        run_debug_command(client_client, "multiplayer test")
        client_open_state = client_client.wait_for_state(
            "client MULTIPLAYER_LOBBY without active lobby",
            lambda current: current.get("screen") == "MULTIPLAYER_LOBBY"
            and current.get("multiplayer_lobby") is not None
            and not current["multiplayer_lobby"].get("has_lobby"),
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        assert_state_invariants(client_client)
        assert_action_available(client_open_state, "join_multiplayer_lobby")

        ensure_action_ok(client_client.action("join_multiplayer_lobby"), "join_multiplayer_lobby")
        client_lobby_state = client_client.wait_for_state(
            "client joined lobby",
            lambda current: current.get("screen") == "MULTIPLAYER_LOBBY"
            and current.get("multiplayer_lobby") is not None
            and current["multiplayer_lobby"].get("has_lobby")
            and current["multiplayer_lobby"].get("is_client")
            and to_int(current["multiplayer_lobby"].get("player_count"), 0) == 2,
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )
        host_client.wait_for_state(
            "host sees second player",
            lambda current: current.get("screen") == "MULTIPLAYER_LOBBY"
            and current.get("multiplayer_lobby") is not None
            and to_int(current["multiplayer_lobby"].get("player_count"), 0) == 2,
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        assert_state_invariants(host_client)
        assert_state_invariants(client_client)

        client_lobby = client_lobby_state.get("multiplayer_lobby") or {}
        excluded_client_ids = {host_character_id}
        current_client_character_id = str(client_lobby.get("selected_character_id") or "")
        if current_client_character_id:
            excluded_client_ids.add(current_client_character_id)

        client_character = choose_selectable_character(
            list(client_lobby.get("characters") or []),
            excluded_ids=excluded_client_ids,
        )
        client_character_id = str(client_character.get("character_id"))
        ensure_action_ok(
            client_client.action("select_character", option_index=to_int(client_character.get("index"), -1)),
            f"select_character({client_character_id})",
        )
        client_client.wait_for_state(
            f"client selected {client_character_id}",
            lambda current: (current.get("multiplayer_lobby") or {}).get("selected_character_id") == client_character_id,
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )
        host_client.wait_for_state(
            f"host roster reflects {client_character_id} client",
            lambda current: len(
                [
                    player
                    for player in list((current.get("multiplayer_lobby") or {}).get("players") or [])
                    if not player.get("is_local") and player.get("character_id") == client_character_id
                ]
            )
            == 1,
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        ensure_action_ok(client_client.action("ready_multiplayer_lobby"), "ready_multiplayer_lobby(client)")
        client_client.wait_for_state(
            "client local_ready=true in lobby",
            lambda current: current.get("screen") == "MULTIPLAYER_LOBBY"
            and bool((current.get("multiplayer_lobby") or {}).get("local_ready")),
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )
        host_client.wait_for_state(
            "host sees remote ready state",
            lambda current: len(
                [
                    player
                    for player in list((current.get("multiplayer_lobby") or {}).get("players") or [])
                    if not player.get("is_local") and player.get("is_ready")
                ]
            )
            == 1,
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        assert_state_invariants(host_client)
        assert_state_invariants(client_client)

        ensure_action_ok(host_client.action("ready_multiplayer_lobby"), "ready_multiplayer_lobby(host)")
        host_run_state = host_client.wait_for_state(
            "host leaves MULTIPLAYER_LOBBY and enters multiplayer run",
            lambda current: current.get("screen") != "MULTIPLAYER_LOBBY"
            and current.get("run") is not None
            and len(list(current["run"].get("players") or [])) == 2
            and current.get("multiplayer") is not None
            and current["multiplayer"].get("is_multiplayer"),
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )
        client_run_state = client_client.wait_for_state(
            "client leaves MULTIPLAYER_LOBBY and enters multiplayer run",
            lambda current: current.get("screen") != "MULTIPLAYER_LOBBY"
            and current.get("run") is not None
            and len(list(current["run"].get("players") or [])) == 2
            and current.get("multiplayer") is not None
            and current["multiplayer"].get("is_multiplayer"),
            attempts=args.poll_attempts,
            delay_ms=args.poll_delay_ms,
        )

        assert_state_invariants(host_client)
        assert_state_invariants(client_client)

        if host_run_state.get("run_id") != client_run_state.get("run_id"):
            raise ValidationError(
                f"Expected host and client to share the same run_id, but received host={host_run_state.get('run_id')} client={client_run_state.get('run_id')}"
            )

        return {
            "host": {
                "pid": to_int((host_session or {}).get("pid"), 0),
                "base_url": host_base_url,
                "screen": host_run_state.get("screen"),
                "run_id": host_run_state.get("run_id"),
                "net_game_type": (host_run_state.get("multiplayer") or {}).get("net_game_type"),
                "player_count": len(list((host_run_state.get("run") or {}).get("players") or [])),
                "selected_character_id": host_character_id,
            },
            "client": {
                "pid": to_int((client_session or {}).get("pid"), 0),
                "base_url": client_base_url,
                "screen": client_run_state.get("screen"),
                "run_id": client_run_state.get("run_id"),
                "net_game_type": (client_run_state.get("multiplayer") or {}).get("net_game_type"),
                "player_count": len(list((client_run_state.get("run") or {}).get("players") or [])),
                "selected_character_id": client_character_id,
            },
        }
    finally:
        if not args.keep_games_running:
            seen_pids: set[int] = set()
            for session in (client_session, host_session):
                pid = to_int((session or {}).get("pid"), 0)
                if pid <= 0 or pid in seen_pids:
                    continue
                seen_pids.add(pid)
                stop_pid(pid)

            wait_for_port_release(args.client_api_port)
            wait_for_port_release(args.host_api_port)


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

    state_invariants = subparsers.add_parser("state-invariants")
    state_invariants.add_argument("--base-url", default="http://127.0.0.1:8080")
    state_invariants.add_argument("--timeout-sec", type=float, default=5.0)
    state_invariants.set_defaults(func=suite_state_invariants)

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
    main_menu.add_argument("--base-url", default="http://127.0.0.1:8080")
    main_menu.add_argument("--timeout-sec", type=float, default=5.0)
    main_menu.add_argument("--poll-attempts", type=int, default=80)
    main_menu.add_argument("--poll-delay-ms", type=int, default=200)
    main_menu.set_defaults(func=suite_main_menu_active_run)

    new_run = subparsers.add_parser("new-run-lifecycle")
    new_run.add_argument("--base-url", default="http://127.0.0.1:8080")
    new_run.add_argument("--timeout-sec", type=float, default=15.0)
    new_run.add_argument("--poll-attempts", type=int, default=120)
    new_run.add_argument("--poll-delay-ms", type=int, default=250)
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

    multiplayer_flow = subparsers.add_parser("multiplayer-lobby-flow")
    multiplayer_flow.add_argument("--host-api-port", type=int, default=8080)
    multiplayer_flow.add_argument("--client-api-port", type=int, default=8081)
    multiplayer_flow.add_argument("--timeout-sec", type=float, default=10.0)
    multiplayer_flow.add_argument("--poll-attempts", type=int, default=180)
    multiplayer_flow.add_argument("--poll-delay-ms", type=int, default=250)
    multiplayer_flow.add_argument("--request-retries", type=int, default=5)
    multiplayer_flow.add_argument("--retry-delay-ms", type=int, default=500)
    multiplayer_flow.add_argument("--start-attempts", type=int, default=40)
    multiplayer_flow.add_argument("--start-delay-seconds", type=float, default=2.0)
    multiplayer_flow.add_argument("--keep-games-running", action="store_true")
    add_startup_argument_options(multiplayer_flow)
    multiplayer_flow.set_defaults(func=suite_multiplayer_lobby_flow)

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
