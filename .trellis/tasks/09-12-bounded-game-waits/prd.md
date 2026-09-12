# Bound every game-side await so no action can hang the HTTP request

Parent: `09-12-agent-trust-hardening` (goal 2 of 5).

## Goal

`GameThread.InvokeAsync` has no timeout and `Router` awaits the action handler, so
any game task the handler awaits without a deadline can pin an HTTP request
forever. The caller sees a hang, not an error and not `pending`. Nine such
`await`s exist today.

## Requirements

1. Every game task awaited by an action handler must be bounded by an explicit
   deadline and must not be awaited bare.
2. A task that is still running at the deadline is reported as `pending` with a
   message naming the action and the timeout, and the task stays observed so its
   exception cannot go unobserved. A task that finished but failed is reported as
   an error, not as `pending`.
3. No behavior change on the success path: timeouts are chosen from the existing
   waits of the same action (`choose_rest_option` 10 s, save/quit 20 s, purchase
   10 s, console 10 s, multiplayer lobby 10 s, crystal sphere 10 s).
4. Reuse the existing `WaitForTaskResultAsync(Task<bool>, TimeSpan)` helper
   instead of adding a second, divergent waiting pattern.

## Acceptance Criteria

- [x] These call sites no longer await a game task without a deadline: `choose_rest_option`, `crystal_clear_cell`, `choose_event_option` finished branch, `buy_card` / `buy_relic` / `buy_potion`, `save_and_quit`, `host_multiplayer_lobby`, `join_multiplayer_lobby`, `run_console_command`, `invite_ai_teammate` FastHost path.
- [x] A timed-out wait returns `status="pending"`, `stable=false`, and a message that names the action and the timeout (the FastHost path returns its pre-existing failure result instead, by design).
- [x] A wait whose task completed with `false` or with an exception returns the established error envelope (`409 invalid_action`), never a silent `pending`.
- [x] A source-contract test fails if any bare `await <expr>;` reappears anywhere in `GameActionService.cs` outside the two background observers and the `WaitForTaskResultAsync` wrapper; proven with a mutation probe.
- [x] A pure policy unit test pins the timeout decision (still running at deadline ⇒ timeout even if the task completes later), including the unreachable-call guard.
- [x] Full offline sweep green: C# 265 PASS / 0 FAIL, build 0 warning / 0 error, gates pass, preflight exit 0 (recorded in `evidence.md`).

## Constraints

- Offline only; no live game session.
- `configure-await` semantics must stay game-thread safe: do not introduce
  `ConfigureAwait(false)` on paths that touch Godot objects.

## Notes

- Evidence and the per-site timeout table are in `research/bounded-waits-audit.md`.
- Verification record: `evidence.md`.
- Spec sync: the bounded-game-task contract is now recorded in
  `.trellis/spec/mod/game-actions.md` §"Waiting and frame safety".
