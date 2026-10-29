# Decision quality — what the AI knows, and what local computation could help

> Status: **COMPLETE (first full pass)**. Task `10-28-bold-experience-overhaul`. Read-only research:
> the only file written is this one.
> Baseline: v0.17.0, branch `dev`. Decompiled game sources read from `extraction/decompiled/`
> (that tree is a snapshot and **may be older than the installed game** — every member named below
> was checked in that tree, not against the running game).
>
> Scope: the decision-quality axis only — what the model is told, what it is told to do, what could
> be computed locally, and how to prove an improvement. Cross-project landscape and speed live in
> `landscape.md`.

## Status log
- [x] Orientation read (project-map §4/§5/§6/§10, AGENTS.md)
- [x] Agent view inventoried (combat / rewards / shop / map / rooms / run / selection / event / rest / chest / game-over)
- [x] Strategy content audited (`PlayPrompt.cs`, `PlaybookSections.cs`, `SKILL.md`, `references/*`, `docs/game-knowledge/*`, `scene_guidance.py`)
- [x] Jev executor + strategy planner audited
- [x] Benchmark audited (docs + JSON + 11 cases)
- [x] Memory / cross-run audited
- [x] Decompiled damage/intent/preview API verified (exact members named in §3.1)
- [x] Proposals ranked
- [x] Measurement plan

---

## 0. Verdict in one paragraph

The agent already receives a **good, dense state**: live intents with per-hit and total damage,
hand cards with modifier-aware dynamic values, powers on both sides, block/energy/HP, the whole map
graph, and a fresh `lethal_risks` mechanism. What it does **not** receive is *the arithmetic over
that state*. Every number the game would need to answer "how much damage is coming, how much block
do I need, can I kill that one this turn, in what order" exists inside the process — the mod even
computes two of them on every combat state build and then throws them away unless they happen to be
lethal (`GameStateService.CombatRisks.cs:93-115`). So the model is asked to do 12-number mental
arithmetic, card-by-card, every turn, and the fast path (Jev) is asked to do it from option text
that contains **no numbers at all** (`JevOptionEnumerator.cs:428-449`). The strategy text that is
supposed to cover for that is generic and threshold-shaped, has no per-character / per-archetype /
per-boss content, and is ~4 bullets for combat. Meanwhile the repository already contains
96 KB of card-behavior and 36 KB of monster-behavior indexes that the in-game agent cannot read.

The single boldest, cheapest, highest-win-rate move is therefore **not** a better prompt: it is to
compute the combat turn locally, exactly, in-process, and hand the model (and Jev) the answer.

---

## 1. What the model sees today (per screen, field names)

### 1.1 Two decision paths, two different inputs

| Path | Trigger | What it receives |
|---|---|---|
| **LLM play step** | dual-layer off, or Jev failed/low confidence, or a queued player instruction | system: `PlayPrompt.PlaySystem` (the shared SKILL.md contract, ~3.5 K chars) + `Playbook for the screen…` slice (≤2400 chars) + optional reply-language + `Strategy for the screen…` slice + standing planner strategy + compaction + optional vision; then user: `Latest compact game state:\n<whole agent_view JSON>` (`AgentLoop.Decision.cs:160-235`) |
| **Jev execution step** | dual-layer on, Jev configured, confidence ≥ threshold | one `systemone` request: `state` = the compact `agent_view` verbatim, one `choice` question whose `criteria` is `option_id → description`, one `danger` score question; `instructions` = macro goal + screen + playbook slice + strategy facets (`JevExecutionDecider.cs:258-336`) |

Both paths get the **same** compact state; the difference is that Jev must pick from *option
descriptions* rather than from the state, so anything missing from the option text is invisible to it.

### 1.2 Combat — `combat` block of the compact `agent_view`

Source: `GameStateService.AgentView.cs:231-328` (compact) over `GameStateService.Combat.cs` /
`CombatRisks.cs` (raw). `AgentViewVersion = 12`.

| Key | Content | Notes |
|---|---|---|
| `action_readiness` | `can_use_combat_actions`, `reason`, `modal_open`, `hand_in_card_play`, `action_queue_unsettled`, … | passed through unchanged; documented as "the first field to read" |
| `card_value_note` | prose: previews include Strength; preview is **not** a guaranteed target hit | the product's own admission that target math is missing |
| `player.hp` / `.block` / `.energy` / `.stars` / `.focus` | `"12/70"`, ints | |
| `player.orbs[]` / `.pets[]` / `.pet_missing` | orb passive/evoke, pet hp/block | |
| `player.cards_played_this_turn` / `.attacks_played_this_turn` / `.skills_played_this_turn` | ints | feeds relic counters ("Pen Nib 9/10") |
| `player.powers[]` / `enemies[].powers[]` | `"strength 3 [debuff]: …"` short lines (id + amount + description) | both sides |
| `players[]` | teammate `current_hp`/`max_hp`/`block`/`energy`/`is_alive` | co-op |
| `end_turn_will_kill_player` | bool | aggregate over `lethal_risks[].will_kill_player` |
| `lethal_risks[]` | **only lethal rows**: `risk_id`, `source`, `reason`, `incoming_damage`, `damage_after_block`, `player_hp`, `player_block`, `power_id`, `power_amount` | see §2 G1 — this is the near-miss |
| `hand[]` | `i`, `card_id`, `line` (`"Strike [1费]：造成 6 点伤害"`), `upgraded`, `energy_cost`, `star_cost`, `values[]{name,base_value,preview}`, `playable`, `can_play_result`, `target` hint, `targets[]`, `why`, `unplayable_reason(_raw)`, `unplayable_preventer_id/_type`, `keywords[]`, `mods[]` | `values[].preview` is the modifier-applied number |
| `draw[]` / `discard[]` / `exhaust[]` | grouped `line` (`"Strike*3 [1费]：…"`) + `card_ids[]` + `keywords` + `mods` | **counts, not order** — draw order is correctly unknowable |
| `enemies[]` | `i`, `enemy_id`, `name`, `hp` `"34/44"`, `base_max_hp`, `block`, `intent` / `move_id`, `powers[]`, `intents[]{i,intent_type,label,damage,hits,total_damage,status_card_count}`, `alive`, `hittable` | `damage` is per hit **after** modifiers, `total_damage` = damage × hits |

