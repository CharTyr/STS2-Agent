# Execution plan

1. Source contract for the in-combat guard (`CombatGate.QueueReadIsCombatOnly`), with both
   destructive cases run before it is kept. -- done
2. `docs/api.md`: `combat` top-level table, `combat.action_readiness` table plus the full `reason`
   vocabulary, `combat.lethal_risks[]` table, and a note in the compact `agent_view` section saying
   those three are carried through verbatim. -- done
3. `api-facts` gate: pin all three tables to their records and the reason list to the gate method;
   add three destructive cases to the gate self-test. -- done
4. `scripts/lib-build-fingerprint.ps1` plus calls from both packaging scripts. -- done
5. `## Unreleased` in CHANGELOG, with the convention written into CONTRIBUTING, AGENTS.md and the
   operations spec; cross-version index at `history/build-fingerprints.md`. -- done
6. Play skill guidance for `action_readiness`. -- done
7. Status page: post-tag "主线未发布" section per rule 3 of its own maintenance rules. -- done
8. Full offline validation and the health sweep. -- done, see evidence.md

## Rollback

Every item is additive and independent. Reverting the commit restores the previous state; no
runtime code is involved, so no rebuild or republish is needed either way.
