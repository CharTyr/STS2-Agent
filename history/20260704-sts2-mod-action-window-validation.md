# STS2 Run Decision Log

- Date: 2026-07-04
- Seed: 8J3Z9VA668
- Character: Defect
- Ascension: 10
- Goal: Validate Mod action-window fix after Steam launch.
- Result: Mod loaded successfully after repacking PCK with Godot 4.5.1; action-window fix verified during turn 2 draw.
- Log mode: compact validation notes.

## Route Summary

| Act/Floor | Options | Choice | Reason | Risk/Backup |
| --- | --- | --- | --- | --- |
| Floor 8 / Combat | Continue existing run or stop at main menu | Continued run | Needed a live combat draw transition to verify `available_actions` gating | Stop after validation; do not continue gameplay decisions |

## Stage Decisions

| Step | Floor/Screen | State Snapshot | Options Considered | Decision | Reason | Result/Follow-up |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Main Menu | `continue_run` available | Continue or only verify API | `continue_run` | Needed live combat state after Steam launch | Entered floor 8 combat, seed `8J3Z9VA668`, Defect A10 |
| 2 | Combat Turn 1 | Initial combat transition showed hand 0, energy 0/3, actions only `save_and_quit,discard_potion` | Wait or act | Wait/sample | Verify no combat action is exposed during draw transition | Stable state later showed hand 5, energy 3, actions included `play_card,end_turn` |
| 3 | Combat Turn 1 -> 2 | Enemy intent was status-card insertion, no direct damage | End turn for validation or stop | `end_turn` | Needed to observe next-turn draw window | During turn 2 draw, hand counts 0,1,2,3,4,5 exposed only passive actions; `play_card,end_turn` appeared only after hand 5 and stable delay |
