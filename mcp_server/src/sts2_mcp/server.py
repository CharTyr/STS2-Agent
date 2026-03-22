from __future__ import annotations

import json
import os
import threading
import time
from collections import Counter
from dataclasses import dataclass
from typing import Any, Callable

from fastmcp import FastMCP

from .client import Sts2Client
from .handoff import Sts2HandoffService
from .knowledge import Sts2KnowledgeBase

ToolHandler = Callable[..., dict[str, Any]]

JSON_FILE_EXTENSION = ".json"
JSON_FILE_EXTENSION_LENGTH = len(JSON_FILE_EXTENSION)
GAME_DATA_RELATIVE_PATH = ("..", "..", "data", "eng")
KNOWN_ITEM_ID_KEYS = ("id", "ID", "Id")
ITEM_IDS_SEPARATOR = ","

SCENE_MENU = "menu"
SCENE_COMBAT = "combat"
SCENE_SHOP = "shop"
SCENE_EVENT = "event"

COMBAT_SCREEN_KEYWORDS = ("combat",)
COMBAT_SCREEN_NAMES = {"combat_reward", "combat_victory"}
SHOP_SCREEN_KEYWORDS = ("shop", "merchant")
EVENT_SCREEN_KEYWORDS = ("event",)
EVENT_SCREEN_NAMES = {"event_room", "ancient_event"}
_AGENT_VIEW_BACKFILL_KEYS = (
    "run",
    "combat",
    "reward",
    "map",
    "shop",
    "rest",
    "event",
    "selection",
    "character_select",
    "timeline",
    "chest",
    "modal",
    "game_over",
)


@dataclass(frozen=True, slots=True)
class ActionToolSpec:
    name: str
    kind: str
    description: str


_LEGACY_ACTION_TOOLS: tuple[ActionToolSpec, ...] = (
    ActionToolSpec("end_turn", "no_args", "End the player's turn during combat."),
    ActionToolSpec("play_card", "card_target", "Play a card from the current hand."),
    ActionToolSpec("choose_map_node", "option_index", "Travel to a map node."),
    ActionToolSpec("collect_rewards_and_proceed", "no_args", "Auto-collect rewards and advance."),
    ActionToolSpec("claim_reward", "option_index", "Claim a single reward item."),
    ActionToolSpec("choose_reward_card", "option_index", "Pick a card from a reward screen."),
    ActionToolSpec("skip_reward_cards", "no_args", "Skip the current card reward."),
    ActionToolSpec("select_deck_card", "option_index", "Select a card on a deck selection screen."),
    ActionToolSpec("confirm_selection", "no_args", "Confirm the current manual card-selection overlay."),
    ActionToolSpec("open_chest", "no_args", "Open the treasure chest in the current room."),
    ActionToolSpec("choose_treasure_relic", "option_index", "Choose a relic from an opened chest."),
    ActionToolSpec("choose_event_option", "option_index", "Choose an option in the current event room."),
    ActionToolSpec("choose_rest_option", "option_index", "Choose a rest-site option."),
    ActionToolSpec("open_shop_inventory", "no_args", "Open the merchant inventory."),
    ActionToolSpec("close_shop_inventory", "no_args", "Close the merchant inventory."),
    ActionToolSpec("buy_card", "option_index", "Buy a card from the open merchant inventory."),
    ActionToolSpec("buy_relic", "option_index", "Buy a relic from the open merchant inventory."),
    ActionToolSpec("buy_potion", "option_index", "Buy a potion from the open merchant inventory."),
    ActionToolSpec("remove_card_at_shop", "no_args", "Use the merchant card-removal service."),
    ActionToolSpec("continue_run", "no_args", "Continue the current run from the main menu."),
    ActionToolSpec("abandon_run", "no_args", "Open the abandon-run confirmation from the main menu."),
    ActionToolSpec("open_character_select", "no_args", "Open the character select screen."),
    ActionToolSpec("open_timeline", "no_args", "Open the timeline screen."),
    ActionToolSpec("close_main_menu_submenu", "no_args", "Close the current main-menu submenu."),
    ActionToolSpec("choose_timeline_epoch", "option_index", "Choose a visible epoch on the timeline screen."),
    ActionToolSpec("confirm_timeline_overlay", "no_args", "Confirm the current timeline inspect or unlock overlay."),
    ActionToolSpec("select_character", "option_index", "Pick a character on the character select screen."),
    ActionToolSpec("embark", "no_args", "Start the run from character select."),
    ActionToolSpec("unready", "no_args", "Cancel local ready status in a multiplayer character-select lobby."),
    ActionToolSpec("increase_ascension", "no_args", "Increase the lobby ascension level when the local player is allowed to change it."),
    ActionToolSpec("decrease_ascension", "no_args", "Decrease the lobby ascension level when the local player is allowed to change it."),
    ActionToolSpec("use_potion", "option_target", "Use a potion from the player's belt."),
    ActionToolSpec("discard_potion", "option_index", "Discard a potion from the player's belt."),
    ActionToolSpec("confirm_modal", "no_args", "Confirm the currently open modal."),
    ActionToolSpec("dismiss_modal", "no_args", "Dismiss or cancel the currently open modal."),
    ActionToolSpec("return_to_main_menu", "no_args", "Leave the game over screen and return to the main menu."),
    ActionToolSpec("proceed", "no_args", "Click the current Proceed or Continue button."),
)


def _env_flag(name: str, default: bool = False) -> bool:
    value = os.getenv(name, "")
    if not value:
        return default

    return value.strip().lower() in {"1", "true", "yes", "on"}


def _normalize_tool_profile(tool_profile: str | None) -> str:
    value = (tool_profile or os.getenv("STS2_MCP_TOOL_PROFILE") or "guided").strip().lower()
    if value in {"full", "legacy"}:
        return "full"
    if value in {"layered", "planner", "multi-agent"}:
        return "layered"

    return "guided"


def _debug_tools_enabled() -> bool:
    return _env_flag("STS2_ENABLE_DEBUG_ACTIONS")


_GAME_DATA_CACHE: dict[str, Any] | None = None
_GAME_DATA_INDEXES: dict[str, dict[str, Any]] = {}
_GAME_DATA_CACHE_LOCK = threading.Lock()
_GAME_DATA_INDEXES_LOCK = threading.Lock()

# Default field sets per scene/context. These are used by `get_relevant_game_data` to
# minimize token usage by returning only the most relevant fields.
_SCENE_FIELD_SETS: dict[str, dict[str, list[str]]] = {
    SCENE_COMBAT: {
        "cards": [
            "id",
            "name",
            "description",
            "type",
            "rarity",
            "target",
            "cost",
            "is_x_cost",
            "star_cost",
            "is_x_star_cost",
            "damage",
            "block",
            "keywords",
            "tags",
            "vars",
            "upgrade",
        ],
        "monsters": [
            "id",
            "name",
            "type",
            "min_hp",
            "max_hp",
            "moves",
            "damage_values",
            "block_values",
        ],
        "powers": [
            "id",
            "name",
            "description",
            "type",
            "stack_type",
        ],
    },
    SCENE_SHOP: {
        "cards": [
            "id",
            "name",
            "description",
            "type",
            "rarity",
            "cost",
        ],
        "relics": [
            "id",
            "name",
            "description",
            "rarity",
            "pool",
        ],
        "potions": [
            "id",
            "name",
            "description",
            "rarity",
        ],
    },
    SCENE_EVENT: {
        "events": [
            "id",
            "name",
            "description",
            "options",
        ],
    },
}


def _get_game_data_dir() -> str:
    # Always use bundled English metadata.
    here = os.path.dirname(__file__)
    return os.path.abspath(os.path.join(here, *GAME_DATA_RELATIVE_PATH))


def _load_game_data() -> dict[str, Any]:
    global _GAME_DATA_CACHE
    if _GAME_DATA_CACHE is not None:
        return _GAME_DATA_CACHE

    with _GAME_DATA_CACHE_LOCK:
        if _GAME_DATA_CACHE is not None:
            return _GAME_DATA_CACHE

        data_dir = _get_game_data_dir()
        if not os.path.isdir(data_dir):
            raise RuntimeError(f"Game data directory not found: {data_dir!r}.")

        data: dict[str, Any] = {}
        for filename in sorted(os.listdir(data_dir)):
            path = os.path.join(data_dir, filename)
            if os.path.isdir(path):
                continue
            if not filename.lower().endswith(JSON_FILE_EXTENSION):
                continue

            key = filename[:-JSON_FILE_EXTENSION_LENGTH]
            try:
                with open(path, "r", encoding="utf-8") as f:
                    data[key] = json.load(f)
            except Exception as exc:
                raise RuntimeError(f"Failed to load game data file {path!r}: {exc}") from exc

        _GAME_DATA_CACHE = data
        return data


def _add_case_insensitive_item_id(index: dict[str, Any], item_id: str, item: Any) -> None:
    normalized = item_id.strip()
    if not normalized:
        return
    index[normalized] = item
    index[normalized.upper()] = item
    index[normalized.lower()] = item


def _ensure_game_data_index(collection: str) -> dict[str, Any]:
    """Return a map of id -> item for a collection (builds index on first use)."""
    global _GAME_DATA_INDEXES
    if collection in _GAME_DATA_INDEXES:
        return _GAME_DATA_INDEXES[collection]

    with _GAME_DATA_INDEXES_LOCK:
        if collection in _GAME_DATA_INDEXES:
            return _GAME_DATA_INDEXES[collection]

        data = _load_game_data()
        if collection not in data:
            raise KeyError(f"Unknown game data collection: {collection}")

        items = data[collection]
        if isinstance(items, dict):
            index = {}
            for raw_id, item in items.items():
                _add_case_insensitive_item_id(index=index, item_id=str(raw_id), item=item)
        elif isinstance(items, list):
            index = {}
            for item in items:
                item_id = ""
                for key in KNOWN_ITEM_ID_KEYS:
                    candidate = item.get(key)
                    if candidate:
                        item_id = str(candidate).strip()
                        break
                if not item_id:
                    continue
                _add_case_insensitive_item_id(index=index, item_id=item_id, item=item)
        else:
            raise TypeError(f"Unsupported data type for collection {collection!r}: {type(items)}")

        _GAME_DATA_INDEXES[collection] = index
        return index


