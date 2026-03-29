from __future__ import annotations

import json
import os
import re
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def _env_flag(name: str, default: bool = False) -> bool:
    value = os.getenv(name, "")
    if not value:
        return default

    return value.strip().lower() in {"1", "true", "yes", "on"}


def _utc_timestamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _normalize_run_id(value: str | None) -> str | None:
    if not value:
        return None

    cleaned = re.sub(r"[^A-Za-z0-9._-]+", "_", value).strip("._-")
    return cleaned or None


def _safe_int(value: Any) -> int | None:
    return value if isinstance(value, int) else None


def _string_list(values: Any) -> list[str]:
    if not isinstance(values, list):
        return []
    return [str(value) for value in values if isinstance(value, (str, int, float))]


def _compact_power(power: Any) -> dict[str, Any] | None:
    if not isinstance(power, dict):
        return None

    return {
        "power_id": power.get("power_id"),
        "name": power.get("name"),
        "amount": power.get("amount"),
        "is_debuff": bool(power.get("is_debuff", False)),
    }


def _compact_card(card: Any) -> dict[str, Any] | None:
    if not isinstance(card, dict):
        return None

    compact = {
        "index": _safe_int(card.get("index")),
        "card_id": card.get("card_id"),
        "name": card.get("name"),
        "energy_cost": card.get("energy_cost"),
        "star_cost": card.get("star_cost"),
        "playable": card.get("playable"),
        "requires_target": bool(card.get("requires_target", False)),
        "target_index_space": card.get("target_index_space"),
        "valid_target_indices": card.get("valid_target_indices"),
        "unplayable_reason": card.get("unplayable_reason"),
    }
    return {key: value for key, value in compact.items() if value is not None}


def _compact_enemy(enemy: Any) -> dict[str, Any] | None:
    if not isinstance(enemy, dict):
        return None

    intents = enemy.get("intents") if isinstance(enemy.get("intents"), list) else []
    compact_intents: list[dict[str, Any]] = []
    for intent in intents:
        if not isinstance(intent, dict):
            continue
        compact_intent = {
            "intent_type": intent.get("intent_type"),
            "label": intent.get("label"),
            "damage": intent.get("damage"),
            "hits": intent.get("hits"),
            "total_damage": intent.get("total_damage"),
        }
        compact_intents.append({key: value for key, value in compact_intent.items() if value is not None})

    powers = enemy.get("powers") if isinstance(enemy.get("powers"), list) else []
    return {
        "index": _safe_int(enemy.get("index")),
        "enemy_id": enemy.get("enemy_id"),
        "name": enemy.get("name"),
        "current_hp": enemy.get("current_hp"),
        "max_hp": enemy.get("max_hp"),
        "block": enemy.get("block"),
        "intent": enemy.get("intent"),
        "move_id": enemy.get("move_id"),
        "powers": [power for power in (_compact_power(power) for power in powers) if power is not None],
        "intents": compact_intents,
    }


def _compact_reward_item(item: Any) -> dict[str, Any] | None:
    if not isinstance(item, dict):
        return None

    compact = {
        "index": _safe_int(item.get("index")) or _safe_int(item.get("i")),
        "reward_type": item.get("reward_type"),
        "card_id": item.get("card_id"),
        "relic_id": item.get("relic_id"),
        "potion_id": item.get("potion_id"),
        "name": item.get("name"),
        "label": item.get("label"),
        "description": item.get("description"),
        "rules_text": item.get("rules_text"),
        "claimable": item.get("claimable"),
        "upgraded": item.get("upgraded"),
    }
    return {key: value for key, value in compact.items() if value is not None}


def _compact_map_node(node: Any) -> dict[str, Any] | None:
    if not isinstance(node, dict):
        return None

    compact = {
        "index": _safe_int(node.get("index")),
        "row": _safe_int(node.get("row")),
        "col": _safe_int(node.get("col")),
        "node_type": node.get("node_type"),
        "state": node.get("state"),
    }
    return {key: value for key, value in compact.items() if value is not None}


