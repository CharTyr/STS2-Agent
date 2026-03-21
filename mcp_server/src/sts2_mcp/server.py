from __future__ import annotations

import json
import os
import threading
import time
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


def _get_power_amount(powers: list[dict[str, Any]], power_id: str) -> int:
    """Get stacked amount for a power by id (case-insensitive exact match)."""
    needle = power_id.upper()
    for p in powers:
        pid = (p.get("power_id") or "").upper()
        if pid == needle or pid == f"{needle}_POWER":
            return int(p.get("amount", 0))
    return 0


def _compute_card_damage(base_damage: int, strength: int, is_weak: bool, target_vulnerable: bool, hits: int = 1) -> int:
    """Compute actual damage for a single card play."""
    per_hit = base_damage + strength
    if is_weak:
        per_hit = int(per_hit * 0.75)
    if target_vulnerable:
        per_hit = int(per_hit * 1.5)
    return max(0, per_hit) * max(1, hits)


def _compute_card_block(base_block: int, dexterity: int, is_frail: bool) -> int:
    """Compute actual block for a single card play."""
    value = base_block + dexterity
    if is_frail:
        value = int(value * 0.75)
    return max(0, value)


def _coerce_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def _extract_agent_view(state: dict[str, Any]) -> dict[str, Any]:
    agent_view = state.get("agent_view")
    return agent_view if isinstance(agent_view, dict) else {}


def _extract_run_payload(state: dict[str, Any]) -> dict[str, Any]:
    run = state.get("run")
    if isinstance(run, dict):
        return run

    run = _extract_agent_view(state).get("run")
    return run if isinstance(run, dict) else {}


def _extract_reward_payload(state: dict[str, Any]) -> dict[str, Any]:
    reward = state.get("reward")
    if isinstance(reward, dict):
        return reward

    reward = _extract_agent_view(state).get("reward")
    return reward if isinstance(reward, dict) else {}


def _extract_run_deck(state: dict[str, Any]) -> list[dict[str, Any]]:
    deck = _extract_run_payload(state).get("deck")
    if not isinstance(deck, list):
        return []
    return [card for card in deck if isinstance(card, dict)]


def _extract_run_potions(state: dict[str, Any]) -> list[dict[str, Any]]:
    potions = _extract_run_payload(state).get("potions")
    if not isinstance(potions, list):
        return []
    return [potion for potion in potions if isinstance(potion, dict)]


def _extract_reward_card_options(state: dict[str, Any]) -> list[dict[str, Any]]:
    reward = _extract_reward_payload(state)
    raw_options = reward.get("card_options")
    if isinstance(raw_options, list):
        return [card for card in raw_options if isinstance(card, dict)]

    compact_options = reward.get("cards")
    if isinstance(compact_options, list):
        return [
            card for card in compact_options
            if isinstance(card, dict) and card.get("card_id")
        ]

    return []


def _extract_run_hp(state: dict[str, Any]) -> tuple[int, int]:
    run = _extract_run_payload(state)
    if run:
        current_hp = run.get("current_hp")
        max_hp = run.get("max_hp")
        if current_hp is not None or max_hp is not None:
            return _coerce_int(current_hp), _coerce_int(max_hp)

        hp_value = run.get("hp")
        if isinstance(hp_value, str):
            current_text, sep, max_text = hp_value.partition("/")
            if sep:
                return _coerce_int(current_text.strip()), _coerce_int(max_text.strip())

    return 0, 0


def _count_occupied_potions(potions: list[dict[str, Any]]) -> int:
    count = 0
    for potion in potions:
        if potion.get("occupied") is True or potion.get("potion_id") or potion.get("name"):
            count += 1
            continue

        line = potion.get("line")
        if isinstance(line, str):
            normalized = line.strip()
            if normalized and not normalized.startswith("空"):
                count += 1

    return count


def _get_card_type_name(card: dict[str, Any]) -> str:
    return str(card.get("card_type") or card.get("type") or "").upper()


def _get_card_text(card: dict[str, Any]) -> str:
    return str(card.get("rules_text") or card.get("description") or card.get("line") or "")


def _get_energy_cost(card_data: dict[str, Any], default: int = 1) -> int:
    for key in ("energy_cost", "cost"):
        value = card_data.get(key)
        if value is not None:
            return _coerce_int(value, default)
    return default


