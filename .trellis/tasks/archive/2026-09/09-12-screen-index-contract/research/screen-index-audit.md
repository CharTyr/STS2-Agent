# Audit evidence: screens and index spaces (2026-09-12)

## A3 — timeline epoch index space

State exposes the **unfiltered** slot list with its own indexes:

```text
GameStateService.cs:4614-4623   BuildTimelinePayload
  var slots = GetTimelineSlots(currentScreen)
      .Select((slot, index) => new TimelineSlotPayload {
          index = index,
          epoch_id = slot.model.Id,
          state = slot.State.ToString().ToLowerInvariant(),
          is_actionable = slot.State is EpochSlotState.Obtained or EpochSlotState.Complete })
      .ToArray();
GameStateService.cs:3382-3387   compact view: i = slot.index, line, actionable
```

The executor indexes a filtered list:

```text
GameActionService.cs:5041-5057  ResolveTimelineSlot
  var slots = GameStateService.GetTimelineSlots(currentScreen)
      .Where(slot => slot.State is EpochSlotState.Obtained or EpochSlotState.Complete)
      .ToArray();
  if (optionIndex < 0 || optionIndex >= slots.Length) { throw new ApiException(409, "invalid_target", ...); }
  return slots[optionIndex];
```

Consequence: with one non-actionable slot before the target, the index the state
printed either 409s or selects a different epoch.

## E1#5 — crystal descriptors under-report

```text
GameStateService.cs:516-527
  crystal_set_tool   requires_target = false, requires_index = false
  crystal_clear_cell requires_target = false, requires_index = false
GameActionService.cs:1583-1590  crystal_clear_cell: x/y required
GameActionService.cs:1606-1617  optional tool switch
GameActionService.cs:1547, 1634-1648  crystal_set_tool: tool = "big" | "small"
GameStateService.cs:7982-7989   ActionDescriptor { name, requires_target, requires_index }
```

## E1#1 — Fake Merchant event screen is a dead end

```text
ActiveScreenContext returns NFakeMerchant (ActiveScreenContext.cs:99-109)
GameStateService.ResolveNonModalScreen has no NFakeMerchant branch -> "UNKNOWN"
GameStateService.cs:1226-1230  CanOpenShopInventory requires currentScreen is NMerchantRoom
GameStateService.cs:5831-5839  GetMerchantRoom: NMerchantRoom | NMerchantInventory
GameActionService.cs:3490      ExecuteOpenShopInventoryAsync requires NMerchantRoom
```

The event exists in the packaged data (`mcp_server/data/eng/events.json:1126`), and
the screen really does have a working shop (see the decompiled class facts in
`design.md` section 3). Today only `proceed` is exposed, so the shop content is
unreachable for the agent.

## E1#2 — patch notes map to MAIN_MENU with no actions

```text
GameStateService.cs:6694   NPatchNotesScreen => "MAIN_MENU"
NPatchNotesScreen : Control, IScreenContext  (not NMainMenu / NSubmenu)
-> every Can* that gates on NMainMenu or NSubmenu is false, so available_actions is empty
```

## E1#3 — inspect overlays resolve to UNKNOWN

```text
ActiveScreenContext.cs:29-44   NInspectCardScreen / NInspectRelicScreen / NSendFeedbackScreen
GameStateService.cs:6698       _ => "UNKNOWN"
-> running screens expose only save_and_quit, which is a passive action, so
   wait_until_actionable never returns true (GameBridge.cs:10-14, 186-187)
```

Decompiled close hooks: `NInspectCardScreen.Close()` (public, `:202`),
`NInspectRelicScreen.Close()` (public, `:356`),
`NSendFeedbackScreen.Close()` (private, Godot-registered `:55`/`:324`).

## Not verified here

- No live game run; screen behaviour is inferred from the decompiled game code
  and the mod's own predicates.
- `NTreasureRoomRelicCollection` with zero relics and an all-locked event are
  listed by the audit as *possible* dead ends; they are left alone because the
  audit could not establish that they become the active screen.
