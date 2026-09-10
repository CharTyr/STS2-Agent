# Evidence: API doc and action contract alignment

Runner: main session. Date: 2026-09-10. All commands offline; no game instance was started.

## What was wrong

`docs/api.md` documented 21 actions in detail and mentioned a few more in a supplementary table.
`GameActionService.ExecuteAsync` accepts 55. Eighteen actions the mod accepts appeared nowhere in
the document: resolve_rewards, switch_profile, continue_game_over, dismiss_game_over_wait,
save_and_quit, confirm_unlock, close_cards_view, confirm_selection, choose_capstone_option,
choose_bundle, confirm_bundle, host_multiplayer_lobby, join_multiplayer_lobby,
ready_multiplayer_lobby, disconnect_multiplayer_lobby, increase_ascension, decrease_ascension,
invite_ai_teammate. The document also carried a stale protocol version (2026-03-10-v0 vs the
2026-03-11-v1 the router returns), omitted the `command` and `player_id` request fields, and
listed a `status` enum without the `failed` value the payload can carry.

## Change

- `docs/api.md`: new "动作总表" section between the `POST /action` heading and the request-body
  table, wrapped in `<!-- BEGIN ACTION CONTRACT -->` / `<!-- END ACTION CONTRACT -->`, listing all 55
  actions in the `- \`name\` — purpose` format.
- `docs/api.md`: protocol version corrected to `2026-03-11-v1`; `command` and `player_id` request
  fields added; `status` enum extended with `failed`; four error codes that the code can return were
  added to the error table (`forbidden_actor`, `mcp_disabled`, `session_not_ready`, `pause_pending`).
- `scripts/check_verification_gates.py`: the `api-doc` gate parses the action switch in
  `STS2AIAgent/Game/GameActionService.cs` and the marked block in `docs/api.md` and requires set
  equality in both directions.

## Evidence

| Command | Result |
| --- | --- |
| `python scripts/check_verification_gates.py --only api-doc` | exit 0, "55 actions match between GameActionService.cs and docs/api.md" |
| `powershell -File scripts/test-verification-gates.ps1` | case "api-doc drift rejects undocumented action" PASS (removing one line from the block fails the gate) and "api-doc drift rejects phantom action" PASS (adding an action the code does not accept fails the gate) |

## Boundary

The gate proves the document and the dispatch switch agree. It does not prove that each action
works in game; per-action behaviour still needs the game-connected suites.

