# POSIX start-game-session seed and extra args

Date: 2026-09-15

## Change
`scripts/start-game-session.sh` now matches the PowerShell isolated-launch contract:

- Unknown options and arguments after `--` are collected as extra game args instead of a usage exit.
- `--clientId VALUE` and `--clientId=VALUE` are parsed.
- A present clientId seeds `SlayTheSpire2/default/<clientId>/settings.save` with `mods_enabled=true` and STS2AIAgent enabled.
- An existing file with `mods_enabled: true` is left intact.
- Extra args are forwarded to the game executable.
- `STS2_API_PORT` is still exported; `STS2_ENABLE_DEBUG_ACTIONS=1` is exported when requested.

Functions are sourceable without launching the game.

## Files
- scripts/start-game-session.sh
- mcp_server/tests/test_posix_script_portability.py

## Commands
```
cd mcp_server
uv run --locked python -m unittest discover -s tests -v
```

## Results
PASS. 206 tests, 0 failures, 8.783s, exit 0.

New cases: `PosixStartGameSessionContractTests` (source contract plus Git Bash fixture for clientId forms, seed JSON, and skip-overwrite).

## Limits
Offline only. No game start, no mod deploy, no commit/push/release.
