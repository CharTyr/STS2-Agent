---
name: sts2-mcp-player
description: Play or validate Slay the Spire 2 through the local sts2-ai-agent MCP tools. Use when navigating the main menu, timeline, map, combat, rewards, shops, rest sites, events, chests, card selections, potions, or debug-enabled test flows, and when another agent needs a strict state-first workflow that avoids stale indexes, invalid room actions, and tool-profile confusion.
---

# STS2 MCP Player

Use this skill when driving the STS2 MCP mod as a gameplay agent or when validating the live MCP contract against the running game.

## Quick Start

1. Call `health_check` once at session start.
2. Prefer the guided tool surface: `health_check`, `get_game_state`, `get_available_actions`, `act`.
3. Before every decision, call `get_game_state`.
4. Only invoke actions that are present in `available_actions`.
5. After every action, inspect the returned `state`; if needed, fetch fresh state again before the next step.
6. Recompute indexes from fresh payloads every time. Never reuse stale hand, node, reward, or selection indexes.

Do not trust memory over the current payload. The game mutates screens in place, overlays replace rooms, and some actions complete only after a follow-up state transition.

## Choose the Right Tool Surface

- Use the guided profile for normal play and most evaluations.
- Use legacy per-action tools only when a harness explicitly needs tool-by-tool coverage.
- Use `run_console_command` only in development flows where debug actions are enabled.

For validation flows, read [references/debug-and-validation.md](references/debug-and-validation.md).

## Non-Negotiable State Rules

- Treat `UNKNOWN` as transient only once. Re-read state once; if it persists, stop guessing and inspect the payload.
- Resolve overlays before room flow. `MODAL`, `CARD_SELECTION`, reward-card overlays, and timeline overlays take priority over map or combat planning.
- Treat `pending` responses as an instruction to stay inside the returned screen flow.
- Treat `proceed` as a room action, not a universal fallback.

## Screen Routing

- `MAIN_MENU`: prefer `continue_run`; if unavailable, finish timeline gates or start a run from `open_character_select`.
- `CHARACTER_SELECT`: choose an unlocked character, wait for `can_embark = true`, then `embark`.
- `MAP`: use `choose_map_node`.
- `COMBAT`: stay inside combat actions unless a selection overlay interrupts.
- `REWARD`: prefer `collect_rewards_and_proceed` unless making deliberate reward choices.
- `CARD_SELECTION`: finish the selection with `select_deck_card` and, when exposed, `confirm_selection`.
- `SHOP`: `open_shop_inventory` first, then buy/remove actions, then `close_shop_inventory`, then `proceed`.
- `REST`: use `choose_rest_option`; if selection opens, resolve it before `proceed`.
- `CHEST`: `open_chest -> choose_treasure_relic -> proceed`.
- `EVENT`: use `choose_event_option` even after combat returns to a finished event.

For detailed per-screen sequences and pitfalls, read [references/screen-playbooks.md](references/screen-playbooks.md).

## Common Pitfalls

- `skip_reward_cards` closes the overlay but may leave the underlying reward item claimable.
- Multi-select overlays may require `confirm_selection`; do not assume one click is enough.
- Potion targeting depends on `target_type`; some potions need no `target_index`.
- `shop.is_open = true` means inner inventory, not room completion.
- Timeline gates can block run start until the overlay is confirmed or the submenu is closed.

## Minimal Decision Heuristics

- In combat, spend energy efficiently and avoid ending turn with obvious free value unused.
- In rewards, take cards only when the upgrade is clear; otherwise skip.
- In shops, check relics and removal before committing all gold.
- In events, prefer unlocked options and re-read state after every branch.
