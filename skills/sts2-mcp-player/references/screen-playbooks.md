# Screen Playbooks

Use this reference when the active screen is clear and you need the exact action order or guardrails for that screen.

## MAIN_MENU and Timeline

- If `continue_run` is available, prefer it over starting a new run.
- If only `open_character_select` and `open_timeline` are available, there is no active run.
- If `open_timeline` is available and a run is blocked, finish the flow:
  - `open_timeline`
  - `choose_timeline_epoch` on a slot whose compact `timeline.slots[].actionable` is `true` (compact slots expose only `i`, `line`, and `actionable`; the epoch state text is folded into `line`, while only raw state carries a separate `state`)
  - `confirm_timeline_overlay` or `confirm_unlock` until the overlay is gone
  - slot any newly granted epochs (slotting `NEOW_EPOCH` grants `SILENT1_EPOCH`)
  - `close_main_menu_submenu` only after unlock overlays are finished
- Character unlocks (`NUnlockCharacterScreen`) appear on the timeline after an epoch is slotted, not on `GAME_OVER`.
- The first timeline visit shows `NTimelineTutorial`. Screen is `TIMELINE` with `confirm_timeline_overlay`; do not `close_main_menu_submenu` until the tutorial and unlock overlays are done.
- `choose_timeline_epoch` should already return a state that exposes `confirm_timeline_overlay` or `UNLOCK` when the overlay is ready.

## CHARACTER_SELECT

- Use the first unlocked character unless the task specifies otherwise.
- After `select_character`, wait for `character_select.embark = true`.
- `embark` can be a heavy transition. Prefer a longer request timeout and tolerate a short retry window.
- If a `MODAL` appears after `open_character_select` or `embark`, resolve it before making any gameplay decision.

## BUNDLE_SELECTION

- Read `bundles[]` from compact state.
- `choose_bundle` with `option_index` from that list.
- When `confirm_bundle` is exposed, confirm before expecting map or combat.

## CAPSTONE_SELECTION

- Read `capstone.options[].i` and `line`.
- `choose_capstone_option` once per exposed option set.
- Re-read state; capstone can return to map, an event, or another overlay.

## MAP

- Compact state uses `map.options[].i` as the only legal node index; raw state uses `map.available_nodes[]`.
- Recompute node indexes after every room transition.
- In multiplayer, read `map.local_vote` and `map.votes`. If `local_vote` is already set, call `wait_until_actionable` instead of voting again. If another player has voted and you have not, follow that option.
- `choose_map_node` should not be considered done until the returned screen matches the destination room or stabilized combat entry.

## COMBAT

- Stay inside `play_card`, `end_turn`, `use_potion`, and `discard_potion`.
- If a card or potion opens `CARD_SELECTION`, immediately switch to selection flow.
- Do not call room actions from combat, even if you still remember the prior room.
- For unsupported ally-target cards, expect the payload to mark them unplayable instead of guessing targets.

## CARD_SELECTION

- Always read the compact selection fields `selection.min`, `selection.max`, `selection.selected`, and `selection.confirm`.
- The mod confirms a selection when the raw metadata has `CanConfirm` true and either `RequiresConfirmation` is true or `MinSelect < MaxSelect`; that is the rule behind compact `selection.confirm`, so a `min < max` multi-select can confirm even when `RequiresConfirmation` is false.
- Single-select flows usually end with `select_deck_card`.
- Multi-select flows may stay `pending` until `confirm_selection` becomes available.
- Card-selection variants are broader than deck remove and upgrade. Handle combat-hand overlays, transforms, enchants, and simple-grid selections the same way: trust the current selection payload.

## REWARD

- Prefer `collect_rewards_and_proceed` for hands-off reward cleanup. An empty rewards overlay with `can_proceed=false` is still that action; it should close the overlay instead of staying pending.
- If `reward.pending_card_choice = true`, use `choose_reward_card` or `skip_reward_cards`.
- If `skip_reward_cards` closes only the overlay, re-read state to see whether the parent reward remains claimable.
- Do not use `proceed` on reward flows.
- `claim_reward` indexes refer to the reward payload's original entries, not a filtered list of claimable rewards.

## SHOP

- Enter the inventory with `open_shop_inventory`.
- While `shop.open = true`, use `buy_card`, `buy_relic`, `buy_potion`, and `remove_card_at_shop`.
- Leave inner inventory with `close_shop_inventory`.
- Leave the shop room with `proceed`.
- If potion slots are full, do not expect `buy_potion` to remain available.

