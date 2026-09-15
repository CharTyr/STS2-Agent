# First-boot isolated seed

Date: 2026-09-15

## Problem
A brand-new `--clientId` was seeded with an 8-field `settings.save`. The game replaced it with a full SettingsSave v8 whose `mod_settings` was null, so STS2AIAgent did not load on the first process start. Patching `mod_settings` on a file the game had already written worked; the missing-file path still used the sparse template.

## Change
`Initialize-IsolatedClientSettings` / `sts2_initialize_isolated_client_settings` now match CompanionProfileBootstrap:

- Existing file with `mods_enabled=true` is left intact.
- Existing file with `mod_settings: null` is patched in place.
- Missing file copies `SlayTheSpire2/steam/**/settings.save`, then forces `mods_enabled=true` and enables STS2AIAgent while keeping template keys such as `language`.
- If no steam template exists, write a complete SettingsSave v8 fallback (not the old 8-field file).
- Writes UTF-8 without BOM.

## Files
- scripts/start-game-session.ps1
- scripts/start-game-session.sh
- mcp_server/tests/test_posix_script_portability.py

## Commands
```
cd mcp_server
uv run --locked python -m unittest tests.test_posix_script_portability -v
```

## Results
PASS. 17 tests, 0 failures, 2.068s, exit 0.

Coverage: copy steam template (language/aspect_ratio/OtherMod preserved, STS2AIAgent enabled), skip already-enabled files, null `mod_settings` patch in place, complete fallback without a steam template.

## Limits
## Live first-boot
clientId 2026091526, directory did not exist. start-game-session logged "seeded isolated settings for clientId 2026091526 from steam template".
GET /health 18080 first process start: ok, mod_version 0.12.3, status ready.
settings.save after boot: mods_enabled=true, language=zhs (copied from steam template), schema_version=8, 23 keys, first bytes 123,13,10 (no BOM).
Steam profile settings.save hash unchanged: C360D60B840994FD34FAA60C1F61AA25449D902A4122BA5958F022A25D23BF3E.
No commit/push/release.
