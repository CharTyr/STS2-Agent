# PRD: MCP Payload Slimming & Session Static Deduplication (#233)

## Problem
External MCP clients calling `get_game_state`, `wait_until_actionable`, `act`, and `decide` on either the Native C# `/mcp` endpoint or the Python `sts2_mcp` sidecar rapidly exhaust model context windows because:
1. **Intra-frame duplication**: While built-in auto-play runs `PromptStateDiet.Apply`, MCP responses still emit full `map` graphs off `MAP`, duplicate solo `run.players[0]` inventories, combat `run.piles` copies, verbose `action_readiness` metadata, null/empty hand-card fields, and null top-level screen sections (~2,500+ dead tokens/call).
2. **Cross-call static repetition**: Unchanged `run.deck`, `run.relic_descriptions`, `run.ascension_effects`, already-delivered `glossary` definitions, `combat.card_value_note`, and unchanged `(screen, event_id)` `scene_guidance` inside `decide()` are re-sent verbatim on every turn/step (~2,500–3,500 tokens/call).
3. **Turn-plan N-copy state bloat**: `act(..., then=[...])` embeds a full `state` object inside every `plan.steps[i]` entry in addition to `final_state`.

## Goals & Non-Goals
- **Goals**:
  - Apply parity lossless intra-frame compact slimming across Native C# MCP and Python `sts2_mcp` whenever `raw_state=false`.
  - Deduplicate unchanged static blocks (`run.deck`, `run.relic_descriptions`, `run.ascension_effects`, `combat.card_value_note`, delta-filtered `glossary`, and `decide()` `scene_guidance`) within a session + `run_id`, automatically re-emitting whenever content mutates or `run_id` changes.
  - Strip intermediate `state` snapshots from `plan.steps[]` in `act(..., then=[...])` while preserving `final_state` (and `raw_state=true` fidelity).
  - Add `full_state: bool = false` on `get_game_state` as an explicit escape hatch that returns the full slimmed compact snapshot and refreshes session baselines while keeping `get_game_state` under the 380-char wire budget and `test_native_tool_alignment.py` in lockstep.
- **Non-Goals**:
  - Modifying raw HTTP `/state` or `raw_state=true` outputs.
  - Adding stateful arguments to `decide()` (must remain zero-argument) or `wait_until_actionable` (must keep `{"timeout_seconds", "raw_state"}`).

## Acceptance Criteria
- Intra-frame compact MCP payload size drops by >= 30% on a full combat snapshot, and second+ consecutive combat calls in the same session/run drop by >= 55% vs baseline compact output.
- Any mutation to `run.deck`, `run.relics`/`relic_descriptions`, `run.ascension_effects`, or newly encountered `glossary` terms is immediately emitted on the next MCP call.
- `get_game_state(full_state=true)` emits all compact static blocks and refreshes session baselines; `get_game_state(raw_state=true)` returns untouched raw `/state`.
- All C# tests, Python `unittest` suites (including `test_native_tool_alignment.py` and `test_tool_surface_budget.py`), and `scripts/check_verification_gates.py` pass offline.
