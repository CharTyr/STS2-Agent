"""Native MCP surface alignment: tool names, arguments, and switch coverage.

The native server advertises tools from `AgentTools.Mcp` but executes them through
`NativeMcpServer.ExecuteToolAsync`. Those two halves can drift independently, and a
name-only comparison misses both a renamed argument and a case that was deleted while
the tool stayed advertised. The assertions here therefore compare three surfaces:

1. tool names: Python guided tools vs `AgentTools.Mcp` (existing) and vs the
   `ExecuteToolAsync` switch branches (bidirectional, with an explicit exemption);
2. argument names: Python registered inputSchema vs the `arguments` keys the C#
   switch case (or the helper it delegates to) actually parses;
3. action argument names: the `docs/api.md` frozen contract column vs the schema of
   the registered full-profile legacy tool, with a commented exemption list.

The `GameActionService.ExecuteAsync` action-name sets are owned by
`test_legacy_action_coverage.py` and are deliberately not duplicated here.
"""

from __future__ import annotations

import asyncio
import json
import os
import re
import unittest
from pathlib import Path

from sts2_mcp.client import Sts2Client
from sts2_mcp.server import _LEGACY_ACTION_TOOLS, create_server


_TOOL_CALL = re.compile(r'\bTool\s*\(\s*"([^"]+)"')
_TOOL_OBJECT = re.compile(r'\bName\s*=\s*"([^"]+)"')
_FIELD_REFERENCE = re.compile(r"\b(ReadOnly|Play|Mcp)\b")
_LIST_FIELD = re.compile(
    r"public\s+static\s+readonly\s+IReadOnlyList<LlmTool>\s+(ReadOnly|Play|Mcp)\s*=\s*",
    re.MULTILINE,
)


def _find_source_root() -> Path:
    candidates = [Path(__file__).resolve().parents[2], Path.cwd()]
    for candidate in candidates:
        if (candidate / "STS2AIAgent/Agent/AgentTools.cs").is_file() and (
            candidate / "STS2AIAgent/Server/NativeMcpServer.cs"
        ).is_file():
            return candidate
    raise AssertionError(
        "Could not locate STS2-Agent source files required for native MCP alignment: "
        "STS2AIAgent/Agent/AgentTools.cs and STS2AIAgent/Server/NativeMcpServer.cs"
    )


def _extract_list_initializers(source: str) -> dict[str, str]:
    matches = list(_LIST_FIELD.finditer(source))
    if not matches:
        raise AssertionError(
            "AgentTools.cs has no ReadOnly/Play/Mcp IReadOnlyList<LlmTool> definitions"
        )

    initializers: dict[str, str] = {}
    for match in matches:
        field_name = match.group(1)
        end = source.find(";", match.end())
        if end < 0:
            raise AssertionError(f"AgentTools.cs initializer for {field_name} is unterminated")
        initializers[field_name] = source[match.end() : end]
    return initializers


def _native_tool_names(source_root: Path) -> set[str]:
    agent_tools_path = source_root / "STS2AIAgent/Agent/AgentTools.cs"
    native_server_path = source_root / "STS2AIAgent/Server/NativeMcpServer.cs"
    try:
        agent_tools = agent_tools_path.read_text(encoding="utf-8")
        native_server = native_server_path.read_text(encoding="utf-8")
    except OSError as exc:
        raise AssertionError(f"Could not read native MCP source contract: {exc}") from exc

    if not re.search(r"AgentTools\.Mcp\.Select\s*\(", native_server):
        raise AssertionError(
            "NativeMcpServer.cs does not build tools/list from AgentTools.Mcp; "
            "the native source contract is no longer connected"
        )

    initializers = _extract_list_initializers(agent_tools)
    if "Mcp" not in initializers:
        raise AssertionError("AgentTools.cs is missing the Mcp tool list")

    visiting: set[str] = set()

    def resolve(field_name: str) -> set[str]:
        if field_name in visiting:
            raise AssertionError(f"Cyclic AgentTools list reference involving {field_name}")
        expression = initializers.get(field_name)
        if expression is None:
            raise AssertionError(f"AgentTools.cs is missing referenced list {field_name}")

        visiting.add(field_name)
        try:
            names = set(_TOOL_CALL.findall(expression))
            names.update(_TOOL_OBJECT.findall(expression))
            for reference in _FIELD_REFERENCE.findall(expression):
                if reference != field_name:
                    names.update(resolve(reference))
            return names
        finally:
            visiting.remove(field_name)

    names = resolve("Mcp")
    if not names:
        raise AssertionError("AgentTools.Mcp resolved to no tool names")
    return names


