# Live validation checklist

Items that deterministic offline tests cannot settle: they need the game running with the mod
deployed. Collecting them in one place keeps "we proved it offline" from being read as "we saw it
work".

A session that walks this list needs the game installed, the mod built and deployed
(`scripts/build-mod.ps1 -Configuration Release`), and `scripts/start-game-session.ps1
-EnableDebugActions` so the debug console suites can run.

Legend: **[mod]** Mod API online only · **[combat]** needs a fight · **[room]** needs a specific
screen · **[coop]** needs two instances · **[eye]** needs a human or model to watch behaviour.

## Status as of 2026-09-20 (v0.14.0 candidate)

Run against the v0.14.0 candidate build (`STS2AIAgent.dll` deployed from `feat/v0.14-contracts`),
game v0.111.0, in the isolated profile `default\2026092014` (`--windowed --force-steam off
--clientId 2026092014`). The player's real profile was not written to: its `current_run.save` and
`progress.save` still carry their 2026-09-12 / 2026-09-17 timestamps.

### Offline-blocking checks, on the real game **[mod]**

- `/health`: `mod_version=0.14.0`, `game_version=v0.111.0`, `status=ready`, `instance_role=human`,
  compatibility **27/27 reflected members present, 0 missing**.
- `run_sts2_validation.py mod-load --deep-check` → `{"health_ok": true, "state_ok": true,
  "actions_ok": true, "screen": "MAIN_MENU", "available_action_count": 3}`.
- `state-summary` and `state-invariants` → `failure_count: 0`, `warning_count: 0`.
- `patch-check` → exit 0. The recorded `build/validation-2026-09-17/action-surface-baseline.jsonl`
  MAIN_MENU sample was present, all three compared actions matched their flags
  (`mismatches: []`); `abandon_run` / `continue_run` appeared only in the baseline, which is the
  expected diagnostic for a baseline taken during an active run.

### The decision explanation chain with a real model **[eye]**

Provider `CommandCode` (`https://api.commandcode.ai/provider/v1`), model
`deepseek/deepseek-v4.1-flash`, configured through an isolated agent settings file
(`STS2_AGENT_SETTINGS_PATH`) so the player's own agent settings were untouched. The overlay's
**Test Connection** button was pressed for real: 对话模型 and 游玩模型 both reported **连通成功**.

Autoplay was then started from the main menu and ran a real run unattended:

- 45 accepted decisions, all attributed to one run id (`D9P0C6TEVH8N`), advancing from floor 1 to
  floor 3 (one relic picked up, deck 10 → 12).
- **22 of 26** sampled entries carried a model-authored reason, and they are real reasoning rather
  than restatement, for example: *"Cannot kill the 29 HP enemy this turn (3 strikes with Vulnerable
  = 27), so I play Defend to reduce the incoming 6-damage attack to 1."* and *"A first-time shuffle
  tutorial modal is blocking combat, so I confirm it to resume play."*
- `GET /decisions` returned entries carrying `source`, `action`, `reason`, `requests_spent`,
  `total_tokens` and `run_id`.
- `/events/stream` published `decision_made` live (75 frames observed in a 40 s window alongside
  `screen_changed`, `combat_started` and the action-window events).
- The overlay's 决策日志 tab showed the newest-first log with the per-step token count and the
  per-run total, and reported the budget stop: *"已达到会话 Token 预算上限（622,864/600,000
  tokens），已自动停止游玩"* — the session budget guard stopped the loop at the configured cap
  rather than running on.

Session cost, as reported by the mod: **52 requests / 622,864 tokens** (prompt 585,244,
completion 37,620). The 600,000-token cap is what ended the run; the request cap (150) was not
reached.

### Found in this session, fixed

1. **The isolated profile could not enable the mod at all when the player also subscribes on the
   Workshop.** `Initialize-IsolatedClientSettings` flipped only the *first* `STS2AIAgent` entry in
   `mod_list`. A clone of a real profile holds two: `mods_directory` and `steam_workshop`. The
   launcher enabled the first and left the second `false`, and the game reads the id as disabled if
   any entry says so — it logged `Skipping loading mod STS2AIAgent, it is set to disabled in
   settings` twice and started with no mod at all. Fixed by enabling every agent entry (rewriting
   back-to-front) and by making the readiness check inspect all of them;
   `scripts/test-isolated-settings.ps1` pins it offline and runs in the preflight.
2. **A non-numeric `--clientId` silently validated a different profile.** `--clientId 20260920v14`
   made the game fall back to client 1 (`Profile-scoped data path initialized:
   user://default/1/modded/profile1`) while the launcher seeded `default\20260920v14`, so the
   launcher was preparing a file the game never read. `Initialize-IsolatedClientSettings` now
   rejects a non-numeric id with a message that names the fallback.

### Local dual-instance co-op on this candidate **[coop]**

