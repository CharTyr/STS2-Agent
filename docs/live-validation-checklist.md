# Live validation checklist

Items that deterministic offline tests cannot settle: they need the game running with the mod
deployed. Collecting them in one place keeps "we proved it offline" from being read as "we saw it
work".

A session that walks this list needs the game installed, the mod built and deployed
(`scripts/build-mod.ps1 -Configuration Release`), and `scripts/start-game-session.ps1
-EnableDebugActions` so the debug console suites can run.

Legend: **[mod]** Mod API online only · **[combat]** needs a fight · **[room]** needs a specific
screen · **[coop]** needs two instances · **[eye]** needs a human or model to watch behaviour.

## Status as of 2026-09-13

Verified against the released v0.12.0 build (`mod_version=0.12.0`, game `v0.111.0`). The host was an
isolated offline copy of the game (`--windowed --force-steam off --clientId <id>`, API on `18080`) driven
over HTTP, with a zero-cost local stub model on `127.0.0.1:18098` answering the agent's model calls. No
real model quota was spent, and the Steam profile's save files were left byte-identical (hashes compared
before and after).

- All seven `GET /data/{collection}` endpoints answer — cards 596 / relics 299 / monsters 107 / potions 66 /
  events 57 / powers 283 / characters 5. `powers` answering 200 with 283 entries is the live confirmation
  of the guarded-localization fix: previously the whole collection failed with a 500.
- `resolve_rewards` takes the card named by `option_index` (0 and 1 both picked the matching card rather
  than always the first), so the choice really does travel with the request. An out-of-range index is 409
  `invalid_target` carrying `option_count`, and the reward survives untouched (deck stayed at 10 cards).
- The reward overlay reports `REWARD` after the Card reward is claimed, with `pending_card_choice = true`,
  `choose_reward_card` / `skip_reward_cards` offered, `collect_rewards_and_proceed` still able to finish the
  screen, and no `select_deck_card` on offer.
- `CARD_PILE` is its own screen name and `close_cards_view` really pops it — the executor's `ForceClick` on
  the screen's own BackButton is enough. Clicking the draw pile in a fight reported `CARD_PILE` with
  `close_cards_view` offered; the action returned `completed` back on `COMBAT`. The top-bar deck button
  (`CARDS_VIEW`) closes the same way.
- `PATCH_NOTES` and `close_main_menu_submenu` round-trip: the main menu's patch-notes button reports
  `PATCH_NOTES` with only `close_main_menu_submenu` offered, and that action returns to `MAIN_MENU`.
- `run.relic_ids` and `run.relics` come back the same length and in the same order (3 / 3).
- A live combat enemy carried `intents[]` with `damage` / `hits` / `total_damage` (12 / 1 / 12) alongside the
  legacy `intent` / `move_id` pair, and combat `players[]` carried both the local and the remote player.
- `invite_ai_teammate` with `CompanionAutoSelectCharacter = true` (the default): the teammate joined the
  lobby, picked its character and readied itself — `character_select.players[]` showed the teammate's slot
  with `is_ready = true` while the host's own slot stayed unready.
- `invite_ai_teammate` with `CompanionAutoSelectCharacter = false`: the teammate reached
  `CHARACTER_SELECT` and **stopped** — the companion's own bootstrap logged
  `Companion bootstrap CHARACTER_SELECT|-|close_main_menu_submenu,select_character,embark`, an empty action,
  and both `character_select.players[]` entries stayed `is_ready = false`. Across a 6 m 30 s dwell the
  teammate process stayed alive and on `CHARACTER_SELECT`, well past the five-minute bootstrap budget, with
  no `Timed out joining the local room.` in the log. So the clock really is held while a person chooses.
  The actions stay advertised on the companion's API — that is the supported path for a human or an external
  agent to drive `select_character` + `embark`; what the bootstrap withholds is its own input.

### Found in this session

