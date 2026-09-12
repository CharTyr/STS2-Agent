# Evidence: request-scoped reward choice (2026-09-12)

## Implementation

| Piece | Where |
| --- | --- |
| `RewardFlowChoiceState` (per-request intent, `ConsumePendingChoice()`) | `STS2AIAgent/Game/RewardChoicePolicy.cs` |
| static field removed, both call sites and both signatures threaded | `STS2AIAgent/Game/GameActionService.cs` |
| retry semantics documented | `docs/api.md` (`resolve_rewards` section) |

## Branch-by-branch equivalence (reviewed by reading the diff)

| Branch | Before | After | Equivalent |
| --- | --- | --- | --- |
| valid explicit index | read N, reset to Auto before selecting | `ConsumePendingChoice()` returns N and resets in the same step | yes |
| out-of-range explicit index | reset to Auto, then throw 409 `invalid_target` (`action="resolve_rewards"`) | consumption resets, then the same 409 payload; no card is clicked | yes |
| auto with zero options | `Kind=Auto` is invalid ⇒ `return false`, no reset needed | consume resets, `return false` | yes |
| skip sentinel | reset to Auto, click the skip alternative, set `_cardRewardSkipped` | same branch, unchanged code | yes |
| second card screen in one drain | second read saw Auto | second `ConsumePendingChoice()` returns Auto | yes |

## Review round

The reviewer found and fixed two weak tests:

1. one assertion looked for `RewardChoicePolicy.AutoChoice=`, which can never
   appear in assignment position, so the "no write-back at consume time" check was
   vacuous;
2. nothing pinned that `resolve_rewards` forwards its **own** choice to the drain,
   so replacing it with `AutoChoice` would have silently dropped the explicit index
   while every related test stayed green.

It also confirmed there is no third caller hidden behind a default parameter, that
no new static state was introduced, and that the other static fields in the file
are either turn-scoped counters (correct to keep) or dead code.

## Commands actually run

```text
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release      -> 278 PASS / 0 FAIL
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental        -> 0 warnings / 0 errors
cd mcp_server; uv run --locked python -m unittest discover -s tests            -> Ran 76 tests, OK
python scripts/check_verification_gates.py                                     -> passed
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1         -> exit 0, 12 OK steps
```

## Semantic change (intended, documented)

An explicit card choice belongs to the call that carries it. Retrying a
`pending` `resolve_rewards` with `collect_rewards_and_proceed` now resolves card
rewards automatically instead of inheriting the earlier explicit choice; retrying
with `resolve_rewards` is unaffected because the request carries the choice again.

## Recorded follow-up

`_cardRewardSkipped` intentionally keeps its static lifetime (the
`skip_reward_cards` → `collect_rewards_and_proceed` sequence needs it) and its
timeout exit still leaves it set for a later drain. Separate task.

## Not verified

No live game session; all claims are offline code contracts and unit tests.
