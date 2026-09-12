# Live validation checklist

Items that deterministic offline tests cannot settle: they need the game running with the mod
deployed. Collecting them in one place keeps "we proved it offline" from being read as "we saw it
work".

A session that walks this list needs the game installed, the mod built and deployed
(`scripts/build-mod.ps1 -Configuration Release`), and `scripts/start-game-session.ps1
-EnableDebugActions` so the debug console suites can run.

Legend: **[mod]** Mod API online only · **[combat]** needs a fight · **[room]** needs a specific
screen · **[coop]** needs two instances · **[eye]** needs a human or model to watch behaviour.

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