def _compute_combat_analysis(state: dict[str, Any]) -> dict[str, Any]:
    """Analyze current combat: compute effective card values and tactical summary."""
    combat = state.get("combat")
    if not combat:
        return {"error": "not_in_combat", "message": "Combat analysis is only available during combat."}

    # --- Extract player info ---
    player = combat.get("player", {})
    energy = player.get("energy", 0)
    player_block = player.get("block", 0)
    player_powers = player.get("powers", [])
    strength = _get_power_amount(player_powers, "STRENGTH")
    dexterity = _get_power_amount(player_powers, "DEXTERITY")
    is_weak = _get_power_amount(player_powers, "WEAK") > 0
    is_frail = _get_power_amount(player_powers, "FRAIL") > 0

    # --- Extract enemy info ---
    enemies_raw = combat.get("enemies", [])
    enemies = []
    total_incoming = 0
    for e in enemies_raw:
        if not e.get("is_alive", True):
            continue
        hp = e.get("current_hp", 0)
        block = e.get("block", 0)
        e_powers = e.get("powers", [])
        vulnerable = _get_power_amount(e_powers, "VULNERABLE") > 0
        intents = e.get("intents", [])
        intent_damage = 0
        for intent in intents:
            if intent.get("intent_type") == "Attack":
                intent_damage += intent.get("total_damage") or intent.get("damage", 0)
        total_incoming += intent_damage
        enemies.append({
            "index": e.get("index"),
            "name": e.get("name"),
            "hp": hp,
            "block": block,
            "vulnerable": vulnerable,
            "intent_damage": intent_damage,
        })

    # --- Build card index from static data ---
    card_data_index = _ensure_game_data_index("cards")

    # --- Analyze hand ---
    hand = combat.get("hand", [])
    analyzed_cards: list[dict[str, Any]] = []
    for card in hand:
        card_id = card.get("card_id", "")
        static = _lookup_game_data_item(index=card_data_index, item_id=card_id)

        # Prefer C#-computed values (from mod) over static data + Python calculation
        cs_dmg = card.get("dmg") or card.get("computed_damage")
        cs_blk = card.get("blk") or card.get("computed_block")
        cs_hits = card.get("hits") or card.get("hit_count")

        base_dmg = (static or {}).get("damage") if isinstance(static, dict) else None
        base_blk = (static or {}).get("block") if isinstance(static, dict) else None
        hit_count = cs_hits or ((static or {}).get("hit_count") if isinstance(static, dict) else None)
        card_cost = card.get("energy_cost", 0)
        is_playable = card.get("playable", False)

        computed_damage = None
        computed_block = None
        damage_per_energy = None

        if cs_dmg is not None:
            # Use C#-computed value directly (already accounts for powers)
            computed_damage = int(cs_dmg) * max(1, int(hit_count or 1))
            if card_cost and card_cost > 0:
                damage_per_energy = round(computed_damage / card_cost, 1)
        elif base_dmg is not None:
            # Fallback: compute from static data + Python power calculation
            any_vulnerable = any(e["vulnerable"] for e in enemies)
            computed_damage = _compute_card_damage(
                base_dmg, strength, is_weak,
                target_vulnerable=any_vulnerable,
                hits=int(hit_count or 1),
            )
            if card_cost and card_cost > 0:
                damage_per_energy = round(computed_damage / card_cost, 1)

        if cs_blk is not None:
            computed_block = int(cs_blk)
        elif base_blk is not None:
            computed_block = _compute_card_block(base_blk, dexterity, is_frail)

        entry: dict[str, Any] = {
            "index": card.get("index"),
            "card_id": card_id,
            "name": card.get("name", ""),
            "energy_cost": card_cost,
            "playable": is_playable,
        }
        if computed_damage is not None:
            entry["computed_damage"] = computed_damage
            if damage_per_energy is not None:
                entry["damage_per_energy"] = damage_per_energy
        if computed_block is not None:
            entry["computed_block"] = computed_block

        analyzed_cards.append(entry)

    # --- Compute tactical summary ---
    # Greedy: play cards by damage_per_energy descending until energy exhausted
    attacks = sorted(
        [c for c in analyzed_cards if c.get("computed_damage") and c.get("playable")],
        key=lambda c: c.get("damage_per_energy", 0),
        reverse=True,
    )
    blocks = sorted(
        [c for c in analyzed_cards if c.get("computed_block") and c.get("playable")],
        key=lambda c: c.get("computed_block", 0),
        reverse=True,
    )

    remaining_energy = energy
    max_damage = 0
    for c in attacks:
        cost = c.get("energy_cost")
        cost = 1 if cost is None else _coerce_int(cost, 1)
        if cost <= remaining_energy:
            max_damage += c["computed_damage"]
            remaining_energy -= cost

    remaining_energy_for_block = energy
    max_block = 0
    for c in blocks:
        cost = c.get("energy_cost")
        cost = 1 if cost is None else _coerce_int(cost, 1)
        if cost <= remaining_energy_for_block:
            max_block += c["computed_block"]
            remaining_energy_for_block -= cost

    # Per-enemy lethal check
    enemy_analysis = []
    for e in enemies:
        effective_hp = e["hp"] + e["block"]
        enemy_analysis.append({
            "index": e["index"],
            "name": e["name"],
            "effective_hp": effective_hp,
            "lethal": max_damage >= effective_hp,
            "intent_damage": e["intent_damage"],
            "vulnerable": e["vulnerable"],
        })

    net_damage_taken = max(0, total_incoming - player_block - max_block)

    return {
        "hand": analyzed_cards,
        "energy": energy,
        "player_powers": {
            "strength": strength,
            "dexterity": dexterity,
            "weak": is_weak,
            "frail": is_frail,
        },
        "summary": {
            "max_damage_all_attacks": max_damage,
            "max_block_all_defense": max_block,
            "total_incoming_damage": total_incoming,
            "current_block": player_block,
            "net_damage_if_all_block": net_damage_taken,
        },
        "enemies": enemy_analysis,
    }


