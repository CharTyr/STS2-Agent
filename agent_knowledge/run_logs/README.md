# STS2 Run Decision Logs

Codex should create one run log in this directory whenever it plays or continues a Slay the Spire 2 run for the user.

The log is for later review, so record decision intent as the run progresses. Keep entries brief enough that logging does not block play.

## File Naming

Use this pattern when possible:

```text
YYYYMMDD-HHMM_<character>_<seed>.md
```

If the seed or character is not available yet, use `unknown-seed` or `unknown-character`, then update the header once the state exposes the value.

## Minimum Header

```markdown
# STS2 Run Decision Log

- Date:
- Seed:
- Character:
- Ascension:
- Goal:
- Result:
- Log mode: sync brief notes after each meaningful decision; batch flush at room boundaries if needed.

## Route Summary

| Act/Floor | Options | Choice | Reason | Risk/Backup |
| --- | --- | --- | --- | --- |

## Stage Decisions

| Step | Floor/Screen | State Snapshot | Options Considered | Decision | Reason | Result/Follow-up |
| --- | --- | --- | --- | --- | --- | --- |
```

## What To Record

- Seed, character, ascension, goal, and final result.
- Route choices and route changes, especially elites, rests, shops, events, and boss preparation.
- Card rewards, relic rewards, potion choices, shop buys/removes, event branches, rest-site choices, and chest choices.
- Combat decisions at a useful granularity: potion use, key power setup, lethal planning, block-vs-damage tradeoffs, forced-end states, and risky turns.
- For routine card play, a compact turn entry is acceptable: list the chosen sequence and why it was better than the main alternative.
- Any MCP/action anomaly, stale-state concern, forced-end condition, or payload mismatch that affected the decision.

## Logging Rules

- Read fresh state before deciding, then log the decision with the state snapshot that mattered.
- Logging must not become a prerequisite for legal gameplay. If the run is in a fast action window, play first and flush the note immediately after the returned state stabilizes.
- Prefer one short row per meaningful decision over long prose.
- If the run is interrupted, leave the log with the latest known floor, screen, HP, deck/relic highlights, and next intended action.
