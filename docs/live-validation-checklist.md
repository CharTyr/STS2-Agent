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

### External-takeover route (issue #85)

Verified 2026-09-13 against a build carrying the issue-#85 change, on the same isolated offline host
(`--windowed --force-steam off --clientId 2026091001`, API on `18080`) driven entirely over HTTP, with
a settings file whose endpoint points at a **dead** port (`127.0.0.1:18199`) and **no `roleTests` entries
at all** — nothing configured and nothing verified. Script: `build/validation-2026-09-13/verify-takeover.ps1`,
evidence `takeover-evidence.jsonl` (both gitignored). Every step below is one run of that script; the
invite was a real call, not a resumed session.

- **`invite_ai_teammate` launches with nothing verified.** It returned 200 `completed` on the first call,
  the second process came up as `role=companion` on `18081`, and its message no longer promises auto-play:
  「第二实例已就绪…本机已创建 4 人大厅，请选角色后 Ready 开局。你打自己的角色；AI 会自动加入并点开局，
  然后停在原地等待外部接管，不会自己出牌。」 The model gate is what the route switch drops, not the launch.
- **The host discovers the companion API.** `GET /health` on the host carried
  `"companion": {"api_host":"127.0.0.1","api_port":18081,"process_id":64872,"auto_play":false}`, and that port
  answered `role=companion` / `status=ready`. No session token appears anywhere in the response.
- **The teammate really is idle, and really never called a model.** After the shared run was reached
  (`MAP`) and 20 s of dwell: `play_running=false`, `play_phase="paused"`, `session_requests=0`,
  `stop_kind=null`. Zero requests is the part that matters — the endpoint was dead, so a single model call
  would have shown up as a config/network stop instead of passing quietly.
- **It is drivable by an outside agent.** The companion advertised `choose_map_node` on `MAP`, so the
  external path is `GET /state` + `POST /action` against `18081`, exactly as the issue described.
- **`POST /teammate/control` is the supported start/pause.** `{"running":false}` → 200 with
  `phase:"paused"`, `play_running:false`, `play_phase:"paused"`, `companion_auto_play:false`.
  `{"running":true}` → 409 `teammate_control_failed` naming the unverified play model, which is the
  intended behaviour: this route can park the teammate, and starting the in-process loop still needs the
  verified model the in-game “Resume” button requires. `{"running":"yes"}` → 400 `invalid_request`.
- **The companion window has no overlay at all** (`ModEntry` skips it for companions), which is why the
  route is reported on `/health` and the host status line rather than in the second window.
- **After the companion process is killed**, `/health` drops the `companion` block back to `null` while
  `companion_process_exited` turns `true` — the discovery block never points at a dead port.

### Found in this session

- **The route flag was recorded before the attempt, not after it.** A rejected retry — the usual one being
  “the teammate window is already running” — overwrote `_companionAutoPlay` before the launch was even
  attempted, so `/health` would report `auto_play:false` for a teammate that the in-process loop was
  actively playing, inviting an external agent to take over a seat already in use. The flag is now written
  only when the attempt established a new connection (`CoopRoute.SupportedSurfaces` pins the ordering).
- **`teammate_control_failed` was documented retryable but returned `retryable:false`.** The usual causes
  (a launch still in progress, an unfinished previous control, an unconfirmed pause) all clear on their own,
  so the code now sets `retryable: true` to match the documented contract.
- **`/health` kept advertising a companion port after that process exited.** The launcher deliberately keeps
  the last session handle so a retry cannot start a third window, and the discovery block inherited that
  persistence. It now returns `null` once `CompanionProcessExited` is set.

+### External-takeover route driven by a real external agent (issue #85)

