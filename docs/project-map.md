# Project map

> **Read this first.** This is the orientation document for any session working in this repo: what
> each file is for, how the pieces talk, the invariants that bite, and where to go for a given task.
> It replaces "send explorers to re-learn the project". Go to the source only for the files your
> task touches.
>
> **Keep it current, in the same change.** Add, rename or delete a source file, or change a flow or
> invariant listed here, and you update this file in that commit. `ProjectMap.EverySourceMapped` and
> `ProjectMap.NoDanglingPaths` (C# suite, `STS2AIAgent.Tests/ProjectMapFreshnessTests.cs`) fail when
> a mod source or MCP module is missing from this map, or when the map names a path that no longer
> exists. They don't catch outdated prose, so skim the sections you touched.
>
> Snapshot: 2026-09-23, `dev`. Deeper references: [api.md](api.md) (HTTP contract),
> [ADR 0001](adr/0001-single-action-surface.md) (one action surface),
> [ADR 0002](adr/0002-two-mcp-surfaces.md) (two MCP surfaces), `.trellis/spec/` (coding specs),
> `AGENTS.md` (build/test/release commands; local, gitignored), `.trellis/spec/index.md`.

## 1. The system in one screen

```
Slay the Spire 2 (Godot 4, .NET 9)
└─ STS2AIAgent.dll (mod)                       ModEntry.Initialize
   ├─ GameThread            captures the game SynchronizationContext; ALL game/Godot access goes through it
   ├─ GameStateService      /state builder (raw + compact agent_view) + the ONE action enumeration
   ├─ GameActionService     /action executor (validate → game thread → bounded wait → ActionResponsePayload)
   ├─ HttpServer/Router     127.0.0.1:8080 (auto-increments if busy, unless STS2_API_PORT pins it)
   │    ├─ NativeMcpServer  POST /mcp (off by default) — same tool face as the Python sidecar
   │    └─ GameEventService SSE /events/stream (polls state, emits typed events)
   ├─ AgentRuntime (singleton) — lifecycle: settings, auto-play session, chat, budget, co-op, persistence
   │    └─ AgentLoop        one decision turn: state → LLM (tools) or Jev → act → receipt
   │         └─ GameBridge  the seam from the agent to GameStateService/GameActionService (via GameThread)
   ├─ AgentOverlayHost      in-game overlay (Play / Settings tabs), only on the host, never on the companion
   └─ Multiplayer/*         local dual-instance co-op: launches a 2nd game as the AI "companion"
mcp_server/ (Python, optional)  FastMCP sidecar wrapping the HTTP API for external AI clients
```

Two ways to drive the game:
1. **In-game agent.** The player configures an OpenAI-compatible endpoint and model in the overlay
   and presses Start. `AgentRuntime` → `AgentLoop` → `GameBridge` → game.
2. **External agent.** An MCP client uses the Python sidecar or the native `/mcp`, both over the
   HTTP API.

Both paths end in the same `GameActionService.ExecuteAsync` behind `ActionExecutionGate`, so only
one action runs at a time.

## 2. Cross-cutting invariants (read before any change)

- **Game thread.** Game objects and Godot nodes may only be touched on the game thread:
  `GameThread.InvokeAsync(...)`. From an agent turn, always use the overloads that take the
  turn's `CancellationToken`, plus a deadline where the turn cannot wait forever:
  `InvokeAsync(Func<T>, timeout, token)`, or `InvokeAsync(Func<Task<T>>, startTimeout, token)`
  for async work. The async overload can abandon work only while it is still queued, so an
  abandoned act never runs later. The token-less overloads only check cancellation inside the
  callback. When the game thread stops pumping (window occluded or backgrounded), the callback
  never runs and pause can't land. Guarded by `AgentTurn.GameThreadPostsCarryToken`.
- **No unbounded awaits in game paths.** Every await in `GameActionService*`, the overlay and the
  co-op coordinators is bounded (`GameTaskBoundingContractTests`). Wait for transitions with
  `WaitForNextFrameAsync` in a polling loop with a deadline.
- **One action surface (ADR 0001).** `EnumerateAvailableActions` (`GameStateService.cs`) is the only
  place an action becomes available. `/state.available_actions` and the `/actions/available`
  descriptors both come from it. A payload list and its action handler must index the **same
  helper** (see §5 index table).
- **Two MCP surfaces (ADR 0002).** A tool is added to both the native `NativeMcpServer*` + `AgentTools`
  and the Python `server.py`. `mcp_server/tests/test_native_tool_alignment.py` compares them in both
  directions with an empty exemption list.
- **Backward-compatible API.** New fields are additive and old fields are never removed. Every new
  payload field needs a row in `docs/api.md`, or the `api-facts` gate
  (`scripts/check_verification_gates.py`) fails.
- **Actions are never auto-replayed.** A lost action response is `outcome_unknown`, followed by at
  most one `/state` reconciliation, never a retry. The only exception is the mod's `action_in_flight`
  refusal, which is retryable.
- **One request = one accounting.** Every model or Jev request is counted exactly once, including
  failed, canceled or interrupted ones (`AgentTurnCanceledException.Receipt`). The budget is checked
  **before** dispatch (`SessionBudgetGuard.CheckBudget`).
- **Source-text contract tests.** Many offline tests pin literal source text, method-body hashes
  (`GameStateServiceRelocationContractTests`, `PredicateRelocationContractTests`), file placement in
  partial classes, file line-count budgets (`SourceShapeContractTests`, and `test_source_shape.py`
  for Python: `client.py` ≤ 700 lines) and the list of files the tests don't compile
  (`SourceCoverageTests.KnownUncompiledSources`: every new game-dependent `.cs` goes there). If you
  change a pinned body on purpose, regenerate its hash and say why in the test's remarks.
- **Localization.** The Chinese string is the key: `Loc.T("中文")`. Every call site needs an English
  entry in `Localization/Loc.Strings.*.cs` (`LocalizationTests`). Never freeze `Loc.T` at
  construction time.
- **Secrets.** API keys are never logged, exported or persisted outside the settings file:
  `DiagnosticExport.Redact`, and session files redact chat.
- **Reasoning is display-only.** A provider's `reasoning_content` reaches the player only through
  `LiveThoughtBuffer`, and only while `ShowThinkingInChat` is on: with the switch off (the default) it is
  not stored at all. Either way it is never persisted, logged, exported or replayed to a provider -- the
  tool-call echo is the provider's own field, not this buffer. The buffer empties when a turn records its
  reasoning, when a turn ends without one, and when a request starts, so a partial can neither double a
  completed bubble nor outlive its turn.

## 3. Mod entry, overlay UI, configuration

### Files
- `STS2AIAgent/ModEntry.cs` — `[ModInitializer]`. Order: LocSource → GameThread → GameEventService → HttpServer → AgentRuntime → `ReflectedGameMembers.ProbeAtStartup` → overlay (skipped on the companion or headless). Shutdown runs on ProcessExit/DomainUnload.
- `STS2AIAgent/Ui/AgentOverlayHost.cs` — overlay chrome and lifecycle: CanvasLayer install and retry until `NGame.Instance`, placement/drag/edge-tab (clamped on-screen), hotkey poll, the 800 ms `OnProcessFrame` tick, `ToggleVisible`, `RebuildInPlace`.
- `STS2AIAgent/Ui/AgentOverlayHost.Tabs.cs` — `_pages`, `BuildPages`, `ShowTab` (flushes dirty settings when leaving Settings), tab row.
- `STS2AIAgent/Ui/AgentOverlayHost.Pages.cs` — Play and co-op page bodies plus the whole `RefreshDynamic` pass (`RefreshModeControls`, `RefreshJevReading`, `ApplyTone`).
- `STS2AIAgent/Ui/AgentOverlayHost.ChatCard.cs` — conversation card, `RefreshChatLog` (draws the recorded turns, then the in-flight `AgentRuntime.LiveThought` partial when the reasoning switch is on), `ChatTailLimit = 80`.
- `STS2AIAgent/Ui/AgentOverlayHost.Settings.cs` — settings form: `BuildSettingsPage`, `RebuildSettingsForm`, `HarvestSettings` (the only reader of form fields), `SaveSettingsFromUi`, model cards and the per-model Test button, the Jev section, advanced options.
- `STS2AIAgent/Ui/AgentOverlayHost.PlayControl.cs` — solo/co-op mode switch (`ChangePlayModeAsync`: pause first, then commit) and companion Jev settings sync.
- `STS2AIAgent/Ui/UiFactory.cs` — every control and palette→StyleBox. `Label` does **not** wrap (reports its full text as min width); `Wrapped` does, with a 120 px floor. `Row` stretches its children, `TightRow` doesn't. Also `TryParseHotkey`.
- `STS2AIAgent/Ui/OverlayTheme.cs` — Godot-free palettes plus contrast math (test-enforced contrast floors).
- `STS2AIAgent/Ui/OverlaySwatch.cs` — theme preview tile.
- `STS2AIAgent/Ui/OverlayTabCatalog.cs` — tabs as data (`Play`, `Settings`).
- `STS2AIAgent/Ui/SegmentedSwitch.cs` — exclusive segment control (`SetSelected` without a callback, `Select` with one).
- `STS2AIAgent/Ui/PlayModeSwitchPolicy.cs` — pure gate `ShouldCommit(current, requested, pauseConfirmed)`.
- `STS2AIAgent/Ui/DecisionLogView.cs` — Godot-free decision line text (`RecentLimit = 50`).
- `STS2AIAgent/Config/AgentSettings.cs` — the settings model: endpoints, models, bindings, Jev, budgets, co-op, `ModelTests`, `RoleTests`. `EnsureValidShape` repairs without replacing. `TryResolvePlayModel` (play = conversation model), `TryResolveRoleModel`, `HasJevConfigured`.
- `STS2AIAgent/Config/SettingsStore.cs` — the only persistence path: atomic temp file + `File.Replace` with a `.bak`, corrupt-file recovery, `LastNotice`. `STS2_AGENT_SETTINGS_PATH` overrides the location.
- `STS2AIAgent/Config/SettingsClone.cs` — deep copy of **every** persisted field. A new setting must be added here too.
- `STS2AIAgent/Config/SettingsBinding.cs` — blocks deleting an endpoint or model that a role still uses.
- `STS2AIAgent/Config/FirstRunSetup.cs` — first-run and invite gate verdict from the play **role** record (`missing`/`failed`/`filled_unverified`/`verified`).
- `STS2AIAgent/Config/ModelRoleProbe.cs` — role records (conversation/play/vision), `Fingerprint` (url+model+key SHA-256), `Current`, `Upsert`, `InvalidateMismatched`, error `Classify`.
- `STS2AIAgent/Config/SessionBudgetLimits.cs` — budget input parsing and reset gating.
- `STS2AIAgent/Config/InstanceRole.cs` — host vs companion identity (from the environment).
- `STS2AIAgent/Config/ThinkingIntensity.cs` — thinking-level parsing and mapping.
- `STS2AIAgent/Localization/Loc.cs` — `Loc.T`, the language switch, the `LanguageChanged` event.
- `STS2AIAgent/Localization/LocSource.cs` — resolves the player's language (settings file first, then the game's manager).
- `STS2AIAgent/Localization/Loc.Strings.Ui.cs` / `STS2AIAgent/Localization/Loc.Strings.Runtime.cs` / `STS2AIAgent/Localization/Loc.Strings.Facing.cs` / `STS2AIAgent/Localization/Loc.Strings.Game.cs` / `STS2AIAgent/Localization/Loc.Strings.Support.cs` — the zh→en tables, split by area.
- `STS2AIAgent/Vision/ScreenshotService.cs` — JPEG readback of the game viewport; the Godot capture host hides/restores the overlay and waits for `RenderingServer.FramePostDraw`, not `SceneTree.ProcessFrame` (which still shows the old frame). The capture gate and deadline policy are in `ScreenshotCapturePolicy`.

