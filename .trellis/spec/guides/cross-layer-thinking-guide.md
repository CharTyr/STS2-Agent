# Cross-Layer Thinking Guide

## State and action changes

- Trace the action through [GameStateService](../../../STS2AIAgent/Game/GameStateService.cs), [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs), and [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs).
- Do raw state and compact agent_view expose enough information to choose the same legal action and current indexes?
- Are availability names and descriptors consistent with execution guards, including modal screens?
- Does the caller retain status, stable, and fresh state after a transition? Could retrying an uncertain action execute it twice?
- Use [Mod contracts](../mod/game-actions.md) and [MCP contracts](../mcp/contracts-and-tests.md) for implementation and error behavior.

## Tool and gameplay changes

- Check [AgentTools](../../../STS2AIAgent/Agent/AgentTools.cs), [NativeMcpServer](../../../STS2AIAgent/Server/NativeMcpServer.cs), and Python [server.py](../../../mcp_server/src/sts2_mcp/server.py). Which surfaces need this capability?
- Preserve guided/layered/full and debug gates; do not assume the two MCP implementations have identical transport or every tool.
- Check [alignment tests](../../../mcp_server/tests/test_native_tool_alignment.py) and [gameplay instructions](../../../skills/sts2-mcp-player/SKILL.md) for intended alignment.

## Release and verification

- When changing the version, check the manifest, Router version, Python project metadata, and generated/locked metadata enforced by preflight. See [Operations](../operations/validation-and-release.md).
- Choose validation that proves the changed boundary: pure tests for policies/transport; a real game for native timing and screen behavior.
- Distinguish a successful offline check from a verified gameplay outcome in the final report.
