# Implement: screens and index spaces

1. `GameStateService.cs`
   - new `ResolveNonModalScreen` branches: `NFakeMerchant`, `NPatchNotesScreen`,
     `NInspectCardScreen`, `NInspectRelicScreen`, `NSendFeedbackScreen`;
   - `CanOpenShopInventory` + a shared "fake merchant button usable" helper;
   - `CanCloseMainMenuSubmenu` accepts patch notes;
   - `CanCloseCardsView` accepts the two inspect screens;
   - `ActionDescriptor` gains `requires_coordinates` / `requires_tool`, set on the
     two Crystal Sphere descriptors.
2. `GameActionService.cs`
   - `ResolveTimelineSlot` indexes the unfiltered list and validates the slot state;
   - `ExecuteOpenShopInventoryAsync` handles `NFakeMerchant`;
   - `ExecuteCloseMainMenuSubmenuAsync` handles `NPatchNotesScreen`;
   - `ExecuteCloseCardsViewAsync` handles the two inspect screens;
   - extend the two close waits as described in `design.md`.
3. Add `ScreenResolutionContractTests` and `TimelineIndexContractTests`; register
   them in `TestRunner.AllTests`.

## Validation

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
python scripts/check_verification_gates.py
```

## Review gates

- For every widened predicate, confirm the previously-matching set is unchanged
  (read the diff, not the intent).
- Confirm `docs/api.md` is untouched here: `09-12-docs-release-baseline` owns it.

## Rollback

Each of the five items is independent; partial reverts are safe.
