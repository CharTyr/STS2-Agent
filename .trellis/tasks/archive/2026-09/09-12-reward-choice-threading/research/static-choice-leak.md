# Evidence: the static card-choice leak (2026-09-12)

All line numbers are from the tree at `3f55a3f`.

## The field

```text
GameActionService.cs:81-86
  /// When set by resolve_rewards, TryResolveCardRewardAsync picks this
  /// card index instead of the first option. -2 means skip.
  /// -1 means no pending choice (use default behavior).
  private static int _pendingCardRewardChoice = -1;
```

Writes: `1804`, `1819`, `1824` (request handling), `1849` (commit the choice),
`2560`, `2572`, `2589` (consume-time reset).
Reads: `2548` only.

## Why it leaks

- The entry guard accepts any collectable reward screen, not just a card-reward
  screen (`1786-1794`).
- The request-time validation only runs when the current screen already is
  `NCardRewardSelectionScreen` (`1833-1847`), so any index is accepted while the
  player is on the reward list.
- The value is committed at `1849` and only consumed inside
  `TryResolveCardRewardAsync`, which the drain reaches only through
  `2381-2389`.
- Two exits leave it set: the "no longer on a rewards screen" branch
  (`2391-2395`, clears only `_cardRewardSkipped`) and the timeout return
  (`2417`). Nothing at the run boundary clears it either (`rg
  _pendingCardRewardChoice` finds no other reference).

Consequence: `resolve_rewards` on a reward set without a card reward leaves the
intent behind, and a later `collect_rewards_and_proceed` (`1877`, same drain,
never sets the field) consumes it — skipping the card reward for `-2`, or picking
the Nth card for an explicit index. An out-of-range leftover throws a 409 labelled
`action: "resolve_rewards"` even though that action was not the one called.

## The second static field (not changed by this task)

```text
GameActionService.cs:64-69   private static bool _cardRewardSkipped;
  set true:  1804 (resolve_rewards -1), 2013 (skip_reward_cards), 2577 (skip alternative clicked)
  set false: 1819, 1824 (resolve_rewards pick/auto), 1983 (choose_reward_card), 2393 (left the rewards screen)
  read:      2512 in TryGetNextClaimableRewardButton
```

`skip_reward_cards` sets it so the following `collect_rewards_and_proceed` does not
re-open the card reward through the rewards-screen button. That intent must outlive
the request, so the same parameter threading does not apply. Its own defect is the
timeout exit (`2417`), which leaves it set; a later drain would then hide card
rewards. Left as a recorded follow-up.

## Commands used

```text
rg -n "_pendingCardRewardChoice|_cardRewardSkipped|DrainRewardFlowAsync|TryResolveCardRewardAsync" STS2AIAgent/Game/GameActionService.cs
rg -n "TryResolveCardReward|DrainRewardFlow|RewardChoice" STS2AIAgent.Tests/
```

Existing tests that constrain the change: `RewardChoicePolicyTests` (pure policy),
`GameActionTrustContractTests` (validation precedes the drain; no `FirstOrDefault`
fallback), `RewardFlowContractTests` (empty-reward escape path).

## Not verified

No live game session; the leak analysis is static reading of the handler plus the
drain's control flow.
