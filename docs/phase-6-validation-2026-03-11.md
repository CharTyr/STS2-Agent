# Phase 6 Validation Record

- Validation date: `2026-03-11`
- Validator: Codex
- Git commits: `69ed5c2`, `26cd9b0`, `588d939`, `637deaa`
- Game version: `v0.98.3`
- Mod build: `Release`
- MCP mode: local stdio server + local HTTP mod
- Note: part of the overnight validation started on `v0.98.2`; the final recheck and this record were completed on `v0.98.3`

## Static Checks

- `dotnet build "<repo-root>/STS2AIAgent/STS2AIAgent.csproj" -c Release` passed
- `python -m py_compile "<repo-root>/mcp_server/src/sts2_mcp/client.py" "<repo-root>/mcp_server/src/sts2_mcp/server.py"` passed
- `powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/preflight-release.ps1"` passed
- `powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-mod-load.ps1" -DeepCheck` passed
- `powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1"` passed
- `powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1" -EnableDebugActions` passed

Key output summary:

```text
preflight-release.ps1: OK
test-mod-load.ps1 -DeepCheck: {"health_ok":true,"state_ok":true,"actions_ok":true}
test-debug-console-gating.ps1: disabled -> invalid_action, enabled -> completed
```

Additional tooling note:

- `scripts/build-mod.ps1` now fails fast if `dotnet build` or the Godot PCK step returns a non-zero exit code; it no longer silently installs a stale DLL after a failed build

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
- Startup stability was revalidated after the listener retry patch:
  - two back-to-back `start-game-session.ps1 -EnableDebugActions` runs both reached healthy `/health`
  - `test-mod-load.ps1 -DeepCheck` passed after each restart
  - the latest game log showed only `Listening on http://127.0.0.1:8080/` and no prefix-conflict error

### Combat and consumables

- Entered combat, played through combat state transitions, and used `run_console_command "win"` to accelerate validation
- `use_potion` was validated in real combat earlier in the session
- `discard_potion` remained available and state updates were consistent across non-combat rooms
- `FOUL_POTION` was revalidated in `SHOP` after a protocol fix:
  - before the fix, the payload incorrectly exposed `requires_target = true` and hid `use_potion`
  - after the fix, `TargetedNoCreature` potions no longer require `target_index`
  - live `use_potion` in `SHOP` succeeded and removed the potion from the belt

### Dynamic combat metadata

- Regent combat was revalidated specifically for stars / star-cost behavior
- `Falling Star` correctly reported `star_cost=2` and became `playable=false` with `unplayable_reason="not_enough_stars"` after stars were exhausted
- `Stardust` was used to verify star-X behavior
- A protocol gap was found and fixed in commit `637deaa`: the state previously exposed `star_cost` but did not distinguish star-X cards from fixed-cost star cards
- After the patch, `Stardust` now reports `star_costs_x=true` while preserving the current resolved `star_cost`
- `Bullet Time` was revalidated against live combat state, and the MCP payload reflected in-combat cost changes after the card resolved
- Generated-card combat behavior was also sampled live:
  - `JACK_OF_ALL_TRADES` generated `SALVO` into hand, and the new hand card exposed a stable `card_id`, target metadata, and playability state
  - `WHITE_NOISE` generated `NEUTRON_AEGIS`; the hand payload reported `energy_cost = 0` while the card database baseline is `1`, confirming that temporary free-this-turn modifiers survive into MCP state

### Debug gating

- Debug console tooling was revalidated in commit `588d939`
- Direct mod HTTP action validation confirmed:
  - default release behavior: `run_console_command` returns `invalid_action`
  - development behavior with `STS2_ENABLE_DEBUG_ACTIONS=1`: command succeeds
- MCP server registration was also checked locally:
  - without the env var, `run_console_command` is not registered
  - with the env var, `run_console_command` is registered

### State invariant smoke check

- `scripts/test-state-invariants.ps1` was added as a lightweight payload/action drift check
- It passed on both `MAIN_MENU` and `REWARD` during the latest follow-up validation
- The reward-screen run also confirmed that `reward.can_proceed = true` now implies MCP-side `proceed`
- The script now also guards potion semantics:
  - any usable potion, including non-combat potions, must expose `use_potion`
  - `TargetedNoCreature` potions must not report `requires_target = true`
- The script also now respects the shop's two-layer model:
  - closed shop room: `open_shop_inventory` is expected, but `buy_*` actions are not
  - open merchant inventory: `buy_*` / `remove_card_at_shop` are expected when the payload is affordable

### Reward flow

- `collect_rewards_and_proceed` passed
- Reward -> Map transitions remained stable after debug-fast-forwarded combats
- Manual reward flow was revalidated on the latest build:
  - `claim_reward` for gold updated `run.gold` correctly
  - `claim_reward` for a card reward entered `pending_card_choice = true`
  - `skip_reward_cards` dismissed the card-choice overlay and returned to the reward list
  - card rewards can intentionally remain claimable after `skip_reward_cards`; this mirrors the game's native `DismissScreenAndKeepReward` behavior
  - a protocol gap was found and fixed during this recheck: when `reward.can_proceed = true`, the MCP now also exposes `proceed` on the main reward screen
  - `REWARD -> skip_reward_cards -> proceed -> MAP` passed on the rebuilt mod

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

## Conclusion

- Current status: `release candidate`
- No known protocol-level blocker remains in the validated gameplay chain

Reason:

- The major gameplay chain is now covered end-to-end, including transform / enchant / upgrade card-selection branches and the reward-screen proceed edge case
- Static checks pass and the validated room chains now pass in live runs
- Debug tooling is now properly gated for development-only use
- Dynamic energy / star metadata is now more complete for agent-side decision making
- Remaining risk is breadth rather than a known broken core flow: the game is still new, so future content patches can surface new event/card edge cases that were not part of this runbook

Recommended next step:

1. Keep the current validation record as the release baseline
2. Use `docs/mechanic-coverage-matrix.md` to drive future breadth-oriented mechanic probes
3. Run `scripts/test-state-invariants.ps1` before broad manual playtesting to catch payload/action drift early
4. When STS2 receives a content patch, re-run the same Phase 6 checklist and spot-check reward, event, and card-selection branches first
