---
name: sts2-mcp-player
description: Play or validate Slay the Spire 2 through the local sts2-ai-agent MCP tools, including access to live game metadata from the Mod API (cards/relics/monsters/etc.) for richer decision-making. Use when navigating the main menu, timeline, map, combat, rewards, shops, rest sites, events, chests, card selections, potions, or debug-enabled test flows, and when another agent needs a strict state-first workflow that avoids stale indexes, invalid room actions, and tool-profile confusion.
---

# STS2 MCP Player

Use this skill when driving the STS2 MCP mod as a gameplay agent or when validating the live MCP contract against the running game.

## Connection

Use the STS2 tools already exposed by the host. Before external play, use `health_check` to check
the connection and whether in-game auto-play owns the character. For local-first server names and
optional host configuration, see [references/remote-connection.md](references/remote-connection.md).

## Two ways to play

Mode A: you act. Start with `decide` when you do not already have fresh state; use a current action or
wait state for the next decision. A single combat `act` can carry a `then` plan when its schema
offers it. Follow the shared contract below and respect host limits on autonomous loops.

Mode B: the mod acts, you plan. Start/pause the mod through the local `POST /session/control` endpoint and use `get_planner_briefing` / `update_play_strategy` for strategic guidance. Do not issue competing direct actions while mod auto-play owns the character.

The in-game overlay agent loads the shared play contract below plus the screen-playbooks.md section for the screen it is on as its system prompt. Follow those same documents. Do not invent a parallel workflow.

<!-- BEGIN SHARED PLAY CONTRACT -->

## Quick Start

1. **Use the state you have.** The in-game loop already supplies fresh compact state and the current
   screen's playbook and strategy. Use only advertised tools: external reads and connection checks
   are absent in-game. If an essential fact is unavailable, wait or stop instead of guessing.
2. **External MCP:** decision snapshots include `state`, action descriptors and `scene_guidance`
   from one frame on current mods (the sidecar falls back to separate reads on older mods). A screen
   change alone needs no state read; get guidance only if needed.
3. **Act and continue:** choose from the latest `available_actions` and give a one-sentence `reason`.
   External clients use `act`'s `state`, or a current `final_state` after a `then` plan, for the next
   decision. Check `plan.stop_reason` and every `plan.steps[].status` first; if any indicates an
   unknown outcome, get fresh state before deciding. For transitions or `pending` needing settlement, use `wait_until_actionable` and its
   returned `state`; check `actionable`. A new choice never justifies blindly repeating the prior act.
4. **Plan only known steps:** when `act` offers `then`, combat may include up to nine follow-ups;
   name cards by `card_cid` when available. Stop the plan before an unknown draw or choice. Each
   follow-up checks fresh state; divergence, pending, refusal or unknown outcome stops the plan.
5. **Keep indexes aligned:** route by `state.session` (`mode` and `phase`); control only the local
   player. Take hand, target, node, reward and selection indexes from the latest payload.
6. **Recover context only when needed:** session deduplication marks unchanged deck, relic
   descriptions, ascension effects, glossary and decision guidance with `*_unchanged` /
   `glossary_delta`; changes are sent again. Restore lost context before relying on those blocks.

Do not trust memory over the current payload. The game mutates screens in place, overlays replace rooms, and some actions complete only after a follow-up state transition.

## Game Data Priority Rules

- First use the current payload's descriptions, `glossary`, and metadata already known this run. Do
  not guess a decision-critical mechanic from memory: query game data only to fill a real gap.
- Use `get_game_data_item` for one entity or `get_game_data_items` for a comparison. Use
  `get_relevant_game_data` when broader scene context is needed; without `item_ids` it derives ids
  for the current screen. Compact state supplies ids for hands, enemies, deck, relics, selections,
  rewards, shop stock, bundles and chests.
- Live state controls legality, targeting and current damage; metadata explains static mechanics,
  not live numbers. If they disagree, trust the live state for the current action and refresh only
  if the state is stale or the discrepancy prevents a safe decision.