A second run of the same session, after the single-player one above, on the same isolated profile.

- `invite_ai_teammate` from the main menu launched a second process (PID 37872). The host's
  `/health` reported `companion_process_alive: true`, `companion_process_exited: false` and the
  companion block `{api_host: 127.0.0.1, api_port: 8081, process_id: 37872, auto_play: true}`; the
  companion answered on 8081 with `instance_role: companion`, `mod_version: 0.14.0`, `status: ready`.
- The lobby came up with the companion already auto-selected and readied (`CompanionAutoSelectCharacter`),
  and `embark` put both instances into one run (`FRTG1BWGDRJ4`) at MAP. The party block reads
  correctly from both sides: the host sees `1` as local and `2026092015` as not, the companion sees
  the mirror image. That is the same fact the teammate panel is built on.
- **The teammate plays with the real model.** Its `/decisions` holds four entries on the shared run
  id, one of them the co-op rule doing its job rather than a card choice:
  *"跟随你的地图选择。"* (following your map choice) for `choose_map_node`, then three combat
  decisions with model-authored reasons such as *"Bash first applies Vulnerable so my follow-up
  Strike deals bonus damage and next turn's attacks also hit harder."* The companion's own request
  count reached 6 and it advanced the shared run to floor 1.
- The host's overlay teammate panel showed the other player's live health, block and energy beside
  the host's own bar while both characters stood in the same fight.
- The host's own loop correctly refused to restart: `/session/control {"running": true}` returned
  200 `running`, and `stop_kind` stayed `budget`, because the single-player session had already
  spent the 600,000-token cap for that session. The map vote was then cast by hand through the API,
  which is what let the companion proceed.

Not covered here: the **Steam** two-instance path (this was the local dual-instance launcher), and
the typed `intent` encoder over the wire — the host overlay sends `intent: null`
(`AgentRuntime.Team.cs`), so a typed signal needs an external caller holding the companion session
token, which this session did not exercise.

### Still open after this session

- A complete natural run (13 floors) was not attempted: the token cap ended the single-player
  session at floor 3 on purpose. The remaining floors and an act boss are still unproven on this
  candidate.
- **Steam** co-op was not run at all. The local dual-instance path is covered above; the Steam
  two-instance path is untouched by this session.
- The typed teammate signal was not driven over the wire; see the note above.
- Vision (screenshot attachment) was not enabled, so the multimodal path is still offline-only.
- The `max_tokens` → `max_completion_tokens` retry was not exercised by this endpoint: it accepted
  the request as sent, so which real providers take that branch is still unmeasured.

## Status as of 2026-09-17

### Action-surface baseline (ADR 0001 step 1)

Verified against the installed v0.12.4 third build (DLL SHA256 `0A8FBA67…611C`, identical to `dev`'s
runtime code — `git diff v0.12.4 dev -- 'STS2AIAgent/**' 'mcp_server/src/**'` is empty, so no rebuild
was needed and none was done). Isolated offline host, `--windowed --force-steam off --clientId
2026091701`, API on `18080`, single instance, driven entirely over HTTP. Evidence:
`build/validation-2026-09-17/action-surface-baseline.jsonl` (2,346 records, gitignored).

`GET /state`'s `available_actions` and `GET /actions/available`'s descriptors are built by two
separate 301- and 609-line methods. Source contracts pin their sets equal; this pass asked whether
they agree **at runtime**, which is the baseline ADR 0001 needs before the two can be collapsed.

- **12 screens, 22 distinct action-set variants, 2,346 back-to-back sample pairs.**
- **Non-combat: 0 disagreements in 61 samples.** `PAUSE_MENU` agrees on the *empty* set, which is the
  interesting edge case — both surfaces return an empty array rather than one of them omitting the key.
- **Combat: 23 disagreements in 2,285 samples, every one a timing artifact.** All 23 involve exactly
  the three readiness-gated actions `{end_turn, play_card, use_potion}` and no others, and each
  carries `combat.action_readiness.reason` of `ready` (14) or `snapshot_stabilizing` (9) — caught
  mid-settle.
- **The falsifiable test:** if one surface were wrong, the direction of the difference would track
  *the surface*. Across 1,911 order-alternated samples it tracks **which endpoint was read second**:
  15 cases where the second read saw more actions, 8 where it saw fewer, both directions under both
  request orders. `screen` matched between the paired reads in **all 2,346 samples**, so no room
  transition is involved.

**Conclusion: the two surfaces matched at runtime on every screen reached.** ADR 0001's assumption
holds live, and this file is the replay baseline for step 3 of that ADR.

### Found in this session

