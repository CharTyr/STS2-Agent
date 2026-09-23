"""Loading, indexing and querying the game metadata the MCP tools serve.

`GET /data/{collection}` hands back whole collections of cards, relics, monsters and the rest.
Turning those into answers is its own job: cache a collection, index it by id case-insensitively,
derive which ids the current screen makes relevant, project the fields a caller asked for, and
turn a failure into an error a model can act on.

None of that is tool registration, which is what `server.py` is for. They shared a file until
2026-09-18 and shared nothing else; the split was computed by asking which module-level names
nothing outside this concern references.

`_configure_game_data_loader` is the seam the tests use: the loader is injected rather than
imported, so none of this needs a running mod.
"""

from __future__ import annotations

import threading
from typing import Any, Callable

from .client import Sts2ApiError

# The separator a caller uses to pass several ids in one string argument.
ITEM_IDS_SEPARATOR = ","
KNOWN_ITEM_ID_KEYS = ("id", "ID", "Id")


KNOWN_GAME_DATA_COLLECTIONS = ("cards", "relics", "monsters", "potions", "events", "powers", "characters")


SCENE_MENU = "menu"


SCENE_COMBAT = "combat"


SCENE_SHOP = "shop"


SCENE_EVENT = "event"


SCENE_REWARD = "reward"


SCENE_CARD_SELECTION = "card_selection"


SCENE_CHEST = "chest"


SCENE_BUNDLE_SELECTION = "bundle_selection"


COMBAT_SCREEN_KEYWORDS = ("combat",)


COMBAT_SCREEN_NAMES = {"combat_reward", "combat_victory"}


SHOP_SCREEN_KEYWORDS = ("shop", "merchant")


EVENT_SCREEN_KEYWORDS = ("event",)


EVENT_SCREEN_NAMES = {"event_room", "ancient_event"}


REWARD_SCREEN_KEYWORDS = ("reward",)


CARD_SELECTION_SCREEN_KEYWORDS = ("card_selection",)


CHEST_SCREEN_KEYWORDS = ("chest",)


BUNDLE_SELECTION_SCREEN_KEYWORDS = ("bundle_selection",)


# Screen -> scene, in evaluation order: (scene, keywords, exact names). The order is part of the
# contract, not an implementation detail. "COMBAT_REWARD" carries the reward keyword and is claimed
# by the combat rule that runs first, because the combat screen is the one that names its payload
# `combat`; "BUNDLE_SELECTION" would be swallowed by any broader "selection" keyword, so card
# selection matches the full `card_selection` token instead. The `names` column carries the legacy
# spellings a scene has also been reported under, checked alongside the keyword; the offered-choice
# scenes need none, because every name the mod emits for them spells its own scene.
# Mirrors GameDataFilter.DetectScene, and tests/test_scene_field_alignment.py reads the C# test's
# expectation table back so the two cannot classify the same screen differently.
_SCENE_SCREEN_RULES: tuple[tuple[str, tuple[str, ...], set[str]], ...] = (
    (SCENE_SHOP, SHOP_SCREEN_KEYWORDS, set()),
    (SCENE_EVENT, EVENT_SCREEN_KEYWORDS, EVENT_SCREEN_NAMES),
    (SCENE_COMBAT, COMBAT_SCREEN_KEYWORDS, COMBAT_SCREEN_NAMES),
    (SCENE_REWARD, REWARD_SCREEN_KEYWORDS, set()),
    (SCENE_CARD_SELECTION, CARD_SELECTION_SCREEN_KEYWORDS, set()),
    (SCENE_CHEST, CHEST_SCREEN_KEYWORDS, set()),
    (SCENE_BUNDLE_SELECTION, BUNDLE_SELECTION_SCREEN_KEYWORDS, set()),
)


_GAME_DATA_COLLECTIONS: dict[str, Any] = {}


_GAME_DATA_INDEXES: dict[str, dict[str, Any]] = {}


_GAME_DATA_LOADER: Callable[[str], Any] | None = None


_GAME_DATA_LOCK = threading.RLock()


class GameDataUnavailableError(RuntimeError):
    """A game-data load failure that still knows what the mod said.

    The three game-data tools answer with an `error` object instead of raising, and the play
    skill tells an agent to branch on `error.code`, `error.retryable` and `error.status_code`
    exactly as it does for a failed action. Flattening the mod's envelope into a bare RuntimeError
    dropped those three fields for game-data calls only, so the skill's instruction was true for
    actions and silently false here. This keeps them.
    """

    def __init__(self, message: str, *, code: str, status_code: int, retryable: bool) -> None:
        super().__init__(message)
        self.code = code
        self.status_code = status_code
        self.retryable = retryable


