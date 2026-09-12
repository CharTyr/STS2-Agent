# Implement: request-scoped reward choice

1. Add `RewardFlowChoiceState` to `STS2AIAgent/Game/RewardChoicePolicy.cs`
   (`PendingChoice` read-only, `ConsumePendingChoice()` returns-then-resets).
2. Delete the `_pendingCardRewardChoice` field and its doc comment from
   `GameActionService.cs` (the block above `SyncCardPlayCounters`).
3. `DrainRewardFlowAsync`: take `RewardFlowChoiceState choice` and pass it to
   `TryResolveCardRewardAsync`; leave every `_cardRewardSkipped` line untouched.
4. `TryResolveCardRewardAsync`: read via `choice.ConsumePendingChoice()`; remove the
   three `_pendingCardRewardChoice = ...` assignments.
5. `ExecuteResolveRewardsAsync`: build the state from `pendingChoice` and pass it in.
6. `ExecuteCollectRewardsAndProceedAsync`: pass
   `new RewardFlowChoiceState(RewardChoicePolicy.AutoChoice)`.
7. Add the tests (unit + source contract) and register every method in
   `TestRunner.AllTests`.
8. `docs/api.md`: state the retry semantic under `resolve_rewards`.

## Validation

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
python scripts/check_verification_gates.py
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
```

## Review gates

- Confirm by reading the diff that no reward intent is stored anywhere static, and
  that `_cardRewardSkipped`'s reads and writes are byte-identical to before.
- Confirm the request-time out-of-range rejection still precedes the drain.

## Rollback

Single commit.
