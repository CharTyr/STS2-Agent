#!/usr/bin/env python3
"""No-cost self-tests for the validation budget proxy.

Uses a private test ledger directory. Never touches the real budget-ledger.json
and never calls the paid upstream.
"""
from __future__ import annotations

import json
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib.util

PROXY_PATH = Path(__file__).resolve().with_name("sts2-model-budget-proxy.py")
SPEC = importlib.util.spec_from_file_location("sts2_budget_proxy", PROXY_PATH)
PROXY = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(PROXY)

TEST_DIR = ROOT / "build" / "validation-2026-09-08" / "test-ledgers"
REPORT = ROOT / "build" / "validation-2026-09-08" / "budget-proxy-selftest.json"


class Fail(Exception):
    pass


def write_json(path: Path, obj: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2), encoding="utf-8")


def fresh_ledger(dir_path: Path, name: str) -> Path:
    path = dir_path / name
    if path.exists():
        path.unlink()
    return path


def test_corrupt_fail_closed(dir_path: Path) -> str:
    path = fresh_ledger(dir_path, "corrupt.json")
    path.write_text("{not json", encoding="utf-8")
    try:
        PROXY.Budget(path, 100, 200000)
    except PROXY.LedgerError:
        return "ok"
    raise Fail("corrupt ledger must fail closed")


def test_restart_preserves(dir_path: Path) -> str:
    path = fresh_ledger(dir_path, "restart.json")
    first = PROXY.Budget(path, 100, 200000)
    ok, entry = first.begin("POST", "/v1/chat/completions")
    if not ok:
        raise Fail("first begin should succeed")
    first.finish(entry, 41)
    ok2, entry2 = first.begin("GET", "/v1/models")
    if not ok2:
        raise Fail("second begin should succeed")
    first.finish(entry2, None)
    second = PROXY.Budget(path, 100, 200000)
    snap = second.snapshot()
    if snap["requests"] != 2:
        raise Fail("restart cleared requests: " + str(snap["requests"]))
    if snap["tokens_known"] != 41:
        raise Fail("restart cleared tokens")
    if snap["unknown_usage_requests"] != 1:
        raise Fail("restart cleared unknown usage")
    if snap.get("token_hard_cap_claimed") is not False:
        raise Fail("must never claim exact token hard cap")
    if snap.get("usage_complete") is not False:
        raise Fail("usage_complete should be false when usage missing")
    raised = PROXY.Budget(path, 500, 999999)
    if not raised.snapshot()["stopped"] and raised.snapshot()["requests"] != 2:
        raise Fail("raising caps must not clear counters")
    if raised.snapshot()["requests"] != 2:
        raise Fail("raising caps cleared requests")
    return "ok"


def test_persisted_stop(dir_path: Path) -> str:
    path = fresh_ledger(dir_path, "stopped.json")
    budget = PROXY.Budget(path, 2, 200000)
    for _ in range(2):
        ok, entry = budget.begin("GET", "/v1/models")
        if not ok:
            raise Fail("expected allowed begin")
        budget.finish(entry, 1)
    ok, _ = budget.begin("GET", "/v1/models")
    if ok:
        raise Fail("expected stop at max_requests")
    again = PROXY.Budget(path, 100, 200000)
    snap = again.snapshot()
    if not snap["stopped"] or snap["requests"] != 2:
        raise Fail("restart unstopped or reset: " + json.dumps(snap))
    ok, _ = again.begin("GET", "/v1/models")
    if ok:
        raise Fail("persisted stop must reject after restart even if CLI max is higher")
    return "ok"


def test_limit_no_deadlock(dir_path: Path) -> str:
    path = fresh_ledger(dir_path, "limit.json")
    budget = PROXY.Budget(path, 1, 200000)
    ok, entry = budget.begin("GET", "/v1/models")
    if not ok:
        raise Fail("first should pass")
    budget.finish(entry, 3)
    started = time.time()
    ok2, snap = budget.begin("GET", "/v1/models")
    elapsed = time.time() - started
    if ok2:
        raise Fail("second should be rejected")
    if elapsed > 1:
        raise Fail("begin deadlock/slow at limit: " + str(elapsed))
    if snap.get("token_hard_cap_claimed") is not False:
        raise Fail("exhausted response claimed token hard cap")
    return "ok"