_NATIVE_MEMBER = re.compile(
    r"(?m)^    (?:private|internal|public|protected)\s+(?:static\s+)?(?:async\s+)?"
    r"[\w<>\[\],\s\.\?]+?\s+(\w+)\s*\("
)
_CASE_MARKER = re.compile(r'^[ \t]*(?:case\s+"([^"]+)"|default)[ \t]*:', re.MULTILINE)
_READ_ARGUMENT = re.compile(r'Read(?:String|Int)\(\s*arguments\s*,\s*"([^"]+)"\s*\)')
_READ_TIMEOUT_CALL = re.compile(r'ReadTimeoutSeconds\(\s*arguments\s*(?:,\s*"([^"]+)")?\s*\)')
_TRY_GET_PROPERTY = re.compile(r'TryGetProperty\(\s*"([^"]+)"')
_HELPER_CALL = re.compile(r"\b(\w+Async)\s*\(\s*arguments")


def _native_members(source: str) -> dict[str, str]:
    """Split NativeMcpServer.cs into member-name -> body (modifier-anchored)."""
    matches = list(_NATIVE_MEMBER.finditer(source))
    members: dict[str, str] = {}
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(source)
        members[match.group(1)] = source[match.end() : end]
    return members


def _native_server_source(source_root: Path) -> str:
    return (source_root / "STS2AIAgent/Server/NativeMcpServer.cs").read_text(encoding="utf-8")


def _switch_branch_bodies(method_body: str) -> dict[str, str]:
    markers = list(_CASE_MARKER.finditer(method_body))
    bodies: dict[str, str] = {}
    for index, marker in enumerate(markers):
        end = markers[index + 1].start() if index + 1 < len(markers) else len(method_body)
        name = marker.group(1)
        if name is not None:
            bodies[name] = method_body[marker.end() : end]
    return bodies


def _native_execute_switch_branches(source_root: Path) -> set[str]:
    """Case labels inside NativeMcpServer.ExecuteToolAsync."""
    source = _native_server_source(source_root)
    body = _native_members(source).get("ExecuteToolAsync")
    if body is None:
        raise AssertionError("NativeMcpServer.cs has no ExecuteToolAsync method")
    branches = _switch_branch_bodies(body)
    if len(branches) < 5:
        raise AssertionError(
            "ExecuteToolAsync switch parse looks wrong: " + ", ".join(sorted(branches))
        )
    return set(branches)


def _implicit_timeout_argument(members: dict[str, str]) -> str:
    """The argument key ReadTimeoutSeconds reads without an explicit literal name."""
    body = members.get("ReadTimeoutSeconds")
    if body is None:
        raise AssertionError("NativeMcpServer.cs has no ReadTimeoutSeconds method")
    keys = _TRY_GET_PROPERTY.findall(body)
    if len(keys) != 1:
        raise AssertionError(
            "ReadTimeoutSeconds no longer reads exactly one argument key: " + ", ".join(keys)
        )
    return keys[0]


def _argument_names(body: str, members: dict[str, str], seen: set[str]) -> set[str]:
    names = set(_READ_ARGUMENT.findall(body))
    for match in _READ_TIMEOUT_CALL.finditer(body):
        names.add(match.group(1) or _implicit_timeout_argument(members))
    for helper in _HELPER_CALL.findall(body):
        if helper in members and helper not in seen:
            seen.add(helper)
            names.update(_argument_names(members[helper], members, seen))
    return names


def _native_switch_parameters(source_root: Path) -> dict[str, set[str]]:
    """Argument keys each ExecuteToolAsync case parses, delegated helpers included."""
    source = _native_server_source(source_root)
    members = _native_members(source)
    body = members.get("ExecuteToolAsync")
    if body is None:
        raise AssertionError("NativeMcpServer.cs has no ExecuteToolAsync method")
    branches = _switch_branch_bodies(body)
    if len(branches) < 5:
        raise AssertionError("ExecuteToolAsync switch parse looks wrong")
    return {
        name: _argument_names(branch, members, {name})
        for name, branch in branches.items()
    }


