"""Per-action audit for the Sts2Client convenience methods.

Every convenience method on :class:`Sts2Client` must issue exactly one
``POST /action`` request: the action name is the method name, each index argument
travels under its own wire key, and the payload carries an MCP client context.

Two layers of checks live here:

* a runtime walk over every action method (discovered from the class AST, never a
  hand-written list), asserting the exact call and payload, and
* a static AST guard pinning "exactly one `execute_action` call per method" plus
  "no statements after a return", which is what catches an unreachable block that
  reappears after a method's real implementation.

Only the standard library is used and `_request` is patched out, so nothing here
touches the network.
"""

from __future__ import annotations

import ast
import unittest
from pathlib import Path
from typing import Any
from unittest.mock import patch

from sts2_mcp.client import Sts2Client
from sts2_mcp.server import _LEGACY_ACTION_TOOLS

_CLIENT_SOURCE = Path(__file__).resolve().parents[1] / "src" / "sts2_mcp" / "client.py"

# One distinct sentinel per wire key, so a value that lands on the wrong key
# (for example card_index written into option_index) is not masked by an equal value.
SENTINELS: dict[str, Any] = {
    "option_index": 11,
    "card_index": 12,
    "target_index": 13,
    "x": 14,
    "y": 15,
    "tool": "small",
    "command": "cmd",
}

_PAYLOAD_KEYS = ("card_index", "target_index", "option_index", "x", "y", "tool", "command")

# The parameter shapes the per-action methods are allowed to use. Every signature
# parameter is also a wire key, so a client method and its payload cannot drift apart.
_PARAM_SHAPES: frozenset[tuple[str, ...]] = frozenset(
    {
        (),
        ("option_index",),
        ("card_index", "target_index"),
        ("option_index", "card_index"),
        ("option_index", "target_index"),
        ("tool",),
        ("x", "y", "tool"),
        ("command",),
    }
)

# `run_console_command` is debug-gated and has no legacy per-action tool, but it is a
# real action method and must obey the same contract.
_DEBUG_ONLY_ACTIONS = frozenset({"run_console_command"})


def _client_class() -> ast.ClassDef:
    tree = ast.parse(_CLIENT_SOURCE.read_text(encoding="utf-8"))
    for node in tree.body:
        if isinstance(node, ast.ClassDef) and node.name == "Sts2Client":
            return node
    raise AssertionError("Sts2Client class is missing from client.py")


def _execute_action_calls(function: ast.FunctionDef) -> list[ast.Call]:
    calls: list[ast.Call] = []
    for node in ast.walk(function):
        if (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Attribute)
            and node.func.attr == "execute_action"
            and isinstance(node.func.value, ast.Name)
            and node.func.value.id == "self"
        ):
            calls.append(node)
    return calls


def _action_methods() -> dict[str, ast.FunctionDef]:
    """Action methods discovered from the class body, not a hand-maintained list."""
    methods: dict[str, ast.FunctionDef] = {}
    for node in _client_class().body:
        if isinstance(node, ast.FunctionDef) and _execute_action_calls(node):
            methods[node.name] = node
    return methods


def _parameter_names(function: ast.FunctionDef) -> tuple[str, ...]:
    args = function.args
    positional = [arg.arg for arg in args.posonlyargs + args.args if arg.arg != "self"]
    return tuple(positional + [arg.arg for arg in args.kwonlyargs])


def _client_context_literal(call: ast.Call) -> dict[str, Any]:
    for keyword in call.keywords:
        if keyword.arg == "client_context":
            return ast.literal_eval(keyword.value)
    raise AssertionError("execute_action call has no client_context keyword")


def _expected_action_names() -> set[str]:
    return {spec.name for spec in _LEGACY_ACTION_TOOLS} | set(_DEBUG_ONLY_ACTIONS)


