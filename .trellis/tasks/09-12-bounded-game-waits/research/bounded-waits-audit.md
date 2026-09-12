# Audit evidence: unbounded game awaits (2026-09-12)

`GameThread.InvokeAsync` (`STS2AIAgent/Server/Router.cs:245`) has no timeout, and
`Router` awaits the handler, so a game task awaited without a deadline pins the
HTTP request for as long as the game never completes it.

Full list of bare game-task awaits in `STS2AIAgent/Game/GameActionService.cs`
(`Select-String` over the whole file at HEAD `608c583`):

```text
 984: await closeTask;                                  save_and_quit (CloseToMenu)
1620: await minigame.CellClicked(minigame.cells[x, y]);  crystal_clear_cell
2902: await NEventRoom.Proceed();                        choose_event_option (finished branch)
3296: stable = await chooseTask;                         choose_rest_option (non-target, non-SMITH)
3598: var success = await entry.OnTryPurchaseWrapper(inventory);  buy_card
3671: var success = await entry.OnTryPurchaseWrapper(inventory);  buy_relic
3744: var success = await entry.OnTryPurchaseWrapper(inventory);  buy_potion
4056: var hostStarted = await startHostTask;             host_multiplayer_lobby
4097: await scene.JoinToHost(initializer);               join_multiplayer_lobby
4416: await task;                                        invite_ai_teammate (FastHost, 1-arg path)
4430: await task;                                        invite_ai_teammate (FastHost, 0-arg path)
4528: await result.task;                                 run_console_command
5931: var success = await task;                          ObserveBackgroundResultCore (already a background observer)
```

`:5931` is the background observer itself and is intentionally unbounded; it is
not reachable from a request handler's critical path. The other twelve lines are
the fix targets (nine distinct call sites).

## Why :3296 is the worst one

```text
3280: var chooseTask = RunManager.Instance.RestSiteSynchronizer.ChooseLocalOption(...);
3287: else if (selectedOptionId.Equals("SMITH", ...)) {
3289:     // SMITH keeps the task open until the follow-up card selection completes.
3291:     ObserveBackgroundResult(chooseTask, "choose_rest_option");
3292:     stable = await WaitForRestOptionTransitionAsync(TimeSpan.FromSeconds(10));
3293: }
3294: else {
3296:     stable = await chooseTask;      // <-- TOKE and other card-selection options land here
```

The comment two lines above documents exactly the hazard that the `else` branch
ignores: any rest option that opens a card-selection overlay keeps the task open.

## Existing bounded helper to reuse

```text
3432: private static async Task<bool?> WaitForTaskResultAsync(Task<bool> task, TimeSpan timeout)
3434:     var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
3435:     if (completedTask != task) { return null; }
3440:     return await task;
```

Caller pattern already in use at `:3385-3393` (timeout ⇒ observe in background ⇒
return `pending`-shaped failure).

## Timeout precedents from the same file

```text
 271: end_turn                       5 s
 472: play_card                     12 s
 836: switch_profile                15 s
 894: continue_run                  15 s
1001: save_and_quit                20 s   (WaitForMainMenuAfterSaveAndQuitAsync)
1803: claim_reward                 10 s
3285: choose_rest_option target    10 s
3608: buy_card                     10 s
4531: run_console_command          10 s
```

So the per-site timeouts chosen in `design.md` (10 s, except save_and_quit at
20 s) match the file's own conventions.

## Out of scope here

- `ObserveBackgroundResultCore` losing failures on the success path is handled by
  `09-12-action-trust` for `remove_card_at_shop`; the rest-site observe calls keep
  their current semantics because the following transition wait reports the
  outcome.
- Nothing here was verified against a live game.