### Flows
- **Install → tick:** `Install` → `TryBuildOrRetry` → `Build` (`UiFactory.UseTheme` **before** the first control) → pages → `ShowTab` → the 800 ms tick (`RefreshDynamic`, screen label via `GameStateService.CurrentScreenName()`, never a full state build). `AgentRuntime.Changed` → `GameThread.InvokeAsync(RefreshDynamic)`.
- **Save settings:** `SaveSettingsFromUi` → `HarvestSettings` (clone + read editors) → `ModelRoleProbe.InvalidateMismatched` → `AgentRuntime.SaveSettings` → `RebuildSettingsForm`. The rebuild **destroys every editor and label**, so set any status text *after* the rebuild.
- **Model test:** the card's Test runs `AgentRuntime.TestModelAsync` (ping plus a real `act` tool probe). It writes `ModelTests` (the badge) **and** the role records of every role resolving to that model (`RoleTests`, which the invite gate, teammate resume and first-run read). The footer "test connection" runs `TestConfiguredRolesAsync` (ping only, writes role records only).
- **Mode switch:** `ChangePlayModeAsync` pauses the active loop or companion and waits for confirmation. It commits `OverlayPlayMode` only if the pause was confirmed and never auto-starts the other mode.

### Traps
- Post-await UI writes: wrap them in `GameThread.InvokeAsync`. That call throws `InvalidOperationException` during shutdown races, so catch it.
- Any label that can hold a sentence or a path must use `UiFactory.Wrapped`, not `Label`, including a deletion error that starts empty and later becomes long. Three horizontally adjacent metric tiles also clip an expanding Token value; put that metric on its own full-width row. `Wrapped` has a 120 px floor, so a row with two `Wrapped` labels plus two drop-downs (the chat options row, measured 457 px on 2026-09-24) overflows by itself: one label/control pair per row. After a layout change, read the `[STS2AIAgent.Wide]` line in the game log (`offenders=0` per tab). Clipping at the 440 px panel has been re-found in live passes.
- `_settingsDirty` is set only by `WatchLine/WatchCheck/WatchCombo`. Toggles that save immediately (theme, language, companion choice, dual-layer) bypass it on purpose.
- A new setting touches `AgentSettings`, `SettingsClone`, `HarvestSettings` and `EnsureValidShape`.

## 4. In-game agent: loop, runtime, dual-layer, persistence

### Files — AgentLoop (one decision turn; owns no lifecycle)
- `STS2AIAgent/Agent/AgentLoop.cs` — `ChatAsync`, `PlayOnceAsync` (check state → `WaitUntilActionableAsync(20 s)` → Jev or LLM), `CompleteWithToolsAsync` (bounded tool rounds, `MaxToolRounds = 8`), `ExecuteActAsync`, `ExecuteReadToolAsync`, `TestConfiguredRolesAsync`.
- `STS2AIAgent/Agent/AgentLoop.Decision.cs` — `TryDecideWithJevAsync` (budget pre-check → snapshot → decide → execute) and `PlayWithModelAsync` (prompt assembly; **the prompt cache order is load-bearing**: stable prefix first, live state last).
- `STS2AIAgent/Agent/AgentLoop.Arguments.cs` — pure tool-argument parsers.
- `STS2AIAgent/Agent/AgentLoop.Probes.cs` — `CreateClient` (per-request timeout from live settings), run-id and planning-state probes.

