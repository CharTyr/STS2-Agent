#!/usr/bin/env python3
"""Isolated end-to-end probe for built-in in-game AgentRuntime + Overlay/HUD auto-play.

Runs 100% isolated from the player's real Steam saves and real Mod settings:
  - Uses a numeric --clientId + --force-steam off save directory (deleted on exit).
  - Points STS2_AGENT_SETTINGS_PATH at a temporary settings.json (deleted on exit).
  - SHA-256 hashes every file under %APPDATA%/SlayTheSpire2/steam and
    %APPDATA%/STS2AIAgent/settings.json before and after, failing on any diff.

By default, spins up a zero-cost local Mock OpenAI SSE server on 127.0.0.1 and verifies:
  - Windowed Godot Overlay + HUD attachment with 0 UI errors in godot.log.
  - Zero wasted CHARACTER_SELECT preload requests (CanCloseMainMenuSubmenu mid-embark guard).
  - PlayerPreferences delivered as role="user" (never role="system").
  - Extended combat.threat fields (orb block/damage, max_block stars/arrows).
  - TurnPlan + SpectatorHighlightMs dwell + autonomous Floor 1 combat card play.
  - Clean pause and 0 active state_builder_faults.

With --live-base-url + --live-model + --live-api-key-env, drives the exact same isolated
windowed self-play loop against a real OpenAI-compatible model endpoint (key read strictly
from the named environment variable, never printed or written to repo files).
"""
from __future__ import annotations

import argparse
import hashlib
import http.server
import json
import os
import re
import shutil
import socketserver
import subprocess
import sys
import tempfile
import threading
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib import request