**Are the combat numbers "after modifiers"?** Yes for both sides, and this is worth stating precisely
because it is the foundation of every proposal below:

- Enemy intent `damage` comes from `AttackIntent.GetSingleDamage(targets, owner)`, which runs
  `Hook.ModifyDamage(runState, combatState, me.Creature, owner, DamageCalc(), ValueProp.Move, null,
  ModifyDamageHookType.All, CardPreviewMode.None, out _)` against `LocalContext.GetMe(...)`
  (`extraction/decompiled/MegaCrit.Sts2.Core.MonsterMoves.Intents/AttackIntent.cs:84-93`). So
  Strength, Weak on the monster, **Vulnerable on the player**, and damage caps are already in it.
  `total_damage` is `GetSingleDamage × Repeats` (`SingleAttackIntent.cs:22-25`,
  `MultiAttackIntent.cs:31-34`).
- Hand-card `values[].preview` comes from `card.UpdateDynamicVarPreview(CardPreviewMode.Normal,
  card.CurrentTarget, clone)` (`GameStateService.Run.cs:285-293`). `DamageVar.UpdateCardPreview`
  runs the same `Hook.ModifyDamage` but with **`target = card.CurrentTarget`** — which is the UI's
  hover target, so normally `null`. `VulnerablePower.ModifyDamageMultiplicative` returns 1 unless
  `target == Owner` (`VulnerablePower.cs:26-30`), so a Strike that is really 9 into a Vulnerable
  enemy is reported as 6. This is exactly what `card_value_note` warns about.
- Block previews (`BlockVar.UpdateCardPreview` → `Hook.ModifyBlock(combatState, owner.Creature, …)`,
  `BlockVar.cs:29-33`) are **already exact** with no target needed: Dexterity and Frail are in.

### 1.3 The other screens (compact)

| Screen | Keys | What is *not* there |
|---|---|---|
| `run` | `character`, `ascension`, `ascension_effects[]`, `floor`, `act_id`, `boss_id`, `hp`, `gold`, `max_energy`, `base_orb_slots`, `deck[]` (grouped), `relics[]`, `relic_ids[]`, `relic_stacks[]`, `relic_descriptions[]`, `players[]`, `potions[]{i,potion_id,line,description,usable,discard,target,targets}`, `piles{}` | no deck stats (type/cost/rarity mix, upgrade count, curse count), no potion-slot capacity, no second boss in the compact view, no act length |
| `map` | `open`, `current`, `local_vote`, `boss_node`, `second_boss_node`, `nodes[]{coord,node_type,visited,children[]}`, `votes[]`, `options[]{i,line}` | no path evaluation, no distance-to-boss, no per-option risk; node types are `MapPointType` (`Unassigned, Unknown, Shop, Treasure, RestSite, Monster, Elite, Boss, Ancient`) |
| `reward` | `pending_card_choice`, `can_proceed`, `rewards[]{i,line,claimable}`, `cards[]{i,card_id,line,selected,upgrade,keywords,mods}`, `alternatives[]` | **`rarity`** (raw has it, `GameStateService.Rewards.cs:194`), card `type`, no deck comparison |
| `shop` | `open`, `can_open`, `can_close`, `cards[]` (`line` holds the price), `relics[]{line,affordable,stocked}`, `potions[]`, `remove{price,affordable,available,used}` | price is text-only on cards/relics/potions (no numeric field) |
| `selection` | `kind`, `prompt`, `min`/`max`/`selected`/`confirm`, `cards[]` (+ optional `upgrade` preview) | |
| `rest` | `options[]{i,option_id,line,requires_target,target_index_space,valid_target_indices,enabled}` | no "how much would HEAL restore" number |
| `event` | `id`, `title`, `finished`, `options[]{i,text_key,line,locked,proceed,kill}` | risk grades live only in `docs/game-knowledge/events.md` (Python sidecar only) |
| `chest` | `opened`, `claimed`, `relics[]{i,relic_id,line}` | |
| `game_over` | `victory`, `floor`, `character`, `phase`, `can_continue`, `can_return`, `save_status/verified/error` | no cause of death, no run history |
| `modal` | `type`, `underlying_screen`, `confirm`, `dismiss`, labels | |
| other | `multiplayer`, `multiplayer_lobby`, `character_select`, `timeline`, `capstone`, `unlock`, `bundles`, `crystal_sphere` (raw passthrough), `glossary` | |

### 1.4 What is missing in combat, as a list

1. **Total incoming damage** across living enemies (computed internally, discarded unless lethal).
2. **Block needed** and **unblocked damage against current block**.
3. **Per-enemy lethal**: "this enemy dies to 7 more damage" / "this one kills you next turn".
4. **Per-(card, enemy) exact damage**, i.e. the target-specific multiplier applied.
5. **A survivable/dies-this-turn verdict** and the **cheapest block line** that gets there.
6. **Sequencing / order** ("kill A first, then B"), and **energy accounting** (spendable after the
   mandatory block).
7. **Which threat is unblockable** — `ValueProp.Unblockable` sources are not distinguished from
   blockable ones in the aggregate.