def _guided_tool_schemas() -> dict[str, set[str]]:
    """Registered guided tool -> inputSchema property names."""

    class Dummy:
        def get_state(self) -> dict:
            return {"screen": "MAIN_MENU", "available_actions": []}

    previous = os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)
    try:
        server = create_server(client=Dummy(), tool_profile="guided")  # type: ignore[arg-type]
        tools = asyncio.run(server.list_tools())
    finally:
        if previous is not None:
            os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = previous
    return {
        tool.name: set((tool.parameters or {}).get("properties", {})) for tool in tools
    }


def _legacy_tool_schemas() -> dict[str, set[str]]:
    """Registered full-profile legacy tool -> inputSchema property names."""
    server = create_server(client=Sts2Client(), tool_profile="full")

    async def collect() -> dict[str, set[str]]:
        schemas: dict[str, set[str]] = {}
        for spec in _LEGACY_ACTION_TOOLS:
            tool = await server.get_tool(spec.name)
            if tool is None:
                raise AssertionError(
                    f"full profile did not register the legacy tool {spec.name!r}"
                )
            schemas[spec.name] = set((tool.parameters or {}).get("properties", {}))
        return schemas

    return asyncio.run(collect())


_CONTRACT_BEGIN = "<!-- BEGIN ACTION CONTRACT -->"
_CONTRACT_END = "<!-- END ACTION CONTRACT -->"
_CONTRACT_ACTION = re.compile(r"^-\s*`([a-z][a-z0-9_]*)`(.*)$")
_DOC_PARAMETER = re.compile(r"`([a-z_][a-z0-9_]*)`")

# The argument names a POST /action contract line may carry. Restricting to this
# vocabulary ignores the other backticked tokens that share those descriptions:
# screen names (PATCH_NOTES, CARD_INSPECT), enum values (big/small), and debug env
# flags (STS2_ENABLE_DEBUG_ACTIONS).
_ACTION_ARGUMENT_VOCABULARY = {
    "option_index",
    "card_index",
    "target_index",
    "x",
    "y",
    "tool",
}


def _documented_action_arguments(source_root: Path) -> dict[str, set[str]]:
    text = (source_root / "docs/api.md").read_text(encoding="utf-8")
    begin = text.find(_CONTRACT_BEGIN)
    end = text.find(_CONTRACT_END)
    if begin < 0 or end < 0 or end <= begin:
        raise AssertionError(
            f"docs/api.md is missing the {_CONTRACT_BEGIN} / {_CONTRACT_END} block"
        )
    documented: dict[str, set[str]] = {}
    for line in text[begin + len(_CONTRACT_BEGIN) : end].splitlines():
        match = _CONTRACT_ACTION.match(line)
        if match is None:
            continue
        documented[match.group(1)] = {
            name
            for name in _DOC_PARAMETER.findall(match.group(2))
            if name in _ACTION_ARGUMENT_VOCABULARY
        }
    if len(documented) < 40:
        raise AssertionError("docs/api.md action contract parse looks wrong")
    return documented


# crystal_clear_cell also accepts an optional "tool" (mirroring the guided act
# docstring and _register_crystal_cell_tool), but the frozen contract block only spells
# out x/y. The gap is listed here explicitly rather than silently tolerated; the test
# also fails if this exemption becomes stale.
_DOC_ARGUMENT_EXEMPTIONS: dict[str, set[str]] = {
    "crystal_clear_cell": {"tool"},
}

# run_console_command is documented but deliberately not a legacy per-action tool: it is
# registered separately and only when STS2_ENABLE_DEBUG_ACTIONS is truthy.
_DOCUMENTED_WITHOUT_LEGACY_TOOL = {"run_console_command"}


