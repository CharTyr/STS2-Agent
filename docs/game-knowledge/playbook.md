# STS2 MCP Playbook

This document is for agents that operate the game through MCP. It is not a full strategy guide. Its job is to reduce the gap between "understanding the state" and "taking the next action".

## Core Rules

- Trust live game state before static knowledge.
- Identify the object first, then decide the action.
- Internal English ids are usually more stable than localized UI text.

## Combat

1. Check which cards in `combat.hand[]` have `playable=true`.
2. If `requires_target=true`, choose from `combat.enemies[]`.
3. For unfamiliar cards:
   - read [cards.md](./cards.md)
   - then read [card-behaviors.md](./card-behaviors.md)
4. Default action priority:
   - secure lethal
   - cover dangerous enemy intent
   - exploit free cards, draw, and strong setup
   - only then take slower value lines

## Potions

- Potion actions are not fully implemented yet, but the knowledge base can still support planning.
- Lookup order:
  - [potions.md](./potions.md)
  - [potion-behaviors.md](./potion-behaviors.md)
- `Usage=CombatOnly` usually means burst or tactical combat value.
- `Usage=AnyTime` usually means healing, route prep, or broader utility.

## Monsters

- Read real-time `intent` first. Do not rely only on the monster name.
- For unfamiliar `enemy_id`:
  - read [monsters.md](./monsters.md)
  - then read [monster-behaviors.md](./monster-behaviors.md)
- `MultiAttackIntent` usually raises the value of block and damage reduction.
- If the passive summary contains `PowerCmd.Apply<...>`, the monster may enter combat with a built-in mechanic.

## Events

- Check whether `event.options[]` are locked and whether an option is `is_proceed`.
- For unfamiliar events, start with [events.md](./events.md).
- Do not infer outcomes from the event title alone. Use the current option text in the live state.

## Characters

- Read [characters.md](./characters.md) for starting deck, relics, and unlock chains.
- Opening deck and relics strongly affect early route and reward choices.

## Project Capability Boundaries

- Current project support is strongest around combat, map, rewards, chest flow, and event flow.
- `REST`, `SHOP`, `use_potion`, and `GAME_OVER` are still being completed.
- If the knowledge base contains an object but MCP does not yet expose the action, treat it as reference only.