8. **Teammate incoming damage** in co-op (intents are computed against the local player only).
9. **Next-turn expectation** (the monster's next move after this one) — determinable in-process for
   many monsters (`MoveState.FollowUpStateId`, `RandomBranchState.States[].GetWeight()`), unused.
10. Machine-readable **card `type`** per hand card (attack/skill/power) — the model reads it out of
    the localized `line`. Also no separate `hits` scalar for multi-hit player cards; the count is only
    inside `values[]` (`CalculatedHits` / `Repeat` / `Times`), so any arithmetic must look it up by name.

---

## 2. Gaps ranked by win-rate impact (with evidence)

Ranking logic: impact = how many of a run's ~1,000 decisions it degrades × how badly; evidence =
a file/line that proves the gap rather than a hunch.

### G1 — The combat arithmetic is never done for the model (highest)
`BuildCombatLethalRiskPayloads` computes `incomingDamage` = Σ `intent.total_damage` over living
enemies and `damageAfterBlock = max(0, incoming - block)` on **every** combat state build, then
emits a risk row **only when `damageAfterBlock >= current_hp`**
(`GameStateService.CombatRisks.cs:93-115`). On every non-lethal turn — i.e. almost all of them —
those two numbers are thrown away. The strategy text then asks the model to do it by hand:
*"prefer the play that minimises `combat.enemies[].intents[].total_damage` reaching you, weighing
block against killing the attacker"* (`skills/sts2-mcp-player/references/strategy.md:83`), and the
shared contract says *"Budget block and pick targets from `intents[]` (`damage`/`hits`/`total_damage`)"*
(`SKILL.md:126`). With 2–4 enemies × 1–3 intents × per-hit-vs-total this is a 6–12 number sum against
a block value, recomputed each turn, per candidate line. This is where a plausible-looking blunder
("block 12 when 18 is coming") comes from, and it is pure arithmetic that a `int` can do.
**Fix cost: ~S.** Impact: **high**.

### G2 — Attack previews are target-blind, so the model multiplies Vulnerable itself
`values[].preview` is computed with `card.CurrentTarget` (`GameStateService.Run.cs:286`), non-null
only while the UI hovers a target, so in practice always `null` → no Vulnerable/Debilitate/PaperPhrog
multiplier (`VulnerablePower.cs:26-30`). The product documents the hole rather than closing it
(`GameStateService.AgentView.cs:248`, `docs/api.md:1084`) and tells the model to "check
target-specific Vulnerable, caps and block" itself. Consequences: under-kills (wasted turn + an extra
enemy turn), and over-blocking. **Fix cost: M** (needs the per-target preview loop). Impact: **high**.

### G3 — The fast path chooses card plays from text with no numbers at all
`JevOptionEnumerator` builds a `play_card` option as `Play {line} → {enemyName}`
(`JevOptionEnumerator.cs:428-449`), where `line` is `Name [cost]：rules text` built in
`FormatCardLine`. So Jev's `criteria` map — the entire choice surface of the executor — reads
`play_card:2->1: "Play Strike [1费]：造成 6 点伤害 → Jaw Worm"` with **no** live damage, no
"kills", no block value, no threat context. The danger score is the only numeric judgement it can
make. Since dual-layer exists to run *most* actions through Jev, this is the cheapest place to
buy accuracy per line of code. **Fix cost: S–M.** Impact: **high**.

### G4 — Strategy content is generic, threshold-shaped, and has no character/archetype/boss layer
Totals: `strategy.md` is 102 lines covering 7 screens; its combat section is 6 numbered rules
(`strategy.md:76-85`). Combat gets 2 headings injected (`PlaybookSections.cs:54`) ≈ 1.1 KB, and the
COMBAT playbook is 4 procedural bullets (`screen-playbooks.md:50-56`). There is **no** per-character
content: a grep of `SKILL.md`, `strategy.md` and `screen-playbooks.md` for
`Ironclad|Silent|Defect|Necrobinder|Regent|character` returns **zero** strategy sentences — every hit
is routing ("choose an unlocked character") or filename advice. Six playable characters exist with
materially different kits and different starting HP (Defect 75 / Ironclad 80 / Necrobinder 66 /
Regent 75 / Silent 70 / Deprived 1000, `docs/game-knowledge/characters.md`), yet every HP threshold in
the strategy text is character-independent. Also missing: archetype packages (Strength, Poison,
Orb/Focus, Summon/Osty, Forge/Stars), per-elite or per-boss preparation, card-tier guidance, and
potion-hold rules by encounter. Thresholds are openly heuristic ("roughly 65%", "roughly 250 gold",
"roughly 75 gold", and the file says so at `strategy.md:97-102`). In the same repository,
`docs/game-knowledge/card-behaviors.md` (96 KB), `monster-behaviors.md` (36 KB), `powers.md` (43 KB)
and `events.md` (46 KB) hold exactly that knowledge — and the mod embeds **only three files**
(`STS2AIAgent.csproj` `EmbeddedResource`: SKILL.md, screen-playbooks.md, strategy.md).
**Fix cost: M** (content work + mapping tests). Impact: **medium-high** (it compounds with G1–G3
rather than replacing them).

### G5 — Reward/shop choices lack the two facts a player uses first
Compact `reward.cards[]` / `shop.cards[]` drop **`rarity`** (present in raw at
`GameStateService.Rewards.cs:194`) and card `type`; shop prices are text-only (`"… | 120g"`), and
there are **no deck statistics** in `run` beyond grouped lines. So "does this card fill a gap in this
deck" requires the model to count grouped lines by hand under a 4,000-token-ish compact budget, which
it reliably does badly or skips. **Fix cost: S** for the fields, **M** for the advisor. Impact:
**medium** (a run has ~15–25 card-reward decisions; each is a small EV change that compounds).

### G6 — The map graph is exposed but never evaluated
`map.nodes[]` carries `coord`/`node_type`/`visited`/`children[]` (`docs/api.md:1115`,
`GameStateService.AgentView.cs:602-608`) precisely so a client can plan a route end-to-end — and then
the only guidance is prose thresholds applied by the LLM against a graph it must traverse mentally
(`strategy.md:15-24`). A working path enumerator already exists in the repository but only on the
Python side and only as a node-type list: `mcp_server/src/sts2_mcp/knowledge.py:289-315`
(`_enumerate_paths` / `_path_summary`). Act composition is known and deterministic enough to reason
about (`MapPointTypeCounts`: 3 shops, 5 elites (×1.6 on SwarmingElites), 3–6 rests, 10–14 unknowns
per act). **Fix cost: M.** Impact: **medium** (one decision per floor, ~50 per run, but each can cost
a whole act).

