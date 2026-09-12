"""Contract tests for the agent-facing surface.

Covers the compact-state marker on get_game_state, the actionable key on every
wait_until_actionable return path, the reward_choice tool kind used by the
full-profile resolve_rewards tool, and the skill field names the agent reads.
"""

from __future__ import annotations

import asyncio
import unittest
from pathlib import Path
from unittest.mock import patch

from sts2_mcp.client import Sts2Client
from sts2_mcp.server import create_server

RAW_ONLY_SKILL_FIELDS = (
    "can_embark",
    "is_open",
    "has_relic_been_claimed",
    "is_locked",
    "will_kill_player",
    "min_select",
    "max_select",
    "selected_count",
    "requires_confirmation",
    "can_confirm",
    "slots[].state",
)

COMPACT_SKILL_FIELDS = (
    "character_select.embark",
    "shop.open",
    "chest.claimed",
    "selection.min",
    "selection.max",
    "selection.selected",
    "selection.confirm",
    "timeline.slots[].actionable",
    "wait_for_event",
    "FAKE_MERCHANT",
    "PATCH_NOTES",
    "CARD_INSPECT",
    "RELIC_INSPECT",
    "FEEDBACK",
)


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


class FailingEventClient(DummyClient):
    """Client whose SSE stream is unavailable, forcing the polling fallback."""

    def wait_for_event(self, *, event_names=None, timeout=0.0) -> dict | None:
        self.wait_calls += 1
        raise RuntimeError("SSE stream unavailable")


def _read_skill_text() -> str:
    root = Path(__file__).resolve().parents[2]
    skill_dir = root / "skills" / "sts2-mcp-player"
    parts = [
        (skill_dir / "SKILL.md").read_text(encoding="utf-8"),
        (skill_dir / "references" / "screen-playbooks.md").read_text(encoding="utf-8"),
        (skill_dir / "README.md").read_text(encoding="utf-8"),
    ]
    return "\n".join(parts)


class AgentStateShapeTests(unittest.TestCase):
    def test_get_game_state_marks_compact_when_agent_view_is_present(self) -> None:
        client = DummyClient(
            states=[
                {
                    "screen": "COMBAT",
                    "agent_view": {
                        "screen": "COMBAT",
                        "actions": ["end_turn"],
                        "crystal_sphere": {"divinations_left": 0},
                    },
                }
            ]
        )
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("get_game_state"))

        result = tool.fn()

        self.assertIs(result["compact_agent_view"], True)
        self.assertEqual(result["screen"], "COMBAT")
        self.assertEqual(result["available_actions"], ["end_turn"])

    def test_get_game_state_keeps_existing_available_actions(self) -> None:
        client = DummyClient(
            states=[
                {
                    "agent_view": {
                        "screen": "MAP",
                        "available_actions": ["choose_map_node"],
                    }
                }
            ]
        )
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("get_game_state"))

        result = tool.fn()

        self.assertIs(result["compact_agent_view"], True)
        self.assertEqual(result["available_actions"], ["choose_map_node"])

    def test_get_game_state_marks_raw_fallback(self) -> None:
        client = DummyClient(
            states=[{"screen": "COMBAT", "available_actions": ["end_turn"]}]
        )
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("get_game_state"))

        result = tool.fn()

        self.assertIs(result["compact_agent_view"], False)
        self.assertEqual(result["screen"], "COMBAT")
        self.assertEqual(result["available_actions"], ["end_turn"])

    def test_get_game_state_docstring_documents_both_shapes(self) -> None:
        client = DummyClient(states=[{"screen": "COMBAT"}])
        server = create_server(client=client, tool_profile="guided")
        tool = asyncio.run(server.get_tool("get_game_state"))

        description = tool.fn.__doc__ or ""

        self.assertIn("compact_agent_view", description)
        self.assertIn("get_raw_game_state", description)

    def test_get_game_state_marker_reflects_the_returned_shape(self) -> None:
        # A marker echoed by the payload never wins over the shape we actually returned.
        compact_client = DummyClient(
            states=[{"agent_view": {"screen": "MAP", "compact_agent_view": False}}]
        )
        compact_server = create_server(client=compact_client, tool_profile="guided")
        compact_tool = asyncio.run(compact_server.get_tool("get_game_state"))

        self.assertIs(compact_tool.fn()["compact_agent_view"], True)

        raw_client = DummyClient(states=[{"screen": "MAP", "compact_agent_view": True}])
        raw_server = create_server(client=raw_client, tool_profile="guided")
        raw_tool = asyncio.run(raw_server.get_tool("get_game_state"))

        self.assertIs(raw_tool.fn()["compact_agent_view"], False)

    def test_get_game_state_falls_back_to_raw_when_agent_view_is_not_a_dict(self) -> None:
        for value in (None, ["not", "a", "dict"]):
            with self.subTest(agent_view=value):
                client = DummyClient(states=[{"screen": "MAP", "agent_view": value}])
                server = create_server(client=client, tool_profile="guided")
                tool = asyncio.run(server.get_tool("get_game_state"))

                result = tool.fn()

                self.assertIs(result["compact_agent_view"], False)
                self.assertEqual(result["screen"], "MAP")


