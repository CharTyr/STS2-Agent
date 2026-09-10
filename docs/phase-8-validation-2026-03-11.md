# Phase 8 Validation - 2026-03-11

> Historical snapshot: this file records a point-in-time validation run, not current state. Current status: [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md).

This phase closed the remaining main-menu lifecycle gaps and added a single-entry regression script for the full smoke suite.

## Fixes

- `choose_timeline_epoch` now waits until `confirm_timeline_overlay` is actually available before reporting `stable=true`.
- This keeps `response.data.state.available_actions` aligned with the current `/state` payload, instead of returning a stale action set.
- `test-full-regression.ps1` now self-bootstraps an active run when the profile starts on a no-run main menu, then restarts the game once to reach a stable `continue_run` state.
- `test-new-run-lifecycle.ps1` now uses a longer request timeout with retry handling for heavy transitions such as `embark`.

## Added Scripts

- `scripts/test-main-menu-active-run.ps1`
- `scripts/test-new-run-lifecycle.ps1`
- `scripts/test-full-regression.ps1`

## Validated Flows

- Active-run main menu: `abandon_run -> dismiss_modal`
- Timeline submenu: `open_timeline -> choose_timeline_epoch -> confirm_timeline_overlay -> close_main_menu_submenu`
- Continue flow: `continue_run`
- New-run lifecycle: `abandon_run -> confirm_modal -> open_character_select -> select_character -> embark`
- Game-over lifecycle: `run_console_command die -> return_to_main_menu`
- Full regression bundle:
  - build and install the mod
  - deep-check mod load
  - verify debug console gating with debug actions disabled and enabled
  - verify MCP guided/full tool profiles
  - verify combat hand confirm selections
  - verify deferred potion selections
  - rerun state invariants after each stateful phase

## Commands

- `powershell -ExecutionPolicy Bypass -File "scripts/test-full-regression.ps1"`

## Final Result

- The full regression entrypoint passed on 2026-03-11 with 22 sequential steps and no failed checks.
