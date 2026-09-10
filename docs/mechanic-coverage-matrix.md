# Mechanic Coverage Matrix

> 历史快照（Historical snapshot）：本矩阵记录的是 2026-03-11 对 `v0.98.3` 的一次覆盖度盘点，其中的 `release candidate` 结论与 `Remaining gap` 只属于当时，不代表当前状态。当前能力与验收状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)；本文件只作为"机制覆盖度"这一视角的历史证据保留。

Last updated: `2026-03-11`

This document separates protocol-chain coverage from gameplay-mechanic breadth.

- Protocol-chain coverage asks: can the MCP mod move through the game's screens and actions without getting stuck?
- Mechanic coverage asks: do the exposed payloads still stay correct when game rules mutate cards, rewards, resources, and room semantics?

The project is already in a strong `release candidate` state for protocol-chain coverage.
The remaining risk is mechanic breadth, especially around rare card/relic/event interactions and future STS2 patches.

## Coverage Scale

- `High`: directly validated in live play on the current build
- `Medium`: partly validated, but not broadly sampled across variants
- `Low`: protocol exists, but mechanic breadth was not intentionally stressed yet
- `Unknown`: not enough evidence collected yet

## Matrix

| Area | Representative mechanics | Current coverage | Evidence | Remaining gap / next probe |
| --- | --- | --- | --- | --- |
| Main menu gates | `continue_run`, `abandon_run`, timeline gate, FTUE/modal gating | High | Main-menu recovery and timeline unlock flow were validated live | Recheck after major UI patches |
| Reward flow | claim reward, card reward branch, skip card overlay, leave reward room | High | Manual and automatic reward flows were validated; reward-screen `proceed` gap was fixed | Sample more special reward types such as removal and linked rewards |
| Deck selection variants | remove, upgrade, transform, enchant | High | `select_deck_card` now passes live on all four single-select branches | Multi-select or future selection variants still need watchfulness |
| Shop semantics | room vs inventory, purchases, card removal, stock depletion | High | Buy card/relic/potion and remove card were validated live | Price modifiers and unusual sale states need more samples |
| Rest semantics | `HEAL`, `SMITH`, return to room, leave room | High | Both major branches validated live | Rare relic-driven rest options still need sampling |
| Chest semantics | open chest, relic choice, leave room | High | Full chest chain validated live | Multi-choice chest edge cases need future checks if added |
| Event semantics | normal options, finished proceed, nested combat | High | `NEOW` and nested combat event flow validated live | More mutation-heavy events should be sampled |
| Potion lifecycle | use potion, discard potion, empty slots, `TargetedNoCreature` semantics | Medium | Core use/discard flows validated; `FOUL_POTION` shop use was revalidated after the MCP stopped mislabeling it as a required-target potion | Queued and full-belt replacement behaviors need more coverage |
| Dynamic energy cost | combat-time cost changes, temporary discounts | Medium | `Bullet Time` was validated live | Cost increases, generated cards, and cross-turn resets need broader coverage |
| Star resource / star cost | Regent stars, fixed star cost, star-X cost | High | `Falling Star` and `Stardust` validated; `star_costs_x` gap was fixed | More Regent-only interactions should be sampled |
| Unplayable reasons | insufficient energy, insufficient stars | Medium | `not_enough_stars` was validated | Other reasons need explicit sampling and naming review |
| Created / transformed cards in combat | temporary cards, generated cards, copied cards | Medium | `JACK_OF_ALL_TRADES` generated `SALVO` with a stable hand payload; `WHITE_NOISE` generated `NEUTRON_AEGIS` and the hand payload reflected its temporary free cost | Copied cards and transform-in-combat variants still need more coverage |
| Event/relic/card cross-hooks | rewards mutated by relics, event-side deck mutation, rerolls | Low | Basic event and reward hooks are working | Rare hook stacks still need deliberate probes |
| Character breadth | non-Regent characters and their unique resources | Low | Regent was intentionally stressed | Other launch characters still need targeted coverage passes |
| Post-patch resilience | payload/action consistency after a game update | Unknown | Current validation is good for `v0.98.3` | Re-run this matrix after every STS2 content patch |

## Current Release Interpretation

- `Formal protocol completeness`: effectively yes for the validated gameplay chain
- `Mechanic completeness`: no, not in the "every meaningful interaction has been sampled" sense
- `Operational recommendation`: release candidate is appropriate, but keep a mechanic regression pass ready for every game patch

## Highest-Value Next Probes

1. Character breadth
   Probe at least one non-Regent launch character and stress their unique resource or card mechanic.
2. Created-card behavior
   Validate generated temporary cards, copied cards, and transformed cards inside combat state payloads.
3. Broader unplayable reasons
   Intentionally trigger more reason codes and confirm MCP payload strings stay stable and useful.
4. Potion edge cases
   Validate targeted potions, queued potions, and potion-slot overflow / replacement behavior.
5. Relic-driven mutations
   Sample rest, reward, and shop flows while relics modify cost, rewards, or room options.

## Suggested Regression Recipes

Use these in development-only sessions where debug actions are explicitly enabled.

### Reward semantics

1. Enter or resume a reward room.
2. Claim gold.
3. Claim a card reward.
4. Call `skip_reward_cards`.
5. Re-read state.
6. If `reward.can_proceed = true`, call `proceed`.

Expected result:

- `skip_reward_cards` closes only the card overlay
- the underlying card reward may still remain claimable
- `proceed` leaves the reward room when the main reward screen exposes it

### Card-selection variants

1. Force or encounter a remove / upgrade / transform / enchant branch.
2. Confirm `screen = CARD_SELECTION`.
3. Confirm `selection.kind`.
4. Call `select_deck_card`.
5. Confirm transition back to the owning room or event.

Expected result:

- no hanging confirmation state
- no stale `CARD_SELECTION` screen after a valid single-card pick

### Dynamic combat metadata

1. Enter combat with a character/resource combination that can mutate costs.
2. Capture state before and after the cost mutation.
3. Check `energy_cost`, `star_cost`, `costs_x`, `star_costs_x`, and `unplayable_reason`.

Expected result:

- resolved costs update with combat-time state
- X-cost flags remain semantic, not just copied from the current resolved value

## Maintenance Rule

When a new STS2 patch lands, re-run:

1. `scripts/preflight-release.ps1`
2. `scripts/test-mod-load.ps1 -DeepCheck`
3. `scripts/test-state-invariants.ps1`
4. the Phase 6 manual flow
5. the targeted probes in this matrix
