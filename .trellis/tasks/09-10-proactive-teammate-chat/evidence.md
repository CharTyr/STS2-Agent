# Evidence: proactive teammate chat and selectable tone

Runner: main session. Date: 2026-09-10. All verification offline; the game was never launched.

## Change

| Layer | File | Change |
| --- | --- | --- |
| Policy | `STS2AIAgent/Agent/ProactiveChatPolicy.cs` (new) | `ProactiveChatMoment`, `ProactiveChatInput/Decision`, `Observe`, `Decide`, `BuildPrompt`; `ProactiveChatTones` tone table, `Normalize`, `BuildSystemInstruction`. No Godot or game types. |
| Settings | `STS2AIAgent/Config/AgentSettings.cs` | `ProactiveChatEnabled` (default false) and `ProactiveChatTone`; tone normalised in `EnsureValidShape`. |
| Chat contract | `STS2AIAgent/Agent/IGameBridge.cs` | `ChatOptions.ReadOnly` and `ChatOptions.ExtraSystemInstruction`. |
| Chat execution | `STS2AIAgent/Agent/AgentLoop.cs` | Appends the extra system instruction; `ReadOnly` forces `allowAct = false`. |
| Runtime | `STS2AIAgent/Agent/AgentRuntime.cs` | Situation key tracking, `TryProactiveChatAsync` called under the existing turn gate after each play turn, budget accounting with `recordBudget: true`, diagnostic-only failure handling, and volume bookkeeping delegated to `ProactiveChatSession` (reset by `TryResetSessionStats`). |
| UI | `STS2AIAgent/Ui/AgentOverlayHost.cs` | Opt-in checkbox and tone dropdown in advanced settings, harvest + clone, persisted through the existing save path. |
| Tests | `STS2AIAgent.Tests/ProactiveChatPolicyTests.cs` (new), `AgentLoopTests.cs`, `TestRunner.cs`, `STS2AIAgent.Tests.csproj` | 15 policy tests, 5 session tests, 2 chat-contract tests. |
| Docs | `docs/proactive-chat-review.md`, `PRODUCT_PLAN_CURRENT.md`, `README.md`, `README.zh-CN.md` | Capability recorded as implemented behind an opt-in and explicitly not live-validated. |

## Behaviour

- Triggers: combat start and combat end only, derived from the situation key the auto-play loop
  already reads.
- Volume: at most 6 messages per session, at least 75 s apart.
- Gate order in `Decide`: opt-in off -> no moment -> not playing -> budget blocked -> session cap ->
  minimum interval. Each refusal returns a stable reason string.
- Read-only: a proactive turn cannot dispatch a game action even when the model answers with play
  wording, because `ReadOnly` is folded into the `allowAct` expression rather than relying on the
  prompt.
- Failure isolation: any exception or model error is recorded as a diagnostic event and the
  auto-play turn still returns its real result.

## Evidence

| Command | Result |
| --- | --- |
| `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` | exit 0; 22 new cases PASS. Policy: DefaultsOff, UnknownToneFallsBack, ShapeRepair, TonesDistinct, SituationKey, ObserveBoundaries, RefusesDisabled, RefusesWithoutMoment, RefusesWhilePaused, RefusesOnBudget, RefusesAtSessionCap, RefusesInsideInterval, SendsWhenAllGatesPass, PromptPerMoment, VolumeStaysSmall. Session: SessionInterval, SessionCap, RefusalsKeepTheCap, ResetClearsBounds, IntervalBoundaryInclusive. Chat contract: ReadOnlyCannotAct, ToneReachesSystemPrompt. Previously passing cases still pass |
| `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release` | exit 0, 0 warnings, 0 errors (this is the only check that compiles the UI wiring) |
| `powershell -File scripts/preflight-release.ps1` | exit 0; includes the C# harness and the MCP suite |

## Boundary

The runtime's volume bound is proven by `ProactiveChatSessionTests`, which drives the same type `AgentRuntime` calls: the loop stops at the session cap, refuses inside the minimum interval, and a pause/budget/opt-in refusal leaves the counter and the interval stamp untouched. Deterministic tests prove the policy, the settings plumbing, the read-only guarantee and the prompt
injection. They do not prove live behaviour: whether a real model produces a useful sentence at the
right moment, whether 75 s feels right, and whether the trigger fires correctly across real screen
transitions all remain unverified until someone plays a live game. No document claims otherwise.
