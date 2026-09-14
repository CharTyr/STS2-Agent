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

1. Add character ownership and more human-readable effect summaries for cards.
2. Add risk tags and choice semantics for events.
3. Add route, rest-site, shop, and potion strategy rules once those MCP actions are fully implemented.