def _assess_elite_risk(state: dict[str, Any]) -> dict[str, Any]:
    """Assess whether the player should take an elite fight or avoid it."""
    deck_cards = _extract_run_deck(state)
    deck_size = len(deck_cards)
    current_hp, max_hp = _extract_run_hp(state)
    potions = _extract_run_potions(state)
    potion_count = _count_occupied_potions(potions)

    hp_ratio = current_hp / max(1, max_hp)

    risk_factors: list[str] = []
    reward_factors: list[str] = []

    # HP assessment
    if hp_ratio < 0.4:
        risk_factors.append(f"HP critically low ({current_hp}/{max_hp})")
    elif hp_ratio < 0.55:
        risk_factors.append(f"HP below comfort zone ({current_hp}/{max_hp})")
    else:
        reward_factors.append(f"HP healthy ({current_hp}/{max_hp})")

    # Deck readiness
    if deck_size < 12:
        risk_factors.append(f"Deck too thin ({deck_size} cards) — less margin for error")
    elif deck_size > 20:
        risk_factors.append(f"Deck bloated ({deck_size} cards) — inconsistent draws")
    else:
        reward_factors.append(f"Good deck size ({deck_size} cards)")

    # Potions
    if potion_count >= 2:
        reward_factors.append(f"{potion_count} potions available for emergency")
    elif potion_count == 1:
        reward_factors.append("1 potion as safety net")
    else:
        risk_factors.append("No potions — no safety net")

    # Reward value: elites give relics
    reward_factors.append("Elite rewards: relic + higher gold + better card rewards")

    # Decision
    risk_score = len(risk_factors)
    reward_score = len(reward_factors)
    recommendation = "TAKE" if risk_score <= 1 and hp_ratio >= 0.5 else "AVOID"

    return {
        "recommendation": recommendation,
        "risk_factors": risk_factors,
        "reward_factors": reward_factors,
        "hp_ratio": round(hp_ratio, 2),
        "summary": f"{'Take the elite — rewards outweigh risk.' if recommendation == 'TAKE' else 'Avoid — too risky with current state. Heal or improve deck first.'}",
    }


