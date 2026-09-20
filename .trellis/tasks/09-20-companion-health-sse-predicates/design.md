# Design — Companion health, event stream lifecycle, and predicate split

## Boundaries

1. **Companion identity policy** stays in `Multiplayer/CompanionHealth.cs`; callers continue to ask one question: whether the endpoint is the expected live companion process.
2. **Health projection** stays in `Server/Router.cs`; role-specific projection changes only values of existing host-only keys.
3. **Event demand and delivery** stays in `Server/GameEventService.cs`, with small game-independent lifecycle/delivery seams extracted only if needed for offline tests.
4. **Predicate relocation** remains the same `partial GameStateService`; no public API or call graph redesign.

## Task 1 design

Add one explicit liveness-status predicate accepting exactly `ready` or `degraded`. `IsExpectedProcess` keeps every existing envelope and identity check. POSIX startup helper checks membership in the same two literal statuses while retaining process-alive and port-owned-by-pid guards. Add C# value/type/malformed cases and a shell-focused source/fixture regression where practical.

## Task 2 design

Classify current fields:

- **Common/self:** service, versions, status, api host/port, process ID, instance role, MCP state/url, play state/phase/stop kind/session requests, compatibility, state-build timing.
- **Host-only:** companion process alive/exited, companion discovery, dual status/outcome, team control status.

Keep host-only keys for shape compatibility. On companion, each host-only key is `null`; on host, current values are unchanged. Prefer an internal role-projection helper using plain inputs if that enables deterministic offline contract tests without constructing Godot runtime objects. Document nullable role semantics.

## Task 3 design

Keep one long-lived poll task per `Start()` lifecycle to avoid start/stop races. The loop waits on a demand signal while subscriber count is zero and only builds state after demand exists. Subscription and unsubscription update demand under `_gate`; signal mechanics must tolerate coalescing and avoid disposed-source reuse. `Stop()` marks the service stopped under the same gate, clears subscribers/state, cancels the loop, completes channels, waits boundedly, then disposes lifecycle resources. `Subscribe()` after `Stop()` does not restart polling; normal Mod startup calls `Start()` before routing.

State/event ordering contract:

- First subscriber after a fresh/no-snapshot lifecycle wakes polling; first successful sample publishes `session_started` to that subscriber.
- Subsequent subscribers while `_lastState` exists receive an immediate `stream_ready` snapshot.
- Returning to zero subscribers may retain `_lastState`; a later subscriber can immediately receive `stream_ready`, then polling resumes. This preserves existing reconnect usefulness without building while idle.

A small lifecycle coordinator or injected state-builder seam may be used to test counts/concurrency without Godot. It must not create a second production poll loop.

## Task 4 design

Use bounded channels with `BoundedChannelFullMode.Wait`, but never call `WriteAsync`: producer continues using non-blocking `TryWrite`. In this mode a full queue makes `TryWrite` return false rather than evicting data. Remove and complete that subscriber under the existing service gate. Other subscribers still receive the same envelope in order.

`event_id` remains a global monotonically increasing envelope ID. The server does not replay missed events; channel completion closes SSE cleanly. A reconnect establishes a fresh stream and state alignment (`stream_ready`/fresh `/state`), and IDs make discontinuity observable. Python `iter_events` already treats EOF as normal completion; `wait_for_event` currently returns `None` on EOF rather than reconnecting. Because higher-level `wait_until_actionable` then falls back to polling only on errors, audit decides whether EOF should reconnect until its deadline. If changed, bound it by the existing deadline and preserve immediate surfacing of connection refusal.

## Task 5 design

Use Roslyn/source analysis to identify method declarations whose names start `Can` or `Is`. Move those methods in source order to `GameStateService.Predicates.cs`. Move an additional helper only when every reference to it originates inside the moved set; shared helpers remain in `GameStateService.cs`. Preserve complete declaration text byte-for-byte apart from surrounding blank lines. Add a source contract or deterministic verification script/test proving each removed nonblank declaration/body line occurs in the partial, update source enumerations that name files, lower `GameStateService.cs` budget, and add a named budget only if the predicate partial necessarily exceeds the default 1000-line cap (set close to actual size, never increasing aggregate allowance).

## Compatibility

- `/health` key names remain stable. Only companion values of nonsensical host-only fields become `null`.
- `status=degraded` remains visible and compatibility details stay intact.
- SSE event names and envelope shape stay stable. Slow subscribers now receive an explicit disconnect instead of a silent gap.
- No state/action/predicate behavior change is permitted in Task 5.

## Risks and mitigations

- **Lost wakeup on first subscriber:** perform subscriber insertion and demand transition atomically under `_gate`; use a coalescing async signal whose set-before-wait is retained.
- **Old unsubscribe affects new subscription:** subscriber IDs remain monotonic and removal is ID-specific; demand derives from current dictionary count.
- **Stop/restart resource reuse:** lifecycle generation owns its own CTS/signal; `Stop()` detaches them under lock before disposal.
- **First-sample ordering drift:** tests pin first subscriber `session_started` and later/reconnect `stream_ready` behavior.
- **Source-text gate blindness after partial split:** update all source enumerations and add/preserve relocation evidence.

## Rollback

Tasks are ordered and independently testable. If an event lifecycle implementation introduces races, revert only Task 3/4 event-service changes while retaining Task 1/2 health fixes. Task 5 is mechanical and can be reversed by concatenating unchanged moved declarations back into their original source location.
