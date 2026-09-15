# Goal evidence — gate select_character after local ready

Date: 2026-09-15

## Change
CanSelectCharacter now returns false before the Godot button / lobby character-list probes when CanUnready is true, when the multiplayer test lobby LocalPlayer.isReady is true, or when NCharacterSelectScreen.Lobby.LocalPlayer.isReady is true. Advertising still goes through CanSelectCharacter; unready stays independently advertised through CanUnready. ExecuteSelectCharacterAsync / ExecuteSelectMultiplayerLobbyCharacterAsync still re-check CanSelectCharacter.

The multiplayer_lobby.has_lobby invariant now skips requiring select_character when local_ready or can_unready is true, matching the existing character_select skip.

## Files
- STS2AIAgent/Game/GameStateService.cs
- STS2AIAgent.Tests/CharacterSelectReadyContractTests.cs
- STS2AIAgent.Tests/TestRunner.cs
- scripts/run_sts2_validation.py

## Commands
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
cd mcp_server; uv run --locked python -m unittest discover -s tests -v

## Results
CharacterSelectReady.ReadyGate, CharacterSelectReady.Advertising, CharacterSelectReady.ExecutorGate PASS. Full C# runner exit 0.
Python unittest: 201 tests, OK, 7.579s.

## Limits
Source-contract and offline Python tests only. Did not start the game, rebuild, or deploy the mod. Live CHARACTER_SELECT / MULTIPLAYER_LOBBY still need a later game-online state-invariants run after the new DLL is installed.
