# 5. POSIX builds produce complete artifacts and support staging only

## Goal
POSIX stage/install includes DLL, PCK and mod_id.json. --skip-install performs build/staging with no game install writes. Invalid installation roots are rejected before side effects; explicitly supplied usable custom mods destinations remain supported. Help explains mode and prerequisites.

## Evidence
scripts/build-mod.sh:191-227 creates mods unconditionally, copies only DLL/PCK and has no skip-install; Windows build-mod.ps1 stages/installs mod_id.json and rejects absent game roots unless SkipInstall.

## Acceptance
Offline fixtures verify skip-install no install writes, complete staging, ordinary installation, typo rejection, custom destination; bash syntax and full Python suite plus verification gates
Record actual checks and live-environment limits.

## Scope
scripts/build-mod.sh and focused offline POSIX build tests, test integration; Operations spec later owned by main
No release, provider calls or player-save mutation required.

## Authorization
Latest instruction (2026-09-15): organize tasks and stop. This is a planned task, not authorized for continued execution in this session. On resumption use xai/grok-4.6 xhigh subagents unless the user changes that preference.