### G7 — The in-game agent does not learn across runs
`PlaySessionStore` is per-run (`sessions/<runId>.json` beside the settings file), and
`PlaySessionMemory` is the *active run's* bounded decision buffer. `agent_knowledge/` (combat notes
keyed by enemy composition, event notes keyed by event id, route options) is read and written **only**
by the Python sidecar (`knowledge.py`, `Sts2KnowledgeBase`) or by an external agent following
`SKILL.md:209-215` (`run_logs/`). Nothing in the mod reads a previous run's outcome. So a death to a
specific boss or elite changes nothing about the next run — the highest-variance loss (a repeated
mistake) is structurally unlearnable. **Fix cost: M–L.** Impact: **medium now, high compounding**.

### G8 — The offline benchmark cannot detect any of the above
`docs/decision-benchmark.json` has **11 cases**, all of which are payload echo or legality checks
(`combat-lethal-before-end-turn`, `combat-wait-when-gated`, `event-reject-lethal-option`,
`map-low-health-avoid-elite`, `rest-low-health-heal`, `shop-buy-affordable-sale-card`,
`reward-choose-indexed-card`, …). None requires arithmetic, sequencing, target focus, or block
budgeting; the scorer has no notion of a "correct plan"; and no model runner is implemented
(`docs/decision-benchmark.md:131-136`). The example answers file is a hand-built 100 % baseline. So
after shipping every proposal below, today's harness would still report the same score. **Fix cost:
S–M.** Impact: **it gates the ability to prove anything else** — rank it with the fixes, not after.

### G9 — The planner is a prose writer, not a calculator
`StrategyPlanner` sends a trimmed state (≤4,000 chars, `AgentLoop.Probes.cs:52`) and a system prompt
asking for `goal` / `posture` / `instructions` / `option_hints` — no numbers, no per-option analysis
(`StrategyPlanner.cs:98-110`). Its output reaches Jev as prose nudges. This is a reasonable design,
but it means the plan layer cannot compensate for G1–G3 either. **Impact: low on its own**; it
becomes more valuable once the numbers exist to plan against.

### G10 — Co-op threat numbers are local-player-only
`GetSingleDamage` modifies against `LocalContext.GetMe(...).Creature`
(`AttackIntent.cs:87-91`), so an intent shown to the agent is the damage *the local player* would
take. A planner cannot budget the teammate's survival, and the co-op strategy section is deliberately
not injected in-game (`PlaybookSections.cs:74-80`). **Impact: medium in co-op only.**

---

## 3. Bold proposals, ranked by impact / effort / risk

Effort: **S** ≤ ~1 day, **M** ~2–4 days, **L** > 1 week (single experienced dev, tests included).
"Pure" = Godot/game-free class compiled into `STS2AIAgent.Tests` (the project's pattern:
`SandpitSafetyPolicy.cs`, `NoProgressPolicy.cs`, `PlaybookSections.cs`, … registered by hand in
`STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`).

### 3.1 P1a — `combat.threat`: the arithmetic that is already computed, published (S, impact ★★★★★, risk ★)

Add one additive compact block, computed in the same build pass:

```jsonc
"combat": {
  "threat": {
    "incoming": 18,             // Σ intent.total_damage over living enemies
    "incoming_unblockable": 6,  // the part from ValueProp.Unblockable sources
    "block": 5,
    "block_needed": 13,         // max(0, incoming - block)
    "unblocked": 13,            // damage that reaches HP if you end the turn now
    "hp_after": 11,             // current_hp - unblocked  (null when unknown)
    "lethal": false,            // unblocked >= current_hp
    "per_enemy": [ {"i":0,"incoming":10,"after_block":10,"kills":false},
                   {"i":1,"incoming":8,"after_block":3,"kills":false} ],
    "kills_you_if_unblocked": [0,1]   // optional: which enemies are the lethal contributors
  }
}
```

- **Where the numbers come from:** the compact builder already has `enemies[].intents[].total_damage`
  and `player.block` — no new game call at all. `incoming_unblockable` needs the intent's `ValueProp`
  (not currently projected; add `props` to `CombatEnemyIntentPayload` or derive per intent type).
- **Code:** pure `CombatThreatPolicy.cs` (input record → output record) in `STS2AIAgent/Game/`,
  mirroring `CombatRisks.cs`'s concern but not its "only when lethal" gate. `CombatRisks` then reads
  the policy's output instead of recomputing (single definition of the sum — this also removes a
  latent double-definition risk: `lethal_risks` and `threat` must never disagree).
- **Reaches the model:** automatically — it is inside the compact state both paths already receive.
- **Docs/contracts:** a row in `docs/api.md` "compact `agent_view` 的增补字段" table,
  `AgentViewVersion` 12 → 13, and the rename table only if names differ. `api-facts` gate reads these.
- **External MCP agents:** free (same `agent_view`).
- **Risk:** co-op caveat (local-player-only, G10) must be stated in the field doc; `hp_after` can be
  negative → clamp and say `lethal: true`.
- **Why it is first:** it closes G1 for ~40 lines of arithmetic, costs zero extra game-thread work,
  and it is the one field that changes every combat decision the model takes.

### 3.2 P1b — `GET /combat-plan`: the exact per-card, per-target turn calculator (M, impact ★★★★★, risk ★★)

The real "turn calculator". Two halves, deliberately split so the pure half is testable offline:

**(i) Game-side extractor** (`GameStateService.CombatPlan.cs`, ~120 lines) — for a requested frame
compute, on the game thread:

