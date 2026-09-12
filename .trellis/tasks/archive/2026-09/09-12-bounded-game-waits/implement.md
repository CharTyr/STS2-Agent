# Implement: bounded game waits

1. Add `STS2AIAgent/Game/GameTaskWaitPolicy.cs`.
2. Register it in `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`.
3. In `GameActionService.cs`, add the generic `WaitForGameTaskAsync` overloads next
   to the existing `WaitForTaskResultAsync` (`:3432`) and re-implement
   `WaitForTaskResultAsync` on top of them.
4. Convert the nine call sites in the order listed in `design.md`, keeping each
   site's existing post-wait logic and message style.
5. Add `GameTaskWaitPolicyTests` and `GameTaskBoundingContractTests`; register
   every test method in `TestRunner.AllTests`.

## Validation

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
python scripts/check_verification_gates.py
```

## Review gates

- After each converted site: confirm the success path is byte-equivalent in
  behavior (same follow-up wait, same message) and that a faulted task becomes an
  error instead of `pending`.
- Before commit: no bare game-task `await` remains; the contract test proves it.

## Rollback

Per-site conversion is independent; partial reverts are safe.
