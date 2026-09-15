# POSIX start-game-session seed and extra args

## Goal
scripts/start-game-session.sh must match the PowerShell isolated-launch contract for clientId seeding and extra arguments.

## Requirements
- Parse leftover / extra launch arguments, including --clientId VALUE and --clientId=VALUE.
- When a clientId is present, seed an isolated settings.save under the platform equivalent of SlayTheSpire2/default/<clientId> with mods_enabled=true and STS2AIAgent enabled, without overwriting an already-enabled file.
- Forward those extra arguments to the game executable.
- Keep exporting STS2_API_PORT (and STS2_ENABLE_DEBUG_ACTIONS when requested).
- Cover the new behavior in mcp_server/tests/test_posix_script_portability.py with offline fixtures. Do not start the game.

## Acceptance Criteria
- [x] A clientId extra arg seeds settings and is forwarded to the exe in tests or a dry-run assertion.
- [x] Existing enabled settings are left intact.
- [x] STS2_API_PORT is still exported.
- [x] Python unittest for the portability file passes.
