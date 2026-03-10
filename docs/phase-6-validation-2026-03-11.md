# Phase 6 Validation Record

- Validation date: `2026-03-11`
- Validator: Codex
- Git commits: `69ed5c2`, `26cd9b0`
- Game version: `v0.98.3`
- Mod build: `Release`
- MCP mode: local stdio server + local HTTP mod
- Note: part of the overnight validation started on `v0.98.2`; the final recheck and this record were completed on `v0.98.3`

## Static Checks

- `dotnet build "C:/Users/chart/Documents/project/sp/STS2AIAgent/STS2AIAgent.csproj" -c Release` passed
- `python -m py_compile "C:/Users/chart/Documents/project/sp/mcp_server/src/sts2_mcp/client.py" "C:/Users/chart/Documents/project/sp/mcp_server/src/sts2_mcp/server.py"` passed
- `powershell -ExecutionPolicy Bypass -File "C:/Users/chart/Documents/project/sp/scripts/preflight-release.ps1"` passed
- `powershell -ExecutionPolicy Bypass -File "C:/Users/chart/Documents/project/sp/scripts/test-mod-load.ps1" -DeepCheck` passed

Key output summary:

```text
preflight-release.ps1: OK
test-mod-load.ps1 -DeepCheck: {"health_ok":true,"state_ok":true,"actions_ok":true}
```

## Real-Game Validation

### Main menu and start flow

- `MAIN_MENU -> open_timeline -> close_main_menu_submenu` passed
- `MAIN_MENU -> open_character_select -> select_character -> embark` passed
- `embark` now lands in `EVENT` (`NEOW`) rather than dropping to `UNKNOWN`
- After commit `26cd9b0`, `embark` action payload was revalidated and returned `screen="EVENT"`

### Continue / resume flow

- `continue_run` successfully restored an in-progress run multiple times
- Follow-up `GET /state` stabilized to the correct room state (`REWARD`, `MAP`, `SHOP`, etc.)
- After commit `26cd9b0`, `continue_run` action payload itself was revalidated and returned a stable non-`UNKNOWN` screen

### Combat and consumables

- Entered combat, played through combat state transitions, and used `run_console_command "win"` to accelerate validation
- `use_potion` was validated in real combat earlier in the session
- `discard_potion` remained available and state updates were consistent across non-combat rooms

### Reward flow

- `collect_rewards_and_proceed` passed
- Reward -> Map transitions remained stable after debug-fast-forwarded combats

### Shop flow

- `run_console_command "room Shop"` used to jump directly into the room
- `open_shop_inventory` passed
- `buy_card` passed
- `buy_potion` passed
- `buy_relic` passed
- `remove_card_at_shop -> select_deck_card` passed
- `close_shop_inventory -> proceed` passed

Observed state changes:

- gold decreased correctly
- purchased inventory entries were marked out of stock
- deck count changed after purchase/removal
- potion occupancy and relic count updated correctly

### Rest flow

- `choose_rest_option -> HEAL` passed after fix
- `HEAL` now restores HP and exposes `proceed`
- `choose_rest_option -> SMITH -> select_deck_card` passed after fix
- upgraded card state returned in `run.deck[]`
- `REST -> proceed -> MAP` passed

### Chest flow

- `open_chest` passed after fix
- nested relic collection is now surfaced through `chest.relic_options[]`
- `choose_treasure_relic` passed
- `CHEST -> proceed -> MAP` passed

### Event flow

- Standard event flow was already validated on `NEOW`
- Nested event combat validated with:

```text
run_console_command "event BATTLEWORN_DUMMY"
choose_event_option(1)
run_console_command "win"
choose_event_option(0)
```

Result:

- `EVENT -> COMBAT -> EVENT(is_finished=true) -> MAP` passed

### End-of-run flow

- `run_console_command "die"` passed
- `GAME_OVER` payload was present
- `return_to_main_menu` passed

## Findings

| Severity | Area | Issue | Repro |
| --- | --- | --- | --- |
| P2 | Start-of-run branch coverage | `deck_transform_select` / `deck_enchant_select` support was added to state recognition, but the final commit was not independently re-smoked on a dedicated transform/enchant branch after the patch landed | start a fresh run and force a transform/enchant card-selection branch |

## Conclusion

- Current status: `canary / gray release candidate`
- Not yet at "formal release complete"

Reason:

- The major gameplay chain is now largely covered and the biggest blockers found during real testing were fixed in commits `69ed5c2` and `26cd9b0`
- Static checks pass and most room chains now pass in live runs
- A final clean rerun on the latest commit is still needed for the remaining start-of-run transform/enchant selection branches

Recommended next step:

1. Re-run one fresh-run validation on commit `26cd9b0`
2. Force a transform or enchant deck selection branch and confirm `screen="CARD_SELECTION"` plus `selection.kind`
3. If those branches also pass, re-evaluate whether the project can move from gray release candidate to formal release
