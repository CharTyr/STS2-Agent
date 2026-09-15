# Live combat loop evidence

Date: 2026-09-15
Isolated host clientId 2026091001 API 18080, companion 18081, game v0.111.0, mod 0.12.3.
Third-party mods parked at %TEMP%/sts2-disabled-mods-20260915-combat for the fight.

## Setup
- continue_ai_teammate on MAIN_MENU returned 409; invite_ai_teammate returned 200 pending then CHARACTER_SELECT.
- Companion API 18081 came up with auto_play=false, session_requests=0.
- Companion Ready closed select_character and advertised unready.
- Host embarked; both reached MAP floor 2 with choose_map_node.

## Floor-2 monster fight
Both voted choose_map_node option_index 0 (Monster). Raw log: drive_log.jsonl (111 events).
- 25x play_card completed, 7x end_turn completed.
- Host first attacks: enemy HP 121 -> 115 -> 109 -> 103; companion 103 -> 97.
- Empty available_actions during enemy/transition turns were polled, not treated as failure.
- Last hits: companion play_card enemy 19 -> 9, then 9 -> dead.
- Host HP 64 -> 60; companion 54 -> 52.

## Rewards to MAP
- 02:20:53 host collect_rewards_and_proceed REWARD -> MAP actions choose_map_node, discard_potion, status completed.
- 02:20:54 companion same path, HP 52, MAP with choose_map_node.

## Invariants
Both 18080 and 18081: screen=MAP, checked_actions=2, failure_count=0, warning_count=0.
No paid model calls. Steam profile hashes captured before launch.