def _check_boss_readiness(state: dict[str, Any]) -> dict[str, Any]:
    """Evaluate whether the current deck and HP are ready for the Act 1 boss."""
    deck_cards = _extract_run_deck(state)
    deck_size = len(deck_cards)
    current_hp, max_hp = _extract_run_hp(state)
    potions = _extract_run_potions(state)

    card_data_index = _ensure_game_data_index("cards")

    # Analyze deck composition
    attack_count = 0
    block_count = 0
    draw_count = 0
    aoe_count = 0
    vulnerable_sources = 0
    strength_sources = 0
    total_base_damage = 0
    total_base_block = 0

    for card in deck_cards:
        cid = card.get("card_id", "")
        static = _lookup_game_data_item(index=card_data_index, item_id=cid)
        if not isinstance(static, dict):
            continue

        dmg = static.get("damage", 0)
        blk = static.get("block", 0)
        desc = str(static.get("description", "")).upper()

        if dmg:
            attack_count += 1
            total_base_damage += dmg
        if blk:
            block_count += 1
            total_base_block += blk
        if "DRAW" in desc:
            draw_count += 1
        if "ALL ENEM" in desc:
            aoe_count += 1
        if "VULNERABLE" in desc:
            vulnerable_sources += 1
        if "STRENGTH" in desc:
            strength_sources += 1

    # Generate readiness checks
    checks: list[dict[str, Any]] = []

    # HP check
    hp_ok = current_hp >= max_hp * 0.6
    checks.append({
        "check": "hp",
        "pass": hp_ok,
        "detail": f"{current_hp}/{max_hp} HP" + (" — consider resting" if not hp_ok else " — healthy"),
    })

    # Deck size check
    size_ok = 12 <= deck_size <= 20
    checks.append({
        "check": "deck_size",
        "pass": size_ok,
        "detail": f"{deck_size} cards" + (" — too thin" if deck_size < 12 else " — too bloated" if deck_size > 20 else " — good range"),
    })

    # Damage output
    avg_damage_per_draw = total_base_damage / max(1, deck_size) * 5  # ~5 card hand
    dmg_ok = avg_damage_per_draw >= 25
    checks.append({
        "check": "damage_output",
        "pass": dmg_ok,
        "detail": f"~{avg_damage_per_draw:.0f} base damage/turn" + (" — need more damage cards" if not dmg_ok else " — sufficient"),
    })

    # Block coverage
    avg_block_per_draw = total_base_block / max(1, deck_size) * 5
    blk_ok = avg_block_per_draw >= 15
    checks.append({
        "check": "block_output",
        "pass": blk_ok,
        "detail": f"~{avg_block_per_draw:.0f} base block/turn" + (" — need more block cards" if not blk_ok else " — sufficient"),
    })

    # Vulnerable source
    vuln_ok = vulnerable_sources >= 1
    checks.append({
        "check": "vulnerable_source",
        "pass": vuln_ok,
        "detail": f"{vulnerable_sources} source(s)" + (" — Bash alone may not be enough" if vulnerable_sources < 2 else ""),
    })

    # Draw cards
    draw_ok = draw_count >= 1
    checks.append({
        "check": "draw_cards",
        "pass": draw_ok,
        "detail": f"{draw_count} draw card(s)" + (" — deck cycling will be slow" if not draw_ok else ""),
    })

    # Potions
    potion_count = _count_occupied_potions(potions)
    checks.append({
        "check": "potions",
        "pass": potion_count >= 1,
        "detail": f"{potion_count} potion(s) — {'save for boss' if potion_count >= 1 else 'try to acquire one before boss'}",
    })

    passed = sum(1 for c in checks if c["pass"])
    total = len(checks)
    ready = passed >= total - 1  # allow 1 failed check

    return {
        "ready": ready,
        "score": f"{passed}/{total}",
        "checks": checks,
        "summary": "Deck is boss-ready." if ready else "Deck has gaps — prioritize fixing failed checks before boss.",
        "deck_stats": {
            "size": deck_size,
            "attacks": attack_count,
            "blocks": block_count,
            "draw": draw_count,
            "aoe": aoe_count,
            "vulnerable_sources": vulnerable_sources,
            "strength_sources": strength_sources,
        },
    }


def _score_card_for_deck(
    card_id: str,
    card_data: dict[str, Any],
    current_deck: list[dict[str, Any]],
    deck_size: int,
) -> dict[str, Any]:
    """Score a card reward option against the current deck composition."""
    score = 50  # baseline
    reasons: list[str] = []

    # Count deck composition
    attack_count = sum(1 for c in current_deck if _get_card_type_name(c) == "ATTACK")
    skill_count = sum(1 for c in current_deck if _get_card_type_name(c) == "SKILL")
    power_count = sum(1 for c in current_deck if _get_card_type_name(c) == "POWER")

    card_type = str(card_data.get("type", "")).upper()
    base_dmg = _coerce_int(card_data.get("damage"))
    base_blk = _coerce_int(card_data.get("block"))
    energy_cost = _get_energy_cost(card_data)
    has_draw = "draw" in str(card_data.get("powers_applied", "")).lower() or "draw" in str(card_data.get("description", "")).lower()

    # Deck size penalty — bigger decks need stronger cards to justify adding
    if deck_size > 18:
        score -= 10
        reasons.append("deck already large (>18)")
    elif deck_size < 12:
        score += 5
        reasons.append("deck is small, card adds consistency")

    # Value damage cards if deck is defense-heavy
    if attack_count < skill_count and base_dmg > 0:
        score += 10
        reasons.append("deck needs more damage")

    # Value block cards if deck is attack-heavy
    if skill_count < attack_count and base_blk > 0:
        score += 10
        reasons.append("deck needs more block")

    # Powers are generally valuable (one-time play, permanent effect)
    if card_type == "POWER":
        score += 15
        reasons.append("powers provide lasting value")

    # Draw is always valuable
    if has_draw:
        score += 12
        reasons.append("draw improves deck cycling")

    # Efficiency: damage/energy or block/energy
    if base_dmg and energy_cost:
        eff = base_dmg / max(1, energy_cost)
        if eff >= 8:
            score += 10
            reasons.append(f"high damage efficiency ({eff:.0f}/energy)")
    if base_blk and energy_cost:
        eff = base_blk / max(1, energy_cost)
        if eff >= 6:
            score += 8
            reasons.append(f"high block efficiency ({eff:.0f}/energy)")

    # AoE bonus — check for multi-target or "ALL" in description
    desc = str(card_data.get("description", "")).upper()
    if "ALL ENEM" in desc or "AOE" in desc:
        aoe_count = sum(1 for c in current_deck if "ALL ENEM" in _get_card_text(c).upper())
        if aoe_count == 0:
            score += 15
            reasons.append("deck has no AoE — this fills a critical gap")

    # 0-cost cards are almost always good
    if energy_cost == 0:
        score += 10
        reasons.append("0-cost = free value")

    return {
        "card_id": card_id,
        "score": min(100, max(0, score)),
        "reasons": reasons,
    }


