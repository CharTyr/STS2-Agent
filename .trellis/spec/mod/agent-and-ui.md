# Agent, UI, Configuration, and LLM Boundaries

## Keep agent logic behind `IGameBridge`

[AgentLoop](../../../STS2AIAgent/Agent/AgentLoop.cs) is the pure decision/orchestration layer. It receives an `IGameBridge`, an `ILlmClientFactory`, a settings provider, and optional team/budget providers. `PlayOnceAsync` waits for an actionable state, reads compact state, asks the configured model, validates an action against the latest legal actions and indexes, and delegates through the bridge. `ChatAsync` selects read-only or play tools from `ChatOptions`; teammate conversation is explicitly read-only even if its text sounds like a play request.

[IGameBridge](../../../STS2AIAgent/Agent/IGameBridge.cs) is the seam to preserve when adding agent capabilities. Add a bridge method only for a game-facing capability that AgentLoop/MCP genuinely needs, then implement it in [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) and in relevant test fakes. Keep JSON serialization and game-thread dispatch inside `GameBridge`, not in the decision logic. The bridge currently serializes:

- compact `agent_view` and raw state;
- available-action descriptors and names;
- game-data item/batch/relevant projections;
- actions with `action`, `status`, `stable`, `message`, and compact state; and
- bounded actionable waits and optional JPEG screenshots.

`GameBridge` already calls `GameThread.InvokeAsync` for game access. It serializes and dispatches; it does not call `AgentRuntime`. Session observation belongs to the caller that already holds the fresh frame: `Router` for `/state`, `/action`, and `/strategy`, and `AgentRuntime` for its own game-thread read. A compact frame or `CurrentRunBoundary.RunId` may confirm the same run, but only `ObserveSessionStateSnapshot` on a fresh raw frame may switch or clear the live session.

`AgentLoop` should call the interface and should not import Godot types or reach into `GameStateService` directly. The fake bridge in [McpServiceTests](../../../STS2AIAgent.Tests/McpServiceTests.cs) demonstrates the expected test seam; [AgentLoopTests](../../../STS2AIAgent.Tests/AgentLoopTests.cs) exercises state checks, action legality, pause behavior, and run boundaries without a live game.

## `AgentRuntime` owns application orchestration

[AgentRuntime](../../../STS2AIAgent/Agent/AgentRuntime.cs) owns the singleton lifecycle, current `AgentSettings`, `SettingsStore`, `AgentLoop`, autoplay session, team conversation, budget guard, status fields, and `Changed` event. Its constructor wires `AgentLoop` to a production `GameBridge`, `DefaultLlmClientFactory`, a locked settings provider, and optional team/budget providers. UI and HTTP callers should use its public operations (`SaveSettings`, `StartAutoPlay`, `StopAutoPlay`, chat/team controls, MCP toggle) instead of mutating private state.

`SaveSettings` and `ReloadSettings` keep the same `SessionBudgetGuard` instance. They call `UpdateLimits` with the new `MaxSessionTokens` / `MaxSessionRequests` so caps take effect immediately, while accumulated `ConsumedTokens` and `RequestCount` stay. `SessionBudgetGuard.Observe` records `Math.Max(0, RequestsSpent)`, so an immediate zero-request action cannot decrement the request ledger. The constructor and `TryResetSessionStats` still assign a fresh guard; reset is the path that zeroes usage.

The `Changed` event is a state notification. [AgentOverlayHost](../../../STS2AIAgent/Ui/AgentOverlayHost.cs) subscribes at install time and schedules `RefreshDynamic` through `GameThread.InvokeAsync`; this keeps Godot controls on the game thread even when a runtime operation completes asynchronously. Follow that pattern for new asynchronous status updates.

