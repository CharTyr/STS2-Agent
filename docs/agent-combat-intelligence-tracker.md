# Agent Combat Intelligence - Improvement Tracker

**Branch:** `feature/agent-combat-intelligence`
**Goal:** Enable the AI agent to beat the Act 1 Boss
**Created:** 2026-03-20

---

## Phase 1: Let the Agent "See" (Priority: Critical)

| # | Task | File(s) | Status | Notes |
|---|------|---------|--------|-------|
| 1.1 | Expose computed damage/block on hand cards | `GameStateService.cs` | ✅ DONE | Via TryExtractCardValues reflection |
| 1.2 | Ensure draw/discard/exhaust piles always exposed | `GameStateService.cs` | ✅ DONE | Already in BuildAgentCombatPayload |
| 1.3 | Add combat_analysis pre-computation in MCP | `server.py` | ✅ DONE | Lethal calc, max damage/block |
| 1.4 | Replace template strings with actual values in agent_view | `GameStateService.cs` | ✅ DONE | GetCardFormattedDescription + resolved_text |

## Phase 2: Let the Agent "Plan Ahead" (Priority: High)

| # | Task | File(s) | Status | Notes |
|---|------|---------|--------|-------|
| 2.1 | Expose enemy move rotation + current index | `GameStateService.cs` | ✅ DONE | move_history + turn_count via reflection |
| 2.2 | Add deck-building strategy to SKILL.md | `SKILL.md` | ✅ DONE | Ironclad archetypes + combat heuristics |
| 2.3 | Add route planning heuristics | `SKILL.md`, `server.py` | ✅ DONE | HP thresholds, elite/shop/rest rules |
| 2.4 | Add Boss-specific strategy guide | `docs/game-knowledge/` | ✅ DONE | Queen, Lagavulin, MechaKnight, OwlMagistrate |

## Phase 3: Let the Agent "Strategize" (Priority: Medium)

| # | Task | File(s) | Status | Notes |
|---|------|---------|--------|-------|
| 3.1 | Run-level strategy context persistence | `server.py`, `handoff.py` | TODO | Track build direction |
| 3.2 | Card pick recommendation tool | `server.py` | ✅ DONE | `evaluate_card_rewards` tool |
| 3.3 | Boss preparation check tool | `server.py` | ✅ DONE | `check_boss_readiness` tool |
| 3.4 | Economy management rules | `SKILL.md` | ✅ DONE | Gold priority, saving rules, skip rules |

## Phase 4: Polish (Priority: Low)

| # | Task | File(s) | Status | Notes |
|---|------|---------|--------|-------|
| 4.1 | Cross-run knowledge learning | `knowledge.py` | TODO | What works vs what Boss |
| 4.2 | Elite fight risk assessment | `server.py` | ✅ DONE | `assess_elite_risk` tool |
| 4.3 | Potion timing optimization | `SKILL.md` | ✅ DONE | Boss/elite/lethal timing rules |
| 4.4 | Full character coverage | `SKILL.md`, `docs/` | TODO | Silent, Regent, etc. |

---

## Progress Log

### 2026-03-20
- Created feature branch `feature/agent-combat-intelligence`
- Completed analysis: identified 5-layer bottleneck model
- Critical finding: agent cannot see computed damage/block values (template strings only)
- Starting Phase 1.1: expose computed card values in C# mod
- **Phase 1.3 DONE**: Added `get_combat_analysis` MCP tool in `server.py`
  - Helper functions: `_get_power_amount`, `_compute_card_damage`, `_compute_card_block`
  - Main function: `_compute_combat_analysis` — cross-references live state with static card data
  - Computes per-card `computed_damage`, `computed_block`, `damage_per_energy`
  - Tactical summary: max damage/block, per-enemy lethal check, incoming damage analysis
  - Accounts for Strength, Dexterity, Weak, Frail, Vulnerable modifiers
  - Note: needs live testing when game is available
- **Phase 1.2 DONE**: Draw/discard/exhaust piles already exposed via `BuildAgentCombatPayload` in C#
- **Phase 2.2 DONE**: Added `ironclad-strategy.md` — deck archetypes, card tiers, Act 1 priorities
- **Phase 2.3 DONE**: Route planning heuristics added to SKILL.md — HP thresholds, elite/shop/rest rules
- **Phase 2.4 DONE**: Added `act1-boss-guide.md` — Queen, Lagavulin Matriarch, MechaKnight, OwlMagistrate strategies
- Updated SKILL.md combat heuristics — integrated `get_combat_analysis` tool into turn workflow
- **Phase 3.2 DONE**: Added `evaluate_card_rewards` MCP tool + `_score_card_for_deck` scoring function
  - Scores cards by deck composition balance, efficiency, draw, AoE gap-filling
- **Phase 3.4 DONE**: Economy management rules added to SKILL.md
  - Gold spending priority, saving rules, skip reward heuristics
- **Phase 3.3 DONE**: Added `check_boss_readiness` MCP tool + `_check_boss_readiness` function
  - Checks: HP, deck size, damage output, block coverage, vulnerable sources, draw, potions
  - Returns pass/fail per check + overall readiness score
- **Phase 4.2 DONE**: Added `assess_elite_risk` MCP tool + `_assess_elite_risk` function
  - TAKE/AVOID recommendation based on HP ratio, deck size, potion count
- **Phase 1.1 DONE**: C# mod — `TryExtractCardValues` reflection extracts Damage/Block/HitCount from CardModel
  - Added `computed_damage`, `computed_block`, `hit_count`, `card_type` to CombatHandCardPayload
  - Agent view now includes `dmg`, `blk`, `hits`, `type` fields per hand card
- **Phase 1.4 DONE**: C# mod — `GetCardFormattedDescription` resolves template strings via GetFormattedText()
  - `resolved_text` field added to CombatHandCardPayload (actual numbers instead of `{Damage:diff()}`)
  - Agent view `line` now uses resolved text when available
- **Phase 2.1 DONE**: C# mod — `TryExtractMoveHistory` reflection extracts move history from Monster
  - Added `move_history` (string[]) and `turn_count` to CombatEnemyPayload
  - Python combat analysis updated to prefer C#-computed values over static data fallback
- **All C# changes need game testing** — reflection-based extraction depends on actual game class structure
