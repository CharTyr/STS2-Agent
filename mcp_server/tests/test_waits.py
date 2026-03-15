from __future__ import annotations

import asyncio
import socket
import unittest
from unittest.mock import patch

from sts2_mcp.client import Sts2ApiError, Sts2Client
from sts2_mcp.server import create_server


class FakeClock:
    def __init__(self) -> None:
        self.now = 0.0

    def monotonic(self) -> float:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.now += seconds


class DummyClient:
    def __init__(self, states: list[dict], event: dict | None = None) -> None:
        self._states = list(states)
        self._event = event
        self.wait_calls = 0

    def get_health(self) -> dict:
        return {"ok": True}

    def get_state(self) -> dict:
        if len(self._states) > 1:
            return self._states.pop(0)
        return self._states[0]

    def get_available_actions(self) -> list[dict]:
        return [{"name": "act"}]

    def wait_for_event(self, *, event_names=None, timeout=0.0) -> dict | None:
        self.wait_calls += 1
        return self._event


class FakeSocket:
    def __init__(self) -> None:
        self.timeout = None
        self.timeout_history: list[float] = []

    def settimeout(self, timeout: float) -> None:
        self.timeout = float(timeout)
        self.timeout_history.append(float(timeout))


class FakeResponse:
    def __init__(self, clock: FakeClock, schedule: list[tuple[float, bytes]]) -> None:
        self._clock = clock
        self._schedule = list(schedule)
        self._socket = FakeSocket()
        self.fp = type("FakeFp", (), {"raw": type("FakeRaw", (), {"_sock": self._socket})()})()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb) -> bool:
        return False

    def readline(self) -> bytes:
        if not self._schedule:
            return b""

        delay, payload = self._schedule.pop(0)
        timeout = self._socket.timeout
        if timeout is None:
            raise AssertionError("socket timeout was not set before readline")
        if delay > timeout:
            self._clock.now += timeout
            raise socket.timeout("timed out")

        self._clock.now += delay
        return payload


class WaitBehaviorTests(unittest.TestCase):
    def test_wait_for_event_keeps_processing_after_comments(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        clock = FakeClock()
        open_calls = 0
        response = FakeResponse(
            clock,
            [
                (0.0, b": stream opened\n"),
                (0.0, b"\n"),
                (0.0, b"event: target\n"),
                (0.0, b'data: {"matched": true}\n'),
                (0.0, b"\n"),
            ],
        )

        def fake_urlopen(http_request, timeout=None):
            nonlocal open_calls
            open_calls += 1
            return response

        with patch("sts2_mcp.client.request.urlopen", new=fake_urlopen):
            with patch("sts2_mcp.client.time.monotonic", new=clock.monotonic):
                event = client.wait_for_event(event_names={"target"}, timeout=20.0)

        self.assertEqual(open_calls, 1)
        self.assertIsNotNone(event)
        self.assertEqual(event["event"], "target")

    def test_wait_for_event_respects_deadline_without_forcing_reconnects(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        clock = FakeClock()
        open_calls = 0
        response = FakeResponse(
            clock,
            [
                (15.0, b": heartbeat\n"),
                (0.0, b"\n"),
                (10.0, b"event: never-reached\n"),
            ],
        )

        def fake_urlopen(http_request, timeout=None):
            nonlocal open_calls
            open_calls += 1
            return response

        with patch("sts2_mcp.client.request.urlopen", new=fake_urlopen):
            with patch("sts2_mcp.client.time.monotonic", new=clock.monotonic):
                event = client.wait_for_event(timeout=20.0)

        self.assertIsNone(event)
        self.assertEqual(open_calls, 1)
        self.assertGreaterEqual(len(response._socket.timeout_history), 2)
        self.assertAlmostEqual(response._socket.timeout_history[0], 20.0, places=2)
        self.assertAlmostEqual(response._socket.timeout_history[-1], 5.0, places=2)

    def test_wait_until_actionable_returns_immediately_when_state_is_actionable(self) -> None:
        client = DummyClient(states=[{"available_actions": ["proceed"]}])
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        result = tool.fn(timeout_seconds=20.0)

        self.assertEqual(result["source"], "state")
        self.assertFalse(result["matched"])
        self.assertEqual(client.wait_calls, 0)

    def test_wait_until_actionable_falls_back_to_polling(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[
                {"available_actions": []},
                {"available_actions": []},
                {"available_actions": ["proceed"]},
            ]
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=2.0)

        self.assertEqual(result["source"], "polling")
        self.assertEqual(result["state"]["available_actions"], ["proceed"])


if __name__ == "__main__":
    unittest.main()