# Default field sets per scene/context. These are used by `get_relevant_game_data` to
# minimize token usage by returning only the most relevant fields.
# Mirrors STS2AIAgent/Agent/GameDataFilter.cs SceneFieldSets, the reference side that the
# C# tests keep aligned with the export contract. tests/test_scene_field_alignment.py
# asserts both tables stay equal, so a change here needs the same change there.
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
        ],
        "powers": [
            "id",
            "name",
            "description",
            "type",
            "stack_type",
            "allow_negative",
        ],
        "potions": [
            "id",
            "name",
            "description",
            "rarity",
            "pool",
            "usage",
            "target_type",
        ],
    },
    SCENE_SHOP: {
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
            "keywords",
        ],
        "relics": [
            "id",
            "name",
            "description",
            "rarity",
            "pool",
            "is_melted",
        ],
        "potions": [
            "id",
            "name",
            "description",
            "rarity",
            "pool",
            "usage",
            "target_type",
        ],
    },
    SCENE_EVENT: {
        "events": [
            "id",
            "name",
            "type",
            "act",
            "description",
            "options",
        ],
    },
    # A card being offered is evaluated the same way whether it sits in a shop, behind a reward
    # screen, in a bundle or in a deck-selection grid: cost, type, rarity and description decide it.
    # These three scenes therefore share the shop card set instead of combat's larger one -- the
    # combat-only damage/block/vars/upgrade fields would ride along on every offer lookup, which is
    # exactly the bloat the projection exists to avoid. The chest shares the shop relic set for the
    # same reason. Gold/potion/relic reward rows carry no stable id, so those scenes declare no
    # collection for them and the run-level fallback answers instead of a guessed id.
    SCENE_REWARD: {
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
            "keywords",
        ],
    },
    SCENE_CARD_SELECTION: {
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
            "keywords",
        ],
    },
    SCENE_BUNDLE_SELECTION: {
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
            "keywords",
        ],
    },
    SCENE_CHEST: {
        "relics": [
            "id",
            "name",
            "description",
            "rarity",
            "pool",
            "is_melted",
        ],
    },
}


def _normalize_collection_name(collection: str) -> str:
    """The one spelling of a collection name: trimmed and lowercased.

    Both scene tables are keyed by this spelling, and a caller spells the collection by hand -- the
    tool's argument is free text -- so every lookup goes through here instead of depending on it.
    """
    return (collection or "").strip().lower()


def scene_field_set(scene: str, collection: str) -> list[str] | None:
    """The fields to project for one collection in one scene, or `None` when nothing is projected.

    The tool calls this with the caller's own spelling of both names, so the lookup is
    case-insensitive here rather than at each call site: the C# mirror's tables are
    OrdinalIgnoreCase dictionaries, and the same call has to answer the same way on both surfaces.
    A scene with no field set for the collection returns `None`, which is the tool's signal to hand
    back the item unprojected instead of an empty record.
    """
    collections = _SCENE_FIELD_SETS.get((scene or "").strip().lower())
    if not collections:
        return None
    return collections.get(_normalize_collection_name(collection))


def _configure_game_data_loader(loader: Callable[[str], Any]) -> None:
    global _GAME_DATA_LOADER
    with _GAME_DATA_LOCK:
        _GAME_DATA_LOADER = loader


def _reset_game_data_cache() -> None:
    with _GAME_DATA_LOCK:
        _GAME_DATA_COLLECTIONS.clear()
        _GAME_DATA_INDEXES.clear()


def _load_game_data_collection(collection: str) -> Any:
    normalized = collection.strip()
    if not normalized:
        raise KeyError("Unknown game data collection: ''")

    with _GAME_DATA_LOCK:
        if normalized in _GAME_DATA_COLLECTIONS:
            return _GAME_DATA_COLLECTIONS[normalized]
        loader = _GAME_DATA_LOADER

    if loader is None:
        raise RuntimeError("Game data loader is not configured.")

    try:
        data = loader(normalized)
    except Sts2ApiError as exc:
        if exc.status_code == 404 or exc.code == "collection_not_found":
            raise KeyError(f"Unknown game data collection: {normalized}") from exc
        # Keep the mod's envelope: the tools answer with an error object instead of raising, and
        # an agent branches on code/retryable/status_code for a failed action and a failed
        # game-data call alike. Flattening it here made those fields vanish for this path only.
        raise GameDataUnavailableError(
            str(exc), code=exc.code or "game_data_unavailable", status_code=exc.status_code,
            retryable=exc.retryable) from exc
    except Exception as exc:
        raise GameDataUnavailableError(
            f"Failed to load game data collection {normalized!r}: {exc}",
            code="game_data_unavailable", status_code=0, retryable=False) from exc

    if not isinstance(data, (dict, list)):
        raise TypeError(f"Unsupported data type for collection {normalized!r}: {type(data)}")

    with _GAME_DATA_LOCK:
        _GAME_DATA_COLLECTIONS[normalized] = data

    return data


