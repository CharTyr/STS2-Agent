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

- [ ] These call sites no longer await a game task without a deadline: `choose_rest_option` (GameActionService.cs:3296), `crystal_clear_cell` (:1620), `choose_event_option` finished branch (:2902), `buy_card` / `buy_relic` / `buy_potion` (:3598 / :3671 / :3744), `save_and_quit` (:984), `host_multiplayer_lobby` (:4056), `join_multiplayer_lobby` (:4097), `run_console_command` (:4528), `invite_ai_teammate` FastHost path (:4416 / :4430).
- [ ] A timed-out wait returns `status="pending"`, `stable=false`, and a message that names the action and the timeout.
- [ ] A wait whose task completed with `false` or with an exception returns the established error envelope (`409 invalid_action` / `503 state_unavailable`), never a silent `pending`.
- [ ] A source-contract test fails if any of the nine bare `await` shapes reappears in `GameActionService.cs`.
- [ ] A pure policy unit test pins the timeout decision (still running at deadline ⇒ timeout even if the task completes later).
- [ ] Full offline sweep green.

## Constraints

- Offline only; no live game session.
- `configure-await` semantics must stay game-thread safe: do not introduce
  `ConfigureAwait(false)` on paths that touch Godot objects.

## Notes

- Evidence and the per-site timeout table are in `research/bounded-waits-audit.md`.
