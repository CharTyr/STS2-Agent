---
name: sts2-mcp-player
description: Play or validate Slay the Spire 2 through the local sts2-ai-agent MCP tools, including access to live game metadata from the Mod API (cards/relics/monsters/etc.) for richer decision-making. Use when navigating the main menu, timeline, map, combat, rewards, shops, rest sites, events, chests, card selections, potions, or debug-enabled test flows, and when another agent needs a strict state-first workflow that avoids stale indexes, invalid room actions, and tool-profile confusion.
---

# STS2 MCP Player

Use this skill when driving the STS2 MCP mod as a gameplay agent or when validating the live MCP contract against the running game.

## Server Names

This skill is transport-agnostic. It only assumes that your host agent exposes one active STS2 MCP server with the standard tool surface.

- Recommended local server name (`local_server_name`): `sts2-ai-agent`
- Recommended remote server name (`remote_server_name`): `sts2-ai-agent-remote`
- Runtime rule: always try `local_server_name` first, then `remote_server_name`.
- If your host already exposes STS2 tools directly, call tools directly and skip MCP config troubleshooting.

## Recommended SubAgent Config

Use a conservative SubAgent profile for STS2. The goal is to keep the tool surface small, keep one MCP action per iteration, and avoid polluting the main chat prompt with long-running game state.

- Recommended plugin settings: `max_concurrent = 1`, `auto_discover = false`, `broadcast_iteration_progress = false`, `inject_status_to_main_prompt = false`
- Recommended retention settings: `inject_completed_for_seconds = 120`, `status_retention_seconds = 900`
- Recommended skill settings: `allowed_tool_names = ["health_check", "get_game_state", "get_raw_game_state", "get_available_actions", "act", "get_game_data_item", "get_game_data_items", "get_relevant_game_data", "wait_for_event", "wait_until_actionable"]`, `max_mcp_tools_per_iteration = 1`, `share_to_main_chat = false`

### Simplified Config

If your host plugin supports the built-in STS2 shortcut section, prefer this minimal config over a long JSON blob:

```toml
[sts2]
enabled = true
local_server_name = "sts2-ai-agent"
remote_server_name = "sts2-ai-agent-remote"
```

This skill does not require a long MCP config walkthrough. At runtime, simply follow local-first fallback:

1. Use `local_server_name` first.
2. If local is unavailable, retry the same flow on `remote_server_name`.
3. If aliases are unavailable but STS2 tools are already exposed, call the tools directly.

For an optional skill-local remote checklist, read [references/remote-connection.md](references/remote-connection.md).

The in-game overlay agent loads the shared play contract below plus references/screen-playbooks.md as its system prompt. Follow those same documents. Do not invent a parallel workflow.

<!-- BEGIN SHARED PLAY CONTRACT -->

## Quick Start

1. Connection checking belongs to the external MCP client or orchestrator that launched the session; the in-game play loop has no `health_check`, so it reads live state instead.
2. Prefer the guided decision loop: `get_game_state -> get_available_actions -> act`.
   Use `wait_until_actionable` across animations and screen changes. Use `get_raw_game_state` only if compact state is missing a needed field.
3. For cards, monsters, relics, potions, shop items, and event options, prioritize game-data tools before using memory:
   `get_relevant_game_data` (default, scene-aware minimal context) ->
   `get_game_data_item` (single-entity lookup) ->
   `get_game_data_items` (batch compare/filter).
   The compact view carries the ids those lookups key on: `combat.hand[].card_id`,
   `combat.enemies[].enemy_id`, `run.deck[].card_ids`, `run.relic_ids`,
   `selection.cards[].card_id`, `reward.cards[].card_id`, `shop.cards[].card_id`,
   `bundles[].cards[].card_id`, and `chest.relics[].relic_id`.
4. Before every decision, call `get_game_state`.
5. Route by `state.session` first:
   `session.mode` distinguishes `singleplayer` vs `multiplayer`, and
   `session.phase` distinguishes `menu`, `character_select`,
   `multiplayer_lobby`, and `run`.
