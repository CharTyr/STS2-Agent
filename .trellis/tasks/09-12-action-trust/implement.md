# Implement: action trust

Order matters: policies first, then their call sites, then tests.

## Steps

1. Create `STS2AIAgent/Game/RewardChoicePolicy.cs` (`RewardChoiceKind`,
   `RewardChoiceResolution`, `RewardChoicePolicy.Resolve`).
2. Create `STS2AIAgent/Game/MenuTransitionPolicy.cs`.
3. Create `STS2AIAgent/Game/BackgroundTaskOutcome.cs`.
4. Create `STS2AIAgent/Game/CardPlayCounterPolicy.cs`.
5. Register all four files in `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`.
6. `GameActionService.ExecuteResolveRewardsAsync` (~L1695-1717): validate an
   explicit index against the live option list when a reward screen is open;
   throw `409 invalid_target` when invalid.
7. `GameActionService.TryResolveCardRewardAsync` (~L2381-2420): consume through
   `RewardChoicePolicy`; remove the silent `options.FirstOrDefault()` fallback
   for an explicit-but-invalid index.
8. `WaitForMainMenuExitAsync` (~L5156): drop the "`GetOpenModal() != null` ⇒ true"
   branch, delegate to `MenuTransitionPolicy.IsMenuExited`; report the blocking
   modal in the message.
9. `WaitForEmbarkTransitionAsync` (~L5331): same, through
   `MenuTransitionPolicy.IsEmbarkSettled`.
10. `IsCharacterSelectOpenOrActionableModal` (~L5074): a modal no longer counts
    as an open character select; rename to reflect the narrower contract and
    update `WaitForCharacterSelectOpenAsync`.
11. `ExecuteRemoveCardAtShopAsync` (~L3793): keep the fire-and-forget observe,
    add the already-failed check through `BackgroundTaskOutcome.DescribeFailure`.
12. `ExecuteChooseBundleAsync` / `ExecuteConfirmBundleAsync` (~L3140/L3214):
    replace the empty-state fallback with `503 state_unavailable`.
13. `ExecutePlayCardAsync` (~L465-494): add `RollBackCardPlayCounters` guarded by
    `CardPlayCounterPolicy.ShouldRollBack`.
14. Add the four test classes + the source-contract assertions; register every
    test in `STS2AIAgent.Tests/TestRunner.AllTests`.

## Validation commands

Run from the repository root:

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
python scripts/check_verification_gates.py
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
```

```bash
cd mcp_server && uv run --locked python -m unittest discover -s tests
```

## Review gates

- After step 7: confirm a valid index still selects that exact card and that
  auto/skip semantics are untouched.
- After steps 8-10: confirm the three removed branches cannot be reached by a
  legitimate success path (each returned true only while a modal was open).
- Before commit: full sweep above must be green, and `git diff` must not touch
  any file outside `STS2AIAgent/Game/`, `STS2AIAgent.Tests/`.

## Rollback points

- Steps 1-5 are additive; reverting them alone is safe.
- Steps 6-13 are independent per defect; each can be reverted on its own.
