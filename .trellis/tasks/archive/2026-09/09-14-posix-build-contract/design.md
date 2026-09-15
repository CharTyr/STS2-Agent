# Design

Align build-mod.sh with actual Windows contract and shared resolver. Preserve custom --data-dir/--mods-dir semantics where safe. Check missing install parent/game root before mkdir; allow creating mods within an existing installation. Use fixture fake dotnet/Godot executables to execute script paths without real game or install, rather than only text assertions. Do not change workshop layout on unverified loader assumptions.

Preserve public wire/settings contracts. Rollback via goal commit; preserve previous changes.
