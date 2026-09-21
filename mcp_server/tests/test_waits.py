from __future__ import annotations

import asyncio
import socket
import unittest
from typing import Any
from unittest.mock import patch

from urllib.error import URLError

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

    def test_wait_for_event_reconnects_after_server_closes_a_slow_subscriber(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        clock = FakeClock()
        open_calls = 0
        responses = [
            FakeResponse(clock, []),
            FakeResponse(
                clock,
                [
                    (0.0, b"event: stream_ready\n"),
                    (0.0, b"id: 130\n"),
                    (0.0, b'data: {"event_id": 130}\n'),
                    (0.0, b"\n"),
                ],
            ),
        ]

        def fake_urlopen(http_request, timeout=None):
            nonlocal open_calls
            open_calls += 1
            return responses.pop(0)

        with patch("sts2_mcp.client.request.urlopen", new=fake_urlopen):
            with patch("sts2_mcp.client.time.monotonic", new=clock.monotonic):
                event = client.wait_for_event(event_names={"stream_ready"}, timeout=20.0)

        self.assertEqual(open_calls, 2)
        self.assertIsNotNone(event)
        self.assertEqual(event["id"], "130")
        self.assertEqual(event["data"]["event_id"], 130)

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
        self.assertTrue(result["actionable"])
        self.assertEqual(client.wait_calls, 0)

    def test_wait_until_actionable_ignores_passive_actions(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[
                {"available_actions": ["save_and_quit"]},
                {"available_actions": ["save_and_quit"]},
                {"available_actions": ["save_and_quit", "play_card"]},
            ]
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=2.0)

        self.assertEqual(result["source"], "polling")
        self.assertEqual(result["state"]["available_actions"], ["save_and_quit", "play_card"])

    def test_wait_until_actionable_ignores_structured_passive_actions(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[
                {"available_actions": [{"name": "save_and_quit"}]},
                {"available_actions": [{"name": "save_and_quit"}]},
                {"available_actions": [{"name": "save_and_quit"}, {"name": "play_card"}]},
            ]
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=2.0)

        self.assertEqual(result["source"], "polling")
        self.assertEqual(
            result["state"]["available_actions"],
            [{"name": "save_and_quit"}, {"name": "play_card"}],
        )

    def test_wait_until_actionable_supports_structured_actions_fallback(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[
                {"actions": [{"name": "save_and_quit"}]},
                {"actions": [{"name": "save_and_quit"}]},
                {"actions": [{"name": "save_and_quit"}, {"name": "play_card"}]},
            ]
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=2.0)

        self.assertEqual(result["source"], "polling")
        self.assertEqual(
            result["state"]["actions"],
            [{"name": "save_and_quit"}, {"name": "play_card"}],
        )

    def test_wait_until_actionable_polls_after_passive_action_event(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[
                {"available_actions": []},
                {"available_actions": ["save_and_quit"]},
                {"available_actions": ["save_and_quit", "play_card"]},
            ],
            event={"event": "available_actions_changed"},
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=2.0)

        self.assertEqual(result["source"], "polling")
        self.assertTrue(result["matched"])
        self.assertEqual(result["state"]["available_actions"], ["save_and_quit", "play_card"])

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
        self.assertTrue(result["actionable"])

    def test_wait_until_actionable_real_client_polls_when_event_stream_refused(self) -> None:
        clock = FakeClock()
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        states = [
            {"available_actions": []},
            {"available_actions": []},
            {"available_actions": ["proceed"]},
        ]
        state_calls = 0
        event_opens = 0

        def fake_urlopen(http_request, timeout=None):
            nonlocal event_opens
            url = getattr(http_request, "full_url", str(http_request))
            if "/events/stream" in url:
                event_opens += 1
                # A refused transport must surface on the first attempt. The fake clock only
                # advances when the server sleeps, so a regression that retried in place would
                # spin forever instead of failing; the cap turns that hang into an assertion.
                if event_opens > 5:
                    raise AssertionError("a refused event stream must not be reopened in a retry loop")
                raise URLError(ConnectionRefusedError("refused"))
            raise AssertionError(f"unexpected urlopen: {url}")

        def fake_get_state():
            nonlocal state_calls
            state_calls += 1
            if len(states) > 1:
                return states.pop(0)
            return states[0]

        def fake_get_available_actions():
            return [{"name": "proceed"}]

        client.get_state = fake_get_state  # type: ignore[method-assign]
        client.get_available_actions = fake_get_available_actions  # type: ignore[method-assign]
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.client.request.urlopen", new=fake_urlopen):
            with patch("sts2_mcp.client.time.monotonic", new=clock.monotonic):
                with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
                    with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                        result = tool.fn(timeout_seconds=2.0)

        self.assertEqual(result["source"], "polling")
        self.assertTrue(result["actionable"])
        self.assertFalse(result["matched"])
        self.assertEqual(result["event_stream_error"]["code"], "connection_error")
        self.assertGreaterEqual(state_calls, 2)
        self.assertEqual(event_opens, 1)
        self.assertLess(clock.now, 2.0)

    def test_wait_for_event_keeps_read_timeouts_inside_the_wait(self) -> None:
        """The other half of the contract: an idle stream is retried, never raised out.

        wait_until_actionable must only fall back to polling on transport failures. A read
        timeout means the stream opened fine and simply had nothing to say, so it stays inside
        wait_for_event until the deadline instead of surfacing as connection_error.
        """

        client = Sts2Client(base_url="http://127.0.0.1:8080")
        clock = FakeClock()
        opens = 0

        class IdleResponse:
            def __init__(self, timeout):
                self.timeout = timeout
            def __enter__(self):
                return self
            def __exit__(self, *args):
                return False
            def readline(self):
                clock.sleep(min(2.5, self.timeout))
                raise socket.timeout("idle stream")

        def fake_urlopen(http_request, timeout=None):
            nonlocal opens
            opens += 1
            return IdleResponse(timeout)

        # Time advances on simulated I/O, not on reading the clock. Extra deadline checks must
        # not make a test consume seconds that no operation actually spent.
        with patch("sts2_mcp.client.request.urlopen", new=fake_urlopen), \
             patch("sts2_mcp.client.time.monotonic", new=clock.monotonic):
            result = client.wait_for_event(timeout=5.0)

        self.assertIsNone(result)
        self.assertEqual(opens, 2)
        self.assertEqual(clock.now, 5.0)


class WaitUntilActionableStateShapeTests(unittest.TestCase):
    """The wait returns the shape `get_game_state` returns, instead of the raw `/state` payload.

    The skill sends a client through `wait_until_actionable` across every animation, so a raw answer
    there cost the full 4,000-9,500 tokens on the step where the client was least likely to need
    them -- and the native surface had been answering compact all along. `raw_state=True` keeps the
    full payload reachable.
    """

    RAW = {
        "state_version": 16,
        "screen": "COMBAT",
        "available_actions": ["play_card"],
        "combat": {"hand": [{"i": 0, "card_id": "STRIKE", "line": "Deal 6 damage."}]},
        "agent_view": {
            "version": 11,
            "screen": "COMBAT",
            "available_actions": ["play_card"],
            "combat": {"hand": [{"i": 0, "card_id": "STRIKE"}]},
        },
    }

    def _tools(self, state: dict[str, Any]) -> tuple[Any, Any]:
        server = create_server(client=DummyClient(states=[state]))
        return (
            asyncio.run(server.get_tool("wait_until_actionable")),
            asyncio.run(server.get_tool("get_game_state")),
        )

    def test_the_wait_answers_compact_by_default_exactly_like_get_game_state(self) -> None:
        wait_tool, state_tool = self._tools(dict(self.RAW))

        result = wait_tool.fn(timeout_seconds=20.0)

        self.assertTrue(result["actionable"])
        self.assertIs(result["state"]["compact_agent_view"], True)
        # Not merely compact-ish: the two tools answer the same object for the same state, so a
        # client that learned to read one has learned to read the other.
        self.assertEqual(result["state"], state_tool.fn())

    def test_raw_state_is_opt_in_and_comes_back_unmarked(self) -> None:
        wait_tool, _ = self._tools(dict(self.RAW))

        result = wait_tool.fn(timeout_seconds=20.0, raw_state=True)

        self.assertEqual(result["state"], self.RAW)
        self.assertNotIn("compact_agent_view", result["state"])

    def test_a_payload_without_agent_view_is_marked_false(self) -> None:
        raw_only = {"screen": "COMBAT", "available_actions": ["proceed"]}
        wait_tool, _ = self._tools(raw_only)

        result = wait_tool.fn(timeout_seconds=20.0)

        self.assertEqual(result["state"]["available_actions"], ["proceed"])
        self.assertIs(result["state"]["compact_agent_view"], False)

    def test_the_wait_schema_documents_raw_state(self) -> None:
        wait_tool, _ = self._tools(dict(self.RAW))

        self.assertIn("raw_state", wait_tool.parameters["properties"])
        self.assertIn("compact", wait_tool.fn.__doc__ or "")

    def test_every_result_key_the_wait_had_before_is_still_there(self) -> None:
        wait_tool, _ = self._tools(dict(self.RAW))

        result = wait_tool.fn(timeout_seconds=20.0)

        for key in (
            "matched",
            "actionable",
            "event",
            "state",
            "actions",
            "timeout_seconds",
            "source",
            "event_stream_error",
        ):
            self.assertIn(key, result)


if __name__ == "__main__":
    unittest.main()
