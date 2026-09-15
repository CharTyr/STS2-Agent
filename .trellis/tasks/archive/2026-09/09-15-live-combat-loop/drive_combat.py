#!/usr/bin/env python3
from __future__ import annotations

import json
import time
import urllib.error
import urllib.request
from pathlib import Path

HOST = "http://127.0.0.1:18080"
COMP = "http://127.0.0.1:18081"
LOG = Path(__file__).with_name("drive_log.jsonl")


def req(base: str, method: str, path: str, body: dict | None = None, timeout: float = 20.0) -> dict:
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(
        base.rstrip("/") + path,
        data=data,
        method=method,
        headers={"Content-Type": "application/json"} if body is not None else {},
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.load(response)
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", "replace")
        try:
            parsed = json.loads(raw)
        except json.JSONDecodeError:
            parsed = {"raw": raw}
        return {"http_status": exc.code, "error": parsed}


def state(base: str) -> dict:
    payload = req(base, "GET", "/state", timeout=8)
    return payload.get("data") or payload


def log(event: str, **payload) -> None:
    row = {"t": time.strftime("%H:%M:%S"), "event": event, **payload}
    with LOG.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(row, ensure_ascii=False) + "\n")
    print(json.dumps(row, ensure_ascii=False), flush=True)


def compact(data: dict) -> dict:
    combat = data.get("combat") or {}
    hand = combat.get("hand") or []
    enemies = combat.get("enemies") or []
    reward = data.get("reward") or {}
    return {
        "screen": data.get("screen"),
        "actions": data.get("available_actions") or [],
        "energy": combat.get("energy"),
        "hp": (data.get("run") or {}).get("current_hp"),
        "hand": [
            {
                "i": i,
                "id": card.get("id"),
                "name": card.get("name"),
                "cost": card.get("cost"),
                "playable": card.get("is_playable") if card.get("is_playable") is not None else card.get("playable"),
                "requires_target": card.get("requires_target"),
            }
            for i, card in enumerate(hand)
        ],
        "enemies": [
            {
                "i": i,
                "name": enemy.get("name") or enemy.get("id"),
                "hp": enemy.get("current_hp") if enemy.get("current_hp") is not None else enemy.get("hp"),
            }
            for i, enemy in enumerate(enemies)
        ],
        "reward_keys": list(reward.keys()) if reward else [],
        "pending_card": reward.get("pending_card_choice") if reward else None,
        "can_proceed": reward.get("can_proceed") if reward else None,
    }


def act(base: str, action: str, **args) -> dict:
    body = {"action": action}
    body.update({key: value for key, value in args.items() if value is not None})
    before = compact(state(base))
    result = req(base, "POST", "/action", body, timeout=25)
    time.sleep(0.4)
    after = compact(state(base))
    log("action", base=base[-5:], action=action, args=args, before=before, after=after, result={
        "http_status": result.get("http_status"),
        "status": (result.get("data") or result).get("status") if isinstance(result.get("data") or result, dict) else None,
        "message": (result.get("data") or result).get("message") if isinstance(result.get("data") or result, dict) else None,
        "error": result.get("error"),
    })
    return result


def wait_until(predicate, timeout: float = 45.0, interval: float = 0.6) -> tuple[dict, dict]:
    deadline = time.time() + timeout
    last_host = last_comp = {}
    while time.time() < deadline:
        last_host = compact(state(HOST))
        last_comp = compact(state(COMP))
        if predicate(last_host, last_comp):
            return last_host, last_comp
        time.sleep(interval)
    return last_host, last_comp


def playable_cards(data: dict) -> list[dict]:
    return [card for card in data.get("hand") or [] if card.get("playable")]


def pick_card(data: dict) -> dict | None:
    cards = playable_cards(data)
    if not cards:
        return None
    attacks = [card for card in cards if str(card.get("id") or card.get("name") or "").upper() in {"STRIKE", "BASH", "CLEAVE", "TWINSTRIKE", "ANGER"} or "STRIKE" in str(card.get("id") or "").upper() or "BASH" in str(card.get("id") or "").upper()]
    return (attacks or cards)[0]


def drive_combat_side(base: str, label: str) -> str:
    data = compact(state(base))
    actions = set(data.get("actions") or [])
    if data.get("screen") not in {"COMBAT", "REWARD", "MAP", "EVENT", "SHOP", "REST", "CHEST", "UNKNOWN"} and data.get("screen"):
        return data["screen"]
    if "play_card" in actions:
        card = pick_card(data)
        if card is None:
            return data.get("screen") or "COMBAT"
        kwargs = {"card_index": card["i"]}
        if card.get("requires_target") and data.get("enemies"):
            kwargs["target_index"] = 0
        act(base, "play_card", **kwargs)
        return "acted"
    if "end_turn" in actions:
        act(base, "end_turn")
        return "ended"
    if "choose_reward_card" in actions:
        act(base, "choose_reward_card", option_index=0)
        return "reward"
    if "skip_reward_cards" in actions:
        act(base, "skip_reward_cards")
        return "reward"
    if "collect_rewards_and_proceed" in actions:
        act(base, "collect_rewards_and_proceed")
        return "reward"
    if "proceed" in actions:
        act(base, "proceed")
        return "proceed"
    if "choose_map_node" in actions:
        return "MAP"
    log("idle", label=label, data=data)
    return data.get("screen") or "idle"


def main() -> int:
    LOG.write_text("", encoding="utf-8")
    host = compact(state(HOST))
    comp = compact(state(COMP))
    log("start", host=host, comp=comp)

    if host.get("screen") == "MAP" and "choose_map_node" in host.get("actions", []):
        act(HOST, "choose_map_node", option_index=0)
    if compact(state(COMP)).get("screen") == "MAP" and "choose_map_node" in compact(state(COMP)).get("actions", []):
        act(COMP, "choose_map_node", option_index=0)

    host, comp = wait_until(
        lambda h, c: h.get("screen") in {"COMBAT", "REWARD"} or c.get("screen") in {"COMBAT", "REWARD"},
        timeout=40,
    )
    log("after_vote", host=host, comp=comp)

    deadline = time.time() + 240
    saw_combat = host.get("screen") == "COMBAT" or comp.get("screen") == "COMBAT"
    back_on_map = False
    while time.time() < deadline:
        host = compact(state(HOST))
        comp = compact(state(COMP))
        if host.get("screen") == "COMBAT" or comp.get("screen") == "COMBAT":
            saw_combat = True
        if saw_combat and host.get("screen") == "MAP" and comp.get("screen") == "MAP" and "choose_map_node" in (host.get("actions") or []):
            back_on_map = True
            break
        host_result = drive_combat_side(HOST, "host")
        comp_result = drive_combat_side(COMP, "comp")
        if host_result in {"acted", "ended", "reward", "proceed"} or comp_result in {"acted", "ended", "reward", "proceed"}:
            continue
        time.sleep(0.7)

    host = compact(state(HOST))
    comp = compact(state(COMP))
    log("done", saw_combat=saw_combat, back_on_map=back_on_map, host=host, comp=comp)
    print("RESULT", json.dumps({"saw_combat": saw_combat, "back_on_map": back_on_map, "host": host, "comp": comp}, ensure_ascii=False))
    return 0 if saw_combat and back_on_map else 2


if __name__ == "__main__":
    raise SystemExit(main())
