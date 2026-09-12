# Design: reward-set-scoped skip intent

## Why scoping, not clearing on success

The intent behind `_cardRewardSkipped` is real and must survive across two requests
(`skip_reward_cards`, then the `collect_rewards_and_proceed` that follows). Any fix
that only adds a clear-on-success exit still guesses where the intent ends; keying it
to the reward set makes "where it ends" a fact about identity, so the timeout exit and
the pending-retry path need no special handling at all.

## Identity

From the decompiled game flow:

```text
NCardRewardSelectionScreen.ShowScreen(...)   -> NOverlayStack.Instance.Push(screen)
NOverlayStack.Push(screen)                   -> Peek()?.AfterOverlayHidden(); AddChildSafely(screen); _overlays.Add(screen)
NOverlayStack.Remove(screen)                 -> only when the owner closes it
CardReward.OnSelect()                        -> ShowScreen(...); await CardsSelected(); NOverlayStack.Instance?.Remove(selectionScreen)
```

`Push` hides the previous overlay but keeps it in the stack, so while the
card-selection overlay is open the owning `NRewardsScreen` is still a live child of
the same overlay stack. Therefore:

- current screen is `NRewardsScreen` ⇒ the owner is that screen;
- current screen is `NCardRewardSelectionScreen` ⇒ the owner is the `NRewardsScreen`
  among the selection screen's siblings (its parent is the overlay stack);
- anything else, or no such sibling ⇒ unresolved (`0`), and the skip is not honored.

`ulong` comes from Godot's `GetInstanceId()`, which the file already uses for reward
buttons (`GameActionService.cs:2394`, `:2505`).

## New pure type

`STS2AIAgent/Game/RewardSkipScope.cs` (added to the test `Compile` list so the
offline runner can pin it):

```csharp
/// <summary>
/// Remembers that the player skipped a card reward, keyed to the reward set that
/// recorded it. The intent must outlive the request that set it (skip_reward_cards
/// is one call and the collect that follows is another), so it is stored rather than
/// threaded; the reward-set id keeps it from ever applying to a different set.
/// </summary>
internal sealed class RewardSkipScope
{
    private bool _skipped;
    private ulong _rewardSetId;

    /// <summary>True only for the reward set the skip was recorded in. An unresolved
    /// scope (id 0) never applies, so a missing identity re-shows the reward instead
    /// of silently dropping it.</summary>
    public bool AppliesTo(ulong rewardSetId);

    public void MarkSkipped(ulong rewardSetId);

    public void Clear();

    /// <summary>Recorded scope id for diagnostics; 0 when nothing is recorded.</summary>
    public ulong RecordedRewardSetId { get; }
}
```

`AppliesTo`: `_skipped && _rewardSetId != 0 && _rewardSetId == rewardSetId`.

## Read side: resolving the owner

New public helper in `GameStateService.cs` (read-side screen inspection is already
its responsibility), reusing the existing `FindDescendants<T>` helper:

```csharp
/// <summary>
/// Identity of the reward set a reward-related screen belongs to. Returns 0 when it
/// cannot be resolved, which callers treat as "no scope".
/// </summary>
public static ulong GetRewardSetId(IScreenContext? currentScreen)
```

Behavior: `NRewardsScreen` ⇒ its own id; `NCardRewardSelectionScreen` ⇒ the id of the
valid `NRewardsScreen` found under the selection screen's parent (the overlay stack),
or 0; otherwise 0.

## Write and read sites

| Site | Today | After |
| --- | --- | --- |
| `ExecuteSkipRewardCardsAsync` (write true) | `_cardRewardSkipped = true` | `CardRewardSkips.MarkSkipped(GameStateService.GetRewardSetId(currentScreen))` |
| drain skip-alternative click (write true) | `_cardRewardSkipped = true` | `MarkSkipped` with the id resolved from the selection screen |
| `ExecuteResolveRewardsAsync` `-1` (write true) | `_cardRewardSkipped = request.option_index.Value == -1` | mark/clear with the resolved id |
| `ExecuteResolveRewardsAsync` pick/auto, `ExecuteChooseRewardCardAsync`, drain "left the reward screen" (writes false) | `= false` | `CardRewardSkips.Clear()` |
| `TryGetNextClaimableRewardButton` (read) | `(!_cardRewardSkipped \|\| button.Reward is not CardReward)` | `(!CardRewardSkips.AppliesTo(rewardsScreen.GetInstanceId()) \|\| button.Reward is not CardReward)` |

The static holder stays in `GameActionService` (it is the same class's state), but it
is now a `RewardSkipScope` instance rather than a bool.

## Behavior deltas

1. **Fixed:** a skip recorded in reward set A no longer suppresses the card reward in
   a later reward set B, however the earlier drain exited.
2. **Unchanged:** skip → collect on the same set still suppresses re-opening the card
   reward, including the pending/timeout retry, because the set's identity is stable.
3. **Fail-safe:** when the owner cannot be resolved the skip is ignored, so the worst
   case is a visible, repeatable card-reward screen rather than a silently dropped
   reward.

## Compatibility

- No wire, action, error, or route change; `GameStateService.GetRewardSetId` is additive.
- `RewardFlowChoiceState` and `RewardChoicePolicy` are untouched.

## Verification

- Unit: `RewardSkipScopeTests` (applies to its own id; not to another id; not to id 0;
  clear; re-mark overwrites).
- Source contract: the button filter reads `AppliesTo`, the field is gone,
  `GetRewardSetId` handles both screen types, and each write site records or clears.
- Full offline sweep.

## Rollback

Single commit; reverting restores the unscoped bool.
