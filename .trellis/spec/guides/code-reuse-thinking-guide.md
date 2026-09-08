# Code Reuse Thinking Guide

## Before adding a helper

- Search for the behavior before writing it. Use semantic search when the owner is unknown, then exact symbol searches for call sites.
- Is the owner already [GameStateService](../../../STS2AIAgent/Game/GameStateService.cs), [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs), or the [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) adapter?
- For repeated UI construction, inspect [UiFactory](../../../STS2AIAgent/Ui/UiFactory.cs). For persistence, inspect [SettingsStore](../../../STS2AIAgent/Config/SettingsStore.cs). Do not duplicate their responsibilities in callbacks.
- Can a policy stay independent of Godot so the existing [C# test harness](../../../STS2AIAgent.Tests/STS2AIAgent.Tests.csproj) can exercise it?

## Before changing shared values or contracts

- Search all occurrences of the action name, payload field, environment key, or version value.
- Native C# MCP and Python MCP are separate implementations. Keep intentional shared behavior aligned through [alignment tests](../../../mcp_server/tests/test_native_tool_alignment.py), rather than forcing an artificial shared runtime.
- Preserve established JSON field names when cleaning up internal naming.
- Prefer an existing decoder or projection over a third local interpretation of the same payload.

## After the change

- Check all affected callers and examples, including [gameplay instructions](../../../skills/sts2-mcp-player/SKILL.md).
- Follow [Mod](../mod/index.md), [MCP](../mcp/index.md), and [Operations](../operations/index.md) checks as applicable.
- Extract abstractions only when they clarify an existing responsibility; a small one-use operation does not require a new framework.
