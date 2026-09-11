# Changelog

> Release attribution is recorded against tags or release commits. Post-tag maintenance is listed separately; current validation limits are maintained in [PRODUCT_PLAN_CURRENT.md](https://github.com/CharTyr/STS2-Agent/blob/main/PRODUCT_PLAN_CURRENT.md).

## v0.10.6 - 2026-09-11

> Release attribution: git tag `v0.10.6` points to the release commit; covers the post-`v0.10.5` work on `main`, including PR #81 (`1b7236a`, `27fa223`).

### Fixed

- Simple card multi-select panels (the events and rewards that ask for N cards) now report the real `min_select` / `max_select` / `selected_count` and per-card `selected` flags, and advertise `confirm_selection`. `/state` used to report `1/1/0` for them, so an accepted pick looked unacknowledged: the agent re-clicked the same card, toggled it back off, and never left the screen (#81).
- `select_deck_card` on a combat-hand multi-select reports `pending` until the pick is confirmed, matching the play contract and the sibling `use_potion` path; it used to report `completed` while the overlay was still open for more picks.
- Three failed decisions in a row are no longer reported as a finished run. Stop-kind classification matched the bare word 当前局, which also appears in the retry hint, so the overlay claimed the run had ended and told the player to start a new one while the run was still live.
- fastmcp 3.1.0 → 3.4.7 (CVE-2026-32871, fixed in 3.2.0) and fast-uri 3.1.0 → 3.1.7 (CVE-2026-13676, fixed in 3.1.6) via refreshed lockfiles; npm audit drops from 9 advisories to 0 (#50, #51).
- The verification gates work in a fresh checkout, and the gate self-test is parseable under any Windows ANSI code page. Non-ASCII PowerShell scripts must now carry a UTF-8 BOM, enforced by a new `script-encoding` gate.

### Added

- Opt-in proactive teammate chat: the agent may speak on its own at combat start and combat end, with the tone picked in the settings tab. Off by default, read-only (it cannot dispatch a game action even when the model answers with play wording), bounded to 6 messages per session and at least 75 seconds apart, and billed against the session budget.

### Changed

- Card-grid selection metadata and the click-settle path read from the shared `NCardGridSelectionScreen` base, so deck and simple panels use one code path instead of two (#81).
- `docs/api.md` now documents every action; the stale coverage list was archived.
- Existing Steam Workshop item updates default to public visibility; first uploads with item ID 0 default to private, and an explicit `-Visibility` wins (`96bd410`).

### Validation

- Merged build (`ee308ff`, DLL `47CE0F90`) on an isolated game copy: the event multi-select reported `2/2/0` and completed in two clicks (30 ms then 131 ms) with the native `Player 1 chose cards [...]` line; the Sea Glass panel (min 0 / max 15) advertised `confirm_selection` and finished in 168 ms.
- Measured on the same build: the upgrade / transform / enchant panels still report `1/1/0` and burn a full 10-second timeout on the first pick. Tracked as #82 with the evidence.
- 214 core tests pass, 0 failures; 49 MCP tests pass; verification gates, gate self-test, and release preflight pass.

## v0.10.5 - 2026-09-08

> Release attribution: git tag \`v0.10.5\` points to the release commit; covers \`19710ad\` (#78), \`4b4da6e\` (#79), \`22907b0\` (#80) after tag \`v0.10.4\` (\`1c86596\`).

### Fixed

- Empty reward overlays no longer remain pending after collecting rewards (#78, \`19710ad\`).
- \`continue_game_over\` waits for the native summary save before returning to the main menu; it no longer force-enables the Return button after 15 seconds (#79, \`4b4da6e\`).
- Autoplay rethrows run-boundary stops from \`act\` and re-checks compact state after \`get_game_state\` / \`wait_until_actionable\`, so leaving a run no longer burns extra model rounds.
- Session request budgets are checked before each LLM round and shared with chat/teammate replies, not only after a finished autoplay turn.
- Deck multi-select no longer confirms at \`min_select\`; cards report \`selected\`, and \`confirm_selection\` works on deck-grid screens.
- Combat selection waits for action-queue readiness; settle timeouts return pending instead of a weaker \`stable=true\`.
- Companion HTTP ports may fall back and are rediscovered by pid/port file; settings are recopied on every invite; offline launches always get a distinct \`clientId\`.
- Companion bootstrap joins the host lobby and no longer runs \`multiplayer test\` itself.
- Compact state now includes \`native_profile_id\` / \`profiles[]\`; the playbook tells the agent not to \`switch_profile\` unless asked.

### Added

- First-run overlay opens a short setup path. Default URL + model name is unverified until **Test Connection** succeeds per role (chat / play / vision).
- Settings keep per-role test results, a save indicator, and a confirm step before deleting a referenced endpoint or model.
- AI Teammate page shows why the companion is waiting or stopped, what to click next, unknown token usage, and a redacted diagnostics copy.
- Session-scoped team chat from the human overlay to the launched AI companion; team replies use the play model and inform future play decisions without granting chat permission to execute actions.
- AI teammate page is the default entry for the human window, with an invite flow that saves model settings and checks main-menu and autoplay preconditions.
- \`process_id\` on \`/health\`; service, role, port, and process identity are verified before accepting a companion connection.
- GitHub zip now ships \`README.zh-CN.md\` and \`LICENSE\`; install copy steps match the \`mod/\` folder. Native MCP remains the recommended external-client entry.

### Changed

- Session budgets (token/request limits) keep a safe ceiling and restore from corrupt settings backups; exhausted budgets stop autoplay with an in-overlay next-action hint.
- MCP Origin contract: same-origin request and missing Origin are accepted, untrusted/malformed origins are rejected, no open CORS \`*\`.
- Stalled stream bodies time out (tests inject shorter timeouts; production default remains 3 minutes).
- \`play_card\` selection is cancellable and waits for action-queue settlement.

### Validation

- Isolated dual-instance run on current main (DLL \`72C72F02\`): first map combat wiped by idle end-turn; both instances ran \`continue_game_over\` once (4.2s / 2.4s), \`save_verified=true\`, both \`progress.save\` mtimes updated after continue, returning to main menu; no forced 15s Return.
- Over-limit UI: \`maxSessionRequests=1\` stops with \`stop_kind=budget\` and the overlay shows the request-limit message and next actions.
- 180 core tests pass, 0 failures. The initial change validation did not include full preflight or Workshop installation. Subsequent v0.10.5 release validation passed preflight and release directory/ZIP checks (recorded in `15e483c`); Steam manual installation was updated. On 2026-09-09, after the user enabled the Workshop mod and restarted, startup logs confirmed DLL/PCK loading from the subscribed directory, the overlay was visible, and health/state/action queries passed with version 0.10.5. This was a loading smoke test, not full-run, second-restart, or upgrade/rollback validation; see the [acceptance record](https://github.com/CharTyr/STS2-Agent/blob/main/history/workshop-load-acceptance_2026-09-09.md).

## v0.10.4 - 2026-09-07

> Release attribution: git tag `v0.10.4` points to `1c86596`; release commit `2157697`.

### Fixed
- Relic/shop FTUE popups no longer stay open after confirm_modal.
- Timeline first-visit tutorial can be confirmed; screen is TIMELINE not MAIN_MENU.
- In-game play rejects locked event options.

### Changed
- Character unlocks are documented as timeline overlays. Slot obtained epochs, then confirm_unlock.

### Validation
- Profile 3: Silent unlocked via NEOW then SILENT1. confirm_unlock completed NUnlockCardsScreen, NUnlockTimelineScreen, NUnlockPotionsScreen, NUnlockMiscScreen. GAME_OVER save_status=verified.

## v0.10.3 - 2026-09-07

### Fixed
- Invited teammates no longer stop autoplay while waiting to follow a map vote.
- Map node votes are not offered during an active fight, so end_turn/play_card stay available.

### Added
- In-game autoplay and native MCP load the sts2-mcp-player play contract.
- README and Workshop listings tell external MCP clients to load that skill.

### Validation
- Live invite run YFZH54KDS15D: companion followed map votes and played cards; host reached GAME_OVER with save_status=verified.

## v0.10.2 - 2026-09-06

### Added

- In-mod MCP: overlay Connect tab can turn on a Streamable HTTP endpoint at `http://127.0.0.1:<api-port>/mcp` and shows copyable client config. No Python/`uv` sidecar is required for Cursor / Claude / Codex.

### Fixed

- `package-release.ps1` reads `mod_manifest.json` as UTF-8 so Chinese metadata does not break packaging on Windows PowerShell 5.

## v0.10.1 - 2026-09-06

### Fixed

- Companion bootstrap clicks through Neow / bundle / card / reward instead of treating EVENT as already in the run, so both players reach the map together.
- The AI teammate follows the human map vote so both enter the same node.
- Combat actions still work if the map screen remains the active context; ending a turn no longer requires the HUD button to stay visible.

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
