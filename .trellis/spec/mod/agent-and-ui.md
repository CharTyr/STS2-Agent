# Agent, UI, Configuration, and LLM Boundaries

## Keep agent logic behind `IGameBridge`

[AgentLoop](../../../STS2AIAgent/Agent/AgentLoop.cs) is the pure decision/orchestration layer. It receives an `IGameBridge`, an `ILlmClientFactory`, a settings provider, and optional team/budget providers. `PlayOnceAsync` waits for an actionable state, reads compact state, asks the configured model, validates an action against the latest legal actions and indexes, and delegates through the bridge. `ChatAsync` selects read-only or play tools from `ChatOptions`; teammate conversation is explicitly read-only even if its text sounds like a play request.

[IGameBridge](../../../STS2AIAgent/Agent/IGameBridge.cs) is the seam to preserve when adding agent capabilities. Add a bridge method only for a game-facing capability that AgentLoop/MCP genuinely needs, then implement it in [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) and in relevant test fakes. Keep JSON serialization and game-thread dispatch inside `GameBridge`, not in the decision logic. The bridge currently serializes:

- compact `agent_view` and raw state;
- available-action descriptors and names;
- game-data item/batch/relevant projections;
- actions with `action`, `status`, `stable`, `message`, and compact state; and
- bounded actionable waits and optional JPEG screenshots.

`GameBridge` already calls `GameThread.InvokeAsync` for game access. `AgentLoop` should call the interface and should not import Godot types or reach into `GameStateService` directly. The fake bridge in [McpServiceTests](../../../STS2AIAgent.Tests/McpServiceTests.cs) demonstrates the expected test seam; [AgentLoopTests](../../../STS2AIAgent.Tests/AgentLoopTests.cs) exercises state checks, action legality, pause behavior, and run boundaries without a live game.

## `AgentRuntime` owns application orchestration

[AgentRuntime](../../../STS2AIAgent/Agent/AgentRuntime.cs) owns the singleton lifecycle, current `AgentSettings`, `SettingsStore`, `AgentLoop`, autoplay session, team conversation, budget guard, status fields, and `Changed` event. Its constructor wires `AgentLoop` to a production `GameBridge`, `DefaultLlmClientFactory`, a locked settings provider, and optional team/budget providers. UI and HTTP callers should use its public operations (`SaveSettings`, `StartAutoPlay`, `StopAutoPlay`, chat/team controls, MCP toggle) instead of mutating private state.

The `Changed` event is a state notification. [AgentOverlayHost](../../../STS2AIAgent/Ui/AgentOverlayHost.cs) subscribes at install time and schedules `RefreshDynamic` through `GameThread.InvokeAsync`; this keeps Godot controls on the game thread even when a runtime operation completes asynchronously. Follow that pattern for new asynchronous status updates.

## UI boundary and reusable controls

[ModEntry](../../../STS2AIAgent/ModEntry.cs) installs the overlay only for a display-capable primary instance. [AgentOverlayHost](../../../STS2AIAgent/Ui/AgentOverlayHost.cs) owns the overlay node, tabs (chat, settings, play, teammate, connect), event wiring, dynamic labels, input harvesting, and placement persistence. [UiFactory](../../../STS2AIAgent/Ui/UiFactory.cs) owns repeated Godot control construction and the local palette/style helpers (`PanelStyle`, `Button`, `Label`, `Line`, `Check`, `Combo`, `Multiline`, `Rich`, and layout helpers).

Use `UiFactory` for controls that match an existing pattern; keep tab-specific composition and event handlers in `AgentOverlayHost`. UI callbacks may collect user input, call an `AgentRuntime` method, and request refresh. They should not serialize `AgentSettings` themselves, call `GameActionService` directly, or access native game objects from a background continuation. The existing overlay calls `GameThread.InvokeAsync(RefreshDynamic)` after asynchronous team operations and in `OnRuntimeChanged`.

When adding a control, preserve the existing interaction properties: reuse the existing font/palette and per-control mouse-filter choices. Interactive controls use `MouseFilter.Stop`, while `Row` and `Column` layout containers use `Ignore`; secret fields use `LineEdit.Secret`, and text areas use bounded minimum heights. Keep player-facing copy in the overlay near the control that explains the behavior, and keep domain validation in Config/Runtime code where it can be tested.

## Configuration is typed and persisted through `SettingsStore`

[AgentSettings](../../../STS2AIAgent/Config/AgentSettings.cs) models endpoint records, model records, conversation/play/vision role bindings, overlay preferences, MCP settings, role-test fingerprints, and optional session budgets. `TryResolvePlayModel` falls back to `ConversationModelId` when no explicit play model is selected; `TryResolveRoleModel` returns null when a model or enabled endpoint cannot be resolved. Preserve this role-resolution behavior when adding another role.

`AgentSettings.CreateDefault` supplies an OpenAI-compatible endpoint and tool-capable default model. `EnsureValidShape` repairs null collections, missing IDs, thinking/hotkey defaults, invalid overlay coordinates, invalid MCP ports, and non-positive budgets. Call it before saving or after deserializing user settings; do not silently replace a valid user file with an in-memory ad-hoc object.