def test_concurrency(dir_path: Path) -> str:
    path = fresh_ledger(dir_path, "conc.json")
    budget = PROXY.Budget(path, 10, 200000)
    results: list[bool] = []
    barrier = threading.Barrier(50)

    def worker() -> None:
        barrier.wait(timeout=5)
        ok, entry = budget.begin("POST", "/v1/chat/completions")
        if ok:
            budget.finish(entry, 2)
        results.append(ok)

    threads = [threading.Thread(target=worker) for _ in range(50)]
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=15)
    if len(results) != 50:
        raise Fail("missing thread results")
    if results.count(True) != 10:
        raise Fail("expected 10 successes, got " + str(results.count(True)))
    snap = budget.snapshot()
    if snap["requests"] != 10:
        raise Fail("concurrent requests != 10")
    return "ok"


class MockUpstream(BaseHTTPRequestHandler):
    # Keep-alive framing matters here, not just tidiness: with the default HTTP/1.0 the
    # server closes the socket right after the response, and Windows turns that close into
    # a reset while the proxy is still reading -> ConnectionAbortedError -> a spurious 502.
    # HTTP/1.1 plus a drained request body keeps every exchange framed so the proxy never
    # sees an aborted read.
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt: str, *args: object) -> None:
        return

    def _drain_body(self) -> None:
        """Consume the request body so a kept-alive connection stays usable."""
        length = int(self.headers.get("Content-Length") or 0)
        if length > 0:
            self.rfile.read(length)

    def do_GET(self) -> None:  # noqa: N802
        body = json.dumps({"data": [{"id": "MiniMaxAI/MiniMax-M3"}], "usage": {"total_tokens": 1}}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self) -> None:  # noqa: N802
        self._drain_body()
        mode = getattr(self.server, "mode", "json")
        if mode == "sse":
            payload = (
                "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\n"
                "data: {\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":6,\"total_tokens\":10}}\n\n"
                "data: [DONE]\n\n"
            ).encode()
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return
        body = json.dumps({"id": "x", "choices": [], "usage": {"total_tokens": 7}}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def start_mock(mode: str = "json") -> tuple[ThreadingHTTPServer, str]:
    httpd = ThreadingHTTPServer(("127.0.0.1", 0), MockUpstream)
    httpd.mode = mode
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    host, port = httpd.server_address[:2]
    return httpd, "http://%s:%s" % (host, port)


def start_proxy(ledger: Path, upstream: str, fixture: str = "none", max_requests: int = 100) -> tuple[ThreadingHTTPServer, str]:
    httpd = ThreadingHTTPServer(("127.0.0.1", 0), PROXY.Handler)
    httpd.upstream = upstream
    httpd.budget = PROXY.Budget(ledger, max_requests, 200000)
    httpd.fixture = fixture
    httpd.fixture_timeout = 0.2
    httpd.upstream_timeout = 5.0
    httpd.allowed_model = "MiniMaxAI/MiniMax-M3"
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    host, port = httpd.server_address[:2]
    return httpd, "http://%s:%s" % (host, port)


def http_json(url: str, method: str = "GET", body: dict | None = None) -> tuple[int, dict]:
    data = None if body is None else json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            raw = resp.read()
            try:
                parsed = json.loads(raw.decode("utf-8")) if raw else {}
            except json.JSONDecodeError:
                parsed = {"raw": raw.decode("utf-8", errors="replace")}
            return resp.status, parsed
    except urllib.error.HTTPError as err:
        raw = err.read() or b"{}"
        try:
            parsed = json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError:
            parsed = {"raw": raw.decode("utf-8", errors="replace")}
        return err.code, parsed


def test_http_allowlist_and_count(dir_path: Path) -> str:
    mock, mock_url = start_mock("json")
    try:
        ledger = fresh_ledger(dir_path, "http.json")
        proxy, base = start_proxy(ledger, mock_url, max_requests=5)
        try:
            status, payload = http_json(base + "/v1/models")
            if status != 200:
                raise Fail("models status " + str(status))
            status, payload = http_json(
                base + "/v1/chat/completions",
                "POST",
                {"model": "MiniMaxAI/MiniMax-M3", "messages": [{"role": "user", "content": "x"}]},
            )
            if status != 200:
                raise Fail("chat status " + str(status))
            status, payload = http_json(
                base + "/v1/embeddings",
                "POST",
                {"model": "MiniMaxAI/MiniMax-M3", "input": "x"},
            )
            if status != 403:
                raise Fail("embeddings should 403, got " + str(status))
            status, payload = http_json(
                base + "/v1/chat/completions",
                "POST",
                {"model": "gpt-4o", "messages": [{"role": "user", "content": "x"}]},
            )
            if status != 400:
                raise Fail("wrong model should 400, got " + str(status))
            snap = PROXY.Budget(ledger, 5, 200000).snapshot()
            if snap["requests"] != 2:
                raise Fail("allowlist rejects must not count, requests=" + str(snap["requests"]))
            if snap["tokens_known"] != 8:
                raise Fail("tokens_known expected 1+7=8, got " + str(snap["tokens_known"]))
        finally:
            proxy.shutdown()
    finally:
        mock.shutdown()
    return "ok"


def test_sse_usage(dir_path: Path) -> str:
    mock, mock_url = start_mock("sse")
    try:
        ledger = fresh_ledger(dir_path, "sse.json")
        proxy, base = start_proxy(ledger, mock_url)
        try:
            status, _payload = http_json(
                base + "/v1/chat/completions",
                "POST",
                {"model": "MiniMaxAI/MiniMax-M3", "messages": [{"role": "user", "content": "x"}]},
            )
            if status != 200:
                raise Fail("sse chat status " + str(status))
            snap = PROXY.Budget(ledger, 100, 200000).snapshot()
            if snap["tokens_known"] != 10:
                raise Fail("sse usage not parsed: " + str(snap["tokens_known"]))
            if snap["unknown_usage_requests"] != 0:
                raise Fail("sse marked unknown")
        finally:
            proxy.shutdown()
    finally:
        mock.shutdown()
    return "ok"


def test_fixture_not_counted(dir_path: Path) -> str:
    ledger = fresh_ledger(dir_path, "fixture.json")
    budget = PROXY.Budget(ledger, 100, 200000)
    ok, entry = budget.begin("GET", "/v1/models")
    if not ok:
        raise Fail("seed begin")
    budget.finish(entry, 1)
    before = budget.snapshot()["requests"]
    proxy, base = start_proxy(ledger, "http://127.0.0.1:9", fixture="401")
    try:
        status, payload = http_json(
            base + "/v1/chat/completions",
            "POST",
            {"model": "MiniMaxAI/MiniMax-M3", "messages": [{"role": "user", "content": "x"}]},
        )
        if status != 401:
            raise Fail("fixture 401 got " + str(status))
        if payload.get("error", {}).get("counted") is not False:
            raise Fail("fixture must mark counted=false")
    finally:
        proxy.shutdown()
    after = PROXY.Budget(ledger, 100, 200000).snapshot()["requests"]
    if after != before:
        raise Fail("fixture 401 counted as upstream attempt")
    return "ok"


def test_http_limit(dir_path: Path) -> str:
    mock, mock_url = start_mock("json")
    try:
        ledger = fresh_ledger(dir_path, "httplimit.json")
        proxy, base = start_proxy(ledger, mock_url, max_requests=2)
        try:
            for _ in range(2):
                status, _ = http_json(base + "/v1/models")
                if status != 200:
                    raise Fail("expected 200 before limit")
            status, payload = http_json(base + "/v1/models")
            if status != 429:
                raise Fail("expected 429 at limit, got " + str(status))
            if payload.get("error", {}).get("token_hard_cap_claimed") is not False:
                raise Fail("limit 429 claimed token hard cap")
        finally:
            proxy.shutdown()
    finally:
        mock.shutdown()
    return "ok"


def main() -> int:
    TEST_DIR.mkdir(parents=True, exist_ok=True)
    real_ledger = ROOT / "build" / "validation-2026-09-08" / "budget-ledger.json"
    real_before = real_ledger.read_bytes() if real_ledger.exists() else None
    tests = [
        ("corrupt_fail_closed", test_corrupt_fail_closed),
        ("restart_preserves", test_restart_preserves),
        ("persisted_stop", test_persisted_stop),
        ("limit_no_deadlock", test_limit_no_deadlock),
        ("concurrency", test_concurrency),
        ("http_allowlist_and_count", test_http_allowlist_and_count),
        ("sse_usage", test_sse_usage),
        ("fixture_not_counted", test_fixture_not_counted),
        ("http_limit", test_http_limit),
    ]
    results = []
    failed = False
    for name, fn in tests:
        try:
            detail = fn(TEST_DIR)
            results.append({"name": name, "ok": True, "detail": detail})
        except Exception as exc:  # noqa: BLE001
            failed = True
            results.append({"name": name, "ok": False, "detail": type(exc).__name__ + ": " + str(exc)})
    real_after = real_ledger.read_bytes() if real_ledger.exists() else None
    real_untouched = real_before == real_after
    if not real_untouched:
        failed = True
        results.append({"name": "real_ledger_untouched", "ok": False, "detail": "selftest wrote real ledger"})
    else:
        results.append({"name": "real_ledger_untouched", "ok": True, "detail": "ok"})
    report = {
        "at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "ok": not failed,
        "note": "No-cost local tests only. Not MiniMax strategy proof. Test ledgers are separate from budget-ledger.json.",
        "results": results,
    }
    write_json(REPORT, report)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())



