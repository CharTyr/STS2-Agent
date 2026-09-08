# Mod Architecture and Boundaries

## Runtime shape

The mod has one in-process C# runtime and one optional out-of-process Python sidecar. The C# project targets `net9.0`, enables nullable reference types, uses implicit usings, and references the game's `sts2`, `0Harmony`, and `GodotSharp` assemblies in [STS2AIAgent.csproj](../../../STS2AIAgent/STS2AIAgent.csproj). Keep new C# code compatible with nullable analysis instead of suppressing warnings or introducing nullable state that the caller cannot observe.

Startup is intentionally ordered in [ModEntry.Initialize](../../../STS2AIAgent/ModEntry.cs): capture the game-thread context, start event publication, start the loopback HTTP server, initialize `AgentRuntime`, then install the overlay unless this is a companion or headless instance. Shutdown releases resources through `AgentRuntime.Shutdown`, overlay uninstall, event stop, and HTTP stop. Code that needs startup state should use these existing lifecycle owners rather than constructing a second server, runtime, or overlay.

The local request path is:

```text
HttpServer.ListenLoopAsync
  -> Router.HandleAsync
      -> /state, /actions/available: GameThread.InvokeAsync(GameStateService...)
      -> /action: GameThread.InvokeAsync(GameActionService.ExecuteAsync)
      -> /mcp: NativeMcpServer.HandleHttpAsync
```

`HttpServer` only accepts loopback traffic and dispatches each context to `Router`; [Router.HandleAsync](../../../STS2AIAgent/Server/Router.cs) supplies request IDs, route selection, JSON/SSE responses, and the common exception envelope. Keep transport parsing and response formatting in this layer. Do not put route-specific JSON parsing into game state builders or action handlers.

## Game-thread ownership

The compact-state read in `GameBridge.GetCompactStateJsonAsync` is a concrete example:

```csharp
return GameThread.InvokeAsync(() =>
{
    var state = GameStateService.BuildStatePayload();
    return JsonSerializer.Serialize(state.agent_view ?? state, JsonOptions);
});
```

Follow the existing C# naming style: PascalCase methods and types, `_camelCase`
private fields. Wire payload members intentionally retain snake_case names such
as `agent_view`; do not rename them as a style-only cleanup. See
[GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) and
[GameStateService](../../../STS2AIAgent/Game/GameStateService.cs).

[GameThread](../../../STS2AIAgent/Game/GameThread.cs) captures `SynchronizationContext.Current` and the managed thread ID during initialization. Its `InvokeAsync` overloads execute inline when already on the game thread and otherwise post to the captured context, propagating results and exceptions through a `TaskCompletionSource`. Godot objects and game managers must only be inspected or mutated inside that boundary.

The boundary already exists at both external entry points:

- `Router.HandleAsync` wraps `/state`, `/actions/available`, `/data/*`, and `/action` calls in `GameThread.InvokeAsync`.
- [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) wraps its state, action, game-data, and screenshot operations in `GameThread.InvokeAsync` for agent and MCP callers.

An action handler reached through Router or GameBridge therefore runs inside the existing dispatch. Do not wrap the handler again just to satisfy a generic “game thread” rule. Inside a handler, use `GameThread.WaitForNextFrameAsync` for frame synchronization and preserve the current synchronization context. [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs) delegates its private frame helper to this method; the helper deliberately avoids `ConfigureAwait(false)` because resuming on a thread-pool thread would break Godot access.

`WaitForNextFrameAsync` is not an action timeout. When a valid Godot `SceneTree` exists, it waits for `ProcessFrame` but bounds the wait with a 50 ms `Task.Delay` because an occluded/background window may never emit the signal. When the game or tree is unavailable it falls back to a short delay. Each action transition still needs its own deadline, such as `WaitForPlayCardTransitionAsync(card, TimeSpan.FromSeconds(12))` in [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs). A loop that only waits for frames without a deadline is incomplete.

## State, action, and transport ownership

[GameStateService](../../../STS2AIAgent/Game/GameStateService.cs) is the read-side projection of native screen, combat, run, and multiplayer objects. It decides the canonical screen name, builds nested payloads, and derives available action names/descriptors. [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs) owns mutations, native UI calls, argument validation, and transition stabilization. Keep these directions separate: state builders must not click buttons, and action handlers should return a fresh state rather than inventing a parallel state model.

`Router` exposes `/health`, `/state`, `/actions/available`, `/data/{collection}`, `/events/stream`, `/action`, and local session/companion controls. The `GameStateService` and `GameActionService` APIs are the stable seam for those routes. A new route is only warranted for a transport capability that cannot be represented by the existing endpoint; a new game action belongs in `/action` and its action switch.

Errors cross the HTTP boundary through [ApiException](../../../STS2AIAgent/Server/ApiException.cs). The exception carries `StatusCode`, machine-readable `Code`, optional `Details`, and `Retryable`; Router serializes those fields under `error` and adds `request_id`. Preserve this shape so the Python [Sts2ApiError](../../../mcp_server/src/sts2_mcp/client.py) can classify the same failure. Unexpected exceptions become a generic `500 internal_error` at the Router boundary; do not leak an unrelated ad-hoc JSON shape from a handler.

## Agent and MCP boundaries

[IGameBridge](../../../STS2AIAgent/Agent/IGameBridge.cs) is the pure agent-facing interface. It exposes compact/raw state, available actions, screen and game-data queries, action execution, bounded actionable waits, and optional screenshots. [AgentLoop](../../../STS2AIAgent/Agent/AgentLoop.cs) consumes that interface together with `ILlmClientFactory`; it does not need to know Godot types. [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) is the production adapter that serializes `GameStateService` results and invokes `GameActionService` on the game thread.

The native MCP server is an in-process JSON-RPC/HTTP implementation. [NativeMcpServer](../../../STS2AIAgent/Server/NativeMcpServer.cs) receives `/mcp` traffic from Router, lists `AgentTools.Mcp`, calls the bridge, and implements local origin/session policy. Its tools are aligned with the C# tool definitions in [AgentTools](../../../STS2AIAgent/Agent/AgentTools.cs). Keep native MCP behavior inside these C# classes.

The Python sidecar is a separate implementation. [Sts2Client](../../../mcp_server/src/sts2_mcp/client.py) speaks the local HTTP API and maps transport/API failures to `Sts2ApiError`; [create_server](../../../mcp_server/src/sts2_mcp/server.py) registers FastMCP tools and profile-specific surfaces; [network_server.py](../../../mcp_server/src/sts2_mcp/network_server.py) owns the sidecar's HTTP transport options. Do not make the C# native server import Python behavior or make the Python client depend on C# internals. When the common guided tool contract changes, update both implementations and verify [test_native_tool_alignment.py](../../../mcp_server/tests/test_native_tool_alignment.py), which intentionally permits Python's `wait_for_event` convenience tool in addition to the native surface.

## Cross-layer change rule

For a change crossing state, action, agent, UI, or MCP, trace it in both directions before editing. A useful local example is the `play_card` path: `GameStateService` advertises it, `AgentTools` describes its indexes, `AgentLoop` validates against the latest state and delegates through `IGameBridge`, `GameBridge` calls `GameActionService`, and the action handler returns `status`, `stable`, `message`, and a fresh state. Contract tests in [AgentLoopTests](../../../STS2AIAgent.Tests/AgentLoopTests.cs), [McpServiceTests](../../../STS2AIAgent.Tests/McpServiceTests.cs), and [CombatDiagnosticsContractTests](../../../STS2AIAgent.Tests/CombatDiagnosticsContractTests.cs) show the expected seams.
