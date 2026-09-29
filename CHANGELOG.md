# Changelog

## Unreleased

### Added

- Added a `leave_rewards` action that clicks the reward screen's own "continue" button, so callers can exit after claiming rewards one-by-one with `claim_reward` + `choose_reward_card` / `skip_reward_cards`.
- Reward-bulk cleanup (`collect_rewards_and_proceed` / `resolve_rewards`) now claims non-potion rewards before potion rewards, giving relics that add potion slots a chance to be taken first.
- The built-in agent compact state now ships each potion's authoritative effect text (`run.potions[].effect`), and the glossary / play prompt ground card, potion, relic, and block rules in live payload text instead of the model's memory.
- Upgrade-selection screens now expose `upgraded_rules_text` and `upgraded_energy_cost` for every candidate card (in both the raw payload and the built-in agent's compact state), so the agent can compare a card against its upgraded version instead of guessing.
- Added `upgraded_preview_note` (upgrade screens only) to surface why an upgrade preview could not be produced.
- The map is now reported whenever a run is active, not only while the map screen is open: the graph, current node, boss nodes and rows/columns come from the run model, so an agent can read the route from events, rest sites, shops and combat the same way a player can open the map overlay. A new `is_open` flag tells callers whether the overlay is actually open (travel flags and available nodes are only meaningful then).
- Added a GitHub Actions CI workflow that runs the game-independent C# tests plus the MCP server's syntax check, unit tests and import check.
- Declared `pytest` as a `dev` dependency group in `mcp_server/pyproject.toml` (with `[tool.pytest.ini_options]`) so `uv run pytest` works out of the box.

### Fixed

- Fixed `leave_rewards` missing from the MCP `Sts2Client`, which made `create_server(tool_profile="full")` raise `AttributeError` at startup (caught by the MCP test suite).
- Rest-site selection discovery now resolves the campfire upgrade selection screen itself instead of scanning the whole scene tree for any card grid, so an unrelated card grid (for example a deck view opened at a campfire) is no longer reported as a pending selection or able to suppress `proceed`.
- Preview upgrades are reverted with a retry and report `revert_failed`, so a candidate card can no longer be left upgraded if restoring it fails.
- Fixed the campfire (rest site) smith flow: the upgrade selection UI is not always part of the active screen, so the state could report `screen = REST` with an empty `rest.options` and no selectable cards while the upgrade overlay was open. The selection search root now widens to the scene tree for rest sites, so the screen resolves to `CARD_SELECTION` (`selection.kind = deck_upgrade_select`) with `select_deck_card` available.
- `proceed` is no longer offered while a card selection is pending, which previously let an agent skip the campfire upgrade (or another selection overlay) by clicking the underlying room's continue button.
- Upgrade previews are produced by temporarily upgrading the card and reverting it with `DowngradeInternal()`; the earlier approach returned the un-upgraded text because the game only renders upgrade text while the card is actually in an upgraded state.

### Changed

- `discard_potion` is now usable on the main reward screen (still blocked on the card-reward sub-screen), enabling "drop one potion, claim another".
- Play prompt now instructs the in-game agent to prefer manual reward claims over `collect_rewards_and_proceed` / `resolve_rewards` (which auto-pick the first card), and to resolve full-slot potions by claiming a slot relic first or discarding a potion first.

### Compatibility

- Rewards, potion-full handling, and the new `leave_rewards` exit flow require a live-game verification pass (see `docs/phase-*` validation templates).

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
