# Design: bounded game waits

## Existing helper

`GameActionService.cs:3432-3441` already has:

```csharp
private static async Task<bool?> WaitForTaskResultAsync(Task<bool> task, TimeSpan timeout)
{
    var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
    if (completedTask != task) { return null; }   // timeout
    return await task;
}
```

Used by the rest-site target path, which already treats `null` as "timeout ⇒
observe in background ⇒ return pending". Generalize this shape instead of
inventing a second one:

```csharp
private static async Task<Task<T>?> WaitForGameTaskAsync<T>(Task<T> task, TimeSpan timeout)
{
    var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
    return completedTask == task ? task : null;
}

private static async Task<Task?> WaitForGameTaskAsync(Task task, TimeSpan timeout)
{
    var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
    return completedTask == task ? task : null;
}
```

`null` means "the deadline passed and the task is still running". Because the
task object is returned, the caller can hand it to `ObserveBackgroundResult`
(already present, `:5849`) so a later exception is logged instead of lost.

Keep `WaitForTaskResultAsync` as a thin wrapper over the generic overload so the
rest-site call sites do not change.

## Pure policy

New: `STS2AIAgent/Game/GameTaskWaitPolicy.cs`

```csharp
internal enum GameTaskWaitOutcome { Completed, TimedOut, Failed }

internal static class GameTaskWaitPolicy
{
    public static GameTaskWaitOutcome Classify(bool taskCompleted, bool taskFaulted, bool deadlineReached);
    public static string DescribeTimeout(string actionName, TimeSpan timeout);
}
```

Rules:

| taskCompleted | taskFaulted | deadlineReached | outcome |
| --- | --- | --- | --- |
| true | true | any | `Failed` |
| true | false | any | `Completed` |
| false | any | true | `TimedOut` |
| false | any | false | `Completed` is not returned; callers only classify after the wait returned, so this row means "still waiting" and must be pinned as `TimedOut`-safe — assert `false`/`false` ⇒ `Completed` is **not** allowed; use `TimedOut` only when the deadline passed and otherwise keep waiting. The method is called once, after the wait, so the fourth row (still running, deadline not reached) is unreachable and the implementation should throw `ArgumentOutOfRangeException` for it so a future caller cannot silently misclassify.

`DescribeTimeout` returns e.g. `"choose_rest_option did not finish within 10s; the game task is still running."`.

## Per-site changes

| Site | Line | Timeout | On timeout |
| --- | ---: | ---: | --- |
| `choose_rest_option` non-target/non-SMITH | 3296 | 10 s | keep today's transition check, report pending, observe the task |
| `crystal_clear_cell` `CellClicked` | 1620 | 10 s | observe, then run the existing settle wait |
| `choose_event_option` finished branch | 2902 | 10 s | observe, then run the existing transition wait |
| `buy_card` / `buy_relic` / `buy_potion` purchase | 3598 / 3671 / 3744 | 10 s | timeout ⇒ observe + `pending`; faulted ⇒ 409 |
| `save_and_quit` `CloseToMenu` | 984 | 20 s | observe, then run the existing main-menu wait |
| `host_multiplayer_lobby` `StartHost` | 4056 | 10 s | timeout ⇒ `pending`; faulted ⇒ 409 |
| `join_multiplayer_lobby` `JoinToHost` | 4097 | 10 s | timeout ⇒ `pending`; faulted ⇒ 409 |
| `run_console_command` `result.task` | 4528 | 10 s | observe, then run the stability wait |
| `invite_ai_teammate` FastHost | 4416 / 4430 | 10 s | timeout ⇒ stop probing modes, return the existing failure result |

Failure text comes from `BackgroundTaskOutcome.DescribeFailure` (added by
`09-12-action-trust`); reuse it rather than re-reading `Task.Exception` inline.

## Compatibility

- No new action names, no response-shape change beyond the already-required
  `pending` status and an explicit timeout message.
- `GameActionService` keeps its signature; only the internal waits change.

## Verification

- `GameTaskWaitPolicyTests`: all three outcomes plus the unreachable-row guard.
- Source-contract test `GameTaskBoundingContractTests`: assert that
  `GameActionService.cs` contains none of the nine bare-await shapes and that
  it calls `WaitForGameTaskAsync` at each of the nine sites.
- Full offline sweep.

## Rollback

Single commit; reverting restores the previous (unbounded) waits.
