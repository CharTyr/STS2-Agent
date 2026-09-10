# Proactive teammate chat and selectable conversation tone

## Goal

Give the in-game agent a proactive speaking capability: at a small number of meaningful moments it says one short, state-grounded sentence on its own, in a tone the player selects. It ships off by default behind an explicit opt-in and never bypasses the existing pause and budget rules.

## Requirements

- R1 Off by default. A default or freshly-loaded settings file must not enable proactive speech; only an explicit player action turns it on.
- R2 Selectable tone. The player picks a conversation tone, persisted with the other settings, and the tone must visibly change the instruction the model receives. An unknown or empty stored value falls back to the default tone instead of failing.
- R3 Bounded volume. At most a small fixed number of proactive messages per session, and never two within a minimum interval. The capability must be quiet by construction, not by hoping the model behaves.
- R4 Reuses the existing chat path. Proactive messages go through the same chat call, the same history log, the same usage accounting and the same budget guard as a player-initiated message. No parallel LLM path.
- R5 Cannot play the game. A proactive message must never dispatch a game action, even if the model answers with play wording. It is read-only by construction, not by prompt wording alone.
- R6 Respects pause and budget. No proactive message while auto-play is paused, and none once the session budget guard refuses further calls.
- R7 Deterministic offline coverage. The trigger policy, tone normalisation, and read-only guarantee are covered by the C# core test executable, which runs without the game.
- R8 The capability is documented as implemented-but-not-live-validated; no document may claim in-game acceptance that was not performed.

## Constraints

- Do not launch the game. Every acceptance command is offline.
- The proactive trigger is derived only from state the auto-play loop already reads. No new game-thread polling loop, no timer that competes with the turn gate.
- The proactive send happens while the existing turn gate is already held; it must not try to acquire it again.
- A failure in the proactive path must never abort or fail the auto-play turn.

## Acceptance Criteria

- [ ] `STS2AIAgent/Agent/ProactiveChatPolicy.cs` exists and contains the decision logic with no Godot or game dependency.
- [ ] `AgentSettings@@ carries the opt-in (default false) and the tone (normalised in `EnsureValidShape`).
- [ ] `ChatOptions@@ can force a read-only chat, and `AgentLoop.ChatAsync@@ honours it even when the message text looks like a play request.
- [ ] `AgentRuntime@@ sends at most `MaxMessagesPerSession` proactive messages, never inside the minimum interval, never while paused, never while the budget guard blocks.
- [ ] The overlay exposes the opt-in and the tone selector, persists both, and survives a settings clone/harvest round trip.
- [ ] `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` passes with new tests registered in `TestRunner`, and the previously passing tests still pass.
- [ ] `docs/proactive-chat-review.md` and the current-status page record the new capability with an explicit "not live-validated" boundary.
- [ ] No document or commit message claims in-game verification of proactive chat.

## Evidence Boundary

Deterministic tests prove the policy, the settings plumbing and the read-only guarantee. They do not prove that a real model produces a good sentence at the right moment in a real run; that stays explicitly unverified until someone plays a live game.
