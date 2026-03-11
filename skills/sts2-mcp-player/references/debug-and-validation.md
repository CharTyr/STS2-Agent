# Debug and Validation

Use this reference when the task is not ordinary play, but smoke testing, protocol validation, or debug-assisted reproduction.

## Tool Profiles

- Guided profile is the default and should stay compact:
  - `health_check`
  - `get_game_state`
  - `get_available_actions`
  - `act`
- Guided profile exposes `run_console_command` only when debug actions are enabled.
- Full profile exposes legacy per-action tools and is appropriate only when a harness explicitly needs them.

## Debug Policy

- `run_console_command` is development-only.
- Use it only when the environment explicitly enables `STS2_ENABLE_DEBUG_ACTIONS = 1`.
- Do not make normal gameplay depend on debug actions.
- If the command is unavailable, continue with the regular MCP flow instead of failing the entire plan.

## Recommended Validation Order

1. Build and install the mod.
2. Check mod load and `/health`.
3. Verify debug console gating with debug disabled and enabled.
4. Verify MCP tool profile exposure.
5. Run stateful flow checks against a live session.
6. Re-run state invariants after each major phase.

## Canonical Scripts in This Repository

- `scripts/build-mod.ps1`
  - Builds the DLL, packs the mod, and installs it into the game `mods/` directory.
- `scripts/test-mod-load.ps1 -DeepCheck`
  - Verifies `/health`, `/state`, and `/actions/available`.
- `scripts/test-debug-console-gating.ps1`
  - Verifies mod-side gating, MCP tool registration, and payload wiring.
- `scripts/test-mcp-tool-profile.ps1`
  - Verifies guided and full tool exposure rules.
- `scripts/test-state-invariants.ps1`
  - Verifies that `state.available_actions` stays aligned with the screen payload.
- `scripts/test-full-regression.ps1`
  - Runs the full smoke suite end to end.
  - Stops any running game before install.
  - Self-bootstraps an active run when the profile starts on a no-run main menu.
  - Re-checks state invariants after each stateful phase.

## Important Validation Notes

- Startup often reaches `/health` before the main menu is fully actionable. Wait for a stable screen with actions before assuming readiness.
- Run-start transitions such as `embark` can take longer than lightweight actions. Use a longer request timeout and allow a short retry window.
- After any debug travel or injection command, re-read state before choosing the next action.
- Keep the game process stopped at the end of automated suites unless the caller explicitly wants a live session left open.

## Known Non-Blocking Noise

- The game's own debug `die` command can emit a missing localization key in early-run event contexts.
- Treat that as game-side log noise unless the MCP state contract or action flow actually fails.
