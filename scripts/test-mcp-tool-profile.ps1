param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$mcpRoot = Join-Path $RepoRoot "mcp_server"

Push-Location $mcpRoot
try {
    $pythonScript = @'
import asyncio
import json
import os

from sts2_mcp.server import create_server

ESSENTIAL_TOOLS = {
    "health_check",
    "get_game_state",
    "get_available_actions",
    "get_game_data_item",
    "get_game_data_items",
    "get_relevant_game_data",
    "wait_for_event",
    "wait_until_actionable",
    "act",
}
LAYERED_TOOLS = {
    "create_planner_handoff",
    "create_combat_handoff",
    "complete_combat_handoff",
    "complete_event_handoff",
    "get_planner_context",
    "get_combat_context",
    "append_combat_knowledge",
    "append_event_knowledge",
}
GUIDED_DEBUG_TOOLS = ESSENTIAL_TOOLS | {"run_console_command"}
LEGACY_ACTION_TOOLS = {
    "play_card",
    "choose_map_node",
    "claim_reward",
    "proceed",
    "confirm_selection",
    "unready",
    "increase_ascension",
    "decrease_ascension",
}


async def list_tool_names(server):
    return sorted(tool.name for tool in await server.list_tools())


async def main():
    os.environ.pop("STS2_ENABLE_DEBUG_ACTIONS", None)

    guided = await list_tool_names(create_server())
    layered = await list_tool_names(create_server(tool_profile="layered"))
    full = await list_tool_names(create_server(tool_profile="full"))

    os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = "1"
    guided_debug = await list_tool_names(create_server())

    failures = []

    if not ESSENTIAL_TOOLS.issubset(set(guided)):
        failures.append("guided profile is missing one or more essential tools")

    if set(guided) != ESSENTIAL_TOOLS:
        failures.append(f"guided profile should expose exactly the essential tool set, but exposed {guided}")

    if any(name in guided for name in LEGACY_ACTION_TOOLS):
        failures.append("guided profile should not expose legacy per-action tools")

    if "run_console_command" in guided:
        failures.append("guided profile should hide run_console_command while debug actions are disabled")

    if set(layered) != (ESSENTIAL_TOOLS | LAYERED_TOOLS):
        failures.append(f"layered profile should expose essential tools plus layered helpers, but exposed {layered}")

    if set(guided_debug) != GUIDED_DEBUG_TOOLS:
        failures.append(f"guided debug profile should only add run_console_command, but exposed {guided_debug}")

    if not LEGACY_ACTION_TOOLS.issubset(set(full)):
        failures.append("full profile should expose legacy action wrappers")

    if not LAYERED_TOOLS.issubset(set(full)):
        failures.append("full profile should include layered helper tools")

    if len(full) <= len(guided):
        failures.append("full profile should expose more tools than guided profile")

    print(json.dumps({
        "guided_count": len(guided),
        "guided_tools": guided,
        "layered_count": len(layered),
        "layered_tools": layered,
        "guided_debug_count": len(guided_debug),
        "full_count": len(full),
        "failures": failures,
    }, ensure_ascii=False))

    return 1 if failures else 0


raise SystemExit(asyncio.run(main()))
'@

    $pythonScript | uv run python - | Out-Host
}
finally {
    Pop-Location
}
