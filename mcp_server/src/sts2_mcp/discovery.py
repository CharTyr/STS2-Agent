"""Resolve live local instances from mod-written hints, verified against /health."""
from __future__ import annotations

import json
import os
from pathlib import Path
import tempfile
import time
from urllib import request


def _modified(path: Path) -> float:
    try:
        return path.stat().st_mtime
    except OSError:
        return 0


def discover_base_url() -> str:
    selector = os.getenv("STS2_API_INSTANCE", "human").strip()
    if selector not in {"human", "companion"} and not selector.isdecimal():
        raise ValueError("STS2_API_INSTANCE must be human, companion or a process ID")
    matches: set[str] = set()
    # Hints are untrusted and contain no arbitrary URLs: only validated loopback ports are probed.
    deadline = time.monotonic() + 3
    for path in sorted(Path(tempfile.gettempdir()).glob("sts2-ai-agent-instance-*.json"), key=_modified, reverse=True)[:100]:
        if time.monotonic() >= deadline:
            raise ValueError("Instance discovery timed out. Set STS2_API_BASE_URL explicitly.")
        try:
            if path.stat().st_size > 4096:
                continue
            hint = json.loads(path.read_text(encoding="utf-8"))
            pid, port, role = hint["process_id"], hint["api_port"], hint["instance_role"]
            if type(pid) is not int or pid <= 0 or type(port) is not int or not 0 < port <= 65535:
                continue
            if (selector in {"human", "companion"} and role != selector) or (selector.isdecimal() and pid != int(selector)):
                continue
            url = f"http://127.0.0.1:{port}"
            with request.urlopen(url + "/health", timeout=0.3) as response:
                health = json.loads(response.read(65536))
            if not isinstance(health, dict) or not isinstance(health.get("data"), dict):
                continue
            data = health["data"]
            if (health.get("ok") is True and data.get("service") == "sts2-ai-agent"
                    and data.get("process_id") == pid and data.get("api_port") == port
                    and data.get("instance_role") == role and data.get("status") in {"ready", "degraded"}):
                matches.add(url)
        except (OSError, ValueError, KeyError, TypeError):
            continue
    if len(matches) > 1:
        raise ValueError("Multiple matching game instances. Set STS2_API_INSTANCE to a process ID or STS2_API_BASE_URL explicitly.")
    if matches:
        return matches.pop()
    if "STS2_API_INSTANCE" in os.environ:
        raise ValueError("No matching live game instance. Start the mod or set STS2_API_BASE_URL explicitly.")
    return "http://127.0.0.1:8080"
