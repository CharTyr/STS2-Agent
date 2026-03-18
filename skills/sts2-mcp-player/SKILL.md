---
name: sts2-mcp-player
description: Play or validate Slay the Spire 2 through the local sts2-ai-agent MCP tools, including access to bundled game metadata (cards/relics/monsters/etc.) for richer decision-making. Use when navigating the main menu, timeline, map, combat, rewards, shops, rest sites, events, chests, card selections, potions, or debug-enabled test flows, and when another agent needs a strict state-first workflow that avoids stale indexes, invalid room actions, and tool-profile confusion.
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
- Recommended skill settings: `allowed_tool_names = ["health_check", "get_game_state", "get_available_actions", "act", "get_game_data_item", "get_game_data_items", "get_relevant_game_data"]`, `max_mcp_tools_per_iteration = 1`, `share_to_main_chat = false`

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

## Quick Start

1. Call `health_check` once at session start.
2. Prefer the guided decision loop: `get_game_state -> get_available_actions -> act` (with `health_check` only at session start).
3. For cards, monsters, relics, potions, shop items, and event options, prioritize game-data tools before using memory:
   `get_relevant_game_data` (default, scene-aware minimal context) ->
   `get_game_data_item` (single-entity lookup) ->
   `get_game_data_items` (batch compare/filter).
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

## Choose the Right Tool Surface

- Use the guided profile for normal play and most evaluations.
- Keep `get_relevant_game_data` / `get_game_data_item` / `get_game_data_items` available in guided runs for card, monster, potion, shop, and event decisions.
- Use legacy per-action tools only when a harness explicitly needs tool-by-tool coverage.
- Use `run_console_command` only in development flows where debug actions are enabled.

For validation flows, read [references/debug-and-validation.md](references/debug-and-validation.md).

## Non-Negotiable State Rules

- Treat `UNKNOWN` as transient only once. Re-read state once; if it persists, stop guessing and inspect the payload.
- Treat `state.session` as the source of truth for singleplayer vs multiplayer. Do not infer mode from screen names or tool names alone.
- Resolve overlays before room flow. `MODAL`, `CARD_SELECTION`, reward-card overlays, and timeline overlays take priority over map or combat planning.
- Treat `pending` responses as an instruction to stay inside the returned screen flow.
- Treat `proceed` as a room action, not a universal fallback.

## Screen Routing

- `MAIN_MENU`: prefer `continue_run`; if unavailable, finish timeline gates or start a run from `open_character_select`.
- `CHARACTER_SELECT`: choose an unlocked character, wait for `can_embark = true`, then `embark`.
- `MULTIPLAYER_LOBBY`: stay on the same compact tool surface; use `available_actions` for `host_multiplayer_lobby`, `join_multiplayer_lobby`, `select_character`, `ready_multiplayer_lobby`, or `disconnect_multiplayer_lobby`.
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
- Multiplayer targeting still controls only the local player. Use `target_index_space` and `valid_target_indices`; never assume teammate control.
- `shop.is_open = true` means inner inventory, not room completion.
- Timeline gates can block run start until the overlay is confirmed or the submenu is closed.

## Minimal Decision Heuristics

- In combat, spend energy efficiently and avoid ending turn with obvious free value unused.
- In rewards, take cards only when the upgrade is clear; otherwise skip.
- In shops, check relics and removal before committing all gold.
- In events, prefer unlocked options and re-read state after every branch.
