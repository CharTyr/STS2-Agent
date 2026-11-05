# Design: MCP Payload Slimming & Session Static Deduplication (#233)

## Architecture

### 1. Stateless Intra-Frame Compact Slimming (C# & Python Parity)
Applied to compact `agent_view` payloads when `raw_state == false`:
- Drop `map` when `screen != "MAP"`.
- Drop `run.piles` when `in_combat == true` (combat piles live in `combat`).
- When `run.players` has exactly 1 player, keep only `{id, character, is_local, hp, max_hp, gold}`.
- Trim `action_readiness` out of combat; in combat keep only `{in_combat, phase, play_phase, can_play_actions, current_side, is_player_turn, hand_play_in_progress, is_ending_turn, waiting_for_other_players, blocking_reason}`.
- On `combat.hand[]`: drop `playable=true`, `unplayable_reason=null` when playable, `requires_target=false`, `has_valid_targets=true` when `requires_target!=true`, plus null scalars and empty `afflictions`/`enchantments` arrays.
- Drop null top-level sections while preserving core keys (`version`, `compact_agent_view`, `screen`, `available_actions`, `in_combat`, `run_id`, `turn`).

Note: `DietRules.DropCardValueNote` is NOT stripped unconditionally on frame 1 in MCP because external MCP clients may call `get_game_state` before reading system prompt resources; instead, `combat.card_value_note` is kept on the first combat frame of a `(session, run_id)` and deduplicated on subsequent combat frames.

### 2. Session-Scoped Static Block Deduplication
Keyed by `(sessionId, runId)` (bounded LRU of 16 sessions in `NativeMcpServer`; single stdio/process session with lock in `sts2_mcp.create_server()`):
- **`run.deck`**: Compute canonical content hash. If identical to last emitted hash in `(sessionId, runId)` and `!fullState`, replace `run.deck` with `run.deck_unchanged: true` and `run.deck_count: <total card count>`. Re-emit full `run.deck` immediately on any add/remove/upgrade.
- **`run.relic_descriptions`**: Compute canonical hash over `(relics, relic_ids, relic_descriptions)`. Keep lightweight `relics`/`relic_ids` + dynamic `relic_stacks`, and if the description hash is unchanged and `!fullState`, replace `run.relic_descriptions` with `run.relic_descriptions_unchanged: true`.
- **`run.ascension_effects`**: If canonical hash is unchanged and `!fullState`, replace `run.ascension_effects` with `run.ascension_effects_unchanged: true`.
- **`glossary` (Delta Filtering)**: Track set of `(term -> definition_hash)` already delivered in `(sessionId, runId)`. On subsequent calls (`!fullState`), emit only new/changed glossary entries; if all present entries were already delivered, remove `glossary` and set `glossary_unchanged: true`.
- **`combat.card_value_note`**: Emit once per `(sessionId, runId)`, then omit on subsequent frames unless `full_state=true`.
- **`decide()` `scene_guidance`**: Track last `(screen, event_id)` emitted by `decide()`. First call on that `(screen, event_id)` emits full `scene_guidance`; consecutive `decide()` calls on the same `(screen, event_id)` emit `{"screen": screen, "scene": scene, "guidance_unchanged": true}` (`get_scene_guidance()` always emits full guidance).
- **Reset Triggers**: `full_state=true` on `get_game_state` emits full slimmed blocks and refreshes hashes; `run_id` change (or null/menu transition) resets run hashes; MCP `initialize` / `DELETE /mcp` resets the session entry.

### 3. Turn-Plan Intermediate Step State Trim
In `NativeMcpServer.TurnPlan.cs` and `mcp_server/src/sts2_mcp/play_tools.py` (`!raw_state`):
- Strip `"state"` from `plan.steps[]` entries (preserving `action`, `status`, `stable`, `message`, `error`, `reconciliation`), keeping the single projected + deduplicated `final_state` (and top-level single-action `state` when no `then` plan ran).

### 4. Tool Schema Alignment & Wire Budget
- Add optional `full_state: boolean` only to `get_game_state` in `AgentTools.cs`, `NativeMcpServer.Tools.cs`, and `mcp_server/src/sts2_mcp/server.py`, keeping wire length `< 380` chars.