6. Only invoke actions that are present in `available_actions`.
7. After every action, inspect the returned `state`; if needed, fetch fresh state again before the next step.
8. Treat multiplayer as local-player control only. Never invent teammate actions that are not present in the latest state.
9. Recompute indexes from fresh payloads every time. Never reuse stale hand, node, reward, or selection indexes.

Do not trust memory over the current payload. The game mutates screens in place, overlays replace rooms, and some actions complete only after a follow-up state transition.

## Game Data Priority Rules

- Never guess static game facts (card text, potion targeting, monster metadata, relic effects, event option details) from memory when game-data tools are available.
- Use `get_relevant_game_data` first for current-scene context in combat/shop/event/menu flows.
- Use `get_game_data_item` when you need deep details for one entity id.
- Use `get_game_data_items` when comparing multiple entities (for example, reward-card choices, shop candidates, potion options).
- If state and metadata disagree, trust live state for legality and metadata for semantics; then re-read state.

## Non-Negotiable State Rules

- Treat `UNKNOWN` as transient only once. Re-read state once; if it persists, stop guessing and inspect the payload.
- Treat `state.session` as the source of truth for singleplayer vs multiplayer. Do not infer mode from screen names or tool names alone.
- Resolve overlays before room flow. `MODAL`, `CARD_SELECTION`, reward-card overlays, and timeline overlays take priority over map or combat planning; `modal.underlying_screen` names the room underneath.
- Treat `pending` responses as an instruction to stay inside the returned screen flow.
- Treat `proceed` as a room action, not a universal fallback.

## Action Failure Rules

- A failed action (or failed game-data call) returns an `error` object. Read `error.code` before deciding what to do: `invalid_request`, `invalid_action`, and `invalid_target` mean the request itself was wrong, so re-read state and fix the action or its indexes instead of repeating it.
- Retry only when `error.retryable` is `true` (for example `state_unavailable` while a transition is still settling). When it is false, change your approach rather than retrying the same call.
- `error.status_code` mirrors the API status when present; `internal_error` means an unexpected failure, not a legal move you can reach by retrying.

## Screen Routing

- `MAIN_MENU`: prefer `continue_run`; if unavailable, finish timeline gates (slot obtained epochs, confirm unlock overlays) or start a run from `open_character_select`. Character unlocks happen on the timeline, not on `GAME_OVER`. Do not call `switch_profile` unless asked; `option_index` is the native profile id 1..3.
- `CHARACTER_SELECT`: choose an unlocked character, wait for `character_select.embark = true`, then `embark`.
- `MULTIPLAYER_LOBBY`: stay on the same compact tool surface; use `available_actions` for `host_multiplayer_lobby`, `join_multiplayer_lobby`, `select_character`, `ready_multiplayer_lobby`, or `disconnect_multiplayer_lobby`.
- `MAP`: use `choose_map_node` with `map.options[].i`. In multiplayer, if `map.local_vote` is set, `wait_until_actionable` instead of voting again; if `map.votes` exist and you have not voted, follow that option.
- `COMBAT`: stay inside combat actions unless a selection overlay interrupts. Budget block and pick targets from `combat.enemies[].intents[]` (`damage` / `hits` / `total_damage`) and both sides' `powers` lines, not from `lethal_risks` alone.
- `REWARD`: prefer `collect_rewards_and_proceed` unless making deliberate reward choices.
- `CARD_SELECTION`: finish the selection with `select_deck_card` and, when exposed, `confirm_selection`.
- `SHOP`: `open_shop_inventory` first, then buy/remove actions, then `close_shop_inventory`, then `proceed`.
- `REST`: use `choose_rest_option`; if selection opens, resolve it before `proceed`.
- `CHEST`: `open_chest -> choose_treasure_relic -> proceed`.
- `BUNDLE_SELECTION`: `choose_bundle` then `confirm_bundle` when exposed.
- `CAPSTONE_SELECTION`: `choose_capstone_option` from `capstone.options[].i`.
- `EVENT`: use `choose_event_option` even after combat returns to a finished event.
- `CRYSTAL_SPHERE`: `crystal_clear_cell` until divinations are spent, then `proceed`.
- `GAME_OVER`: `continue_game_over` first. Wait while `game_over.phase=summary_animating`. Use `return_to_main_menu` only when it is exposed.
- `UNLOCK`: `confirm_unlock` repeatedly until the screen closes; never bypass it with a menu-return action. The compact top level mirrors the raw block under `unlock` (`unlock_type`, `items`).
- `FAKE_MERCHANT`: the Fake Merchant event screen. `open_shop_inventory` opens its inventory and `proceed` leaves the screen.
- `PATCH_NOTES`: patch notes shown from the main menu. `close_main_menu_submenu` closes it.
- `CARD_LIBRARY`: the deck / compendium card viewer. `close_main_menu_submenu` returns to the previous screen, both from the main menu and from inside a run.
- `CARD_PILE`: a combat card pile (draw / discard / exhaust). `close_cards_view` returns to combat.
- `CARD_INSPECT` / `RELIC_INSPECT`: inspect overlays. The same `close_cards_view` action that closes the card list also closes these.
- `FEEDBACK`: the feedback form. No mod action closes it yet, so only open it when the task asks and let the player finish it.
- `PAUSE_MENU`, `SETTINGS`, `COMPENDIUM`, `RELIC_COLLECTION`, `POTION_LAB`, `BESTIARY`, `STATS`, `RUN_HISTORY` (and `CARD_LIBRARY` when it is opened from inside a run): the pages shown over a frozen run when a person presses pause. Room actions are suppressed and `capstone` is null on all of them; `close_main_menu_submenu` is the only action advertised, and it steps back one page (`CARD_LIBRARY` -> `COMPENDIUM` -> `PAUSE_MENU`). The pause menu itself offers nothing - a person resumes that one, so wait instead of trying to act.


