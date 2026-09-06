# Changelog

## v0.10.0 - 2026-09-06

### Added

- Same-PC AI teammate: from the main-menu overlay, invite a second windowed instance (`--force-steam off --clientId` + `-fastmp join`) into a Steam-hosted 4-player room (本地1人、1ai, two online seats remain).
- Companion bootstrap joins FastHost character select, writes lobby `max_players=4`, and only acts for its own character.
- First-run overlay copy for OpenAI-compatible provider setup before inviting a teammate.
- HTTP action `invite_ai_teammate` plus `/health` `process_id` / `instance_role` so the host can verify the companion process.
- Bilingual Steam Workshop listing and player README: quick start in-game, details on GitHub. MCP is not in the Workshop item.

### Fixed

- Steam overlay invite uses ENet FastHost (injected `-fastmp`) so an offline companion can join; `/state` reports the raw lobby `max_players` after `EnsureFourPlayerLobby`.
- Companion settings stay isolated (`settings.companion.json`) and companion HTTP ports can fall back and be rediscovered by pid/port file.

## Unreleased

### Fixed

- Autoplay now rethrows run-boundary stops from `act` and re-checks compact state after `get_game_state` / `wait_until_actionable`, so leaving a run no longer burns extra model rounds.
- Session request budgets are checked before each LLM round and shared with chat/teammate replies, not only after a finished autoplay turn.
- Deck multi-select no longer confirms at `min_select`; cards report `selected`, and `confirm_selection` works on deck-grid screens.
- Combat selection waits for action-queue readiness; settle timeouts return pending instead of a weaker `stable=true`.
- Companion HTTP ports may fall back and are rediscovered by pid/port file; settings are recopied on every invite; offline launches always get a distinct `clientId`.
- Companion bootstrap joins the host lobby and no longer runs `multiplayer test` itself.
- Compact state now includes `native_profile_id` / `profiles[]`; the playbook tells the agent not to `switch_profile` unless asked.

### Added

- Added session-scoped team chat from the human overlay to the launched AI companion. Team replies use the play model; recent conversation informs future play decisions without granting chat permission to execute actions.
- Made the AI teammate page the default entry for the human window, with an invite flow that saves model settings and checks the main-menu and autoplay preconditions.
- Added `process_id` to `/health` and verify service, role, port, and process identity before accepting a companion connection.

### Fixed

- HTTP startup now falls back to bounded dynamic port selection when Windows excludes the default and nearby ports. Explicitly configured ports remain fixed.
- Companion port discovery checks an actual HTTP bind. Repeated invitations retain the existing child process instead of starting additional games, including after connection timeout.
- Companion startup detects early process exit and propagates cancellation while waiting for health.

### Validation

- Core tests cover run-boundary rethrow, in-flight request budgets, companion port-file discovery, settings reseed, and deck selected-card contracts. Live co-op and in-game selection still need a game session.

## v0.9.2 - 2026-08-31

### Added

- Added reproducible Steam Workshop packaging, SteamCMD VDF generation, bilingual listing copy, and an upload-ready preview image.

### Fixed

- Aligned the player-facing mod manifest version and description with the release metadata.
- Added release-preflight checks that keep the mod, API, MCP package, and lockfile versions synchronized.

### Compatibility

- Verified against Slay the Spire 2 v0.111.0.

## v0.9.1 - 2026-08-27

### Added

- Added `CRYSTAL_SPHERE` state and actions for the Crystal Sphere divination minigame.
- Added `UNLOCK` state payloads and `confirm_unlock` for post-run unlock showcases.

### Fixed

- Fixed AoE and random-target potion actions that could remain pending indefinitely.
- Fixed `confirm_unlock` for derived unlock screens whose private confirm button is declared on a base class.
- Updated the multiplayer release regression to resolve bundle selection during run intro.

### Compatibility

- Verified against Slay the Spire 2 `v0.111.0`.

## v0.9.0 - 2026-08-19

### Highlights

- Players can configure models and auto-play inside the game. MCP is optional and can be started from the overlay for external agents.
- Compatible with Slay the Spire 2 `v0.111.0`.

### Added

- In-game agent overlay (F8 / right-edge AI tab): multi-endpoint and multi-model settings, chat, per-model thinking intensity, auto-play, optional screenshot vision, and local dual-instance launch.
- Overlay **接入** tab: one-click HTTP MCP start/stop, copy API/MCP URLs, and connection notes for Cursor / Claude / Codex / raw HTTP.
- OpenAI-compatible LLM client with tool calling; optional vision model can caption screenshots for non-vision play models.
- HTTP API auto-binds the next port when 8080 is taken. `/health` reports `api_port` and `instance_role`.
- Compact `agent_view` now includes `multiplayer`, `multiplayer_lobby`, and `capstone` summaries.
- Models without tool calling can emit a single JSON `act` object. `get_raw_game_state` and `wait_until_actionable` are available in the in-game tool loop.

### Fixed

- Advice questions such as “Should I play a card?” no longer unlock chat `act`.
- Auto-play and chat no longer mutate the game at the same time; pausing no longer looks like an LLM failure.
- Chat attach-state / screenshot checkboxes are restored from settings.
- Timeline `option_index` validation uses compact `timeline.slots`.
- JSON act fallback only runs for models that do not support tools. Failed acts can be retried in the same turn.