class ClientActionSurfaceTests(unittest.TestCase):
    def test_action_method_surface_matches_legacy_tools(self) -> None:
        expected = _expected_action_names()
        self.assertGreaterEqual(len(expected), 50, "legacy action surface parse looks wrong")
        self.assertEqual(expected, set(_action_methods()))

    def test_action_method_parameter_shapes_cover_the_documented_forms(self) -> None:
        observed: dict[tuple[str, ...], list[str]] = {}
        for name, function in _action_methods().items():
            observed.setdefault(_parameter_names(function), []).append(name)

        unknown = sorted(shape for shape in observed if shape not in _PARAM_SHAPES)
        self.assertEqual([], unknown, "unexpected per-action parameter shapes")

        representatives: dict[tuple[str, ...], str] = {
            (): "end_turn",
            ("option_index",): "choose_map_node",
            ("card_index", "target_index"): "play_card",
            ("option_index", "card_index"): "resolve_rewards",
            ("option_index", "target_index"): "choose_rest_option",
            ("tool",): "crystal_set_tool",
            ("x", "y", "tool"): "crystal_clear_cell",
            ("command",): "run_console_command",
        }
        for shape, representative in representatives.items():
            with self.subTest(shape=shape):
                self.assertIn(shape, observed)
                self.assertIn(representative, observed[shape])

        # Both index-carrying rest options share one shape.
        self.assertIn("use_potion", observed[("option_index", "target_index")])


class ClientActionPayloadTests(unittest.TestCase):
    def test_every_action_method_posts_one_action_with_the_mapped_payload(self) -> None:
        methods = _action_methods()
        self.assertEqual(_expected_action_names(), set(methods))

        for name in sorted(methods):
            with self.subTest(action=name):
                self._assert_single_action_post(name, methods[name])

    def _assert_single_action_post(self, name: str, function: ast.FunctionDef) -> None:
        kwargs = {param: SENTINELS[param] for param in _parameter_names(function)}

        expected_payload: dict[str, Any] = {key: None for key in _PAYLOAD_KEYS}
        expected_payload.update(kwargs)
        expected_payload["action"] = name
        expected_payload["client_context"] = {"source": "mcp", "tool_name": name}

        client = Sts2Client(base_url="http://127.0.0.1:8080")
        with patch.object(client, "_request", return_value={"ok": True}) as request_mock:
            result = getattr(Sts2Client, name)(client, **kwargs)

        self.assertEqual({"ok": True}, result)
        request_mock.assert_called_once_with(
            "POST",
            "/action",
            payload=expected_payload,
            is_action=True,
        )

        call = request_mock.call_args
        payload = call.kwargs["payload"]
        self.assertEqual("POST", call.args[0])
        self.assertEqual("/action", call.args[1])
        self.assertIs(True, call.kwargs["is_action"])
        self.assertEqual(name, payload["action"])
        self.assertEqual({"source": "mcp", "tool_name": name}, payload["client_context"])
        for param, sentinel in kwargs.items():
            self.assertEqual(
                sentinel,
                payload[param],
                f"{param} must carry its own value, not another key's",
            )


class ClientActionAstGuardTests(unittest.TestCase):
    def test_every_action_method_calls_execute_action_exactly_once(self) -> None:
        methods = _action_methods()
        for name in sorted(_expected_action_names()):
            with self.subTest(action=name):
                self.assertIn(name, methods, f"{name} no longer issues an action")
                self.assertEqual(
                    1,
                    len(_execute_action_calls(methods[name])),
                    f"{name} must call execute_action exactly once; 0 means it stopped "
                    "posting, 2+ means a duplicate or unreachable block came back",
                )

    def test_action_methods_have_no_statements_after_return(self) -> None:
        for name, function in sorted(_action_methods().items()):
            body = function.body
            for index, statement in enumerate(body):
                with self.subTest(action=name, statement_index=index):
                    if isinstance(statement, ast.Return):
                        self.assertEqual(
                            index,
                            len(body) - 1,
                            f"{name} has unreachable statements after its return",
                        )

    def test_ast_action_name_and_context_match_the_method(self) -> None:
        for name, function in sorted(_action_methods().items()):
            calls = _execute_action_calls(function)
            with self.subTest(action=name):
                self.assertEqual(1, len(calls))
                call = calls[0]
                self.assertTrue(call.args, f"{name} must pass the action name positionally")
                self.assertEqual(name, ast.literal_eval(call.args[0]))
                self.assertEqual(
                    {"source": "mcp", "tool_name": name},
                    _client_context_literal(call),
                )


if __name__ == "__main__":
    unittest.main()