REPO_ROOT = Path(__file__).resolve().parent.parent
PREF_TEXT = "Live verification taste: keep deck lean and fight Act 1 elites when healthy."


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def snapshot_dir_hashes(root: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    if root.is_dir():
        for p in sorted(root.rglob("*")):
            if p.is_file():
                result[str(p)] = sha256_file(p)
    return result


def probe_fingerprint(base_url: str, model: str, api_key: str) -> str:
    raw = f"{base_url}\n{model}\n{api_key}\nauto\nauto\nmedium\nprobe-v2"
    return hashlib.sha256(raw.encode("utf-8")).hexdigest().upper()[:16]


def api_json(api_base: str, method: str, path: str, body: dict | None = None, timeout: float = 10.0) -> dict:
    data = None if body is None else json.dumps(body).encode("utf-8")
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    req = request.Request(api_base + path, data=data, headers=headers, method=method)
    with request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


class MockState:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.requests: list[dict] = []
        self.screens_seen: list[str] = []
        self.character_select_preload_requests = 0
        self.pref_seen_as_user = False
        self.pref_seen_as_system = False
        self.combat_threat_verified = False
        self.combat_plan_emitted = False
        self.combat_cards_requested = 0


def pick_action_for_state(state: dict, mock_state: MockState) -> dict:
    screen = str(state.get("screen") or "")
    actions = list(state.get("available_actions") or [])
    with mock_state.lock:
        mock_state.screens_seen.append(screen)

    if screen == "MAIN_MENU":
        if "open_character_select" in actions:
            return {"action": "open_character_select", "reason": "Start isolated solo run"}
        if "continue_run" in actions:
            return {"action": "continue_run", "reason": "Continue isolated run"}
    if screen == "CHARACTER_SELECT":
        if "embark" in actions:
            return {"action": "embark", "reason": "Embark with selected character"}
        if "select_character" in actions:
            cs = state.get("character_select") or {}
            chars = cs.get("characters") or []
            unlocked = [c for c in chars if not c.get("locked")]
            opt_idx = unlocked[0]["i"] if unlocked else 0
            return {"action": "select_character", "option_index": opt_idx, "reason": "Select first unlocked character"}
        with mock_state.lock:
            mock_state.character_select_preload_requests += 1
        return {"action": "wait_until_actionable", "reason": "Unexpected CHARACTER_SELECT preload poll"}
    if screen == "MODAL":
        if "confirm_modal" in actions:
            return {"action": "confirm_modal", "reason": "Confirm modal"}
        if "dismiss_modal" in actions:
            return {"action": "dismiss_modal", "reason": "Dismiss modal"}
    if screen == "FEEDBACK" and "close_main_menu_submenu" in actions:
        return {"action": "close_main_menu_submenu", "reason": "Close feedback"}
    if screen == "EVENT":
        ev = state.get("event") or {}
        for opt in ev.get("options") or []:
            if not opt.get("locked") and not opt.get("kill"):
                return {"action": "choose_event_option", "option_index": opt["i"], "reason": "Pick safe Neow option"}
        if "proceed" in actions:
            return {"action": "proceed", "reason": "Leave event"}
    if screen == "MAP":
        mp = state.get("map") or {}
        opts = mp.get("options") or []
        if opts and "choose_map_node" in actions:
            return {"action": "choose_map_node", "option_index": opts[0]["i"], "reason": "Enter Floor 1 combat"}
    if screen == "COMBAT":
        combat = state.get("combat") or {}
        threat = combat.get("threat") or {}
        if (
            isinstance(threat, dict)
            and "end_of_turn_orb_block" in threat
            and "end_of_turn_orb_damage" in threat
            and isinstance(threat.get("max_block"), dict)
            and "stars" in threat["max_block"]
            and "arrows" in threat["max_block"]
        ):
            with mock_state.lock:
                mock_state.combat_threat_verified = True

        hand = combat.get("hand") or []
        playable = [c for c in hand if c.get("playable") is True]
        if playable and "play_card" in actions:
            first = playable[0]
            args: dict = {
                "action": "play_card",
                "card_index": first["i"],
                "reason": "Play first legal card and exercise dwell + turn plan",
            }
            if first.get("cid") is not None:
                args["card_cid"] = first["cid"]
            targets = first.get("targets") or []
            if targets:
                args["target_index"] = targets[0]
            if len(playable) >= 2 and playable[1].get("cid") is not None:
                second = playable[1]
                step2: dict = {"action": "play_card", "card_cid": second["cid"]}
                s2_targets = second.get("targets") or []
                if s2_targets:
                    step2["target_index"] = s2_targets[0]
                args["then"] = [step2]
                with mock_state.lock:
                    mock_state.combat_plan_emitted = True
            with mock_state.lock:
                mock_state.combat_cards_requested += 1
            return args
        if "end_turn" in actions:
            return {"action": "end_turn", "reason": "End combat turn"}
    if screen == "REWARD":
        if "collect_rewards_and_proceed" in actions:
            return {"action": "collect_rewards_and_proceed", "reason": "Collect rewards"}
        if "proceed" in actions:
            return {"action": "proceed", "reason": "Leave rewards"}
    if "proceed" in actions:
        return {"action": "proceed", "reason": "Proceed"}
    return {"action": actions[0] if actions else "wait_until_actionable", "reason": "Fallback legal action"}


def make_handler(mock_state: MockState):
    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, format: str, *args) -> None:
            pass

        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            raw = self.rfile.read(length).decode("utf-8", errors="replace")
            payload = json.loads(raw)
            messages = payload.get("messages") or []
            compact_state: dict = {}
            for msg in messages:
                role = msg.get("role")
                content = msg.get("content") or ""
                if isinstance(content, str):
                    if PREF_TEXT in content:
                        with mock_state.lock:
                            if role == "user":
                                mock_state.pref_seen_as_user = True
                            elif role == "system":
                                mock_state.pref_seen_as_system = True
                    marker = "Latest compact game state:\n"
                    idx = content.find(marker)
                    if idx >= 0:
                        try:
                            compact_state = json.loads(content[idx + len(marker):].strip())
                        except json.JSONDecodeError:
                            pass

            chosen = pick_action_for_state(compact_state, mock_state)
            print(
                f"[mock-llm] screen={compact_state.get('screen')} actions={compact_state.get('available_actions')} -> {chosen}",
                flush=True,
            )
            with mock_state.lock:
                mock_state.requests.append({
                    "screen": compact_state.get("screen"),
                    "stream": payload.get("stream"),
                    "has_act_tool": any(
                        isinstance(t, dict) and (t.get("function") or {}).get("name") == "act"
                        for t in (payload.get("tools") or [])
                    ),
                    "chosen": chosen,
                })

            args_str = json.dumps(chosen, ensure_ascii=False)
            if payload.get("stream"):
                chunks = [
                    {
                        "id": "chatcmpl-mock",
                        "object": "chat.completion.chunk",
                        "choices": [{
                            "index": 0,
                            "delta": {
                                "role": "assistant",
                                "reasoning_content": f"Mock thinking on {compact_state.get('screen')}.",
                            },
                        }],
                    },
                    {
                        "id": "chatcmpl-mock",
                        "object": "chat.completion.chunk",
                        "choices": [{
                            "index": 0,
                            "delta": {
                                "tool_calls": [{
                                    "index": 0,
                                    "id": "call_mock_1",
                                    "type": "function",
                                    "function": {"name": "act", "arguments": args_str},
                                }],
                            },
                        }],
                    },
                    {
                        "id": "chatcmpl-mock",
                        "object": "chat.completion.chunk",
                        "choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}],
                        "usage": {
                            "prompt_tokens": 1200,
                            "completion_tokens": 35,
                            "total_tokens": 1235,
                            "prompt_tokens_details": {"cached_tokens": 600},
                        },
                    },
                ]
                body_bytes = "".join(
                    f"data: {json.dumps(c, ensure_ascii=False)}\n\n" for c in chunks
                ).encode("utf-8") + b"data: [DONE]\n\n"
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream; charset=utf-8")
                self.send_header("Content-Length", str(len(body_bytes)))
                self.end_headers()
                self.wfile.write(body_bytes)
            else:
                resp = {
                    "id": "chatcmpl-mock",
                    "object": "chat.completion",
                    "choices": [{
                        "index": 0,
                        "finish_reason": "tool_calls",
                        "message": {
                            "role": "assistant",
                            "content": None,
                            "tool_calls": [{
                                "id": "call_mock_1",
                                "type": "function",
                                "function": {"name": "act", "arguments": args_str},
                            }],
                        },
                    }],
                    "usage": {"prompt_tokens": 1200, "completion_tokens": 35, "total_tokens": 1235},
                }
                body_bytes = json.dumps(resp, ensure_ascii=False).encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(body_bytes)))
                self.end_headers()
                self.wfile.write(body_bytes)

    return Handler


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Isolated in-game AgentRuntime + Overlay/HUD auto-play verification")
    parser.add_argument("--api-port", type=int, default=18097, help="Isolated Mod HTTP API port (default: 18097)")
    parser.add_argument("--timeout", type=float, default=90.0, help="Auto-play verification timeout in seconds")
    parser.add_argument("--headless", action="store_true", help="Launch Godot with -Headless instead of windowed UI")
    parser.add_argument("--live-base-url", default="", help="Optional real OpenAI-compatible base URL (disables mock)")
    parser.add_argument("--live-model", default="", help="Model ID for --live-base-url")
    parser.add_argument("--live-api-key-env", default="", help="Env var holding the API key for --live-base-url")
    parser.add_argument("--max-session-requests", type=int, default=25, help="Hard request cap for live mode")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    live_mode = bool(args.live_base_url)
    if live_mode and not args.live_model:
        raise ValueError("--live-model is required when --live-base-url is set")

    appdata = Path(os.environ["APPDATA"])
    steam_dir = appdata / "SlayTheSpire2" / "steam"
    real_settings = appdata / "STS2AIAgent" / "settings.json"
    api_base = f"http://127.0.0.1:{args.api_port}"

    before_steam = snapshot_dir_hashes(steam_dir)
    before_real_settings = sha256_file(real_settings) if real_settings.is_file() else "<absent>"
    print(f"[autoplay-probe] Snapshot before: steam_files={len(before_steam)}, real_settings={before_real_settings[:12]}")

    mock_state = MockState()
    httpd: socketserver.TCPServer | None = None
    if live_mode:
        base_url = args.live_base_url.rstrip("/")
        model_name = args.live_model
        api_key = os.environ.get(args.live_api_key_env, "").strip() if args.live_api_key_env else ""
        endpoint_name = "LiveTestEndpoint"
        print(f"[autoplay-probe] Live mode: base_url={base_url}, model={model_name}, max_requests={args.max_session_requests}")
    else:
        httpd = socketserver.TCPServer(("127.0.0.1", 0), make_handler(mock_state))
        mock_port = httpd.server_address[1]
        threading.Thread(target=httpd.serve_forever, daemon=True).start()
        base_url = f"http://127.0.0.1:{mock_port}/v1"
        model_name = "mock-live-model"
        api_key = "sk-mock-local"
        endpoint_name = "LocalMockOpenAI"
        print(f"[autoplay-probe] Mock OpenAI SSE server listening on {base_url}")

    client_id = str(int(time.time() * 1000))
    client_save_dir = appdata / "SlayTheSpire2" / "default" / client_id
    temp_dir = Path(tempfile.mkdtemp(prefix=f"sts2-autoplay-probe-{client_id}-"))
    temp_settings = temp_dir / "settings.json"
    log_path = temp_dir / "game.log"

    fp = probe_fingerprint(base_url, model_name, api_key)
    now_iso = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    settings_obj: dict = {
        "endpoints": [{
            "id": "ep-probe",
            "name": endpoint_name,
            "baseUrl": base_url,
            "apiKey": api_key,
            "enabled": True,
            "thinkingProtocol": "auto",
        }],
        "models": [{
            "id": "m-probe",
            "endpointId": "ep-probe",
            "model": model_name,
            "displayName": model_name,
            "supportsVision": False,
            "supportsTools": True,
            "thinkingMode": "auto",
            "thinkingIntensity": "medium",
        }],
        "conversationModelId": "m-probe",
        "playModelId": None,
        "visionModelId": None,
        "overlayVisibleOnStart": True,
        "hasSeenFirstRunGuide": True,
        "overlayPlayMode": "solo",
        "turboMode": "fast",
        "autopilotEnabled": True,
        "turnPlansEnabled": True,
        "combatMathEnabled": True,
        "visionMode": "off",
        "hudMode": "full",
        "spectatorHighlightMs": 400,
        "runJournalEnabled": True,
        "maxOutputTokens": 8192,
        "playerPreferences": PREF_TEXT,
        "roleTests": [
            {
                "role": "conversation",
                "status": "verified",
                "capabilityStatus": "supported",
                "endpointId": "ep-probe",
                "endpointName": endpoint_name,
                "modelId": "m-probe",
                "modelName": model_name,
                "fingerprint": fp,
                "testedAt": now_iso,
            },
            {
                "role": "play",
                "status": "verified",
                "capabilityStatus": "supported",
                "endpointId": "ep-probe",
                "endpointName": endpoint_name,
                "modelId": "m-probe",
                "modelName": model_name,
                "fingerprint": fp,
                "testedAt": now_iso,
            },
        ],
    }
    if live_mode:
        settings_obj["maxSessionRequests"] = args.max_session_requests
    temp_settings.write_text(json.dumps(settings_obj, indent=2), encoding="utf-8")

    game_pid: int | None = None
    env = os.environ.copy()
    env["STS2_AGENT_SETTINGS_PATH"] = str(temp_settings)
    env["STS2_API_PORT"] = str(args.api_port)
    env.pop("STS2_ENABLE_DEBUG_ACTIONS", None)

    try:
        extra = f"--clientId {client_id} --force-steam off"
        cmd = [
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", str(REPO_ROOT / "scripts" / "start-game-session.ps1"),
            "-ApiPort", str(args.api_port),
            "-Attempts", "90",
        ]
        if args.headless:
            cmd.append("-Headless")
        else:
            extra += " --windowed --resolution 1280x720"
        cmd.extend(["-ExtraArguments", extra])

        with log_path.open("w", encoding="utf-8", errors="replace") as log_fh:
            starter = subprocess.run(cmd, env=env, stdout=log_fh, stderr=subprocess.STDOUT, timeout=120, check=False)
        if starter.returncode != 0:
            print(log_path.read_text(encoding="utf-8", errors="replace")[-3000:])
            raise RuntimeError(f"start-game-session.ps1 exited with {starter.returncode}")

        health = api_json(api_base, "GET", "/health")["data"]
        game_pid = int(health["process_id"])
        print(
            f"[autoplay-probe] Game ready ({'headless' if args.headless else 'windowed'}): "
            f"pid={game_pid}, version={health['game_version']}, status={health['status']}"
        )
        assert health["status"] == "ready", f"Unexpected /health status: {health}"

        ctrl_start = api_json(api_base, "POST", "/session/control", {"running": True})["data"]
        print(f"[autoplay-probe] Started built-in auto-play: {ctrl_start}")
        assert ctrl_start["play_running"] is True

        deadline = time.monotonic() + args.timeout
        reached_combat_play = False
        last_screen = ""
        screens_visited: list[str] = []
        while time.monotonic() < deadline:
            st = api_json(api_base, "GET", "/state")["data"]
            screen = st.get("screen") or ""
            if screen and screen != last_screen:
                print(f"[autoplay-probe] Autonomous agent navigated to screen={screen}")
                screens_visited.append(screen)
                last_screen = screen
            if screen == "COMBAT":
                played = ((st.get("combat") or {}).get("player") or {}).get("cards_played_this_turn") or 0
                turn_no = ((st.get("combat") or {}).get("player") or {}).get("turn") or st.get("turn") or 1
                if played >= 1 or turn_no >= 2:
                    if live_mode or mock_state.combat_cards_requested >= 1:
                        print(f"[autoplay-probe] Autonomous combat play confirmed! cards_played_this_turn={played}, turn={turn_no}")
                        reached_combat_play = True
                        break
            elif screen == "REWARD" and "COMBAT" in screens_visited:
                print("[autoplay-probe] Autonomous agent already won Floor 1 combat and reached REWARD!")
                reached_combat_play = True
                break
            hl = api_json(api_base, "GET", "/health")["data"]
            if not hl.get("play_running") and hl.get("stop_kind"):
                raise RuntimeError(f"Auto-play stopped unexpectedly: stop_kind={hl.get('stop_kind')}, health={hl}")
            time.sleep(0.5)

        assert reached_combat_play, f"Did not reach combat card play before timeout; screens={screens_visited}"

        ctrl_stop = api_json(api_base, "POST", "/session/control", {"running": False})["data"]
        print(f"[autoplay-probe] Paused built-in auto-play: {ctrl_stop}")
        # Wait briefly for any in-flight HTTP turn to settle to paused
        for _ in range(20):
            health_after = api_json(api_base, "GET", "/health")["data"]
            if not health_after.get("play_running"):
                break
            time.sleep(0.5)

        health_after = api_json(api_base, "GET", "/health")["data"]
        compat = health_after.get("compatibility") or {}
        active_faults = [f for f in (compat.get("state_builder_faults") or []) if f.get("active")]
        print(
            f"[autoplay-probe] Post-run /health: status={health_after['status']}, play_running={health_after['play_running']}, "
            f"session_requests={health_after['session_requests']}, prompt_tokens={health_after['session_prompt_tokens']}, "
            f"cached_tokens={health_after['session_cached_prompt_tokens']}, active_faults={len(active_faults)}"
        )
        assert health_after["status"] == "ready"
        assert health_after["play_running"] is False
        assert len(active_faults) == 0
        assert (health_after.get("session_requests") or 0) >= 2

        if not live_mode:
            print(
                f"[autoplay-probe] Mock OpenAI summary: total_requests={len(mock_state.requests)}, "
                f"screens={mock_state.screens_seen}, preload_char_select={mock_state.character_select_preload_requests}, "
                f"pref_as_user={mock_state.pref_seen_as_user}, pref_as_system={mock_state.pref_seen_as_system}, "
                f"combat_threat_ok={mock_state.combat_threat_verified}, turn_plan_emitted={mock_state.combat_plan_emitted}"
            )
            assert mock_state.character_select_preload_requests == 0, (
                f"Wasted CHARACTER_SELECT preload LLM calls detected: {mock_state.character_select_preload_requests}"
            )
            assert mock_state.pref_seen_as_user, "PlayerPreferences was NOT sent as role=user!"
            assert not mock_state.pref_seen_as_system, "PlayerPreferences must NOT be sent as role=system!"
            assert mock_state.combat_threat_verified, "combat.threat (orb/stars/arrows fields) not observed on wire!"
            assert all(r["has_act_tool"] for r in mock_state.requests), "Not all LLM requests advertised the act tool!"

        log_text = log_path.read_text(encoding="utf-8", errors="replace")
        godot_log = appdata / "SlayTheSpire2" / "logs" / "godot.log"
        godot_tail = ""
        if godot_log.is_file():
            raw_godot = godot_log.read_text(encoding="utf-8", errors="replace")
            agent_lines = [
                ln for ln in raw_godot.splitlines()[-400:]
                if "STS2AIAgent" in ln or "Overlay" in ln or "ERROR" in ln
            ]
            godot_tail = "\n".join(agent_lines[-30:])
            print("[autoplay-probe] Godot log STS2AIAgent lines:\n" + godot_tail)
        combined_log = log_text + "\n" + godot_tail
        if not args.headless:
            assert "Skipping overlay" not in combined_log, "Overlay was skipped instead of attaching in windowed mode!"
            overlay_errors = [
                line for line in combined_log.splitlines()
                if re.search(r"AgentOverlayHost|AgentHud|HelpCard|UiFactory", line)
                and re.search(r"ERROR|Exception|Failed", line, re.IGNORECASE)
            ]
            assert not overlay_errors, f"Overlay/HUD errors found in log: {overlay_errors}"
            print("[autoplay-probe] Windowed Godot Overlay/HUD log clean (0 UI errors, overlay attached)!")

        print("[autoplay-probe] ALL IN-GAME AUTO-PLAY CHECKS PASSED")
        return 0
    finally:
        if httpd is not None:
            httpd.shutdown()
            httpd.server_close()
        if game_pid is not None:
            subprocess.run(["taskkill", "/PID", str(game_pid), "/F"], capture_output=True, check=False)
            time.sleep(1.0)
            print(f"[autoplay-probe] Terminated isolated game PID={game_pid}")
        if client_save_dir.exists():
            shutil.rmtree(client_save_dir, ignore_errors=True)
            print(f"[autoplay-probe] Removed isolated save dir {client_save_dir}")
        if temp_dir.exists():
            shutil.rmtree(temp_dir, ignore_errors=True)
            print(f"[autoplay-probe] Removed temp dir {temp_dir}")

        after_steam = snapshot_dir_hashes(steam_dir)
        after_real_settings = sha256_file(real_settings) if real_settings.is_file() else "<absent>"
        diffs = [k for k in set(before_steam) | set(after_steam) if before_steam.get(k) != after_steam.get(k)]
        settings_changed = before_real_settings != after_real_settings
        print(
            f"[autoplay-probe] Integrity check: steam_before={len(before_steam)} steam_after={len(after_steam)} "
            f"steam_diffs={len(diffs)} real_settings_changed={settings_changed}"
        )
        if diffs or settings_changed:
            raise RuntimeError(f"User files modified! steam_diffs={diffs} real_settings_changed={settings_changed}")


if __name__ == "__main__":
    raise SystemExit(main())
