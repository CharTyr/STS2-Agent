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
  - `relics.md`
  - `powers.md`
  - `events.md`

## Regeneration

```powershell
powershell -ExecutionPolicy Bypass -File "scripts/generate-sts2-knowledge.ps1"
```

## Next Steps

1. ~~Add character ownership and more human-readable effect summaries for cards.~~ **Done 2026-09-20.** `cards.md` and `card-behaviors.md` carry an `Owner` column (the character whose card pool declares the card; a pool no character owns shows that pool's title) and an `Effect` column. Numbers come from the card's `CanonicalVars`, amounts from the expressions actually passed to the command calls; an amount that cannot be resolved statically is `?` rather than a guess, and a call with no mapping falls back to a readable form of its own name.
2. ~~Add risk tags and choice semantics for events.~~ **Done 2026-09-20.** `events.md` gained an `Option Risk Details` table: one row per option the event builds, with its handler, effect, cost, risk grade, and whether choosing it ends the event, moves to another page, or repeats. Grades are `lethal-possible` (the game itself marks the option via `ThatDoesDamage` / `ThatWillKillPlayerIf`), `harmful`, `costly`, `none-detected`, `locked`, and `unknown`, and the file states that `none-detected` is an absence of evidence rather than a guarantee.
3. ~~Add relic and power indexes, and fill the monster HP ranges.~~ **Done 2026-09-21.** `relics.md` carries rarity, owner pool, and a hook-labelled effect for all 289 relics; `powers.md` carries type, `StackType`, the hooks a power overrides, and the effect each hook produces for all 257 concrete powers. `monsters.md` went from 15 filled HP rows to all 121: the generator now resolves `AscensionHelper.GetValueIfAscension` (keeping the non-ascension value, since the table prices a base run), a `MaxInitialHp` that reads `MinInitialHp`, a property named in the same class (`TestSubject.FirstFormHp`), and HP inherited from a base class (`MysteriousKnight` from `FlailKnight`, the three `DecimillipedeSegment*` from `DecimillipedeSegment`). Effects are still derived only from declarations — a hook body with no command and no readable return is written as `no hook effect detected in source` rather than described.
4. ~~Give every monster move its own effect summary, with amounts.~~ **Done 2026-09-21.** `monster-behaviors.md` ends each move with `->` and what the move does, read from the move's own method the way `card-behaviors.md` reads a card's `OnPlay`, so an intent that carries no numbers lands on the number: `SHARPEN_MOVE=BuffIntent -> Gain 4 Strength (StrengthPower)`. A power is named twice on purpose - the readable name and the class `powers.md` indexes - and an amount that names a member of the monster's own class (`private int CrushStrength => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 4, 3)`) is read at its base (non-ascension) value, the convention `monsters.md` uses for HP. `?` marks an amount that only exists while the fight runs (5 moves: a heal scaled by the number of players in the combat, a damage field the fight assigns, a hit count behind a lambda, a damage computed from pressure). The move scan had only read a declaration that closed after one intent, so 114 moves were missing; all 358 are listed now, across all 121 rows including the four that inherit their base's state machine (`DecimillipedeSegmentBack`/`Front`/`Middle` from `DecimillipedeSegment`, `MysteriousKnight` from `FlailKnight`). 324 moves carry a resolved effect and 34 read `(no command in move body)` - a body whose only calls are presentation, or whose work lives in a private helper, which is stated rather than guessed. Every power named in a move (28 classes over 145 references) resolves to a row in `powers.md`.
5. **Still open:** add route, rest-site, shop, and potion strategy rules once those MCP actions are fully implemented. The strategy rules now exist in [../../skills/sts2-mcp-player/references/strategy.md](../../skills/sts2-mcp-player/references/strategy.md); what remains is feeding them to an agent through `get_relevant_game_data`'s scene derivation instead of requiring a file read.
