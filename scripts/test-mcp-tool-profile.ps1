param(
    [string]$RepoRoot = "C:/Users/chart/Documents/project/sp"
)

$ErrorActionPreference = "Stop"
$mcpRoot = Join-Path $RepoRoot "mcp_server"

Push-Location $mcpRoot
try {
    $pythonScript = @'
import asyncio
import json
import os

from sts2_mcp.server import create_server

ESSENTIAL_TOOLS = {"health_check", "get_game_state", "get_available_actions", "act"}
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
    full = await list_tool_names(create_server(tool_profile="full"))

    os.environ["STS2_ENABLE_DEBUG_ACTIONS"] = "1"
    guided_debug = await list_tool_names(create_server())

    failures = []

    if not ESSENTIAL_TOOLS.issubset(set(guided)):
        failures.append("guided profile is missing one or more essential tools")

    if len(guided) > 5:
        failures.append(f"guided profile should stay compact, but exposed {len(guided)} tools")

    if any(name in guided for name in LEGACY_ACTION_TOOLS):
        failures.append("guided profile should not expose legacy per-action tools")

    if "run_console_command" in guided:
        failures.append("guided profile should hide run_console_command while debug actions are disabled")

    if "run_console_command" not in guided_debug:
        failures.append("guided profile should expose run_console_command when debug actions are enabled")

    if not LEGACY_ACTION_TOOLS.issubset(set(full)):
        failures.append("full profile should expose legacy action wrappers")

    if len(full) <= len(guided):
        failures.append("full profile should expose more tools than guided profile")

    print(json.dumps({
        "guided_count": len(guided),
        "guided_tools": guided,
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
