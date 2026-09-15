# 1. Active-session budget identity survives settings changes

## Goal
Saving or reloading settings during autoplay must update configured limits while retaining one coherent accumulated token/request ledger shared by AgentLoop, recovery and chat. Lowered limits must prevent subsequent calls; raising limits must not trigger a stale-limit stop.

## Evidence
AgentRuntime.cs:393-401,467-473 replaces _budgetGuard while AutoPlayLoopAsync:925-968 captures it once. AgentLoop uses a provider for the current object. Save/test/reload can therefore split accounting and enforcement.

## Acceptance
C# deterministic limit-lowering, limit-raising, settings/reload wiring and cumulative accounting tests; full dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
Record actual checks and live-environment limits.

## Scope
STS2AIAgent/Agent/SessionBudgetGuard.cs, AgentRuntime.cs, focused budget tests and TestRunner registration
No release, provider calls or player-save mutation required.

## Authorization
Latest instruction (2026-09-15): organize tasks and stop. This is a planned task, not authorized for continued execution in this session. On resumption use xai/grok-4.6 xhigh subagents unless the user changes that preference.
