# Goal 2 evidence — zero-request immediate actions

Date: 2026-09-15

## Change
`SessionBudgetGuard.Observe` now records `Math.Max(0, result.RequestsSpent)` instead of inventing 1 when RequestsSpent is 0 and the turn is not WaitingForGame. Companion immediate acts (map follow / confirm_modal) therefore consume no model-request quota. Genuine AgentLoop counts still pass through exactly.

## Files
- STS2AIAgent/Agent/SessionBudgetGuard.cs
- STS2AIAgent/Agent/AgentRuntime.cs (explicit RequestsSpent = 0 on companion Act)
- STS2AIAgent.Tests/SessionBudgetGuardTests.cs
- STS2AIAgent.Tests/TestRunner.cs

## Commands
`dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`
First run failed Budget.ImmediateActsDoNotHitCap because cancellation raced Observe. Rewrote the test so three zero-request acts then one RequestsSpent=1 hit the cap via AutoPlayStoppedException.

## Results
PASS including Budget.ZeroRequestImmediateAct, Budget.WaitingStillZero, Budget.NegativeRequestsClamp, Budget.MultiRoundExactCount, Budget.ImmediateActsDoNotHitCap. Goal 1 UpdateLimits tests still PASS. Final run exit 0.

## Limits
Deterministic C# harness only. No live companion autoplay. Goal 1 identity/lock preserved.