class NativeToolAlignmentTests(unittest.TestCase):
    def test_python_guided_matches_native_source_tool_surface(self) -> None:
        source_root = _find_source_root()
        native_names = _native_tool_names(source_root)

        async def collect() -> set[str]:
            class Dummy:
                def get_state(self) -> dict:
                    return {"screen": "MAIN_MENU", "available_actions": []}

            os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)
            server = create_server(client=Dummy(), tool_profile="guided")  # type: ignore[arg-type]
            return {tool.name for tool in await server.list_tools()}

        python_names = asyncio.run(collect())
        missing = native_names - python_names
        self.assertFalse(
            missing,
            "Python guided MCP is missing tools from AgentTools.Mcp: "
            + ", ".join(sorted(missing)),
        )

        # The Python sidecar owns the event-stream convenience tool; native
        # AgentTools.Mcp intentionally exposes the compact action surface only.
        sidecar_only = python_names - native_names
        self.assertEqual(
            {"wait_for_event"},
            sidecar_only,
            "Unexpected Python guided-only tools versus AgentTools.Mcp: "
            + ", ".join(sorted(sidecar_only)),
        )

    def test_native_switch_covers_the_python_guided_surface(self) -> None:
        source_root = _find_source_root()
        switch_branches = _native_execute_switch_branches(source_root)
        python_names = set(_guided_tool_schemas())

        python_without_branch = python_names - switch_branches
        self.assertEqual(
            {"wait_for_event"},
            python_without_branch,
            "Python guided tools with no NativeMcpServer.ExecuteToolAsync case "
            "(add an exemption with a reason if deliberate): "
            + ", ".join(sorted(python_without_branch)),
        )

        branch_without_python = switch_branches - python_names
        self.assertEqual(
            set(),
            branch_without_python,
            "NativeMcpServer.ExecuteToolAsync cases with no Python guided tool: "
            + ", ".join(sorted(branch_without_python)),
        )

        # tools/list is built from AgentTools.Mcp, so every advertised name must also be
        # executable by the switch: an advertised-but-unhandled tool only fails at call
        # time with "Unknown tool".
        advertised_without_branch = _native_tool_names(source_root) - switch_branches
        self.assertEqual(
            set(),
            advertised_without_branch,
            "AgentTools.Mcp advertises tools with no ExecuteToolAsync case: "
            + ", ".join(sorted(advertised_without_branch)),
        )

    def test_shared_guided_tools_use_identical_argument_names(self) -> None:
        source_root = _find_source_root()
        python_schemas = _guided_tool_schemas()
        native_schemas = _native_switch_parameters(source_root)

        shared = set(python_schemas) & set(native_schemas)
        self.assertGreater(len(shared), 6, "Python/C# guided tool overlap parse looks wrong")

        mismatches = {
            name: {
                "python": sorted(python_schemas[name]),
                "csharp_parsed": sorted(native_schemas[name]),
            }
            for name in sorted(shared)
            if python_schemas[name] != native_schemas[name]
        }
        self.assertFalse(
            mismatches,
            "Guided tool argument names diverge between the Python inputSchema and the "
            "NativeMcpServer parsing: " + json.dumps(mismatches, sort_keys=True),
        )

        # wait_until_actionable reads its key through ReadTimeoutSeconds rather than an
        # inline literal, so pin it explicitly: a parser that drops delegated reads or a
        # renamed key both have to show up here instead of passing silently.
        self.assertEqual({"timeout_seconds"}, python_schemas["wait_until_actionable"])
        self.assertEqual({"timeout_seconds"}, native_schemas["wait_until_actionable"])

    def test_documented_action_arguments_match_registered_legacy_tools(self) -> None:
        source_root = _find_source_root()
        documented = _documented_action_arguments(source_root)
        registered = _legacy_tool_schemas()

        documented_without_tool = set(documented) - set(registered)
        self.assertEqual(
            _DOCUMENTED_WITHOUT_LEGACY_TOOL,
            documented_without_tool,
            "Documented actions without a full-profile legacy tool "
            "(add an exemption with a reason if deliberate): "
            + ", ".join(sorted(documented_without_tool)),
        )

        tool_without_document = set(registered) - set(documented)
        self.assertEqual(
            set(),
            tool_without_document,
            "Full-profile legacy tools missing from the docs/api.md contract block: "
            + ", ".join(sorted(tool_without_document)),
        )

        mismatches: dict[str, dict[str, list[str]]] = {}
        stale_exemptions: list[str] = []
        for action in sorted(set(documented) & set(registered)):
            exempt = _DOC_ARGUMENT_EXEMPTIONS.get(action, set())
            expected = documented[action] | exempt
            if expected != registered[action]:
                mismatches[action] = {
                    "documented": sorted(documented[action]),
                    "registered": sorted(registered[action]),
                }
            elif exempt and documented[action] == registered[action]:
                stale_exemptions.append(action)

        self.assertFalse(
            mismatches,
            "Documented /action arguments diverge from the registered legacy tool "
            "schema: " + json.dumps(mismatches, sort_keys=True),
        )
        self.assertFalse(
            stale_exemptions,
            "_DOC_ARGUMENT_EXEMPTIONS entries are stale (the two sides now agree): "
            + ", ".join(stale_exemptions),
        )


if __name__ == "__main__":
    unittest.main()