class WaitUntilActionableKeyTests(unittest.TestCase):
    def test_immediate_branch_returns_actionable(self) -> None:
        client = DummyClient(states=[{"available_actions": ["proceed"]}])
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        result = tool.fn(timeout_seconds=20.0)

        self.assertEqual(result["source"], "state")
        self.assertFalse(result["matched"])
        self.assertTrue(result["actionable"])
        self.assertEqual(client.wait_calls, 0)

    def test_passive_immediate_state_is_not_actionable(self) -> None:
        clock = FakeClock()
        client = DummyClient(states=[{"available_actions": ["save_and_quit"]}])
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=1.0)

        self.assertIs(result["actionable"], False)

    def test_polling_timeout_is_not_actionable(self) -> None:
        clock = FakeClock()
        client = DummyClient(states=[{"available_actions": []}], event=None)
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=1.0)

        self.assertEqual(result["source"], "polling")
        self.assertFalse(result["matched"])
        self.assertIs(result["actionable"], False)

    def test_event_branch_reports_actionable_even_without_match(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[
                {"available_actions": []},
                {"available_actions": ["proceed"]},
            ],
            event=None,
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=2.0)

        self.assertFalse(result["matched"])
        self.assertIs(result["actionable"], True)

    def test_event_branch_can_match_without_becoming_actionable(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[{"available_actions": []}, {"available_actions": []}],
            event={"event": "screen_changed"},
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=1.0)

        self.assertEqual(result["source"], "polling")
        self.assertTrue(result["matched"])
        self.assertIs(result["actionable"], False)

    def test_event_branch_can_match_and_become_actionable(self) -> None:
        clock = FakeClock()
        client = DummyClient(
            states=[
                {"available_actions": []},
                {"available_actions": []},
                {"available_actions": ["play_card"]},
            ],
            event={"event": "available_actions_changed"},
        )
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=2.0)

        self.assertTrue(result["matched"])
        self.assertIs(result["actionable"], True)

    def test_event_stream_failure_falls_back_to_polling_with_actionable(self) -> None:
        clock = FakeClock()
        client = FailingEventClient(
            states=[
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
        self.assertFalse(result["matched"])
        self.assertIs(result["actionable"], True)

    def test_event_stream_failure_without_action_stays_not_actionable(self) -> None:
        clock = FakeClock()
        client = FailingEventClient(states=[{"available_actions": []}])
        server = create_server(client=client)
        tool = asyncio.run(server.get_tool("wait_until_actionable"))

        with patch("sts2_mcp.server.time.monotonic", new=clock.monotonic):
            with patch("sts2_mcp.server.time.sleep", new=clock.sleep):
                result = tool.fn(timeout_seconds=1.0)

        self.assertEqual(result["source"], "polling")
        self.assertFalse(result["matched"])
        self.assertIs(result["actionable"], False)


class RewardChoiceToolTests(unittest.TestCase):
    def test_full_profile_resolve_rewards_accepts_optional_indices(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        with patch.object(
            client,
            "execute_action",
            return_value={"action": "resolve_rewards", "stable": True},
        ) as execute_mock:
            server = create_server(client=client, tool_profile="full")
            tool = asyncio.run(server.get_tool("resolve_rewards"))

            properties = tool.parameters["properties"]
            self.assertEqual(set(properties), {"option_index", "card_index"})

            tool.fn()
            tool.fn(card_index=2)

        self.assertEqual(execute_mock.call_count, 2)
        first = execute_mock.call_args_list[0]
        self.assertEqual(first.args, ("resolve_rewards",))
        self.assertIsNone(first.kwargs["option_index"])
        self.assertIsNone(first.kwargs["card_index"])
        second = execute_mock.call_args_list[1]
        self.assertIsNone(second.kwargs["option_index"])
        self.assertEqual(second.kwargs["card_index"], 2)

    def test_full_profile_resolve_rewards_still_forwards_option_index(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")
        with patch.object(
            client,
            "execute_action",
            return_value={"action": "resolve_rewards", "stable": True},
        ) as execute_mock:
            server = create_server(client=client, tool_profile="full")
            tool = asyncio.run(server.get_tool("resolve_rewards"))

            tool.fn(option_index=-1)

        kwargs = execute_mock.call_args_list[0].kwargs
        self.assertEqual(kwargs["option_index"], -1)
        self.assertIsNone(kwargs["card_index"])

    def test_client_resolve_rewards_forwards_card_index(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")

        with patch.object(client, "_request", return_value={"ok": True}) as request_mock:
            client.resolve_rewards(card_index=3)

        request_mock.assert_called_once_with(
            "POST",
            "/action",
            payload={
                "action": "resolve_rewards",
                "card_index": 3,
                "target_index": None,
                "option_index": None,
                "x": None,
                "y": None,
                "tool": None,
                "command": None,
                "client_context": {"source": "mcp", "tool_name": "resolve_rewards"},
            },
            is_action=True,
        )

    def test_client_resolve_rewards_defaults_to_no_indexes(self) -> None:
        client = Sts2Client(base_url="http://127.0.0.1:8080")

        with patch.object(client, "_request", return_value={"ok": True}) as request_mock:
            client.resolve_rewards()

        payload = request_mock.call_args.kwargs["payload"]
        self.assertIsNone(payload["option_index"])
        self.assertIsNone(payload["card_index"])


class SkillFieldContractTests(unittest.TestCase):
    def test_skill_reads_compact_field_names(self) -> None:
        text = _read_skill_text()

        for token in COMPACT_SKILL_FIELDS:
            self.assertIn(token, text, "skill must name the compact field " + token)

    def test_skill_states_the_real_confirm_rule(self) -> None:
        text = _read_skill_text()

        self.assertIn("RequiresConfirmation", text)
        self.assertIn("MinSelect < MaxSelect", text)

    def test_raw_only_field_names_are_never_read_without_a_raw_label(self) -> None:
        text = _read_skill_text()

        for token in RAW_ONLY_SKILL_FIELDS:
            for line in text.splitlines():
                if token not in line:
                    continue
                self.assertIn(
                    "raw",
                    line.lower(),
                    "raw-only field " + token + " must be labeled raw: " + line,
                )


if __name__ == "__main__":
    unittest.main()
