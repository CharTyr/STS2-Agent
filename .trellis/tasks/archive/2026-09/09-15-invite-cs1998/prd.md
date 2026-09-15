# Silence CS1998 on invite/continue pending

## Goal
Release builds of GameActionService must not warn CS1998 on invite_ai_teammate / continue_ai_teammate after the pending-observer change.

## Requirements
- ExecuteInviteAiTeammateAsync and ExecuteContinueAiTeammateAsync still return pending immediately when LaunchDualInstanceAsync / continue is not finished, via ObserveBackgroundTask.
- They must not be async methods with zero await. Prefer dropping async and returning Task.FromResult for pending/completed payloads. Throws stay throws.
- Do not change HTTP status mapping, DualLaunchOutcome classification, or the 20s-timeout removal.
- Do not start the game, deploy, commit, or edit start-game-session scripts.

## Acceptance Criteria
- [ ] dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release has no CS1998 on those two methods.
- [ ] Existing C# core tests still pass, including DualLaunch / invite pending contracts.
- [ ] evidence.md records the warning-before/after and the test command.