def _lookup_game_data_item(index: dict[str, Any], item_id: str) -> Any:
    return index.get(item_id) or index.get(item_id.upper()) or index.get(item_id.lower())


def _build_game_data_tool_error(collection: str, exc: Exception) -> dict[str, Any]:
    if isinstance(exc, KeyError):
        available_collections = sorted(_GAME_DATA_CACHE.keys()) if _GAME_DATA_CACHE else []
        return {
            "error": {
                "type": "unknown_collection",
                "collection": collection,
                "message": str(exc),
                "available_collections": available_collections,
            }
        }

    if isinstance(exc, RuntimeError):
        return {
            "error": {
                "type": "game_data_unavailable",
                "collection": collection,
                "message": str(exc),
            }
        }

    return {
        "error": {
            "type": "invalid_game_data",
            "collection": collection,
            "message": str(exc),
        }
    }


def get_game_data_items_fields(collection: str, item_ids: str, fields: str | None) -> dict[str, Any]:
    """Return multiple items with selected top-level fields only.

    - `item_ids`: comma-separated ids.
    - `fields`: comma-separated top-level keys. Empty or `None` returns full items.
    """
    if not item_ids:
        return {}

    index = _ensure_game_data_index(collection)
    ids = [s.strip() for s in item_ids.split(ITEM_IDS_SEPARATOR) if s.strip()]
    requested_fields = [s.strip() for s in fields.split(ITEM_IDS_SEPARATOR) if s.strip()] if fields else []

    result: dict[str, Any] = {}
    for item_id in ids:
        item = _lookup_game_data_item(index=index, item_id=item_id)
        if item is None:
            result[item_id] = None
            continue

        if not requested_fields or not isinstance(item, dict):
            result[item_id] = item
            continue

        filtered = {key: item[key] for key in requested_fields if key in item}
        result[item_id] = filtered

    return result


