# PRD: Combat Prefix Cache Split, Boss Turn-Plan Divergence & Decision Pacing (#234)

## Problem
During built-in auto-play Boss fights against OpenAI-compatible endpoints (e.g. `deepseek-v4.1-flash`):
1. **Low prefix-cache hit rate (~47%)**: `Latest compact game state` serializes volatile `combat` fields before static `run` fields (`deck`, `relics`, `relic_ids`, `relic_descriptions`, `ascension_effects`) and run-static `glossary` entries (~2.5k–3.5k tokens). Every card play mutates `combat` near the start of the user message, invalidating provider prefix caching for all static run tokens behind it.
2. **Boss mid-turn plan divergence blind spots**: `TurnPlanPolicy.Diverged` (C#) and `turn_plan.diverged` (Python) stop `then` plans on hand growth, screen/turn changes, and HP loss, but do not stop when a standing Boss/enemy mutates its `intent`/`move_id`/`intents` mid-turn (reactive phase shift) or summons/awakens new alive enemies (`combat.enemies`).
3. **No configurable request pacing**: Users hitting provider burst RPM/TPM limits in multi-phase Boss fights have no way to set a minimum elapsed interval between primary LLM decisions.

## Goals & Non-Goals
- **Goals**:
  - Hoist static run context (`run_id`, `character`, `ascension`, `ascension_effects`, `max_energy`, `base_orb_slots`, `deck`, `relics`, `relic_ids`, `relic_descriptions`, and baseline run `glossary` terms) into a deterministic `LlmMessage.System` message inside the static prefix, stripping those exact keys from the dynamic `Latest compact game state` user message.
  - Extend `TurnPlanPolicy.Diverged` (C#) and `turn_plan.diverged` (Python) + `turn_plan_vectors.json` with `enemies_changed` (newly alive/spawned enemy) and `intent_changed` (standing alive enemy mutated `intent`/`move_id`/`intents`).
  - Add optional `MinDecisionIntervalSeconds` (`null` / `<= 0` = disabled, clamped `<= 300s`) to `AgentSettings`, `SettingsClone`, `CompanionPlaySettings`, and Advanced Settings UI, enforcing cancellation-aware monotonic pacing before primary LLM play requests without slowing `Autopilot` or `TurnPlan` follow-up steps.
- **Non-Goals**:
  - Changing gameplay rules or adding artificial sleep on zero-request `Autopilot` or `then` steps.

## Acceptance Criteria
- On a realistic Boss combat snapshot, dynamic `Latest compact game state` shrinks by >= 45%, static prefix share reaches >= 75% of total prompt chars, and the static system prefix remains byte-identical across consecutive combat steps while immediately updating when deck/relics change.
- Shared C# and Python `turn_plan_vectors.json` pass new `enemies_changed` and `intent_changed` Boss mutation cases without false positives when an enemy dies.
- `MinDecisionIntervalSeconds` spaces back-to-back primary LLM requests by only the remaining elapsed gap, cancels immediately on Stop, and leaves `Autopilot` / `TurnPlan` follow-ups unthrottled.
