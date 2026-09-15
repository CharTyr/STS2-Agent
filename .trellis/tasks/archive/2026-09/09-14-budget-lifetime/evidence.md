# Goal 1 evidence — active-session budget identity

Date: 2026-09-15

## Change

Keep one `SessionBudgetGuard` identity for the session. Saving or reloading settings updates limits in place and preserves `ConsumedTokens` / `RequestCount`. Explicit `TryResetSessionStats` still creates a fresh zeroed guard (already gated while play is running).

## Files changed

- `STS2AIAgent/Agent/SessionBudgetGuard.cs` — `UpdateLimits` using the constructor `>0 => value else null` rule; Record / CheckBudget / Observe / UpdateLimits share one lock; Observe `RequestsSpent=0` fallback unchanged.
- `STS2AIAgent/Agent/AgentRuntime.cs` — `SaveSettings` / `ReloadSettings` call `UpdateLimits` on the existing guard; constructor and `TryResetSessionStats` still `CreateBudgetGuard`.
- `STS2AIAgent.Tests/SessionBudgetGuardTests.cs` — lowering, raising, clearing, lock-safe concurrent update+record, and Save/Reload source-contract tests.
- `STS2AIAgent.Tests/TestRunner.cs` — registered as `Budget.UpdateLimits*` and `Budget.RuntimeSaveReloadKeepsGuard`.

## Commands

```
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
```

Re-run after fixing the raising-limit assertion (Observe at 4/4 was a new-cap stop, not a stale-limit stop):

```
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj --no-restore
```

## Results

- First full run: `Budget.UpdateLimitsRaisingContinues` failed because Observe landed exactly on the new cap (`>= MaxRequests`). Other new budget tests passed.
- After raising the test cap to 5 so Record/Observe stay under the new limit: PASS=391 FAIL=0 EXIT=0.
- New tests: `Budget.UpdateLimitsLoweringStops`, `Budget.UpdateLimitsRaisingContinues`, `Budget.UpdateLimitsClearingRemovesAxis`, `Budget.UpdateLimitsLockSafe`, `Budget.RuntimeSaveReloadKeepsGuard`.

## Limits

- Deterministic C# harness only. No live game, no provider calls, no player-save mutation.
- `AgentRuntime` is not constructed in tests (Godot/game dependencies). Wiring is a source contract: Save/Reload call `UpdateLimits` and do not assign `CreateBudgetGuard`; reset still does.
- `AutoPlayLoopAsync` still captures the guard once; identity is now stable, so that capture stays coherent with AgentLoop's provider.
- `.trellis/spec/mod/agent-and-ui.md` still says SaveSettings "swaps the in-memory settings and budget guard". Spec text was left unchanged in this goal.

## Not done (other goals)

- Observe `RequestsSpent=0` fallback (goal 2).
- No commit, push, or archive.
