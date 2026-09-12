# Evidence: reward-set-scoped skip intent (2026-09-12)

## Implementation

| Piece | Where |
| --- | --- |
| `RewardSkipScope` (skip + reward-set id, fail-safe `AppliesTo`) | `STS2AIAgent/Game/RewardSkipScope.cs` |
| owner resolution `GetRewardSetId` | `GameStateService.cs` (the only addition there) |
| static bool replaced by the scope holder; six write sites and one read site updated | `GameActionService.cs` |
| skip scope documented | `docs/api.md` (`skip_reward_cards` section) |

## Read-site truth table (the risky part)

Condition: `!AppliesTo || reward is not CardReward`.

| AppliesTo | reward kind | clickable | matches old unscoped bool |
| --- | --- | --- | --- |
| true | CardReward | no (skip honored) | yes |
| true | other | yes | yes |
| false | CardReward | yes | yes |
| false | other | yes | yes |

Fail-safe direction: an unresolvable scope (id `0`) yields `AppliesTo == false`, i.e. the
skip is **not** honored. The worst case is a visible, repeatable card-reward screen
rather than a silently dropped reward, and a unit test plus a mutation check pin it.

## Identity is real, not assumed

The decompiled flow, in order:

1. `NCardRewardSelectionScreen.ShowScreen` calls `NOverlayStack.Instance.Push(screen)`.
2. `NOverlayStack.Push` runs `AddChildSafely(screen)`, adds it to the overlay list, and
   only calls `Peek()?.AfterOverlayHidden()` on the previous screen — it hides that
   screen, it does not remove it.
3. `NRewardsScreen` pushed itself onto the same stack when it opened.
4. `AfterOverlayHidden` on the rewards screen only disables its proceed button and
   fades it; the node stays in the stack.
5. `CardReward.OnSelect` removes the selection screen when it is done, and the rewards
   screen becomes `Peek()` again.

So the id recorded while the selection overlay is open (the owning rewards screen found
under the selection screen's parent) is the same object that becomes the current screen
afterwards. `FindDescendants` includes its root, so the lookup also holds if the
selection screen were ever parented inside the rewards screen.

## Review round

No defect required a code change. The reviewer verified by mutation (then reverted):
renaming the read predicate to a differently-named bool still fails the contract test;
reverting to the old field fails two tests; dropping the `_rewardSetId != 0` guard
fails the fail-safe unit test. That establishes the new tests are not vacuous.

## Commands actually run

```text
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release      -> 290 PASS / 0 FAIL
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental        -> 0 warnings / 0 errors
cd mcp_server; uv run --locked python -m unittest discover -s tests            -> Ran 76 tests, OK
python scripts/check_verification_gates.py                                     -> passed
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1         -> exit 0, 12 OK steps
```

## Recorded assumptions (offline)

- The overlay-stack ownership is read from the decompiled sources; if the runtime shape
  differs, `GetRewardSetId` returns 0 and the skip is simply not honored.
- If two live `NRewardsScreen` instances ever coexisted under the overlay stack, the
  lookup takes the first valid one; a stale id then means "not honored" — the same
  fail-safe direction, never a dropped reward.
- The skip holder is static and unsynchronized across concurrent requests, exactly as
  the bool was before; not a regression.

## Not verified

No live game session; all claims are offline code contracts, decompiled-flow evidence,
and unit tests.
