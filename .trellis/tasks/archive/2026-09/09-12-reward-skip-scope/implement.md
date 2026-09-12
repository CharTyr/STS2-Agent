# Implement: reward-set-scoped skip intent

1. Add `STS2AIAgent/Game/RewardSkipScope.cs`; register it in
   `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`.
2. Add `GameStateService.GetRewardSetId(IScreenContext?)` (handles `NRewardsScreen`
   and `NCardRewardSelectionScreen`; 0 when unresolved).
3. `GameActionService`: replace the `private static bool _cardRewardSkipped` field
   with a `RewardSkipScope` holder and update the five write sites and the one read
   site per the design table.
4. Tests: `RewardSkipScopeTests` plus source-contract assertions; register every new
   method in `TestRunner.AllTests`.
5. Documentation: if `docs/api.md` has a `skip_reward_cards` section, add one
   sentence stating the skip applies to the reward set it was recorded in.

## Validation

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
python scripts/check_verification_gates.py
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
```

## Review gates

- Read the diff to confirm the read-site condition is still "do not click a CardReward
  button while a skip applies" and not an inverted or always-true predicate.
- Confirm the unresolved-scope path returns "not skipped" (fail-safe), not "skipped".
- Confirm no other file gained a copy of the skip state.

## Rollback

Single commit.
