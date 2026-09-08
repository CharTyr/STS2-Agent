#!/usr/bin/env python3
"""Validation budget proxy. Never claims an exact token hard cap.

Real ledger and test ledgers must be different files. Fixture HTTP statuses
do not count as upstream attempts. Credentials are used only when forwarding
allowlisted routes.
"""
from __future__ import annotations

import argparse
import json
import os
import ssl
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

MAX_REQUESTS_DEFAULT = 100
MAX_TOKENS_DEFAULT = 200000
ALLOWED_GET = frozenset({"/v1/models"})
ALLOWED_POST = frozenset({"/v1/chat/completions"})
DEFAULT_ALLOWED_MODEL = "MiniMaxAI/MiniMax-M3"
HOP_BY_HOP = {
    "connection",
    "keep-alive",
    "proxy-authenticate",
    "proxy-authorization",
    "te",
    "trailers",
    "transfer-encoding",
    "upgrade",
    "host",
    "content-length",
    "authorization",
    "user-agent",
    "origin",
    "referer",
    "cookie",
}


class LedgerError(RuntimeError):
    pass


def utc_now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def normalize_path(path: str) -> str:
    raw = (path or "/").split("?", 1)[0]
    if raw != "/" and raw.endswith("/"):
        raw = raw[:-1]
    return raw


def parse_usage(obj: object) -> int | None:
    if not isinstance(obj, dict):
        return None
    usage = obj.get("usage")
    if not isinstance(usage, dict):
        return None
    total = usage.get("total_tokens")
    if isinstance(total, int) and total >= 0:
        return total
    prompt = usage.get("prompt_tokens")
    completion = usage.get("completion_tokens")
    if isinstance(prompt, int) or isinstance(completion, int):
        return max(0, int(prompt or 0)) + max(0, int(completion or 0))
    return None


def parse_sse_usage(raw: bytes) -> int | None:
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        text = raw.decode("utf-8", errors="replace")
    found: int | None = None
    for line in text.splitlines():
        if not line.startswith("data:"):
            continue
        data = line[5:].strip()
        if not data or data == "[DONE]":
            continue
        try:
            parsed = json.loads(data)
        except json.JSONDecodeError:
            continue
        usage = parse_usage(parsed)
        if usage is not None:
            found = usage
    return found


def empty_ledger(max_requests: int, max_tokens: int) -> dict[str, Any]:
    return {
        "schema": 2,
        "max_requests": max_requests,
        "max_tokens": max_tokens,
        "requests": 0,
        "tokens_known": 0,
        "unknown_usage_requests": 0,
        "stopped": False,
        "stop_reason": None,
        "usage_complete": True,
        "token_hard_cap_claimed": False,
        "entries": [],
    }


def validate_ledger(loaded: object, path: Path) -> dict[str, Any]:
    if not isinstance(loaded, dict):
        raise LedgerError("fail closed: ledger is not an object: " + str(path))
    required_int = ("requests", "tokens_known", "unknown_usage_requests")
    for key in required_int:
        value = loaded.get(key)
        if not isinstance(value, int) or value < 0:
            raise LedgerError("fail closed: invalid " + key + " in " + str(path))
    entries = loaded.get("entries")
    if not isinstance(entries, list):
        raise LedgerError("fail closed: entries must be a list: " + str(path))
    if loaded["requests"] < len(entries):
        raise LedgerError("fail closed: requests smaller than entries: " + str(path))
    return loaded