The dual-instance launch gate is claimed, never shared. `TryLaunchDualInstanceAsync` and `TryContinueDualInstanceAsync` return `null` when another attempt already owns `_dualLaunchGate`, and that null is the only reliable "this attempt is not mine" signal: the winner writes `DualLaunchOutcome` after claiming, and a failed claim is not ordered after that write, so a loser that reads the field can still observe `Idle` or the previous attempt's terminal value. Both HTTP executors answer a null launch with `pending` and classify only the task they own. A concurrent second call must never be reported as `completed`, and a second `continue_ai_teammate` while a launch is in flight must not fall through to the menu probes, which would answer with a misleading "no saved run" 409.

## UI boundary and reusable controls

[ModEntry](../../../STS2AIAgent/ModEntry.cs) installs the overlay only for a display-capable primary instance. [AgentOverlayHost](../../../STS2AIAgent/Ui/AgentOverlayHost.cs) owns the overlay node, tabs (chat, settings, play, teammate, connect), event wiring, dynamic labels, input harvesting, and placement persistence. [UiFactory](../../../STS2AIAgent/Ui/UiFactory.cs) owns repeated Godot control construction and the visual system: the palette in use, the spacing and type scales, styled buttons (`Button` with a `ButtonKind`, `TabButton` with an active state), `Card`, `Heading`, `Badge`, `Gap`, `Divider`, and the layout helpers.

The colour half of that system lives in [OverlayTheme.cs](../../../STS2AIAgent/Ui/OverlayTheme.cs), which is Godot-free on purpose so the executable test project can link it. `OverlayThemeCatalog` holds the presets and resolves an unknown id to the default; `OverlayColor.ContrastRatio` is the measuring instrument the tests use. Three rules follow from that split, and each has a contract test:

- `UiFactory.UseTheme` runs before the first control is built. A Godot control keeps the colours it was created with, so applying the theme afterwards leaves the panel in the previous palette and nothing fails.
- A theme swatch calls `ApplyThemeChoice`, saves only the theme through a clone of the stored settings, repaints existing controls, and replaces only the swatch picker. It must not harvest or rebuild unfinished settings fields. `PersistHarvested` still rebuilds the overlay when a full settings save finds that the saved palette differs from the displayed palette.
- Every preset clears WCAG contrast floors against every surface it draws on. `OverlayThemeTests` fails a theme whose body text, secondary text or accent label is unreadable, which is the check a screenshot cannot make.

`ClipText` stays off on buttons. Godot's clipped button reports a minimum width that excludes its label, so a tab row laid out by natural width collapsed to six empty slivers live on 2026-09-20; the label has to drive the size.

Use `UiFactory` for controls that match an existing pattern; keep tab-specific composition and event handlers in `AgentOverlayHost`. UI callbacks may collect user input, call an `AgentRuntime` method, and request refresh. They should not serialize `AgentSettings` themselves, call `GameActionService` directly, or access native game objects from a background continuation. The existing overlay calls `GameThread.InvokeAsync(RefreshDynamic)` after asynchronous team operations and in `OnRuntimeChanged`.

When adding a control, preserve the existing interaction properties: reuse the existing font/palette and per-control mouse-filter choices. Interactive controls use `MouseFilter.Stop`, while `Row` and `Column` layout containers use `Ignore`; secret fields use `LineEdit.Secret`, and text areas use bounded minimum heights. Keep player-facing copy in the overlay near the control that explains the behavior, and keep domain validation in Config/Runtime code where it can be tested.

## Configuration is typed and persisted through `SettingsStore`

[AgentSettings](../../../STS2AIAgent/Config/AgentSettings.cs) models endpoint records, model records, conversation/play/vision role bindings, overlay preferences, MCP settings, role-test fingerprints, and optional session budgets. `TryResolvePlayModel` falls back to `ConversationModelId` when no explicit play model is selected; `TryResolveRoleModel` returns null when a model or enabled endpoint cannot be resolved. Preserve this role-resolution behavior when adding another role.