def _compact_event_option(option: Any) -> dict[str, Any] | None:
    if not isinstance(option, dict):
        return None

    compact = {
        "index": _safe_int(option.get("index")) or _safe_int(option.get("i")),
        "option_id": option.get("option_id"),
        "label": option.get("label"),
        "description": option.get("description"),
        "line": option.get("line"),
        "disabled": option.get("disabled"),
    }
    return {key: value for key, value in compact.items() if value is not None}


class Sts2RunLogger:
    def __init__(
        self,
        knowledge_root: str | Path | None = None,
        *,
        log_root: str | Path | None = None,
        enabled: bool | None = None,
    ) -> None:
        if log_root is None:
            configured = os.getenv("STS2_AGENT_RUN_LOG_DIR", "").strip()
            if configured:
                log_root = Path(configured).expanduser().resolve()
            else:
                base_root = Path(knowledge_root).expanduser().resolve() if knowledge_root is not None else Path.cwd()
                log_root = base_root / "logs" / "runs"

        self._root_dir = Path(log_root).expanduser().resolve()
        self._enabled = _env_flag("STS2_ENABLE_RUN_LOGS", default=True) if enabled is None else enabled
        self._lock = threading.RLock()
        self._last_signatures: dict[str, str] = {}
        self._last_summaries: dict[str, dict[str, Any]] = {}
        self._started_runs: set[str] = set()
        self._current_run_id: str | None = None

    @property
    def root_dir(self) -> Path:
        return self._root_dir

    def record_state(
        self,
        state: dict[str, Any],
        *,
        reason: str,
        event: dict[str, Any] | None = None,
    ) -> bool:
        if not self._enabled or not isinstance(state, dict):
            return False

        run_id = self._resolve_run_id(state)
        if run_id is None:
            return False

        summary = self._summarize_state(state)
        signature = json.dumps(summary, ensure_ascii=False, sort_keys=True, separators=(",", ":"))

        with self._lock:
            self._current_run_id = run_id
            self._ensure_run_started_locked(run_id, summary)
            if self._last_signatures.get(run_id) == signature:
                return False

            entry = {
                "timestamp_utc": _utc_timestamp(),
                "type": "state",
                "category": self._state_category(summary),
                "reason": reason,
                "summary": summary,
            }
            compact_event = self._compact_event(event)
            if compact_event is not None:
                entry["trigger_event"] = compact_event

            self._append_locked(run_id, entry)
            self._last_signatures[run_id] = signature
            self._last_summaries[run_id] = summary
            return True

    def record_action(
        self,
        *,
        action: str,
        params: dict[str, Any] | None = None,
        response: dict[str, Any] | None = None,
        client_context: dict[str, Any] | None = None,
        error: Exception | None = None,
    ) -> bool:
        if not self._enabled:
            return False

        post_state = response.get("state") if isinstance(response, dict) and isinstance(response.get("state"), dict) else None
        run_id = self._resolve_run_id(post_state or {})
        if run_id is None:
            return False

        after_summary = self._summarize_state(post_state) if post_state is not None else None

        with self._lock:
            self._current_run_id = run_id
            if after_summary is not None:
                self._ensure_run_started_locked(run_id, after_summary)

            entry = {
                "timestamp_utc": _utc_timestamp(),
                "type": "action",
                "action": action,
                "params": {key: value for key, value in (params or {}).items() if value is not None},
            }
            if client_context:
                entry["client_context"] = client_context

            before_summary = self._last_summaries.get(run_id)
            if before_summary is not None:
                entry["before_state"] = before_summary
            if after_summary is not None:
                entry["after_state"] = after_summary

            if error is not None:
                entry["status"] = "error"
                entry["error"] = {
                    "type": type(error).__name__,
                    "message": str(error),
                }
            elif isinstance(response, dict):
                entry["status"] = response.get("status") or "completed"
                if "stable" in response:
                    entry["stable"] = bool(response.get("stable"))
                if response.get("message"):
                    entry["message"] = response.get("message")
            else:
                entry["status"] = "completed"

            self._append_locked(run_id, entry)
            return True

    def record_action_state(
        self,
        action: str,
        state: dict[str, Any] | None,
    ) -> bool:
        if not isinstance(state, dict):
            return False

        return self.record_state(state, reason=f"action:{action}")

    def _resolve_run_id(self, state: dict[str, Any]) -> str | None:
        run_id = _normalize_run_id(str(state.get("run_id", "")).strip()) if isinstance(state, dict) else None
        if run_id is not None:
            return run_id

        return self._current_run_id

    def _ensure_run_started_locked(self, run_id: str, summary: dict[str, Any]) -> None:
        if run_id in self._started_runs:
            return

        entry = {
            "timestamp_utc": _utc_timestamp(),
            "type": "run_started",
            "summary": {
                "screen": summary.get("screen"),
                "run": summary.get("run"),
            },
        }
        self._append_locked(run_id, entry)
        self._started_runs.add(run_id)

    def _append_locked(self, run_id: str, entry: dict[str, Any]) -> None:
        path = self._root_dir / f"{run_id}.jsonl"
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(entry, ensure_ascii=False, sort_keys=True))
            handle.write("\n")

    @staticmethod
    def _state_category(summary: dict[str, Any]) -> str:
        if summary.get("reward") is not None:
            return "reward"

        screen = str(summary.get("screen", "")).upper()
        if screen == "COMBAT":
            return "combat_turn"
        if screen == "MAP":
            return "map"
        if screen in {"EVENT", "EVENT_ROOM"}:
            return "event"
        if "SHOP" in screen:
            return "shop"
        if "REST" in screen:
            return "rest"
        return "state"

    @staticmethod
    def _compact_event(event: dict[str, Any] | None) -> dict[str, Any] | None:
        if not isinstance(event, dict):
            return None

        payload = {
            "id": event.get("id"),
            "event": event.get("event"),
        }
        data = event.get("data")
        if isinstance(data, dict):
            payload["data_type"] = data.get("type") or data.get("event")

        return {key: value for key, value in payload.items() if value is not None}

    def _summarize_state(self, state: dict[str, Any]) -> dict[str, Any]:
        summary: dict[str, Any] = {
            "state_version": _safe_int(state.get("state_version")),
            "screen": state.get("screen"),
            "turn": _safe_int(state.get("turn")),
            "available_actions": _string_list(state.get("available_actions") or state.get("actions")),
        }

        session = state.get("session")
        if isinstance(session, dict):
            summary["session"] = {
                "mode": session.get("mode"),
                "phase": session.get("phase"),
                "control_scope": session.get("control_scope"),
            }

        run = state.get("run")
        if isinstance(run, dict):
            deck = run.get("deck") if isinstance(run.get("deck"), list) else []
            relics = run.get("relics") if isinstance(run.get("relics"), list) else []
            potions = run.get("potions") if isinstance(run.get("potions"), list) else []
            summary["run"] = {
                "run_id": state.get("run_id"),
                "character_id": run.get("character_id"),
                "character_name": run.get("character_name"),
                "floor": run.get("floor"),
                "current_hp": run.get("current_hp"),
                "max_hp": run.get("max_hp"),
                "gold": run.get("gold"),
                "max_energy": run.get("max_energy"),
                "deck_size": len(deck),
                "relic_count": len(relics),
                "occupied_potion_slots": sum(1 for potion in potions if isinstance(potion, dict) and potion.get("occupied")),
            }

        combat = state.get("combat")
        if isinstance(combat, dict) and (
            isinstance(combat.get("hand"), list)
            or isinstance(combat.get("enemies"), list)
            or state.get("screen") == "COMBAT"
        ):
            player = combat.get("player") if isinstance(combat.get("player"), dict) else {}
            hand = combat.get("hand") if isinstance(combat.get("hand"), list) else []
            enemies = combat.get("enemies") if isinstance(combat.get("enemies"), list) else []
            draw = combat.get("draw") if isinstance(combat.get("draw"), list) else []
            discard = combat.get("discard") if isinstance(combat.get("discard"), list) else []
            exhaust = combat.get("exhaust") if isinstance(combat.get("exhaust"), list) else []
            powers = player.get("powers") if isinstance(player.get("powers"), list) else []
            summary["combat"] = {
                "player": {
                    "current_hp": player.get("current_hp"),
                    "max_hp": player.get("max_hp"),
                    "block": player.get("block"),
                    "energy": player.get("energy"),
                    "stars": player.get("stars"),
                    "focus": player.get("focus"),
                    "powers": [power for power in (_compact_power(power) for power in powers) if power is not None],
                },
                "hand": [card for card in (_compact_card(card) for card in hand) if card is not None],
                "draw_count": len(draw),
                "discard_count": len(discard),
                "exhaust_count": len(exhaust),
                "enemies": [enemy for enemy in (_compact_enemy(enemy) for enemy in enemies) if enemy is not None],
            }

        reward = state.get("reward")
        if isinstance(reward, dict) and reward:
            rewards = reward.get("rewards") if isinstance(reward.get("rewards"), list) else []
            card_options = reward.get("card_options") if isinstance(reward.get("card_options"), list) else []
            alternatives = reward.get("alternatives") if isinstance(reward.get("alternatives"), list) else []
            summary["reward"] = {
                "pending_card_choice": bool(reward.get("pending_card_choice", False)),
                "can_proceed": bool(reward.get("can_proceed", False)),
                "rewards": [item for item in (_compact_reward_item(item) for item in rewards) if item is not None],
                "card_options": [item for item in (_compact_reward_item(item) for item in card_options) if item is not None],
                "alternatives": [item for item in (_compact_reward_item(item) for item in alternatives) if item is not None],
            }

        map_payload = state.get("map")
        if isinstance(map_payload, dict) and map_payload:
            current_node = map_payload.get("current_node") if isinstance(map_payload.get("current_node"), dict) else None
            available_nodes = map_payload.get("available_nodes") if isinstance(map_payload.get("available_nodes"), list) else []
            summary["map"] = {
                "current_node": _compact_map_node(current_node) if current_node is not None else None,
                "available_nodes": [node for node in (_compact_map_node(node) for node in available_nodes) if node is not None],
            }

        event = state.get("event")
        if isinstance(event, dict) and event:
            options = event.get("options") if isinstance(event.get("options"), list) else []
            summary["event"] = {
                "event_id": event.get("event_id"),
                "title": event.get("title"),
                "options": [option for option in (_compact_event_option(option) for option in options) if option is not None],
            }

        shop = state.get("shop")
        if isinstance(shop, dict) and shop:
            cards = shop.get("cards") if isinstance(shop.get("cards"), list) else []
            relics = shop.get("relics") if isinstance(shop.get("relics"), list) else []
            potions = shop.get("potions") if isinstance(shop.get("potions"), list) else []
            summary["shop"] = {
                "purge_cost": shop.get("purge_cost"),
                "cards": [item for item in (_compact_reward_item(item) for item in cards) if item is not None],
                "relics": [item for item in (_compact_reward_item(item) for item in relics) if item is not None],
                "potions": [item for item in (_compact_reward_item(item) for item in potions) if item is not None],
            }

        rest = state.get("rest")
        if isinstance(rest, dict) and rest:
            options = rest.get("options") if isinstance(rest.get("options"), list) else []
            summary["rest"] = {
                "options": [option for option in (_compact_event_option(option) for option in options) if option is not None],
            }

        selection = state.get("selection")
        if isinstance(selection, dict) and selection:
            cards = selection.get("cards") if isinstance(selection.get("cards"), list) else []
            summary["selection"] = {
                "kind": selection.get("kind"),
                "prompt": selection.get("prompt"),
                "cards": [item for item in (_compact_reward_item(item) for item in cards) if item is not None],
            }

        return summary
