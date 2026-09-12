# Design: action trust

## Boundary

All changes are inside `STS2AIAgent/Game/`. `GameActionService.cs` keeps
owning native calls and transition waits; the *decisions* move into pure
classes that the offline test assembly can compile, following the existing
`CrystalSphereSettlePolicy` / `UnlockConfirmResolutionPolicy` /
`ProgressSaveVerification` pattern. `GameActionService.cs` is not in the test
`Compile` list, so nothing that touches game types can be unit tested.

## 1. Reward choice resolution

New: `STS2AIAgent/Game/RewardChoicePolicy.cs`

```csharp
internal enum RewardChoiceKind { Auto, Skip, Pick }
internal readonly record struct RewardChoiceResolution(
    RewardChoiceKind Kind, int Index, bool IsValid, string? Reason);

internal static class RewardChoicePolicy
{
    // pending: -2 skip, -1 auto (first card), >= 0 explicit index
    public static RewardChoiceResolution Resolve(int pending, int optionCount);
}
```

Rules:

| pending | optionCount | result |
| --- | --- | --- |
| -2 | any | `Skip` |
| -1 | > 0 | `Auto` |
| -1 | 0 | invalid ("no card reward options") |
| >= 0 | index < count | `Pick(index)` |
| >= 0 | index >= count | invalid ("option_index is out of range") |

`GameActionService`:

- request time: if a card reward screen is already open, resolve against its
  option count and throw `ApiException(409, "invalid_target", ...)` when
  invalid; store the pending value only when it is valid or "no choice given".
- consume time (`TryResolveCardRewardAsync`): resolve again against the live
  option list. `Pick` uses that index, `Skip` clicks a skip alternative,
  `Auto` keeps today's first-option behavior, invalid throws 409 instead of
  falling back to `options.FirstOrDefault()`.

## 2. Modal is not a transition

New: `STS2AIAgent/Game/MenuTransitionPolicy.cs`

```csharp
internal static class MenuTransitionPolicy
{
    public static bool IsMenuExited(bool menuScreenStillCurrent, bool modalOpen, bool resolvedScreenUnknown);
    public static bool IsEmbarkSettled(bool multiplayerReady, bool menuScreenStillCurrent, bool modalOpen, bool resolvedScreenUnknown);
    public static bool IsCharacterSelectSettled(bool characterSelectScreenVisible);
}
```

- `IsMenuExited` = `!menuScreenStillCurrent && !modalOpen && !resolvedScreenUnknown`.
- `IsEmbarkSettled` = `multiplayerReady || IsMenuExited(...)`.
- `IsCharacterSelectSettled` = the character-select screen is actually the
  current screen; a modal no longer counts.

`GameActionService`: `WaitForMainMenuExitAsync`, `WaitForEmbarkTransitionAsync`
and `IsCharacterSelectOpenOrActionableModal` delegate to the policy. When the
wait ends unsettled, the response message names the blocking modal so the caller
knows to run `confirm_modal` / `dismiss_modal` from the fresh state.

Note: this can only turn a false `completed` into an honest `pending`; it never
blocks a transition that used to succeed, because the removed branch returned
true *only* when a modal was open, and an open modal is itself the MODAL screen.

## 3. Background purchase outcome

New: `STS2AIAgent/Game/BackgroundTaskOutcome.cs`

```csharp
internal static class BackgroundTaskOutcome
{
    public static string? DescribeFailure(bool isCompleted, bool isFaulted, bool isCanceled, bool? result);
}
```

Returns a human-readable reason for a finished-but-failed task, `null` when the
task is still running or succeeded.

`remove_card_at_shop`: keep `ObserveBackgroundResult` for the success path, but
before returning, if the transition did not settle and the purchase task has
already completed, throw `ApiException(409, "invalid_action", ...)` with the
reported reason. The success path (task still open on deck selection) is
unchanged.

## 4. Bundle state snapshot

`choose_bundle` / `confirm_bundle`: replace `catch { } ... ?? new GameStatePayload()`
with an explicit `ApiException(503, "state_unavailable", ...)` when the snapshot
cannot be built. An action that reports `completed` must carry a real state.

## 5. Card-play counter honesty

New: `STS2AIAgent/Game/CardPlayCounterPolicy.cs`

```csharp
internal static class CardPlayCounterPolicy
{
    public static bool ShouldRollBack(bool playSettled, bool combatInProgress, bool cardStillInHand);
}
```

`= !playSettled && combatInProgress && cardStillInHand`.

`ExecutePlayCardAsync` keeps the optimistic increment (the state should reflect
an in-flight play) and rolls `CardsPlayedThisTurn` / the attack or skill counter
back through a new `RollBackCardPlayCounters(string cardType)` when the policy
says the play never left the hand.

## Compatibility

- No response field is removed or renamed.
- `resolve_rewards` keeps `option_index: -1` = skip, absent = first card, and
  the `card_index` alias.
- New policy types are `internal` and must be added to
  `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`.

## Verification

- New C# tests: `RewardChoicePolicyTests`, `MenuTransitionPolicyTests`,
  `BackgroundTaskOutcomeTests`, `CardPlayCounterPolicyTests`, registered in
  `TestRunner.AllTests`.
- Source-contract test: `GameActionService` no longer contains the three
  "modal means done" shapes and no longer has the `?? new GameStatePayload()`
  bundle fallback.
- Existing suites: C# runner, MCP unittest, verification gates, preflight.

## Rollback

Revert the single commit for this child; each policy file is standalone and no
other child depends on it.