## Non-Negotiable State Rules

- Treat `UNKNOWN` as transient only once. Obtain one fresh state (the in-game loop supplies one on
  the next decision); if it persists, stop guessing and inspect the payload.
- Treat `state.session` as the source of truth: `mode` is singleplayer or multiplayer, and `phase`
  is menu, character_select, multiplayer_lobby or run. Never infer mode from a screen or tool name.
- Resolve overlays before room flow. `MODAL`, `CARD_SELECTION`, reward-card overlays, and timeline overlays take priority over map or combat planning; `modal.underlying_screen` names the room underneath.
- Treat `pending` responses as an instruction to stay inside the returned screen flow; wait or
  resolve the newly exposed choice, never blindly resend the previous action.
- In combat, `combat.action_readiness` shares its gate with `available_actions`:
  `can_use_combat_actions = true` carries `play_card` / `end_turn`. Otherwise `reason` names the
  blocker. `modal_open` means clear the modal first;
  `hand_in_card_play`, `action_queue_unsettled`, `game_action_running`, `not_player_action_phase`
  and `snapshot_stabilizing` all mean wait with `wait_until_actionable`; `combat_paused` means a
  person paused the run, so wait. A missing `play_card` on `COMBAT` is not a lost turn: don't spam
  same-frame reads or switch to an unrelated action.
- Treat `proceed` as a room action, not a universal fallback.

## Action Failure Rules

- Read a failed action or game-data call's `error.code`: `invalid_request`, `invalid_action` and
  `invalid_target` require a corrected action or indexes from fresh state, not the same call again.
