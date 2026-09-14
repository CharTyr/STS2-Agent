# STS2 Game Knowledge Base

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-03-10 23:47:37 +08:00

Local AI-facing indexes generated from the current repository's decompiled STS2 data.

## Coverage

- Characters: 7
- Cards: 577
- Monsters: 121
- Potions: 64
- Events: 68

## Usage

- Prefer these indexes when MCP returns card_id, enemy_id, event_id, or potion_id.
- Read docs/game-knowledge/agent-reference.md first, then inspect the specific index file.
- Use card-behaviors.md, monster-behaviors.md, and potion-behaviors.md when metadata alone is too thin for action choice.
- Refresh this knowledge base after game updates by running powershell -ExecutionPolicy Bypass -File "scripts/generate-sts2-knowledge.ps1".
- How these indexes are put together, and what belongs in each of them: [knowledge-plan.md](./knowledge-plan.md).
