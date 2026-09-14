# AI Agent Reference

This file is not a gameplay guide. It is a lookup order for agents using the MCP tools.

## Suggested Lookup Flow

1. Read the current `GET /state` or MCP `get_game_state` output.
2. If the state contains unfamiliar `card_id`, `enemy_id`, `event_id`, or `potion_id`, check this knowledge base.
3. Prefer internal English ids for stable matching, then use UI text for final decisions.

## Recommended Mapping

- `combat.hand[].card_id` or `run.deck[].card_id`
  - [cards.md](./cards.md)
  - [card-behaviors.md](./card-behaviors.md)
- `combat.enemies[].enemy_id`
  - [monsters.md](./monsters.md)
  - [monster-behaviors.md](./monster-behaviors.md)
- `event.event_id`
  - [events.md](./events.md)
- `run.potions[].potion_id`
  - [potions.md](./potions.md)
  - [potion-behaviors.md](./potion-behaviors.md)
- character opening state
  - [characters.md](./characters.md)

## Decision Notes

- The knowledge base is for recognition and coarse semantics first, not full card text replacement.
- If a decision depends on exact numbers or current room state, trust the live game state before static docs.
- Event choices, rewards, and future shop flows can change based on relics, upgrades, multiplayer constraints, and current HP.
- Decompiled class names are usually more stable than localized UI strings.