def create_server(client: Sts2Client | None = None, tool_profile: str | None = None) -> FastMCP:
    sts2 = client or Sts2Client()
    knowledge = Sts2KnowledgeBase()
    handoff = Sts2HandoffService(knowledge)
    profile = _normalize_tool_profile(tool_profile)
    mcp = FastMCP("STS2 AI Agent")

    def _agent_state() -> dict[str, Any]:
        state = sts2.get_state()
        agent_view = state.get("agent_view")
        if isinstance(agent_view, dict):
            if "available_actions" not in agent_view and isinstance(agent_view.get("actions"), list):
                return {
                    **agent_view,
                    "available_actions": agent_view["actions"],
                }
            return agent_view
        return state

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

    @mcp.tool
    def get_combat_analysis() -> dict[str, Any]:
        """Compute effective damage/block values for every card in hand, considering all active powers.

        Returns per-card computed values and a tactical summary including:
        - Each hand card with `computed_damage`, `computed_block`, and `damage_per_energy`
        - Total available damage/block this turn (energy-limited)
        - Per-enemy lethal check (can this enemy be killed this turn?)
        - Incoming damage (total enemy intent damage vs available block)

        Call this at the start of each combat turn for informed decision-making.
        """
        state = sts2.get_state()
        return _compute_combat_analysis(state)

    @mcp.tool
    def evaluate_card_rewards() -> dict[str, Any]:
        """Score card reward options against your current deck composition.

        Call this when presented with card rewards after combat.
        Returns each reward card with a score (0-100) and reasons for/against picking it.
        Higher score = better fit for your current deck.
        """
        state = sts2.get_state()
        deck_cards = _extract_run_deck(state)
        deck_size = len(deck_cards)
        card_rewards = _extract_reward_card_options(state)

        if not card_rewards:
            return {"error": "no_card_rewards", "message": "No card rewards currently available."}

        card_data_index = _ensure_game_data_index("cards")
        results = []
        for card in card_rewards:
            cid = card.get("card_id", "")
            static = _lookup_game_data_item(index=card_data_index, item_id=cid)
            if not isinstance(static, dict):
                static = {}
            scored = _score_card_for_deck(cid, static, deck_cards, deck_size)
            scored["name"] = card.get("name", cid)
            results.append(scored)

        results.sort(key=lambda x: x["score"], reverse=True)
        return {
            "deck_size": deck_size,
            "recommendations": results,
            "advice": "Pick the highest-scored card, or skip all if no card scores above 60.",
        }

    @mcp.tool
    def check_boss_readiness() -> dict[str, Any]:
        """Check if your current deck, HP, and potions are ready for the boss fight.

        Call this before entering a boss node on the map.
        Returns pass/fail checks for HP, deck size, damage output, block coverage,
        vulnerable sources, draw cards, and potions.
        """
        state = sts2.get_state()
        return _check_boss_readiness(state)

    @mcp.tool
    def assess_elite_risk() -> dict[str, Any]:
        """Assess whether to take an elite fight based on current HP, deck, and potions.

        Call this when choosing a map path that includes an elite node.
        Returns TAKE or AVOID recommendation with risk/reward factors.
        """
        state = sts2.get_state()
        return _assess_elite_risk(state)

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