- per `(hand card, living enemy)`: exact damage from
  `card.UpdateDynamicVarPreview(CardPreviewMode.Normal, enemyCreature, card.DynamicVars.Clone(card))`
  → read the `Damage` var's `PreviewValue` (this is the *exact game preview against that target*).
- per card: `Block` var `PreviewValue` (already exact, `BlockVar.cs:29-33`), energy cost
  (`EnergyCost.GetWithModifiers(All)`), and `CanPlay`.
- per enemy: `current_hp`, `block`, `hittable`, its intent `total_damage` / `ValueProp`.
- player: `current_hp`, `block`, `energy`, `stars`.

**(ii) Pure policy** (`CombatPlanPolicy.cs`, game-free) — a bounded beam/DFS over
(card, target) plays with an energy budget, hand ≤ 10 and energy ≤ ~5, node cap ~20k:

```jsonc
{
  "incoming": 18, "block_from_hand_max": 12, "survivable": true,
  "can_kill": [ {"enemy":0,"cost":2,"damage":14,"overkill":3} ],
  "suggested": [
     {"action":"play_card","card":2,"target":0,"why":"14 dmg kills the 11 HP attacker, removing 10 of the 18 incoming"},
     {"action":"play_card","card":4,"target":null,"why":"8 block covers the remaining 8"}
  ],
  "remaining_incoming": 0, "remaining_energy": 0,
  "advisory": true, "limits":"no draw, no RNG, no on-death triggers"
}
```

- **Exactness, spelled out** (this is the part the owner asked to have verified):

| Question | Exact in-process member | Verified |
|---|---|---|
| Intent damage per hit, after Strength/Weak/Vulnerable/caps | `AttackIntent.GetSingleDamage(IEnumerable<Creature>, Creature)` → `Hook.ModifyDamage(...)` | `AttackIntent.cs:84-93` |
| Intent total for the turn | `AttackIntent.GetTotalDamage` = single × `Repeats` | `SingleAttackIntent.cs:22`, `MultiAttackIntent.cs:31` |
| Card damage **vs a specific target** | `CardModel.UpdateDynamicVarPreview(CardPreviewMode, Creature? target, DynamicVarSet)` → `DamageVar.UpdateCardPreview` → `Hook.ModifyDamage(...)`; read `DamageVar.PreviewValue` | `CardModel.cs:1133-1161`, `DamageVar.cs:28-46` |
| Card block (Dexterity/Frail applied) | `BlockVar.UpdateCardPreview` → `Hook.ModifyBlock(combatState, owner.Creature, …)` | `BlockVar.cs:29-33` |
| Damage caps (Intangible → 1) | inside `Hook.ModifyDamageInternal` (`ModifyDamageCap`) | `Hook.cs:1902-1936` |
| Block absorption | arithmetic on `Creature.Block`; `ValueProp.Unblockable` skips it | `Creature.cs:351-356`, `ValueProp.cs:5-12` |
| Block cap | `Creature.GainBlockInternal` clamps to 999 | `Creature.cs:371-378` |
| Multi-hit **player** cards (e.g. 5 damage ×3) | the hit count is a dynamic var, not a field: read the `CalculatedHits` / `Repeat` / `Times` var's `PreviewValue` alongside `Damage` (e.g. `Barrage` declares `CalculatedVar("CalculatedHits")`, `docs/game-knowledge/card-behaviors.md`) | `CalculatedVar.cs`, card-behaviors index |
| "Is this lethal" | **no `IsLethal` member exists** in the decompiled tree (grep: 0 hits). Compute `damage >= current_hp + block` (drop `block` for `Unblockable`). `DamageResult.WasTargetKilled` exists only *after* damage lands | `Creature.cs:358-370` |
| Stable card identity across frames/runs | `NetCombatCardDb.Instance.TryGetCardId(...)` for the persistable card id (use `card.Id.Entry` for the per-frame id) | parent-verified; needed by P5/P6 |

  Two caveats to write into the code, not just the report: `Hook.ModifyDamage` with `target == null`
  and `CardPreviewMode.MultiCreatureTargeting` hides a number when hittable enemies differ
  (`Hook.cs`, the `MultiCreatureTargeting` branch) — so always pass one concrete target; and a
  preview runs *all* hook listeners, which is why the existing code wraps it in `try/catch`
  (`GameStateService.Run.cs:302-305`) — do the same per cell and return `null` for a cell, never a
  guessed number.

- **Where it reaches clients:** new route `GET /combat-plan` in `Server/Router.cs` (read-only, no
  action), documented in `docs/api.md`; tool `get_combat_plan` on **both** MCP surfaces
  (`Server/NativeMcpServer.Tools.cs` + `AgentTools.Mcp` schema, and
  `mcp_server/src/sts2_mcp/server.py` + `client.py` — `test_native_tool_alignment.py` requires both);
  a `combatPlan` facet in `decide`.
- **Why a route and not a state field:** the extractor is ~`hand × enemies` preview calls (10 × 5 = 50,
  each walking every hook listener). The state build has a 100 ms slow-build budget
  (`StateBuildTiming`), and every compact field is paid on *every* decision. Keep the cheap aggregate
  (P1a) always-on and the matrix opt-in.
- **Risk:** preview side effects (mitigated: the pattern is already trusted in shipped code, wrapped
  per call); game-thread occupancy (measure with `StateBuildTiming`); staleness (the plan must be
  recomputed after any action, so it is a *per-frame* read, not a cached one).

### 3.3 P2 — Put the numbers into the Jev option text (S–M, impact ★★★★★, risk ★)

`JevOptionEnumerator` already computes the enemy-name list per frame; extend the `play_card` option
description with one parenthetical built from the P1b matrix computed **once per frame** (not per
option):

```
Play Strike [1费]：造成 6 点伤害 → Jaw Worm (9 dmg · KILLS · 3 over)
Play Defend [1费]：获得 5 格挡          (block 5 · incoming 18 · still 13)
end_turn                                (13 damage lands · HP 24 → 11)
```

