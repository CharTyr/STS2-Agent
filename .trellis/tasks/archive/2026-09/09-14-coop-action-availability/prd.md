# 4. Co-op action availability respects executable structural guards

## Goal
Raw/compact names and descriptors advertise invite/continue only for a structurally eligible host. Continue additionally requires the existing save probe. Unconfigured models must retain the external-takeover route. Execution keeps defensive checks.

## Evidence
GameStateService.cs:394-401,2787-2790 advertises invite solely on visible NMainMenu; CanContinueAiTeammate:1599-1611 omits PlayRunning. Executors:5236-5249,5383-5403 call CoopLaunchPolicy.GetError which rejects companion/running host.

## Acceptance
Host/companion x running/paused policy truth table, both action lists wired to same probe; full C# suite and Mod Release build. Real native UI availability is separate evidence.
Record actual checks and live-environment limits.

## Scope
STS2AIAgent/Game/GameStateService.cs, focused ContinueCoop/CoopRoute tests and TestRunner
No release, provider calls or player-save mutation required.

## Authorization
Latest instruction (2026-09-15): organize tasks and stop. This is a planned task, not authorized for continued execution in this session. On resumption use xai/grok-4.6 xhigh subagents unless the user changes that preference.
