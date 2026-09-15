# Gate select_character after local ready in source

## Goal
Available-action advertising must not offer select_character when the local player is already ready, instead of relying only on Godot button enablement.

## Requirements
- GameStateService.CanSelectCharacter returns false when the local player is ready (CanUnready / local isReady), for both NCharacterSelectScreen and the multiplayer test lobby.
- ExecuteSelectCharacterAsync already uses CanSelectCharacter; keep that as the executor gate.
- scripts/run_sts2_validation.py multiplayer_lobby.has_lobby path must not require select_character when local_ready or can_unready is true. The CHARACTER_SELECT path already skips in that case; the lobby path still always requires it.
- Add a source-contract test. Do not start the game. Do not rebuild or deploy the mod. Do not edit invite/budget/POSIX files.

## Acceptance Criteria
- [x] CanSelectCharacter is false when the local player is ready.
- [x] select_character is not advertised in that case; unready still is when CanUnready is true.
- [x] The multiplayer_lobby invariant no longer fails a ready lobby for missing select_character.
- [x] C# core tests pass. Python unittest passes if the invariant file changed.