- `outcome_unknown` means an action may have executed. Never automatically replay it; inspect any
  `reconciliation.state` (which does not prove the action's effect), then decide from fresh state.
- Check `plan.stop_reason` and every `plan.steps[].status` even if the first action completed: a
  sidecar can report `rejected` while a step has `outcome_unknown`. If either indicates an unknown
  outcome, `final_state` may predate that step; obtain fresh state before choosing again and never replay it.
- Retry only when `error.retryable` is `true` (for example `state_unavailable` while a transition is still settling), after checking fresh state. When it is false, change your approach rather than retrying the same call.
- `error.status_code` mirrors the API status when present; `internal_error` is unexpected, not a legal move to retry.
- The three game-data tools answer with the same envelope: `collection_not_found` (a bad name, so fix the call),
  `game_data_unavailable` (the mod could not be reached, so `retryable` tells you whether to wait), and
  `invalid_game_data` (the payload shape was wrong; do not retry).

## Screen Routing

- `MAIN_MENU`: prefer `continue_run`; if unavailable, finish timeline gates (slot obtained epochs, confirm unlock overlays) or start a run from `open_character_select`. Character unlocks happen on the timeline, not on `GAME_OVER`. Do not call `switch_profile` unless asked; `option_index` is the native profile id 1..3.
- `CHARACTER_SELECT`: choose an unlocked character, wait for `character_select.embark = true`, then `embark`.
- `MULTIPLAYER_LOBBY`: stay on the same compact tool surface; use `available_actions` for `host_multiplayer_lobby`, `join_multiplayer_lobby`, `select_character`, `ready_multiplayer_lobby`, or `disconnect_multiplayer_lobby`. Once the local player is ready, `select_character` disappears and `unready` takes its place -- re-picking a character means unreadying first.
- `MAP`: use `choose_map_node` with `map.options[].i`. In multiplayer, if `map.local_vote` is set, `wait_until_actionable` instead of voting again; if `map.votes` exist and you have not voted, follow that option.
- `COMBAT`: stay inside combat actions unless a selection overlay interrupts. Budget block and pick targets from `combat.enemies[].intents[]` (`damage` / `hits` / `total_damage`) and both sides' `powers` lines, not from `lethal_risks` alone.
- `REWARD`: prefer `collect_rewards_and_proceed` unless making deliberate reward choices.
- `CARD_SELECTION`: finish the selection with `select_deck_card` and, when exposed, `confirm_selection`.
- `SHOP`: `open_shop_inventory` first, then buy/remove actions, then `close_shop_inventory`, then `proceed`.
- `REST`: use `choose_rest_option`; if selection opens, resolve it before `proceed`.
- `CHEST`: `open_chest -> choose_treasure_relic -> proceed`.
- `BUNDLE_SELECTION`: `choose_bundle` then `confirm_bundle` when exposed.
- `CAPSTONE_SELECTION`: `choose_capstone_option` from `capstone.options[].i`.
- `EVENT`: use `choose_event_option` even after combat returns to a finished event.
- `CRYSTAL_SPHERE`: `crystal_clear_cell` until divinations are spent, then `proceed`.
- `GAME_OVER`: `continue_game_over` first. Wait while `game_over.phase=summary_animating`. Use `return_to_main_menu` only when it is exposed.
- `UNLOCK`: use `confirm_unlock` until the screen closes, deciding each time from fresh state; never blindly replay a pending or unknown result, or bypass it with a menu-return action. The compact top level mirrors the raw block under `unlock` (`unlock_type`, `items`).
- `FAKE_MERCHANT`: the Fake Merchant event screen. `open_shop_inventory` opens its inventory and `proceed` leaves the screen.
- `PATCH_NOTES`: patch notes shown from the main menu. `close_main_menu_submenu` closes it.
- `CARD_LIBRARY`: the deck / compendium card viewer. `close_main_menu_submenu` returns to the previous screen, both from the main menu and from inside a run.
- `CARD_PILE`: a combat card pile (draw / discard / exhaust). `close_cards_view` returns to combat.
- `CARD_INSPECT` / `RELIC_INSPECT`: inspect overlays. The same `close_cards_view` action that closes the card list also closes these.
- `FEEDBACK`: the feedback form. No mod action closes it yet, so only open it when the task asks and let the player finish it.
- `PAUSE_MENU`, `SETTINGS`, `COMPENDIUM`, `RELIC_COLLECTION`, `POTION_LAB`, `BESTIARY`, `STATS`, `RUN_HISTORY` (and `CARD_LIBRARY` when it is opened from inside a run): the pages shown over a frozen run when a person presses pause. Room actions are suppressed and `capstone` is null on all of them; `close_main_menu_submenu` is the only action advertised, and it steps back one page (`CARD_LIBRARY` -> `COMPENDIUM` -> `PAUSE_MENU`). The pause menu itself offers nothing - a person resumes that one, so wait instead of trying to act.


For detailed per-screen sequences and pitfalls, read the section for the current screen in [references/screen-playbooks.md](references/screen-playbooks.md). The in-game loop is handed that one section with the state; an external client reads the file whole.

## Common Pitfalls

- `skip_reward_cards` closes the overlay but may leave the underlying reward item claimable.
- Multi-select overlays may require `confirm_selection`; do not assume one click is enough.
- Potion targeting depends on `target_type`; some potions need no `target_index`.
- Multiplayer targeting still controls only the local player. In the compact view the `target` hint (`enemy` / `player`) plus the `targets` index list tell you what `target_index` may address; the full state names the same fields `target_index_space` and `valid_target_indices`. Never assume teammate control.
- `shop.open = true` means inner inventory, not room completion (raw state spells it `shop.is_open`).
- Timeline gates can block run start until the overlay is confirmed or the submenu is closed.
- `return_to_main_menu` on `GAME_OVER` before `continue_game_over` skips score, unlock, and save.
- `UNLOCK` is not a card-selection screen; only `confirm_unlock`.
- After a multiplayer map vote, `map.local_vote` means wait, not pick a second node.

## Minimal Decision Heuristics

- In combat, spend energy efficiently and avoid ending turn with obvious free value unused.
- In rewards, take cards only when the upgrade is clear; otherwise skip.
- In shops, check relics and removal before committing all gold.
- In events, prefer unlocked options and use the returned fresh state after every branch.

<!-- END SHARED PLAY CONTRACT -->

## Reading a Decision

Both MCP surfaces expose the same tool face, and the read half of a decision can be one call:

- When fresh state is missing, stale or lost, call `decide`. For a needed field absent from compact
  state, call `get_raw_game_state`; after context compaction, use `get_game_state(full_state=true)`
  to restore lost unchanged static blocks.
- `decide` answers with `state` (the compact `agent_view`, same shape as `get_game_state`),
  `available_actions` (the descriptors from `get_available_actions`, with their `requires_index` /
  `requires_target` / target hints), and `scene_guidance` (the same object `get_scene_guidance`
  returns). On current mods `decide` reads one snapshot, so the actions belong to the state beside
  them rather than to a frame the game may already have left. The sidecar falls back to separate
  reads for older mods without that snapshot route.
- `scene_guidance` always carries `screen`, `scene`, `guidance`, and `playbook`. `guidance` is empty
  on a screen with no strategic choice; `playbook` is the per-screen action sequence and is never
  empty (an unmapped screen gets the index of the sections). The Python sidecar adds `event_id` /
  `event_options` / `guidance_source` from the offline event index, which the mod does not ship.

## Reading an Action Result

- When `act` returns a current state, on both surfaces `state` is the compact `agent_view`: the same
  shape `get_game_state` returns. Check `status` and `stable` before using it as the next decision's
  input. `action`, `status`, `stable`, and `message` are unchanged.
- For a non-empty `then` plan, inspect `plan.stop_reason` and every `plan.steps[].status` before
  using a present, current `final_state`; earlier completed steps remain completed if the plan stops.
  A sidecar may report `rejected` while a step says `outcome_unknown`. If either indicates an unknown
  outcome, `final_state` may predate it; obtain fresh state before choosing again. An empty `then` uses
  the ordinary single-action response.
- `raw_state=true` returns the full `/state` payload instead. Use it only to inspect a field the
  compact view does not carry: it is roughly 4,000-9,500 tokens per action.
- A rejected index is correctable rather than merely refused. The error object carries
  `error.details.field` (the index you got wrong), `error.details.submitted` (what you sent),
  `error.details.valid_indices` (the indexes the payload accepts right now), and
  `error.details.valid_field` (the payload path they came from: `combat.hand`, `map.options`,
  `combat.hand[].targets`, ...). Fix the call from those instead of resending the same value.
- An action that is not in `available_actions` answers with the code `invalid_action` and the current
  `available_actions` list in its details.
- `status: "outcome_unknown"` means the action request may have completed but its response was lost.
  One `/state` reconciliation was attempted and the state it read is under `reconciliation.state`
  (compact, marked); `reconciliation.state_read` / `action_effect_compared` (always `false`) /
  `action_outcome` (always `"unknown"`) say that the state was read but the action was never compared
  against it, and `succeeded` / `status` describe that read rather than the action. **Never replay the
  action automatically**: if `reconciliation.state_read` succeeded and the state is still fresh, use
  it to decide again; otherwise obtain fresh state first.
- `wait_until_actionable` answers with the same compact `state` as `get_game_state`, so a wait across an
  animation costs a decision-sized payload rather than the full one. `raw_state=true` is its escape
  hatch, exactly as on `act`.

## Run Decision Logs

- Create one markdown log per played or continued run under `agent_knowledge/run_logs/`.
- Use `YYYYMMDD-HHMM_<character>_<seed>.md` when possible; use `unknown-character` or `unknown-seed` until the state exposes the missing value.
- Record route choices, card/relic/potion rewards, shop buys/removes, event branches, rest choices, key combat turns, potion use, lethal planning, and MCP/action anomalies.
- Keep entries short so logging does not block play. If needed, execute the legal action first and flush the note after the returned state stabilizes.
- Use the template in `agent_knowledge/run_logs/README.md` for the header and decision table.

## Choose the Right Tool Surface

- Use the guided profile for normal play and most evaluations.
- Keep `get_relevant_game_data` / `get_game_data_item` / `get_game_data_items` available in guided runs for card, monster, potion, shop, and event decisions.
- The Python sidecar also provides `wait_for_event` for event monitoring; ordinary actionability
  waits use `wait_until_actionable`.
- Prefer `decide` when a step needs state and guidance together; it is the same answers as the individual read tools, from one call.
- Use legacy per-action tools only when a harness explicitly needs tool-by-tool coverage.
- Use `run_console_command` and `inject_event_churn` only in development flows where debug actions are enabled.
- Check who is playing before you act. `health_check` reports `play_running`: when it is `true`, the
  in-game auto-play loop owns the turns, and the mod does not stop an external `act` from racing it.
  Either ask the player to pause auto-play first, or do not act at all and steer it as the planner
  (see "Planning for the In-Game Loop" below).

Both MCP surfaces (the mod's native `/mcp` and this Python sidecar) expose the same tool names and
arguments. `get_scene_guidance` and `decide.scene_guidance` answer the same four keys on both:
`screen`, `scene`, `guidance` (empty where the screen has no strategic choice) and `playbook` (never
empty). The sidecar adds `event_id` / `event_options` / `guidance_source` from the offline index in
`docs/game-knowledge/events.md`, which the mod does not ship.

## Planning for the In-Game Loop

In dual-layer mode the in-game loop splits the work: the fast Jev execution model picks each concrete
action, and an LLM planner only sets the standing strategy Jev follows. The mod runs its own planner,
and an external agent can take that role through two tools instead of playing moves itself.

- Read first: `get_planner_briefing` answers the current `strategy`, whether `dual_layer` is on for this
  instance and `jev_configured`, the `run_summary` and `screen`, up to five `recent_jev_decisions`
  of this run, and a `confidence_trend`. A strategy only matters when `dual_layer` and
  `jev_configured` are both true.
- Write with `update_play_strategy`. Every field is optional and **an omitted field keeps its current
  value**, so a posture nudge does not erase the plan:
  - `goal`: one sentence naming the macro objective for this screen or act ("kill the weakest enemy
    before it buffs", "hold potions for the elite"). It leads every Jev choice. Capped at 400 characters.
  - `posture`: `aggressive`, `defensive`, or `balanced`.
  - `instructions`: one or two sentences of standing guidance.
  - `option_hints`: nudges keyed by **action kind** -- `play_card`, `end_turn`, `use_potion`,
    `choose_map_node`, `choose_reward_card`, ... The mod re-keys them onto the options the current
    frame actually offers (`play_card:0->1`). A key naming a card, target, or index is dropped, because
    indexes change every frame. A map you send replaces the previous map whole.
- `plan_screen` / `plan_round` in the strategy are read-only provenance from the in-game planner (which
  screen and combat round a plan was written for). Rewriting `goal`, `instructions`, or `option_hints`
  clears them; your strategy then applies until you or the in-game planner write again.
- Refresh on evidence, not on a timer: a new act, a new kind of encounter, or a `confidence_trend`
  that is low or `falling`. Repeated `end_turn` rows with distinct turns are normal play, not a stuck
  loop; judge them against the hand and energy in the state, not against the count.
- Do not `act` while planning for a running loop (see "Choose the Right Tool Surface").

For validation flows, read [references/debug-and-validation.md](references/debug-and-validation.md).

For the choices the shared contract does not make for you — which node to enter, when to heal instead of smith, what to buy, when to drink a potion, which enemy to kill first, and how a host and a teammate divide the work — read [references/strategy.md](references/strategy.md). Every rule there names the payload field it reads. Read the file whole; the in-game loop does not, and cannot, because it has no file access — the mod injects one section of it per screen instead (see the note at the top of that file).