`AgentSettings.CreateDefault` supplies an OpenAI-compatible endpoint and tool-capable default model. `EnsureValidShape` repairs null collections, missing IDs, thinking/hotkey defaults, invalid overlay coordinates, invalid MCP ports, and non-positive budgets. Call it before saving or after deserializing user settings; do not silently replace a valid user file with an in-memory ad-hoc object.

[SettingsStore](../../../STS2AIAgent/Config/SettingsStore.cs) is the only persistence service. It resolves an absolute `STS2_AGENT_SETTINGS_PATH` override or the platform application-data path, uses case-insensitive camel-case JSON, writes through a temporary file, maintains a `.bak`, and backs up/restores corrupt JSON. Save failures leave the original file intact and expose a `SettingsPersistenceNotice`. `AgentRuntime.SaveSettings` calls `EnsureValidShape`, persists, replaces the in-memory settings, calls `UpdateLimits` on the existing budget guard, reapplies MCP settings, and raises `Changed`. The new limits apply immediately; accumulated tokens and requests are not reset.

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

## Regression rules from the dev audit

`SettingsClone.Clone` must copy every persisted preference, including `OverlayTheme`. Exercise a non-default value when testing a new preference; a round trip using defaults cannot reveal an omitted field. A successful save retry clears `save_failed` but does not erase a prior corrupt-file recovery notice.

`CompanionConnection.TryReadStateAsync` returns the complete HTTP envelope from the companion process. `TeammateStatus.Parse` must unwrap a successful `data` object, reject malformed or failed envelopes, and select `is_local=true`: that is the AI player in the companion's own payload. Its hand, health and energy must describe the same player. Test the actual envelope as well as a direct state object.

Caller cancellation must propagate through role probes, vision calls and read tools. Do not store an `OperationCanceledException` as a failed endpoint test when the caller's token is canceled. An independent provider timeout with a live caller token remains a reportable provider failure.

`EventStreamSubscribers` retains snapshots only while at least one subscriber exists. Apply the rule to ordinary disconnects, queue overflow, snapshot overflow and a sample that arrives after the final disconnect. Both publish paths must update polling demand after dropping subscribers. The first sample of a new subscriber lifecycle resets duplicate suppression.

## Turn receipts and budget ordering

`AgentLoop.ExecuteActAsync` must remember a successful bridge response before any follow-up state read, wait or boundary check. Its caller receives the acceptance callback immediately. A later observation failure returns an accepted but unsettled result; it must never clear the fact that this decision already acted. An explicit bridge error still allows a corrected action. Tool arguments are parsed as an object before reading either the action or its reason.

`SessionBudgetGuard.CheckBudget(extraRequests, extraTokens)` checks committed totals plus the current turn's pending usage without recording it. `CompleteWithToolsAsync` passes accumulated known tokens and request attempts, including vision. Recording happens once on completion or interruption. Missing provider usage is not estimated.

`AgentTurnCanceledException` remains an `OperationCanceledException` and carries the turn's partial receipt. Run-boundary stops can also carry a receipt. `AutoPlayRecovery.RunAsync` commits that receipt before honoring cancellation. Runtime callers for step, chat, teammate replies and proactive chat consume interrupted receipts in their existing turn gate. `RecordTurnReceipt` records usage and accepted decisions; `ApplyPlayResult` additionally updates active-play UI fields. Interrupted autoplay uses only the receipt path.

Recovery's optional `turnGate` spans model/game work and receipt commit; the gate is released in a `finally` block. A queued caller must not see the old budget after the previous call has already spent it. Recovery's optional `afterTurn` runs only after play accounting and budget/recovery checks. The runtime puts proactive chat there, acquires the same gate for that chat, and rechecks the budget afterward.

`AgentTurnIntegrityTests` covers accepted-action observation faults, JSON fallback, explicit rejection, token/vision budgets, malformed arguments, cancellation at multiple boundaries, proactive accounting and a queued caller blocked until commit. Source-wiring contracts verify the game-dependent runtime connects those tested components. These tests do not establish live Godot rendering or full-game behavior.
