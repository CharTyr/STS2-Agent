# Changelog

> Release attribution is recorded against tags or release commits. Post-tag maintenance is listed separately; current validation limits are maintained in [PRODUCT_PLAN_CURRENT.md](https://github.com/CharTyr/STS2-Agent/blob/main/PRODUCT_PLAN_CURRENT.md).

## Unreleased

### Added

- **Agent actions can carry a player-facing rationale.** The shared `act` schema now accepts an optional one-sentence `reason`; tool-calling and JSON-fallback models both feed it into the existing `AgentTurnResult.Reasoning` / overlay thought path, so non-reasoning models no longer leave the player with only an opaque action. The Python and native MCP schemas stay aligned, and the Python sidecar preserves the reason in `client_context.decision_reason` for the upcoming decision log.

- **An offline contract for vision (image) requests.** `OpenAI.VisionDataUrl` pins that a user message carrying a JPEG is serialized as the two-part `image_url` data-URL content array providers expect (text part first, base64 payload decoding back to the exact bytes), and `OpenAI.VisionPlainContent` pins that messages without an image keep plain string content. This closes the last `Partial` row on the model-compatibility matrix's request surface; sampling against a real multimodal endpoint remains open.

### Fixed

- **The action-descriptor `requires_target` contract is now written down.** A 2,346-sample live pass had found the flag `false` on every descriptor and could not tell a dead branch from design. It is design: no action unconditionally takes `target_index`; the three that take it conditionally (`play_card`, `use_potion`, multiplayer rest options) advertise it per item on the hand card, potion, or rest option. `docs/api.md`'s descriptor table now states that rule generally instead of carving out `play_card` alone, and `ActionSurface.DescriptorTargetIsDocumentedConstant` pins the walk so the constant cannot drift silently.

## v0.13.1 - 2026-09-20

> Event-stream reliability: the SSE poller is demand-driven, a full subscriber queue now
> disconnects the slow client instead of silently discarding events, shutdown can no longer
> resurrect event state, and a debug action can prove the overflow contract in a live game.
> Co-op hosts get companion-identity and per-role health-key fixes.

### Added

- **A debug action that can prove the slow-subscriber contract in a live game.** `/events/stream` closes a subscriber whose 256-slot queue fills instead of dropping the oldest event, but that path had no in-game evidence: the poll loop publishes only when a digest field really changes, so an idle client never falls behind, and the debug console that could have driven 256 changes answers 409 until a run exists. `POST /action {"action":"inject_event_churn","option_index":400}` (needs `STS2_ENABLE_DEBUG_ACTIONS=1`) publishes numbered synthetic `debug_churn` events through the same publishing path as every other event, so one unread stream plus one healthy stream is enough to watch the slow one get dropped. Counts below 257 are rejected on purpose — a request that cannot fill a queue proves nothing.

### Fixed

- Companion identity checks now accept both live compatibility states, `ready` and `degraded`, while still requiring the exact service, companion role, API port and process ID. The POSIX game launcher uses the same liveness contract, so a working companion with one missing reflection capability is no longer reported as replaced or offline.
- A companion instance no longer reports the host window's default dual-launch and teammate-control messages in `GET /health`. The existing host-only keys remain stable but are `null` when `instance_role` is `companion`; common per-process health, compatibility and state-build fields are unchanged.
- The SSE state poller is demand-driven: with no `/events/stream` subscribers it no longer builds a full state payload every 120 ms. One shared poll loop wakes for the first subscriber, remains active while any subscriber exists, becomes idle after the last leaves, and resumes on reconnect. The ordering contract is unchanged and now holds on both paths: a connection that already has a snapshot gets `stream_ready` immediately, and the first connection of a session gets `session_started` followed by `stream_ready`.
- Full SSE subscriber queues no longer discard old semantic events silently. A slow subscriber is explicitly disconnected without blocking event production or other subscribers; the Python client reconnects within its existing wait deadline and resynchronizes from the next stream snapshot. Event IDs remain monotonic and make a discontinuity observable.
- Shutdown cannot resurrect event state. The poll loop is stopped before the subscriber registry is cleared and every in-flight sample is fenced by its lifecycle generation, so a sample that was already on the game thread when shutdown started is discarded instead of being replayed to whoever connects next. A poll that does not honour cancellation inside the shutdown budget leaves the coordinator stopped rather than letting the next `Start` run a second loop beside it.
- An unchanged state no longer re-sends its event. The first live run of the demand-driven loop showed a client on a stationary screen receiving one `stream_ready` frame per 120 ms poll; the poll now records the snapshot for the next subscriber and only publishes an event whose payload differs from the previous one. The live run also confirmed the intended lifecycle: zero subscribers built nothing over 12 s, the first subscriber produced `session_started` then `stream_ready`, and reconnect produced `stream_ready` alone.

> Live-verified 2026-09-20 on an isolated profile against game v0.111.0: `/health` `ready` with 27/27
> reflected members, `mod-load --deep-check`, `state-summary` and `state-invariants` all clean, and the
> SSE lifecycle measured end to end (0 builds with no subscriber; `session_started` then `stream_ready`
> for the first subscriber; reconnect gets `stream_ready`; no repeated frames; polling stopped after the
> last client left). The degraded companion was verified with a real `degraded` payload too: the
> companion kept serving, its host-only health keys came back `null`, and the shipped identity check
> accepted that payload while still rejecting every wrong-identity and malformed variant (14/14). The
> slow-subscriber overflow was then observed in the game too, through the debug churn action below:
> the server logged `Disconnected 2 slow event subscriber(s)` instead of dropping events silently.

## v0.13.0 - 2026-09-19

> Mostly fixes to what `/state` tells an agent, found by checking every name the mod looked up
> against the game that is actually installed. Relic counters, card keywords and enchantments,
> monster move lists and a resumed combat's `end_turn` / `play_card` had been silently empty or
> missing. Several buttons were being "clicked" through a signal the game's buttons do not have.
>
> `GET /health` gains two blocks. `compatibility` probes every game member the mod reaches by
> reflection and can report `status: "degraded"` where it used to say `ready`. `state_build` reports
> how long state builds take. A client that treats any `status` other than `ready` as failure should
> read `compatibility` first: the mod still works, it is saying which feature will not.
>
> The two largest source files were split and the two action surfaces merged into one decision, with
> no behaviour change. Saves, cards and numbers are untouched.

### Changed

- **The two action surfaces are one decision again.** `GET /state`'s `available_actions` and
  `GET /actions/available`'s descriptors were two hand-written implementations of the same
  question -- 301 and 609 lines consulting the same 50 `Can*` predicates to emit the same 55 action
  names -- so every new action had to be added twice and nothing but a contract test noticed when
  only one was updated. Both now report a single `EnumerateAvailableActions` walk: the name list is
  a 12-line projection of it and the descriptor endpoint a 14-line wrapper.
  `GameStateService.cs` drops from 8,559 to 8,295 lines. See ADR 0001.

  Verified by replaying the live baseline on the refactored build: 45 back-to-back samples across
  all twelve screens the baseline covered, **zero disagreements between the surfaces**, and twelve
  action sets matching the baseline exactly -- including `PAUSE_MENU`'s empty set, where both
  surfaces return an empty array. The one difference, a missing `discard_potion` on
  `CARD_SELECTION`, was traced to the run holding no potions rather than to the change.

  Emission order now follows the descriptor surface, so `crystal_*` appears in a different position
  in `available_actions`. The set is unchanged and no client depends on the order: the play skill and
  `state-invariants` both test membership.

- `ActionSurface.*` now pins that neither surface decides for itself -- neither may name an action or
  consult a `Can*` predicate of its own -- which is a stronger promise than the old "two
  implementations agree". `AGENTS.md` and the game-actions spec describe adding an action in one
  place instead of two.

- **The two biggest files came apart, and nothing they do changed.** `GameStateService.cs` was 8,295
  lines and `GameActionService.cs` 7,061 -- together half the mod. The state service gave up the
  compact `agent_view` rewrite and the 60 payload type declarations; the action service became one
  file per room (combat, rewards, rooms, shop, menus, embark, run, co-op). Each is the same
  `partial` class, so no call site changed and no behaviour moved: the base files' diffs carry
  exactly one genuinely new line each, the `partial` keyword.

  Which member went where was computed rather than chosen -- a member joins a group only when every
  reference to it comes from inside that group -- so 5,697 of the action service's 6,756 member
  lines landed in exactly one room and the 1,059 that two rooms both reach stayed put. The largest
  file in the mod is now 5,872 lines instead of 8,295, and six of the eight room files fit the
  1,000-line default budget with no entry at all.

  On the Python side `client.py`'s 58 per-action wrappers moved to `client_actions.py` as a mixin.
  Both halves now fit the default budget, so neither has a budget entry any more.


- **The MCP server's game-data half moved out.** Loading a collection, caching it, indexing it by id,
  deriving which ids the current screen makes relevant and projecting requested fields is one job;
  registering MCP tools is another. `game_data.py` holds the first, `server.py` drops 1,103 -> 707
  lines, and its budget comes down 1,150 -> 750.

- **`AgentOverlayHost.cs` will not be split, and the architecture page now says why.** At 1,829 lines
  it reads like an oversight next to the two files that did come apart. It was measured instead:
  78 of its 85 instance fields are touched by more than one method, so partials would scatter shared
  mutable state to make the files smaller.

### Added

- **`GET /health` reports how long state builds take (`state_build`).** Every `/state`, action
  response and SSE refresh builds the payload on the game thread, where the game draws no frame until
  it finishes. The request log timed whole requests, queueing included, at Info with no threshold, so a
  build that froze the game for a second looked like any other line. Builds are now timed alone; one
  over 100 ms -- six frames at 60 fps -- is logged as a warning (at most one per 30 s, naming how many
  it held back), and `/health` carries the count, the maximum and its screen, and recent p50/p95.

- **`GET /health` says whether the mod can still read the game.** This mod references the game's
  `sts2.dll`, so almost everything it touches is compile-checked -- but twenty-three private game
  members are found by name at runtime, and those fail quietly: `GetField` returns null, the call
  site falls back to a default, and an agent is handed `max_players: 0` with no way to tell that
  from a lobby that really holds nobody. Slay the Spire 2 is in early access, so that is a patch
  away at any time.

  All twenty-three are now resolved when the mod loads, and the misses are written to the game log --
  the file a player actually attaches to a bug report. `/health` gains a `compatibility` block naming
  any that are missing and the feature each one costs, and `status` is derived from it instead of
  being the literal `"ready"` it has always been -- the one field on the endpoint that could never
  be wrong and never be useful.

- A contract for **`abandon_run`**, the only action that destroys a player's run and one of twelve
  that had nothing asserting what they do. What it pins is where the handler *stops*: it opens the
  confirmation modal and waits, and the run survives until something else answers that modal. A
  later simplification that confirmed the modal here would look like removing a redundant round
  trip and would turn one call into a destroyed save.


- **Two offline gates, bringing the set to eleven.** `arch-facts` checks the architecture
  specification's file table against the files: every listed file exists, each stated line count is
  within 5% of the real one, the totals hold, and **every source file over 1,000 lines appears in
  the table** -- so a monolith cannot exist without the page that exists to name it saying so.
  `doc-links` requires every relative Markdown link in a tracked page to resolve; 403 pages carry
  411 of them and nothing checked any outside the three documents inside the release artifact.

  Both gates ask git what the repository contains, not the filesystem -- see below for why that
  distinction cost a CI run to learn.


- **The three contract surfaces besides the payload are checked.** `docs/api.md` tells a client
  four things it branches on: which routes it may call, which payload fields it reads, which error
  codes it must handle, and which event types it can wait for. Only the payload half was checked,
  and three error codes had already slipped out of the table -- `listener_error` (500),
  `method_not_allowed` (405) and `payload_too_large` (413), each one a real response an agent can
  receive with nothing to look up. All three are documented now, and `api-facts` compares all three
  surfaces in both directions, with the HTTP status included: a code the mod never sends is a branch
  nobody can take, and a row with the right name and the wrong number is worse than no row.

- `doc-links` now checks that a `#L<n>` anchor names a line its file still has. Six anchors in the
  MCP specs had gone stale when `client.py` and `server.py` were split -- a line anchor rots as soon
  as the file is edited, and it rots quietly, because the link still opens.

### Fixed

- **A resumed combat could hide `end_turn` and `play_card` forever** (#151, by @XenoAmess). With an
  empty hand, turn readiness required a recorded card play this turn, and that counter can be reset to
  zero by the round transition after card effects have already emptied the hand -- so an agent polled
  indefinitely. When the turn has started, an enabled native End Turn button is now also accepted as
  evidence. The opening-draw guard is unchanged: the game enables that button only after the turn's
  setup, draw included, has finished.

- **`run.relics[].stack` was null for every relic.** It read `RelicModel.Amount`, which the game does
  not have. It is now the counter the relic shows (`DisplayAmount`, when `ShowCounter`), and null for
  a relic that shows none.

- **Cards never reported their keywords or enchantment to the glossary.** The modifier tags were
  read through seven guessed member names; only `Keywords` existed, and its enum values came out as
  no text at all. Keywords (`Exhaust`, `Retain`, ...) and the enchantment now reach the card's
  glossary matches, so a card that exhausts is explained as one.

- **Piles, powers, potion rarity, event previews and descriptions are read by type.** They were
  read by guessed names on objects whose type was known; the values were right where the guess
  happened to exist, and the rest were dead fallbacks (`ActId`, `BossId`, `DrawDeck`, a private
  `Description`). A future rename now breaks the build instead of emptying a field.

- **`open_character_select` could answer 500.** On the main menu it pushed the character select
  screen without checking the submenu stack returned one; it now answers `503 state_unavailable`,
  retryable, like every other control that is not there yet. Found while giving the eleven handlers
  that had no behaviour contract one each (`HandlerContract.*`); `crystal_set_tool` now refuses by
  the same predicate the action surface offers it by instead of a copy of it.

- **`GET /data/monsters` exported `moves: []` for every monster.** The export read a public
  `MonsterModel.MoveNames` property by reflection; the game removed it, the lookup returned null, and
  the export emptied without a word. `MoveNames` had only ever wrapped a public localization query,
  so the export now calls that directly -- a future rename breaks the build rather than the data.

  The first live run of that fix found the next problem: the same key prefix also holds each move's
  dialogue, so nine monsters exported duplicate moves named with taunts -- `FAKE_MERCHANT_MONSTER`'s
  ENRAGE appeared three times. Only the `.title` key is exported now, which is exactly the key the
  game's own `GetBestiaryMoveName` builds.

  The second live run left three monsters still empty: the Decimillipede's segments share one set
  of text under a key that is not their own id. The prefix now comes from the monster's own title
  key, the same place the game looks, so all 107 monsters export their moves.

- **The compatibility probe no longer answers for code it does not run.** Its first version resolved
  its own copy of each member while the call sites resolved theirs, with their own binding flags, so
  the probe could say `ready` while a reader was broken -- reintroducing the `_longPressDuration` bug
  with a correct registry left every offline test and gate green. Call sites now ask the registry
  for the member, so there is one lookup and one set of flags. The contract that guards this also
  used to look only for `"_x"` literals; five method and property names went straight past it.
  `NEndTurnButton.CanTurnBeEnded`, whose private getter decides whether `end_turn` is ready, was
  one of them, and is now probed.

- **Four private methods the compatibility probe could not see.** `host_multiplayer_lobby`,
  `ready_multiplayer_lobby`, `disconnect_multiplayer_lobby` and `save_and_quit` called
  `NMultiplayerTest.StartHost` / `ReadyButtonPressed` / `Disconnect` and `NPauseMenu.CloseToMenu`
  through helpers that took the method name as a parameter, so the registry's scan -- which looked for
  names written into `GetMethod` calls -- never saw them. They are registered and probed now (27
  members), and a missing lobby handler answers `503 state_unavailable` instead of a 500.

- **Clicks that could not land.** The game's `NButton` is a `Control`, not a Godot `BaseButton`, so it
  has no `pressed` signal. `choose_capstone_option` only emitted that signal, and now clicks.
  `confirm_bundle`'s fallbacks called `OnConfirmPressed`, which the screen does not declare, and emitted
  the same missing signal; `continue_game_over` also emitted it, and set a `disabled` property the
  button does not have, before the `ForceClick` that did the work. The dead steps are removed. Godot
  calls by string (`Call("OpenMultiplayerSubmenu")` and three more) use compile-checked names now.
  The stuck-tutorial fallback's method list is cut to the one name that can match.

- **Four reflective reads that never resolved in the installed game.** `continue_game_over` tried
  three handler names on the button before emitting its pressed signal; none exists on a button
  (`OnPressed` is declared nowhere, the other two live on `NMainMenu`), and the signal turned out to
  be missing too -- see above. The game-over overlay tried
  two methods on `NGameOverScreen` that live on `NCombatRoom` and `NRewardsScreen`. All of them
  returned null on every call and sat in front of the code that did the real work, so nothing
  visible changes; the dead halves are removed rather than left looking like they do something.

- **`end_turn`'s long-press wait had been using a number the mod invented.** The game's
  `NEndTurnLongPressBar._longPressDuration` is a **static** field; the read asked for it with
  instance-only binding flags, so `GetField` returned null and the wait fell through to a
  hard-coded `0.45` against the game's real `0.5` -- 50 ms short, every time, since the line was
  written, with nothing anywhere saying so.

  Found by the compatibility probe on its **first live run**, which is the whole argument for the
  probe: offline it looked fine, because reading a member's name out of the assembly metadata does
  not tell you whether the binding flags can reach it. The registry now records staticness, and all
  22 entries were re-checked against the installed assembly -- this was the only one wrong.


- **Four checks were not checking what they said.** The first three were found by the splits above
  and predate them; the fourth was a defect in a gate added in this same batch:
  - `AgentSourceFixture.MethodBody` resolved a method name by its last occurrence in the source,
    which is correct only while a method is declared before it is called. Across a partial class it
    returned the body of whatever enclosed a *call site*, so a source contract kept asserting --
    against the wrong method, silently.
  - `GameTaskBoundingContractTests` scanned a hard-coded list of files for unbounded awaits, so the
    first bare `await` written in one of the new room files would have gone unseen. It enumerates
    the class's files now and refuses to run if it finds fewer than two.
  - The verification-gate self-test reported PASS for three cases that were failing with "missing
    required file". `Assert-Case` only checked for a non-zero exit, so a gate dying for an unrelated
    reason was indistinguishable from a gate correctly rejecting drifted input. Every case now
    declares the message it expects, and a case that declares none fails.
  - **`doc-links` and `arch-facts` shipped asking the filesystem.** Both passed locally and failed
    on CI, because a working tree holds files a clone does not: `AGENTS.md` is deliberately
    untracked and `extraction/decompiled/` is a local decompile, and 36 links pointed at them. The
    gates' verdict therefore depended on whose machine ran them -- which is, one level up, exactly
    the failure mode they were added to prevent. Both now ask git.

    The 36 links were real defects, not false positives: both documents already said in prose that
    the target does not exist in a fresh checkout. They are written as code spans now, because a
    Markdown link is a promise that clicking works.

- The architecture specification was stale: it carried pre-ADR-0001 measurements and still told
  readers to add each new action to **both** action surfaces, a month after that duplication was
  gone. Rewritten, and now held to the files by the `arch-facts` gate.

### Documentation

- **ADR 0002** records why the mod keeps two MCP tool surfaces -- the in-process native server that
  needs no Python, and the sidecar that provides tool profiles -- and what would change that answer.
  Adding an MCP tool means editing both sides, which is the opposite of the rule ADR 0001
  established for actions, so `AGENTS.md` now says so where the steps are.

## v0.12.5 - 2026-09-17

> Two fixes to what the mod reports about itself, both found by driving a running game rather than by
> reading the code, and both re-verified live on the patched build. `/state.screen` reports
> `GAME_OVER` again after a death, and a game action that fails now names the reason the game gave
> instead of one blank sentence shared by twelve different actions.
>
> Nothing changes for a run that is going well: no cards, no numbers, no save format.
>
> Packaging now writes a `build-fingerprint.json` beside each artifact — every file with its SHA256,
> the summed byte count Steam reports as `file_size`, and the source commit — so a build can be
> identified from a bug report without collecting those numbers by hand afterwards.

### Added

- `docs/api.md` documents the `/state` `combat` object's own fields for the first time.
  `action_readiness`, `players[]`, `end_turn_will_kill_player` and `lethal_risks[]` all shipped and
  all reach agents through the compact `agent_view`, and none of them were described anywhere a
  client could read. The new tables cover the three payload records and every `reason` code
  `action_readiness` can answer with, together with what each one means for an agent deciding
  whether to wait.
- The `api-facts` gate now pins those tables to the records that produce them: `CombatPayload`,
  `CombatActionReadinessPayload` and `CombatLethalRiskPayload` field-for-field, plus every reason
  code `EvaluateCombatActionGate` can emit. A field added to the payload without a documentation
  row, or a documented field the mod no longer sends, fails the gate by name. Three destructive
  cases in `scripts/test-verification-gates.ps1` prove it.
- `scripts/lib-build-fingerprint.ps1` writes a `build-fingerprint.json` next to every packaged
  artifact: each file with its byte count and SHA256, the summed byte count Steam reports as
  `file_size`, and the commit the tree was built from with a dirty flag. Republishing one version
  number means size and hash are the only way to tell builds apart, and until now those numbers
  were collected by hand after the upload. Both `package-release.ps1` and
  `package-steam-workshop.ps1` emit one.
- The play skill tells an agent what `combat.action_readiness` means: a `COMBAT` screen missing
  `play_card` is one of the gate's reasons, not a lost turn, and the reason says whether to clear a
  modal, wait, or leave a paused run alone. The in-game agent reads the same contract.
- `docs/api.md` now describes the whole `/state` surface. Seven sub-structures had no section at
  all -- `session`, `multiplayer`, `multiplayer_lobby`, `character_select`, `timeline`, `modal` and
  `game_over` -- and nine top-level fields were missing from its own field table. 91 fields across
  23 payload records were named nowhere a client could read, including three screens an agent has
  to drive and the `session` block the play skill tells agents to route on first.
- `docs/api.md` documents the compact `agent_view` renames. That view is what MCP `get_game_state`
  returns by default and it renames 43 keys (`can_embark` becomes `embark`, `enough_gold` becomes
  `affordable`, every `index` becomes `i`), so a client following the `/state` names reads
  `undefined` rather than an error. The mapping existed only in the builder methods.
- `GET /health` documents `service`, `api_host` and `api_port`, which had only ever appeared in the
  example JSON.
- Two contracts now guard the shape of the codebase itself, because nothing was counting. Two files
  hold 49% of the mod's C# (`GameStateService.cs` at 8,559 lines and `GameActionService.cs` at
  7,008, against 31,632 across 82 files), and neither got there by a decision.
  `SourceShapeContractTests` and `mcp_server/tests/test_source_shape.py` give every file a line
  budget -- named budgets for the four largest, a default for the rest -- and budgets only go down.
  A second check fails when a budget drifts far above the file it guards, so shrinking a file
  tightens its ratchet instead of leaving room to regrow.
- `ActionSurface.SameActionsOnBothSurfaces` and `ActionSurface.SamePredicatesOnBothSurfaces` pin the
  two action surfaces to each other. `GET /state`'s `available_actions` and
  `GET /actions/available`'s descriptors are one decision written twice -- 301 and 609 lines
  consulting the same 50 `Can*` predicates to emit the same 55 action names, verified field for
  field -- so adding an action to one alone now fails by the name of the action that was forgotten.
  The duplication itself is not removed: it decides what an agent is allowed to do, and rewriting it
  on offline evidence alone is how the 0.12.4 regression happened. ADR 0001 records the plan and the
  live-validation it waits for.
- `.trellis/spec/mod/architecture.md` gains a "Code shape and its known debts" section with the
  measurements, what distinguishes the two monoliths (one is repetition, one is three fused
  concerns), and where new code belongs.
- The `api-facts` gate grew to match: sixteen payload records checked field-for-field against their
  own table, every `GET /health` key against its table, the compact rename table against the
  renames the `BuildAgent*Payload` methods actually perform, and under all of it a coarse net
  requiring every serialized field of every `/state` payload record to be named somewhere in
  `docs/api.md`. Six destructive cases in the gate self-test cover the new paths.

### Fixed

- **A faulted game task now names the exception that faulted it.** Eleven actions answer 409 through
  `DescribeGameTaskFailure` -- save and quit, the crystal sphere, event proceed, rest options, three
  purchases, both lobby operations and `run_console_command` -- and every one of them said the same
  sentence for a request the game legitimately rejected and a request that broke the mod. A live pass
  hit it: `run_console_command room Treasure`, issued while already standing in a treasure room,
  answered `Console command failed: the game task faulted.` with nothing to act on, and the bounded
  retry loop in `run_sts2_validation.py` then repeated it twenty times. That handler's *synchronous*
  branch already named its exception -- the same dishonesty was fixed there once, for `bestiary` --
  and its asynchronous twin was missed. `remove_card_at_shop`, which reaches its 409 through the pure
  `BackgroundTaskOutcome` decision layer rather than that helper, gets the same detail from the same
  shared describer, so the twelfth path cannot drift from the other eleven.

- **`/state.screen` reports `GAME_OVER` again after a death.** Death leaves the combat room active,
  so `ResolveNonModalScreen`'s `FindActiveCombatRoom => "COMBAT"` guard claimed every game-over
  screen and its own `NGameOverScreen` switch arm was unreachable. A live pass caught eight samples
  whose only offered action was `continue_game_over` and all eight reported `COMBAT`; across 2,346
  samples the name `GAME_OVER` never appeared once. `docs/api.md` documents it, the play skill routes
  on it and `run_sts2_validation.py` branches on it, so an agent following the contract waited for a
  screen it would never see and recovered only through the action list. Re-verified live on the
  patched build: two independent deaths, 10 game-over samples, all reporting `GAME_OVER`, with
  `continue_game_over` and `return_to_main_menu` still settling the run and `save_verified` still
  reaching `true`. All 12 screens the baseline covered were re-sampled and none changed name.
  `ScreenResolution.GameOverBeforeCombatRoom` pins the ordering.

- `docs/api.md` said `shop.cards[]`, `shop.relics[]` and `shop.potions[]` carry an `available`
  field. None of those three records has ever had one -- only `shop.card_removal` does -- so an
  agent branching on it read `undefined` and could not tell a sold-out slot from an affordable one.
  The three tables now document `is_stocked` and `enough_gold`, and say which one answers "can I
  buy this".
- The `run` field table was split in two by a stray blank line, so `ascension` and
  `ascension_effects[]` rendered as a separate headerless table.
- `AGENTS.md`'s "add a new action" walkthrough sent readers to `BuildAvailableActionDescriptors`,
  a method that does not exist. The real one is `BuildAvailableActionsPayload`.
- The verification gates crashed instead of reporting on a console that is not UTF-8. Gate messages
  quote the Chinese section headings of `docs/api.md`, and the Windows CI runner's stdout is cp1252,
  so printing one raised `UnicodeEncodeError`: a gate that **passed** still exited 1, and a gate that
  failed would have had its real message replaced by an encoding traceback. The gates now write
  UTF-8 with a replacing error handler, and the self-test runs the suite under `PYTHONIOENCODING=cp1252`.
- The gate's C# property extractor missed identifiers escaped with `@`, so `public EventPayload?
  @event` read as "the docs list a field the code does not have".
- A source contract now pins the in-combat guard around the action-queue read in
  `EvaluateCombatActionGate` (`CombatGate.QueueReadIsCombatOnly`). That guard is what the second
  v0.12.4 re-cut had to add: reading `RunManager.ActionExecutor` / `ActionQueueSet` outside a fight
  failed every `/state` request with a `NullReferenceException`, and 408 offline tests and nine
  gates all passed that build. The contract also keeps the gate the only place in the state builder
  that touches those two members, so the same failure cannot return through a second call site.

## v0.12.4 - 2026-09-15

> Distributed to the Steam Workshop on 2026-09-15 (item 3796486050, public, `file_size` 1233413 equal to the local
> content bytes, `time_updated` 19:38:00), from commit `4b8e7c5`. The GitHub tag and release for `v0.12.4` followed
> on 2026-09-16, pointing at the `dev -> main` merge `3a4ec95` (PR #139) that carries both re-cuts below;
> the release asset is 557906 bytes, SHA256 `AD970DB1204A0DC37602A2751935E87E5B87FC502B21B00E47B2699C4A98EC57`.
>
> **The same version number was rebuilt twice.** The first build could contradict itself inside one
> `/state` response: its in-combat action list and its readiness report were evaluated
> separately around a 200 ms stability sampler, so a snapshot that answered `ready` could omit
> `play_card` and `end_turn`. That fix was re-cut into `0.12.4` instead of spending a version
> number on it, and a second re-cut followed the same day because the first one read the action queue
> outside combat, where `RunManager` has no executor: every `/state` request failed on the main
> menu. Three builds answer to `0.12.4` and only size or hash tells them apart - `file_size`
> 1233413 / `time_updated` 2026-09-15 19:38:00 for the first, 1236997 / 2026-09-15 23:34:57 for the
> second (superseded within the hour), and 1236997 / 2026-09-16 00:42:43 for the current one, whose DLL
> is SHA256 `0A8FBA67...`. The second and third share a byte count, so use the hash to tell those two
> apart. Every upload is recorded with its own numbers in
> [workshop-upload-v0.12.4_2026-09-15.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/workshop-upload-v0.12.4_2026-09-15.md).

### Fixed

- **One `/state` response can no longer contradict itself about combat actions.** `available_actions`,
  `combat.action_readiness` and the potion flags of one payload now come from a single combat-action
  gate, because the gate advances a shared 200 ms stability sampler: evaluating it per action let a response
  straddle that window and say "not yet" in the action list while readiness - built later from the same live
  state - said `ready`. A snapshot that reports `ready` therefore always carries `play_card`/`end_turn`,
  the read-only twin probe is gone, and the gate now also honours `modal_open` and `combat_paused`,
  which only the readiness report used to check.
- Settings changes keep the same session budget guard: new token/request caps apply immediately while
  accumulated usage stays, and in-game actions that spend no model request no longer count against the
  request cap.
- `invite_ai_teammate` and `continue_ai_teammate` answer `pending` while a dual-instance launch is still
  running instead of holding the HTTP request open, so a slow launch no longer reads as a client timeout.
- `select_character` is no longer advertised once the local player is already ready; `unready` is the
  action for that state.
- `wait_until_actionable` polls `/state` when the event stream cannot be opened instead of retrying a dead
  stream until the deadline.
- Isolated `--clientId` launches seed a complete `settings.save` (cloned from the Steam profile, or patched
  in place when `mod_settings` is null) so the mod loads on the first start.
- A second `invite_ai_teammate` or `continue_ai_teammate` that arrives while a launch already owns the gate
  answers 200 `pending` instead of classifying on an outcome it does not own: a concurrent invite could report
  the previous attempt's `completed`, and a concurrent continue answered a misleading "no saved run" 409.
- `state-invariants` requires `play_card` only where the executor advertises a ready combat action surface, so a
  snapshot taken while a played card is still resolving is no longer reported as a missing action.
- POSIX `start-game-session.sh` seeds the isolated profile under `%APPDATA%\SlayTheSpire2` on Git Bash and
  MSYS instead of an XDG path the Windows game never reads, honours `STS2_SLAY_USER_ROOT` on both platforms,
  and warns when the resolved save root does not exist.

### Added

- `scripts/start-game-session.ps1` / `.sh` forward extra game arguments, seed the isolated profile for
  `--clientId`, and export `STS2_API_PORT`; the POSIX `build-mod.sh` gains `--skip-install` and stages
  `mod_id.json`.

## v0.12.3 - 2026-09-13 (republished twice on 2026-09-14)

> Distributed to the Steam Workshop on 2026-09-13, with the GitHub release following the same day: the
> in-tree version, the uploaded Workshop content, the build and the release all come from the same commit.
> **On 2026-09-14 the same version number was rebuilt and republished on both sides** — the Workshop item
> was updated again and the `v0.12.3` tag and GitHub release were re-cut onto the new release commit — so
> the two co-op controls below reach players without spending a version number on a one-day follow-up.
> A build downloaded before that date is an earlier build of this same version and reports `0.12.3` as
> well, so tell builds apart by size or hash rather than by the version string: this first re-cut was
> Workshop `file_size` 1229829 / GitHub asset 552014 bytes `B7684C9F…`, against the original's 1227269 /
> 549933 bytes `B9DC1A07…`. Every upload is recorded with its own numbers in
> [release-v0.12.3_2026-09-13.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/release-v0.12.3_2026-09-13.md).
> The external-takeover route from #85 reached `POST /action` but not the F8 window's own Invite button,
> so the button a person actually clicks was the one that could not start that route; it now chooses the
> route the way the API does, and the tab says which one will run.
>
> **The same version was re-cut a second time later on 2026-09-14**, onto the live-validation follow-up
> and the diagnostics work below. The one player-facing item in it is the Continue button fix; the rest
> is log lines on paths that used to fail silently, release tooling and offline contract tests. Three
> builds now answer to `0.12.3`, and the version string never tells them apart, so use size or hash —
> Workshop `file_size` 1227269 / GitHub asset 549933 bytes `B9DC1A07…` for the first,
> 1229829 / 552014 bytes `B7684C9F…` for the second, and 1232901 / 555061 bytes `E882B653…` for the
> third, which is the current one. Every upload is recorded with its own numbers in
> [release-v0.12.3_2026-09-13.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/release-v0.12.3_2026-09-13.md) and
> [PRODUCT_PLAN_CURRENT.md](https://github.com/CharTyr/STS2-Agent/blob/main/PRODUCT_PLAN_CURRENT.md).

### Added

- **The AI Teammate tab gains the two co-op controls that existed only through the API.** Above the Invite
  button, **禁用自动选角** binds `companionAutoSelectCharacter` (ticked = `false`) and is saved on toggle,
  because the companion reads settings at launch and this tab has no separate Save; the line under it says
  what the second window will do either way: take the preselected character, or wait on the character
  screen for the AI driving it (or a person at that window) to choose and embark. Under the Invite button,
  **继续上次联机对局** goes through the same runtime entry as `continue_ai_teammate`, chooses the co-op
  route the way the API does, and is enabled only where that action is accepted: host main menu with a
  co-op save on disk. The game's own Load button opens a saved co-op run over Steam networking, rejects the
  local-connection player ids and renames the save as corrupt, so the way back into a saved run lives here.
  Only the pressed button reads as busy while a launch is in flight, and the tab re-reads Continue's
  availability whenever it comes into view.

### Fixed

- **The Continue button no longer stays grey until you leave the tab.** It was refreshed when the AI
  Teammate page came into view and nowhere else, so a button that was correctly disabled while the
  launch dialog was up stayed disabled after the dialog closed: the action was available over the API
  the whole time, but the window did not re-read it until the tab was left and re-entered. The panel
  tick now re-reads the button's availability whenever that page is visible (live: grey at brightness
  364 with the dialog up, enabled at 660 four seconds after it cleared, with no tab switch).
- **The overlay's Invite button uses the same route as the API.** It called the auto-play overload, so with
  no verified play model the click was refused at the old model gate while the identical request over
  `POST /action` launched the teammate for external takeover — backwards for a route meant to be driven
  from outside, since the button a person clicks was the one that could not be used. The button now
  evaluates `FirstRunSetup.Evaluate(settings).ReadyToInvite` exactly as `invite_ai_teammate` does: a
  verified play model means auto-play, otherwise the teammate launches, joins the run and comes up paused
  for external takeover without calling a model at all.
- The two lines under the **AI Teammate** tab describe that route instead of asking for a connection test,
  so the hint and the button can no longer contradict each other.

### Diagnostics, tooling and tests (second re-cut)

- **A probe that throws is no longer indistinguishable from a room with nothing to offer.**
  `CanChooseEventOption` and `CanChooseRestOption` answered every failure, including a thrown probe,
  with a bare catch that returned false, so a room whose probe threw simply lost its action from
  `available_actions` with nothing anywhere saying why. They still fail closed; the exception is logged.
- **Twelve empty catch blocks in `GameActionService.cs` now say what failed** — the cancel chain, the
  reward-drain fallbacks (`TryEnableProceedButton`, `NOverlayStack.Remove`, `ProceedFromTerminalRewardsScreen`),
  the two `confirm_bundle` press fallbacks, the three `continue_game_over` hooks and the Godot
  environment lookup. Returns and control flow are unchanged; a recovery that keeps failing is now
  visible in the log instead of only in a stuck action.
- **The bounded-await contract reads the syntax tree instead of a normalized line shape.** It could not
  see `await entry.OnTryPurchaseWrapper(player, timeout);` (the argument list is where the pattern wanted
  a semicolon) or `await completedTask!;` (the null-forgiving operator), and it only ever scanned
  `GameActionService.cs`. It now walks every await expression in the game-driving files — adding
  `DualInstanceCoordinator.cs`, `LocalDualInstanceLauncher.cs` and `AgentOverlayHost.cs` — and accepts a
  call only when it targets a mod member, a `Task.` race, or a live `CancellationToken`.
- **`get_game_data_*` keeps the mod's error envelope on a failure** (`code` / `status_code` / `retryable`
  were being dropped on the game-data path), and **`wait_until_actionable` no longer reports a broken event
  stream as an ordinary timeout** — its broad `except Exception` turned a dead stream into "nothing
  happened", which is the one thing a caller cannot act on.
- Release tooling: the `packaged-links` gate finally has a destructive case like the other seven;
  preflight runs the shared `check_release_metadata.py` instead of its own drifted copy; and the artifact
  check now reads the three version sources inside the artifact and refuses an inconsistent one.

## v0.12.2 - 2026-09-13

> The co-op handoff: the player who wants to fight their own character while an outside agent drives the
> teammate window no longer has to fake a passing model test to get there, and the state that agent reads
> stopped contradicting itself. Release and Workshop upload:
> [release-v0.12.2_2026-09-13.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/release-v0.12.2_2026-09-13.md). Live:
> [docs/live-validation-checklist.md](https://github.com/CharTyr/STS2-Agent/blob/main/docs/live-validation-checklist.md).

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

> The pause boundary: once a person presses pause, the agent no longer reads the run underneath. The pause menu and every page reached from it report their own screen, their action surfaces stay closed, and no page's own furniture — the pause menu's "abandon" entry among it — arrives as a capstone option any more. Release and Workshop upload: [release-v0.12.1_2026-09-13.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/release-v0.12.1_2026-09-13.md).

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

> Co-op is the headline: a saved multiplayer run can be continued with the AI teammate, and the teammate's character can be left for you to pick. The rest is a trust pass over the agent-facing state — every signal it surfaces now matches what the executor accepts, and no game-side wait can hang a request. Release and Workshop upload: [release-v0.12.0_2026-09-13.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/release-v0.12.0_2026-09-13.md).

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
- `docs/api.md`, both READMEs and the play skill are back in sync with the shipped contracts, including the bounded-wait envelopes and the new screen names (`3f55a3f`, `c060554`). [docs/live-validation-checklist.md](https://github.com/CharTyr/STS2-Agent/blob/main/docs/live-validation-checklist.md) collects the checks that only a live game can settle (`4554c8d`).

## v0.11.0 - 2026-09-12

> The overlay follows the language the game is running in. Chinese clients read exactly what they read before; English clients (and every other language) now read English instead of Chinese. Live evidence: [localization-2026-09-12.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/localization-2026-09-12.md); release and Workshop upload: [release-v0.11.0_2026-09-12.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/release-v0.11.0_2026-09-12.md).

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

> Distributed to the Steam Workshop on 2026-09-12 (item 3796486050, public, file_size 1088228). The in-tree version, the uploaded Workshop content and the build all come from commit `f9330ba`; no GitHub tag or release exists for this version yet. Live evidence: [validation-acceptance_2026-09-11.md](https://github.com/CharTyr/STS2-Agent/blob/main/history/validation-acceptance_2026-09-11.md).

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
