# Isolated clientId seed revalidation

Date: 2026-09-15
clientId: 2026091518 (directory did not exist beforehand)

## First launch (sparse seed) failed
start-game-session wrote a small JSON then the game replaced it with a full SettingsSave v8 whose mod_settings was null. /health never came up. The replacement file had fullscreen true, skip_intro_logo false, and no BOM.

## Fix
Initialize-IsolatedClientSettings now writes UTF-8 without BOM. If an existing file has mod_settings null, it patches that field in place instead of replacing the whole file. POSIX start-game-session.sh does the same null-patch.

## Second launch (patched complete file) succeeded
Log: patched isolated settings for clientId 2026091518
Game: Loading STS2AIAgent.dll then RUNNING MODDED Loaded 1 mods
GET /health 18080: ok, mod_version 0.12.3, status ready, instance_role human
settings.save after boot: mods_enabled true, STS2AIAgent enabled, first bytes 123,13,10 (no BOM)

Steam profile settings.save / current_run.save / current_run_mp.save hashes unchanged.
