# Protocol v2: authoritative fact events and schemas

Protocol `2026-08-31-v2` adds an authoritative event layer for clients that need to reconstruct combat without inferring actions from polled snapshots. State version is `12`; compact agent-view version is `7`.

## Published schemas

All schemas use JSON Schema Draft 2020-12 and ship as embedded resources in the mod.

| Endpoint | Schema |
| --- | --- |
| `GET /health` | `GET /schemas/health` |
| `GET /state` response data | `GET /schemas/state` |
| each `GET /events/stream` SSE data object | `GET /schemas/event` |
| each `/data/{collection}` response data array | `GET /schemas/data-collection` |

`GET /schemas` lists the published names. The state schema enumerates every supported `screen` and conditionally requires its scene payload.

## Event envelope

Every event has a monotonic sequence and association fields:

```json
{
  "event_id": 42,
  "sequence": 42,
  "protocol_version": "2026-08-31-v2",
  "correlation_id": "combat_4_card_12",
  "run_id": "ABC123",
  "combat_id": "combat_4",
  "type": "action_finished",
  "timestamp_utc": "2026-08-31T10:00:00Z",
  "data": {}
}
```

Legacy state-change events can have null association fields. Authoritative combat lifecycle, card, damage, and AI-decision events carry them.
`stream_ready` is a per-subscription cursor: its `sequence` repeats the latest published sequence, and every subsequent fact on that connection has a greater value.

## Card action lifecycle

Each card play emits:

1. `action_started` from `CombatHistory.CardPlayStartedEntry`;
2. one or more `damage_resolved` events from the deepest `CreatureCmd.Damage` overload;
3. `action_finished` from `CombatHistory.CardPlayFinishedEntry`.

All three stages share one `correlation_id`. `action_finished.damage` aggregates all hits and targets for that card without losing the individual `damage_resolved` records.

```json
{
  "type": "action_finished",
  "correlation_id": "combat_4_card_12",
  "data": {
    "action_type": "card_play",
    "actor": {
      "entity_id": "player_2",
      "entity_type": "player",
      "model_id": "SILENT",
      "name": "Silent",
      "player_id": "2",
      "is_local": false
    },
    "source": {
      "source_type": "card",
      "source_id": "POISON_STAB",
      "source_name": "Poisoned Stab",
      "card_instance_id": "combat_17"
    },
    "target": {
      "entity_id": "creature_8",
      "entity_type": "enemy",
      "model_id": "CULTIST",
      "name": "Cultist",
      "player_id": null,
      "is_local": false
    },
    "card": {
      "card_id": "POISON_STAB",
      "card_instance_id": "combat_17",
      "name": "Poisoned Stab",
      "upgraded": false,
      "auto_play": false,
      "play_index": 1,
      "play_count": 1
    },
    "damage": {
      "targets": [
        {
          "target_id": "creature_8",
          "target_name": "Cultist",
          "blocked_damage": 3,
          "hp_loss": 6,
          "overkill_damage": 0,
          "hit_count": 1
        }
      ],
      "total_blocked_damage": 3,
      "total_hp_loss": 6,
      "total_overkill_damage": 0,
      "hit_count": 1
    }
  }
}
```

`hp_loss` is the actual HP removed after block. Requested amount, blocked damage, and overkill remain separate.

## Exact damage source attribution

`damage_resolved.source.source_type` is one of `card`, `power`, `relic`, `potion`, `orb`, `monster`, or `unknown`. The instrumentation scopes the real model method that calls `CreatureCmd.Damage`; it does not infer a source from adjacent snapshots or a retained player-choice context. If no callsite or explicit card source proves ownership, `source_attribution` is `unavailable` and `source` is null.

This distinction allows a client to keep poison ticks, power callbacks, relic effects, potion effects, orb effects, monster effects, and direct card damage separate even when they change the same target HP in immediate succession.

## Optional AI teammate telemetry

When the optional `sts2AITeammate` assembly is present, the mod patches its `DecideAsync(AiDecisionRequest, ...)` backend without adding a static dependency. `ai_decision` contains the chosen action, ranking, exact card instance, target, and reason already retained by that mod.

```json
{
  "type": "ai_decision",
  "correlation_id": "decision_req_7",
  "data": {
    "request_id": "req-7",
    "snapshot_id": "snapshot-3",
    "actor_id": "2",
    "actor_role": "ai_teammate",
    "chosen_action_id": "play_card_combat_17_target_creature_8",
    "ranked_action_ids": ["play_card_combat_17_target_creature_8", "end_turn_player_2"],
    "reason": "Remove the attacking target first.",
    "chosen_action": {
      "card_id": "POISON_STAB",
      "card_instance_id": "combat_17",
      "target_id": "creature_8",
      "target_label": "Cultist",
      "energy_cost": 1
    },
    "provenance": "optional_ai_teammate_decision_backend"
  }
}
```

When the chosen card is executed, its exact `card_instance_id` lets the decision, card lifecycle, and damage records reuse the same correlation ID. The base mod continues to work when the optional assembly is absent.

## Encounter and outcome facts

Combat state and lifecycle events expose `encounter_id` plus `encounter_type`. Combat values are `normal`, `elite`, or `boss`, sourced from `Encounter.RoomType`.

`combat_ended` always includes `outcome`, `victory`, and `outcome_source`. A victory is recorded only after the game's `CombatManager.CombatWon` event. `CombatEnded` without that event is recorded as defeat, so clients no longer need to treat “combat ended” as proof of a win.

## Complete multiplayer inventories

Every `combat.players[]` entry now includes powers, orbs, hand, draw/discard/exhaust/play piles, full run deck, relics, and potions. Every `run.players[]` entry includes the full deck, relics, potions, gold, maximum energy, and base orb slots. `is_local` remains the ownership boundary.

## Static data collections

The existing read-only exports remain available at:

- `/data/cards`
- `/data/relics`
- `/data/monsters`
- `/data/potions`
- `/data/events`
- `/data/powers`
- `/data/characters`

Use the raw records for forward-compatible fields and `data-collection.schema.json` for the stable minimum shape.
