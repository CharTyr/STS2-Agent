# Evidence: screens and index spaces (2026-09-12)

## Implementation

| Item | Read side (`GameStateService.cs`) | Write side (`GameActionService.cs`) |
| --- | --- | --- |
| timeline index space | `BuildTimelinePayload` unchanged (full slot list, same `index`) | `ResolveTimelineSlot` indexes the same unfiltered list; a non-actionable slot is rejected before the click |
| Crystal Sphere descriptors | `ActionDescriptor` gained `requires_coordinates` / `requires_tool` | — |
| `FAKE_MERCHANT` | new screen mapping, `CanOpenShopInventory` fake-merchant branch + shared `GetFakeMerchantButton` guard | `ExecuteOpenShopInventoryAsync` clicks the merchant button for the event screen; `NMerchantRoom` keeps `OpenInventory()` |
| `PATCH_NOTES` | new mapping, `CanCloseMainMenuSubmenu` accepts it | `ExecuteCloseMainMenuSubmenuAsync` clicks `_backButton` or calls `Close`; a dedicated close wait |
| `CARD_INSPECT` / `RELIC_INSPECT` / `FEEDBACK` | new mappings, `CanCloseCardsView` accepts the two inspect screens | `ExecuteCloseCardsViewAsync` calls `Close()`; the cards-view branch is unchanged |

## Review round

The reviewer found and fixed two issues:

1. **A real dead end the first pass created.** `NMerchantButton.OnRelease` only
   plays dialogue when `IsLocalPlayerDead` is true and never emits
   `MerchantOpened`, so `ForceClick` cannot open the inventory. The new predicate
   advertised `open_shop_inventory` in that state, which is exactly the
   "action that cannot work" class this goal exists to remove. The shared guard
   now includes that flag and a test pins it.
2. The timeline contract test asserted only the text order inside
   `ResolveTimelineSlot`, so moving the click before the resolution would still
   have been green. It now asserts resolve-before-click in the handler.

It also verified that none of the three widened predicates changed their
pre-existing matching set (read line by line, not by intent) and that the new
close waits are not vacuously true.

## Commands actually run

```text
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release      -> 272 PASS / 0 FAIL
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental        -> 0 warnings / 0 errors
python scripts/check_verification_gates.py                                     -> passed (api-doc still 55 actions)
```

## Behavior differences worth documenting

- `NPatchNotesScreen` no longer reports `screen = "MAIN_MENU"`. Two string
  comparisons observe that: `CoopLaunchPolicy.GetError` now tells the player to
  return to the main menu while the patch-notes page is open (still correct, since
  `invite_ai_teammate` is only offered on `NMainMenu`), and
  `DualInstanceCoordinator` waits for `MAIN_MENU` without matching the patch-notes
  page. The page can only be opened by an explicit user click, and
  `close_main_menu_submenu` is now the documented way out of it.
- `/actions/available` gains `requires_coordinates` / `requires_tool` on every
  descriptor with a `false` default.

## Not verified

No live game session. All claims come from the decompiled game classes and the
mod's own predicates.
