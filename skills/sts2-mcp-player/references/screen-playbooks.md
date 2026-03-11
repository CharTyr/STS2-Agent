# Screen Playbooks

Use this reference when the active screen is clear and you need the exact action order or guardrails for that screen.

## MAIN_MENU and Timeline

- If `continue_run` is available, prefer it over starting a new run.
- If only `open_character_select` and `open_timeline` are available, there is no active run.
- If `open_timeline` is available and a run is blocked, finish the flow:
  - `open_timeline`
  - `choose_timeline_epoch`
  - `confirm_timeline_overlay`
  - `close_main_menu_submenu`
- `choose_timeline_epoch` should already return a state that exposes `confirm_timeline_overlay` when the overlay is ready.

## CHARACTER_SELECT

- Use the first unlocked character unless the task specifies otherwise.
- After `select_character`, wait for `character_select.can_embark = true`.
- `embark` can be a heavy transition. Prefer a longer request timeout and tolerate a short retry window.
- If a post-embark `MODAL` appears, resolve it before making any gameplay decision.

## MAP

- `map.available_nodes[]` is the only source of truth for valid node indexes.
- Recompute node indexes after every room transition.
- `choose_map_node` should not be considered done until the returned screen matches the destination room or stabilized combat entry.

## COMBAT

- Stay inside `play_card`, `end_turn`, `use_potion`, and `discard_potion`.
- If a card or potion opens `CARD_SELECTION`, immediately switch to selection flow.
- Do not call room actions from combat, even if you still remember the prior room.
- For unsupported ally-target cards, expect the payload to mark them unplayable instead of guessing targets.

## CARD_SELECTION

- Always read `selection.min_select`, `selection.max_select`, `selection.selected_count`, `selection.requires_confirmation`, and `selection.can_confirm`.
- Single-select flows usually end with `select_deck_card`.
- Multi-select flows may stay `pending` until `confirm_selection` becomes available.
- Card-selection variants are broader than deck remove and upgrade. Handle combat-hand overlays, transforms, enchants, and simple-grid selections the same way: trust the current selection payload.

## REWARD

- Prefer `collect_rewards_and_proceed` for hands-off reward cleanup.
- If `reward.pending_card_choice = true`, use `choose_reward_card` or `skip_reward_cards`.
- If `skip_reward_cards` closes only the overlay, re-read state to see whether the parent reward remains claimable.
- Do not use `proceed` on reward flows.
- `claim_reward` indexes refer to the reward payload's original entries, not a filtered list of claimable rewards.

## SHOP

- Enter the inventory with `open_shop_inventory`.
- While `shop.is_open = true`, use `buy_card`, `buy_relic`, `buy_potion`, and `remove_card_at_shop`.
- Leave inner inventory with `close_shop_inventory`.
- Leave the shop room with `proceed`.
- If potion slots are full, do not expect `buy_potion` to remain available.

## REST

- Use `choose_rest_option` on enabled entries only.
- If smithing or a relic option opens `CARD_SELECTION`, finish selection first, then `proceed`.

## CHEST

- `open_chest`
- `choose_treasure_relic`
- Wait until `chest.has_relic_been_claimed = true`
- `proceed`

## EVENT

- Use `choose_event_option` for both normal branches and finished synthetic proceed options.
- Expect event flows like `EVENT -> COMBAT -> EVENT` or `EVENT -> COMBAT -> MAP`.
- Re-read state after every branch because events mutate in place.

## MODAL and GAME_OVER

- Resolve `MODAL` before anything else with `confirm_modal` or `dismiss_modal`.
- On `GAME_OVER`, use `return_to_main_menu`.

## Potion Targeting

- `AnyEnemy`: requires `target_index`.
- `AnyPlayer`: does not require `target_index`.
- `TargetedNoCreature`: does not require `target_index`.
- If the payload marks a potion unusable, do not try to force it.
