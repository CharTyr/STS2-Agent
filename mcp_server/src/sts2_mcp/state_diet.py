"""Intra-frame compact state slimming and session-scoped static deduplication for MCP.

Mirrors `STS2AIAgent/Agent/PromptStateDiet.cs` (with `card_value_note` deduplicated per session
rather than dropped unconditionally on frame 1) and `STS2AIAgent/Server/McpStateSlimmer.cs`.
"""

from __future__ import annotations

from copy import deepcopy
import hashlib
import json
import threading
from typing import Any

_MAP_KEYS_OUTSIDE_GRAPH = frozenset({"open", "current", "options", "votes"})
_SOLO_PLAYER_KEYS = frozenset({"player_id", "is_local", "current_hp", "max_hp", "gold"})
_READINESS_KEYS = frozenset({
    "can_use_combat_actions",
    "reason",
    "running_action_type",
    "modal_open",
    "modal_type",
    "player_actions_disabled",
    "hand_in_card_play",
    "hand_in_card_selection",
    "local_turn_ready",
    "end_turn_kick",
})
_HAND_CARD_IDENTITY_KEYS = frozenset({
    "i",
    "cid",
    "card_id",
    "line",
    "playable",
    "energy_cost",
    "type",
})
_PLAYABLE_CARD_NOISE_KEYS = frozenset({
    "can_play_result",
    "why",
    "unplayable_reason",
    "unplayable_reason_raw",
    "unplayable_preventer_id",
    "unplayable_preventer_type",
})
_RETAINED_TOP_LEVEL_KEYS = frozenset({
    "screen",
    "available_actions",
    "in_combat",
    "run_id",
    "turn",
    "compact_agent_view",
})
_FALLBACK_ACTIVE_RUN_SCOPE = "__active_run__"


def _is_raw_fallback(state: dict[str, Any]) -> bool:
    return state.get("compact_agent_view") is False or "mod_version" in state


def _carries_nothing(value: Any) -> bool:
    if value is None:
        return True
    if isinstance(value, (list, str)):
        return len(value) == 0
    return False


def _is_single_player_run(state: dict[str, Any]) -> bool:
    multiplayer = state.get("multiplayer")
    if not isinstance(multiplayer, dict) or multiplayer.get("is_multiplayer") is not True:
        return True
    run = state.get("run")
    players = run.get("players") if isinstance(run, dict) else None
    return not isinstance(players, list) or len(players) <= 1


def _canonical_hash(value: Any) -> str:
    encoded = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest().upper()


def slim_compact_state(state: Any) -> Any:
    """Stateless intra-frame projection matching `McpStateSlimmer.SlimCompactState`."""
    if not isinstance(state, dict) or _is_raw_fallback(state):
        return state

    out: dict[str, Any] = {}
    for key, val in state.items():
        if val is None and key not in _RETAINED_TOP_LEVEL_KEYS:
            continue
        out[key] = val

    screen = out.get("screen")
    if not (isinstance(screen, str) and screen.upper() == "MAP"):
        map_obj = out.get("map")
        if isinstance(map_obj, dict):
            out["map"] = {k: v for k, v in map_obj.items() if k in _MAP_KEYS_OUTSIDE_GRAPH}

    has_combat = isinstance(out.get("combat"), dict)
    solo = _is_single_player_run(out)
    run = out.get("run")
    if isinstance(run, dict):
        new_run = dict(run)
        if has_combat:
            new_run.pop("piles", None)
        if solo and isinstance(new_run.get("players"), list):
            new_run["players"] = [
                {k: v for k, v in player.items() if k in _SOLO_PLAYER_KEYS}
                if isinstance(player, dict)
                else player
                for player in new_run["players"]
            ]
        out["run"] = new_run

    combat = out.get("combat")
    if isinstance(combat, dict):
        new_combat = dict(combat)
        if "action_readiness" in new_combat:
            if out.get("in_combat") is False:
                new_combat.pop("action_readiness", None)
            elif isinstance(new_combat["action_readiness"], dict):
                new_combat["action_readiness"] = {
                    k: v for k, v in new_combat["action_readiness"].items() if k in _READINESS_KEYS
                }

        hand = new_combat.get("hand")
        if isinstance(hand, list):
            slim_hand: list[Any] = []
            for card in hand:
                if not isinstance(card, dict):
                    slim_hand.append(card)
                    continue
                playable = card.get("playable") is True
                slim_card: dict[str, Any] = {}
                for k, v in card.items():
                    if k in _HAND_CARD_IDENTITY_KEYS:
                        slim_card[k] = v
                    elif playable and k in _PLAYABLE_CARD_NOISE_KEYS:
                        continue
                    elif _carries_nothing(v):
                        continue
                    else:
                        slim_card[k] = v
                slim_hand.append(slim_card)
            new_combat["hand"] = slim_hand
        out["combat"] = new_combat

    return out


