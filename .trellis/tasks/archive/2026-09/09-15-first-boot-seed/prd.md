# First-boot isolated seed must survive game overwrite

## Goal
A brand-new --clientId must load STS2AIAgent on the first process start, not only after the game has already written a full settings.save that we later patch.

## Requirements
- Match CompanionProfileBootstrap: if the isolated settings.save is missing, copy a complete template (prefer the Steam profile settings.save under AppData/SlayTheSpire2/steam) then force mods_enabled=true and STS2AIAgent enabled.
- If the file exists and already has mods_enabled true, leave it alone.
- If the file exists with mod_settings null, patch in place (keep the game's full schema).
- Write UTF-8 without BOM.
- Apply the same contract to scripts/start-game-session.sh.
- Offline tests in test_posix_script_portability.py. Do not start the game in this task.

## Acceptance Criteria
- [x] New-directory seed produces a JSON that includes both mod_settings.mods_enabled=true and at least one non-mod key copied from the template (for example language or schema_version plus extra keys beyond the old sparse 8-field file).
- [x] Enabled files are not overwritten.
- [x] Null mod_settings files are patched in place.
- [x] Python portability tests pass.
