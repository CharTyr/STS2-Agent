# Evidence: the unscoped skip intent (2026-09-12)

Line numbers are from the tree at `d952a38`.

## The field and its sites

```text
GameActionService.cs:64-69   private static bool _cardRewardSkipped;   (doc: "Reset when leaving the reward screen")
1797  _cardRewardSkipped = request.option_index.Value == -1;   resolve_rewards
1812  _cardRewardSkipped = false;                               resolve_rewards pick
1817  _cardRewardSkipped = false;                               resolve_rewards auto
1978  _cardRewardSkipped = false;                               choose_reward_card
2008  _cardRewardSkipped = true;                                skip_reward_cards
2388  _cardRewardSkipped = false;                               drain: observed the reward screen was left
2507  (!_cardRewardSkipped || button.Reward is not CardReward)   read: the reward-button filter
2573  _cardRewardSkipped = true;                                drain: clicked the skip alternative
```

## Why it leaks

The only unconditional clear is `2388`, inside the branch that runs when the drain
observes a non-reward screen. Three other exits do not clear:

```text
2403  proceedButton.ForceClick(); return await WaitForRewardFlowExitAsync(...)
2408  if (await TryEscapeEmptyRewardsScreenAsync(...)) return true;
2412  return IsRewardFlowStable();
```

The drain's entry condition requires being on a reward screen
(`CanCollectRewardsAndProceed`, `GameStateService.cs:896-899` ⇒ `NRewardsScreen` or
`NCardRewardSelectionScreen`), so a normal call takes one of the other exits, and the
clear at `2388` only helps when the screen changes mid-drain and the loop comes back.

The read at `2507` filters the CardReward button for **whatever** reward screen is
being drained, so a leftover skip suppresses the card reward in a later reward set.
The documented flow makes this reachable: the playbook tells the agent to prefer
`collect_rewards_and_proceed` on `REWARD` and to re-read state after
`skip_reward_cards` (`skills/sts2-mcp-player/references/screen-playbooks.md:62-64`).

## Identity evidence (decompiled)

```text
extraction/decompiled/.../NCardRewardSelectionScreen.cs:139-151
  public static NCardRewardSelectionScreen? ShowScreen(options, extraOptions) {
      ... Instantiate ...
      NOverlayStack.Instance.Push(nCardRewardSelectionScreen);
      return ...;
  }

extraction/decompiled/.../NOverlayStack.cs:99-123   Push()
  Peek()?.AfterOverlayHidden();          // hides, does not remove
  this.AddChildSafely((Node)screen);
  _overlays.Add(screen);

extraction/decompiled/.../NOverlayStack.cs:125-159  Remove()
  _overlays.Remove(screen);              // only the owner closes it

extraction/decompiled/.../CardReward.cs:143-212
  OnSelect(): _currentlyShownScreen = NCardRewardSelectionScreen.ShowScreen(...);
              await _currentlyShownScreen.CardsSelected();
              NOverlayStack.Instance?.Remove(_currentlyShownScreen);
```

Consequence: while the selection overlay is open, the owning `NRewardsScreen` is still
a live child of the same overlay stack, so the selection screen's parent holds both.
That is the identity this task keys on, and it is also why the resolution can fail
(and must then fail safe).

Supporting facts:

- `skip_reward_cards` can only run on the selection screen:
  `CanSkipRewardCards` ⇒ `GetCardRewardAlternativeButtons` ⇒ returns empty unless the
  current screen is `NCardRewardSelectionScreen` (`GameStateService.cs:911-914`,
  `:1637-1642`).
- `GetInstanceId()` is already the file's identity idiom for reward nodes
  (`GameActionService.cs:2394`, `:2505`).
- `FindDescendants<T>` already exists for subtree walks
  (`GameStateService.cs:6780-6785`).

## Not verified

- The claim that a dropped card reward is unrecoverable is a game-side property;
  the mod's own escape path calls `TryEnableProceedButton` (`:2446`), i.e. the code
  assumes leaving with an unclaimed reward is possible.
- No live session: the overlay-stack shape is read from the decompiled sources, and
  the implementation must degrade to "do not honor the skip" if it does not hold.
