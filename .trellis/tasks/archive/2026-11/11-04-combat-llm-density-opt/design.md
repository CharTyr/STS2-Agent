# Design: Combat Prefix Cache Split, Boss Turn-Plan Divergence & Decision Pacing (#234)

## Architecture

### 1. Prefix-Cache Friendly State Split (`PlayStateSplit.cs`)
- Operates on `PromptStateDiet.Apply(stateJson)` inside `AgentLoop.PlayWithModelAsync`.
- Hoists run-static fields when `run` carries static inventory (`deck`, `relics`, `relic_ids`, `relic_descriptions`, or `ascension_effects`):
  - `run_id`, `run.character`, `run.ascension`, `run.ascension_effects`, `run.max_energy`, `run.base_orb_slots`, `run.deck`, `run.relics`, `run.relic_ids`, `run.relic_descriptions`.
  - Baseline `glossary`: caches the initial glossary dictionary per `(run_id, static_run_hash)` in `PlayStateSplitCache` so combat-transient glossary additions (e.g. a boss debuff keyword appearing mid-fight) stay in the dynamic `glossary` delta rather than mutating the static system prefix!
- Strips hoisted keys from the dynamic JSON, leaving volatile `run` scalars (`floor`, `act_id`, `boss_id`, `hp`, `max_hp`, `gold`, `deck_stats`, `relic_stacks`, `potions`, multiplayer `players`), `combat`, active screen payloads, and any newly introduced combat-only `glossary` keys.
- Emits `LlmMessage.System("Current run static context:\n" + split.StaticRunJson)` inside the `system` prefix (before `ContextCompaction` and before all `user` messages). When a minimalist test snapshot has no static `run` inventory fields, `StaticRunJson` is `null` and zero extra system message is added.

### 2. Boss Mid-Turn Divergence (`TurnPlanPolicy.cs` & `turn_plan.py`)
Added in `Diverged(planTime, current)` right after `took_damage`, when `planTime` has a non-empty `combat.enemies` array:
1. **`enemies_changed`**: Collect alive enemy identities `(i, enemy_id)` from `planTime`. If `current` contains any enemy with `alive == true` whose `(i, enemy_id)` was not alive at `planTime` (summoned minion, split slime, revived phase), return `"enemies_changed"`.
2. **`intent_changed`**: Map `planTime` alive enemies by index `i` (requiring matching `enemy_id` if both carry `enemy_id`). For each enemy that is still `alive == true` in `current` AND had at least one explicit intent field (`intent`, `move_id`, or `intents`) in `planTime`, compare canonical `(intent, move_id, normalized_intents)` where `normalized_intents` captures `(intent_type, damage, hits, total_damage, status_card_count)` per entry. Any mismatch returns `"intent_changed"`. Dead enemies (`alive == false` in `current`) are ignored so killing a target never stops a valid plan.

### 3. Minimum LLM Decision Interval (`DecisionPacingGate.cs` & Settings)
- `AgentSettings.MinDecisionIntervalSeconds` (`double?`, default `null`, normalized in `NormalizeExperienceSettings`: `null`, NaN/Infinity, or `<= 0` -> `null`; clamped `<= 300`, rounded to 2 decimals).
- Copied in `SettingsClone.Clone`, synced and validated (`is null or (> 0 and <= 300)`) in `CompanionPlaySettings`.
- `DecisionPacingGate`: monotonic clock (`Func<TimeSpan>`) + injectable delay (`Func<TimeSpan, CancellationToken, Task>`). `WaitBeforePrimaryRequestAsync(minIntervalSeconds, ct)` computes `remaining = interval - (now - lastPrimaryRequestAt)`, awaits `delay(remaining, ct)` if `remaining > TimeSpan.Zero` on subsequent requests, and stamps `lastPrimaryRequestAt = clock()` right before the primary model call in `AgentLoop.PlayWithModelAsync`.
- `AgentOverlayHost.Settings.cs`: compact optional numeric input in Advanced card next to `LlmTimeoutSeconds` (empty or `0` = disabled).
