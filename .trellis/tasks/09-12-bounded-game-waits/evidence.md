# Evidence: bounded game waits (2026-09-12)

## Implementation

New: `STS2AIAgent/Game/GameTaskWaitPolicy.cs` — `GameTaskWaitOutcome`
(`Completed` / `TimedOut` / `Failed`), `Classify(taskCompleted, taskFaulted, deadlineReached)`
and `DescribeTimeout(actionName, timeout)`. The "still running and the deadline has
not passed" combination throws `ArgumentOutOfRangeException` so a future caller
cannot silently misclassify a wait that has not finished.

`GameActionService.cs` gained a generic and a non-generic
`WaitForGameTaskAsync(Task, TimeSpan)` plus `ClassifyGameTaskWait`,
`DescribeGameTaskFailure`, `BuildGameTaskTimeoutResponse`, and
`ObserveBackgroundTask`; `WaitForTaskResultAsync` is now a wrapper over the
bounded generic overload, so the rest-site target path keeps its old behavior.

## Call sites converted

| Action | Native task | Timeout | On timeout | On fault |
| --- | --- | ---: | --- | --- |
| `save_and_quit` | `CloseToMenu` | 20 s | observe + existing main-menu wait | 409 |
| `crystal_clear_cell` | `CellClicked` | 10 s | observe + existing settle wait | 409 |
| `choose_event_option` | `NEventRoom.Proceed` | 10 s | observe + existing transition wait | 409 |
| `choose_rest_option` | `ChooseLocalOption` | 10 s | observe + existing transition wait | 409 |
| `buy_card` / `buy_relic` / `buy_potion` | `OnTryPurchaseWrapper` | 10 s | observe + `pending` | 409 |
| `host_multiplayer_lobby` | `StartHost` | 10 s | observe + `pending` | 409 |
| `join_multiplayer_lobby` | `JoinToHost` | 10 s | observe + `pending` | 409 |
| `run_console_command` | console task | 10 s | observe + existing stability wait | 409 |
| `invite_ai_teammate` | FastHost result | 10 s | observe + stop probing modes | returns the existing failure result |

## Review round

The reviewer fixed two issues: `DescribeGameTaskFailure` was reusing the
purchase-specific wording from `BackgroundTaskOutcome` (so a rest option or a
lobby join would report "the purchase task faulted"), and the new contract test
matched fixed variable names, which a rename could slip past. The test now scans
for the `await <expr>;` shape line by line and was proven with a mutation probe
(an injected `_ = await joinTask;` produced `FAIL GameTaskBounding.AllSites`
and was then reverted).

## Race fix made during the final sweep

`Task.WhenAny(task, Task.Delay(timeout))` can let the delay win even when the game
task has already finished. That made the classification read "finished" while the
handed-back task was `null`, so the five sites that await the completed task would
have thrown a null dereference and surfaced as a 500 after a successful purchase.
`WaitForGameTaskAsync` now returns the task whenever `task.IsCompleted` is true,
in both overloads; the contract test pins that shape.

## Commands actually run

```text
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release  -> 265 PASS / 0 FAIL, exit 0
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental    -> 0 warnings / 0 errors
python scripts/check_verification_gates.py                                 -> passed
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1     -> exit 0
```

## Known follow-ups

- `invite_ai_teammate` reports its pre-existing `TimeoutException` path rather than
  a `pending` action response; the design chose that deliberately because the
  caller (`DualInstanceCoordinator`) falls back to the debug lobby.
- `run_console_command` can still report `completed` when the command task timed
  out but the follow-up screen-stability wait says stable. It is the debug-gated
  path and the design kept that behavior; noted for a future tightening.

## Not verified

No live game session. Real-world latency of these native tasks was not measured.