class Budget:
    def __init__(self, path: Path, max_requests: int, max_tokens: int) -> None:
        if max_requests < 1 or max_tokens < 1:
            raise LedgerError("fail closed: caps must be positive")
        self.path = path
        self.max_requests = max_requests
        self.max_tokens = max_tokens
        self.lock = threading.Lock()
        if path.exists():
            try:
                loaded = json.loads(path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as exc:
                raise LedgerError("fail closed: corrupt ledger JSON: " + str(path)) from exc
            loaded = validate_ledger(loaded, path)
            self.data = empty_ledger(max_requests, max_tokens)
            self.data["requests"] = loaded["requests"]
            self.data["tokens_known"] = loaded["tokens_known"]
            self.data["unknown_usage_requests"] = loaded["unknown_usage_requests"]
            self.data["entries"] = loaded["entries"]
            self.data["stopped"] = bool(loaded.get("stopped"))
            self.data["stop_reason"] = loaded.get("stop_reason")
            if loaded.get("stopped"):
                self.data["stopped"] = True
                self.data["stop_reason"] = loaded.get("stop_reason") or "persisted_stop"
            self._recompute_flags()
            if loaded.get("stopped"):
                self.data["stopped"] = True
                self.data["stop_reason"] = loaded.get("stop_reason") or self.data.get("stop_reason") or "persisted_stop"
        else:
            self.data = empty_ledger(max_requests, max_tokens)
        self._recompute_flags()
        self._flush()

    def _copy_unlocked(self) -> dict[str, Any]:
        return json.loads(json.dumps(self.data))

    def _recompute_flags(self) -> None:
        unknown = int(self.data["unknown_usage_requests"])
        self.data["usage_complete"] = unknown == 0
        self.data["token_hard_cap_claimed"] = False
        self.data["max_requests"] = self.max_requests
        self.data["max_tokens"] = self.max_tokens
        if self.data["requests"] >= self.max_requests:
            self.data["stopped"] = True
            self.data["stop_reason"] = self.data.get("stop_reason") or "max_requests"
        if self.data["tokens_known"] >= self.max_tokens:
            self.data["stopped"] = True
            if not self.data.get("stop_reason"):
                self.data["stop_reason"] = "max_tokens"

    def _flush(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._recompute_flags()
        payload = json.dumps(self.data, ensure_ascii=False, indent=2)
        tmp = self.path.parent / (self.path.name + "." + str(os.getpid()) + "." + str(threading.get_ident()) + ".tmp")
        tmp.write_text(payload, encoding="utf-8")
        last_err: Exception | None = None
        for attempt in range(8):
            try:
                os.replace(tmp, self.path)
                return
            except OSError as exc:
                last_err = exc
                time.sleep(0.02 * (attempt + 1))
        self.path.write_text(payload, encoding="utf-8")
        try:
            tmp.unlink()
        except OSError:
            pass
        if last_err is None:
            return

    def snapshot(self) -> dict[str, Any]:
        with self.lock:
            return self._copy_unlocked()

    def begin(self, kind: str, path: str) -> tuple[bool, dict[str, Any]]:
        with self.lock:
            self._recompute_flags()
            if self.data["stopped"] or self.data["requests"] >= self.max_requests:
                self.data["stopped"] = True
                self.data["stop_reason"] = self.data.get("stop_reason") or "max_requests"
                self._flush()
                return False, self._copy_unlocked()
            self.data["requests"] += 1
            entry = {
                "n": self.data["requests"],
                "at": utc_now(),
                "kind": kind,
                "path": path,
                "tokens": None,
                "usage_unknown": True,
            }
            self.data["entries"].append(entry)
            self._flush()
            return True, entry

    def finish(self, entry: dict[str, Any], tokens: int | None) -> None:
        with self.lock:
            if tokens is None:
                self.data["unknown_usage_requests"] += 1
                entry["usage_unknown"] = True
                entry["tokens"] = None
            else:
                entry["usage_unknown"] = False
                entry["tokens"] = int(tokens)
                self.data["tokens_known"] += int(tokens)
            self._flush()



def dump_last_completion(parsed: dict[str, Any], entry: dict[str, Any]) -> None:
    choice = {}
    choices = parsed.get("choices") if isinstance(parsed, dict) else None
    if isinstance(choices, list) and choices and isinstance(choices[0], dict):
        choice = choices[0]
    message = choice.get("message") if isinstance(choice.get("message"), dict) else {}
    tool_calls = message.get("tool_calls") if isinstance(message.get("tool_calls"), list) else []
    names = []
    for call in tool_calls:
        if isinstance(call, dict):
            fn = (call.get("function") or {}).get("name") if isinstance(call.get("function"), dict) else call.get("name")
            names.append(str(fn or ""))
    payload = {
        "at": utc_now(),
        "n": entry.get("n"),
        "id": parsed.get("id") if isinstance(parsed, dict) else None,
        "model": parsed.get("model") if isinstance(parsed, dict) else None,
        "finish_reason": choice.get("finish_reason"),
        "content_len": len(message.get("content") or "") if isinstance(message.get("content"), str) else 0,
        "tool_names": names,
        "tool_count": len(tool_calls),
        "usage": parsed.get("usage") if isinstance(parsed, dict) else None,
    }
    path = Path(r"C:\Users\chart\Documents\project\sp\build\validation-2026-09-08\last-llm.json")
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


class Handler(BaseHTTPRequestHandler):
    server_version = "STS2BudgetProxy/1.1"

    def log_message(self, fmt: str, *args: object) -> None:
        sys.stderr.write("[budget-proxy] " + (fmt % args) + "\n")

    def _send_json(self, code: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        path = normalize_path(self.path)
        if path in {"/health", "/validation/health"}:
            self._send_json(
                200,
                {
                    "ok": True,
                    "role": "budget-proxy",
                    "fixture": self.server.fixture,
                    "counts_upstream": self.server.fixture == "none",
                    "token_hard_cap_claimed": False,
                },
            )
            return
        if path in {"/budget", "/validation/ledger"}:
            self._send_json(200, self.server.budget.snapshot())
            return
        self._forward("GET")

    def do_POST(self) -> None:  # noqa: N802
        self._forward("POST")

    def _reject_allowlist(self, method: str, path: str) -> None:
        self._send_json(
            403,
            {
                "error": {
                    "message": "proxy allowlist: only GET /v1/models and POST /v1/chat/completions",
                    "type": "forbidden",
                    "method": method,
                    "path": path,
                }
            },
        )

    def _forward(self, method: str) -> None:
        path = normalize_path(self.path)
        fixture = self.server.fixture
        if fixture == "401":
            self._send_json(401, {"error": {"message": "fixture unauthorized", "type": "invalid_request_error", "counted": False}})
            return
        if fixture == "429":
            self._send_json(429, {"error": {"message": "fixture rate limited", "type": "rate_limit_error", "counted": False}})
            return
        if fixture == "timeout":
            time.sleep(self.server.fixture_timeout)
            self._send_json(504, {"error": {"message": "fixture timeout", "type": "timeout", "counted": False}})
            return

        allowed = (method == "GET" and path in ALLOWED_GET) or (method == "POST" and path in ALLOWED_POST)
        if not allowed:
            self._reject_allowlist(method, path)
            return

        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length) if length > 0 else b""
        if method == "POST":
            try:
                payload = json.loads(body.decode("utf-8") if body else "{}")
            except (UnicodeDecodeError, json.JSONDecodeError):
                self._send_json(400, {"error": {"message": "invalid json body", "type": "invalid_request_error"}})
                return
            model = payload.get("model") if isinstance(payload, dict) else None
            if model != self.server.allowed_model:
                self._send_json(
                    400,
                    {
                        "error": {
                            "message": "model not allowed by validation proxy",
                            "type": "invalid_request_error",
                            "allowed_model": self.server.allowed_model,
                        }
                    },
                )
                return

        allowed_begin, entry_or_snap = self.server.budget.begin(method, path)
        if not allowed_begin:
            snap = entry_or_snap
            self._send_json(
                429,
                {
                    "error": {
                        "message": "validation budget exhausted",
                        "type": "rate_limit_error",
                        "stop_reason": snap.get("stop_reason"),
                        "requests": snap.get("requests"),
                        "tokens_known": snap.get("tokens_known"),
                        "usage_complete": snap.get("usage_complete"),
                        "token_hard_cap_claimed": False,
                    }
                },
            )
            return

        upstream = self.server.upstream.rstrip("/") + path
        headers: dict[str, str] = {}
        for key, value in self.headers.items():
            if key.lower() in HOP_BY_HOP:
                continue
            headers[key] = value
        key = os.environ.get("STS2_VALIDATION_UPSTREAM_KEY", "")
        if key:
            headers["Authorization"] = "Bearer " + key
        headers["User-Agent"] = "Mozilla/5.0 STS2-Validation-Proxy/1.0"
        req = urllib.request.Request(upstream, data=body if method != "GET" else None, headers=headers, method=method)
        ctx = ssl.create_default_context()
        tokens: int | None = None
        try:
            with urllib.request.urlopen(req, context=ctx, timeout=self.server.upstream_timeout) as resp:
                raw = resp.read()
                content_type = resp.headers.get("Content-Type", "application/json")
                try:
                    parsed = json.loads(raw.decode("utf-8"))
                    tokens = parse_usage(parsed)
                    try:
                        dump_last_completion(parsed, entry_or_snap)
                    except Exception as dump_err:
                        sys.stderr.write("[budget-proxy] dump failed " + type(dump_err).__name__ + "\n")
                except (UnicodeDecodeError, json.JSONDecodeError):
                    tokens = parse_sse_usage(raw)
                self.server.budget.finish(entry_or_snap, tokens)
                self.send_response(resp.status)
                self.send_header("Content-Type", content_type)
                self.send_header("Content-Length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)
        except urllib.error.HTTPError as err:
            raw = err.read() or b"{}"
            try:
                parsed = json.loads(raw.decode("utf-8"))
                tokens = parse_usage(parsed)
            except (UnicodeDecodeError, json.JSONDecodeError):
                tokens = parse_sse_usage(raw)
            self.server.budget.finish(entry_or_snap, tokens)
            sys.stderr.write("[budget-proxy] upstream HTTP " + str(err.code) + "\n")
            self.send_response(err.code)
            self.send_header("Content-Type", err.headers.get("Content-Type", "application/json"))
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)
        except Exception as err:  # noqa: BLE001
            self.server.budget.finish(entry_or_snap, None)
            self._send_json(502, {"error": {"message": "upstream proxy failure", "type": "proxy_error"}})
            sys.stderr.write("[budget-proxy] upstream failure type=" + type(err).__name__ + "\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--listen", default="127.0.0.1:18090")
    parser.add_argument("--upstream", default="https://api.gmi-serving.com")
    parser.add_argument("--ledger", required=True)
    parser.add_argument("--max-requests", type=int, default=MAX_REQUESTS_DEFAULT)
    parser.add_argument("--max-tokens", type=int, default=MAX_TOKENS_DEFAULT)
    parser.add_argument("--allowed-model", default=DEFAULT_ALLOWED_MODEL)
    parser.add_argument("--fixture", choices=["none", "401", "429", "timeout"], default="none")
    parser.add_argument("--fixture-timeout", type=float, default=2.0)
    parser.add_argument("--upstream-timeout", type=float, default=720.0)
    args = parser.parse_args()
    ledger_path = Path(args.ledger)
    try:
        budget = Budget(ledger_path, args.max_requests, args.max_tokens)
    except LedgerError as exc:
        sys.stderr.write("[budget-proxy] " + str(exc) + "\n")
        return 2
    host, port_s = args.listen.rsplit(":", 1)
    httpd = ThreadingHTTPServer((host, int(port_s)), Handler)
    httpd.upstream = args.upstream
    httpd.budget = budget
    httpd.fixture = args.fixture
    httpd.fixture_timeout = args.fixture_timeout
    httpd.upstream_timeout = args.upstream_timeout
    httpd.allowed_model = args.allowed_model
    sys.stderr.write(
        "[budget-proxy] listen="
        + args.listen
        + " fixture="
        + args.fixture
        + " ledger="
        + str(ledger_path)
        + " allowed_model="
        + args.allowed_model
        + "\n"
    )
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        return 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())