Verified 2026-09-13 on the same isolated dual-instance host (dead model endpoint on `127.0.0.1:18199`,
`roleTests` empty), but this pass answers the stronger question: not "can a script poke the API" but
"can an outside agent actually play the seat". An external agent (Grok 4.6, its own model and its own
context — not the mod's in-process loop) was given only the repository's MCP tool surface and the
bundled play skill, and told to fight the teammate's character itself.

Driver: `build/validation-2026-09-13/mcp-call.py` — a thin caller over the bundled MCP server's tool
surface (`create_server()` + `call_tool`), i.e. the same tools Cursor / Claude / Codex would get, not
the raw `/action` endpoint. Evidence: `external-agent-log.jsonl` (84 lines), `external-agent-final-state.json`.

- **A full battle was played and won by the outside agent.** Teammate side, all through MCP: 16 ×
  `play_card` + 6 × `end_turn` + 4 × `confirm_modal` (the FTUE popups), plus `choose_map_node` to enter
  and to leave. `FUZZY_WURM_CRAWLER` went `121/121` → dead over 7 turns, `screen` went `COMBAT` →
  `REWARD` → `MAP`, gold `99` → `110`, and a second fight (`SHRINKER_BEETLE`) was already under way when
  the session stopped.
- **The teammate never took a turn by itself, and never called a model.** Across the whole run its
  `/health` read `play_running=false`, `play_phase="paused"`, `session_requests=0`, `stop_kind=null`; the
  host reported `companion.auto_play=false`. Every decision came from outside the game process.
- **The two seats stayed independent.** The host window was driven alongside the teammate (same map vote,
  `end_turn` each turn) because the run cannot advance otherwise; each side acted only for its own
  character, which is `CompanionActPolicy` doing its job rather than a shared trigger.
- **The supported entry really is the MCP surface.** No `/teammate/control`, no `/session/control`, no
  `run_console_command` appears anywhere in the 84-line log — checked mechanically, not by reading.

### Found in this session

- **The companion's own `POST /session/control` skipped the play-model gate.** `POST /teammate/control`
  and the overlay's Resume button both refuse to start the loop without a verified model, but the
  companion instance accepted `{"running":true}` and started one. Live confirmation after the fix:
  teammate `/session/control {running:true}` → 409 `session_not_ready` carrying the unverified-model hint,
  while `{running:false}` stayed 200 — pausing is deliberately never gated. All start entries now share
  `FirstRunSetup.ReadyToInvite`; `CoopRoute.SharedModelGate` pins the ordering.
- **Three FTUE popups cost three `confirm_modal` calls each on the companion.** `NCombatRulesFtue`
  returned `status=pending` twice before accepting, and at that moment the host window was already in
  `COMBAT` with cards to play. The companion is not stuck (the third call lands), but an external agent
  that treats the first `pending` as failure would stall there. Not changed in this pass.
- **`get_relevant_game_data` needs `collection` and `item_ids`**, while
  `skills/sts2-mcp-player/SKILL.md` describes it as scene-aware and callable without arguments. Calling
  it the documented way fails with fastmcp `missing_argument`. Documentation gap, not a mod defect.
- **Monster metadata does not carry multiplayer scaling.** `get_relevant_game_data monsters
  FUZZY_WURM_CRAWLER` reported `min_hp=55 / max_hp=57`, while the live enemy was `121/121`. The skill
  already says to trust live state; this is a concrete case where the metadata alone would mislead.
- **On the companion instance, `/health`'s `dual_status` and `team_control_status` still read
  「尚未启动双开」/「队友控制尚未连接」.** Those two fields describe the *host's* dual-launch bookkeeping, so
  they are inert on a companion; the fields that matter there (`instance_role`, `play_running`,
  `play_phase`, `session_requests`) are correct.

### Those three findings, fixed and re-verified in the game (2026-09-13)

Re-run on the isolated offline host (`--clientId 2026091001`, API `18080`) with the patched build
deployed. The Steam profile's 183 files were hashed before and after and came out identical; nothing
touched the real save.

- **The paged combat-rules FTUE was correct behaviour; the contract now says so.** `NCombatRulesFtue`
  really is three pages (`build/sts2-decompiled/MegaCrit.Sts2.Core.Nodes.Ftue/NCombatRulesFtue.cs:165`
  `_totalPages = 3`, and only the click after the last page calls `CloseFtue`) — so three calls were
  always the right number. What was missing is that `pending` looked like a stall. A non-final page now
  answers with `Tutorial page advanced; the modal is still open. Call confirm_modal again.` Live:
  `modal=NCombatRulesFtue` → click 1 `pending`, click 2 `pending`, click 3 `completed`, after which
  `modal` was empty and `screen` had moved to `COMBAT`. Every other modal keeps the old
  `Action queued but state is still transitioning.` wording.
- **`get_relevant_game_data` no longer needs `item_ids`.** Omitting them derives the ids the current
  screen is about from live state, in both mirrors (`GameDataFilter.SceneItemSources` and
  `_SCENE_ITEM_SOURCES`); `tests/test_scene_field_alignment.py` keeps the two tables — keys and paths —
  equal, the same way the scene field sets are kept. Live in the host's fight, over the bundled MCP tool
  surface: `{"collection":"monsters"}` returned exactly the three enemies in that room
  (TWIG_SLIME_S / LEAF_SLIME_M / LEAF_SLIME_S), `{"collection":"cards"}` returned the hand
  (DEFEND_IRONCLAD / STRIKE_IRONCLAD), and `relics` fell back to the run relic (BURNING_BLOOD). Passing
  `item_ids` explicitly still answers as before, and a screen with no ids of that collection to offer
  (combat `potions` with empty slots) answers `{}` rather than inventing one.
- **The payload separates the base roll from the scaled HP.** `combat.enemies[].base_max_hp` carries
  `Creature.MonsterMaxHpBeforeModification` — the same dimension as `monsters.min_hp` / `max_hp` — while
  `max_hp` stays the scaled live value. Live in the two-player run, with both instances reporting the
  same numbers: TWIG_SLIME_S `base=9` (metadata 7–11) → `max_hp=19`; LEAF_SLIME_M `base=33` (32–35) →
  `72`; LEAF_SLIME_S `base=13` (11–15) → `28`. Each is `base × players(2) × act-0 factor(1.1)`,
  truncated. Single-player is the degenerate case — `ScaleMonsterHpForMultiplayer` returns early when
  `playerCount == 1` — which is why the original report had to come from a co-op fight.

Two things surfaced while verifying the derivation, and both are fixed in the same pass:

- **A scene can classify into a scene whose payload is null on that screen.** `FAKE_MERCHANT` reads as
  shop (`DetectScene` matches "merchant"), but `BuildShopPayload` answers `null` there because the
  merchant room it reads does not exist on that screen, so walking `shop.cards[].card_id` stepped into a
  JSON null. The walk is now kind-guarded like its Python twin, and an empty scene answer falls back to
  the run-level ids instead of stopping at `{}`.
- **The two mirrors disagreed on empty ids.** C# accepted an empty-string id that Python skipped; both
  now skip it (the C# path list is only ever fed by the state, but a divergence here would have shown up
  as two different answers for the same screen).

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