For detailed per-screen sequences and pitfalls, read [references/screen-playbooks.md](references/screen-playbooks.md).

## Common Pitfalls

- `skip_reward_cards` closes the overlay but may leave the underlying reward item claimable.
- Multi-select overlays may require `confirm_selection`; do not assume one click is enough.
- Potion targeting depends on `target_type`; some potions need no `target_index`.
- Multiplayer targeting still controls only the local player. In the compact view the `target` hint (`enemy` / `player`) plus the `targets` index list tell you what `target_index` may address; the full state names the same fields `target_index_space` and `valid_target_indices`. Never assume teammate control.
- `shop.open = true` means inner inventory, not room completion (raw state spells it `shop.is_open`).
- Timeline gates can block run start until the overlay is confirmed or the submenu is closed.
- `return_to_main_menu` on `GAME_OVER` before `continue_game_over` skips score, unlock, and save.
- `UNLOCK` is not a card-selection screen; only `confirm_unlock`.
- After a multiplayer map vote, `map.local_vote` means wait, not pick a second node.

## Minimal Decision Heuristics

- In combat, spend energy efficiently and avoid ending turn with obvious free value unused.
- In rewards, take cards only when the upgrade is clear; otherwise skip.
- In shops, check relics and removal before committing all gold.
- In events, prefer unlocked options and re-read state after every branch.

<!-- END SHARED PLAY CONTRACT -->

## Run Decision Logs

- Create one markdown log per played or continued run under `agent_knowledge/run_logs/`.
- Use `YYYYMMDD-HHMM_<character>_<seed>.md` when possible; use `unknown-character` or `unknown-seed` until the state exposes the missing value.
- Record route choices, card/relic/potion rewards, shop buys/removes, event branches, rest choices, key combat turns, potion use, lethal planning, and MCP/action anomalies.
- Keep entries short so logging does not block play. If needed, execute the legal action first and flush the note after the returned state stabilizes.
- Use the template in `agent_knowledge/run_logs/README.md` for the header and decision table.

## Choose the Right Tool Surface

- Use the guided profile for normal play and most evaluations.
- Keep `get_relevant_game_data` / `get_game_data_item` / `get_game_data_items` available in guided runs for card, monster, potion, shop, and event decisions.
- Use legacy per-action tools only when a harness explicitly needs tool-by-tool coverage.
- Use `run_console_command` only in development flows where debug actions are enabled.

For validation flows, read [references/debug-and-validation.md](references/debug-and-validation.md).