[SettingsStore](../../../STS2AIAgent/Config/SettingsStore.cs) is the only persistence service. It resolves an absolute `STS2_AGENT_SETTINGS_PATH` override or the platform application-data path, uses case-insensitive camel-case JSON, writes through a temporary file, maintains a `.bak`, and backs up/restores corrupt JSON. Save failures leave the original file intact and expose a `SettingsPersistenceNotice`. `AgentRuntime.SaveSettings` calls `EnsureValidShape`, persists, swaps the in-memory settings and budget guard, reapplies MCP settings, and raises `Changed`.

[SettingsBinding](../../../STS2AIAgent/Config/SettingsBinding.cs) prevents deleting an endpoint/model that is still referenced by the conversation, play, or vision role. Keep deletion and rebinding checks in this domain helper so the UI can display an accurate message and tests can cover disabled endpoints and play-role fallback. [FirstRunSetup](../../../STS2AIAgent/Config/FirstRunSetup.cs) separately decides whether the play model is configured, endpoint-valid, verified, or failed; a filled default does not imply that inviting an AI teammate is ready.

The settings tests in [SettingsStoreTests](../../../STS2AIAgent.Tests/SettingsStoreTests.cs), [SettingsExperienceRegressionTests](../../../STS2AIAgent.Tests/SettingsExperienceRegressionTests.cs), and [PlayerExperienceTests](../../../STS2AIAgent.Tests/PlayerExperienceTests.cs) are executable contract examples. They cover round trips, corrupt-file recovery, secret-safe notices, role binding, first-run gating, budget parsing, and safe reset behavior.

## LLM boundary and request behavior

[LlmTypes](../../../STS2AIAgent/Llm/LlmTypes.cs) defines `ILlmClient`, `ILlmClientFactory`, typed messages/tool calls, usage accounting, and `LlmException`. `DefaultLlmClientFactory` creates the production [OpenAiCompatibleClient](../../../STS2AIAgent/Llm/OpenAiCompatibleClient.cs) with a shared `HttpClient`. Keep provider-specific request shaping in this client or a focused builder such as `ThinkingRequestBuilder`, not in `AgentLoop` or UI code.

`OpenAiCompatibleClient.ResolveCompletionsUrl` normalizes a base endpoint and appends `/chat/completions` only when needed. `CompleteAsync` supports tool calls, streaming usage, reasoning/DeepSeek request variants, caller cancellation, and a bounded request timeout; it can retry a streaming request without streaming when the provider rejects that mode. `PingAsync` is the model-role verification path. Preserve the distinction between an `OperationCanceledException` caused by the caller and an `LlmException` caused by a provider timeout or HTTP failure.

Use an injected `HttpMessageHandler` or `HttpClient` in tests rather than contacting a real provider. [LlmClientTests](../../../STS2AIAgent.Tests/LlmClientTests.cs) verifies URL normalization, request bodies, SSE parsing, usage accumulation, stalled-body timeout, and user cancellation. New provider behavior should extend this deterministic style.

## Native MCP and Python MCP remain separate

The in-process [NativeMcpServer](../../../STS2AIAgent/Server/NativeMcpServer.cs) uses `IGameBridge`, serves JSON-RPC over the mod's loopback HTTP listener, builds its tool list from `AgentTools.Mcp`, and applies session/origin policy. Its contract is covered by [McpServiceTests](../../../STS2AIAgent.Tests/McpServiceTests.cs), including disabled-service responses, initialization, tool/resource listing, action calls, and same-origin versus untrusted-origin handling.

The Python implementation uses [Sts2Client](../../../mcp_server/src/sts2_mcp/client.py) for `/health`, `/state`, `/actions/available`, `/action`, and SSE events, then registers FastMCP tools in [server.py](../../../mcp_server/src/sts2_mcp/server.py). `guided` is the compact default; `layered` adds handoff/knowledge tools; `full` adds generated legacy per-action tools. The Python sidecar also owns `wait_for_event`, which is deliberately not part of the native C# compact tool list. Keep profile gating in `_normalize_tool_profile` and `create_server` rather than conditionally hiding tools in unrelated handlers.

When a shared guided tool changes, update the C# `AgentTools.Mcp`, native dispatch, Python guided registration, and the alignment/contract tests. [test_native_tool_alignment.py](../../../mcp_server/tests/test_native_tool_alignment.py) reads the C# source contract and expects Python guided tools to match it plus exactly `wait_for_event`; this is an intentional synchronization check, not permission to merge the two implementations.

## Test execution shape

The C# test project [STS2AIAgent.Tests.csproj](../../../STS2AIAgent.Tests/STS2AIAgent.Tests.csproj) targets `net9.0`, sets `OutputType` to `Exe`, links selected production source files with `Compile Include`, and runs [TestRunner.Main](../../../STS2AIAgent.Tests/TestRunner.cs). Use `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`; `dotnet test` is not the local contract for this project. Tests should remain deterministic and focused on state, transport, policy, parsing, and orchestration seams. Live game startup, mod deployment, and provider calls are separate validations and should never be smuggled into the unit runner.