# Scene-scoped id sources for get_relevant_game_data: (scene, collection) -> JSON paths into the
# /state payload, each ending in the collection id field. When the caller omits item_ids, these are
# the ids the current screen is about, so the tool matches its own description instead of requiring
# the caller to already know them. Mirrors STS2AIAgent/Agent/GameDataFilter.cs SceneItemSources;
# tests/test_scene_field_alignment.py keeps the two equal.
#
# The collection column is what keeps the answer honest: a screen that is about cards declares paths
# only under "cards", so a relic lookup on a card screen can never come back with card ids (and the
# other way round). A screen whose offered collection has no stable id at all -- the gold/potion/
# relic rows of a reward screen -- declares nothing and falls through to the run-level fallback
# rather than inventing an id.
_SCENE_ITEM_SOURCES: dict[str, dict[str, list[str]]] = {
    SCENE_COMBAT: {
        "cards": ["combat.hand[].card_id"],
        "monsters": ["combat.enemies[].enemy_id"],
        "powers": ["combat.player.powers[].power_id", "combat.enemies[].powers[].power_id"],
        "potions": ["run.potions[].potion_id"],
    },
    SCENE_SHOP: {
        # All three stock lists answered from the offer ids the shelf carries: `shop.cards[].card_id`,
        # `shop.relics[].relic_id` and `shop.potions[].potion_id` are the raw /state payload's own
        # fields. The compact agent_view drops the relic and potion ids (it keeps only the display
        # line), which is why the raw path is the one listed -- it is present whenever the shelf is.
        "cards": ["shop.cards[].card_id"],
        "relics": ["shop.relics[].relic_id"],
        "potions": ["shop.potions[].potion_id"],
    },
    SCENE_EVENT: {
        "events": ["event.event_id"],
    },
    # Offered cards and relics are named differently by the two views one /state response carries:
    # the raw payload says `reward.card_options[]` / `chest.relic_options[]`, the embedded compact
    # agent_view says `reward.cards[]` / `chest.relics[]`. Both are listed (raw first: it is the list
    # the screen builders write, and it is never absent while the screen is up) so a caller handing
    # over either view gets the made offer rather than the deck the player already owns.
    SCENE_REWARD: {
        "cards": ["reward.card_options[].card_id", "agent_view.reward.cards[].card_id"],
    },
    SCENE_CARD_SELECTION: {
        "cards": ["selection.cards[].card_id", "agent_view.selection.cards[].card_id"],
    },
    SCENE_CHEST: {
        "relics": ["chest.relic_options[].relic_id", "agent_view.chest.relics[].relic_id"],
    },
    SCENE_BUNDLE_SELECTION: {
        "cards": ["bundles[].cards[].card_id", "agent_view.bundles[].cards[].card_id"],
    },
}


# Used when the current scene declares no source for the collection: the run-level ids the player
# already owns, so a deck or relic lookup still answers on a screen that is about something else.
_FALLBACK_ITEM_SOURCES: dict[str, list[str]] = {
    "cards": ["run.deck[].card_id"],
    "relics": ["run.relics[].relic_id"],
    "potions": ["run.potions[].potion_id"],
    "monsters": ["combat.enemies[].enemy_id"],
    "powers": ["combat.player.powers[].power_id", "combat.enemies[].powers[].power_id"],
    "events": ["event.event_id"],
}


def _collect_path_ids(node: Any, tokens: list[str], ids: list[str], seen: set[str]) -> None:
    """Walk one a.b[].c path, appending ids in order and skipping what is not there."""
    if not tokens:
        return
    token = tokens[0]
    is_array = token.endswith("[]")
    name = token[:-2] if is_array else token
    if not isinstance(node, dict) or name not in node:
        return
    value = node[name]
    if len(tokens) == 1:
        if isinstance(value, str) and value and value not in seen:
            seen.add(value)
            ids.append(value)
        return
    if is_array:
        if isinstance(value, list):
            for item in value:
                _collect_path_ids(item, tokens[1:], ids, seen)
        return
    _collect_path_ids(value, tokens[1:], ids, seen)