- **`screen` never reports `GAME_OVER` after a death in combat.** Eight samples caught the run in an
  unambiguous game-over state — the only offered action was `continue_game_over` — and all eight
  reported `screen = "COMBAT"`. No sample in the whole run ever reported `GAME_OVER`. The cause is
  ordering in `GameStateService.ResolveNonModalScreen`: the `FindActiveCombatRoom(currentScreen) !=
  null => "COMBAT"` guard runs **before** the switch arm `NGameOverScreen => "GAME_OVER"`, and the
  combat room is still active at that point (the live readiness block reported
  `combat_in_progress = true`, `combat_room_mode = ActiveCombat` after death). Both action surfaces
  agree and the offered actions are correct, so this is not an ADR 0001 issue — but `docs/api.md`
  documents `GAME_OVER` as a screen, `skills/sts2-mcp-player/SKILL.md` routes on it, and
  `run_sts2_validation.py` branches on it. An agent following the documented contract waits for a
  screen name it will never see and only recovers through the action list.
- **`requires_target` was `false` on every descriptor in all 2,346 samples**, across all 34 action
  names that appeared, including `play_card`. No live sample exercised the `true` branch of that
  field. Whether that is intended (targets are validated per card through `target_index` rather than
  advertised on the action) or a gap is not settled by this pass.

### `requires_target` adjudicated as documented behaviour (2026-09-20)

Every descriptor reports `requires_target: false` **by construction**: the walk assigns the literal
to all actions, because the flag describes the static call shape and no action unconditionally
takes `target_index`. Three actions take it conditionally (`play_card`, `use_potion`, and
multiplayer rest options) and advertise that per item — `combat.hand[].requires_target`,
`run.potions[].requires_target` with `valid_target_indices`, and each rest option's own flag with
the `run.players` target space. `docs/api.md`'s descriptor table now states the general rule (it
previously carved out `play_card` only), and
`ActionSurface.DescriptorTargetIsDocumentedConstant` pins the walk so the flag cannot silently
become dynamic without the docs following it.

### That finding, fixed and re-verified in the game (2026-09-17)

`GameStateService.ResolveNonModalScreen` now claims `NGameOverScreen` with its own guard, ahead of
the combat branch, in the same idiom the capstone container already uses. Re-verified on the same
isolated host against a build carrying the change (DLL SHA256 `96DD9A63…22D7`); the released build
was restored afterwards, byte-identical. Evidence:
`build/validation-2026-09-17/gameover-fix-verification.jsonl` and
`gameover-fix-regression-check.jsonl` (both gitignored).

- **Two independent deaths were driven, both from a real fight.** Across both runs, **10 samples had
  `game_over` non-null and all 10 reported `screen = "GAME_OVER"`; none reported `COMBAT`.** That is
  the exact mirror of the pre-fix baseline, where 8 unambiguous game-over samples all reported
  `COMBAT` and 2,346 samples never once produced `GAME_OVER`.
- **The settle path still works end to end**, which matters more than the rename: `continue_game_over`
  advanced `phase` from `summary_animating` to `summary_ready`, `save_status` reached `verified` and
  `save_verified` reached `true`, then `return_to_main_menu` settled the run. Full screen arc:
  `MAIN_MENU → MAP → COMBAT → GAME_OVER → TIMELINE → MAIN_MENU`.