## REST

- Use `choose_rest_option` on enabled entries only.
- If smithing or a relic option opens `CARD_SELECTION`, finish selection first, then `proceed`.

## CHEST

- `open_chest`
- `choose_treasure_relic`
- Wait until `chest.claimed = true` (raw state spells it `chest.has_relic_been_claimed`)
- `proceed`

## EVENT

- Use `choose_event_option` for both normal branches and finished synthetic proceed options.
- Never send a locked option. Read `event.options`, skip the compact `locked=true` entries (raw state spells it `is_locked`), and use the first unlocked option `i`. Option 0 is often locked.
- Skip options marked compact `kill=true` (raw state spells it `will_kill_player`) unless the run is intentionally ending.
- `THE_ARCHITECT` EVENT `PROCEED` is lethal even with godmode. The `fight THE_ARCHITECT_EVENT_ENCOUNTER` debug shortcut is **external MCP only** (it needs `STS2_ENABLE_DEBUG_ACTIONS=1` plus the console/command path, which the in-game play loop does not have). In-game there is no shortcut into that encounter: resolve the event through its own unlocked, non-lethal options and never send the lethal `PROCEED` entry.
- `available_actions` can still contain `choose_event_option` when the first option is locked; that is not permission to pick index 0.
- Expect event flows like `EVENT -> COMBAT -> EVENT` or `EVENT -> COMBAT -> MAP`.
- Re-read state after every branch because events mutate in place.

## CRYSTAL_SPHERE

- Read `crystal_sphere.items` and `crystal_sphere.hidden_cells` from compact
  `get_game_state`; vision is not required.
- `crystal_clear_cell` requires `x` and `y`. Pass `tool="big"` for a
  3×3 clear or `tool="small"` for one cell; the tool can be switched atomically
  in the same `act` call.
- An item is revealed when all of its occupied cells are clear. Revealed bad
  items, including curses, are granted when the minigame ends, so do not complete
  their remaining hidden cells.
- Every divination must be spent. If no safe reward remains, spend a small
  divination on an already clear cell.
- After the last divination, resolve any reward child screens, then use
  `proceed` when it reappears.

## MODAL, GAME_OVER, and UNLOCK

- Resolve `MODAL` before anything else with `confirm_modal` or `dismiss_modal`. Relic/potion FTUEs listen to `Released`; if `confirm_modal` stays pending, retry once and keep waiting until the modal is gone.
- On `GAME_OVER`, use `continue_game_over` first so the native summary, score, and save flow runs. Wait while `game_over.phase=summary_animating`. Use `return_to_main_menu` only when `game_over.can_return` is true and the action is in `available_actions`. Death/victory summary itself does not show character unlocks.
- After returning to `MAIN_MENU`, open the timeline and slot obtained epochs. That is where `UNLOCK` / `confirm_unlock` appears.
- On `UNLOCK`, use `confirm_unlock` repeatedly until the unlock screen closes. Do not call `select_deck_card` or `return_to_main_menu` here.

## FAKE_MERCHANT

- The Fake Merchant event opens as screen `FAKE_MERCHANT`; it is not the normal `SHOP` room.
- `open_shop_inventory` opens its inventory, then the usual `buy_card` / `buy_relic` / `buy_potion` / `remove_card_at_shop` actions apply against compact `shop.open = true`.
- `close_shop_inventory` leaves the inventory and `proceed` leaves the event screen.

## PATCH_NOTES

- Patch notes appear as screen `PATCH_NOTES` from the main menu.
- `close_main_menu_submenu` closes them; its scope now covers the patch-notes view as well as the timeline submenu.
- Do not treat patch notes as a run-blocking gate: closing them is enough.

## CARD_INSPECT and RELIC_INSPECT

- `CARD_INSPECT` and `RELIC_INSPECT` are inspect overlays that can cover a room or a menu.
- `close_cards_view` closes them; its scope now covers the plain card list plus both inspect overlays.
- Resolve them before choosing a room action, then re-read state because the underlying screen resumes underneath.

## FEEDBACK

- Screen `FEEDBACK` is the feedback form.
- No mod action closes it yet, so it is user-triggered only: do not enter it during autonomous play, and if the player opened it, wait instead of guessing an action.

## Potion Targeting

- `AnyEnemy`: requires `target_index`.
- `AnyPlayer`: does not require `target_index`.
- `TargetedNoCreature`: does not require `target_index`.
- If the payload marks a potion unusable, do not try to force it.
