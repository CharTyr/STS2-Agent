# Proactive Chat / Tone / Low-Interruption Implementation Review

> Reviewed: 2026-09-08. Updated: 2026-09-10 (the gap below was closed by an opt-in implementation; the
> 2026-09-08 review is kept as the record of what was true then).
> Source: STS2AIAgent/Agent/AgentRuntime.cs, STS2AIAgent/Agent/AgentLoop.cs, STS2AIAgent/Agent/PlayPrompt.cs,
> STS2AIAgent/Agent/ProactiveChatPolicy.cs.

## Scope

This task asks whether the agent proactively speaks, how chat tone is controlled, and whether auto-play is low-interruption. The finding is a code review, not a product change.

## Findings

### No proactive speaking path exists

- `SendChatAsync` / `SendChatCoreAsync` are the only message entry points and are invoked by the player (UI chat input) or by test/teammate-logic code.
- `AutoPlayLoopAsync` is a decision loop: it inspects state, waits on the turn gate, and calls `PlayOnceAsync` -> `act`. It never pushes a chat message to the player on its own.
- The only autonomous *reply* is `TryCompanionImmediateAsync`: the companion follows the human map vote and replies with a brief decision message. That is a response to a human action, not proactive outreach.
- No `Timer`, background schedule, or `_ = Task.Run` loop sends messages to the player.

Conclusion as of 2026-09-08: **proactive chat was not implemented.** Any feature would need an explicit user
opt-in and a new dispatch path; it was not accidentally present.
Status as of 2026-09-10: **implemented behind an off-by-default opt-in** — see
[Implemented proactive chat](#implemented-proactive-chat-2026-09-10) below.

### Tone is prompt-constrained, not setting-driven

- `ChatSystem` asks for concise, concrete, grounded advice in the player language and forbids inventing indexes or actions.
- `TeammateChatSystem` asks for a friendly co-op partner tone, acknowledging suggestions, explaining disagreements kindly.
- `TeammatePlayContext` treats conversation as historical context and says conversational replies are intentions, not evidence.
- There was no user-facing tone/style setting; tone lived entirely in the system prompts. Since 2026-09-10 the
  proactive-chat path appends a selectable tone instruction (see below); player-initiated chat tone is still
  prompt-defined.

### Low-interruption behaviors that do exist

- **Budget guard**: `CheckBudget` stops auto-play gracefully at request/token caps and shows the next action hint; `SessionBudgetLimits.BudgetRecoveryNextAction` tells the player how to resume.
- **Pause semantics**: `SessionBudgetLimits.CanResetSessionStats` and the session guard preserve cumulative usage across pause/resume; pause does not silently clear counters.
- **Turn gate**: `_turnGate.WaitAsync` serializes model calls; chat during active play is rejected with a message rather than queuing surprise behavior.
- **Recovery backoff**: `AutoPlayRecovery` bounds retries and cancels on timeout instead of hammering the upstream.

## Recommendation (product boundary)

If proactive chat is wanted, define it as a new capability with its own acceptance (trigger, opt-in, budget impact) and record it in the status page; this review confirms it is currently absent by design.

That recommendation was taken up on 2026-09-10. The capability now exists with the boundary below.

## Implemented proactive chat (2026-09-10)

### Scope of the implementation

| Aspect | Decision |
| --- | --- |
| Opt-in | `AgentSettings.ProactiveChatEnabled`, default `false`; the settings tab exposes it under advanced options |
| Tone | `AgentSettings.ProactiveChatTone` (`friendly` / `calm` / `terse`, default `friendly`), selectable in the UI, normalised in `EnsureValidShape` |
| Triggers | Two moments only: combat start and combat end, derived from the situation key the auto-play loop already reads (`ProactiveChatPolicy.SituationKey` / `Observe`) |
| Volume | `MaxMessagesPerSession = 6`, `MinInterval = 75s`, both enforced by `ProactiveChatPolicy.Decide` |
| Model | The conversation model, through the existing `AgentLoop.ChatAsync` path |
| Read-only | `ChatOptions.ReadOnly = true` forces `allowAct = false` in `AgentLoop.ChatAsync`; the proactive turn cannot dispatch a game action even if the model answers with play wording |
| Budget | The turn is accounted with `recordBudget: true`, exactly like a teammate reply; a blocked budget guard stops proactive speech |
| Pause | Sending happens inside the auto-play loop while the turn gate is held, so it cannot run while paused; `PlayRunning` is also checked by the policy |
| Failure isolation | Any exception or model error is recorded as a diagnostic event and never fails the auto-play turn |

### Gate order

`Decide` refuses in this fixed order and returns a stable reason: opt-in off, no moment, not playing, budget
blocked, session cap reached, minimum interval not elapsed. Only when every gate passes does it approve a send.

### Evidence and boundary

Offline coverage: 17 C# core tests (`ProactiveChatPolicyTests` plus two `AgentLoopTests` cases) registered in
`TestRunner`, covering the defaults, tone normalisation, every refusal gate, the moment transitions, the
read-only guarantee and tone injection into the system prompt. The mod project compiles with 0 warnings and
0 errors.

Not verified: real in-game behaviour. Whether a live model produces a useful sentence at the right moment,
whether the 75-second interval feels right, and whether the trigger fires correctly across real screen
transitions all remain unverified until someone plays a live game. Do not describe the offline tests as
in-game acceptance.

