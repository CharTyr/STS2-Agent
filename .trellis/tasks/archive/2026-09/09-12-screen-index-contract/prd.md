# Close screen and index dead ends

Parent: `09-12-agent-trust-hardening` (goal 4 of 5).

## Goal

Two ways an agent gets stuck without a wrong answer: an index space that the
state and the executor disagree about, and screens where the mod exposes no
usable action. One item is a silent mis-target risk, the others are dead ends.

## Requirements

1. **Timeline epoch index space.** `timeline.slots[].index` (raw) / `i` (compact) is
   the slot's position in the full slot list, but
   `GameActionService.ResolveTimelineSlot` matches `option_index` against a
   list filtered to obtained/complete slots. Passing the index the state printed
   can therefore 409 or click a *different* epoch. The executor must index the
   same list the state exposes and reject non-actionable slots explicitly.
2. **`crystal_clear_cell` descriptor.** It declares
   `requires_index = false, requires_target = false` while requiring `x`/`y`
   (and optionally `tool`). The descriptor must carry that requirement so an
   agent reading `/actions/available` is not misled.
3. **Fake Merchant screen.** `NFakeMerchant` resolves to `UNKNOWN`, and
   `open_shop_inventory` only accepts `NMerchantRoom`, so the event's shop is
   unreachable even though the screen has a `MerchantButton` and an inventory.
   Resolve it as `FAKE_MERCHANT` and let `open_shop_inventory` open it.
4. **Patch notes screen.** `NPatchNotesScreen` resolves to `MAIN_MENU` with zero
   available actions, so the agent sits on an actionable screen with nothing to
   do. Resolve it as `PATCH_NOTES` and let the existing
   `close_main_menu_submenu` close it.
5. **Inspect overlays.** `NInspectCardScreen` / `NInspectRelicScreen` resolve to
   `UNKNOWN`; both have a public `Close()`. Resolve them as `CARD_INSPECT` /
   `RELIC_INSPECT` and let the existing `close_cards_view` close them.
   `NSendFeedbackScreen` resolves as `FEEDBACK` for diagnostics and is
   documented as not closable by a mod action.

## Acceptance Criteria

- [x] `choose_timeline_epoch` accepts exactly the `timeline.slots[].index` the same response printed; a non-actionable slot returns `409 invalid_target` with `is_actionable: false` and the slot's state, thrown before the click.
- [x] `/actions/available` reports `crystal_clear_cell` with `requires_coordinates` and `crystal_set_tool` with `requires_tool`; no other descriptor value changed.
- [x] On `FAKE_MERCHANT`, `open_shop_inventory` clicks the merchant button and normal shop actions then apply; `proceed` still leaves the event. The predicate is false when the merchant button cannot actually open the inventory (hidden, disabled, or the local player is dead).
- [x] On `PATCH_NOTES`, `close_main_menu_submenu` is available and closes the screen; `screen` reads `PATCH_NOTES`, and an ineffective close reports `pending` rather than `completed`.
- [x] On `CARD_INSPECT` / `RELIC_INSPECT`, `close_cards_view` is available and calls the screen's `Close()`.
- [x] The five new screen names are the exact strings above; the sibling skill task already documents the same names.
- [x] Contract tests pin each new screen mapping, each widened predicate, and the resolve-before-click order; the full C# suite passes (272 PASS / 0 FAIL).
- [x] No existing action name, response field, or descriptor value is removed.

## Constraints

- Offline only: the host game cannot be driven. Every claim here is a code
  contract claim; the cards say so.
- `GameStateService` stays the read side (resolution, predicates) and
  `GameActionService` the write side (clicks, waits), per the layer spec.
- `09-12-docs-release-baseline` owns every `docs/` edit for this work.

## Notes

- Evidence: `research/screen-index-audit.md`; the decompiled game classes are
  under `extraction/decompiled/` and were used to confirm the close/open hooks.
- Verification record: `evidence.md`.
- `/actions/available` now carries `requires_coordinates` / `requires_tool` on every
  descriptor (default `false`); `09-12-docs-release-baseline` documents them.