### Files — AgentRuntime (singleton; one partial per concern)
- `STS2AIAgent/Agent/AgentRuntime.cs` — `_gate` (state lock), `_turnGate` (semaphore: turn, chat, step and teammate reply are mutually exclusive), `StartAutoPlay`/`StopAutoPlay`, `AutoPlayLoopAsync` (the turn body), `TryCompanionImmediateAsync` (companion follows the host's map vote, confirms FTUE modals), chat entry, dual-launch core, `ClassifyStop`.
- `STS2AIAgent/Agent/AgentRuntime.PlayControl.cs` — `PauseForModeSwitchAsync` (bounded 30 s pause confirmation).
- `STS2AIAgent/Agent/AgentRuntime.ModeControl.cs` — solo/co-op mutual exclusion: `TryBeginPlayModeSwitch`/`EndPlayModeSwitch`, start gates.
- `STS2AIAgent/Agent/AgentRuntime.Accounting.cs` — `AccountTurn`, `RecordTurnReceipt`, `ApplyPlayResult`, the chat history buffer, Jev panel readings, `DescribeActResult`.
- `STS2AIAgent/Agent/AgentRuntime.Session.cs` — run-scoped session: `ObserveSessionStateSnapshot` (only a fresh **raw** game-thread frame may switch or clear the session), `FlushSessionIfDirty`, `RecordDecision`.
- `STS2AIAgent/Agent/AgentRuntime.Status.cs` — status line: `SetStatus`, `SetPlayPhase` (phase plus elapsed seconds), `SetRequestingModelStatus` (also drops the previous request's streamed reasoning).
- `STS2AIAgent/Agent/AgentRuntime.PlayInstructions.cs` — messages sent during solo auto-play become bounded, one-shot, run-scoped guidance for the next decision.
- `STS2AIAgent/Agent/LiveThoughtBuffer.cs` — the reasoning a model is still producing, streamed so the conversation can show it before the turn completes. Display-only (never persisted, never replayed); the player's "show reasoning" switch gates storage, and `Reset` is what stops a completed turn from drawing a second thought bubble.
- `STS2AIAgent/Agent/AgentRuntime.Jev.cs` — `StrategyStore`, `StrategyPlanner` wiring, `BuildJevDecider` (re-resolved from live settings every turn), `TestJevConnectionAsync`.
- `STS2AIAgent/Agent/AgentRuntime.Team.cs` — AI teammate chat and status (`SendTeamMessageAsync`, `ReplyToTeammateAsync`, live status throttle).
- `STS2AIAgent/Agent/AgentRuntime.ModelTest.cs` — `TestModelAsync`, `ModelTestFor`, `IsModelVerified` (fingerprint match).
- `STS2AIAgent/Agent/AgentRuntime.CompanionSettings.cs` — token-authorized Jev settings patch applied to a running companion.

### Files — policies and pure logic (offline-tested; must stay free of Godot/game types)
- `STS2AIAgent/Agent/AutoPlayRecovery.cs` — the turn driver (`RunAsync`: turn gate → turn → commit/receipt → `Observe` → backoff or stop) and the retry/no-progress policy. Waits on the **player** (companion map vote) never run the 150 s stuck clock.
- `STS2AIAgent/Agent/AutoPlaySession.cs` — one loop task: `TryStart`, `RequestPause`, `Phase` (`running`/`stopping`/`paused`).
- `STS2AIAgent/Agent/NoProgressPolicy.cs` — limits (`WaitingForGameLimit = 150 s`, `UnsettledLimit`, repeat limit) and the state `Fingerprint`.
- `STS2AIAgent/Agent/StopKindPolicy.cs` — stop message → `budget`/`run_end`/`config`/`network`/`failed`.
- `STS2AIAgent/Agent/SessionBudgetGuard.cs` — token and request caps; `CheckBudget(extraRequests, extraTokens)` runs before every dispatch.
- `STS2AIAgent/Agent/CurrentRunBoundary.cs` — leaving the run, or a new run identity → `AutoPlayStoppedException(RunEnd)`.
- `STS2AIAgent/Agent/AgentTurnCanceledException.cs` — cancellation that carries the partial turn receipt.
- `STS2AIAgent/Agent/ContextCompaction.cs` — summary plus the last ≤6 decisions once 80% of the context window is used.
- `STS2AIAgent/Agent/PlayPrompt.cs` — system prompts (play, chat, teammate), guidance slices, reply-language rule, JSON act fallback.
- `STS2AIAgent/Agent/PlaybookSections.cs` — screen → embedded playbook slice (≤2400 chars; mirrors `mcp_server/src/sts2_mcp/scene_guidance.py`).
- `STS2AIAgent/Agent/PlayPhases.cs` — the five status phases.
- `STS2AIAgent/Agent/PlayIntent.cs` — the explicit phrases that let a chat message play one move.
- `STS2AIAgent/Agent/ProactiveChatPolicy.cs` — opt-in teammate small talk (bounded count and interval).
- `STS2AIAgent/Agent/CompletionErrors.cs` — empty-completion and reasoning-budget diagnostics.
- `STS2AIAgent/Agent/AgentErrorEnvelope.cs` — in-process action failure → the same error envelope HTTP would send.
- `STS2AIAgent/Agent/ActIndexValidator.cs` — rejects a stale index before acting (409 plus valid indices; `OptionPaths`), and `IsUnsettled`.
- `STS2AIAgent/Agent/ActJsonParser.cs` — parses the no-tools JSON act fallback.
- `STS2AIAgent/Agent/AgentTools.cs` — tool schemas for `ReadOnly`, `Play` (last = `act`), `Chat`, vision, and the MCP subset.
- `STS2AIAgent/Agent/IGameBridge.cs` — the seam interface plus `AgentTurnResult`/`ChatTurn` records (test fakes implement it).
- `STS2AIAgent/Agent/GameBridge.cs` — the implementation: every call is a bounded, token-carrying `GameThread` post. `ActAsync` → `GameActionService.ExecuteAsync`, compact or raw state reads, `WaitUntilActionableAsync` (5 s per probe), game-data lookups, screenshots.
- `STS2AIAgent/Agent/IScreenshotCaptureHost.cs` — the game-side half of a screenshot: overlay visibility, hide/restore, one bounded wait for a frame the renderer actually drew, and the JPEG read-back. Implemented by `ScreenshotService`'s Godot host, faked by the offline tests.
- `STS2AIAgent/Agent/ScreenshotCapturePolicy.cs` — sequences a screenshot *after `GameBridge` acquires the shared lease off the game thread*: remember visibility → hide → wait for two drawn frames → read back → restore in a `finally`. `GameBridge` releases the lease afterward, including on a canceled/failed post; no stale frame is returned after a timeout.
- `STS2AIAgent/Agent/StateViews.cs` — run summary and state diff (mirrors `mcp_server/src/sts2_mcp/state_views.py`).
- `STS2AIAgent/Agent/GameDataFilter.cs` — scene-aware field projections for game data (mirrors `mcp_server/src/sts2_mcp/game_data.py`).
- `STS2AIAgent/Agent/GameDataExportSchema.cs` — field inventory of `/data/{collection}` (two-way test against the export code).
- `STS2AIAgent/Agent/DecisionContext.cs` — reads `client_context.decision_reason` from `POST /action`.
- `STS2AIAgent/Agent/DecisionLog.cs` — bounded, redacted accepted-decision log (`/decisions`).
- `STS2AIAgent/Agent/PlayerFacingSession.cs` — the player-facing session summary the overlay renders (headline, next step, waiting-for-player).
- `STS2AIAgent/Agent/DiagnosticExport.cs` — "export diagnostics" text and `Redact` (masks keys and bearer tokens).
- `STS2AIAgent/Agent/McpProcessLauncher.cs` — optional launch of the Python MCP sidecar.
- `STS2AIAgent/Agent/DualLaunchOutcome.cs` — typed result of a co-op launch attempt.

### Files — dual-layer (Jev) and per-run persistence
- `STS2AIAgent/Agent/IActionDecider.cs` — the execution-decider seam.
- `STS2AIAgent/Agent/JevExecutionDecider.cs` — asks the Jev fast model to pick one enumerated option, forwards a bounded planner goal plus strategy scope and aligns option hints to live criterion IDs; low confidence or failure falls back to the LLM.
- `STS2AIAgent/Agent/JevOptionEnumerator.cs` — turns the current `/state` into concrete Jev options (action + params), reads frame scope and re-keys type-level planner hints onto concrete option IDs. **Its index paths must match `ActIndexValidator.OptionPaths` and the handlers.** Reward cards expand into one option per card plus an explicit skip.
- `STS2AIAgent/Agent/ExecutionDecision.cs` — Jev decision record (choice, confidence, probabilities, danger score, latency).
- `STS2AIAgent/Agent/StrategyPlanner.cs` — background LLM strategy refresh (context switch, low-confidence streak, every N moves); requests a short macro goal and option-kind hints, and records the source screen/round. Budget-reserved, cancelable, version-guarded.
- `STS2AIAgent/Agent/StrategyStore.cs` — the current play strategy (immutable snapshots).
- `STS2AIAgent/Agent/PlayStrategy.cs` — backward-compatible strategy record: existing instructions plus bounded macro goal, source screen/round and kind-level hints. `PlayStrategyUpdate` is the one partial-update merge (omitted fields keep their values, empty update rejected) shared by `POST /strategy` and native MCP `update_play_strategy`; a new externally writable field goes there, not into either writer.
- `STS2AIAgent/Agent/PlannerBriefingProjection.cs` — the run-scoped briefing shared by HTTP and both MCP `get_planner_briefing` surfaces.
- `STS2AIAgent/Agent/PlaySessionStore.cs` — per-run session files under `sessions/`: atomic writes with a last-good backup, run-identity validation, legacy migration, safe delete. Normalize/redact every persisted strategy text field, including newly added goals and source screen, before writing.
- `STS2AIAgent/Agent/PlaySessionRecord.cs` — persisted shape (chat, decisions, strategy; redacted).
- `STS2AIAgent/Agent/PlaySessionMemory.cs` — bounded recent-decision memory for the active run.
- `STS2AIAgent/Llm/IJevClient.cs` / `STS2AIAgent/Llm/JevClient.cs` / `STS2AIAgent/Llm/JevTypes.cs` — TypeSafe Jev HTTP client (pooled connection, URL validation, a 429 retried at most once within the turn budget, each attempt billed separately, credentials redacted from errors).

### Flows
- **Turn:** `StartAutoPlay` → `AutoPlaySession.TryStart(AutoPlayLoopAsync)` → `AutoPlayRecovery.RunAsync` takes `_turnGate` → snapshot (bounded game-thread read) → `CurrentRunBoundary.Check` → session observe → `TryCompanionImmediateAsync` → `AgentLoop.PlayOnceAsync` → (dual-layer on and no pending instruction) `TryDecideWithJevAsync`, otherwise `PlayWithModelAsync` → `CompleteWithToolsAsync` → model calls `act` → `ExecuteActAsync` → `ActIndexValidator` against a fresh decision snapshot → `GameBridge.ActAsync` → `GameActionService.ExecuteAsync` → `AgentTurnResult` → commit (budget `Observe`, `ApplyPlayResult`, `RecordTurnReceipt`, decision log and session) → `Observe` (stop or backoff) → `afterTurn` (proactive chat, same gate). While the model request is in flight, `LlmRequest.OnReasoningDelta` → `AgentRuntime.ReportReasoningDelta` fills `LiveThoughtBuffer` (only when the player's switch is on), the overlay's 800 ms tick draws it, and the turn's own `finally` plus `AppendTurnTraces` clear it — so the streamed bubble yields to the recorded one and never outlives the turn.
- **Pause:** `StopAutoPlay` → `RequestPause` cancels the CTS → every dispatch boundary and game-thread post observes it → the interrupted receipt is committed → `Phase = paused`.
- **Chat:** `SendChatCoreAsync`. While auto-play runs, a message becomes a queued play instruction. Otherwise: budget check → `_turnGate` → `AgentLoop.ChatAsync` (read-only tools unless `PlayIntent` matches).
- **Stops:** budget → `budget`; left the run or a new run → `run_end`; 401/403 → `config`; 3 consecutive failures, repeats, unsettled streaks or a 150 s game-driven wait → `failed`, with a visible reason.
- **Jev turn** (dual-layer on for this mode, Jev configured, no queued play instruction): decision snapshot → `JevOptionEnumerator.Enumerate` (≤255 options; card plays first; ids like `play_card:0->1`, `choose_map_node:2`, `resolve_rewards:skip`) → `JevExecutionDecider` → `/v1/systemone` (a 429 is retried at most once within the turn deadline; each attempt is billed) → confidence below the threshold (default 0.35), an unknown id or an error → empty receipt → **LLM fallback** (`PlayWithModelAsync`). An executed act returns early; a rejected act also falls back (the cost is doubled work, not a stall).
- **Planner:** `ObserveStrategyContext` → `ShouldRefresh` (context key `runId|screen|act` changed, 3 low-confidence turns in a row, or every 10 turns) → background `Task.Run` (one in flight) → reserves one request under `_turnGate` → `StrategyStore.TryUpdate(expectedRevision)`. A plan has a macro goal, original instructions and option-*kind* hints. On each Jev request `AlignHints` maps hints to current concrete IDs, drops stale ones and marks cross-encounter scope. Pause, run change and settings change cancel or supersede a refresh.
- **Session across runs:** a fresh raw frame (`/state`, `/action`, `/strategy`, the turn snapshot) → `ObserveSessionStateSnapshot` → at a new run: flush the old run, `Load(newRunId)`, restore chat, `PlaySessionMemory` and strategy. Continue game re-adopts a retired run id. A late decision for a retired run is appended to *that* run's file and never to the live session. Files: `sessions/<runId>.json` (reserved or overlong seeds are hashed, prefixed `@`), `.tmp` → `File.Replace` → `.bak`, corrupt files are kept as `.corrupt-<stamp>`, `run_unknown` is never persisted, and every string is redacted.

### Traps
- **Jev option availability.** `JevOptionEnumerator` must drop items the executor rejects. `locked`
  is checked for everything, plus per-action flags in `AvailabilityFlags` (`claim_reward→claimable`,
  `choose_rest_option→enabled`, `choose_timeline_epoch→actionable`, `buy_*→affordable/stocked`),
  potions→`usable`/`discard`, cards→`playable`. `ActIndexValidator` accepts any in-list index, so a new
  indexed action without its flag lets Jev pick an option that 409s *after* the Jev request was paid for.
- Session persistence currently runs inside the game-thread read at a run boundary (one frame hitch
  per boundary, not per frame). If it grows, move the flush and load off the game thread.

## 5. Game state (`/state`, `agent_view`, `/data`)

### Files
- `STS2AIAgent/Game/GameStateService.cs` — base partial: `BuildStatePayload` (about 19 builders in a fixed order), `ResolveScreen`/`ResolveNonModalScreen`, `CurrentScreenName` (cheap), `EnumerateAvailableActions` (**the** action surface), `BuildAvailableActionNames(out descriptors)`, `BuildDecisionSnapshotPayload`, `GetLocalPlayer`, `FindDescendants<T>`, shared helpers.
- `STS2AIAgent/Game/GameStateService.Predicates.cs` — every `Can*` gate and action-level `Is*` helper.
- `STS2AIAgent/Game/GameStateService.Combat.cs` — the `CombatActionGate` (evaluated once per build; 200 ms stability sampler), hand cards (`playable`, `valid_target_indices`), target index helpers, intents.
- `STS2AIAgent/Game/GameStateService.CombatRisks.cs` — `end_turn_will_kill_player` and related flags from Doom, Poison, Constrict, Magic Bomb and Sandpit.
- `STS2AIAgent/Game/GameStateService.Rooms.cs` — event, rest, crystal sphere, game over, chest and capstone payloads.
- `STS2AIAgent/Game/GameStateService.Rewards.cs` — reward, card reward, bundle and deck-selection payloads, plus the `GetRewardButtons`/`GetProceedButton` helpers.
- `STS2AIAgent/Game/GameStateService.Shop.cs` — shop payload and the merchant entry helpers.
- `STS2AIAgent/Game/GameStateService.Potions.cs` — potion usability, targeting and `PotionRequiresTarget`.
- `STS2AIAgent/Game/GameStateService.Run.cs` — run, deck and potions payloads.
- `STS2AIAgent/Game/GameStateService.Map.cs` — map payload plus `GetAvailableMapNodes` and `IsMapNodeClickable` (the game's own travelability rule).
- `STS2AIAgent/Game/GameStateService.Menus.cs` — session phase, main menu, character select, lobby and timeline payloads.
- `STS2AIAgent/Game/GameStateService.AgentView.cs` — the compact `agent_view` rewrite (`AgentViewVersion`), one `BuildAgent<Screen>Payload` per screen. MCP `get_game_state` returns this view by default.
- `STS2AIAgent/Game/GameStateService.Payloads.cs` — every payload record. A new state field goes here first, then into `docs/api.md`.
- `STS2AIAgent/Game/StateBuildTiming.cs` — slow-build ring buffer (100 ms threshold), reported in `/health`.
- `STS2AIAgent/Game/GameDataExportService.cs` — `/data/{cards,relics,monsters,potions,events,powers,characters}`. Monster `damage_values`/`block_values` are always null (there are no static numbers; live damage is in `combat.enemies[].intents`), so projections omit them.
- `STS2AIAgent/Game/EventOptionLocalization.cs` — null-safe event option text formatting.
- `STS2AIAgent/Game/ProgressSaveVerification.cs` — compares the on-disk progress save for `game_over.save_verified`.

### Index spaces (payload list ⇄ handler; both sides call the same helper)
| Action | Index refers to | Shared helper |
|---|---|---|
| `play_card` `card_index` | `combat.hand[]` (all cards, including unplayable) | local player's `Hand.Cards` |
| `play_card`/potion `target_index` (enemy) | `combat.enemies[]` (includes dead) | `GetTargetableEnemyIndices` / `ResolveEnemyTarget` |
| `target_index` (player/ally) | `combat.players[]` (slot order) | `GetTargetablePlayerIndices` / `ResolvePlayerTarget` |
| `use_potion` / `discard_potion` | `run.potions[]` | local player's `PotionSlots` |
| `choose_map_node` | `map.available_nodes[]` | `GetAvailableMapNodes` |
| `claim_reward` / `resolve_rewards` | `reward.rewards[]` | `GetRewardButtons` |
| `choose_reward_card` / `skip_reward_cards` | `reward.card_options[]` / `reward.alternatives[]` | `GetCardRewardOptions` / `GetCardRewardAlternativeButtons` |
| `select_deck_card` | `selection.cards[]` | `GetDeckSelectionOptions` |
| `choose_rest_option` (+MEND target) | `rest.options[]` (+ `run.players[]`) | `GetRestOptionTargetIndices` |
| `choose_event_option` | `event.options[]` (locked options included; a finished event has PROCEED at 0) | `eventModel.CurrentOptions` |
| `buy_card` / `buy_relic` / `buy_potion` | `shop.cards[]` / `relics[]` / `potions[]` | `GetMerchant*Entries` |
| `choose_capstone_option` | `capstone.options[]` | `GetCapstoneButtons` |

### Traps
- **Stale Godot caches.** `NMapPoint.IsEnabled` is refreshed only on point state changes, never when
  travel is toggled. Gate on the live screen flags (`IsMapNodeClickable`). Suspect the same pattern
  for any other `IsEnabled`/`Visible` cache.
- **State vs predicate drift.** A field a client acts on should come from the action's own
  predicate. Known exception: `reward.can_proceed` reports the reward screen's button, while
  `proceed` is never offered there; use `collect_rewards_and_proceed`.
- **Re-derived state drifts** between the state build and execution (for example, alive-player
  count for `AnyPlayer` potions). The executor must re-validate, and fail visibly rather than
  retarget.
- Verify game types in `extraction/decompiled/` (grep) before trusting them.

## 6. Game actions (`POST /action`)

### Files
- `STS2AIAgent/Game/GameActionService.cs` — **the dispatch table** (`ExecuteAsync` switch: the only thing kept in the base file), `ActionExecutionGate` lease, shared wait helpers.
- `STS2AIAgent/Game/GameActionService.Combat.cs` — `play_card`, `end_turn`, `use_potion`/`discard_potion` (+ `ResolvePotionTarget`), in-combat card selection, `TryCancelRunningPlayerAction`.
- `STS2AIAgent/Game/GameActionService.Rewards.cs` — `claim_reward`, `resolve_rewards` / `collect_rewards_and_proceed` (stops at a card choice unless given an explicit `option_index`), `choose_reward_card`, `skip_reward_cards`, bundles, deck selection.
- `STS2AIAgent/Game/GameActionService.Rooms.cs` — events, rest site, chest and treasure relics, crystal sphere, capstone screens, `proceed`.
- `STS2AIAgent/Game/GameActionService.Shop.cs` — open/close the shop, buy card/relic/potion, card removal.
- `STS2AIAgent/Game/GameActionService.Run.cs` — `choose_map_node` (single-player `ForceClick`; co-op `OnMapPointSelectedLocally` plus vote wait), deck view, game over / `continue_game_over`.
- `STS2AIAgent/Game/GameActionService.Menus.cs` — main menu, timeline epochs, FTUE and modal confirm/dismiss, settings and abandon flows.
- `STS2AIAgent/Game/GameActionService.Embark.cs` — new run: character select, ascension, embark, continue run.
- `STS2AIAgent/Game/GameActionService.Coop.cs` — multiplayer lobby host/join/load, local four-player host for the companion, ready and start.
- `STS2AIAgent/Game/ActionExecutionGate.cs` — process-wide single-action lease (`action_in_flight` 409 on overlap; released on throw or cancel).
- `STS2AIAgent/Game/GameThread.cs` — the game thread's SynchronizationContext: `InvokeAsync` overloads (see §2), `WaitForNextFrameAsync` (bounded at 50 ms when frames stop).
- `STS2AIAgent/Game/GameTaskWaitPolicy.cs` — bounded waiting on game `Task`s (`GameTaskBounding`).
- `STS2AIAgent/Game/BackgroundTaskOutcome.cs` — outcome of an observed background game task.
- `STS2AIAgent/Game/MenuTransitionPolicy.cs` — main-menu transition settle rules.
- `STS2AIAgent/Game/RewardChoicePolicy.cs` — which reward is auto-claimable versus a real decision (cards are never auto-picked).
- `STS2AIAgent/Game/RewardSkipScope.cs` — scope of a reward skip.
- `STS2AIAgent/Game/CardPlayCounterPolicy.cs` — confirms that a card play actually happened (play counters).
- `STS2AIAgent/Game/CombatTurnReadinessPolicy.cs` — when the player's combat turn is ready for input.
- `STS2AIAgent/Game/CrystalSphereSettlePolicy.cs` — crystal sphere reveal settle rule.
- `STS2AIAgent/Game/FtueModalPolicy.cs` — first-time-user modals that block actions and how to clear them.
- `STS2AIAgent/Game/UnlockConfirmResolutionPolicy.cs` — unlock-confirm screen resolution.
- `STS2AIAgent/Game/ReflectedGameMembers.cs` — every private game member the mod reads by reflection, probed and logged at startup.
- `STS2AIAgent/Game/ReflectedMemberResolver.cs` / `STS2AIAgent/Game/ReflectionMemberAccessor.cs` — reflection lookup and cached accessors.

### Partial → actions
| Partial | Actions |
|---|---|
| Combat | `end_turn`, `play_card`, `use_potion`, `discard_potion` |
| Rewards | `resolve_rewards`, `collect_rewards_and_proceed`, `claim_reward`, `choose_reward_card`, `skip_reward_cards`, `select_deck_card`, `close_cards_view`, `confirm_selection` |
| Rooms | `proceed`, `open_chest`, `choose_treasure_relic`, `choose_event_option`, `crystal_set_tool`, `crystal_clear_cell`, `choose_capstone_option`, `choose_bundle`, `confirm_bundle`, `choose_rest_option` |
| Shop | `open_shop_inventory`, `close_shop_inventory`, `buy_card`, `buy_relic`, `buy_potion`, `remove_card_at_shop` |
| Run | `continue_run`, `abandon_run`, `save_and_quit`, `choose_map_node`, `return_to_main_menu`, `dismiss_game_over_wait`, `continue_game_over` |
| Menus | `switch_profile`, `confirm_modal`, `dismiss_modal`, `close_main_menu_submenu`, `run_console_command`*, `inject_event_churn`* (*debug-gated) |
| Embark | `open_character_select`, `open_timeline`, `confirm_unlock`, `choose_timeline_epoch`, `confirm_timeline_overlay`, `select_character`, `embark`, `unready`, `increase_ascension`, `decrease_ascension` |
| Coop | `host_multiplayer_lobby`, `join_multiplayer_lobby`, `ready_multiplayer_lobby`, `disconnect_multiplayer_lobby`, `invite_ai_teammate`, `continue_ai_teammate` |

### Wait budgets (bounded; tighter than the game's own AutoSlay 10 s/30 s; don't widen casually)
Reward drain 20 s for the whole flow · single reward/card/deck step 10 s · `play_card` 12 s · potion 10 s ·
end-turn transition 5 s · map/proceed/chest/relic/event/rest/crystal/capstone/bundle 10 s · shop 10 s ·
return to main menu 15 s · game-over exit 30 s, summary ready 60 s · modal 2 s + 8 s FTUE-stuck retry ·
embark/character select 5–10 s (epoch, tutorial and profile switch 15 s).

### Reward flow (`DrainRewardFlowAsync`)
Loop until the deadline: advance an open modal → on `NCardRewardSelectionScreen` → `TryResolveCardRewardAsync`
(settle ≥24 frames; no explicit choice → **stop on purpose** with `reward.pending_card_choice = true` and a
"this is your decision" message; `-1` → click the first *enabled* alternative; index → press that card) → left the
rewards screen → done → claim the next claimable button (skips a potion with a full belt, and a skipped card set) →
proceed → empty-screen escape ladder.

### Handler shape (every `Execute<X>Async`)
Resolve the screen → `Can<X>` (409 `invalid_action`) → validate parameters (400 `invalid_request` /
409 `invalid_target`) → act on the game thread → **bounded** transition wait → `ActionResponsePayload`
`{status: completed|pending, stable, message, state}`. `pending` means the game accepted the action
but hasn't settled; the agent treats a run of these as a stall (`NoProgressPolicy.UnsettledLimit`).

### Traps (game facts worth knowing)
- `ForceClick()` = `OnRelease(); EmitSignal(Released)`. It bypasses the button's own guards, so never
  click a node whose usability you haven't proven from **live** state. A click is not evidence of an
  effect: `NMapPoint.OnRelease` ignores clicks while traveling, `NRewardButton` re-enables itself when a
  potion belt is full, and `NEndTurnButton.CallReleaseLogic` no-ops when the turn can't end. Every
  handler must verify its own transition.
- In co-op, choices go through the game's synchronizers (`ActionQueueSynchronizer.RequestEnqueue`,
  `*Synchronizer.ChooseLocalOption`, `OnMapPointSelectedLocally`), not UI clicks.
- Predicate and executor must filter the same way (for example, the card-reward alternatives
  `[Skip, Reroll]` are sorted by position; filter `IsEnabled` on both sides).
- `NEventRoom.Proceed()` dereferences `NMapScreen.Instance` synchronously, so guard it (503 retryable).
- FTUE modals: `NFtue.CloseFtue` is protected, so the escape is `NModalContainer.Clear()` via
  `GameStateService.TryCloseOpenFtue`. `NCombatRulesFtue` has several pages.
- `ActiveScreenContext.GetCurrentScreen()` precedence decides what every `Can*` sees: feedback → modal →
  inspect → logo → main menu/submenu → capstone → map → overlay stack → rooms.
- `Player.PotionSlots` is fixed-length with null entries; `NMapScreen` holds exactly one `NMapPoint` per coord.
- Private game members are only accessed through `ReflectedGameMembers` (lookups never throw; a miss
  shows as `degraded` in `/health`).

## 7. HTTP server, native MCP, LLM client, co-op

### Files
- `STS2AIAgent/Server/HttpServer.cs` — the loopback `HttpListener` host. The port increments when busy unless `STS2_API_PORT` pins it (`STS2_API_ALLOW_FALLBACK`).
- `STS2AIAgent/Server/LoopbackListener.cs` — listener binding and port-fallback rules.
- `STS2AIAgent/Server/Router.cs` — every route (`/health`, `/state`, `/actions/available`, `/decision-snapshot`, `/action`, `/data/*`, `/decisions`, `/strategy`, `/planner-briefing`, `/session/control`, `/teammate/control`, `/companion/*`, `/screenshot`, `/mcp`), the error envelope, and `ModVersion` (one of the five version files).
- `STS2AIAgent/Server/ApiException.cs` — typed API error (status, code, details, retryable).
- `STS2AIAgent/Server/JsonHelper.cs` — shared JSON options and writing.
- `STS2AIAgent/Server/HealthRoleData.cs` — role, play and companion fields reported by `/health`.
- `STS2AIAgent/Server/CompanionPortFile.cs` — the companion writes its port so the host can find it.
- `STS2AIAgent/Server/NativeMcpServer.cs` — Streamable-HTTP MCP endpoint (JSON-RPC, sessions, off by default).
- `STS2AIAgent/Server/NativeMcpServer.Tools.cs` — `ExecuteToolAsync`: native tool implementations, kept in parity with Python `server.py`.
- `STS2AIAgent/Server/GameEventService.cs` — SSE event production (polls state on the game thread, diffs, emits typed events).
- `STS2AIAgent/Server/EventPollingCoordinator.cs` — polling cadence and fallback.
- `STS2AIAgent/Server/EventStreamSubscribers.cs` / `STS2AIAgent/Server/GameEventSubscriberHub.cs` — SSE subscriber registry and fan-out.
- `STS2AIAgent/Server/EventChurnPolicy.cs` / `STS2AIAgent/Server/ConsecutiveRepeatSuppressor.cs` — suppress event churn and repeated events.
- `STS2AIAgent/Llm/OpenAiCompatibleClient.cs` — chat completions with tools, reasoning content, ping, and the tool-calling probe (`ProbeToolCallingAsync`). A successful streamed body is read incrementally: the SSE/JSON decision is made on the first non-blank line, complete lines go to `SseCompletionAccumulator`, and each reasoning delta is reported through `LlmRequest.OnReasoningDelta` while the reply is still arriving.
- `STS2AIAgent/Llm/SseCompletionAccumulator.cs` — the SSE state of one completion, fed one line at a time: delta/content/reasoning accumulation, whole-`message` tolerance, synthetic tool-call ids. `ParseSsePayload` is this accumulator fed in one pass, so a streamed read and a buffered read cannot disagree.
- `STS2AIAgent/Llm/LlmTypes.cs` — request and response DTOs, `ResolvedModel`, `LlmException`.
- `STS2AIAgent/Llm/MaxTokensField.cs` — `max_tokens` vs `max_completion_tokens` per provider.
- `STS2AIAgent/Llm/ThinkingRequestBuilder.cs` — provider-specific thinking/reasoning request fields.
- `STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs` — launches the second game process as the companion (isolated profile, env vars, single-instance guard).
- `STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs` — host side of local co-op: host the lobby, wait for the companion, start or continue the run.
- `STS2AIAgent/Multiplayer/CoopLaunchPolicy.cs` — whether an invite may launch, and whether the companion auto-plays (requires a verified play role).
- `STS2AIAgent/Multiplayer/CoopSavePrecheckPolicy.cs` / `STS2AIAgent/Multiplayer/CoopSaveProbe.cs` — whether a saved co-op run can be continued.
- `STS2AIAgent/Multiplayer/CompanionConnection.cs` — host → companion HTTP (token header `X-STS2-Companion-Session`).
- `STS2AIAgent/Multiplayer/CompanionHealth.cs` — companion liveness.
- `STS2AIAgent/Multiplayer/CompanionProfileBootstrap.cs` — seeds the companion's isolated settings and profile.
- `STS2AIAgent/Multiplayer/CompanionSettingsPatch.cs` — the Jev settings patch pushed to a running companion.
- `STS2AIAgent/Multiplayer/CompanionJevSnapshot.cs` — the companion's non-secret Jev readings shown on the host.
- `STS2AIAgent/Multiplayer/CompanionPlayPolicy.cs` — the companion's immediate decisions (follow the host's map vote, confirm FTUE modals, or wait).
- `STS2AIAgent/Multiplayer/CompanionActPolicy.cs` — which actions the companion may take by itself.
- `STS2AIAgent/Multiplayer/TeamConversation.cs` / `STS2AIAgent/Multiplayer/TeamIntent.cs` — host ↔ teammate chat and intent parsing.
- `STS2AIAgent/Multiplayer/TeammateStatus.cs` — the teammate's live status shown on the host.
- `STS2AIAgent/Multiplayer/CompanionHostWatch.cs` — the companion's host watchdog. The launcher passes `STS2_AGENT_HOST_PID`, and the companion (`AgentRuntime.WatchHostAsync`) quits once the host exits, crashes or is force-killed, so an invisible AI teammate never keeps playing and spending. The host also sends `CloseMainWindow` on a normal shutdown (`LocalDualInstanceLauncher.TryCloseCompanion`).

### Flows
- **HTTP:** `HttpServer` listen loop → `Router.HandleAsync` (every route in one try/catch/finally, so a bad request never kills the listener) → `GameThread.InvokeAsync(...)` → envelope `{ok, request_id, data | error{code,message,retryable,details}}`. `/action` binds the decision to the pre-action `run_id`, and session observation happens on that same game-thread read.
- **SSE:** `GameEventService` polls state on the game thread, diffs it, and publishes typed events. Each `/events/stream` subscription is disposed via `using` when the client disconnects.
- **LLM request:** `OpenAiCompatibleClient.CompleteAsync` → stream by default, and a streamed body is read
  as it arrives: `LooksLikeSse` decides per first non-blank line (so gateway keep-alive comments are fine,
  and a provider that ignores `stream: true` is buffered and parsed as JSON), complete lines feed
  `SseCompletionAccumulator`, and every reasoning delta is handed to `LlmRequest.OnReasoningDelta` before
  the reply finishes. A 400/415/422 while streaming → one non-stream retry (deliberate trade-off). Tool
  calls tolerate numeric ids, object `arguments` and missing ids (an id is synthesized, never dropped).
  The assistant tool-call turn echoes the provider's `reasoning_content` (DeepSeek and Kimi thinking modes
  return 400 without it; nothing is sent to providers that never returned it).
- **Co-op invite:** overlay `LaunchDualAsync` → `CoopLaunchPolicy` (verified play role → the companion auto-plays) → `LocalDualInstanceLauncher.LaunchCompanionAsync` (isolated settings, env: role, port, token, host PID, autoplay) → wait for `/health` from the expected PID (90 s) → `DualInstanceCoordinator` hosts the lobby, the companion joins, run start or continue. The host talks to the companion over HTTP with the `X-STS2-Companion-Session` token.

### Provider quirks the client handles
| Provider | Handling |
|---|---|
| DeepSeek / Kimi (thinking) | `reasoning_content` is streamed to the thought view as it arrives (`LlmRequest.OnReasoningDelta`) and echoed on tool-call turns |
| OpenRouter / gateways | leading SSE comments (`: OPENROUTER PROCESSING`) accepted |
| Ollama / LM Studio / local | empty API key allowed; object `arguments` and missing tool-call ids tolerated |
| Providers rejecting `max_tokens` / thinking fields | `MaxTokensField` and `ThinkingRequestBuilder` choose per provider |
| Any 4xx during streaming | one non-stream retry, then the error surfaces with its HTTP status |

## 8. Python MCP sidecar (`mcp_server/`)

- `mcp_server/src/sts2_mcp/client.py` — sync `urllib` transport. Reads retry (`max_retries=2`); `POST /action` is **never** replayed (`outcome_unknown` plus one reconciliation). Timeouts: read 10 s, action 75 s (the mod's longest wait is 60 s). SSE `iter_events`/`wait_for_event`. Non-action read transport errors keep their raw exception types (pinned by `test_action_replay_safety.py`). Line budget 700.
- `mcp_server/src/sts2_mcp/envelope.py` — `Sts2ApiError`; `parse` is lenient (reads), `parse_strict` is strict (`POST /action`).
- `mcp_server/src/sts2_mcp/client_actions.py` — one wrapper per mod action → `execute_action(name, **fields)`.
- `mcp_server/src/sts2_mcp/server.py` — FastMCP registration and profiles (`guided` default, `layered`, `full`; debug tools only with `STS2_ENABLE_DEBUG_ACTIONS=1`), `act`, `wait_until_actionable`.
- `mcp_server/src/sts2_mcp/action_results.py` — post-action result shaping (`compact_action_result`, index details, reconciliation semantics).
- `mcp_server/src/sts2_mcp/payloads.py` — typed parsing of `/actions/available` and `/decisions`.
- `mcp_server/src/sts2_mcp/decision.py` — the `decide` tool via `/decision-snapshot` (404 fallback to `/state` + `/actions/available`).
- `mcp_server/src/sts2_mcp/state_views.py` — `run_summary`, `diff_state` (mirrors `StateViews.cs`).
- `mcp_server/src/sts2_mcp/game_data.py` — `/data` cache, scene detection, field projection (mirrors `GameDataFilter.cs`).
- `mcp_server/src/sts2_mcp/scene_guidance.py` — strategy and playbook slices (mirrors `PlaybookSections.cs`).
- `mcp_server/src/sts2_mcp/legacy_tools.py` — per-action tools for the `full` profile.
- `mcp_server/src/sts2_mcp/handoff.py` — planner, combat and event handoff packets (`layered`+).
- `mcp_server/src/sts2_mcp/knowledge.py` — on-disk knowledge base (`agent_knowledge/`, `STS2_AGENT_KNOWLEDGE_DIR`).
- `mcp_server/src/sts2_mcp/network_server.py` — HTTP transport (uvicorn); defaults are env-backed (`STS2_API_BASE_URL`).
- `mcp_server/src/sts2_mcp/__main__.py` — `python -m sts2_mcp` → stdio server.
- `mcp_server/src/sts2_mcp/__init__.py` — package marker.

Tools are **synchronous**; don't make them async. Wire keys stay spelled the way the mod spells them.

## 9. Scripts, tests, gates

- **Build:** `scripts/build-mod.ps1` / `build-mod.sh` (dotnet build → headless Godot PCK with `mod_manifest.json` inside → copies dll/pck/`mod_id.json` to `<game>/mods`). **Close the game first** (the DLL is locked while it runs).
- **Paths:** only `scripts/lib-sts2-paths.ps1` / `.sh` know the install layout (arg → env → detect → default). `lib-sts2.sh` holds the runtime helpers.
- **Run:** `start-game-session.ps1`/`.sh` (pins `STS2_API_PORT`, fails if the old listener doesn't release the port, `--clientId` for an isolated profile), `start-mcp-stdio.*`, `start-mcp-network.*` / `serve-sts2-network-mcp.ps1` (all honor `STS2_API_BASE_URL`).
- **Release:** `package-release.ps1` (+ `build-fingerprint.json`), `check_release_package.py`, `check_release_metadata.py` (five version files), `preflight-release.ps1` (every static step), Steam Workshop scripts.
- **Gates:** `python scripts/check_verification_gates.py` (api-facts, doc marks, docs tracked, links, script encoding — `.ps1` with non-ASCII needs a UTF-8 BOM, ps1/sh syntax, architecture facts, api schema, lockfile).
- **Live validation** (game running): `python scripts/run_sts2_validation.py <mod-load|state-summary|state-invariants|main-menu-active-run|…>` and `scripts/test-*.ps1|.sh`. These never count as offline evidence, and offline tests never count as live acceptance.

| Command | Covers |
|---|---|
| `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release` | mod compiles (0 warnings expected) |
| `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` | ~800 offline tests: pure policies (compiled in), source contracts (Roslyn/text) for game-dependent files, this map's freshness. Tests are registered by hand in `STS2AIAgent.Tests/TestRunner.cs` |
| `cd mcp_server; uv run --locked python -m unittest discover -s tests` | 420 Python tests (stdlib unittest only, no pytest) |
| `python scripts/check_verification_gates.py` | static doc/code gates |
| `powershell -File scripts/preflight-release.ps1` | everything above plus packaging checks |

## 10. Where to change X

| Task | Start at | Also touch |
|---|---|---|
| New game action | `GameStateService.cs` `EnumerateAvailableActions` + `Can<X>` in `.Predicates.cs` | `Execute<X>Async` in `GameActionService.<Room>.cs`, dispatch arm in `GameActionService.cs`, payload + `docs/api.md`, agent view, native MCP + Python `client_actions.py`/`legacy_tools.py`, `ActIndexValidator.OptionPaths` + `JevOptionEnumerator` if indexed |
| New state field | `GameStateService.Payloads.cs` → `GameStateService.<Screen>.cs` | `GameStateService.AgentView.cs`, `docs/api.md` |
| Auto-play retry/stop behavior | `AutoPlayRecovery.cs`, `NoProgressPolicy.cs` | `AutoPlayRecoveryTests.cs` |
| Pause/start/mode switch | `AutoPlaySession.cs`, `AgentRuntime.PlayControl.cs`, `AgentRuntime.ModeControl.cs` | `Ui/AgentOverlayHost.PlayControl.cs` |
| Prompt / playbook | `PlayPrompt.cs`, `PlaybookSections.cs`, `skills/sts2-mcp-player/` (embedded resources) | `scene_guidance.py` |
| LLM provider compatibility | `Llm/OpenAiCompatibleClient.cs`, `MaxTokensField.cs`, `ThinkingRequestBuilder.cs` | `docs/model-compatibility-matrix.md` |
| Jev / planner | `JevExecutionDecider.cs`, `JevOptionEnumerator.cs`, `StrategyPlanner.cs`, `AgentRuntime.Jev.cs` | `Llm/JevClient.cs` |
| Session persistence | `AgentRuntime.Session.cs`, `PlaySessionStore.cs` | `PlaySessionRecord.cs` |
| New setting | `Config/AgentSettings.cs` | `SettingsClone.cs`, `HarvestSettings`, `EnsureValidShape` |
| Overlay control/page | `Ui/AgentOverlayHost.Pages.cs` / `.Settings.cs` | `Loc.Strings.Ui.cs`, layout contract tests |
| Co-op | `Multiplayer/DualInstanceCoordinator.cs`, `LocalDualInstanceLauncher.cs`, `CoopLaunchPolicy.cs` | `GameActionService.Coop.cs`, `AgentRuntime.cs` dual-launch core |
| HTTP route | `Server/Router.cs` | `docs/api.md`, `docs/openapi.json`, `mcp_server/src/sts2_mcp/client.py` |
| Version bump | the five files in AGENTS.md | `CHANGELOG.md` (`## Unreleased` → version) |

## 11. Known open items (not bugs to rediscover)

- Live acceptance still pending for: dual-window co-op with a real Jev service, and the mode-switch
  and companion Jev sync (see `CHANGELOG.md` Unreleased and `docs/live-validation-checklist.md`).
- `reward.can_proceed` describes the reward screen's button, not the `proceed` action (see §5).
- The per-model Test and the footer "test connection" both exist. The card test is the one the
  first-run guide names, and it now settles role records too.
- Known and deliberately not changed in the 2026-09-23 sweep (low frequency or design trade-offs):
  session file I/O runs inside the game-thread read at run boundaries (a one-frame hitch per boundary);
  `TryAdvanceRewardModalAsync` keeps waiting on a modal it can't advance until the 20 s drain budget
  ends (this also covers modals still animating in); a 400/415/422 on a streamed LLM request is retried
  once without streaming (one extra request on a permanent misconfiguration); non-action MCP reads keep
  raw transport exception types (pinned by `test_action_replay_safety.py`); `start-game-session.ps1
  -ExtraArguments` splits on whitespace (no quoted arguments).
