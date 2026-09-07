from __future__ import annotations

import asyncio
import os
import re
import unittest
from pathlib import Path

from sts2_mcp.server import create_server


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


if __name__ == "__main__":
    unittest.main()
