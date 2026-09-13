# Changelog

> Release attribution is recorded against tags or release commits. Post-tag maintenance is listed separately; current validation limits are maintained in [PRODUCT_PLAN_CURRENT.md](https://github.com/CharTyr/STS2-Agent/blob/main/PRODUCT_PLAN_CURRENT.md).

## v0.12.2 - 2026-09-13

> The co-op handoff: the player who wants to fight their own character while an outside agent drives the
> teammate window no longer has to fake a passing model test to get there, and the state that agent reads
> stopped contradicting itself. Release and Workshop upload:
> [release-v0.12.2_2026-09-13.md](history/release-v0.12.2_2026-09-13.md). Live:
> [docs/live-validation-checklist.md](docs/live-validation-checklist.md).

### Added

- `POST /teammate/control` on the host window starts or pauses the teammate, with no companion session
  token: the host already holds that token and uses it against the companion’s own `POST /companion/control`
  (#85). `{"running": true|false}` returns `phase` / `play_running` / `play_phase` / `companion_auto_play`;
  it is loopback-only and host-only (403 `local_only`, 409 `not_host`), and a control that cannot be
  confirmed is 409 `teammate_control_failed`. Until now the teammate could only be started or paused from
  the in-game overlay, so an external agent had no supported way to do it.
- `GET /health` carries a `companion` block on the host once a teammate is connected
  (`api_host` / `api_port` / `process_id` / `auto_play`), so a caller can find the teammate’s own
  `GET /state` and `POST /action` instead of guessing the port. The session token is not part of it.
- `docs/api.md` documents the two co-op routes side by side, the new endpoint, and the new field; both
  READMEs gained an “Option C: hand the teammate window to an external agent”.
- `combat.enemies[].base_max_hp` on both the raw and the compact enemy payload (#101). It carries
  `Creature.MonsterMaxHpBeforeModification` — the same dimension as the `monsters` collection’s
  `min_hp` / `max_hp` — while `max_hp` stays the scaled live value. Co-op multiplies monster HP by
  `players × act factor` before it reaches the live creature, so a metadata lookup and a live enemy used to
  read as a contradiction (metadata 7–11 next to a live 19) with nothing in the payload to resolve it.
  Live two-player: `base_max_hp` 9 / 33 / 13 against live `max_hp` 19 / 72 / 28, both instances agreeing.

### Changed

- **Inviting a teammate no longer requires a verified play model.** The route is chosen by
  `FirstRunSetup.Evaluate(settings).ReadyToInvite`, which is the same signal the overlay already used: with a
  verified play model the teammate auto-plays exactly as before, and without one it still joins the lobby and
  starts the run, then parks — its process gets `STS2_AGENT_AUTOPLAY=0`, so it plays nothing and, more to the
  point, never calls a model at all (#85). `invite_ai_teammate` and `continue_ai_teammate` share this switch,
  and the launch status line now says which route is running instead of promising auto-play either way.
- The conditions both routes still enforce are unchanged and are now a named policy
  (`CoopLaunchPolicy.GetStructuralError`): not the companion instance, no auto-play already running on this
  character, main menu. The split is scoped to the model gate and is not a way around those.
- **A paged tutorial now says where it is instead of looking stuck (#101).** `NCombatRulesFtue` is three
  pages by design: one confirm advances a page and leaves the modal open, and only the click after the last
  page closes it. A non-final page answered `pending` with the same wording as a stalled transition, which
  reads as failure to a caller that treats `pending` that way. It now answers `Tutorial page advanced; the
  modal is still open. Call confirm_modal again.`; every other modal keeps the old message.
- **`get_relevant_game_data` works without `item_ids` (#101).** Omitting them derives the ids the current
  screen is about from live state — the hand in a fight, the stock in a shop, the event you are in — and
  falls back to the run-level ids when the scene has nothing to offer. Passing ids explicitly still answers
  exactly those. The id sources exist once per implementation and a test keeps the two tables equal, keys and
  paths, the same way the scene field sets are kept.

### Fixed

- The reported co-op route described the launch that was *attempted*, not the teammate that is *running*: a
  rejected retry — usually “the teammate window is already running” — overwrote the flag before the launch
  check, so `/health` could report `auto_play: false` for a teammate the in-process loop was actively playing,
  inviting an external agent to take over a seat already in use.
- `/health` stopped advertising a companion API port after that process exited.
- `teammate_control_failed` is `retryable: true`, matching what the docs already said; its causes (a launch in
  progress, an unfinished previous control, an unconfirmed pause) all clear on their own.
- **The companion’s own `POST /session/control` skipped the play-model gate (#99).** The host’s Resume
  button, `POST /companion/control` and `POST /teammate/control` all refused to start the loop without a
  verified play model, while the teammate instance accepted `{"running": true}` and started one — the one
  start entry that could begin a model loop every other entry rejects. Every start entry now shares
  `FirstRunSetup.ReadyToInvite`; pausing is deliberately never gated, so nothing can be stuck unable to stop.
- Deriving those ids stepped into JSON nulls (#101). `FAKE_MERCHANT` classifies as a shop screen, but the
  `shop` payload is `null` there — the merchant room those ids come from does not exist on that screen — so
  walking `shop.cards[].card_id` threw instead of answering. The walk is now guarded by JSON kind, and a
  scene with nothing to offer falls back to the run-level ids rather than stopping at an empty answer. The two
  derivations also agreed on empty-string ids, which they previously did not.

## v0.12.1 - 2026-09-13

> The pause boundary: once a person presses pause, the agent no longer reads the run underneath. The pause menu and every page reached from it report their own screen, their action surfaces stay closed, and no page's own furniture — the pause menu's "abandon" entry among it — arrives as a capstone option any more. Release and Workshop upload: [release-v0.12.1_2026-09-13.md](history/release-v0.12.1_2026-09-13.md).

### Fixed

- The in-game pause menu is no longer mistaken for an open capstone screen (#88, `31296bd`). The menu rides in the same `NCapstoneSubmenuStack` container the capstone path matches, so `/state.screen` stayed `COMBAT`, `capstone.options` listed the menu's own buttons (`继续` / `设置` / `放弃` / `保存并退出` / `BackButton`) and `choose_capstone_option` was advertised on a screen where it did nothing — one of those options abandons the run. The container now resolves to `PAUSE_MENU` before the combat and visible-grid branches, `GetCapstoneButtons` excludes it, and both action surfaces stay empty while it is up. Live: pausing in a fight reports `PAUSE_MENU` with an empty action list and a null `capstone`, and Escape resumes.
- `continue_ai_teammate` refuses before a mismatched `--clientId` can destroy the co-op save (#89, `31296bd`). The game canonicalizes the run it loads against the loading process's player id and, on a mismatch, renames `current_run_mp.save` and its backup to `*.VAL.corrupt` without restoring them. The action now reads `players[].net_id` from the save and compares this host's NetId and the NetId the teammate would be launched with before it loads anything: a mismatch is 409 `invalid_action` naming both ids, the save is left byte-identical, and no second process is started.
- The pages reached from the pause menu report themselves instead of the room underneath (#93, `04748f6`): the compendium hub read as `COMBAT` and the card library as `CARD_SELECTION`, each carrying the run's own actions and the page's furniture as `capstone.options` — 51 entries on the card library screen, the first 25 of them labelled `Hitbox`. `SETTINGS`, `COMPENDIUM`, `CARD_LIBRARY`, `RELIC_COLLECTION`, `POTION_LAB`, `BESTIARY`, `STATS` and `RUN_HISTORY` now name themselves when the container's stack holds them, none advertises a room action or `save_and_quit`, `capstone` stays null, and `choose_capstone_option` answers 409 `invalid_action` on every one of them.
- Those pages have a working way out again: `close_main_menu_submenu` pops the container's stack, which is the call the game wires to each page's own BackButton (`CARD_LIBRARY` → `COMPENDIUM` → `PAUSE_MENU`). The action the play skill documented for them, `close_cards_view`, was 409 there — the page sits in the container rather than on a card-viewer screen — so an agent that followed the skill had nothing it could do. The pause page itself is never closable: popping it resumes a run a person paused.
- `save_and_quit` no longer executes from a page whose surface never offered it (`04748f6`). The pause overlay emptied both action lists, but the availability predicate still allowed the action, so it saved and quit the run. The capstone overlay is now part of that predicate.
- A deeper page pushed above the pause menu is no longer reported as a frozen game (#92, `3cf347a`). Matching the container's type alone reported `PAUSE_MENU` for the compendium and the card library opened from it, which also hid the `CARD_LIBRARY` branch behind an inert screen.
- A throwing console command answers honestly: `run_console_command bestiary` is 409 `invalid_action` carrying `Console command failed: NullReferenceException: …` and the command name, where it used to be a bare 500 `internal_error` with `details: null` for a command the game had accepted.

### Changed

- `docs/api.md` documents the new screen names and what those pages offer; `docs/live-validation-checklist.md` records the two live passes that found and confirmed the behaviour, including the two defects the issue had assumed were fine (the documented `close_cards_view` return and the reachable `save_and_quit`).
- The play skill — `skills/sts2-mcp-player/SKILL.md` and its screen playbooks, both embedded in the mod's own prompt — now states what the in-run menu pages really offer instead of claiming a return path that returned 409.
- `scripts/test-multiplayer-lobby-flow.ps1` treats the in-run menu pages as "wait for the human" instead of failing with `Unsupported run progression state`; `PAUSE_MENU` had always thrown there. `scripts/run_sts2_validation.py`'s screen-coverage note names the pages too.


## v0.12.0 - 2026-09-13

> Co-op is the headline: a saved multiplayer run can be continued with the AI teammate, and the teammate's character can be left for you to pick. The rest is a trust pass over the agent-facing state — every signal it surfaces now matches what the executor accepts, and no game-side wait can hang a request. Release and Workshop upload: [release-v0.12.0_2026-09-13.md](history/release-v0.12.0_2026-09-13.md).

### Added

- `continue_ai_teammate` continues a saved multiplayer run and brings the local AI teammate back with it (`6cba491`, #83). Loading a save used to host it over Steam, so the locally launched companion never reconnected and the host was left looking at a disconnected portrait; every co-op run had to be finished in one sitting. The load screen is now its own state, `MULTIPLAYER_LOAD`, `embark` works on it, and a failure after the load flow has started returns the retryable `continue_failed` (unmet preconditions stay `invalid_action`).
- `CompanionAutoSelectCharacter` (default `true`, which is the old behaviour) lets you pick the teammate's character yourself (`9abcbde`, #84). With it off, the teammate still joins the lobby but makes no character decision on either screen where one is made; you pick in the teammate window, or an external agent does it through `select_character` then `embark`. Waiting for that choice is no longer charged against the five-minute bootstrap budget, so nobody is timed out for being away from the window.
- Screens that used to fall back to `CARD_SELECTION` or `MAIN_MENU` have their own names: `CARD_LIBRARY` and `CARD_PILE` (`5b3439c`), plus `FAKE_MERCHANT`, `PATCH_NOTES`, `CARD_INSPECT`, `RELIC_INSPECT` and `FEEDBACK` (`5457e0d`). The card viewers can be closed again — `close_cards_view` for the pile, `close_main_menu_submenu` for the in-run library — and the fake-merchant store is reachable with `open_shop_inventory`.
- The compact agent view carries the fields a decision actually needs (`ca12a4f`): power lines for both sides, enemy `intents[]` with `damage` / `hits` / `total_damage`, the `players[]` party block, `run.relic_ids`, a `card_id` on every card in a selection, and `modal.underlying_screen`. `get_game_state` now reports `compact_agent_view` so a client can tell the compact payload from the raw fallback, and `wait_until_actionable` reports `actionable` on every return path (`33b137e`).

### Fixed

- `GET /data/powers` returned 500 for the whole collection (`7888566`). ModelDb carries entries the localization tables do not cover — `MOCK_` powers among them — and `GetFormattedText` threw for those while the export streamed. The guarded lookup now also covers relics, potions, monsters, characters, the event act name and the event option text; on a live game powers returns 283 entries and the two uncovered ones report a null name instead of failing.
- The reward overlay reports `REWARD` instead of `CARD_SELECTION` once the card reward is claimed (`eec80b9`). It carries visible grid card holders, so it used to be named after the generic grid branch and sent the model after `select_deck_card`, an action that screen deliberately does not offer.
- Every signal the state surfaces now matches what the executor accepts (`492722a`): `selection.can_confirm`, `modal.can_confirm`, `resolve_rewards.requires_index` (`true` → `false`), and `skip_reward_cards` only when its enabled fallback button exists. `select_deck_card` is likewise advertised only where it can actually execute (`ca12a4f`).
- Card-reward choices can no longer leak across calls. A skip is scoped to the reward set that recorded it (`fa7ffbe`), and the choice travels with the request instead of a process-wide static field, so a drain that never reaches the reward screen cannot hand its choice to a later `collect_rewards_and_proceed` (`abc195f`).
- Action trust (`72c96fd`): an out-of-range `option_index` on `resolve_rewards` is 409 `invalid_target` instead of silently taking the first card; an open modal is no longer reported as a completed `continue_run` / `embark` / `open_character_select`; a failed `remove_card_at_shop` purchase is no longer swallowed into `pending`; a failed bundle action no longer returns a fabricated empty state as `completed`; and `play_card` rolls back its optimistic turn count when the card has not left the hand.
- Every game-side `await` is bounded (`12c35b3`). Nine awaits could hang the HTTP request indefinitely; a timeout now returns `pending` naming the action, and a faulted task returns 409 `invalid_action` instead of a lost response.
- `choose_timeline_epoch` takes its index from `timeline.slots[].index` the state actually printed (`5457e0d`). The executor had indexed a filtered list while the payload numbered the full one, so a correct reading of the state could 409 or pick the wrong epoch; a slot that is not actionable is now 409 `invalid_target`.
- A menu wait no longer treats a destroyed node as proof that a transition settled, which used to let `open_timeline` report completion after the main menu was gone (`adcb49b`).
- `invite_ai_teammate` classifies on the structured launch outcome rather than Chinese substrings (`206a0e8`). On a non-Chinese client the old match reported every failure as a success.
- The in-game loop stops on no progress instead of spinning: a repeated action that leaves the state unchanged, or executed actions that never settle the screen, now stops with a visible reason and a backoff, and the false stop is gone too (`40735b5`). The in-game path also keeps `error.code` and `retryable` through the error envelope, and a pending action is no longer counted against the failure budget (`f7dccfe`).
- `get_relevant_game_data`'s per-scene field sets match the real export schema again — `min_hp` / `max_hp` / `damage_values` / `block_values` for combat and monsters, `target_type` / `usage` / `pool` for shop and potions, and new combat and potion entries (`f57cb04`).
- An installed wheel no longer writes `agent_knowledge` next to site-packages and hand back a reference path that does not exist; without a checkout it falls back to the working directory with a warning (`52bafd0`).

### Changed

- `resolve_rewards` accepts an optional `option_index` and a `card_index` alias, so the full profile no longer requires an index that the compact view does not promise (`33b137e`, `abc195f`).
- The packaged game-data snapshot under `mcp_server/data/` is gone (`5cc314f`). Its schema had already diverged from the live export and the loader that read it was removed in v0.6.1; this only affects the wheel and sdist contents.
- Offline gates got both wider and self-checking: every `.ps1` under `scripts/` is parsed on every gate run (`abb99c5`, which also fixed `serve-sts2-network-mcp.ps1`, a script that had never parsed), the 16 mod sources outside the compiled test project have a Roslyn syntax net (`335ba0a`), `docs/` is tracked with a gate against untracked pages, and `package-release.ps1` now refuses to package when the five version sources disagree (`6e56e1d`). CI pins the .NET SDK in `global.json` so the coverage test stops building net9.0 with a .NET 10 Roslyn (`61146a5`).
- `docs/api.md`, both READMEs and the play skill are back in sync with the shipped contracts, including the bounded-wait envelopes and the new screen names (`3f55a3f`, `c060554`). [docs/live-validation-checklist.md](docs/live-validation-checklist.md) collects the checks that only a live game can settle (`4554c8d`).

## v0.11.0 - 2026-09-12

> The overlay follows the language the game is running in. Chinese clients read exactly what they read before; English clients (and every other language) now read English instead of Chinese. Live evidence: [localization-2026-09-12.md](history/localization-2026-09-12.md); release and Workshop upload: [release-v0.11.0_2026-09-12.md](history/release-v0.11.0_2026-09-12.md).

### Added

- The overlay, the in-game status text and the model-facing state payload now follow the game's language setting and switch with it mid-session, with no restart. Chinese is the source text, so a Chinese client is unchanged; every other language reads the English table. A string with no English entry falls back to Chinese rather than going blank, so a half-translated build stays readable.
- Card, relic, potion, orb and pet summary lines follow the language too: `{0}费` becomes `{0} Energy`, `(熔毁)` becomes `(Melted)`, and so on.
- The combat glossary keys and explains its terms in the running language, e.g. `Strength` / "Each point of Strength usually adds 1 damage per attack."

### Fixed

- The glossary recognised only the Chinese keyword spellings, so on an English client card text matched nothing and the glossary came back empty. English spellings now match as well.
- Failure classification read Chinese wording only (`失败` / `请先` / `找不到`, `等待你`, `请求模型`, 认证失败 / 配置错误 / 超时), so on an English client a failed co-op invite, a teammate map wait and a configuration error were all misread as success or as a different kind of stop. These now key off explicit flags and English candidates.
- Text resolved while a field or a computed-once property is initialized used to freeze in whatever language was active at construction, so the overlay kept showing Chinese after the player switched the game to English. Idle wording is resolved on read now.

### Changed

- The Steam Workshop listing says the UI follows the game language (uploaded 2026-09-12, manifest `3781676487912021003`).

## v0.10.7 - 2026-09-12

> Distributed to the Steam Workshop on 2026-09-12 (item 3796486050, public, file_size 1088228). The in-tree version, the uploaded Workshop content and the build all come from commit `f9330ba`; no GitHub tag or release exists for this version yet. Live evidence: [validation-acceptance_2026-09-11.md](history/validation-acceptance_2026-09-11.md).

### Fixed

- Card-grid selection panels (upgrade / transform / enchant) now report their real selection metadata instead of `1/1/0`, and the first pick settles instead of burning the 10-second timeout (`d77982a`, #82). Measured on an isolated copy: enchant `1/1/0` → `0/3/0`, first pick 10036 ms → 151 ms; transform 36 ms then 187 ms; upgrade 173 ms with no regression.
- Proactive chat's six-message allowance is handed back when auto-play starts, so the feature no longer goes permanently silent after six lines; the 75-second interval still spans sessions.
- Starting auto-play after a run that began while auto-play was paused no longer stops instantly as a run-identity change: the run boundary is reset per auto-play session.
- Leaving the current run now stops auto-play again. The loop re-armed the run boundary at the top of every iteration and then checked it, so the "already entered a run" flag was always false and the stop never fired; the boundary is installed once per session now.
- `POST /action` and `POST /session/control` answer malformed or oversized bodies with 400 `invalid_request`. Both used to surface `JsonException` as a 500 `internal_error`, and `/action` had no body size limit at all.
- A failed decision is no longer reported as a budget stop. The retry hint appends the game's own error text, and the classifier matched bare words such as 上限 anywhere in that text, so the overlay advised resetting session stats; budget stops now carry their kind explicitly.
- The overlay's "teammate is acting" view is reachable again: a still-running session always matched the requesting-model branch first, so the player only ever saw "requesting the model", and a solo session showed "you can invite a teammate" while auto-play was running.
- Writing settings from overlay toggles no longer throws into the UI callback when the file is locked or the disk is full; the failure is reported in the status line instead.

### Added

- Combat state reports the player's own pets (`pets[]`, `pet_missing`) in both the full and compact payloads (`7b02168`).
- `docs/api.md` documents `POST /session/control`, `GET /events/stream`, `POST /companion/control` and `POST /companion/message`, plus the `/health` fields and the `stop_kind` value table.
- `docs/api.md` also documents `GET /data/{collection}` and `POST /mcp`, and the error table now lists `local_only`, `companion_session_required`, `companion_not_ready`, `invite_failed`, `collection_not_found`, `export_error` and `origin_not_allowed`.
- The MCP client waits 75 seconds for an action instead of 30, so `continue_game_over` (up to 60 seconds of native saving) no longer looks like a lost response; a refused connection is reported as a retryable `connection_error` rather than an unknown outcome, and a `status="failed"` action is no longer returned as a success.

### Changed

- The status page and `docs/proactive-chat-review.md` no longer claim the proactive chat is unverified in-game, and the historical mechanic matrix flags the deck-selection row that #82 later contradicted.
- The `act` tool text describes the compact view's own target fields; it used to tell the model to read `requires_target` / `target_index_space` / `valid_target_indices`, which only the full state and `rest.options` carry.
- The `full` profile now registers a legacy tool for every mod action, not 45 of 55: `switch_profile`, `dismiss_game_over_wait`, `confirm_unlock`, `close_cards_view`, `host_multiplayer_lobby`, `join_multiplayer_lobby`, `ready_multiplayer_lobby`, `disconnect_multiplayer_lobby` and `invite_ai_teammate` were reachable only through `act`. `run_console_command` stays debug-gated, and a test now holds the coverage.
- The single-step button records its turn against the session budget. It only advanced the display counter, so the guard never reached its limit and repeated steps could pass the configured request cap; reaching it now stops with the budget kind and a visible message.

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
