# Design: screens and index spaces

## 1. Timeline epoch indexing

State side (unchanged, reference only): `GameStateService.BuildTimelinePayload`
(`:4614-4623`) numbers the **full** slot list —
`GetTimelineSlots(currentScreen).Select((slot, index) => new TimelineSlotPayload { index = index, ..., is_actionable = slot.State is Obtained or Complete })`.

`GameStateService.cs:3370-3389` copies `i = slot.index` into the compact view.

Executor side (change):

```csharp
// before: filter to Obtained/Complete, then index -> two index spaces
private static NEpochSlot ResolveTimelineSlot(IScreenContext? currentScreen, int optionIndex)
```

New behavior:

- index the unfiltered `GetTimelineSlots(currentScreen)` array;
- out of range ⇒ `409 invalid_target` with `option_index`, `slot_count`;
- slot state not `Obtained`/`Complete` ⇒ `409 invalid_target` with
  `slot_state`, `is_actionable = false`,
  `option_index_space = "timeline.slots[].index"`.

This keeps the exposed index authoritative and removes the silent
different-epoch risk (previously index 2 could mean the third *actionable* slot).

## 2. Crystal Sphere descriptor

`ActionDescriptor` (`GameStateService.cs:7982-7989`) gains two additive members:

```csharp
public bool requires_coordinates { get; init; }
public bool requires_tool { get; init; }
```

Set `requires_coordinates = true` for `crystal_clear_cell` (descriptor at
`:522-527`) and `requires_tool = true` for `crystal_set_tool` (`:516-521`).
Every other descriptor keeps its current values, so the only visible change is
that these two stop under-reporting. `/actions/available` is the only route that
serializes descriptors; the compact `actions` list stays a plain name list.

## 3. FAKE_MERCHANT

Decompiled facts (`extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Events.Custom/NFakeMerchant.cs`):
`public NMerchantButton MerchantButton { get; private set; }` (`:97`, bound to
`%MerchantButton` `:127`), `_inventory = GetNode<NMerchantInventory>("%Inventory")`
(`:144`), and `CurrentScreenContext` returns `_inventory` once
`_inventory.IsOpen` (`:99-109`). The merchant button is hidden when the event
already fought (`:128-130`).

Changes:

- `GameStateService.ResolveNonModalScreen`: new branch
  `NFakeMerchant => "FAKE_MERCHANT"` before the `UNKNOWN` fallback
  (`:6640-6700`).
- `CanOpenShopInventory` (`:1226-1230`): accept `NMerchantRoom` as today, and
  `NFakeMerchant fake` when
  `fake.MerchantButton is { } button && GodotObject.IsInstanceValid(button) && button.IsVisibleInTree() && button.IsEnabled`
  and the inventory is not already open. Factor the predicate into a small
  helper so the executor and the predicate cannot drift.
- `ExecuteOpenShopInventoryAsync` (`GameActionService.cs:3485-3510`): replace the
  `currentScreen is not NMerchantRoom` requirement with a switch that calls
  `merchantRoom.OpenInventory()` for `NMerchantRoom` and
  `fakeMerchant.MerchantButton.ForceClick()` for `NFakeMerchant`; the existing
  `WaitForShopInventoryOpenAsync` (`:4822`) already accepts "current screen is an
  open `NMerchantInventory`", which is exactly what the fake merchant's
  `CurrentScreenContext` reports once open.
- Buying and closing need no change: after opening, the active screen is an
  `NMerchantInventory` and the existing shop actions apply
  (`NMerchantInventory` has `IsOpen`, `Close`, and `_backButton`).

## 4. PATCH_NOTES

Decompiled facts (`extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.MainMenu/NPatchNotesScreen.cs`):
public `Open()` (`:195`), private `Close()` registered as the Godot method
`Close` (`:217`, `MethodName.Close` at `:34`), buttons `%BackButton` and
`%PatchNotesToggle` (`:129-139`).

Changes:

- `ResolveNonModalScreen`: `NPatchNotesScreen => "PATCH_NOTES"` replacing the
  `MAIN_MENU` mapping at `:6694`.
- `CanCloseMainMenuSubmenu` (`:1383-1392`): accept `NPatchNotesScreen notes`
  when the screen is visible, as an alternative to the `NSubmenu` branch.
- `ExecuteCloseMainMenuSubmenuAsync` (`GameActionService.cs:585-611`): branch on
  the screen — `NPatchNotesScreen` ⇒ `GetPrivateField<NButton>(notes, "_backButton")`
  click when usable, else `((Node)notes).Call("Close")`; keep the submenu-stack
  path for `NSubmenu`. The close wait must also accept "the patch-notes screen is
  gone or no longer visible" (extend `WaitForMainMenuSubmenuCloseAsync` or add a
  sibling wait; do not weaken the existing condition).

## 5. Inspect overlays

Decompiled facts: `NInspectCardScreen` (`...Nodes.Screens/NInspectCardScreen.cs`)
`public void Close()` at `:202`; `NInspectRelicScreen`
(`...Nodes.Screens.InspectScreens/NInspectRelicScreen.cs`) `public void Close()`
at `:356`; `NSendFeedbackScreen` (`...Nodes.Screens.FeedbackScreen/NSendFeedbackScreen.cs`)
private `Close()` at `:324` registered as `MethodName.Close` (`:55`) plus a
`BackButton` (`:195`).

Changes:

- `ResolveNonModalScreen`: `NInspectCardScreen => "CARD_INSPECT"`,
  `NInspectRelicScreen => "RELIC_INSPECT"`, `NSendFeedbackScreen => "FEEDBACK"`
  before the `UNKNOWN` fallback.
- `CanCloseCardsView` (`:921-924`): also true for the two inspect screens.
- `ExecuteCloseCardsViewAsync` (`GameActionService.cs:2030-2060`): branch —
  inspect screens call their `Close()`; `NCardsViewScreen` keeps the existing
  back-button path. Extend `WaitForCardsViewCloseAsync` so an inspect overlay
  counts as closed when the current screen is no longer the inspect screen.

## Compatibility

- New screen strings are additive; every existing screen string is unchanged.
- Two descriptor fields are additive with `false` defaults.
- Widened predicates only add screens that previously had no action at all.

## Verification

- New tests: `ScreenResolutionContractTests` (the five new mappings + no change to
  the existing ones) and `TimelineIndexContractTests` (index space + the
  actionable guard + the descriptor flags), registered in `TestRunner.AllTests`.
- Full C# suite; verification gates (the action contract in `docs/api.md` is
  unaffected because no action name changes).

## Rollback

Single commit; each of the five items is independent.