### Notes

- Overlay starts hidden (F8 / AI tab to open). Drag the title bar to move it; the position is saved.
- Chat is read-only unless the player checks “允许代打” or clearly asks the model to play (`帮我打` / `play for me`).
- Companion dual-instance no longer sets `STS2_ENABLE_DEBUG_ACTIONS`; lobby setup uses the internal console path only.
- Auto-play uses compact state and tools (same contract as MCP) and does not require vision.
- One-click MCP needs `uv` plus the `mcp_server` folder from the release zip (or a repo checkout).

### Compatibility

- Verified against Slay the Spire 2 `v0.111.0`.
- Mod health endpoint reports protocol version `2026-03-11-v1`.

## v0.8.1 - 2026-08-16

### Highlights

- Compatible with Slay the Spire 2 `v0.111.0`.
- Standard singleplayer new runs work again through the 0.111 character-select submenu and FTUE confirm flow.
- Card rules text in `/state` no longer triggers mega-text localization errors.

### Fixed

- Restored mod load on 0.111 after `LobbyPlayer` was split into `StartRunLobbyPlayer`. JSON field names are unchanged.
- Read `StartRunLobby` max players from `_maxPlayers` and `RunLobby` connections from `PlayerIds`.
- `open_character_select` now opens `NSingleplayerSubmenu` and clicks Standard instead of calling `OpenCharacterSelect`.
- Confirm/dismiss modal lookup now covers `NVerticalPopup` Yes/No buttons, `NFtueConfirmButton`, and single-button FTUE prompts such as `NAscensionSingleplayerFtue`.
- Card rules text uses `GetRawText()` so `{Damage}` / `{Block}` placeholders no longer spam localization errors.
- Combat and multiplayer tests wait for real progression actions instead of treating animation-only `save_and_quit` as failure.

### Added

- `scripts/test-natural-room-chain.ps1` walks event → map → destination without debug room jumps, and is included in full regression.

### Compatibility

- Verified against Slay the Spire 2 `v0.111.0`.
- Mod health endpoint reports protocol version `2026-03-11-v1`.

### Known limitations

- `open_character_select` starts Standard mode only; Daily and Custom are not clicked.
- A host-side debug `room RestSite` jump can still fail after multiplayer reward resolution. Normal AI-driven multiplayer play is unaffected.

## v0.8.0 - 2026-07-06

### Highlights

- Compatible with Slay the Spire 2 `v0.107.1` / `v0.108.0`.
- Added `save_and_quit` and tighter combat action readiness.

### Compatibility

- Verified against Slay the Spire 2 `v0.107.1` / `v0.108.0`.
- Mod health endpoint reports protocol version `2026-03-11-v1`.

## v0.7.1 - 2026-05-12

### Fixed

- Fixed `run.boss_id` in the Mod `/state` payload so active runs now expose the current act boss ID instead of returning `null`.
- Switched boss resolution to `RunState.Act.BossEncounter.Id.Entry` with a compatibility fallback for older runtime layouts.

## v0.7.0 - 2026-04-30

### Highlights

- Multiplayer AI control is now release-ready for the main play loop.
- Rest-site `MEND` now works in multiplayer without hanging the HTTP request.
- Multiplayer validation and startup scripts were hardened for repeatable release testing.

### Added

- Rest-site options now expose `requires_target`, `target_index_space`, and `valid_target_indices` so AI clients can resolve multiplayer-only targets correctly.
- Map payloads now expose local and remote vote state, including per-node vote counts and voter IDs.
- Multiplayer validation now covers lobby setup, intro resolution, combat progression, rewards, and multiplayer `MEND` target handling.

### Changed

- `choose_rest_option` now accepts `target_index` for targetable rest actions such as multiplayer `MEND`.
- The PowerShell startup flow now waits for both `/health` and `/state` and prints progress while the game boots.
- Release packaging now includes the changelog alongside the packaged mod and MCP server files.

### Fixed

- Fixed host multiplayer map voting so local votes register correctly instead of being lost on the first click.
- Fixed multiplayer map state visibility so both sides can inspect local votes, remote votes, and node vote counts.
- Fixed multiplayer `MEND` so missing `target_index` returns an immediate structured `invalid_target` error instead of timing out.
- Fixed multiplayer validation timing issues around lobby modals, intro transitions, combat readiness, and turn rollover.
- Fixed the PowerShell multiplayer test harness so it no longer relies on brittle redirected child shells to start game sessions.

### Compatibility

- Verified against Slay the Spire 2 `v0.103.2`.
- Mod health endpoint reports protocol version `2026-03-11-v1`.

### Known limitations

- A host-side debug `room RestSite` jump can still fail after multiplayer reward resolution because of the base game's combat sync state. This does not block normal AI-driven multiplayer play and is treated as a debug-only limitation during release validation.

## v0.6.1 - 2026-04-25

### Highlights

- Added live `/data/*` export endpoints for cards, relics, monsters, potions, events, powers, and characters.
- Switched MCP game-data lookup to the live Mod API with in-process caching.
- Improved error handling for game-data tools and synchronized the MCP tool profile coverage.