Two things only a live game shows. Both were fixed and re-verified in the same session (issues #88 and
#89):

- **The pause menu is reported as an open `capstone` and offered as `choose_capstone_option`.** Opening the
  pause menu in a fight (top-bar button, and also via Escape) leaves `/state.screen` at `COMBAT`, fills
  `capstone.options` with that menu's own buttons — `继续` / `设置` / `放弃` / `保存并退出` / `BackButton` — and
  advertises `choose_capstone_option` in `available_actions`. Calling it returns 200 `pending` and does
  nothing: the menu stays put and the screen never leaves `COMBAT`. The state is wrong in the direction that
  matters, since one of those options abandons the run. Root cause is in the game's own class hierarchy —
  the pause menu is an `NCapstoneSubmenuStack`, which is exactly what `GetCapstoneButtons` matches on — so
  the fix has to tell the pause menu apart from a real capstone screen rather than removing the capstone
  path.
- **`run_console_command bestiary` answers 500 `internal_error`.** The game's own
  `BestiaryConsoleCmd.Process` throws a `NullReferenceException`; the mod surfaces it as an unhandled server
  error with `details: null`. It is a debug-only path (and the game labels the command WIP), but a 500 with
  no context is not an honest envelope for a console command that was accepted and then threw.

### Fixes verified in the same session

All three were re-run against the patched build on the same isolated instance.

- **The pause menu is its own screen and stops being a decision screen.** The container tells its overlays
  apart by its own `Type` (`CapstoneSubmenuType` is `None` / `Settings` / `Compendium` / `Feedback` /
  `PauseMenu` — there is no boss-reward option screen in this build), and `GetCapstoneButtons` now excludes
  `PauseMenu`. With the pause menu open, `/state` reports `screen = PAUSE_MENU`, `available_actions = []`,
  `/actions/available` answers with an empty list and `capstone` is null, `choose_capstone_option` is 409
  `invalid_action`, and Escape returns the run to the screen underneath. The teammate's own bootstrap logged
  `Companion bootstrap PAUSE_MENU|-|`, so the companion sees the same picture. `CAPSTONE_SELECTION` keeps its
  switch arm for the settings / compendium / feedback overlays.
- **`continue_ai_teammate` refuses before the game can damage the save.** The action reads `players[].net_id`
  out of the co-op save and compares it against this host's NetId (the `--clientId` launch argument, 1 when
  omitted) and against the NetId the teammate would be launched with (host + 1), both **before** the load.
  Three live runs:
  - Ids matching (`1,2`, host started with `--clientId 1`): `continue_ai_teammate` returns 200 `completed` on
    `MULTIPLAYER_LOAD`, the launcher starts the second instance (two game processes), and the log shows the
    companion handshaking as NetId 2 and receiving `ClientLoadJoinResponseMessage` — the rejoin that used to
    fail with `NotInSaveGame`.
  - Host id missing (host `--clientId 2026091099`, save `1,2`): 409 `invalid_action`, `retryable: false`, with
    `save_player_net_ids: [1, 2]` and `local_player_id: 2026091099` in the details, and a message that names
    both ids, the consequence and the way out. The save's hash is **unchanged** afterwards, no
    `*.VAL.corrupt` appears, and no second process is started — the destructive load never runs.
  - Teammate id missing (host `--clientId 2`, save `1,2`, so the teammate would be 3): 409 `invalid_action`
    with `companion_client_id: 3`, same unchanged save and single-process evidence.
- **A throwing console command answers honestly.** In a run, `run_console_command bestiary` is now 409
  `invalid_action` carrying `Console command failed: NullReferenceException: Object reference not set to an
  instance of an object.` and `command: bestiary` in the details — where it used to be a bare 500
  `internal_error` with `details: null`. The run is left untouched (`COMBAT`, same action list).

### In-run menu pages (issue #93)

The capstone container's pages used to be reported as the room underneath them: the pause menu's compendium hub
read as `COMBAT`, the card library as `CARD_SELECTION`, and both handed the model the run's own actions plus
the page's furniture as `capstone.options`. Verified live in two passes on one isolated instance, polling
`/state` after every click:

- **Before the fix** (shipped v0.12.0 and the #91 / #92 build): pause menu -> compendium hub reported `COMBAT`
  with `available_actions = [end_turn, play_card, save_and_quit, choose_capstone_option]`; the card library
  reported `CARD_SELECTION` with 51 capstone options whose first 25 labels were `Hitbox`.
- **After the fix**: `PAUSE_MENU`, `SETTINGS`, `COMPENDIUM`, `CARD_LIBRARY`, `RELIC_COLLECTION`, `POTION_LAB`,
  `STATS` and `RUN_HISTORY` each report their own name. None of them advertises a room action or
  `save_and_quit`, `capstone` stays null, and `choose_capstone_option` answers 409 `invalid_action` on every
  one of them. `close_main_menu_submenu` steps back one page (`CARD_LIBRARY` -> `COMPENDIUM` -> `PAUSE_MENU`)
  and is 409 on the pause menu itself, which is where a person resumes. Escape returned the run to `COMBAT`
  with `end_turn` / `play_card` offered again.
- **The live run corrected the issue's own assumption about the way out.** The issue claimed `close_cards_view`
  still worked on the card library because it looks for a `BackButton` regardless of type. It does not: the page
  sits in the container rather than on a card-viewer screen, so both it and `close_main_menu_submenu` were 409,
  before and after the naming fix, and an agent that followed the skill's "use `close_main_menu_submenu` inside
  a run" line had nothing that worked. The fix widens `close_main_menu_submenu` to the container's stack
  (`Stack.Pop()`, the call the game wires to every page's own BackButton) and deliberately excludes the pause
  page, since popping that one resumes a paused run.
- **Second live find: `save_and_quit` executed from a page whose surfaces advertised nothing.** #91 emptied both
  action surfaces over the pause menu, but `CanSaveAndQuit` only excluded the main menu, game over, character
  select and multiplayer, so the action still worked there and saved-and-quit the run during this session's
  probing. The capstone overlay is now part of that predicate, so the executor refuses what the surface never
  offered.
- `BESTIARY` was not opened live: this profile's compendium hub draws no bestiary tile (`NBestiary.CanBeShown()`
  gates it). The mapping and the exclusion from decision screens exist for the build that shows it.

### Environment note for isolated runs

A brand-new `--clientId` directory gets a default `settings.save` whose `mod_settings` is `null`, and the
game then refuses to load the mod at all (`Skipping loading mod STS2AIAgent, user has not yet seen the mods
warning`) — `/health` never comes up. Pre-seed the client dir's `settings.save` with `mod_settings`
(`mods_enabled` plus a `mod_list` entry for `STS2AIAgent`) before launching, or reuse a client dir that has
already run the mod.

## Status as of 2026-09-12

Verified in a live session on a profile with an active run save:

- Reward-card overlay reports `REWARD` (not `CARD_SELECTION`) once the Card reward is claimed, and
  offers `choose_reward_card` / `skip_reward_cards` while exposing no `select_deck_card` and a null
  `selection`. This is the `ResolveNonModalScreen` fix seen working end to end.
- `resolve_rewards` carries `requires_index = false`; an out-of-range `option_index` is rejected with
  409 `invalid_target` and leaves the reward untouched.
- The timeline path works from a main menu that has a run save: `open_timeline` is offered,
  `choose_timeline_epoch` honours `timeline.slots[].index` (57 slots, all actionable), an
  out-of-range index is 409 `invalid_target` with `option_index_space = "timeline.slots[].index"`, a
  missing index is 400 `invalid_request`, and `confirm_timeline_overlay` + `close_main_menu_submenu`
  return to the menu.
- `scripts/test-main-menu-active-run.ps1` passes on an active-run menu.
- `scripts/run_sts2_validation.py state-invariants` passes on the reward screen.
- All seven `GET /data/{collection}` endpoints answer, after the power-export fix below.

Found and fixed during that session (both were invisible offline):

+ `GET /data/powers` returned 500 for the whole collection: a `MOCK_*` power the localization tables
  do not cover made `GetFormattedText` throw while the export streamed. Exported names now go through
  a guarded lookup and a missing entry yields null.
- The "the main menu disables its timeline button while a run save exists" comments in
  `scripts/run_sts2_validation.py` and `scripts/test-main-menu-active-run.ps1` stated a rule the game
  does not have: `NMainMenu.UpdateTimelineButtonBehavior` enables the button while a save exists in
  its "no epoch discovered yet" branch. Both comments were rewritten; neither script asserts either
  way.

## [room] Card-viewer screens

Open the pile screen in a fight (click the draw/discard/exhaust pile) and open the card library
(main-menu or pause-menu compendium, then Card Library).

- `/state.screen` reports `CARD_PILE` / `CARD_LIBRARY` rather than `CARD_SELECTION`
  (`GameStateService.ResolveNonModalScreen`).
  `CARD_PILE` was verified live on 2026-09-13 (clicking the draw pile in a fight reported `CARD_PILE` with
  `close_cards_view` offered, and the action really popped the screen back to `COMBAT`). `CARD_LIBRARY` was
  not reached: neither the main menu nor the in-run pause menu offered a compendium entry in this build, and
  no console command opens one.
- `close_cards_view` closes the pile screen: the executor clicks the screen's own `BackButton` with
  `ForceClick()`, which bypasses input state, so only the live game shows whether the `Released`
  signal really pops it.
- `close_main_menu_submenu` closes the in-run library and returns to the screen underneath. The
  submenu lookup now matches the base `NSubmenuStack`; the in-run stack is `NRunSubmenuStack`, which
  the previous main-menu-only lookup never found.
- The kindred in-run submenus reached by the same stack lookup (compendium, bestiary, relic
  collection, potion lab, run history, settings, stats, pause menu) also offer and honour the action.
- Neither screen exposes `select_deck_card` or a `selection` block.

Note: no debug console command opens either screen. The game's 39 console commands touch only the
map screen; the screens are opened by their own buttons, so a session needs a human click or a
mod-side action.

## [room] Reward and selection screens

- Claim a Card reward and confirm the overlay still reports `REWARD` with `reward.pending_card_choice
= true` (covered above for one variant; multi-reward, bundle and multi-card-choice variants are not).
- `skip_reward_cards` behaves the same as before the `IsEnabled` filter was added (the game does not
  currently disable those buttons, so the two should be indistinguishable).
- `resolve_rewards` without an index picks the first card as documented.
- `collect_rewards_if_needed` in `run_sts2_validation.py` still converges now that the overlay reports
  `REWARD`.
- A single-select grid that needs no confirmation reports `selection.can_confirm = false` and the
  model stops sending `confirm_selection` for it.
- An FTUE popup with no button of its own reports `modal.can_confirm = true` and `confirm_modal`
  finishes it.

## [room] Other screens

- `select_deck_card`'s 409 is visible to callers when nothing is clickable, while the probe still
  lists it in `available_actions`; the asymmetry is known and unclosed.
- Crystal Sphere: `crystal_set_tool` only reports `completed` when reading back the tool confirms the
  click, otherwise `pending`.
- Crystal Sphere: a map screen or capstone overlay covering the sphere must never let
  `/state.screen` report `CRYSTAL_SPHERE` (the offline argument is from the resolution path, not from
  observation).
- The timeline's unlock overlay: after a death, `settle_main_menu` walks real `UNLOCK` layers with
  `confirm_unlock` back to a usable menu.
- Main-menu overlays: `modal.underlying_screen` reports the screen underneath each overlay type.

## [combat] Combat

- Compact `combat.player.powers`, `combat.players[].powers`, `combat.enemies[].powers` and
  `enemies[].intents[]` (`damage`, `hits`, `total_damage`) carry correct, non-empty values; multi-hit
  and buff intents are the interesting cases.
- `run.relic_ids` and `run.relics` are the same length and order; merged `card_ids` groups handle
  two cards that share a name but not an id.
- `scripts/run_sts2_validation.py enemy-intents-payload` passes against a real fight.
- A long enemy turn really does produce the pending path that `AutoPlayRecovery` treats as
  executed-but-unsettled, rather than a failure.
- The compact fingerprint stays stable across turns of genuine inactivity, so the no-progress guard
  fires on spinning and not on slow play.

## [mod] Data export

- Every `GET /data/{collection}` answers `cards`, `relics`, `monsters`, `potions`, `events`,
  `powers`, `characters` (verified 2026-09-12: 596 / 299 / 107 / 66 / 57 / 283 / 5).
- Exported fields match `GameDataExportSchema.cs` and the scene field tables the MCP server uses for
  `get_relevant_game_data`.
- Exported collections stay aligned with the Python client's action surface: every action the client
  exposes is one the mod accepts.

## [coop] Two instances

- `invite_ai_teammate` returns `completed` on a real dual launch, 409 `invite_failed` when the client
  is on English, and `pending` for a concurrent invite.
  Verified 2026-09-13 on an isolated offline host: the invite started the second instance itself (API on
  host port + 1), and with the default settings the teammate auto-selected and readied on its own.
  With `CompanionAutoSelectCharacter = false` the teammate reached `CHARACTER_SELECT` and stayed there for
  6 m 30 s without acting and without timing out (see the 2026-09-13 status section above).
- `continue_ai_teammate` end to end on a Steam-hosted save (verified 2026-09-13): the host reaches
  `MULTIPLAYER_LOAD`, the launcher starts the companion, the companion's bootstrap presses Embark, the host's
  own `embark` completes, and the run resumes on the **same** `run_id` with both players connected.
- `continue_ai_teammate` on an isolated offline host needs the save's player NetIds to line up, and nothing
  checks that for you. The host's NetId in the local path is always **1**, while the game validates the
  *loading instance's* NetId against `players[].net_id` in the save. Two ways it fails, both seen live:
  - Host started with `--clientId <N>` where `N != 1`: the game refuses the load (`Save is invalid! Players
    does not contain local player Id`), **renames `current_run_mp.save` to
    `current_run_mp.<unix>.VAL.corrupt`**, disables the continue button, and the action returns 409
    `continue_failed` whose message blames port 33771. The save is moved aside, not restored, so a failed
    attempt is destructive to that save file.
  - Host started with `--clientId 1` (the NetId the save expects): the load succeeds and reaches
    `MULTIPLAYER_LOAD`, but the launcher starts the companion with clientId `2`, which is not the NetId the
    save recorded for the teammate (an earlier run under `--clientId 2026091301` had written `2026091302`),
    so the host drops the join with `JoinFlow: Disconnected during join flow, reason: NotInSaveGame` and the
    companion falls back to the main menu.
  The isolated path therefore only works when the save was created by a host whose NetId equals the clientId
  the host is started with, and whose teammate NetId equals that clientId + 1. The Steam path has neither
  problem because the host's NetId is the account id.
- `scripts/test-multiplayer-lobby-flow.ps1` crosses `CAPSTONE_SELECTION` and `UNLOCK` without
  throwing `Unsupported run progression state`.
- Multiplayer `players[]` and `target_index` share one index space.
- The network MCP path binds a real port: `scripts/serve-sts2-network-mcp.ps1` serves `/mcp` over
  HTTP/SSE with bearer auth, and `/healthz` reports 200 / 503 / 500 for its three branches.

## [eye] Behaviour only a human can judge

- The in-game model no longer spends a round on `health_check`, and the per-step system prompt
  behaves as intended in a real run.
- After the payload fixes the model stops sending confirmations it does not need and stops passing an
  index nothing consumes.
- A `state_unavailable (retryable: true)` failure reads clearly to a native MCP client and the model
  retries it sensibly.
- The stop/backoff boundaries (five repeats, exponential delay) feel right in practice rather than
  only matching their constants.
- The English strings are machine-translated and have never been read by a native speaker; only
  Chinese and English have been exercised.