def derive_relevant_item_ids(state: Any, collection: str, screen: str) -> list[str]:
    """Ids the current screen is about for one collection, deduplicated and in surface order.

    The scene picks the source, and the source is looked up *by collection*: the scene tables are
    keyed on collection, so a screen that offers cards can never answer a relic question with card
    ids, and a collection the screen does not offer answers from the run-level ids the player already
    owns. That fallback runs only when the scene's own source yields nothing -- an unknown collection,
    a screen whose scene payload is absent (FAKE_MERCHANT classifies as shop but carries no shop
    block), or a screen that simply does not offer the collection. An empty answer is a legitimate
    "nothing to look up here" rather than an error.

    `collection` is matched case-insensitively, the way the C# mirror's dictionaries are; the tool is
    the one place a caller spells it by hand, so "Cards" has to reach the same source as "cards".
    """
    scene = _detect_scene_from_screen(screen)
    collection_key = _normalize_collection_name(collection)
    paths = _SCENE_ITEM_SOURCES.get(scene, {}).get(collection_key)
    ids = _collect_ids_from_paths(state, paths)
    if not ids:
        # A scene can name a source whose payload is absent on that screen: FAKE_MERCHANT is
        # classified as shop, but the shop payload is null there because the merchant room the ids
        # come from does not exist. An empty scene answer therefore still falls back to the
        # run-level ids instead of reporting nothing at all.
        ids = _collect_ids_from_paths(state, _FALLBACK_ITEM_SOURCES.get(collection_key))
    return ids


def _collect_ids_from_paths(state: Any, paths: list[str] | None) -> list[str]:
    """Run every path of one source list over the state, deduplicated and in source order."""
    if not paths:
        return []
    ids: list[str] = []
    seen: set[str] = set()
    for path in paths:
        _collect_path_ids(state, path.split("."), ids, seen)
    return ids


def _add_case_insensitive_item_id(index: dict[str, Any], item_id: str, item: Any) -> None:
    normalized = item_id.strip()
    if not normalized:
        return
    index[normalized] = item
    index[normalized.upper()] = item
    index[normalized.lower()] = item


def _ensure_game_data_index(collection: str) -> dict[str, Any]:
    """Return a map of id -> item for a collection (builds index on first use)."""
    with _GAME_DATA_LOCK:
        cached_index = _GAME_DATA_INDEXES.get(collection)
        if cached_index is not None:
            return cached_index

    items = _load_game_data_collection(collection)
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

    with _GAME_DATA_LOCK:
        cached_index = _GAME_DATA_INDEXES.get(collection)
        if cached_index is not None:
            return cached_index
        _GAME_DATA_INDEXES[collection] = index
        return index


def _lookup_game_data_item(index: dict[str, Any], item_id: str) -> Any:
    return index.get(item_id) or index.get(item_id.upper()) or index.get(item_id.lower())


def _build_game_data_tool_error(collection: str, exc: Exception) -> dict[str, Any]:
    # Every branch carries code/status_code/retryable, the same three fields a failed action
    # exposes, so the play skill's "read error.code, retry only when error.retryable" holds here too.
    if isinstance(exc, KeyError):
        return {
            "error": {
                "type": "unknown_collection",
                "code": "collection_not_found",
                "status_code": 404,
                "retryable": False,
                "collection": collection,
                "message": str(exc),
                "available_collections": list(KNOWN_GAME_DATA_COLLECTIONS),
            }
        }

    if isinstance(exc, GameDataUnavailableError):
        return {
            "error": {
                "type": "game_data_unavailable",
                "code": exc.code,
                "status_code": exc.status_code,
                "retryable": exc.retryable,
                "collection": collection,
                "message": str(exc),
            }
        }

    # Any other RuntimeError (for example a loader that was never configured) still answers as
    # unavailable, with the fields present so the caller's branching never depends on which
    # internal path produced the failure.
    if isinstance(exc, RuntimeError):
        return {
            "error": {
                "type": "game_data_unavailable",
                "code": "game_data_unavailable",
                "status_code": 0,
                "retryable": False,
                "collection": collection,
                "message": str(exc),
            }
        }

    return {
        "error": {
            "type": "invalid_game_data",
            "code": "invalid_game_data",
            "status_code": 502,
            "retryable": False,
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


def _detect_scene_from_screen(screen: str) -> str:
    """Classify one /state screen name into the scene its metadata is scoped by.

    Walks _SCENE_SCREEN_RULES in order and takes the first rule whose keyword appears in the name or
    whose exact name matches. Order decides the overlapping names: a "COMBAT_REWARD" screen is a
    combat screen because the combat rule runs before the reward rule, and "BUNDLE_SELECTION" is not
    a card selection because only the full `card_selection` token matches that rule. Mirrors
    GameDataFilter.DetectScene in the C# mod.
    """
    normalized = (screen or "").lower()
    for scene, keywords, names in _SCENE_SCREEN_RULES:
        if normalized in names or any(keyword in normalized for keyword in keywords):
            return scene
    return SCENE_MENU
