# STS2AIAgent Mod Specification

This layer describes the code that runs inside the Slay the Spire 2 mod and the two MCP surfaces that expose it. It is grounded in the current C# source and the Python sidecar; it is not a generic Godot or MCP template.

## Guides

- [Architecture and boundaries](architecture.md) — thread ownership, transport boundaries, state ownership, and MCP separation.
- [Game state and actions](game-actions.md) — raw/compact state, available actions, action handlers, errors, and transition waits.
- [Agent, UI, configuration, and LLM](agent-and-ui.md) — the `IGameBridge` seam, runtime/UI flow, persisted settings, LLM clients, and test seams.
- [Cross-layer thinking guide](../guides/cross-layer-thinking-guide.md) — repository-wide reasoning guidance to apply when a change crosses the mod, agent, and server layers.

Source entry points:

- [ModEntry.Initialize](../../../STS2AIAgent/ModEntry.cs) starts `GameThread`, events, HTTP, runtime, and (when applicable) the overlay.
- [Router.HandleAsync](../../../STS2AIAgent/Server/Router.cs) owns the local HTTP routes and error envelope.
- [GameStateService](../../../STS2AIAgent/Game/GameStateService.cs) builds the state snapshots and action availability.
- [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs) executes the action switch and waits for transitions.
- [AgentRuntime](../../../STS2AIAgent/Agent/AgentRuntime.cs) connects settings, `AgentLoop`, the bridge, overlay state, and native MCP.

## Pre-Development Checklist

Before changing this package, answer these questions from the source:

1. Is the change transport-facing, state-building, game-mutating, agent-facing, UI-facing, configuration-facing, or LLM-facing? Start with the matching guide above and keep the change in that layer.
2. Does the code touch Godot or game objects? If so, identify the existing `GameThread.InvokeAsync` boundary in [Router](../../../STS2AIAgent/Server/Router.cs) or [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs). A handler already reached through one of those boundaries must not add a second dispatch merely by convention.
3. For a new game action, trace the same action through [GameStateService.BuildAvailableActionNames](../../../STS2AIAgent/Game/GameStateService.cs), [GameStateService.BuildAvailableActionsPayload](../../../STS2AIAgent/Game/GameStateService.cs), and [GameActionService.ExecuteAsync](../../../STS2AIAgent/Game/GameActionService.cs). Decide which state payload and index/target metadata are required before editing.
4. For an action that changes screens or combat state, define an observable stable condition and an explicit timeout. Follow the existing `WaitFor*TransitionAsync` methods and [GameThread.WaitForNextFrameAsync](../../../STS2AIAgent/Game/GameThread.cs); do not create an unbounded frame loop.
5. Preserve the error contract: use [ApiException](../../../STS2AIAgent/Server/ApiException.cs) with an existing status/code category and set `retryable` only when the caller can safely retry after state becomes available.
6. Keep agent logic behind [IGameBridge](../../../STS2AIAgent/Agent/IGameBridge.cs). The production [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) is the game-thread adapter; `AgentLoop` and native MCP should depend on the interface so tests can supply a fake.
7. Keep UI edits in [AgentOverlayHost](../../../STS2AIAgent/Ui/AgentOverlayHost.cs) and reusable Godot control construction in [UiFactory](../../../STS2AIAgent/Ui/UiFactory.cs). Persist settings through `AgentRuntime.SaveSettings` and [SettingsStore](../../../STS2AIAgent/Config/SettingsStore.cs), not by writing JSON from a control callback.
8. Decide whether the integration belongs to the in-process [NativeMcpServer](../../../STS2AIAgent/Server/NativeMcpServer.cs) or the independent [Python MCP server](../../../mcp_server/src/sts2_mcp/server.py). Keep their implementations separate and update the alignment test when their intended tool surface changes.
9. Add or update the appropriate executable C# test registration in [TestRunner](../../../STS2AIAgent.Tests/TestRunner.cs), or a Python test under [mcp_server/tests](../../../mcp_server/tests). The C# project is a `net9.0` executable; it is not a `dotnet test` project.

## Quality Check

Run checks that match the changed layer:

- C# core and contract tests: `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`. The project is an executable with a custom runner, and each test is registered in `TestRunner.AllTests`.
- Python MCP tests: from `mcp_server`, run `uv run --locked python -m unittest discover -s tests -v`, matching the release preflight runner.
- For state/action changes, inspect the relevant `GameStateService` and `GameActionService` paths together; then run the C# executable tests. Contract tests such as [UnlockScreenContractTests](../../../STS2AIAgent.Tests/UnlockScreenContractTests.cs), [GameOverContractTests](../../../STS2AIAgent.Tests/GameOverContractTests.cs), and [CombatDiagnosticsContractTests](../../../STS2AIAgent.Tests/CombatDiagnosticsContractTests.cs) protect source-level action/state invariants.
- For native MCP changes, run the C# tests that exercise [McpServiceTests](../../../STS2AIAgent.Tests/McpServiceTests.cs). For Python tool-surface changes, run [test_native_tool_alignment.py](../../../mcp_server/tests/test_native_tool_alignment.py) and the full Python suite.
- Check that every new polling path has a bounded deadline, every new action returns the established `ActionResponsePayload` shape, and every new route maps expected failures to the established error envelope in `Router.WriteErrorAsync`.
- Live validation is a separate integration step for changes that depend on native screens, timing, or gameplay behavior. Follow the prerequisites in [Operations](../operations/index.md); deterministic tests alone cannot prove those behaviors.