def _register_no_arg_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool() -> dict[str, Any]:
        return handler()

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_option_index_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(option_index: int) -> dict[str, Any]:
        return handler(option_index=option_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_card_target_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(card_index: int, target_index: int | None = None) -> dict[str, Any]:
        return handler(card_index=card_index, target_index=target_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_option_target_tool(mcp: FastMCP, name: str, description: str, handler: ToolHandler) -> None:
    def tool(option_index: int, target_index: int | None = None) -> dict[str, Any]:
        return handler(option_index=option_index, target_index=target_index)

    tool.__name__ = name
    tool.__doc__ = description
    mcp.tool(name=name, description=description)(tool)


def _register_legacy_action_tools(mcp: FastMCP, sts2: Sts2Client) -> None:
    for spec in _LEGACY_ACTION_TOOLS:
        handler = getattr(sts2, spec.name)
        if spec.kind == "no_args":
            _register_no_arg_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "option_index":
            _register_option_index_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "card_target":
            _register_card_target_tool(mcp, spec.name, spec.description, handler)
            continue

        if spec.kind == "option_target":
            _register_option_target_tool(mcp, spec.name, spec.description, handler)
            continue

        raise RuntimeError(f"Unsupported action tool kind: {spec.kind}")


def _detect_scene_from_screen(screen: str) -> str:
    normalized = (screen or "").lower()
    if any(keyword in normalized for keyword in COMBAT_SCREEN_KEYWORDS) or normalized in COMBAT_SCREEN_NAMES:
        return SCENE_COMBAT
    if any(keyword in normalized for keyword in SHOP_SCREEN_KEYWORDS):
        return SCENE_SHOP
    if any(keyword in normalized for keyword in EVENT_SCREEN_KEYWORDS) or normalized in EVENT_SCREEN_NAMES:
        return SCENE_EVENT
    return SCENE_MENU


def _build_agent_state_payload(state: dict[str, Any], knowledge: Sts2KnowledgeBase) -> dict[str, Any]:
    agent_view = state.get("agent_view")
    if not isinstance(agent_view, dict):
        return state

    enriched = dict(agent_view)
    for key in _AGENT_VIEW_BACKFILL_KEYS:
        if key in enriched:
            continue
        raw_value = state.get(key)
        if isinstance(raw_value, (dict, list)):
            enriched[key] = raw_value

    if "available_actions" not in enriched and isinstance(enriched.get("actions"), list):
        enriched["available_actions"] = enriched["actions"]
    if "available_actions" not in enriched and isinstance(state.get("available_actions"), list):
        enriched["available_actions"] = state["available_actions"]

    raw_map = state.get("map")
    compact_map = enriched.get("map")
    if isinstance(raw_map, dict) and isinstance(compact_map, dict) and "route_options" not in compact_map:
        enriched_map = dict(compact_map)
        enriched_map["route_options"] = knowledge.build_route_options(state)
        enriched["map"] = enriched_map

    return enriched


_AOE_TARGETS = {"allenemies"}
_SCALING_POWER_IDS = {
    "accuracy",
    "afterimage",
    "buffer",
    "dexterity",
    "focus",
    "intangible",
    "mantra",
    "plating",
    "poison",
    "strength",
    "thorns",
    "vigor",
}
_SCALING_HINTS = (
    "at the start of your turn",
    "for the rest of combat",
    "whenever",
    "each turn",
)
_REST_NODE_TYPES = {"rest", "campfire"}
_SHOP_NODE_TYPES = {"shop", "merchant"}
_ELITE_NODE_TYPES = {"elite"}
_MONSTER_NODE_TYPES = {"monster", "enemy"}


def _safe_int(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    return None


def _normalize_token(value: Any) -> str:
    if value is None:
        return ""
    return "".join(ch for ch in str(value).lower() if ch.isalnum())


def _safe_ratio(current: int | None, maximum: int | None) -> float:
    if current is None or maximum is None or maximum <= 0:
        return 0.0
    return round(current / maximum, 3)


def _clamp(value: int, minimum: int, maximum: int) -> int:
    return max(minimum, min(maximum, value))


def _dedupe_reasons(reasons: list[str], limit: int = 5) -> list[str]:
    ordered = list(dict.fromkeys(reason for reason in reasons if reason))
    return ordered[:limit]


def _safe_collection_index(collection: str) -> dict[str, Any]:
    try:
        return _ensure_game_data_index(collection)
    except (KeyError, RuntimeError, TypeError):
        return {}


def _lookup_item_meta(index: dict[str, Any], item_id: str | None) -> dict[str, Any]:
    if not item_id or not index:
        return {}

    item = _lookup_game_data_item(index=index, item_id=item_id)
    return item if isinstance(item, dict) else {}


def _run_payload(state: dict[str, Any]) -> dict[str, Any]:
    run = state.get("run")
    return run if isinstance(run, dict) else {}


def _reward_payload(state: dict[str, Any]) -> dict[str, Any]:
    reward = state.get("reward")
    return reward if isinstance(reward, dict) else {}


def _map_payload(state: dict[str, Any]) -> dict[str, Any]:
    map_data = state.get("map")
    return map_data if isinstance(map_data, dict) else {}


def _extract_card_entries(entries: Any) -> list[dict[str, Any]]:
    if not isinstance(entries, list):
        return []

    result: list[dict[str, Any]] = []
    for position, entry in enumerate(entries):
        if not isinstance(entry, dict):
            continue

        count = _safe_int(entry.get("count")) or 1
        card_id = entry.get("card_id") or entry.get("id")
        result.append(
            {
                "index": _safe_int(entry.get("i")) or _safe_int(entry.get("index")) or position,
                "card_id": str(card_id) if card_id is not None else None,
                "name": entry.get("name"),
                "count": max(1, count),
                "energy_cost": _safe_int(entry.get("energy_cost")) if "energy_cost" in entry else _safe_int(entry.get("cost")),
                "star_cost": _safe_int(entry.get("star_cost")),
                "costs_x": bool(entry.get("costs_x") or entry.get("is_x_cost")),
                "star_costs_x": bool(entry.get("star_costs_x") or entry.get("is_x_star_cost")),
                "upgraded": bool(entry.get("upgraded")),
                "rarity": entry.get("rarity"),
                "type": entry.get("card_type") or entry.get("type"),
                "rules_text": entry.get("rules_text") or entry.get("description") or entry.get("line"),
            }
        )

    return result


def _extract_potion_count(entries: Any) -> int:
    if not isinstance(entries, list):
        return 0

    count = 0
    for entry in entries:
        if not isinstance(entry, dict):
            continue

        occupied = entry.get("occupied")
        if isinstance(occupied, bool):
            if occupied:
                count += 1
            continue

        if entry.get("potion_id") or entry.get("name"):
            count += 1

    return count


def _extract_hp_values(run: dict[str, Any]) -> tuple[int | None, int | None]:
    current_hp = _safe_int(run.get("current_hp"))
    max_hp = _safe_int(run.get("max_hp"))
    if current_hp is not None and max_hp is not None:
        return current_hp, max_hp

    hp_summary = run.get("hp")
    if not isinstance(hp_summary, str) or "/" not in hp_summary:
        return current_hp, max_hp

    current_part, max_part = hp_summary.split("/", 1)
    try:
        parsed_current = int(current_part.strip())
        parsed_max = int(max_part.strip())
    except ValueError:
        return current_hp, max_hp

    return current_hp if current_hp is not None else parsed_current, max_hp if max_hp is not None else parsed_max


def _card_profile(card: dict[str, Any], meta: dict[str, Any]) -> dict[str, Any]:
    cost = _safe_int(card.get("energy_cost"))
    if cost is None:
        cost = _safe_int(meta.get("cost"))

    damage = _safe_int(meta.get("damage")) or 0
    block = _safe_int(meta.get("block")) or 0
    hit_count = _safe_int(meta.get("hit_count")) or (1 if damage > 0 else 0)
    total_damage = damage * hit_count if damage > 0 else 0
    draw_count = _safe_int(meta.get("cards_draw")) or 0
    energy_gain = _safe_int(meta.get("energy_gain")) or 0
    hp_loss = _safe_int(meta.get("hp_loss")) or 0
    target = _normalize_token(card.get("target") or meta.get("target"))
    card_type = str(card.get("type") or meta.get("type") or "").strip()
    rarity = str(card.get("rarity") or meta.get("rarity") or "").strip()

    description_parts = [
        part
        for part in (
            card.get("rules_text"),
            meta.get("description"),
            meta.get("description_raw"),
        )
        if isinstance(part, str) and part.strip()
    ]
    description = "\n".join(description_parts).lower()

    powers = meta.get("powers_applied") if isinstance(meta.get("powers_applied"), list) else []
    power_ids = {
        _normalize_token(power.get("power"))
        for power in powers
        if isinstance(power, dict) and power.get("power")
    }

    aoe = target in _AOE_TARGETS or "all enemies" in description
    frontload = aoe or total_damage >= 8 or (target in {"anyenemy", "randomenemy"} and damage >= 7)
    block_card = block > 0 or ("gain" in description and "block" in description)
    weak = "weak" in description or "weak" in power_ids
    vulnerable = "vulnerable" in description or "vulnerable" in power_ids
    poison = "poison" in description or "poison" in power_ids
    scaling = (
        _normalize_token(card_type) == "power"
        or bool(power_ids & _SCALING_POWER_IDS)
        or poison
        or any(phrase in description for phrase in _SCALING_HINTS)
    )

    return {
        "card_id": card.get("card_id"),
        "name": card.get("name") or meta.get("name") or "Unknown Card",
        "type": card_type or None,
        "rarity": rarity or None,
        "cost": cost,
        "x_cost": bool(card.get("costs_x") or meta.get("is_x_cost")),
        "high_cost": cost is not None and cost >= 2 and not bool(card.get("costs_x") or meta.get("is_x_cost")),
        "damage": damage,
        "total_damage": total_damage,
        "block": block,
        "draw_count": draw_count,
        "energy_gain": energy_gain,
        "hp_loss": hp_loss,
        "aoe": aoe,
        "frontload": frontload,
        "block_card": block_card,
        "scaling": scaling,
        "weak": weak,
        "vulnerable": vulnerable,
        "poison": poison,
        "energy": energy_gain > 0,
    }


def _summarize_deck(state: dict[str, Any], cards_index: dict[str, Any]) -> dict[str, Any]:
    run = _run_payload(state)
    deck_entries = _extract_card_entries(run.get("deck"))
    totals: Counter[str] = Counter()
    type_counts: Counter[str] = Counter()
    card_id_counts: Counter[str] = Counter()

    for entry in deck_entries:
        count = max(1, entry.get("count", 1))
        card_id = entry.get("card_id")
        if isinstance(card_id, str) and card_id:
            card_id_counts[card_id] += count

        profile = _card_profile(entry, _lookup_item_meta(cards_index, card_id))
        type_key = _normalize_token(profile.get("type"))
        if type_key:
            type_counts[type_key] += count

        if profile["aoe"]:
            totals["aoe_cards"] += count
        if profile["frontload"]:
            totals["frontload_cards"] += count
        if profile["block_card"]:
            totals["block_cards"] += count
        if profile["scaling"]:
            totals["scaling_cards"] += count
        if profile["draw_count"] > 0:
            totals["draw_cards"] += count
        if profile["weak"]:
            totals["weak_sources"] += count
        if profile["vulnerable"]:
            totals["vulnerable_sources"] += count
        if profile["poison"]:
            totals["poison_sources"] += count
        if profile["energy"]:
            totals["energy_cards"] += count
        if profile["high_cost"]:
            totals["high_cost_cards"] += count
        if profile["hp_loss"] > 0:
            totals["hp_loss_cards"] += count

    current_hp, max_hp = _extract_hp_values(run)
    relic_items = run.get("relic_items") if isinstance(run.get("relic_items"), list) else None
    relics = run.get("relics") if isinstance(run.get("relics"), list) else None
    deck_size = sum(entry["count"] for entry in deck_entries) or _safe_int(run.get("deck_size")) or 0

    return {
        "floor": _safe_int(run.get("floor")) or 0,
        "current_hp": current_hp or 0,
        "max_hp": max_hp or 0,
        "hp_ratio": _safe_ratio(current_hp, max_hp),
        "gold": _safe_int(run.get("gold")) or 0,
        "max_energy": _safe_int(run.get("max_energy")) or 3,
        "deck_size": deck_size,
        "relic_count": len(relic_items) if relic_items is not None else len(relics or []),
        "potion_count": _extract_potion_count(run.get("potions")),
        "aoe_cards": totals["aoe_cards"],
        "frontload_cards": totals["frontload_cards"],
        "block_cards": totals["block_cards"],
        "scaling_cards": totals["scaling_cards"],
        "draw_cards": totals["draw_cards"],
        "weak_sources": totals["weak_sources"],
        "vulnerable_sources": totals["vulnerable_sources"],
        "poison_sources": totals["poison_sources"],
        "energy_cards": totals["energy_cards"],
        "high_cost_cards": totals["high_cost_cards"],
        "hp_loss_cards": totals["hp_loss_cards"],
        "type_counts": dict(type_counts),
        "card_id_counts": dict(card_id_counts),
    }


def _public_deck_summary(summary: dict[str, Any]) -> dict[str, Any]:
    return {key: value for key, value in summary.items() if key != "card_id_counts"}


def _score_reward_card(card: dict[str, Any], deck_summary: dict[str, Any], profile: dict[str, Any]) -> tuple[int, list[str]]:
    reasons: list[str] = []
    score = 50
    floor = deck_summary["floor"]
    act2_or_later = floor >= 17
    duplicate_count = deck_summary["card_id_counts"].get(card.get("card_id") or "", 0)

    if profile["aoe"]:
        bonus = 18 if deck_summary["aoe_cards"] == 0 else 10 if deck_summary["aoe_cards"] == 1 else 4
        if act2_or_later:
            bonus += 4
        score += bonus
        reasons.append("补强范围伤害")
    elif act2_or_later and deck_summary["aoe_cards"] == 0:
        score -= 6
        reasons.append("当前更缺范围伤害")

    if profile["frontload"]:
        bonus = 9 if deck_summary["frontload_cards"] < 5 else 3
        if act2_or_later and deck_summary["frontload_cards"] < 5:
            bonus += 3
        score += bonus
        reasons.append("补强前台输出")
    elif act2_or_later and profile["type"] == "Power" and deck_summary["frontload_cards"] < 4:
        score -= 6
        reasons.append("Act 2 更需要立即影响战局的牌")

    if profile["scaling"]:
        bonus = 14 if deck_summary["scaling_cards"] == 0 else 8 if deck_summary["scaling_cards"] == 1 else 3
        score += bonus
        reasons.append("补强成长与长战能力")

    if profile["block_card"]:
        if deck_summary["block_cards"] < 4:
            score += 10
            reasons.append("补强防御覆盖")
        elif deck_summary["block_cards"] >= 7:
            score -= 3
            reasons.append("牌组已有较多防御")

    if profile["draw_count"] > 0:
        if deck_summary["draw_cards"] < 2:
            score += 8
            reasons.append("补强过牌稳定性")
        elif deck_summary["draw_cards"] >= 4:
            score -= 2
            reasons.append("当前过牌已经不少")

    if profile["weak"] and deck_summary["weak_sources"] == 0:
        score += 5
        reasons.append("补弱化来源")

    if profile["vulnerable"] and deck_summary["vulnerable_sources"] == 0:
        score += 5
        reasons.append("补易伤来源")

    if profile["energy"] and deck_summary["energy_cards"] == 0:
        score += 8
        reasons.append("改善能量回转")

    if profile["x_cost"] and deck_summary["energy_cards"] == 0 and deck_summary["max_energy"] <= 3:
        score -= 5
        reasons.append("当前能量曲线不太支持 X 费")

    if profile["high_cost"] and deck_summary["high_cost_cards"] >= 4:
        score -= 8
        reasons.append("高费牌已经偏多")

    if profile["hp_loss"] > 0 and deck_summary["hp_ratio"] < 0.55:
        score -= 12
        reasons.append("当前血线偏低，不适合继续自伤")

    if duplicate_count >= 2 and _normalize_token(profile.get("type")) == "power":
        score -= 6
        reasons.append("同名 Power 已经偏多")
    elif duplicate_count >= 2:
        score -= 4
        reasons.append("同名牌已经偏多")

    rarity_bonus = {"Rare": 7, "Uncommon": 3}.get(profile.get("rarity"), 0)
    if rarity_bonus > 0:
        score += rarity_bonus
        reasons.append(f"{profile['rarity']} 牌上限更高")

    return _clamp(score, 0, 100), _dedupe_reasons(reasons)


def _evaluate_card_rewards_for_state(state: dict[str, Any]) -> dict[str, Any]:
    cards_index = _safe_collection_index("cards")
    reward = _reward_payload(state)
    reward_cards = _extract_card_entries(
        reward.get("cards") or reward.get("card_options") or reward.get("card_rewards")
    )
    deck_summary = _summarize_deck(state, cards_index)

    if not reward_cards:
        return {
            "best_index": None,
            "reward_cards": [],
            "deck_summary": _public_deck_summary(deck_summary),
            "message": "当前状态没有待选卡牌奖励。",
        }

    evaluated: list[dict[str, Any]] = []
    for entry in reward_cards:
        meta = _lookup_item_meta(cards_index, entry.get("card_id"))
        profile = _card_profile(entry, meta)
        score, reasons = _score_reward_card(entry, deck_summary, profile)
        evaluated.append(
            {
                "index": entry["index"],
                "card_id": entry.get("card_id"),
                "name": profile["name"],
                "score": score,
                "recommendation": "take" if score >= 68 else "consider" if score >= 52 else "skip",
                "reasons": reasons,
            }
        )

    best = max(evaluated, key=lambda item: (item["score"], -(item["index"] or 0)))
    return {
        "best_index": best["index"],
        "reward_cards": evaluated,
        "deck_summary": _public_deck_summary(deck_summary),
    }


def _node_type_matches(node_type: Any, targets: set[str]) -> bool:
    normalized = _normalize_token(node_type)
    if normalized in targets:
        return True

    return any(target in normalized for target in targets)


def _build_elite_route_previews(route_options: Any) -> list[dict[str, Any]]:
    if not isinstance(route_options, list):
        return []

    previews: list[dict[str, Any]] = []
    for option in route_options:
        if not isinstance(option, dict):
            continue

        start_node = option.get("start_node") if isinstance(option.get("start_node"), dict) else {}
        paths = option.get("paths") if isinstance(option.get("paths"), list) else []
        best_preview: dict[str, Any] | None = None

        for path in paths:
            if not isinstance(path, dict):
                continue

            node_types = path.get("node_types") if isinstance(path.get("node_types"), list) else []
            normalized_types = [_normalize_token(node_type) for node_type in node_types]
            elite_index = next(
                (index for index, node_type in enumerate(normalized_types) if _node_type_matches(node_type, _ELITE_NODE_TYPES)),
                None,
            )
            if elite_index is None:
                continue

            types_before_elite = normalized_types[:elite_index]
            preview = {
                "start_index": _safe_int(start_node.get("index")) or _safe_int(start_node.get("i")) or 0,
                "start_node": {
                    "row": _safe_int(start_node.get("row")),
                    "col": _safe_int(start_node.get("col")),
                    "type": start_node.get("node_type") or start_node.get("type"),
                },
                "elite_distance": elite_index + 1,
                "rest_before_elite": any(_node_type_matches(node_type, _REST_NODE_TYPES) for node_type in types_before_elite),
                "shop_before_elite": any(_node_type_matches(node_type, _SHOP_NODE_TYPES) for node_type in types_before_elite),
                "monster_before_elite": sum(
                    1 for node_type in types_before_elite if _node_type_matches(node_type, _MONSTER_NODE_TYPES)
                ),
                "elite_count": sum(
                    1 for node_type in normalized_types if _node_type_matches(node_type, _ELITE_NODE_TYPES)
                ),
                "path_types": node_types,
            }
            preview["threat_score"] = (
                preview["elite_distance"]
                + 2 * preview["elite_count"]
                - (2 if preview["rest_before_elite"] else 0)
                - (1 if preview["shop_before_elite"] else 0)
            )

            if best_preview is None or preview["threat_score"] < best_preview["threat_score"]:
                best_preview = preview

        if best_preview is not None:
            previews.append(best_preview)

    previews.sort(key=lambda preview: (preview["threat_score"], preview["elite_distance"], preview["start_index"]))
    return previews


def _assess_elite_risk_for_state(state: dict[str, Any]) -> dict[str, Any]:
    cards_index = _safe_collection_index("cards")
    deck_summary = _summarize_deck(state, cards_index)
    route_previews = _build_elite_route_previews(_map_payload(state).get("route_options"))
    best_route = route_previews[0] if route_previews else None

    risk_score = 50
    positive: list[str] = []
    negative: list[str] = []
    hp_ratio = deck_summary["hp_ratio"]
    act2_or_later = deck_summary["floor"] >= 17

    if hp_ratio < 0.35:
        risk_score += 28
        negative.append("当前血量过低")
    elif hp_ratio < 0.5:
        risk_score += 18
        negative.append("当前血线偏低")
    elif hp_ratio >= 0.75:
        risk_score -= 8
        positive.append("血量缓冲充足")
    elif hp_ratio >= 0.6:
        risk_score -= 4
        positive.append("血量尚可")

    if deck_summary["potion_count"] == 0:
        risk_score += 8
        negative.append("没有药水缓冲")
    elif deck_summary["potion_count"] >= 2:
        risk_score -= 8
        positive.append("药水储备充足")
    else:
        risk_score -= 3
        positive.append("有药水可应急")

    if deck_summary["aoe_cards"] == 0:
        risk_score += 12 if act2_or_later else 8
        negative.append("缺范围伤害")
    else:
        risk_score -= 5
        positive.append("有范围伤害处理多目标")

    if deck_summary["frontload_cards"] < 5:
        risk_score += 10
        negative.append("前台输出不足")
    elif deck_summary["frontload_cards"] >= 7:
        risk_score -= 6
        positive.append("前台输出足够")

    if deck_summary["block_cards"] < 4 and deck_summary["weak_sources"] == 0:
        risk_score += 10
        negative.append("防御覆盖偏弱")
    else:
        risk_score -= 5
        positive.append("有一定防御与减伤")

    if deck_summary["scaling_cards"] == 0:
        risk_score += 6
        negative.append("缺持续成长")
    else:
        risk_score -= 3
        positive.append("有成长手段")

    if deck_summary["draw_cards"] < 2:
        risk_score += 4
        negative.append("过牌偏少")
    elif deck_summary["draw_cards"] >= 3:
        risk_score -= 2
        positive.append("过牌稳定性尚可")

    if deck_summary["max_energy"] >= 4:
        risk_score -= 3
        positive.append("能量上限较高")

    if best_route is None:
        risk_score += 4
        negative.append("当前路线缺少精英前缓冲信息")
    else:
        if best_route["rest_before_elite"]:
            risk_score -= 10
            positive.append("存在精英前营火")
        if best_route["shop_before_elite"] and deck_summary["gold"] >= 150:
            risk_score -= 6
            positive.append("可先商店补强再打精英")
        if best_route["elite_distance"] <= 2 and not best_route["rest_before_elite"]:
            risk_score += 8
            negative.append("精英过近，缺少缓冲节点")
        if best_route["elite_count"] > 1:
            risk_score += 4
            negative.append("该分支后续精英密度偏高")

    risk_score = _clamp(risk_score, 0, 100)
    recommendation = "TAKE" if risk_score <= 40 else "CAUTION" if risk_score <= 65 else "AVOID"

    return {
        "recommendation": recommendation,
        "risk_score": risk_score,
        "hp_ratio": deck_summary["hp_ratio"],
        "deck_summary": _public_deck_summary(deck_summary),
        "best_elite_route": best_route,
        "route_preview": route_previews[:3],
        "factors": {
            "positive": _dedupe_reasons(positive),
            "negative": _dedupe_reasons(negative),
        },
    }


def _build_check(status: str, value: Any, target: str, reason: str) -> dict[str, Any]:
    return {
        "status": status,
        "value": value,
        "target": target,
        "reason": reason,
    }


def _check_boss_readiness_for_state(state: dict[str, Any]) -> dict[str, Any]:
    cards_index = _safe_collection_index("cards")
    deck_summary = _summarize_deck(state, cards_index)
    hp_ratio = deck_summary["hp_ratio"]
    current_hp = deck_summary["current_hp"]
    deck_size = deck_summary["deck_size"]
    checks = {
        "hp": _build_check(
            "pass" if hp_ratio >= 0.6 or current_hp >= 45 else "borderline" if hp_ratio >= 0.45 or current_hp >= 30 else "fail",
            current_hp,
            ">= 45 HP or >= 60% HP",
            "血线越高，Boss 战容错越高。",
        ),
        "deck_size": _build_check(
            "pass" if 10 <= deck_size <= 30 else "borderline" if 8 <= deck_size <= 34 else "fail",
            deck_size,
            "10-30 张左右",
            "过小容易缺关键功能，过大则抽不到核心牌。",
        ),
        "damage": _build_check(
            "pass"
            if deck_summary["frontload_cards"] + deck_summary["vulnerable_sources"] + deck_summary["energy_cards"] >= 6
            else "borderline"
            if deck_summary["frontload_cards"] + deck_summary["vulnerable_sources"] + deck_summary["energy_cards"] >= 4
            else "fail",
            {
                "frontload_cards": deck_summary["frontload_cards"],
                "vulnerable_sources": deck_summary["vulnerable_sources"],
                "energy_cards": deck_summary["energy_cards"],
            },
            "至少 6 点综合输出指标",
            "需要足够的单体输出把 Boss 战拖回可控节奏。",
        ),
        "block": _build_check(
            "pass"
            if deck_summary["block_cards"] + deck_summary["weak_sources"] >= 5
            else "borderline"
            if deck_summary["block_cards"] + deck_summary["weak_sources"] >= 3
            else "fail",
            {
                "block_cards": deck_summary["block_cards"],
                "weak_sources": deck_summary["weak_sources"],
            },
            "至少 5 点综合防御指标",
            "Boss 战要求稳定防御，不只是偶尔一手好牌。",
        ),
        "scaling": _build_check(
            "pass"
            if deck_summary["scaling_cards"] + deck_summary["poison_sources"] + deck_summary["energy_cards"] >= 2
            else "borderline"
            if deck_summary["scaling_cards"] + deck_summary["poison_sources"] + deck_summary["energy_cards"] >= 1
            else "fail",
            {
                "scaling_cards": deck_summary["scaling_cards"],
                "poison_sources": deck_summary["poison_sources"],
                "energy_cards": deck_summary["energy_cards"],
            },
            "至少 2 点成长指标",
            "没有成长手段时，Boss 往往会在中后段把你压死。",
        ),
        "draw": _build_check(
            "pass" if deck_summary["draw_cards"] >= 2 else "borderline" if deck_summary["draw_cards"] >= 1 else "fail",
            deck_summary["draw_cards"],
            "至少 2 张稳定过牌",
            "过牌保证关键回合能摸到防御和核心输出。",
        ),
        "vulnerable": _build_check(
            "pass"
            if deck_summary["vulnerable_sources"] >= 1
            else "borderline"
            if deck_summary["frontload_cards"] >= 7
            else "fail",
            deck_summary["vulnerable_sources"],
            "至少 1 个易伤来源或很强的前台输出",
            "易伤能明显抬高 Boss 战的伤害效率。",
        ),
        "potions": _build_check(
            "pass" if deck_summary["potion_count"] >= 2 else "borderline" if deck_summary["potion_count"] >= 1 else "fail",
            deck_summary["potion_count"],
            "至少 2 瓶可用药水",
            "药水是 Boss 战最稳定的临时补强。",
        ),
    }

    status_counts = Counter(check["status"] for check in checks.values())
    if status_counts["fail"] >= 3 or (
        checks["hp"]["status"] == "fail"
        and (checks["damage"]["status"] == "fail" or checks["block"]["status"] == "fail")
    ):
        recommendation = "NOT_READY"
    elif status_counts["fail"] == 0 and status_counts["borderline"] <= 2:
        recommendation = "READY"
    else:
        recommendation = "BORDERLINE"

    return {
        "recommendation": recommendation,
        "deck_summary": _public_deck_summary(deck_summary),
        "checks": checks,
        "passed_checks": status_counts["pass"],
        "borderline_checks": status_counts["borderline"],
        "failed_checks": status_counts["fail"],
    }


def _shop_payload(state: dict[str, Any]) -> dict[str, Any]:
    shop = state.get("shop")
    return shop if isinstance(shop, dict) else {}


def _rest_payload(state: dict[str, Any]) -> dict[str, Any]:
    rest = state.get("rest")
    return rest if isinstance(rest, dict) else {}


def _extract_shop_cards(entries: Any) -> list[dict[str, Any]]:
    cards = _extract_card_entries(entries)
    if not isinstance(entries, list):
        return cards

    for card, raw_entry in zip(cards, entries):
        if not isinstance(raw_entry, dict):
            continue
        card["price"] = _safe_int(raw_entry.get("price")) or 0
        card["affordable"] = bool(raw_entry.get("affordable"))
        card["stocked"] = raw_entry.get("stocked") is not False

    return cards


def _is_basic_strike_or_defend(card: dict[str, Any], meta: dict[str, Any]) -> bool:
    name = str(card.get("name") or meta.get("name") or "").strip().lower()
    return name in {"strike", "defend"}


def _find_removal_candidates(
    state: dict[str, Any],
    cards_index: dict[str, Any],
    deck_summary: dict[str, Any],
) -> list[dict[str, Any]]:
    run = _run_payload(state)
    candidates: list[dict[str, Any]] = []
    for entry in _extract_card_entries(run.get("deck")):
        meta = _lookup_item_meta(cards_index, entry.get("card_id"))
        profile = _card_profile(entry, meta)
        rarity_key = _normalize_token(meta.get("rarity") or entry.get("rarity"))
        type_key = _normalize_token(meta.get("type") or entry.get("type"))
        duplicate_count = deck_summary["card_id_counts"].get(entry.get("card_id") or "", 0)
        score = 0
        reasons: list[str] = []

        if type_key == "curse":
            score += 40
            reasons.append("诅咒是最高优先级删牌目标")
        elif rarity_key == "basic":
            score += 14
            reasons.append("基础牌在 Act 2 往往边际收益偏低")

        if _is_basic_strike_or_defend(entry, meta):
            score += 10
            reasons.append("Strike/Defend 常是最稳的删牌对象")

        if duplicate_count >= 3:
            score += 6
            reasons.append("同名牌数量偏多")

        if profile["aoe"] or profile["scaling"] or profile["draw_count"] > 0 or profile["weak"] or profile["vulnerable"]:
            score -= 10
            reasons.append("这张牌承担了功能位，不适合优先删除")

        if score <= 0:
            continue

        candidates.append(
            {
                "card_id": entry.get("card_id"),
                "name": profile["name"],
                "remove_score": score,
                "reasons": _dedupe_reasons(reasons, limit=3),
            }
        )

    candidates.sort(key=lambda item: (-item["remove_score"], item["name"]))
    return candidates[:5]


def _evaluate_shop_removal(
    state: dict[str, Any],
    deck_summary: dict[str, Any],
    cards_index: dict[str, Any],
) -> dict[str, Any] | None:
    shop = _shop_payload(state)
    removal = shop.get("remove")
    if not isinstance(removal, dict):
        return None

    available = bool(removal.get("available"))
    used = bool(removal.get("used"))
    affordable = bool(removal.get("affordable"))
    price = _safe_int(removal.get("price")) or 0
    candidates = _find_removal_candidates(state, cards_index, deck_summary)
    score = 45
    reasons: list[str] = []

    if not available or used:
        score = 0
        reasons.append("删牌当前不可用")
    else:
        if affordable:
            score += 6
            reasons.append("当前金币足够删牌")
        else:
            score -= 30
            reasons.append("当前金币不足以删牌")

        if deck_summary["deck_size"] >= 18:
            score += 8
            reasons.append("牌组偏厚，删牌收益更高")

        if candidates:
            score += min(20, candidates[0]["remove_score"])
            reasons.append(f"当前有明显低价值目标：{candidates[0]['name']}")
        else:
            score -= 8
            reasons.append("当前牌组里没有特别差的删牌目标")

        if price >= 100:
            score -= 4
            reasons.append("删牌价格偏高")

    score = _clamp(score, 0, 100)
    return {
        "price": price,
        "available": available,
        "affordable": affordable,
        "score": score,
        "recommendation": "take" if score >= 68 else "consider" if score >= 55 else "skip",
        "candidate_cards": candidates[:3],
        "reasons": _dedupe_reasons(reasons),
    }


def _relic_profile(relic: dict[str, Any], meta: dict[str, Any]) -> dict[str, Any]:
    description = str(meta.get("description") or relic.get("description") or "").lower()
    rarity = str(meta.get("rarity") or relic.get("rarity") or "").strip()
    return {
        "name": relic.get("name") or meta.get("name") or "Unknown Relic",
        "rarity": rarity or None,
        "draw": "draw" in description,
        "block": "block" in description or "dexterity" in description,
        "frontload": "strength" in description or "vigor" in description or "damage" in description,
        "scaling": "strength" in description or "dexterity" in description or "focus" in description or "poison" in description,
        "potion": "potion" in description,
        "gold": "gold" in description,
        "combat_setup": "start each combat" in description or "at the start of each combat" in description,
        "description": description,
    }


def _score_shop_relic(relic: dict[str, Any], deck_summary: dict[str, Any], profile: dict[str, Any]) -> tuple[int, list[str]]:
    score = 58
    reasons: list[str] = []
    rarity_bonus = {
        "commonrelic": 4,
        "uncommonrelic": 8,
        "rarerelic": 14,
        "ancientrelic": 16,
    }.get(_normalize_token(profile.get("rarity")), 6)
    score += rarity_bonus

    if profile["draw"] and deck_summary["draw_cards"] < 2:
        score += 10
        reasons.append("补强开局与过牌稳定性")
    if profile["block"] and deck_summary["block_cards"] < 5:
        score += 10
        reasons.append("补强防御覆盖")
    if profile["frontload"] and deck_summary["frontload_cards"] < 5:
        score += 9
        reasons.append("补强前台输出")
    if profile["scaling"] and deck_summary["scaling_cards"] < 2:
        score += 8
        reasons.append("补强成长能力")
    if profile["potion"] and deck_summary["potion_count"] < 2:
        score += 6
        reasons.append("提升药水上限或药水收益")
    if profile["gold"] and deck_summary["floor"] < 30:
        score += 4
        reasons.append("仍有楼层可把经济收益转成强度")
    if profile["combat_setup"]:
        score += 4
        reasons.append("稳定的每战即时收益")

    return _clamp(score, 0, 100), _dedupe_reasons(reasons)


def _find_upgrade_targets(state: dict[str, Any], cards_index: dict[str, Any]) -> list[dict[str, Any]]:
    run = _run_payload(state)
    targets: list[dict[str, Any]] = []
    for entry in _extract_card_entries(run.get("deck")):
        if entry.get("upgraded"):
            continue

        meta = _lookup_item_meta(cards_index, entry.get("card_id"))
        upgrade = meta.get("upgrade")
        if not isinstance(upgrade, dict) or not upgrade:
            continue

        profile = _card_profile(entry, meta)
        score = 50
        reasons: list[str] = []
        rarity_key = _normalize_token(meta.get("rarity"))

        if profile["aoe"]:
            score += 14
            reasons.append("升级后能明显改善群战")
        if profile["scaling"]:
            score += 12
            reasons.append("升级能放大成长价值")
        if profile["frontload"]:
            score += 10
            reasons.append("升级后能更快影响战局")
        if profile["draw_count"] > 0 or profile["energy"]:
            score += 6
            reasons.append("升级会改善节奏或手感")
        if rarity_key == "rare":
            score += 6
            reasons.append("高稀有度牌升级收益通常更高")
        if rarity_key == "basic" and not (profile["weak"] or profile["vulnerable"]):
            score -= 10
            reasons.append("基础牌升级优先级偏低")

        targets.append(
            {
                "card_id": entry.get("card_id"),
                "name": profile["name"],
                "upgrade_score": _clamp(score, 0, 100),
                "reasons": _dedupe_reasons(reasons, limit=3),
            }
        )

    targets.sort(key=lambda item: (-item["upgrade_score"], item["name"]))
    return targets[:5]


def _rest_option_kind(option: dict[str, Any]) -> str:
    token = _normalize_token(f"{option.get('option_id', '')} {option.get('line', '')}")
    if "rest" in token or "heal" in token or "sleep" in token:
        return "rest"
    if "smith" in token or "upgrade" in token:
        return "smith"
    if "dig" in token:
        return "dig"
    if "lift" in token:
        return "lift"
    if "recall" in token:
        return "recall"
    return "other"


def _potion_profile(potion: dict[str, Any], meta: dict[str, Any]) -> dict[str, Any]:
    description = str(
        meta.get("description")
        or potion.get("usage")
        or potion.get("description")
        or potion.get("line")
        or ""
    ).lower()
    rarity = str(meta.get("rarity") or potion.get("rarity") or "").strip()
    has_strength_down = "lose" in description and "strength" in description
    return {
        "name": potion.get("name") or meta.get("name") or "Unknown Potion",
        "rarity": rarity or None,
        "aoe": "all enemies" in description,
        "block": "block" in description,
        "draw": "draw" in description,
        "strength": "strength" in description and not has_strength_down,
        "dexterity": "dexterity" in description,
        "weak": "weak" in description or has_strength_down,
        "vulnerable": "vulnerable" in description,
        "poison": "poison" in description,
        "energy": "gain [energy" in description or "[energy:1]" in description,
        "damage": "deal " in description,
    }


def _score_potion_value(profile: dict[str, Any], deck_summary: dict[str, Any]) -> tuple[int, list[str]]:
    score = 50
    reasons: list[str] = []
    rarity_bonus = {
        "common": 0,
        "uncommon": 6,
        "rare": 12,
        "event": 4,
    }.get(_normalize_token(profile.get("rarity")), 3)
    score += rarity_bonus

    if profile["aoe"] and deck_summary["aoe_cards"] == 0:
        score += 16
        reasons.append("补足当前缺的范围处理")
    if profile["block"] and deck_summary["hp_ratio"] < 0.55:
        score += 12
        reasons.append("低血线时防御药水价值更高")
    if profile["draw"] and deck_summary["draw_cards"] < 2:
        score += 8
        reasons.append("补强过牌稳定性")
    if profile["strength"] and deck_summary["frontload_cards"] < 5:
        score += 8
        reasons.append("补强爆发输出")
    if profile["dexterity"] and deck_summary["block_cards"] < 5:
        score += 8
        reasons.append("补强防御强度")
    if profile["weak"] and deck_summary["weak_sources"] == 0:
        score += 7
        reasons.append("补减伤来源")
    if profile["vulnerable"] and deck_summary["vulnerable_sources"] == 0:
        score += 7
        reasons.append("补易伤来源")
    if profile["poison"] and deck_summary["scaling_cards"] < 2:
        score += 6
        reasons.append("补成长伤害")
    if profile["energy"]:
        score += 12
        reasons.append("能量药水在关键战斗很值钱")
    if profile["damage"] and deck_summary["frontload_cards"] < 5:
        score += 6
        reasons.append("补即时伤害")

    return _clamp(score, 0, 100), _dedupe_reasons(reasons)


def _evaluate_potions_for_state(state: dict[str, Any]) -> dict[str, Any]:
    potions_index = _safe_collection_index("potions")
    cards_index = _safe_collection_index("cards")
    deck_summary = _summarize_deck(state, cards_index)
    run = _run_payload(state)
    shop = _shop_payload(state)
    belt_entries = run.get("potions") if isinstance(run.get("potions"), list) else []
    occupied_belt = [
        entry
        for entry in belt_entries
        if isinstance(entry, dict) and (entry.get("occupied") is not False) and (entry.get("potion_id") or entry.get("name"))
    ]
    total_slots = len(belt_entries)
    empty_slots = max(0, total_slots - len(occupied_belt))

    current_potions: list[dict[str, Any]] = []
    for entry in occupied_belt:
        meta = _lookup_item_meta(potions_index, entry.get("potion_id"))
        profile = _potion_profile(entry, meta)
        keep_score, reasons = _score_potion_value(profile, deck_summary)
        current_potions.append(
            {
                "index": _safe_int(entry.get("i")) or _safe_int(entry.get("index")) or 0,
                "potion_id": entry.get("potion_id"),
                "name": profile["name"],
                "keep_score": keep_score,
                "reasons": reasons,
            }
        )

    current_potions.sort(key=lambda item: (item["keep_score"], item["index"]))
    weakest_potion = current_potions[0] if current_potions else None

    shop_potions: list[dict[str, Any]] = []
    raw_shop_potions = shop.get("potions") if isinstance(shop.get("potions"), list) else []
    for entry in raw_shop_potions:
        if not isinstance(entry, dict):
            continue

        meta = _lookup_item_meta(potions_index, entry.get("potion_id"))
        profile = _potion_profile(entry, meta)
        buy_score, reasons = _score_potion_value(profile, deck_summary)
        price = _safe_int(entry.get("price")) or 0
        affordable = bool(entry.get("affordable"))
        if not affordable:
            buy_score -= 35
            reasons.append("当前金币不足")
        elif price <= 45:
            buy_score += 3
            reasons.append("价格较低")
        elif price >= 70:
            buy_score -= 6
            reasons.append("价格偏高")

        if empty_slots == 0:
            buy_score -= 6
            reasons.append("药水槽已满，需要替换现有药水")

        shop_potions.append(
            {
                "option_index": _safe_int(entry.get("i")) or _safe_int(entry.get("index")) or 0,
                "potion_id": entry.get("potion_id"),
                "name": profile["name"],
                "price": price,
                "affordable": affordable,
                "buy_score": _clamp(buy_score, 0, 100),
                "recommendation": "buy" if buy_score >= 64 else "consider" if buy_score >= 54 else "skip",
                "reasons": _dedupe_reasons(reasons),
            }
        )

    shop_potions.sort(key=lambda item: (-item["buy_score"], item["price"], item["option_index"]))
    best_shop_potion = next((item for item in shop_potions if item["affordable"]), None)
    recommended_purchase: dict[str, Any] | None = None
    if best_shop_potion is not None:
        if empty_slots > 0 and best_shop_potion["buy_score"] >= 56:
            recommended_purchase = {
                "option_index": best_shop_potion["option_index"],
                "potion_id": best_shop_potion["potion_id"],
                "name": best_shop_potion["name"],
                "replace_index": None,
            }
        elif weakest_potion is not None and best_shop_potion["buy_score"] >= weakest_potion["keep_score"] + 8:
            recommended_purchase = {
                "option_index": best_shop_potion["option_index"],
                "potion_id": best_shop_potion["potion_id"],
                "name": best_shop_potion["name"],
                "replace_index": weakest_potion["index"],
            }

    return {
        "deck_summary": _public_deck_summary(deck_summary),
        "belt": {
            "occupied_slots": len(occupied_belt),
            "total_slots": total_slots,
            "empty_slots": empty_slots,
            "potions": current_potions,
        },
        "shop_potions": shop_potions,
        "recommended_purchase": recommended_purchase,
    }


def _evaluate_shop_options_for_state(state: dict[str, Any]) -> dict[str, Any]:
    shop = _shop_payload(state)
    if not shop:
        return {
            "recommended_action": None,
            "message": "当前状态没有商店库存可评估。",
        }

    cards_index = _safe_collection_index("cards")
    relics_index = _safe_collection_index("relics")
    deck_summary = _summarize_deck(state, cards_index)
    removal = _evaluate_shop_removal(state, deck_summary, cards_index)
    potion_analysis = _evaluate_potions_for_state(state)

    evaluated_cards: list[dict[str, Any]] = []
    for card in _extract_shop_cards(shop.get("cards")):
        meta = _lookup_item_meta(cards_index, card.get("card_id"))
        profile = _card_profile(card, meta)
        score, reasons = _score_reward_card(card, deck_summary, profile)
        price = _safe_int(card.get("price")) or 0
        affordable = bool(card.get("affordable"))
        stocked = card.get("stocked") is not False

        if not stocked:
            score -= 30
            reasons.append("当前已售出")
        elif not affordable:
            score -= 35
            reasons.append("当前金币不足")
        else:
            if price <= 60:
                score += 4
                reasons.append("价格较低")
            elif price >= 130:
                score -= 8
                reasons.append("价格偏高")
            elif price >= 100:
                score -= 4
                reasons.append("价格不低")

            if removal and removal["affordable"] and removal["score"] >= 70 and price + removal["price"] > deck_summary["gold"]:
                score -= 6
                reasons.append("可能更该把金币留给删牌")

        score = _clamp(score, 0, 100)
        evaluated_cards.append(
            {
                "option_index": card["index"],
                "card_id": card.get("card_id"),
                "name": profile["name"],
                "price": price,
                "affordable": affordable,
                "score": score,
                "recommendation": "buy" if score >= 70 else "consider" if score >= 56 else "skip",
                "reasons": _dedupe_reasons(reasons),
            }
        )

    evaluated_relics: list[dict[str, Any]] = []
    raw_relics = shop.get("relics") if isinstance(shop.get("relics"), list) else []
    for relic in raw_relics:
        if not isinstance(relic, dict):
            continue

        meta = _lookup_item_meta(relics_index, relic.get("relic_id"))
        profile = _relic_profile(relic, meta)
        score, reasons = _score_shop_relic(relic, deck_summary, profile)
        price = _safe_int(relic.get("price")) or 0
        affordable = bool(relic.get("affordable"))
        stocked = relic.get("stocked") is not False

        if not stocked:
            score -= 30
            reasons.append("当前已售出")
        elif not affordable:
            score -= 40
            reasons.append("当前金币不足")
        else:
            if price <= 180:
                score += 2
                reasons.append("价格还算合理")
            elif price >= 260:
                score -= 6
                reasons.append("价格偏高")

        evaluated_relics.append(
            {
                "option_index": _safe_int(relic.get("i")) or _safe_int(relic.get("index")) or 0,
                "relic_id": relic.get("relic_id"),
                "name": profile["name"],
                "price": price,
                "affordable": affordable,
                "score": _clamp(score, 0, 100),
                "recommendation": "buy" if score >= 72 else "consider" if score >= 60 else "skip",
                "reasons": _dedupe_reasons(reasons),
            }
        )

    action_candidates: list[tuple[int, int, dict[str, Any]]] = []
    if removal and removal["affordable"]:
        action_candidates.append(
            (
                removal["score"],
                3,
                {
                    "kind": "remove_card_at_shop",
                    "score": removal["score"],
                    "candidate_card": removal["candidate_cards"][0] if removal["candidate_cards"] else None,
                },
            )
        )

    for relic in evaluated_relics:
        if relic["affordable"]:
            action_candidates.append(
                (
                    relic["score"],
                    4,
                    {
                        "kind": "buy_relic",
                        "option_index": relic["option_index"],
                        "item_id": relic["relic_id"],
                        "name": relic["name"],
                        "score": relic["score"],
                    },
                )
            )

    for card in evaluated_cards:
        if card["affordable"]:
            action_candidates.append(
                (
                    card["score"],
                    2,
                    {
                        "kind": "buy_card",
                        "option_index": card["option_index"],
                        "item_id": card["card_id"],
                        "name": card["name"],
                        "score": card["score"],
                    },
                )
            )

    for potion in potion_analysis["shop_potions"]:
        if potion["affordable"]:
            action_candidates.append(
                (
                    potion["buy_score"],
                    1,
                    {
                        "kind": "buy_potion",
                        "option_index": potion["option_index"],
                        "item_id": potion["potion_id"],
                        "name": potion["name"],
                        "score": potion["buy_score"],
                    },
                )
            )

    action_candidates.sort(key=lambda item: (-item[0], -item[1]))
    recommended_action = action_candidates[0][2] if action_candidates and action_candidates[0][0] >= 56 else None

    return {
        "recommended_action": recommended_action,
        "deck_summary": _public_deck_summary(deck_summary),
        "remove": removal,
        "cards": evaluated_cards,
        "relics": evaluated_relics,
        "potions": potion_analysis["shop_potions"],
    }


def _assess_rest_site_for_state(state: dict[str, Any]) -> dict[str, Any]:
    rest = _rest_payload(state)
    options = rest.get("options") if isinstance(rest.get("options"), list) else []
    if not options:
        return {
            "best_index": None,
            "options": [],
            "message": "当前状态没有可评估的营火选项。",
        }

    cards_index = _safe_collection_index("cards")
    deck_summary = _summarize_deck(state, cards_index)
    elite_risk = _assess_elite_risk_for_state(state)
    boss_readiness = _check_boss_readiness_for_state(state)
    upgrade_targets = _find_upgrade_targets(state, cards_index)
    evaluated_options: list[dict[str, Any]] = []

    for option in options:
        if not isinstance(option, dict):
            continue

        enabled = option.get("enabled") is not False
        kind = _rest_option_kind(option)
        score = 50
        reasons: list[str] = []

        if not enabled:
            score = 0
            reasons.append("当前不可选")
        elif kind == "rest":
            if deck_summary["hp_ratio"] < 0.45 or deck_summary["current_hp"] < 30:
                score += 25
                reasons.append("当前血线偏低，优先补血")
            elif deck_summary["hp_ratio"] < 0.6:
                score += 12
                reasons.append("血量不算安全")
            else:
                score -= 4
                reasons.append("血量尚可，补血收益下降")

            if boss_readiness["checks"]["hp"]["status"] == "fail":
                score += 12
                reasons.append("Boss readiness 的血量检查未过")
            if elite_risk["recommendation"] != "TAKE" and deck_summary["hp_ratio"] < 0.6:
                score += 8
                reasons.append("近期精英风险偏高")

        elif kind == "smith":
            if deck_summary["hp_ratio"] >= 0.65:
                score += 10
                reasons.append("血量足够支持贪升级")
            elif deck_summary["hp_ratio"] < 0.5:
                score -= 18
                reasons.append("血量太低，不适合继续贪升级")

            if upgrade_targets:
                score += 8 + min(8, len(upgrade_targets) * 2)
                reasons.append(f"当前有高价值升级目标：{upgrade_targets[0]['name']}")
            else:
                score -= 10
                reasons.append("当前缺少特别值钱的升级目标")

            if boss_readiness["recommendation"] == "READY":
                score += 6
                reasons.append("整体 readiness 尚可，可以换更强上限")

        elif kind == "dig":
            if deck_summary["hp_ratio"] >= 0.65:
                score += 8
                reasons.append("血量允许拿长期收益")
            else:
                score -= 8
                reasons.append("当前血量不适合贪 relic")

        elif kind == "lift":
            if deck_summary["hp_ratio"] >= 0.65:
                score += 7
                reasons.append("血量允许拿长期强度")
            else:
                score -= 7
                reasons.append("当前血量不适合贪力量")
            if deck_summary["frontload_cards"] < 5:
                score += 5
                reasons.append("当前牌组仍缺前台输出")

        elif kind == "recall":
            if deck_summary["hp_ratio"] >= 0.55:
                score += 4
                reasons.append("血量尚可，可以承担 key 成本")
            else:
                score -= 10
                reasons.append("当前血量不适合回忆拿 key")

        evaluated_options.append(
            {
                "index": _safe_int(option.get("i")) or _safe_int(option.get("index")) or 0,
                "option_id": option.get("option_id"),
                "kind": kind,
                "line": option.get("line"),
                "score": _clamp(score, 0, 100),
                "recommendation": "take" if score >= 68 else "consider" if score >= 56 else "skip",
                "reasons": _dedupe_reasons(reasons),
            }
        )

    best_option = max(evaluated_options, key=lambda item: (item["score"], -(item["index"] or 0)))
    return {
        "best_index": best_option["index"],
        "best_option_id": best_option.get("option_id"),
        "deck_summary": _public_deck_summary(deck_summary),
        "upgrade_targets": upgrade_targets[:3],
        "options": evaluated_options,
    }


def create_server(client: Sts2Client | None = None, tool_profile: str | None = None) -> FastMCP:
    sts2 = client or Sts2Client()
    knowledge = Sts2KnowledgeBase()
    handoff = Sts2HandoffService(knowledge)
    profile = _normalize_tool_profile(tool_profile)
    mcp = FastMCP("STS2 AI Agent")

    def _agent_state() -> dict[str, Any]:
        state = sts2.get_state()
        return _build_agent_state_payload(state, knowledge)

    def _is_actionable_state(state: dict[str, Any]) -> bool:
        actions = state.get("available_actions")
        if not isinstance(actions, list):
            actions = state.get("actions")
        return isinstance(actions, list) and len(actions) > 0

    def _wait_until_actionable_impl(
        timeout_seconds: float,
        *,
        monotonic: Callable[[], float] = time.monotonic,
        sleep: Callable[[float], None] = time.sleep,
    ) -> dict[str, Any]:
        timeout = max(0.1, float(timeout_seconds))
        actionable_events = {
            "player_action_window_opened",
            "route_decision_required",
            "reward_decision_required",
            "available_actions_changed",
            "screen_changed",
        }

        state = sts2.get_state()
        if _is_actionable_state(state):
            return {
                "matched": False,
                "event": None,
                "state": state,
                "actions": sts2.get_available_actions(),
                "timeout_seconds": timeout,
                "source": "state",
            }

        started_at = monotonic()
        event: dict[str, Any] | None = None
        source = "events"

        try:
            event = sts2.wait_for_event(event_names=actionable_events, timeout=timeout)
        except Exception:
            event = None
            source = "polling"

        remaining = max(0.0, timeout - (monotonic() - started_at))
        state = sts2.get_state()

        if event is None and not _is_actionable_state(state) and remaining > 0:
            source = "polling"
            interval = max(0.05, float(os.getenv("STS2_MCP_FALLBACK_POLL_SECONDS", "0.25")))
            deadline = monotonic() + remaining
            baseline_signature = "|".join(sorted(str(name) for name in (state.get("available_actions") or [])))

            while monotonic() < deadline:
                sleep(interval)
                state = sts2.get_state()
                if _is_actionable_state(state):
                    break

                signature = "|".join(sorted(str(name) for name in (state.get("available_actions") or [])))
                if signature != baseline_signature:
                    break

        return {
            "matched": event is not None,
            "event": event,
            "state": state,
            "actions": sts2.get_available_actions(),
            "timeout_seconds": timeout,
            "source": source,
        }

    @mcp.tool
    def health_check() -> dict[str, Any]:
        """Check whether the STS2 AI Agent Mod is loaded and reachable."""
        return sts2.get_health()

    @mcp.tool
    def get_game_state() -> dict[str, Any]:
        """Read the compact agent-facing game state snapshot."""
        return _agent_state()

    @mcp.tool
    def get_raw_game_state() -> dict[str, Any]:
        """Read the full raw `/state` snapshot for debugging or schema inspection."""
        return sts2.get_state()

    @mcp.tool
    def get_available_actions() -> list[dict[str, Any]]:
        """List currently executable actions with `requires_index` and `requires_target` hints."""
        return sts2.get_available_actions()

    if profile in {"full", "layered"}:
        @mcp.tool
        def get_planner_context(planner_note: str | None = None) -> dict[str, Any]:
            """Build a planner-focused snapshot with route branches and linked event knowledge."""
            return knowledge.build_planner_context(sts2.get_state(), planner_note=planner_note)

        @mcp.tool
        def create_planner_handoff(
            planning_focus: str | None = None,
            previous_combat_summary: str | None = None,
        ) -> dict[str, Any]:
            """Build a clean planner-agent packet for route, reward, event, and shop decisions."""
            return handoff.create_planner_handoff(
                sts2.get_state(),
                planning_focus=planning_focus,
                previous_combat_summary=previous_combat_summary,
            )

        @mcp.tool
        def get_combat_context(
            planner_note: str | None = None,
            include_knowledge: bool = True,
        ) -> dict[str, Any]:
            """Build a combat-focused snapshot and link it to the canonical combat knowledge entry."""
            return knowledge.build_combat_context(
                sts2.get_state(),
                planner_note=planner_note,
                include_knowledge=include_knowledge,
            )

        @mcp.tool
        def create_combat_handoff(
            planner_message: str | None = None,
            combat_objective: str | None = None,
        ) -> dict[str, Any]:
            """Build a clean combat-agent packet with linked combat knowledge and planner guidance."""
            return handoff.create_combat_handoff(
                sts2.get_state(),
                planner_message=planner_message,
                combat_objective=combat_objective,
            )

        @mcp.tool
        def complete_combat_handoff(
            combat_key: str,
            summary: str,
            planner_message: str | None = None,
            pattern_note: str | None = None,
            trait_note: str | None = None,
            tactical_note: str | None = None,
        ) -> dict[str, Any]:
            """Persist a combat-agent summary and optional enemy-pattern notes, then return a planner-facing brief."""
            return handoff.complete_combat_handoff(
                combat_key=combat_key,
                summary=summary,
                planner_message=planner_message,
                pattern_note=pattern_note,
                trait_note=trait_note,
                tactical_note=tactical_note,
            )

        @mcp.tool
        def append_combat_knowledge(note: str, section: str = "observations") -> dict[str, Any]:
            """Append a note to the active combat knowledge file."""
            return knowledge.append_combat_note(
                sts2.get_state(),
                note=note,
                section=section,
            )

        @mcp.tool
        def append_event_knowledge(
            note: str,
            section: str = "observations",
            option_index: int | None = None,
        ) -> dict[str, Any]:
            """Append a note to the active event knowledge file."""
            return knowledge.append_event_note(
                sts2.get_state(),
                note=note,
                section=section,
                option_index=option_index,
            )

        @mcp.tool
        def complete_event_handoff(
            event_id: str,
            summary: str,
            option_index: int | None = None,
            planning_note: str | None = None,
            outcome_note: str | None = None,
        ) -> dict[str, Any]:
            """Persist an event outcome summary and optional event notes, then return a planner-facing brief."""
            return handoff.complete_event_handoff(
                event_id=event_id,
                summary=summary,
                option_index=option_index,
                planning_note=planning_note,
                outcome_note=outcome_note,
            )

    @mcp.tool
    def get_game_data_item(collection: str, item_id: str) -> dict[str, Any] | None:
        """Return a single item from a game metadata collection by id.

        Example: `get_game_data_item(collection='cards', item_id='ABRASIVE')`
        """
        if not item_id:
            return None

        try:
            index = _ensure_game_data_index(collection)
            return _lookup_game_data_item(index=index, item_id=item_id)
        except (KeyError, RuntimeError, TypeError) as exc:
            return _build_game_data_tool_error(collection=collection, exc=exc)

    @mcp.tool
    def get_game_data_items(collection: str, item_ids: str) -> dict[str, Any]:
        """Return multiple items (by comma-separated ids) from a collection."""
        if not item_ids:
            return {}

        try:
            index = _ensure_game_data_index(collection)
            ids = [s.strip() for s in item_ids.split(ITEM_IDS_SEPARATOR) if s.strip()]
            result: dict[str, Any] = {}
            for i in ids:
                result[i] = _lookup_game_data_item(index=index, item_id=i)
            return result
        except (KeyError, RuntimeError, TypeError) as exc:
            return _build_game_data_tool_error(collection=collection, exc=exc)

    @mcp.tool
    def get_relevant_game_data(collection: str, item_ids: str) -> dict[str, Any]:
        """Return items with only the most relevant fields for the current game context.

        This automatically detects the current scene (combat/shop/event/menu) and returns
        only the fields most useful for AI decision-making in that context, minimizing token usage.

        - `collection`: e.g. `cards`, `relics`, `monsters`, `events`
        - `item_ids`: comma-separated ids

        Recommended for most queries to save tokens and reduce uncertainty.
        """
        # Auto-detect current scene from game state
        state = sts2.get_state()
        screen = state.get("screen", "")
        scene = _detect_scene_from_screen(screen)
        try:
            suggested_fields = _SCENE_FIELD_SETS.get(scene, {}).get(collection)
            if not suggested_fields:
                # Fallback to basic query if no scene-specific fields defined
                return get_game_data_items(collection=collection, item_ids=item_ids)

            return get_game_data_items_fields(
                collection=collection,
                item_ids=item_ids,
                fields=",".join(suggested_fields),
            )
        except (KeyError, RuntimeError, TypeError) as exc:
            return _build_game_data_tool_error(collection=collection, exc=exc)

    @mcp.tool
    def evaluate_card_rewards() -> dict[str, Any]:
        """Score card reward options against your current deck composition.

        Returns each reward card with a score (0-100) and reasons for/against picking it.
        Higher score = better fit for the current deck.
        """
        return _evaluate_card_rewards_for_state(_build_agent_state_payload(sts2.get_state(), knowledge))

    @mcp.tool
    def assess_elite_risk() -> dict[str, Any]:
        """Assess whether to take an elite fight based on current HP, deck, and potions."""
        return _assess_elite_risk_for_state(_build_agent_state_payload(sts2.get_state(), knowledge))

    @mcp.tool
    def check_boss_readiness() -> dict[str, Any]:
        """Check whether the current deck, HP, and potions are ready for the next boss fight."""
        return _check_boss_readiness_for_state(_build_agent_state_payload(sts2.get_state(), knowledge))

    @mcp.tool
    def evaluate_shop_options() -> dict[str, Any]:
        """Score shop cards, relics, potions, and card removal against the current run state."""
        return _evaluate_shop_options_for_state(_build_agent_state_payload(sts2.get_state(), knowledge))

    @mcp.tool
    def assess_rest_site() -> dict[str, Any]:
        """Score rest-site options using HP, upgrade targets, and upcoming risk signals."""
        return _assess_rest_site_for_state(_build_agent_state_payload(sts2.get_state(), knowledge))

    @mcp.tool
    def evaluate_potions() -> dict[str, Any]:
        """Evaluate current belt potions and any visible shop potions, including replacement suggestions."""
        return _evaluate_potions_for_state(_build_agent_state_payload(sts2.get_state(), knowledge))

    @mcp.tool
    def wait_for_event(event_names: str = "", timeout_seconds: float = 20.0) -> dict[str, Any]:
        """Wait for one matching game event from `/events/stream`.

        - `event_names`: comma-separated event names. Empty means accept any event.
        - `timeout_seconds`: maximum wait time before returning `matched=false`.
        """
        timeout = max(0.1, float(timeout_seconds))
        target_names = [name.strip() for name in event_names.split(",") if name.strip()]
        event = sts2.wait_for_event(
            event_names=target_names or None,
            timeout=timeout,
        )
        if event is None:
            return {
                "matched": False,
                "event": None,
                "event_names": target_names,
                "timeout_seconds": timeout,
            }

        return {
            "matched": True,
            "event": event,
            "event_names": target_names,
            "timeout_seconds": timeout,
        }

    @mcp.tool
    def wait_until_actionable(timeout_seconds: float = 20.0) -> dict[str, Any]:
        """Wait until a new actionable phase is reported, then return fresh state.

        This reduces high-frequency polling between enemy turns, map transitions,
        and reward animations. Falls back to basic polling when SSE events are
        unavailable or no matching event arrives in time.
        """
        return _wait_until_actionable_impl(timeout_seconds)

    @mcp.tool
    def act(
        action: str,
        card_index: int | None = None,
        target_index: int | None = None,
        option_index: int | None = None,
    ) -> dict[str, Any]:
        """Execute one currently available game action through the compact tool surface.

        Usage loop:
            1. Call `get_game_state()` or `get_available_actions()`.
            2. Branch on `state.session.mode` and `state.session.phase`.
            3. Pick an action that is currently available.
            4. Pass only the indices required by that action from the latest state.
            5. Read state again after the action completes.

        Compact-tool rules:
            - Guided mode intentionally keeps the tool surface small: use this
              single `act` tool for both singleplayer and multiplayer actions.
            - Multiplayer never changes the control scope; you only control the
              local player exposed by the latest state.
            - Never guess actions from screen names alone. Only call names that
              are present in `state.available_actions`.

        Notes:
            - Use `card_index` for `play_card`.
            - Use `option_index` for map, reward, shop, event, rest, selection,
              and multiplayer-lobby actions.
            - Use `target_index` only when the latest state marks a card or potion as `requires_target=true`.
            - Read `target_index_space` and `valid_target_indices` from state to know whether `target_index`
              refers to `combat.enemies[]` or `combat.players[]`.
            - `run_console_command` is intentionally excluded from this compact tool.
        """
        normalized = action.strip().lower()
        if normalized == "run_console_command":
            raise RuntimeError("run_console_command is gated separately and must use its own tool when enabled.")

        return sts2.execute_action(
            normalized,
            card_index=card_index,
            target_index=target_index,
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "act",
                "tool_profile": profile,
            },
        )

    if profile == "full":
        _register_legacy_action_tools(mcp, sts2)

    if _debug_tools_enabled():
        @mcp.tool
        def run_console_command(command: str) -> dict[str, Any]:
            """Run a game dev-console command for local validation or debugging."""
            return sts2.run_console_command(command=command)

    return mcp


def main() -> None:
    create_server().run(transport="stdio", show_banner=False)


if __name__ == "__main__":
    main()
