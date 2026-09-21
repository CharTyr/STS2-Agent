# Knowledge Base Plan

The current knowledge base has two layers:

- Static index layer
  - Directly generated from `extraction/decompiled`
  - Solves "what is this id or internal name"
- Decision support layer
  - Tells MCP agents how to use the indexes
  - Avoids mixing static facts with live game state

## Current Files

- [README.md](./README.md)
- [agent-reference.md](./agent-reference.md)
- [playbook.md](./playbook.md)
- Generated indexes:
  - `characters.md`
  - `cards.md`
  - `card-behaviors.md`
  - `monsters.md`
  - `monster-behaviors.md`
  - `potions.md`
  - `potion-behaviors.md`
  - `events.md`

## Regeneration

```powershell
powershell -ExecutionPolicy Bypass -File "scripts/generate-sts2-knowledge.ps1"
```

## Next Steps

1. ~~Add character ownership and more human-readable effect summaries for cards.~~ **Done 2026-09-20.** `cards.md` and `card-behaviors.md` carry an `Owner` column (the character whose card pool declares the card; a pool no character owns shows that pool's title) and an `Effect` column. Numbers come from the card's `CanonicalVars`, amounts from the expressions actually passed to the command calls; an amount that cannot be resolved statically is `?` rather than a guess, and a call with no mapping falls back to a readable form of its own name.
2. ~~Add risk tags and choice semantics for events.~~ **Done 2026-09-20.** `events.md` gained an `Option Risk Details` table: one row per option the event builds, with its handler, effect, cost, risk grade, and whether choosing it ends the event, moves to another page, or repeats. Grades are `lethal-possible` (the game itself marks the option via `ThatDoesDamage` / `ThatWillKillPlayerIf`), `harmful`, `costly`, `none-detected`, `locked`, and `unknown`, and the file states that `none-detected` is an absence of evidence rather than a guarantee.
3. **Still open:** add route, rest-site, shop, and potion strategy rules once those MCP actions are fully implemented. The strategy rules now exist in [../../skills/sts2-mcp-player/references/strategy.md](../../skills/sts2-mcp-player/references/strategy.md); what remains is feeding them to an agent through `get_relevant_game_data`'s scene derivation instead of requiring a file read.