class SessionStateDedup:
    """Thread-safe session tracker omitting unchanged static run/glossary/guidance blocks."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._last_run_scope: str | None = None
        self._deck_hash: str | None = None
        self._relic_desc_hash: str | None = None
        self._ascension_hash: str | None = None
        self._card_value_note_sent: bool = False
        self._glossary: dict[str, str] = {}
        self._last_guidance_hash: str | None = None

    def reset(self) -> None:
        with self._lock:
            self._reset_run_unlocked()
            self._last_guidance_hash = None

    def _reset_run_unlocked(self) -> None:
        self._last_run_scope = None
        self._deck_hash = None
        self._relic_desc_hash = None
        self._ascension_hash = None
        self._card_value_note_sent = False
        self._glossary.clear()

    def apply_to_state(self, state: Any, *, full_state: bool = False) -> Any:
        slimmed = slim_compact_state(state)
        if not isinstance(slimmed, dict) or _is_raw_fallback(slimmed):
            return slimmed
        working = deepcopy(slimmed)
        with self._lock:
            self._dedup_state_unlocked(working, full_state=full_state)
        return working

    def apply_to_wait_result(self, result: Any, *, raw_state: bool = False) -> Any:
        if raw_state or not isinstance(result, dict):
            return result
        state = result.get("state")
        if not isinstance(state, dict) or _is_raw_fallback(state):
            return result
        return {**result, "state": self.apply_to_state(state, full_state=False)}

    def apply_to_act_result(self, result: Any, *, raw_state: bool = False) -> Any:
        if raw_state or not isinstance(result, dict) or "error" in result:
            return result
        out = dict(result)
        if isinstance(out.get("plan"), dict):
            final_state = out.get("final_state")
            if isinstance(final_state, dict):
                out["final_state"] = self.apply_to_state(final_state, full_state=False)
                out.pop("state", None)
            elif isinstance(out.get("state"), dict):
                out["state"] = self.apply_to_state(out["state"], full_state=False)
            return out

        if isinstance(out.get("state"), dict):
            out["state"] = self.apply_to_state(out["state"], full_state=False)
            if isinstance(out.get("previous"), dict) and "state" in out["previous"]:
                out["previous"] = {k: v for k, v in out["previous"].items() if k != "state"}

        recon = out.get("reconciliation")
        if isinstance(recon, dict) and isinstance(recon.get("state"), dict):
            out["reconciliation"] = {
                **recon,
                "state": self.apply_to_state(recon["state"], full_state=False),
            }
        return out

    def apply_to_decide_result(self, result: Any) -> Any:
        if not isinstance(result, dict):
            return result
        out = dict(result)
        with self._lock:
            state = out.get("state")
            if isinstance(state, dict) and not _is_raw_fallback(state):
                slim_state = deepcopy(slim_compact_state(state))
                self._dedup_state_unlocked(slim_state, full_state=False)
                out["state"] = slim_state

            guidance = out.get("scene_guidance")
            if isinstance(guidance, dict):
                guidance_hash = _canonical_hash(guidance)
                if self._last_guidance_hash == guidance_hash:
                    out["scene_guidance"] = {
                        "screen": guidance.get("screen"),
                        "scene": guidance.get("scene"),
                        "guidance_unchanged": True,
                    }
                else:
                    self._last_guidance_hash = guidance_hash
        return out

    def _dedup_state_unlocked(self, state: dict[str, Any], *, full_state: bool) -> None:
        run_scope = self._read_run_scope(state)
        if run_scope is None:
            self._reset_run_unlocked()
            if full_state:
                self._last_guidance_hash = None
            return

        if full_state or self._last_run_scope != run_scope:
            self._reset_run_unlocked()
            self._last_run_scope = run_scope
            if full_state:
                self._last_guidance_hash = None

        run = state.get("run")
        if isinstance(run, dict):
            deck = run.get("deck")
            if isinstance(deck, list) and len(deck) > 0:
                deck_hash = _canonical_hash(deck)
                deck_groups = len(deck)
                if not full_state and self._deck_hash == deck_hash:
                    run.pop("deck", None)
                    run["deck_unchanged"] = True
                    run["deck_groups"] = deck_groups
                else:
                    self._deck_hash = deck_hash
            elif "deck" in run:
                self._deck_hash = None

            relic_descs = run.get("relic_descriptions")
            if isinstance(relic_descs, list) and len(relic_descs) > 0:
                relic_hash = _canonical_hash(
                    [run.get("relics"), run.get("relic_ids"), relic_descs]
                )
                if not full_state and self._relic_desc_hash == relic_hash:
                    run.pop("relic_descriptions", None)
                    run["relic_descriptions_unchanged"] = True
                else:
                    self._relic_desc_hash = relic_hash
            elif "relic_descriptions" in run:
                self._relic_desc_hash = None

            ascension_effects = run.get("ascension_effects")
            if isinstance(ascension_effects, list) and len(ascension_effects) > 0:
                asc_hash = _canonical_hash(ascension_effects)
                if not full_state and self._ascension_hash == asc_hash:
                    run.pop("ascension_effects", None)
                    run["ascension_effects_unchanged"] = True
                else:
                    self._ascension_hash = asc_hash
            elif "ascension_effects" in run:
                self._ascension_hash = None

        combat = state.get("combat")
        if isinstance(combat, dict):
            note = combat.get("card_value_note")
            if isinstance(note, str) and note:
                if not full_state and self._card_value_note_sent:
                    combat.pop("card_value_note", None)
                else:
                    self._card_value_note_sent = True

        glossary = state.get("glossary")
        if isinstance(glossary, dict):
            if len(glossary) == 0:
                self._glossary.clear()
            else:
                to_remove: list[str] = []
                for term, definition in glossary.items():
                    def_text = json.dumps(definition, ensure_ascii=False, sort_keys=True)
                    if not full_state and self._glossary.get(term) == def_text:
                        to_remove.append(term)
                    else:
                        self._glossary[term] = def_text

                if to_remove:
                    for term in to_remove:
                        glossary.pop(term, None)
                    if not glossary:
                        state.pop("glossary", None)
                        state["glossary_unchanged"] = True
                    else:
                        state["glossary_delta"] = True

    @staticmethod
    def _read_run_scope(state: dict[str, Any]) -> str | None:
        run_id = state.get("run_id")
        if isinstance(run_id, str):
            trimmed = run_id.strip()
            if trimmed and trimmed != "run_unknown":
                return trimmed
        if isinstance(state.get("run"), dict):
            return _FALLBACK_ACTIVE_RUN_SCOPE
        glossary = state.get("glossary")
        if isinstance(state.get("combat"), dict) or (isinstance(glossary, dict) and len(glossary) > 0):
            return _FALLBACK_ACTIVE_RUN_SCOPE
        return None


class SessionStateDedupRegistry:
    """Bounded LRU registry of per-session `SessionStateDedup` trackers."""

    def __init__(self, max_sessions: int = 16) -> None:
        self._max_sessions = max(1, max_sessions)
        self._lock = threading.Lock()
        self._sessions: dict[str, SessionStateDedup] = {}

    def get(self, session_id: str | None) -> SessionStateDedup:
        key = session_id.strip() if isinstance(session_id, str) and session_id.strip() else "__default__"
        with self._lock:
            existing = self._sessions.pop(key, None)
            if existing is not None:
                self._sessions[key] = existing
                return existing
            if len(self._sessions) >= self._max_sessions:
                oldest = next(iter(self._sessions))
                self._sessions.pop(oldest, None)
            created = SessionStateDedup()
            self._sessions[key] = created
            return created
