# Audit evidence: action trust (2026-09-12)

Source: read-only scout pass over `STS2AIAgent/Game/GameActionService.cs` (5990
lines) plus the surrounding state/transport code. Line numbers are from the
`main` tree at the time of the audit (HEAD `608c583`).

## Defect A2 — out-of-range card index silently picks the first card

Store side:

```text
GameActionService.cs:1695-1717
  if (request.option_index.HasValue) {
      if (request.option_index.Value == -1) { _pendingCardRewardChoice = -2; ... }
      else { _pendingCardRewardChoice = request.option_index.Value; _cardRewardSkipped = false; }
  }
  else if (request.card_index.HasValue) { _pendingCardRewardChoice = request.card_index.Value; ... }
```

Only `-1` is special-cased; `-5` or `99` is stored as-is. Consume side:

```text
GameActionService.cs:2408-2420
  var options = GameStateService.GetCardRewardOptions(cardRewardScreen);
  if (_pendingCardRewardChoice >= 0 && _pendingCardRewardChoice < options.Count) {
      selected = options[_pendingCardRewardChoice]; _pendingCardRewardChoice = -1;
  } else {
      selected = options.FirstOrDefault();          // <-- silent fallback
  }
```

The response is `status = stable ? "completed" : "pending"` (`:1721-1728`), so a
caller that asked for the third card can be told the reward flow completed while
the mod took the first card.

Sentinel meaning is documented at `:79-84`: `-2` skip, `-1` no pending choice.

## Defect A1 — an open modal counts as a completed transition

```text
GameActionService.cs:5156-5175  WaitForMainMenuExitAsync   (continue_run, caller :894)
  if (GameStateService.GetOpenModal() != null) { return true; }
GameActionService.cs:5331-5352  WaitForEmbarkTransitionAsync (embark, caller :3963)
  if (GameStateService.GetOpenModal() != null) { return true; }
GameActionService.cs:5074-5083  IsCharacterSelectOpenOrActionableModal (open_character_select, wait :5059-5072, caller :540)
  if (currentScreen is NCharacterSelectScreen) { return true; }
  return GameStateService.CanConfirmModal(currentScreen) || GameStateService.CanDismissModal(currentScreen);
```

An error dialog, a save dialog, or any other modal therefore produces
`status="completed"` while the game is still on the menu / not in character
select. Callers then act on a state that never changed.

Note for the fix: MODAL is itself a resolved screen
(`GameStateService.cs:777-782`), and the modal actions are exposed there, so an
honest `pending` is actionable for the caller.

## Defect A5 — a failed background purchase is swallowed

```text
GameActionService.cs:3793-3796
  // Fire-and-forget: merchant card removal opens deck selection and blocks
  // until the player confirms a card. Do not await the full task here.
  ObserveBackgroundResult(entry.OnTryPurchaseWrapper(inventory), "remove_card_at_shop");
  var stable = await WaitForShopCardRemovalTransitionAsync(TimeSpan.FromSeconds(10));
```

`ObserveBackgroundResultCore` (`:5927-5941`) only logs:
`Log.Warn(... "returned false")` / `Log.Error(...)`. The action returns
`pending` either way, so "you cannot afford this" looks like "still
transitioning" and the caller retries the same action.

Contrast: `buy_card` checks the same purchase call in-line and throws
`409 invalid_action` on `false` (`:3596-3606`).

## Defect B2 — bundle actions can report completed with an empty state

```text
GameActionService.cs:3140-3149 (choose_bundle) and :3214-3223 (confirm_bundle)
  GameStatePayload? state = null;
  try { state = GameStateService.BuildStatePayload(); } catch { }
  ...
      status = stable ? "completed" : "pending",
      state = state ?? new GameStatePayload()
```

A snapshot failure turns into a default object while the status still says
`completed`, so a caller reading `state.screen` gets a meaningless value.

## Defect A6 — play_card counters survive a play that never happened

```text
GameActionService.cs:465-472
  var currentTurn = combatState?.RoundNumber ?? 0;
  SyncCardPlayCounters(currentTurn);
  CardsPlayedThisTurn++;
  var cardType = card.Type.ToString();
  if (cardType == "Attack") AttacksPlayedThisTurn++;
  else if (cardType == "Skill") SkillsPlayedThisTurn++;
  var stable = await WaitForPlayCardTransitionAsync(card, TimeSpan.FromSeconds(12));
```

The counters feed the public state (`GameStateService.cs:2757`) and the
turn-ready check (`GameStateService.cs:2263-2267`:
`Hand.Cards.Count == 0 && CardsPlayedThisTurn == 0 ⇒ not ready`).
`IsPlayCardStable` returns false while `card.Pile?.Type == PileType.Hand`
(`GameActionService.cs:1116-1119`), so "card still in hand" is exactly the
observable failure case the rollback must use.

## Verification surface that exists today

- `STS2AIAgent.Tests` compiles selected production files
  (`STS2AIAgent.Tests.csproj:11-47`) and runs a custom runner
  (`TestRunner.AllTests`). Pure policy classes are the established pattern:
  `CrystalSphereSettlePolicy`, `UnlockConfirmResolutionPolicy`,
  `ProgressSaveVerification`, `StopKindPolicy`.
- `GameActionService.cs` itself is only covered by source-text contract tests
  (`UnlockScreenContractTests`, `MapCombatGatingTests`,
  `DeckSelectionContractTests`, ...), which is why the new logic is extracted
  into policy classes with real assertions.
- Offline sweep: `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release`
  (232 tests at audit time), `uv run --locked python -m unittest discover -s tests`
  (55 tests), `python scripts/check_verification_gates.py`,
  `scripts/preflight-release.ps1`.

## Out of scope here

- Unbounded waits (defect A4) and the timeline index space (A3) belong to the
  sibling tasks `09-12-bounded-game-waits` and `09-12-screen-index-contract`.
- No live-game check was performed; all evidence is code-level.