- **No other screen changed.** All 12 screens the baseline covered were re-sampled (75 samples, 25
  per-screen comparison rows): 16 exact matches, and **not one screen resolved to a different name**.
  Five action-set differences were each traced to a different game situation rather than to the edit
  — three are `discard_potion` absent because no potion was held (the run also reproduced the
  baseline's full five-action combat set exactly, 4 samples, once a potion was held), one is `proceed`
  on a single-option chest that auto-claimed its relic, and one is `open_timeline` on a main menu with
  no run save.
- **The action surfaces still agree on the patched build**: 103 back-to-back samples with alternating
  request order, one disagreement, and it was a `TIMELINE` sample taken at the instant that overlay
  was opening (`confirm_timeline_overlay` seen by the second read only; the next four samples agreed).
  Every combat sample agreed, including across the `snapshot_stabilizing` → `ready` transition that
  produced all 23 of the baseline's disagreements.

The player's real Steam profile was hashed before and after: 184 files, aggregate SHA256
`0D164367…4083` both times, per-file diff empty. Writes landed only under `default\2026091701\`.

### Also seen while verifying (not caused by the fix)

- **The console's room name for rest sites is `RestSite`, not `Rest`** — `room Rest` answers
  `Room 'REST' not found`. Worth knowing before extending `run_sts2_validation.py`.
- **A console command that succeeded reads as a failure when retried.** Re-issuing `room Treasure`
  while already standing in a treasure room answers 409 `Console command failed: the game task
  faulted.`, although the first call had worked. The retry loop in `run_debug_command` therefore
  reports a failure for a command that did what was asked. **Fixed**: the message was the real
  problem -- the fault's own exception was being discarded, so a rejected request and a broken one
  read identically. `DescribeGameTaskFailure` now names the exception, which covers all eleven
  actions that answer through it, and `remove_card_at_shop` gets the same detail through the shared
  describer. The retry loop itself was left alone: with a real message its `last_error` finally says
  something, and changing retry semantics on a guess about the exception text is the sort of thing
  this project has been burned by.

  **Verified live on the patched build** (same isolated host, `--clientId 2026091701`). The first
  `room Treasure` returns `completed` and lands on `CHEST`; the second now answers:

  ```
  Console command failed: the game task faulted: InvalidOperationException: Attempted to start
  new relic picking session while one was already occurring.
  ```

  **The exception turns out to be benign and self-explanatory**: the treasure room's relic-picking
  session is already open, so a second `room Treasure` legitimately cannot start another. That is
  exactly the distinction the old wording made impossible — a request the game refused read the same
  as a request that broke the mod. A repeated `room Monster`, by contrast, still returns `completed`,
  which is why only the treasure path surfaced this.

  The change is invisible where nothing failed: a successful console command still answers
  `completed`, and `/health` plus `/state` on `MAIN_MENU`, `MAP`, `CHEST` and `COMBAT` are unchanged.
  One cosmetic defect was found and fixed in the same pass — the game's message ends in `!` and each
  call site appends `.`, so the first live rendering read `...already occurring!.`; the describer now
  trims the exception's own trailing punctuation, and the quoted message above is from the re-run
  after that fix.
- **After a death settles, the main menu offers `continue_run` / `abandon_run` while `state.run` is
  `null`.** The pre-fix baseline recorded the same action set, so this is not new, but the
  combination is odd enough to deserve its own look.
- **A `TIMELINE` overlay sits between `return_to_main_menu` and `MAIN_MENU`.** An agent routing
  `GAME_OVER → MAIN_MENU` has to close it with `close_main_menu_submenu`, which is what
  `settle_main_menu` already does.

### Not reached, and why

- **`TIMELINE`** — `open_timeline` is not offered on either main-menu variant of a fresh profile (no
  epochs discovered); probed directly and got 409 `invalid_action`. Genuinely gated, and both
  surfaces agree on its absence.
- **`GAME_OVER` as a screen value** — the state was reached, the screen name was not; see above.
- **`CARD_PILE`** — needs a click on a pile during a fight; no console command opens it, matching the
  note already in this file.
- **`SETTINGS` / `COMPENDIUM` / `CARD_LIBRARY` / `RELIC_COLLECTION` / `POTION_LAB` / `STATS` /
  `RUN_HISTORY`** — `PAUSE_MENU` itself was reached by sending Escape to the window, but the pages
  under it need real mouse input: synthetic `SetCursorPos` + `mouse_event` produced no hover, no
  cursor and no click, and keyboard focus navigation was ignored. These need a human at the machine.
- **`MULTIPLAYER_LOBBY`** — deliberately skipped; this pass was single-instance by design.
- `BUNDLE_SELECTION`, `CRYSTAL_SPHERE`, `FAKE_MERCHANT`, `CARDS_VIEW`, `CARD_INSPECT`,
  `RELIC_INSPECT`, `UNLOCK`, `PATCH_NOTES`, `BESTIARY` — not encountered and not attempted.

One coverage gap worth naming: the `CHARACTER_SELECT` variant that offers only
`close_main_menu_submenu` (embark in flight) was seen in a direct read but the run advanced before
the harness sampled it, so it is not in the JSONL.

### Player data

The real Steam profile (`%APPDATA%\SlayTheSpire2\steam\<account>`) was hashed before and after: 184
files both times, identical manifest digest, empty `diff`. The isolated run wrote only under
`default\2026091701\`. The installed mod was not rebuilt or redeployed — `STS2AIAgent.dll` is still
`0A8FBA67…611C`. `DamageMeter` never had to be moved aside: the game log shows it was already
disabled in settings and skipped at load, so its known `RunRngSet.get_Seed()` crash never fired.

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

- **`invite_ai_teammate` launches with nothing verified.** The first call returns 200 `pending`
  (`LaunchDualInstanceAsync` is `Task.Run` and does not wait). Watch host `GET /health` for
  `companion_process_alive`, `dual_status`, then the `companion` block until the second process
  is up; on 2026-09-13 that was `role=companion` on `18081`, and its message no longer promises auto-play:
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

### External-takeover route driven by a real external agent (issue #85)

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
  **Current code changes these host-only health keys (including companion process/discovery and launch outcome)
  to `null` on the companion while preserving the key shape** — fixed and re-verified in the game on
  2026-09-20, with a companion whose `/health` really reported `degraded`; see the section below.

### The demand-driven event stream, measured in the game (2026-09-20)

Isolated profile (`--clientId 2026092001` semantics: an off-profile settings.save with the mod
enabled), game v0.111.0, mod 0.13.0 plus this round's changes, API on `18080`, process 27496/55512.
Every number below came from `GET /health`'s `state_build.samples` around real `/events/stream`
connections:

- **Zero subscribers build nothing.** `samples` went `0 -> 0` over 12 s on a freshly loaded process,
  and `354 -> 354` across a later idle window.
- **One subscriber starts the single poll loop.** `samples` grew from `0` to `85` while a stream was
  held open, then kept growing at ~6/s (the 120 ms interval, with build cost counted in).
- **First subscriber ordering.** The first stream of a fresh process delivered `session_started`
  (`event_id` 1) and then `stream_ready` (`event_id` 2) with the same `run_id`/`screen`.
- **Reconnect ordering.** A later stream delivered `stream_ready` alone, because a snapshot now
  existed — the documented split between the two paths.
- **The first run of this loop had a real bug the offline tests missed.** Every poll re-announced the
  snapshot, so a client on a stationary `MAIN_MENU` received ~60 `stream_ready` frames in 10 s. The
  fix (record the snapshot, publish only changed payloads) was deployed and re-measured: 1 frame, and
  `samples` growth confined to the poll loop.
- **Disconnect lag is real and bounded, not zero.** After the client closed, polling continued for
  ~33 s (two 15 s heartbeats) before two consecutive failed heartbeat writes ended the subscriber.
  In TCP terms the connection was not closed until then, so counting it was correct; shortening the
  heartbeat is a deliberate decision rather than a free win. A client that resets the connection is
  noticed sooner.
- **Existing game-connected suites still pass** with `--base-url http://127.0.0.1:18080`:
  `mod-load --deep-check` (`health_ok/state_ok/actions_ok`, `MAIN_MENU`, 5 actions),
  `state-summary`, and `state-invariants` (5 actions, 0 failures, 0 warnings).

### The degraded companion, with a real `degraded` payload (2026-09-20)

The one thing the offline tests cannot supply is a companion that really is degraded. It was made
real by forcing a single registry lookup to miss (`ReflectedGameMembers.Resolve` returning null for
`NDevConsole._devConsole`), which is exactly the shape of a renamed private member after a game
patch. The build was deployed, used for the measurements below, and then reverted; the installed mod
and the profile settings were restored afterwards.

- **`/health` reported the degradation honestly**: `status=degraded`,
  `compatibility.reflected_members_missing=1`, `missing_members=[{"member":"NDevConsole._devConsole",
  "feature":"run_console_command"}]`, and the log carried
  `Compatibility: 1 of 27 reflected game members are missing ... status "degraded"`.
- **The companion kept serving while degraded**: `instance_role=companion`, `play_running=false`,
  `play_phase=paused`, `session_requests=0`, `api_port=18082`, `process_id=24224`, and its API answered
  every request. Nothing about the degradation stopped the process being a usable teammate.
- **The host-only keys are `null` on the companion and still present**: `companion_process_alive`,
  `companion_process_exited`, `companion`, `dual_status`, `dual_launch_outcome` and
  `team_control_status` all exist in the payload with value `null` — the key shape the fix promised,
  instead of the host's `尚未启动双开` / `队友控制尚未连接` text.
- **`CompanionHealth.IsExpectedProcess` accepted that exact payload.** A throwaway probe fed the
  captured response through the shipped check: the live degraded payload, the same payload with
  `status` forced to `ready`, and the same payload with `degraded` were accepted, while an unknown
  status, an empty status, a null status, a missing status, a wrong port, a wrong pid, a wrong role, a
  wrong service, `ok=false`, an array `data`, and malformed JSON were all rejected — **14/14**. Before
  the fix the first three of those were rejections, which is what made a degraded teammate look like a
  replaced one.

### The slow-subscriber overflow, observed in the game (2026-09-20, second attempt)

The first attempt could not reach 256 state changes (kept below for the record). The debug action
added for exactly this purpose made it reachable: `POST /action
{"action":"inject_event_churn","option_index":N}` publishes N numbered synthetic `debug_churn`
events through the **same** publishing path as every other event. Host instance, API `18080`, game
v0.111.0, mod 0.13.0 plus this round:

- **A count that cannot fill a queue is refused.** `option_index: 100` → HTTP 400 `invalid_request`,
  `count must be at least 257 to fill one subscriber queue (capacity 256), so nothing below that
  proves anything.`
- **A count that can fill one is published.** `option_index: 400` → HTTP 200 `completed`,
  `Published 400 synthetic debug_churn event(s).`
- **The server disconnected the subscribers that fell behind, and said so.**
  `[WARN] [STS2AIAgent.GameEventService] Disconnected 2 slow event subscriber(s); reconnect to
  resynchronize state.` Two, because neither stream kept up: the deliberately unread one, and the one
  the probe was draining too slowly to absorb 400 events arriving in a single burst.
- **Nothing was silently dropped in place of a disconnect.** The healthy subscriber received its
  `stream_ready` and then `debug_churn` frames; the frames stop where its own queue filled, which is
  the contract — a full queue ends that subscriber instead of evicting the oldest event and pretending
  nothing happened.

That closes the last offline-only item for this round. One caveat worth stating: the
`Disconnected 2` line cannot tell the two cases apart by itself, so the evidence that the healthy
subscriber kept working is the frames it counted before its own queue filled. A future run could keep
the healthy reader ahead of the burst (`option_index` well under what a fast drain absorbs) to watch
one subscriber dropped while another survives the same burst.

### Why the first attempt failed (kept for the record)

A stream that is never read, plus enough state changes to fill its 256-slot queue, is what the
disconnect contract needs. Reaching that on a live instance was not possible at first, and the reason
is worth recording rather than leaving as "not done":

- The poll loop only publishes an event when a digest field actually changes, so a client sitting on
  `MAIN_MENU` receives nothing to fall behind on — the queue stays empty no matter how long it holds
  the connection.
- Driving 256 changes from the outside is not available either. The debug console is the natural
  tool, but every `run_console_command` goes through `RunManager.Instance.DebugOnlyGetState()` and
  the player lookup, so it answers 409 `invalid_action` until a run exists; the run-provoking actions
  (`act`, `fight`) are themselves console commands, and the main menu's own actions do not cycle a
  screen (`switch_profile` and `abandon_run` both left the screen at `MAIN_MENU`).
- That is why `inject_event_churn` exists: it publishes through the real path instead of waiting for
  the game to cooperate.

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

- `GET /data/powers` returned 500 for the whole collection: a `MOCK_*` power the localization tables
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
- Crystal Sphere: every item whose cells are not all clear reports `kind = null` and
  `is_good = null` on both `/state` and compact `get_game_state`, and both fields fill in only on the
  read after the last occupied cell is cleared; `x/y/width/height`, `cells` and `hidden_cells` stay
  readable throughout. The keys never disappear, so a client that branches on `undefined` sees null.
- Crystal Sphere: the divination count is unchanged by the null identity fields — clearing, tool
  switching and the final `proceed` behave exactly as before, so the fix is invisible to an agent
  that only ever read occupancy.
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
- Every monster exports a non-empty `moves` list with no repeated id, each `name` a short move title
  rather than dialogue (verified 2026-09-18: 107 / 107, 0 duplicates).
- Exported fields match `GameDataExportSchema.cs` and the scene field tables the MCP server uses for
  `get_relevant_game_data`.
- Exported collections stay aligned with the Python client's action surface: every action the client
  exposes is one the mod accepts.

## [coop] Two instances

- `invite_ai_teammate` returns 200 `pending` on the first call of a real dual launch
  (`LaunchDualInstanceAsync` is `Task.Run`, so the action does not wait for the second process).
  Do not expect `completed` on that HTTP response; watch host `GET /health` for
  `companion_process_alive`, `dual_status`, and the `companion` discovery block
  (`api_host`/`api_port`/`process_id`). A concurrent invite while that launch is in flight also
  returns `pending` (`DualLaunchOutcome.InProgress`) and must not be read as `completed` or
  `invite_failed`. A repeat invite after the teammate window is already running likewise returns
  `pending` on the HTTP call; the background attempt then fails and `dual_status` shows the
  already-running refusal. `409 invite_failed` only appears if that launch task has already
  finished in failure when the handler inspects it. Client language is not a success/failure
  signal; classification is on `DualLaunchOutcome`, not localized `dual_status` text.
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
- **The AI Teammate tab's two co-op controls were pressed on a real machine on 2026-09-14** (#111), on an
  isolated offline host (`--windowed --force-steam off --clientId 1`, API 18080) whose co-op save is the local
  test save (players `1,2`). Both work:

  - **Disable automatic character pick**: ticking it wrote `companionAutoSelectCharacter: false` to the settings
    file at that instant, the line under it switched to the "waits on the character screen" wording, and the
    teammate launched by the **mouse-clicked** Invite button came up on `CHARACTER_SELECT` with
    `play_running=false`, still there 70s later with both slots `is_ready=false` — it did not pick for itself.
  - **Continue the saved co-op run**: the mouse-clicked button launched the companion itself (PID 71620, API
    18081); both sides reached `MULTIPLAYER_LOAD`, both `embark` presses completed, and the run resumed in
    `COMBAT` with the save's numbers (80/80 HP, 113/112 gold, floor 2); the teammate stayed
    `play_running=false / play_phase=paused / session_requests=0`, i.e. waiting for external takeover. Evidence
    in `build/validation-2026-09-14/evidence/` (gitignored).

  The pass also found and fixed a real defect, which is what a live pass is for: the Continue button is the only
  control on that page whose availability depends on the *game screen*, and the page refreshed it only when the tab
  came into view. A panel already open on this tab therefore kept the button greyed out for the whole boot modal —
  the action was available over the API the entire time — and only a tab switch brought it back. The panel tick now
  re-reads it (`RefreshContinueAvailability`), `CoopRoute.OverlayEntries` pins that, and the fix was re-verified on
  the same live host: grey while the modal was up (brightness 364), bright 4 s after it cleared with no tab switch
  (660).

  Two things the pass deliberately did not cover: the Steam-hosted save path (this host is offline/local-connection,
  which is the path this feature exists for), and `DamageMeter`, an unrelated 2026-05 mod in `mods/` that throws
  `MissingMethodException: RunRngSet.get_Seed()` inside its own `OnRunStarted` when a run starts on game v0.111.0. It
  was moved out for the pass and restored afterwards; nothing in this repository needs changing for it.

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

## 2026-09-18 — 兼容性探测与两轮大重构（游戏 v0.111.0）

隔离实例（`--windowed --force-steam off`，clientId `2026091801` / `2026091802`，API 18080），
分支 `feat/upstream-break-visibility`。

**探测在第一次实机运行就抓到一个真 bug。** `/health` 返回 `degraded`，点名
`NEndTurnLongPressBar._longPressDuration` 缺失。根因不是探测误报：该字段是**静态**的，
而注册表把它登记成实例成员——`GameActionService.Combat.cs` 里真正读它的代码
**用的也是实例标志**，所以自这行写下来起就从未读到过真值，一直用写死的 `0.45` 兜底，
而游戏的实际值是 `0.5`。长按等待每次少 50ms，没有任何地方会说。

离线读元数据得出的「22 个全部存在」是**对名字而言正确、对问题而言无关**：
一个成员的名字在不在，说明不了绑定标志够不够得着它。这是实机唯一能给出的结论。

修复后（`6771b0e`）复验：`status: ready`、`reflected_members_checked: 22`、
`reflected_members_missing: 0`、`missing_members: []`。

**注入故障验证**：把 `_saveAndQuitButton` 改名为一个不存在的名字、重建重启，
`/health` 精确点名 `NPauseMenu._saveAndQuitButtonFixtureGone`（feature `save_and_quit`）
并报 `degraded`。随后按字节还原。

**两轮大重构首次有实机证据**（PR #148 / #150 的 partial 拆分此前只有源码级证明）：
主菜单 `/state` 200 且 `screen: MAIN_MENU`——v0.12.4 那次「主菜单每个 `/state` 都 500」的
回归未复现；`/actions/available` 非空；`open_character_select → select_character → embark
→ MAP → choose_map_node → COMBAT` 全链路走通（途中一次性 FTUE 弹窗，`dismiss_modal` 后继续）；
`end_turn` 返回 `completed` / `stable: true`，`turn` 由 1 推进到 2。

**未验证**：长按时长是否真的用上了 0.5——这个数字在 HTTP 面上看不出来，没有勉强去证。

玩家真实存档两轮前后各哈希一次：`default/1`、`default/2`、`default/1001` **逐文件一致**；
差异全部落在两个新建的隔离 clientId 目录、Godot 日志滚动与 Sentry 运行记录。
`mods/` 三个文件已按字节还原到已发布的 v0.12.5（DLL `1624BBF5…D4A7`）。
证据：`build/validation-2026-09-18/`（gitignore）。

**第三轮（`d3e0d47`，clientId `2026091803`）——统一查找之后。** 调用点改为只向注册表要成员、
注册表 23 条（新增 `NEndTurnButton.CanTurnBeEnded`）、探测改为加载时运行并写日志、删除 4 处死反射之后：

- `/health`：`ready`，23 检查 / 0 缺失；`godot.log` 出现 `Compatibility: all 23 reflected game members resolved.`
- 注入故障：`/health` `degraded`、缺失**恰为 1** 并点名 `NPauseMenu._saveAndQuitButtonFixtureGone`；日志同步出现两行 `WARN`
- 读取改了路径的功能逐一走通，**零 500 / 零 `internal_error`**：`combat.action_readiness`（`CanTurnBeEnded`）、
  `end_turn`（回合 1→2）、`run_console_command help`（`_devConsole`）、`save_and_quit`（`_saveAndQuitButton`）
  → `continue_run`（`_standardButton`）回到同一局，以及 `die` → `GAME_OVER` → `continue_game_over`
  → `return_to_main_menu`——删掉三个死查找后结算流程照常推进（**更正，同日稍晚**：当时归功于
  「pressed 信号那一半」，是错的。`NButton` 是 `Control` 而非 Godot `BaseButton`，没有 pressed 信号，
  那一发什么也没做；推动流程的是紧随其后的 `ForceClick()`。该信号与 `Set("disabled")` 已一并删除）
- 日志 `error|exception|fatal` 扫描：只有三处 Godot 引擎自身的 `Invalid Task ID`，无源自本 mod 的异常

玩家档案 `default/1`、`default/2`、`default/1001` 逐文件一致；`mods/` 还原到发布版 v0.12.5。

**第四至六轮（`fix/monster-moves-export`，clientId `2026091804`–`2026091806`）——怪物招式导出。**
`GET /data/monsters` 的 `moves` 在装着的游戏上一直是空数组（`MonsterModel.MoveNames` 已不存在）。

| 轮次 | 提交 | 有招式 / 总数 | 含重复 id 的怪物 | 发现 |
| --- | --- | --- | --- | --- |
| 4 | `67f01ee` 直接调本地化 API | 104 / 107 | 9 | 同一前缀下还有台词 key，`FAKE_MERCHANT_MONSTER` 的 ENRAGE 出现三次、名字是台词 |
| 5 | `4f1ab94` 只取 `.title` | 104 / 107 | 0 | 三个 `DECIMILLIPEDE_SEGMENT_*` 仍空：它们共用一套文案，key 不是自己的 id |
| 6 | `f7208fd` 前缀取自 Title key | **107 / 107** | **0** | 三个分段各得 BULK / CONSTRICT / DEAD / REATTACH / WRITHE；其余 104 个与第五轮逐条（id + name + 顺序）一致 |

三轮 `/health` 均为 `ready`、23 / 0。玩家档案 `default/1`、`default/2`、`default/1001` 逐文件一致；
`mods/` 三个文件还原到发布版哈希。

**第七轮（`bc6ada6`，clientId `2026091807`）——失败，而且是离线看不出来的那种。** 注册表补登
`NMultiplayerTest.StartHost` / `ReadyButtonPressed` / `Disconnect` 与 `NPauseMenu.CloseToMenu` 后，
mod 加载即崩：注册表连基类一起搜，`NMultiplayerTest` 的私有 `Disconnect(NetError)` 与 Godot
`GodotObject` 的公开 `Disconnect(StringName, Callable)` 同名，`GetMethod` 抛 `AmbiguousMatchException`。
27 条在同一个 `Lazy` 里解析，异常被缓存，于是加载时自检、`/health` 与一切查注册表的动作都 500——
连毫不相干的 `open_character_select` 也是。离线读元数据只证明了「这个名字在这个类型上」，
证明不了「按这组标志找只找到一个」。

**第七轮 b（`d9b9c02`，clientId `2026091808`）——通过。** 查找改为只看声明类型（27 条逐一核对均声明在所登记的类型上），
解析器永不抛异常且离线用同形状的假类型复现了该异常：

- `/health`：`ready`，27 / 0；日志 `Compatibility: all 27 reflected game members resolved.`，全会话 `Ambiguous` 0 处
- `save_and_quit`（经注册表调 `CloseToMenu`）→ `continue_run` 回到同一局
- `win` 进奖励页 → `collect_rewards_and_proceed` 回地图
- `die` → `continue_game_over`（现在只靠 `ForceClick`）→ `return_to_main_menu`
- 联机测试大厅 `host` / `ready` / `disconnect` 全部 `completed`——正是第七轮崩在的那条路径
- `Completed 500` 0 次（第七轮 282 次）

玩家档案 `default/1`、`default/2`、`default/1001` 逐文件一致；`mods/` 还原到发布版哈希。

**第八轮（`6dc592b`，clientId `2026091809`）——按类型读取、行为契约与构建计时，通过。** 用调试命令构造局面后查 `/state`：

- 能力：玩家 `STRENGTH_POWER` 3（`is_debuff: false`）、敌人 `VULNERABLE_POWER` 2（`is_debuff: true`）
- 遗物计数：`PEN_NIB` 的 `stack` 为 `0`、起始遗物 `BURNING_BLOOD` 为 `null`——此前所有遗物恒为 `null`
- 药水：`BLOCK_POTION` 的 `rarity: "Common"`，`description` 非空
- 关键词与附魔：`SECOND_WIND` 的术语匹配含「消耗」；`enchant SHARP` 后该牌 `mods` 为 `["Enchantment","锋利"]`、术语匹配含「附魔」——此前所有卡恒为空
- 抽/弃/消耗堆 5 / 0 / 0，与手牌 6 张合计等于牌组 10 张；`act_id: "0"`，`boss_id: "VANTOM_BOSS"`
- `discard_potion`、`use_potion`、`end_turn` 均 `completed`；`Completed 500` 0 次
- `/health` 的 `state_build`：3,049 次构建，p50 2.8 ms、p95 16.8 ms，只有 1 次过阈（113.8 ms，`MAIN_MENU`），
  日志恰好一条对应的 `WARN`。这是第一份真实分布：100 ms 的阈值在常态下不会误报

未验证：`open_character_select` 拿不到界面时的 503（未能构造）、`crystal_set_tool` 的前置检查（未进水晶球事件）。
玩家档案 `default/1`、`default/2`、`default/1001` 逐文件一致；`mods/` 还原到发布版哈希。
