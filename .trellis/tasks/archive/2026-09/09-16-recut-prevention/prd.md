# Close the gaps that let four same-version re-cuts happen

## Problem

Between 2026-09-13 and 2026-09-16 two version numbers were each published three times: v0.12.3 was
re-cut twice and v0.12.4 was re-cut twice. Three builds answer to `0.12.4` and the version string
never tells them apart.

The second v0.12.4 re-cut is the one that matters. It fixed a regression the *first* re-cut had
introduced: `EvaluateCombatActionGate` runs before the screen checks so one evaluation can answer
every caller, which put `RunManager.Instance.ActionExecutor` / `ActionQueueSet` ahead of everything
that knew a fight was up. On the main menu `RunManager` has neither, so **every `/state` request
failed with a NullReferenceException** and the mod was unusable outside combat. That build passed
408 C# tests and all nine offline gates, and it was live on the Workshop for about an hour.

Three separate things were missing, and each is independently worth closing:

1. Nothing pinned the in-combat guard, so the same mistake can be made again.
2. `docs/api.md` never documented the `/state` `combat` object's own fields, so the very field that
   fix is about (`action_readiness`) was invisible to every client, along with `players[]`,
   `end_turn_will_kill_player` and `lethal_risks[]` -- all of which reach agents through the compact
   `agent_view`.
3. Every re-cut began as a post-tag fix with nowhere in the changelog to live, and the numbers that
   distinguish the resulting builds were collected by hand after each upload.

## Acceptance criteria

- A source contract fails if the action-queue read leaves the in-combat guard, or if a second call
  site in the state builder starts reading those members. Proven by destructive verification.
- `docs/api.md` documents `CombatPayload`, `CombatActionReadinessPayload` and
  `CombatLethalRiskPayload` field-for-field and every `reason` code the gate can emit, with what
  each one means for an agent deciding whether to wait.
- The `api-facts` gate fails by name when any of those tables drifts from the record that produces
  it, in either direction. Proven by three destructive cases in the gate self-test.
- Packaging writes a fingerprint (per-file SHA256, summed bytes, source commit, dirty flag) beside
  every artifact, and a cross-version index explains how to identify a build from a bug report.
- `CHANGELOG.md` has an `## Unreleased` section and the convention is written into CONTRIBUTING,
  AGENTS.md and the operations spec.
- The play skill tells an agent how to read `action_readiness`.
- No runtime mod code changes, so the published build is untouched.

## Second pass: the rest of the /state contract

Writing the combat tables raised the obvious question -- if `combat` was undocumented, what else is?
An audit of every payload record against `docs/api.md` answered it: **91 fields across 23 records
were named nowhere a client could read**, including seven sub-structures with no section at all
(`session`, `multiplayer`, `multiplayer_lobby`, `character_select`, `timeline`, `modal`,
`game_over`) and nine fields missing from the top-level table. Three of those are screens an agent
has to drive itself, and `session` is the block the play skill names as the first routing decision.

Two more gaps came out of the same audit:

- The compact `agent_view` renames 43 keys and is what MCP `get_game_state` returns by default. A
  client following the documented `/state` names reads `undefined`, not an error. The mapping lived
  only in the builder methods.
- `docs/api.md` documented an `available` field on `shop.cards[]` / `relics[]` / `potions[]` that
  none of those records has ever had. That one is a documentation *error*, not a gap.

### Additional acceptance criteria

- The audit reports 0 undocumented fields.
- The gate covers the whole surface, not the three combat records: per-table checks for sixteen
  records, `GET /health` keys, the rename table against the builders, and a coarse net over every
  field of every record.
- Six new destructive cases, and the existing ones still pass.

## Out of scope

- Live validation. This round changes documentation, tests and scripts only; the live conclusions
  for the current build stand as recorded for the 0.12.4 third build.
- Publishing. The branch is prepared locally; pushing and opening the PR is the maintainer's call.
