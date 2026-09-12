# Scope the card-reward skip intent to the reward set that recorded it

Follow-up from `archive/2026-09/09-12-reward-choice-threading`, which threaded the
card *choice* through the request but deliberately left `_cardRewardSkipped` alone.
The user picked option B for that second field: bind it to the reward set's identity
instead of trying to bound it by exit path.

## Goal

`GameActionService._cardRewardSkipped` is a process-wide bool that only the
"already left the reward screen" branch clears
(`GameActionService.cs:2388`). A drain that ends through its own proceed click
(`:2403`), the empty-reward escape (`:2408`), or the timeout return (`:2412`)
leaves it set. The drain then filters the CardReward button out of the next
reward set it visits (`:2507`), so a later card reward can be silently dropped.

The intent itself is legitimate and must outlive its request: `skip_reward_cards` is
one request and the `collect_rewards_and_proceed` that follows is another. The fix
is therefore not to remove the memory but to key it to the reward set it was
recorded in, so it can never apply to a different reward set.

## Requirements

1. The skip intent is stored with the identity of the reward set that recorded it,
   and is honored only while that same reward set is the one being drained.
2. The identity is the owning rewards screen: `NRewardsScreen.GetInstanceId()`. When
   the recording screen is the card-selection overlay
   (`NCardRewardSelectionScreen`), the owner is the sibling `NRewardsScreen` still
   present under the shared overlay stack.
3. Fail-safe direction: when the owner cannot be resolved, the skip is **not**
   honored. Re-showing a card reward is visible and recoverable; silently dropping
   one is not.
4. `skip_reward_cards` → `collect_rewards_and_proceed` on the same reward set keeps
   working exactly as today, including the pending-then-retry case.
5. `resolve_rewards` with `option_index: -1` keeps skipping for the reward set it
   ran on; `choose_reward_card` and an explicit `resolve_rewards` pick keep clearing
   the skip.
6. No behavior change for any path that already behaves correctly (the drain's
   within-flow reads, the empty-reward escape, and the button filter's meaning).

## Acceptance Criteria

- [x] Pure unit tests prove the skip applies to its own reward set id, not to another id, and not when the recorded scope is unresolvable (id `0`); the reviewer confirmed by mutation that dropping the `_rewardSetId != 0` guard fails `UnresolvedNeverApplies`.
- [x] Source-contract tests fail if the button filter reads an unscoped bool again and if `_cardRewardSkipped` returns; the reviewer proved the renamed-bool evasion is caught too.
- [x] A source-contract test pins the owner resolution for both `NRewardsScreen` and `NCardRewardSelectionScreen`.
- [x] `skip_reward_cards` and the drain's skip-alternative click record the scope; `choose_reward_card`, an explicit `resolve_rewards` pick/auto, and leaving the reward screen clear it (all six write sites map 1:1 onto the old behavior).
- [x] `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` passes: 290 PASS / 0 FAIL.
- [x] `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental` is 0 warnings / 0 errors.
- [x] `python scripts/check_verification_gates.py` passes and `scripts/preflight-release.ps1` exits 0 with 12 OK steps.
- [x] `docs/api.md` states the skip scope where `skip_reward_cards` is documented.

## Constraints

- Offline only; no live game session. The identity argument rests on the decompiled
  game flow recorded in `research/skip-scope-identity.md`.
- The overlay-stack shape is an assumption about the running game; the code must
  degrade to "do not honor the skip" when it does not hold.
- No action name, response field, error code, or route changes; no version bump.

## Notes

- Evidence for the leak and for the overlay-stack identity: `research/skip-scope-identity.md`.
- Related precedent: `RewardFlowChoiceState` (same file family) shows the house style
  for a small pure state type with a consumed-once contract.
- Verification record: `evidence.md`.
