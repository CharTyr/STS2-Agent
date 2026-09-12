# Design: request-scoped reward choice

## Current state (the defect)

```text
GameActionService.cs
  86   private static int _pendingCardRewardChoice = -1;      // process-wide
 1849   _pendingCardRewardChoice = pendingChoice;              // written by resolve_rewards
 1851   var stable = await DrainRewardFlowAsync(TimeSpan.FromSeconds(20));
 2391   if (currentScreen is not NRewardsScreen rewardsScreen) {
 2393       _cardRewardSkipped = false;
 2394       return true;                                        // <-- does NOT clear the choice
 2417   return IsRewardFlowStable();                            // <-- timeout path, also does not clear
 2548   var resolution = RewardChoicePolicy.Resolve(_pendingCardRewardChoice, options.Count);  // only consumer
```

The only consumer is `TryResolveCardRewardAsync` (reached from the drain only when
the current screen is `NCardRewardSelectionScreen`, drain line 2381). A reward set
with no card reward leaves the value set, and nothing in the reward flow or at run
boundaries clears it, so the next `collect_rewards_and_proceed` inherits it.

## Shape

New type in `STS2AIAgent/Game/RewardChoicePolicy.cs` (same concern as
`RewardChoicePolicy`, and that file is already in the test `Compile` list, so the
offline runner can test it):

```csharp
/// <summary>
/// One reward-drain invocation's card-choice intent. Created per request and never
/// stored statically, so a drain that finished, timed out, or never reached a card
/// reward cannot influence a later call.
/// </summary>
internal sealed class RewardFlowChoiceState
{
    public RewardFlowChoiceState(int pendingChoice);

    public int PendingChoice { get; }

    /// <summary>
    /// Returns the pending choice and resets it to the automatic behavior, so a second
    /// card-reward screen inside the same drain takes the first card.
    /// </summary>
    public int ConsumePendingChoice();
}
```

`ConsumePendingChoice` is what the old code did with three separate assignments
(`GameActionService.cs:2560`, `:2572`, `:2589`); moving the reset into the read
keeps the within-drain behavior and removes the three scattered writes.

## Signature changes

```csharp
// required parameter: no default, so every caller states its intent
private static async Task<bool> DrainRewardFlowAsync(TimeSpan timeout, RewardFlowChoiceState choice);
private static async Task<bool> TryResolveCardRewardAsync(
    NCardRewardSelectionScreen cardRewardScreen, DateTime deadline, RewardFlowChoiceState choice);
```

Call sites:

| Caller | Argument |
| --- | --- |
| `ExecuteResolveRewardsAsync` | `new RewardFlowChoiceState(pendingChoice)` built from the request |
| `ExecuteCollectRewardsAndProceedAsync` | `new RewardFlowChoiceState(RewardChoicePolicy.AutoChoice)` |

Both call sites are in the same file; there are only two (verified by grep). The
request-time validation in `ExecuteResolveRewardsAsync` (the
`optionCount > 0 && !resolution.IsValid` guard) is unchanged; only the value it
passes on changes.

## Behavior deltas

1. **Fixed leak.** With no reaching card-reward screen, the choice dies with the
   call. Previously a later `collect_rewards_and_proceed` could apply it.
2. **Documented retry semantic.** A `pending` `resolve_rewards` retried with
   `collect_rewards_and_proceed` now uses the automatic behavior instead of the
   original explicit choice. Retrying with `resolve_rewards` is unaffected because
   the retry request carries the choice again. This is the accepted cost of
   removing the process-wide state and must be written in `docs/api.md`.
3. **Nothing else changes.** Within one drain the choice is still consumed once and
   the later screens fall back to the first card.

## Deliberately not changed

`_cardRewardSkipped` keeps its static lifetime. It carries an intent that must
outlive the request: after `skip_reward_cards` clicks the skip alternative, the
following `collect_rewards_and_proceed` must not re-open the card reward through
`TryGetNextClaimableRewardButton`'s `(!_cardRewardSkipped || button.Reward is not
CardReward)` filter. Threading it like the choice would need a per-request value
that the caller cannot supply, so it stays as is; its timeout-path staleness
(`GameActionService.cs:2417`) is recorded as a follow-up instead.

## Compatibility

- No action name, response field, error code, or HTTP route changes.
- `RewardChoicePolicy.Resolve` and the sentinels (`SkipChoice`, `AutoChoice`) are
  unchanged, so existing tests keep their meaning.

## Verification

- New unit tests for `RewardFlowChoiceState.ConsumePendingChoice` (explicit index
  once, then automatic) plus the existing `RewardChoicePolicyTests`.
- Source-contract tests: `GameActionService.cs` must not mention
  `_pendingCardRewardChoice`, must pass a choice into `DrainRewardFlowAsync`, and
  `ExecuteCollectRewardsAndProceedAsync` must pass `RewardChoicePolicy.AutoChoice`.
- `RewardFlowContractTests`, `GameActionTrustContractTests`, and the full offline
  sweep must stay green.

## Rollback

Single commit; reverting restores the static field.
