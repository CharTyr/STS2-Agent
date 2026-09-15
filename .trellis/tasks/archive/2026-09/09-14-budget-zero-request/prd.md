# 2. Immediate game actions consume zero model requests

## Goal
Immediate map-vote/tutorial actions and game waits must not consume model request quota. Genuine model requests still count exactly once, including multi-round model turns; runtime displayed counters and guard totals agree.

## Evidence
SessionBudgetGuard.Observe:43-47 converts RequestsSpent=0 to 1 unless WaitingForGame. AgentRuntime.TryCompanionImmediateAsync:1027-1033 returns Acted with RequestsSpent default 0, while AccountTurn uses the actual count.

## Acceptance
Immediate Acted with RequestsSpent=0 keeps RequestCount=0; repeated immediate actions under maxRequests=1 continue; subsequent real request reaches cap; full C# runner
Record actual checks and live-environment limits.

## Scope
STS2AIAgent/Agent/SessionBudgetGuard.cs, AgentRuntime immediate result if needed, budget/recovery tests and TestRunner
No release, provider calls or player-save mutation required.

## Authorization
Latest instruction (2026-09-15): organize tasks and stop. This is a planned task, not authorized for continued execution in this session. On resumption use xai/grok-4.6 xhigh subagents unless the user changes that preference.