- Enumerator stays pure if the matrix is passed in (a small `FrameMath` record) rather than read —
  good, because `JevOptionEnumerator` is offline-tested.
- Also add the aggregate to the `danger` score's `instructions` ("incoming 18, block 5, unblocked
  13") so the two questions agree.
- **Impact:** the fast path is where most actions are decided; this is the smallest diff that makes
  it numerate. **Risk:** option text length (Jev pays tokens per option) — cap at ~60 chars per
  option and only for playable cards.

### 3.4 P3 — Strategy content with a character/archetype/boss layer (M, impact ★★★☆☆, risk ★)

Keep the injection-per-screen architecture (it saves ~3 K tokens/step) but give the model content
that a strong player actually uses:

1. **Character pages** — one `## Character: Ironclad` … per playable character (6: Ironclad, Silent,
   Defect, Necrobinder, Regent, Deprived), sliced by `run.character`, mapped in
   `PlaybookSections.ScreenHeadings` under a new run-level mechanism (today `RunLevelHeadings` is
   empty, `PlaybookSections.cs:67`) or as an extra COMBAT heading chosen by character. Contract tests
   already enforce "a heading no screen maps to must be declared" (`PlaybookSectionsTests`), so this
   cannot silently ship dead.
2. **Archetype packages** — "if you have N Strength sources, prefer X"; "Poison wants time, block
   early"; "Orb decks: Focus before Evoke"; "Osty/Summon: keep the pet alive".
3. **Elite/boss prep**, generated from `docs/game-knowledge/monster-behaviors.md` (it already reads
   each move's real numbers, e.g. `Doormaker … BEAM_MOVE=SingleAttackIntent(LaserBeamDamage) -> Deal
   31 damage`) — a per-act "these are the two elites, this is the number that kills you" block.
4. **Replace the arithmetic instructions** with "read `combat.threat` / call `get_combat_plan`"
   (G1/G2), so the prompt stops asking for mental math.
5. Budget: `MaxInjectedCharacters = 2400` per slice, so this must stay sliced, not appended. A
   per-character page of ~1,200 chars is affordable *if* it replaces, not adds to, the generic combat
   section.

**Risk:** content drift from the real game (v0.17.0 target). Mitigate by generating the numeric parts
from `docs/game-knowledge/*` (already generated from decompiled sources) rather than hand-writing
numbers, and by keeping every threshold labelled as tunable.

### 3.5 P4 — Map path planner (M, impact ★★★☆☆, risk ★)

- **Pure** `MapRoutePolicy.cs`: enumerate root→boss paths from `map.nodes[]` (port `_enumerate_paths`
  from `knowledge.py:289`), score each path by node-type counts under (a) `hp%` bands, (b) `gold`,
  (c) deck readiness (size / upgrade count / curse count from P5's stats), (d) act boss id.
- **Output**: additive `map.route_plan` — per *currently selectable option* a score, the best path it
  leads to, and a one-line reason; plus `recommended_option_index`.
- **Reaches the model:** compact field (cheap: the graph is already built) — so both paths and all
  MCP clients get it; no new route needed. `AgentViewVersion` bump.
- **Risk:** over-confident routing. Keep it a *ranking with reasons*, and keep the strategy prose's
  "the state wins" rule. Also: `map.nodes[]` is complete even when the overlay is closed
  (`docs/api.md:1114`), but travel is only legal when `map.open`, so the plan must be advisory.
- **Secondary (optional, M)**: next-move expectation. `MoveState.FollowUpStateId` and
  `RandomBranchState.States[].GetWeight()` make "what comes after this move" computable
  (`MonsterMoveStateMachine.cs`, `RandomBranchState.cs:8-70`), but `RollMove` mutates state and uses
  RNG, so this needs a read-only re-implementation per monster family. High value for potion/block
  timing on elites; do not start here.

### 3.6 P5 — Card reward / shop advisor (M, impact ★★★☆☆, risk ★★)

- **Fields first (S):** add `rarity` and `type` to compact `reward.cards[]` / `shop.cards[]`
  (raw already has rarity: `GameStateService.Rewards.cs:194`), and a numeric `price` on shop entries
  (today it is only inside `line`). Add `run.deck_stats`:
  `{count, attacks, skills, powers, curses, upgrades, avg_cost, zero_cost, draw_cards, block_cards}`.
  **No new game calls** — the deck is already enumerated.
- **Pure** `RewardValuePolicy.cs`: score each offered card against deck gaps (e.g. "no block card
  below 1 cost", "3 attacks already redundant", "deck > 25 cards and nothing to remove"), plus
  rarity/skip priors, and emit `reward.advice[]` (`take` / `skip` / `why`, ≤1 line each).
- **Why hedged:** `docs/decision-benchmark.md:8-9,121-122` deliberately refuses to score "card
  strength" offline, and that instinct is right — the advice must be *gap-based* and explicitly
  advisory, not a tier list. Price the effort honestly: the value is real but smaller than P1–P3.
- **Shop** reuses the same policy (`shop.remove` first when a curse/dead card exists is already
  `strategy.md` advice; the advisor makes it computable).

### 3.7 P6 — Cross-run run journal (M–L, impact ★★☆☆☆ now, ★★★★☆ compounding, risk ★★)

- **Store**: `RunJournalStore` — append-only `journal.jsonl` under the same directory family as
  `sessions/` (beside the settings file, so `STS2_AGENT_SETTINGS_PATH` relocates it, mirroring
  `PlaySessionStore.DefaultDirectory()`), one record per finished run:
  `{run_id, character, ascension, seed?, result, floor, act, boss_id, died_to:{enemy_id, intent,
  incoming, hp}, deck_summary, key_decisions}`.
- **Write points**: `game_over` payload build (the frame already knows `victory`, `floor`,
  `character`) plus the last combat frame for `died_to` (last `lethal_risks` row / the enemy whose
  intent matched the fatal blow). Honest limit: the exact killer is *inferred*, and must be stored as
  `inferred: true`.
- **Read point**: at run start (`CurrentRunBoundary` transition / `embark`), build a ≤600-char
  "lessons from previous runs" block — same character, same act boss, recent deaths — and inject it as
  a **system message on the static prefix** (like the planner strategy, `AgentLoop.Decision.cs:187-192`)
  so it does not invalidate the per-step cache.
- **Redaction:** reuse `PlaySessionStore`'s normalization/redaction path; never store chat text.
- **Risk:** poisoning future runs with noise from a tiny sample. Cap at the last N runs, require ≥2
  observations before stating a lesson, and label it as a prior.

### 3.8 P7 — Benchmark v2: cases the calculators can be scored against (S–M, impact ★★★★☆ as an enabler, risk ★)

- Add a `plan` expectation kind: a case declares `must_kill_enemy: 0`, `max_damage_taken: 0`,
  `energy_at_most: 3`, and *any* action set satisfying it scores — so the fixture tests judgement,
  not one blessed sequence.
- Add 8–12 **math cases** built from real captured states: "kill the 3 HP enemy instead of blocking",
  "block exactly enough, not 4 over", "don't end turn with `end_turn_will_kill_player` and a legal
  kill available", "the target with Vulnerable is the one to hit".
- Add a **runner** for the in-game decision function (not a model call): feed a state snapshot to
  `CombatPlanPolicy` and compare with the case's accepted set. Zero cost, no network, no game.
- `schema_version: 2` per the doc's own rule (`decision-benchmark.md:87-89`); keep v1 cases scoring.

### 3.9 Ranking summary

| # | Proposal | Win-rate impact | Effort | Risk | Gaps closed |
|---|---|---|---|---|---|
| 1 | P1a `combat.threat` aggregate | ★★★★★ (always on, every combat turn) | S | ★ | G1 |
| 2 | P2 numbers in Jev options | ★★★★★ (the fast path) | S–M | ★ | G3, G1 |
| 3 | P1b `GET /combat-plan` calculator | ★★★★★ (exact per-target + sequence) | M | ★★ | G1, G2, G6-ish |
| 4 | P7 benchmark v2 | ★★★★☆ (enabler) | S–M | ★ | G8 |
| 5 | P3 strategy content w/ character + boss layer | ★★★☆☆ | M | ★ | G4 |
| 6 | P5 reward/shop fields + advisor | ★★★☆☆ | M | ★★ | G5 |
| 7 | P4 map path planner | ★★★☆☆ | M | ★ | G6 |
| 8 | P6 cross-run run journal | ★★☆☆☆ now | M–L | ★★ | G7 |

Suggested order of work: **P1a → P7 (so the next step is measurable) → P2 → P1b → P3 → P5 → P4 → P6.**

---

## 4. How to measure improvement

Today there are three honest measurement layers and one missing one.

**(1) Offline unit tests (free, CI).** Every pure policy above lands in
`STS2AIAgent.Tests` via an explicit `Compile Include` (the pattern for ~40 policies already
registered). Fixtures are small crafted frames whose answer is provable by hand:
```
energy 3, hp 20, block 0, enemies [ {hp 3, incoming 11}, {hp 30, incoming 7} ],
hand [ {cost 1, dmg 6}, {cost 1, block 5} ]
→ plan kills enemy 0, blocks 5, remaining_incoming 7, hp_after 13
```
Properties worth asserting (they catch the real blunders, not just the fixtures): the plan never
spends more energy than available, never targets a dead/untargetable enemy, never claims `lethal`
when `unblocked < hp`, and `threat.lethal == (unblocked >= hp)` for every generated frame.

**(2) Deterministic snapshot replay (free, local).** `scripts/decision_benchmark.py
--capture-base-url http://127.0.0.1:8080 --output build/…` already captures a raw `/state` read-only
into gitignored `build/` (`decision-benchmark.md:30-38`). Capture ~30 real combat frames (normal,
elite, boss, multi-enemy, Vulnerable/Weak, Intangible), add the compact projection, and assert the
calculator's output against a hand-checked expectation. This is the regression net for game-version
drift (the decompiled tree is a snapshot; the installed game may differ).

**(3) Benchmark scoring (free, CI).** Once P7 lands, `python scripts/decision_benchmark.py --answers
…` scores a candidate action set including the math cases, deterministically, with no game, network
or model. It cannot prove win-rate — the doc is right about that — but it makes "did the change
reduce blunders in these situations?" a number.

**(4) Live win-rate (the only real evidence).** Requirements, all already available:
- `scripts/start-game-session.ps1 --clientId <id>` seeds an isolated settings profile → repeatable
  sessions;
- `sessions/<runId>.json` holds every accepted decision with `run_id`/`screen`/`action`/`reason`;
- `game_over` payload gives `victory`, `floor`, `character`;
- so a `runs/summary.jsonl` append (result, floor, act, character, boss, deaths) makes
  **win-rate / median floor / deaths-per-act** computable without manual counting. Add it in the same
  change as P6's journal (same file, same write point).
- A/B protocol: same model, same Jev config, **same seeds** (seed is exposed on the run once
  `state` carries it; until then, same `--clientId` + fixed character + fixed ascension), feature flag
  on/off via a setting or `STS2_AGENT_*` env var. Report n, win-rate, median floor, and the
  distribution — not a single run. With ~5 %-class effects, n ≥ 20 runs per arm before believing it.

**What NOT to use as evidence:** the example answers file's 100 % (a hand-built baseline,
`decision-benchmark.md:117-118`); a single successful run; token counts (they measure cost, not
quality).

---

## 5. Risks and unknowns

**Correctness risks of the calculators**
1. *Preview hooks are not guaranteed side-effect-free.* `CardPreviewMode` exists because some hooks
   behave differently while previewing. The shipped code already calls `UpdateDynamicVarPreview` once
   per hand card, so the exposure is not new — but P1b multiplies it by enemy count. Mitigation: one
   `try/catch` per cell returning `null` (the existing pattern, `GameStateService.Run.cs:302-305`), and
   a hard cap on cells (`hand × enemies > 60` → return the estimate-only fallback).
2. *Multi-target cards.* `Hook.ModifyDamage` has a `MultiCreatureTargeting` branch that hides a number
   when hittable enemies differ. Never use that mode for the matrix; always one concrete target, and
   label a card "hits all" so the sequence search does not double-count it.
3. *On-death / on-hit triggers.* Thorns, Explosive, "when this dies" powers are not in the preview
   (they fire on damage, not on preview). A plan that says "kill it with 3 attacks" can eat Thorns
   three times. Mitigation: name the limit in the output (`limits` field) and keep the plan advisory.
4. *Block arithmetic assumes no mid-turn block loss.* True in the base game; a power that consumes
   block would break it. Accept and state.

**Game-version drift**
5. `extraction/decompiled/` is a snapshot; `IsLethal` in the task brief does **not** exist there
   (grep: 0 hits), which is itself evidence the tree is not the last word. Every member used must be
   either compile-checked (public, so the compiler catches a rename — the pattern
   `BuildCreaturePowerPayloads` adopted, `GameStateService.Combat.cs:471-475`) or registered in
   `ReflectedGameMembers` with a `/health` probe. Prefer the compile-checked route.

**Performance / game-thread**
6. `StateBuildTiming` reports builds over 100 ms; the state build is already the hot path and P1a is
   free while P1b is not. P1b must be a separate route, bounded, and measured before/after.

**Contract and process risks**
7. Every new compact field needs a `docs/api.md` row (the `api-facts` gate fails otherwise), an
   `AgentViewVersion` bump, and — for a new route — `docs/openapi.json`, `Router.cs`, and both MCP
   surfaces (`test_native_tool_alignment.py`, ADR 0002). New source files must be added to
   `docs/project-map.md` (`ProjectMap.EverySourceMapped`).
8. Compact fields are paid on **every** decision and every Jev request. A `threat` block of ~8 scalars
   is cheap (<100 tokens); the per-cell matrix would not be. This is the whole reason for the
   two-layer split.
9. `agent_view` shape changes ripple into the skill docs (`SKILL.md`, `references/strategy.md`,
   `scene_guidance.py`) and their tests (`test_scene_guidance_alignment.py`).

**Open unknowns (need the running game or a decision, not more reading)**
10. Is `card.CurrentTarget` ever non-null outside hover? (If it sometimes is, current previews are
    inconsistent rather than merely target-blind.)
11. Co-op: does the intent damage shown for a monster attacking the *teammate* mislead the local
    player? `GetSingleDamage` uses `LocalContext.GetMe`, so almost certainly yes — worth an explicit
    `intent.targets_local_player` field.
12. How smooth is win-rate as a function of these changes? Unknown and probably small per change;
    measure with the A/B protocol, never claim it from one run.
13. Whether a deterministic monster "next move" read is cheap enough to be worth it (P4 secondary).
14. Whether the *installed* game version still has the exact `Hook.ModifyDamage` /
    `UpdateDynamicVarPreview` signatures. Verify at build time (compile), not by assumption.

---

## Appendix A — Files this analysis is keyed to

| Concern | File |
|---|---|
| Compact view (all screens), `AgentViewVersion` | `STS2AIAgent/Game/GameStateService.AgentView.cs` |
| Hand cards / intents / enemies | `STS2AIAgent/Game/GameStateService.Combat.cs` |
| Lethal risk sum (the discarded arithmetic) | `STS2AIAgent/Game/GameStateService.CombatRisks.cs` |
| Preview construction (`values[].preview`) | `STS2AIAgent/Game/GameStateService.Run.cs:276-306` |
| Map graph | `STS2AIAgent/Game/GameStateService.Map.cs` |
| Reward/shop payloads | `STS2AIAgent/Game/GameStateService.Rewards.cs`, `.Shop.cs` |
| Play prompt assembly | `STS2AIAgent/Agent/PlayPrompt.cs`, `AgentLoop.Decision.cs` |
| Screen → guidance slicing | `STS2AIAgent/Agent/PlaybookSections.cs` |
| Shared play contract (embedded) | `skills/sts2-mcp-player/SKILL.md` (markers `BEGIN/END SHARED PLAY CONTRACT`) |
| Strategy rules (embedded, sliced) | `skills/sts2-mcp-player/references/strategy.md` |
| Screen playbooks (embedded, sliced) | `skills/sts2-mcp-player/references/screen-playbooks.md` |
| Jev options (no numbers today) | `STS2AIAgent/Agent/JevOptionEnumerator.cs` |
| Jev request shape | `STS2AIAgent/Agent/JevExecutionDecider.cs` |
| Strategy planner | `STS2AIAgent/Agent/StrategyPlanner.cs` |
| Memory (per run only) | `STS2AIAgent/Agent/PlaySessionStore.cs`, `PlaySessionMemory.cs` |
| Offline knowledge (MCP-only) | `mcp_server/src/sts2_mcp/knowledge.py`, `docs/game-knowledge/*` |
| Guidance mirror for external agents | `mcp_server/src/sts2_mcp/scene_guidance.py` |
| Benchmark | `docs/decision-benchmark.md`, `docs/decision-benchmark.json` |
| Game damage/preview API | `extraction/decompiled/MegaCrit.Sts2.Core.MonsterMoves.Intents/AttackIntent.cs`, `…/Hook.cs`, `…/Localization.DynamicVars/DamageVar.cs`, `BlockVar.cs`, `…/Models/CardModel.cs`, `…/Entities.Creatures/Creature.cs`, `…/ValueProps/ValueProp.cs` |
