# Auto-play decision pipeline: speed and cost

> Read-only investigation for the bold experience overhaul, `dev` @ v0.17.0 (`fdc50fd`).
> Scope: the in-game auto-play turn (`AgentRuntime` → `AutoPlayRecovery` → `AgentLoop` → `GameBridge` →
> `GameActionService`), what it costs in model requests/tokens per game unit, where that cost is avoidable,
> and how to fix it without breaking the repo's invariants.
>
> All token figures below are **measured** where a local artifact exists and **estimated** (marked ≈) otherwise.
> Measurement method: character counts (`(Get-Content -Raw f).Length`, and JSON re-serialized compactly from
> real captured `/state` responses), and tokens ≈ chars/3.5 for English, CJK chars counted as 1 token each.

---

## 1. How it works today

### 1.1 The turn driver

`AgentRuntime.StartAutoPlay` → `AutoPlaySession.TryStart(AutoPlayLoopAsync, _lifetime.Token)`.
`AgentRuntime.AutoPlayLoopAsync` is the turn body handed to `AutoPlayRecovery.RunAsync`:

1. `GameThread.InvokeAsync(() => GameStateService.BuildStatePayload(), TurnGameThreadBudget=15 s, token)` —
   one **raw** frame for screen / phase / run id / `in_combat` / `act_id`.
2. `CurrentRunBoundary.Check` (run left or replaced → stop `run_end`), `ObserveSessionStateSnapshot`,
   `ObserveProactiveMoment`.
3. `NonCombatOnlyPolicy` gate (returns `AgentLoop.WaitForCombat()` with 0 requests when combat is not ours).
4. `TryCompanionImmediateAsync` — the only existing "decide without a model" path (see §1.6).
5. `SetRequestingModelStatus`, then `AgentLoop.PlayOnceAsync(token, checkState, SetPlayPhase, observeDecider)`.
6. `ObserveStrategyContext` — may trigger a **background** planner request (`StrategyPlanner.RefreshAsync`).
7. `finally`: `_requestingModel = false`, `ClearLiveThought()`.
8. `AutoPlayRecovery.RunAsync` then: `Commit` (budget `Observe` + `ApplyPlayResult` + `RecordTurnReceipt` +
   decision log + session) → `Observe` (retry/stop policy) → `afterTurn` (opt-in proactive chat, under
   `_turnGate`) → `delay`.

`AutoPlayRecovery.RunAsync` holds `_turnGate` (a `SemaphoreSlim`) for the whole turn, so chat, the "step"
button and teammate replies are serialized against a turn.

### 1.2 One decision turn (`AgentLoop.PlayOnceAsync`)

- `PlayPhases.ReadingState`; if `NonCombatOnlyEnabled`, read compact state and yield; otherwise `checkState(compact)`.
- `PlayPhases.WaitingForGame` → `GameBridge.WaitUntilActionableAsync(TimeSpan.FromSeconds(20), token)`.
  On timeout it returns `{ Error = "Timed out waiting for an actionable state.", WaitingForGame = true,
  RequestsSpent = 0 }` — no model request, and `AutoPlayRecovery` backs off 1 s.
- Re-read compact state (`GetCompactStateJsonAsync`), re-check non-combat ownership.
- `TryEscapeSandpitAsync` (`AgentLoop.Sandpit.cs`): at a lethal Sandpit countdown it plays Frantic Escape with
  **0 requests** (and `ValidateSandpitDispatch` re-checks the live frame at dispatch).
- Dual-layer: if `DualLayerSoloEnabled` / `DualLayerCoopEnabled`, no queued player instruction, a decider is
  configured and `StrategyStore` is wired → `TryDecideWithJevAsync`; a committed act or an error returns there,
  otherwise the LLM takes over **in the same turn** (`PlayWithModelAsync`, carrying Jev's receipt).
- Otherwise → `PlayWithModelAsync` → `CompleteWithToolsAsync` (tool rounds, `MaxToolRounds = 8`) → the model
  calls `act` → `ExecuteActAsync`.

`ExecuteActAsync` is the only path to the game:

1. `GetActionSnapshotJsonAsync` — one game-thread build that yields the compact state **and** the action
   descriptors from the same walk (`BuildDecisionSnapshotPayload`).
2. Non-combat re-check, then legality: `action` must be a member of `state.available_actions`
   (`EnumerateAvailableActions`, ADR 0001).
3. `ActIndexValidator.Validate(action, card_index, target_index, option_index, descriptors, compact)` —
   rejects a stale/absent index with 409 + `valid_indices` (`OptionPaths` is the index map).
4. `ValidateSandpitDispatch`, then `GameBridge.ActAsync` → `GameActionService.ExecuteAsync` behind
   `ActionExecutionGate`.
5. If `ActIndexValidator.IsUnsettled(result)` → `WaitUntilActionableAsync(20 s)` + a state read, and the act is
   reported as `pending`/`stable:false` **without** an error (so it does not spend the retry budget).

`stopAfterAct: true` means the turn returns as soon as one act was accepted: **one act per decision, no batching**.

### 1.3 Every wait, sleep, backoff and bounded timeout on the path

| Where | Bound | Notes |
|---|---|---|
| `AgentRuntime.TurnGameThreadBudget` | 15 s | turn snapshot post + companion immediate |
| `GameBridge.GameThreadStartBudget` | 15 s | `ActAsync` post start (abandonable while queued) |
| `AgentBridge.ActionableProbeBudget` | 5 s | per probe inside `WaitUntilActionableAsync` |
| `GameBridge.WaitUntilActionableAsync` outer deadline | 20 s (`PlayOnceAsync`), 20 s (unsettled act), tool `timeout_seconds` (default 20, clamp 1–120) | loops per frame: full `BuildStatePayload` + `WaitForNextFrameAsync` |
| `GameThread.WaitForNextFrameAsync` | 16 ms per frame, bounded 50 ms when frames stop | |
| `GameThread.InvokeAsync(func, token)` | **no deadline** (`Timeout.InfiniteTimeSpan`) | used by every `GameBridge` state read — the map's "every call is bounded" claim holds for acts, not for reads |
| `OpenAiCompatibleClient.DefaultRequestTimeout` | 10 min (+1 min `HttpClient.Timeout`) | per-request CTS from `LlmRequestTimeoutSeconds` when set (`AgentLoop.CreateClient`); a stalled provider can hold a turn for 10 min |
| `AutoPlayRecovery.Observe` | 1 s (waiting for game / waiting for player), 2 s, 4 s, 8 s (`2^min(n,3)` for unsettled, reasoning-exhausted, failures), 1 s on a repeated no-op | |
| `TryCompanionImmediateAsync` | `Task.Delay(400)` when waiting on the host's map vote | |
| `NoProgressPolicy.WaitingForGameLimit` | 150 s wall-clock | game-driven waits only; a wait on the *player* never runs the clock |
| `AutoPlayRecovery` stop rules | 3 consecutive failures, `RepeatThreshold` repeats with the same fingerprint, `UnsettledLimit` pending acts in a row | |
| Game action waits (`GameActionService.*`) | reward drain 20 s whole flow, one reward step 10 s, `play_card` 12 s, potion 10 s, end-turn 5 s, most rooms 10 s, return to menu 15 s, game-over 30 s / summary 60 s | inside one `act`, not additional model requests |
| `ScreenshotCapturePolicy` | 3 s render wait, capture gate lease = same deadline | only when a screenshot is attached |
| Strategy planner | one refresh in flight; context change, 3 low-confidence in a row, or every 10 turns | costs 1 request when it fires |
| Overlay tick | 800 ms (`OnProcessFrame`) | display only, off the decision path |

Two consequences that matter later:

- A single decision can burn 20 s (wait) + model latency (up to 10 min) + 20 s (settle) with **no output**.
- `WaitUntilActionableAsync` rebuilds the **whole** state (`BuildStatePayload`) once per frame while it waits.
  During a 20 s animation wait that is up to ~1,200 full builds (19 builders each, including the 64-node map,
  deck, relics and party inventory) on the game thread. That is the most likely source of the "agent makes the
  game hitch" reports, and `StateBuildTiming` already flags any build over 100 ms.

### 1.4 Prompt composition (`AgentLoop.PlayWithModelAsync`)

Messages are assembled **fresh for every decision**; the list is never carried across turns. Order (deliberate,
documented as "the cache contract"):

| # | Message | Size (measured chars) | ≈ tokens | Stable? |
|---|---|---|---|---|
| 1 | `system` = `PlayPrompt.PlaySystem` (boilerplate + the shared contract extracted from `skills/sts2-mcp-player/SKILL.md` between the SHARED PLAY CONTRACT markers) | **11,930** | 3,409 | yes (until the skill file or language changes) |
| 2 | `system` "Playbook for the screen…" + `PlaybookGuidance(screen)` slice of `references/screen-playbooks.md` (cap 2,400 chars) | 907 (COMBAT) / 793 mean | 259 | per screen |
| 3 | `system` `ReplyLanguageInstruction` (omitted for "auto") | 0 or ~110 | 0–31 | per setting |
| 4 | `system` "Strategy for the screen…" + `ScreenGuidance(screen)` slice of `references/strategy.md` | 1,863 (COMBAT, capped) | 532 | per screen |
| 5 | `system` standing strategy from the planner (skipped when default) | 0–400 | 0–110 | per plan refresh |
| 6 | `system` `TeammatePlayContext` + `user` "Recent team conversation…" | 0–2,000 | 0–570 | companion only |
| 7 | `user` vision caption / image | 0–1,500 + image | 0–430 | only if a screenshot is attached |
| 8 | `system` `ContextCompaction.Format(...)` | 0 unless prompt ≥ 80 % of the window | 0 | rare |
| 9 | `user` queued player guidance | 0–400 | 0–110 | rare |
| 10 | `user` "Latest compact game state:\n" + compact `agent_view` | **11,827–14,923** | 3,546–4,753 | **every decision** |
| 11 | `user` "Choose the next legal action… act exactly once." | 130 | 37 | yes |
| — | `tools` = `AgentTools.Play` (8 tools, `act` last) | ≈4,900 JSON chars | ≈1,400 | yes |

Measured totals for a COMBAT decision ≈ **9,200 prompt tokens**; add the JSON envelope and provider template and
it matches the live ledger's **11,380 tokens per request** (§2). The game data inside message 10 is localized
(card/relic/power text in the player's language), which is why the CJK share matters: 685 CJK chars in a
14.9 k-char frame.

How each figure was measured (so they can be re-derived):

- messages 1–5, 11: assembled from the committed Markdown/`PlayPrompt` text in a script (`PlaySystem` = 11,930
  chars; contract slice alone = 11,146 chars) and the real slice function's cap/paragraph rule (COMBAT playbook
  907, COMBAT strategy 1,863 after the 2,400-char cap).
- message 10: real captured `/state` envelopes, `agent_view` re-serialized compactly
  (`json.dumps(..., separators=(",",":"))`), CJK counted as 1 token/char.
- tools: `AgentTools.cs` string literals measured (1,861 chars of top-level descriptions + 1,553 chars of parameter
  descriptions) plus ≈1,500 chars of JSON scaffolding for 8 tools = **≈4,900 chars ≈ 1,400 tokens**; this is the
  only estimated row.
- the derived ≈9.2 k and the ledger's 11,380 differ by the request envelope, provider chat template and the
  occasional larger frame (14.9 k-char state = 4,753 tokens); the ratio 9.2/11.4 = 0.81 is consistent.

**What is *not* in the prompt**: the decision log, `PlaySessionMemory`, the previous turn's reasoning or action,
except through `ContextCompaction` (only above 80 % of the model's window — with the 256 k default that never
fires in a normal run) and through the planner's standing strategy. Every decision is therefore
**stateless**: the model cannot remember that it already decided to hold a card, or that it just played one.

**Prefix caching**: ordering is correct — everything screen-stable first, volatile state last — and the code says
so explicitly. Nothing sets `cache_control`, so the win is whatever the provider does automatically (DeepSeek and
OpenAI cache stable prefixes; the ledger's input tokens are therefore *billed* tokens, not necessarily *cost*).
The real cache-hostile fact is not the order, it is that **49 % of every request is static text that is re-sent
because each decision is a fresh request**.

### 1.5 Requests per game unit

Measured from `build/isolated-agent/decisions.jsonl` (gitignored local artifact, 316 accepted decisions,
305 `agent_loop`, 662 requests, 7,533,746 tokens):

| Metric | Value |
|---|---|
| requests per accepted decision | **2.17** (192 decisions took 2, 61 took 3, 41 took 1, 11 took 4–7) |
| tokens per request | **11,380** |
| tokens per accepted decision | **24,700** |
| `play_card` | 2.33 requests, **28,893** tokens per decision (170 of 305 decisions) |
| `end_turn` | 1.76 requests, **19,751** tokens per decision (46 decisions) |
| `choose_map_node` | 1.83 requests, 17,342 tokens (24) |
| `collect_rewards_and_proceed` | 1.79 requests, 13,481 tokens (14) |
| `buy_card` / `buy_relic` | 5.0 / 4.5 requests, 52,554 / 46,123 tokens |
| **only 13 % of decisions finish in one request** | 41 of 305 |

Cross-checks against the repo's own records:

- `docs/live-validation-checklist.md`: **52 requests / 622,864 tokens** (prompt 585,244 + completion 37,620) for
  45 accepted decisions, floors 1→3 → 11,978 tokens/request, 1.16 requests/decision, 13,841 tokens/decision.
- `CHANGELOG.md` 0.17.0: the default request cap was raised 30 → **1500 "about enough for one full run"**, with
  30 ≈ one combat and **800–1300 requests per run** estimated.
- `docs/live-validation-2026-09-23-results.md`: 54 `agent_loop` entries = **≥105 requests / 901,646 tokens**;
  22 Jev entries = 22 requests / 88,451 tokens.

Derived game units (using 2.17 requests and 24.7 k tokens per decision, 3.7 plays per combat turn from
`play_card` 170 / `end_turn` 46):

| Game unit | Decisions | Requests | Tokens |
|---|---|---|---|
| one card play | 1 | 2.3 | ≈29 k |
| one combat **turn** (3–4 plays + end_turn) | ≈4.7 | ≈10 | ≈116 k |
| one 3–4 turn combat | ≈15–19 | ≈33–41 | ≈370–470 k |
| one floor | ≈15–17 | ≈33–37 | ≈370–420 k |
| one full run (3 acts) | ≈700–1000 | **≈1500–2200** (repo estimate 800–1300) | **≈9–15 M** |

The gap between the repo's "800–1300 requests per run" and the 2.17 requests/decision figure is the point of
§3.1: at one request per decision the same run would need ~700.

Jev is *not* a prompt-size saver: `JevExecutionDecider.BuildRequest` forwards the same compact state, the same
playbook slice, the strategy and up to 255 enumerated options (`play_card:0->1`, `choose_map_node:2`, …), i.e.
≈4–5 k tokens per Jev decision. Its win is output length/latency and a cheap model, not input tokens. In the
2026-09-24 run 56 of 82 decisions were Jev, so dual-layer roughly halves the *price* per decision while leaving
the *prompt* the same shape.

### 1.6 What already avoids a model call

| Mechanism | File / symbol | Scope |
|---|---|---|
| Sandpit emergency escape | `AgentLoop.TryEscapeSandpitAsync`, `SandpitSafetyPolicy` | one very specific lethal case, 0 requests |
| Companion immediate | `AgentRuntime.TryCompanionImmediateAsync` + `Multiplayer/CompanionPlayPolicy.DecideImmediate` | companion only: follow the host's map vote; confirm a blocking FTUE modal. `Wait` → 400 ms delay |
| Non-combat-only ownership | `NonCombatOnlyPolicy`, `AgentLoop.WaitForCombat` | yields the turn, no request |
| Reward drain internals | `GameActionService.Rewards.DrainRewardFlowAsync` + `RewardChoicePolicy` | *inside one act*: claims claimable rows, proceeds, and stops on purpose at a real card choice (`reward.pending_card_choice = true`). Cards are never auto-picked |
| Jev | `JevExecutionDecider`, `JevOptionEnumerator` | a different (cheaper/faster) model, still a request |
| Wait/repeat guards | `AutoPlayRecovery`, `NoProgressPolicy` | stop instead of spending |

There is **no** general "the state already decides this" layer: a MODAL with a single `confirm_modal`, a MAP with
one `available_nodes` entry, a REWARD screen whose only move is `collect_rewards_and_proceed`, an `end_turn` with
zero energy and nothing playable, or a GAME_OVER with one button all still cost a full 11 k-token decision.
`RewardChoicePolicy` is *not* that layer — it resolves an index that the model already sent, inside `resolve_rewards`.

### 1.7 Multi-action batching: what exists today

Nothing. `AgentTools.Play`'s `act` accepts exactly one action (`action`, `card_index`, `target_index`,
`option_index`, `x`, `y`, `tool`, `reason`, `raw_state`). In `CompleteWithToolsAsync`, `stopAfterAct: true` returns
the turn immediately after the first accepted act, and a second `act` inside one response is refused with
`{"error":"only one act is allowed per decision"}`. So:

- one model round trip per single game action,
- the hand index space (`combat.hand[]`, all cards including unplayable) shifts by one after every `play_card`,
  which is exactly why a plan cannot be expressed in indices (see §4.3).

### 1.8 The streamed-tool-argument truncation

- Live finding (CHANGELOG 0.17.0, `history/release-v0.17.0_2026-10-28.md`): a gateway streamed `act` arguments as
  only `{"action":"play_card"}` and finished with `finish_reason=tool_calls`; the non-streaming body of the same
  request was truncated the same way, so the "retry without streaming" mitigation only helps on gateways whose
  non-streaming path is intact. Three such turns stopped a run (`连续 3 次决策未成功`).
- Detection: `AgentLoop.LooksLikeTruncatedStreamedAct` (`AgentLoop.Arguments.cs`) — an `act` whose action
  `ActIndexValidator.RequiresIndex` but whose `card_index` (or `option_index`) is missing, plus
  `IsMissingIndexError` (`"must come from"`) as a second signal from the validator.
- Containment: `CompleteWithToolsAsync` sets `toolStreamFallbackTried` and re-sends the **whole turn**
  once (`continue`), so the retry is a second full request. Then the content-only fallback
  (`ActJsonParser.TryParse`, `PlayPrompt.JsonActFallback`) is used **only when the model does not support tools**
  at all — never when a tool call was truncated.
- Accounting: the retry is counted (`requestsSpent++` per round), so a truncated turn costs 2 requests, and needs
  3 such turns in a row to stop the run.

---

## 2. Measured / estimated costs

### 2.1 Compact state, measured on real captured frames

Local captures in `build/live-2026-10-27/*.json` (HTTP envelopes containing a full `/state` with `agent_view`):

| Screen | `agent_view` chars | ≈ tokens | biggest parts |
|---|---|---|---|
| COMBAT (`a3-combat-readiness`) | 14,923 | 4,753 | `map` 4,955 (1,416) · `combat` 4,683 · `run` 4,639 |
| COMBAT (`a2-combat-ready`) | 11,827 | 3,546 | `map` 4,955 · `combat` 3,864 · `run` 2,420 |
| REWARD (`a4-reward-5`) | 10,381 | 3,172 | **`map` 4,955** · `run` 3,119 · `combat` 1,402 (empty combat!) |
| MAP (`a4-step5-map-final`) | 10,336 | — | `map` 5,015 (of which `available_nodes`/`options` only 63) · `run` 3,293 |
| raw `/state` (same frame) | 39,371–47,452 | ≈11 k | matches `docs/api.md`'s "≈4,000–9,500 tokens" |

Breakdown of one COMBAT frame's sub-objects:

- `map.nodes` = **4,831 chars / 64 nodes**, emitted on **every** screen — combat, reward, event, shop. It is the
  act's whole graph (`coord`, `node_type`, `visited`, `children`), while the action only ever reads
  `map.available_nodes` (63 chars on a MAP frame). That is ~1,400 tokens ≈ **35 % of a reward frame and 30 % of a
  combat frame, every single decision**.
- `run.players[]` = 1,196–1,765 chars in **single-player**: `deck` (every card, repeated per copy),
  `relics`, `potions` for the *same local player* whose data is already in `run.deck` / `run.relics` /
  `run.potions`.
- `run.piles` (936 chars) duplicates `combat.draw` + `combat.discard` + `combat.exhaust` (901 + …) in combat.
- `combat.action_readiness` = 598–607 chars of 22 booleans, mostly `false`/`null`, on every screen (it is kept on
  non-combat frames too, where it means nothing).
- `combat.card_value_note` = 227 chars, a constant string repeated every combat decision.
- `combat.hand[]` = ~400 chars per card, 16 fields of which ~10 are `null` in the common case
  (`why`, `unplayable_reason`, `unplayable_reason_raw`, `unplayable_preventer_id`, `unplayable_preventer_type`,
  `target`, `targets`, `keywords`, `mods`, `can_play_result`).

### 2.2 Cost of the fixed prefix (per request, every time)

| Part | ≈ tokens | Comment |
|---|---|---|
| `PlayPrompt.PlaySystem` (shared contract) | 3,409 | 11,930 chars, byte-identical between decisions |
| `AgentTools.Play` schemas | ≈1,400 | 8 tools; `act` alone ≈2,000 chars |
| screen slices (playbook + strategy) | 791 | 259 + 532 for COMBAT |
| closing instruction | 37 | |
| **fixed prefix** | **≈5,600** | **49 % of a measured 11,380-token request** |
| compact state | 3,546–4,753 | the only part that is genuinely new information |

### 2.3 What a run costs

`662 requests × 11,380 = 7.53 M` tokens for 305 decisions (one measured stretch); the repo's own per-run estimate
of 800–1300 requests at the same per-request cost is **9.1–14.8 M input tokens per run**, plus completion tokens
(≈6 % of the total in the 52-request sample: 37,620 / 622,864).

At one request per decision, the same 305 decisions would be ≈3.5 M tokens (−53 %); with whole-turn batching
(§4.3) a combat turn becomes one request, which is ≈−70 % on combat; with the forced-move layer (§4.2) another
≈−20 % of decisions. Those are multiplicative; §4.8 gives the combined target.

Latency: every decision is a serial chain of *wait for actionable → request → act → settle*, so a combat turn is
3–5 sequential model round trips. With 2.17 requests/decision the model is asked twice per action, so a 4-turn
combat is ≈8–10 round trips before the game can move.

---

## 3. Pain points, ranked by player impact

### 3.1 Two model requests per action, on 87 % of decisions — P0

**Evidence**: 192/305 decisions spent exactly 2 requests, 61 spent 3, only 41 spent 1 (13 %); 2.17 requests and
24,700 tokens per accepted action. **Cause**: two independent code paths each add a full request, and both
re-send the entire ≈11 k-token prefix:

1. **Tool-less streamed completion → whole-turn non-streaming retry.** In `CompleteWithToolsAsync`, a streamed
   `act` turn whose completion carries no tool call (`ToolCalls.Count == 0`, `acted == null`, `stopAfterAct`,
   `SupportsTools`, `request.Stream`) flips `toolStreamFallbackTried` and `continue`s — re-sending the identical
   prompt with `stream:false`. That condition does not require any evidence of a *streaming* failure (e.g. an
   empty completion); a thinking model that answers with prose and no tool call pays for the whole turn twice.
2. **A read-tool round.** The model may call `get_game_state` / `get_available_actions` / `wait_until_actionable`
   before acting; each read-tool round is another full request. The state and the action list are **already** in
   message 10 and in the tool descriptions, so those rounds buy nothing the first request did not carry.

Player impact: 2× cost, 2× wait. This is also the direct reason the 1500-request cap only covers about one run.

### 3.2 15 % of decisions are forced by the game, yet pay for a model — P0

**Evidence**: 46 of 305 decisions were `end_turn` at 19,751 tokens each, and the sampled reasons are all
forced-move phrasing — *"No energy left and every card is unplayable, so end the turn"*, *"No playable cards with
0 energy; ending turn is the only legal combat action"* (18/18 sampled). Add `collect_rewards_and_proceed` (14,
a drain with no real choice), `proceed` (5), `claim_reward` (5), `continue_run` (6),
`open_character_select`/`embark`/`dismiss_modal`/`open_chest` and single-node `choose_map_node`: **≈20–25 % of a
run's decisions have exactly one legal non-passive action.** The 2026-09-23 session even records two of them as
chat moments ("two zero-energy turns", "the map's only option") — the model was asked, and answered, a question
with one answer.

Player impact: ~25 % of the money and ~25 % of the wall-clock spent watching an animation while a model is asked
to say "end turn".

### 3.3 The state payload carries data no decision can use — P1

`map.nodes` (4,831 chars) on every screen, `run.players` duplicating the local player's inventory, `run.piles`
duplicating `combat.draw/discard/exhaust`, `action_readiness` on non-combat screens, and ~10 always-null fields
per hand card. Measured: 2,500–2,900 of a 3,500–4,750-token state is either screen-irrelevant or duplicated
(**−23 % of the whole prompt** if trimmed).

Player impact: slower prefill on every decision, more tokens, and — worse — a *noisier* prompt: the model has to
notice that the map it can see is not the map it can click, and that the party block repeats its own deck.

### 3.4 One action per request, so a turn is N round trips — P1

`stopAfterAct: true` + "only one act is allowed per decision". A 4-turn combat is 15–19 decisions ≈ 8–10 sequential
round trips; the player watches the agent think after every single card. The model also cannot commit to a line
("Strike, Strike, Bash, end turn"), so it re-derives the turn plan from scratch each time — and, because the
prompt is stateless, it has no memory of the plan it just had.

### 3.5 The truncating gateway still stops runs — P1 (availability, not cost)

`LooksLikeTruncatedStreamedAct` covers exactly two fields (`card_index` for `play_card`, `option_index` for the
rest) and its only remedy is a whole-turn non-streaming re-send. On the gateway measured on 2026-10-28 the
non-streaming body was truncated too, so the run stopped after three turns (`release-v0.17.0`: "缓解，非根治").
Every affected turn also costs the wasted stream request.

### 3.6 The wait loop stutters the game — P2

`WaitUntilActionableAsync` calls `BuildStatePayload()` (19 builders, 64-node map, deck, relics, party inventory)
once per rendered frame for up to 20 s, twice per decision (pre-decision wait and unsettled-act settle). That is
game-thread CPU the player feels as frames dropped while the agent "waits for the animation".

### 3.7 Minor, but cheap to fix — P2

- `GameBridge` state reads use the token-only `GameThread.InvokeAsync` overload → no deadline (contradicts the
  project map's "every call is a bounded, token-carrying post" for reads).
- A stalled provider can hold a turn for 10 minutes (`DefaultRequestTimeout`), which reads as "the agent froze".
- The 512-token `act` schema, the 3,400-token contract and the 8-tool list are re-sent even when only `act` can
  possibly be called.
- `MaxToolRounds = 8` with a ~11 k prompt allows one decision to cost ~88 k tokens.

---

## 4. Bold improvement proposals

Ranking (impact × effort × risk, best first). "Impact" is measured/estimated savings on the §2 baseline, not a
promise — live calibration is required (§5).

| # | Proposal | Expected impact | Effort | Risk |
|---|---|---|---|---|
| 4.1 | Kill the second request per decision | **−45–55 % requests**, −45 % tokens | S–M | **L** |
| 4.2 | Forced-move resolver (no model call) | **−20–25 % decisions**, −20 % tokens | M | **L** |
| 4.3 | Whole-turn action plan (validated, step-wise) | **−60–70 % combat requests**, turn latency from 4 round trips to 1 | L | M–H |
| 4.4 | Compact-state diet (per-screen projection) | **−23 % prompt tokens**, faster prefill | M | L |
| 4.5 | Static-prefix trim + play tool surface = `act` | −5–9 % tokens/request, and the 49 % static prefix becomes cache-earning instead of cache-hoping | S | L |
| 4.6 | Truncation: capability memo + content fallback + unique-index resolution | removes a run-killing failure mode; −1 wasted request/turn on affected gateways | M | L–M |
| 4.7 | Cheap readiness probe for the wait loop | no token change; removes per-frame full state builds | S–M | L |

### 4.1 Kill the second request per decision (biggest, cheapest)

**Goal**: 2.17 → ≈1.05 requests per accepted decision.

1. **Instrument first (1 commit, shipped alone).** Add additive fields to the decision receipt/log so the two
   causes become separable: `tool_rounds` (`AgentTurnResult.ToolRounds`, already computed, not logged),
   `read_tool_rounds` (count of non-`act` tool calls executed), `stream_demoted` (bool, whether
   `toolStreamFallbackTried` fired). `DecisionLogEntry` gains three nullable fields; `docs/api.md` gets the rows
   (`api-facts` gate); `RatioOfOneRequestPerDecision` becomes a measurable target instead of a guess.
2. **Stop paying for `act` turns twice.** Make `stream:false` the default for `act` turns and stream only when the
   player has `ShowThinkingInChat` on (`AgentSettings` — add the setting to `SettingsClone`, `HarvestSettings`,
   `EnsureValidShape` per the project map's trap). Alternative if streaming must stay: only demote when the
   completion is *empty* (`completion.Content == null && completion.Reasoning == null`), which is the actual
   signature of a provider that cannot serialize tools on the stream path; a prose answer should be answered with
   the tiny "you must call act" nudge, not a full re-send.
3. **Remove the read-tool round from the play path.** `AgentTools.Play` drops `get_game_state`,
   `get_raw_game_state`, `get_available_actions` (the compact state and `available_actions` are already message 10
   and the legality surface); keep `get_game_data_*`/`get_relevant_game_data` because card metadata is *not* in
   the state, and keep `wait_until_actionable` only for the "the screen is animating" case, answered from the
   snapshot the act path would take anyway.
   - **Invariant**: `AgentTools.Mcp` is `ReadOnly + Play` — do **not** shrink what `AgentTools.Mcp` advertises
     (ADR 0002 / `test_native_tool_alignment.py`). Introduce `AgentTools.PlayInGame` for the loop and leave
     `AgentTools.Mcp` on the current list.
   - `McpPlayerSkillTests.SkillTracksLivePlayContract` pins that `wait_until_actionable` and `get_raw_game_state`
     remain *reachable in game* (through the contract text or a screen slice). Keep both named in
     `PlayPrompt.PlaySystem`'s "Tools:" line (they stay callable on the MCP surface and are still documented),
     and update that test deliberately if the claim changes.

**Impact**: 2.17 → 1.05 requests/decision (if cause 1 dominates, it alone is 63 % → ~13 %) → **≈−52 % requests
and tokens** on the measured ledger, and roughly halves the per-decision wall-clock.

### 4.2 Forced-move resolver — decisions that need no model

A pure, offline-testable policy, modelled on the two precedents (`CompanionPlayPolicy.DecideImmediate`,
`SandpitSafetyPolicy`):

```
ForcedActionPolicy.Resolve(screen, available_actions[], descriptors, compact_state)
    -> { None | Act(action, card/option index?, reason) }
```

Rules (each one only when the resolution is provably unique *and* index-free or single-element):

| Screen / state | Resolution | Why it is forced |
|---|---|---|
| `MODAL` with `confirm_modal` and no `dismiss_modal` | `confirm_modal` | the only legal action (already the companion's rule) |
| `GAME_OVER` with only `continue_game_over` | that | one action |
| `MAP` with exactly one `map.available_nodes` | `choose_map_node` option 0 | one node |
| `REWARD` with no card choice pending and no `claimable` row | `collect_rewards_and_proceed` | drain already claims + proceeds and stops at a real card choice (`DrainRewardFlowAsync`) |
| any screen whose non-passive action set has exactly one member that needs **no** index | that action | one action |
| `COMBAT` where every `combat.hand[].playable == false`, every potion `usable == false`, and `end_turn` is legal | `end_turn` | nothing else can be done; the model cannot improve on it |
| single-element indexed lists (`event.options` with one enabled option, `capstone.options`, `rest.options`) | that index | one option |

Never auto: card rewards, `choose_reward_card`, any multi-option event/map/rest, `buy_*`, `select_deck_card`,
and anything on a screen with a real strategic choice. Also skip the shortcut when
`Settings.NonCombatOnlyEnabled` (the ownership boundary must be evaluated by the existing gate) and when a Sandpit
escape is pending (the existing escape runs first).

#### 4.2.1 Exact conditions, in evaluation order

`ForcedActionPolicy.Resolve` runs top-down and returns the first match. Inputs: `screen`, `available_actions[]`,
the action descriptors (`/decision-snapshot`'s `available_actions`), the compact state. "Passive" = the
`GameBridge.PassiveActions` set (`discard_potion`, `save_and_quit`) — never chosen by a shortcut.

```
 0  guard: NonCombatOnlyPolicy.IsCombat(screen, in_combat)          -> None (the ownership gate decides)
 0b guard: SandpitSafetyPolicy.Read(state).Critical                  -> None (AgentLoop.TryEscapeSandpitAsync owns it)
 1  MODAL:      confirm_modal in actions && dismiss_modal not in actions
                && (modal.type_name contains "Ftue" || !in_combat)    -> confirm_modal            (no index)
 2  GAME_OVER:  actions == [continue_game_over]                       -> continue_game_over      (no index)
 3  COMBAT:     every combat.hand[].playable == false
                && every run.potions[].usable == false
                && every run.potions[].discard is irrelevant
                && end_turn in actions                                -> end_turn                (0 requests)
 4  MAP:        map.available_nodes.Count == 1                        -> choose_map_node(0)      (1 index, fixed)
 5  REWARD:     reward.pending_card_choice != true
                && reward.card_options.Count == 0
                && no row with claimable == true                      -> collect_rewards_and_proceed
                (the drain claims/proceeds and stops on a real card choice by design)
 6  indexed singletons: the action's own index list (ActIndexValidator.OptionPaths) has
                exactly one *available* element AND available_actions has no other
                index-free member that could change the choice:
                event.options(1 enabled) -> choose_event_option(that index)
                capstone.options(1)      -> choose_capstone_option(0)
                rest.options(1)          -> choose_rest_option(0)
                chest.relics(1)          -> choose_treasure_relic(0)
 7  generic:    after removing passives, exactly one action remains AND it needs no index
                -> that action
 8  otherwise   None (the model decides)
```

Notes that keep this honest:

- Rule 3 is checked against `playable` from the **live** `CombatActionGate` (already in the compact frame), not
  against energy arithmetic — cost modifiers, `X` costs, star costs and "unplayable while…" reasons are all
  already folded into `playable`/`why`.
- Rule 3 must still consult `end_turn_will_kill_player`/`lethal_risks`: ending a turn that kills the player is
  *the same legal move* the model would take, but the shortcut should log the risk in its reason so the decision
  log shows why. It must not fire when a potion is `usable` (rule 3 requires that).
- Rule 5 must not fire while `reward.pending_card_choice` is true (a card choice is a real decision) and must not
  fire when the only claimable row is a potion reward with a full belt (`RewardClaimPolicy.IsClaimable` already
  models the "click is swallowed" trap, #219).
- Rules 6/7 never fire when two members of the same list are available (that is a choice), and never for
  `choose_reward_card`, `resolve_rewards`, `buy_*`, `select_deck_card`, `choose_map_node` with >1 node.

Implementation sketch (respecting invariants):

- New `STS2AIAgent/Agent/ForcedActionPolicy.cs` — pure, no Godot types, takes the compact JSON + descriptors JSON
  as strings (like `NonCombatOnlyPolicy` / `SandpitSafetyPolicy`). Register it in `docs/project-map.md`
  (`ProjectMap.EverySourceMapped`) and in `SourceCoverageTests.KnownUncompiledSources` only if it references game
  types (it must not).
- Hook in `AgentLoop.PlayOnceAsync` **after** `TryEscapeSandpitAsync` and **before** the Jev/LLM branch, so the
  Jev request is saved too. It must route through `ExecuteActAsync` (fresh snapshot → `available_actions`
  membership → `ActIndexValidator` → `GameBridge.ActAsync`), i.e. the same legality surface, ADR 0001 untouched.
- `DecisionLog` source: a new value `"auto"`, label in `PlayerLabels.Source` (`"自动"` → English table entry in
  `Localization/Loc.Strings.Runtime.cs`), reason from `Loc.T(...)` with an English entry. `requests_spent = 0`,
  `total_tokens = null`.
- A setting `AutoResolveForcedMoves` (default on) so the behaviour is auditable and revertible; per the map's
  trap, a new setting touches `AgentSettings`, `SettingsClone`, `HarvestSettings`, `EnsureValidShape`.
- Safety: `AutoPlayRecovery` must not read a 0-request turn as "no progress" — it already treats `Acted != null`
  as progress; add a unit test for the repeat fingerprint (a forced `end_turn` on an unchanging state must still
  trip `RepeatThreshold`).

**Impact**: the measured ledger's 46 `end_turn` + ~25 forced non-combat decisions out of 305 = **−23 % decisions
and −20 % tokens**, and it removes 2 requests for each of them.

### 4.3 Whole-turn action plan (validated, step-wise) — the ambitious one

**Goal**: one request per combat turn instead of 3–5, without letting a stale index act.

**Surface**: a new tool used **server-side only**:

```
tool "act_plan" (in a new AgentTools.PlayInGame list; NOT added to AgentTools.Mcp)
  steps: [ { action, card_id?, upgraded?, occurrence?, enemy_id?, option?, reason? } ]  (1..8)
```

`card_id`/`enemy_id`/map `coord` instead of indices, because the executor re-resolves them against a **fresh**
snapshot before every step. That is the whole design: a plan is a *sequence of intents*, never a sequence of
indices.

**Executor** (`AgentLoop.ExecutePlanAsync`, new partial `AgentLoop.Plan.cs`):

```
for step in steps (max 8, no further steps after end_turn):
    snapshot = await _bridge.GetActionSnapshotJsonAsync(token)      // bounded game-thread read, no tokens
    resolved = PlanStepResolver.Resolve(step, snapshot)             // pure, offline-tested
        - play_card: card_id (+upgraded, +occurrence among *remaining* copies) -> live combat.hand index
          must be `playable`; enemy_id -> index in combat.enemies[] where alive/hittable
        - use_potion/discard_potion: potion_id
        - choose_map_node: coord (map.nodes[].coord); fall back to index only for a single available node
        - choose_reward_card / claim_reward: card_id or line prefix
        - end_turn / proceed / collect_rewards_and_proceed / confirm_modal / ... : no index
    if resolved.Stop -> stop the plan, keep the executed prefix
    validate via ActIndexValidator against the same snapshot (never a second build)
    outcome = await ExecuteActAsync(resolved.ToActArgs(), token, checkState, onAccepted)
    if outcome.Error != null -> stop (no retry: the state diverged or the step was illegal)
    expect := step.Expect (optional)  // energy_before - cost, card count -1, screen unchanged
    if !PostconditionHolds(expect, outcome.CompactState) -> stop
```

Stop conditions (all of them hand the turn back to the normal decision path, which then sees the real frame):

1. the step's `card_id` no longer exists among playable hand cards (played, discarded, transformed, exhausted);
2. energy after the step is less than the next step's declared cost (or `empty`/`X` semantics differ);
3. the screen changed (combat over, card-draw selection modal opened, card reward appeared);
4. the game reported `pending`/unstable (`ActIndexValidator.IsUnsettled`);
5. a random effect added cards (`draw` count changed in a way the plan did not declare) — the plan simply stops
   before the next step; nothing is guessed;
6. `NonCombatOnlyPolicy` or `SandpitSafetyPolicy` throws (already checked inside `ExecuteActAsync`).

Return shape (additive, backward compatible):

- `AgentTurnResult` gains `PlanStepsRequested`, `PlanStepsExecuted`, `PlanStoppedReason` (nullable). `Acted` keeps
  the **last** executed action name so `PlayerLabels`, `NoProgressPolicy` and the overlay keep working unchanged.
- `ActResultJson` = a compact `{steps:[…], stopped:{reason, step_index}, state:{…}}`.
- `DecisionLog`: one entry **per executed step** (same action vocabulary, `/decisions` keeps one row per action so
  `docs/api.md` and the MCP `get_decision_log` readers do not change), plus additive `turn_id` (shared by the
  steps of one request), `plan_index`, `plan_size`. `requests_spent` is recorded **once**, on the last step of the
  turn (or as `requests_spent = 1, plan_size = n` on a single row with `plan_index` — pick one and pin it in
  `DecisionLogTests`); the invariant "one request = one accounting" is preserved because
  `SessionBudgetGuard` is fed by the turn receipt, and the new rows carry `0` for the non-dispatch steps.
- Budget: unchanged — one `CheckBudget` before the request, one `Observe(receipt)` after. The plan does not spend
  extra requests, so the 1500 cap now buys ~3× the run.

**Prompt side**: `PlayPrompt.PlaySystem`'s closing line changes from "call act exactly once" to "call `act` once,
or `act_plan` when you can commit to a line; use card ids, not indices". The screen slices for COMBAT gain a
short paragraph on how to express a plan (names, not indices). Update:
`McpPlayerSkillTests.SkillTracksLivePlayContract` (it pins the *contract text*, not this sentence),
`PlaybookSectionsTests` (heading set unchanged), and the `act_plan` description in `AgentTools`.

**Why not indices**: `play_card.card_index` indexes `combat.hand[]` including unplayable cards, and the hand array
shrinks on every play, so step 2 of any index-based plan is already wrong. `enemy_id` is stable; `coord` is
stable; `potion_id` is stable; occurrence counting handles four copies of `STRIKE_IRONCLAD` (`occurrence = 0,1,2`).

#### 4.3.1 Executor grammar and the pitfalls it has to survive

**Step grammar** (all fields optional except `action`; indices are never accepted):

```json
{"action":"play_card","card_id":"STRIKE_IRONCLAD","upgraded":false,"occurrence":1,"enemy_id":"FUZZY_WURM_CRAWLER","reason":"…"}
{"action":"use_potion","potion_id":"FIRE_POTION","enemy_id":"…"}
{"action":"choose_map_node","coord":"2,3"}
{"action":"end_turn"}
```

Resolution against the live snapshot, per step (pure function `PlanStepResolver`):

| `action` | Resolve to | Ambiguity rule | Stop if |
|---|---|---|---|
| `play_card` | index in `combat.hand[]` of the `occurrence`-th card whose `card_id` (+`upgraded`) matches, in hand order, skipping copies already consumed by earlier steps | same `card_id` + `upgraded` copies are interchangeable; `occurrence` only picks *which copy*, never *which card* | no match, or `playable == false`, or `draw`/`discard`/`exhaust` changed such that the id vanished |
| `play_card` + target | `target_index` from `enemy_id` → live `combat.enemies[]` where `alive && hittable` | if two live enemies share an `enemy_id`, take the first in slot order | target gone, or the card no longer requires a target (`target` null) |
| `use_potion` | index in `run.potions[]` by `potion_id` | `PotionSlots` is fixed-length with `null` holes, so indices are stable — but resolve by id anyway so a discard/claim does not retarget | `usable == false`, or the slot was emptied |
| `choose_map_node` | index in `map.available_nodes[]` by `coord` | exactly one node may match | the coord is not in `available_nodes` (route changed) |
| `end_turn` / `proceed` / `collect_rewards_and_proceed` / `confirm_modal` / `dismiss_modal` | itself | — | not in `available_actions` |

**Pitfall matrix** (what each real hazard does to a plan, and the rule that contains it):

1. **Hand index shift after `play_card`** — the concrete reason indices cannot be used. `play_card.card_index`
   indexes `combat.hand[]` (all cards, unplayable included, same order as the live `Hand.Cards`), and the array
   shrinks by one on every play, so step 2 of an index plan is off by one, step 3 by two. Card **ids** are stable
   across the shift; `occurrence` + a per-plan consumed multiset makes the mapping deterministic.
2. **Draw effects** (Battle Trance, Pommel Strike, enemy-imposed draws) — the hand grows mid-plan; new copies may
   land at the end of the hand. Because resolution is by `card_id`+`upgraded`+`occurrence` against the *current*
   hand, a plan step that names the *third* Strike after a draw may resolve to a different physical copy — which is
   harmless (copies are interchangeable) — but a step that named `occurrence: 3` when only two copies exist must
   stop, not fall back to a different card. The plan's declared postcondition (`hand count`, `energy`) is what
   detects "too much happened"; the stop reason is `state_diverged:hand`.
3. **In-combat card-selection prompts mid-plan** (a card that asks you to choose a card to exhaust/upgrade, or any
   effect that opens `CARD_SELECTION` / sets `hand_in_card_selection`) — the screen changes, `available_actions`
   becomes the selection surface, and `select_deck_card` indexes a completely different list
   (`selection.cards[]`). A plan **always stops** on a screen change; card selection is exactly the case where a
   half-finished plan is meaningless. The next turn sees `CARD_SELECTION` and decides normally (or a future
   extension may allow one `select_deck_card` step by `card_id`, never by index).
4. **Potions** — resolved by `potion_id`; target by `enemy_id` (a potion aimed at an enemy that died mid-plan is a
   409 `invalid_target` in the current executor, which the plan must treat as "stop", not "retarget"). Using a
   potion can also draw/apply powers; the postcondition check catches anything the plan did not declare. Drinking
   a potion does **not** shift other potion indices (fixed-length slots with nulls), so a `use_potion` followed by
   another `use_potion` is safe.
5. **Energy / cost drift** — never *predict* cost: read `energy_cost` (and `star_cost`) from the live frame and
   require `playable == true`. The plan's optional `expect: {energy_after}` catches a cost modifier changing under
   it; absence of `expect` is not an error (the plan is advisory, the executor is authoritative).
6. **Enemy death / combat end** — `screen != "COMBAT"` or `play_card` no longer in `available_actions` → stop,
   return the executed prefix; `AutoPlayRecovery` sees a normal successful receipt.
7. **Random targets / random values** — out of scope by construction: the plan never asserts damage numbers, only
   that the step was legal and the state moved in the declared direction.
8. **Sandpit** — a plan containing `end_turn` at a lethal local countdown would hit
   `ValidateSandpitDispatch`, which throws `AutoPlayStoppedException` and **stops the run**. Guard before the plan:
   if `SandpitSafetyPolicy.Read(state).Critical`, do not accept a plan at all (the existing escape path owns it).
9. **Unsettled acts** — `ActIndexValidator.IsUnsettled(result)` (a `pending` response) ends the plan; the loop
   must not enqueue another step against a screen that has not settled.
10. **Turn gate / wall clock** — the whole plan runs inside one turn under `_turnGate`; cap it (8 steps, and a
    wall-clock budget of ~15 s) so a long plan cannot make pause/chat unresponsive. Each step takes its own
    `ActionExecutionGate` lease through the existing `ExecuteActAsync`, so overlap protection is unchanged.
11. **`stopAfterAct` semantics** — the plan tool must be the only responder for that turn: if the model emits both
    `act` and `act_plan`, execute the first one received and answer the other with
    `{"error":"one action or plan per decision"}` (mirrors today's "only one act is allowed per decision").
12. **Decision log / accounting** — one row per executed step (`requests_spent = 0` on all but the row that owns
    the request), a shared `turn_id`, and `plan_index`/`plan_size`; `AccountTurn` stays the single writer or
    `/health.session_requests` and `RunSpend` double-count.

**Risk controls**: max 8 steps, plan only when the model is confident (the tool description says so), a setting
`ActionPlanEnabled` (default on for combat, off for rewards), and a first release limited to `play_card` +
`use_potion` + a trailing `end_turn`.

**Impact**: `play_card` at 2.33 requests / 28.9 k tokens per card becomes ~0.35 requests / ~4 k tokens amortized
(a 3–4 card turn = 1 request ≈ 11 k tokens instead of 4 requests ≈ 116 k). Combat is 170/305 of decisions, so
**−60 % to −70 % of all requests** on a normal run, and a combat turn resolves in one round trip.

### 4.4 Compact-state diet

Per-screen projection instead of "everything, always" (`GameStateService.AgentView.cs`,
`BuildAgentViewPayload`). Additive-only (no field is removed from the *raw* payload; the compact view keeps its
keys but with screen-appropriate emptiness, exactly as it already does for `combat`/`reward`):

1. **`map` outside MAP**: emit `{open, current, available_nodes, votes}` (63 chars) and drop `nodes` unless
   `screen == "MAP"`; when the agent is on MAP, keep `nodes` (the route *is* the decision) but drop `visited`
   nodes' `children`. Saves **4,800 chars ≈ 1,400 tokens on every non-MAP decision**.
2. **`run.players`**: in single-player emit ids/hp/gold only (the local player's deck/relics/potions are already
   at `run.*`); keep the full block only when `player_count > 1`. Saves **1,200–1,765 chars**.
3. **`run.piles`**: omit in combat (identical to `combat.draw/discard/exhaust`; `BuildAgentRunPayload` and
   `BuildAgentCombatPayload` read the same `PlayerCombatState` piles). Saves **≈936 chars**.
4. **`action_readiness`**: null outside combat; inside combat keep the eight fields a decision reads
   (`can_use_combat_actions`, `reason`, `running_action_type`, `modal_open`, `modal_type`,
   `player_actions_disabled`, `hand_in_card_selection`, `local_turn_ready`) and drop the eleven always-false
   bookkeeping flags. Saves **≈400 chars**.
5. **`combat.card_value_note`** (227 chars, constant) → move into `PlayPrompt`'s static prefix (it is guidance, not
   state): saves 227 tokens-carrying chars per combat decision and puts it where the provider can cache it.
6. **`combat.hand[]` null-pruning**: emit `why`/`unplayable_reason`/`unplayable_reason_raw`/
   `unplayable_preventer_*` only when the card is unplayable; `target`/`targets` only when it requires a target;
   `keywords`/`mods` only when non-empty. Saves **~150–200 chars per card** (≈750–1,000 per 5-card hand).
7. Optional: a `detail=combat|reward|map|…` query/argument so an external client can still ask for everything.

Expected: a COMBAT frame 14.9 k → ≈7.5 k chars (**−50 %**), a REWARD frame 10.4 k → ≈4.5 k (**−57 %**); on the
whole prompt that is **≈−2,600 of 11,380 tokens per request (−23 %)**.

Invariants: `docs/api.md` rows for every changed compact field, `api-facts` gate; `CompactViewFidelityTests` pins
that decision-critical keys exist — update the pins deliberately (they assert presence of `powers`, not of
`relic_descriptions`); `ProjectMap` prose for `GameStateService.AgentView.cs`; `AgentViewVersion` bump to 13
(clients see the version).

### 4.5 Static-prefix trim + play tool surface

- `act`'s `raw_state` argument is a route/tool concern (`ActAsync(rawState)`), not a decision concern: keep it in
  the schema but move its 200-char description into a one-liner. Same for `x`/`y`/`tool` (Crystal Sphere only) —
  group them behind one "crystal_sphere" note. ≈−400 chars.
- Drop `get_raw_game_state`, `get_game_data_items`, `get_available_actions` from the in-game play list (§4.1),
  ≈−700 chars of schema.
- The 3,409-token `PlaySystem` is 45 % of the *contract* text; audit it against what the model actually needs at a
  decision point (the play contract currently doubles as the MCP skill's documentation). Moving the "how to drive
  each screen" prose to the per-screen slices — where it is only paid on that screen — is the honest split. Target
  −1,000 tokens of the static prefix.
- **Cache hygiene**: nothing volatile before the state (already true); add a `cache_control`-style optional field
  only if a provider needs it, behind a setting. Verify with a live `usage.prompt_tokens_details.cached_tokens`
  read (additive log line in `/health` or the decision entry) that the prefix is actually being cached.

### 4.6 Truncation robustness

1. **Capability memo per model fingerprint.** `ModelRoleTestRecord` / `AgentSettings.ModelTests` already carry a
   fingerprint and a `CapabilityStatus`. Add `TruncatesStreamedToolArgs` (nullable bool) written when
   `LooksLikeTruncatedStreamedAct` fires; `PlayWithModelAsync` then starts that model's act turns with
   `stream:false`. Stops re-paying the wasted request every turn on a bad gateway (log shows each turn currently
   pays it, because `toolStreamFallbackTried` is per-turn).
2. **Content-JSON fallback**, not just for tool-less models: when `act` arguments are truncated *and* the
   completion's `Content` parses via `ActJsonParser`, use it (append `PlayPrompt.JsonActFallback` to `messages[0]`
   and retry once with `tools: null`). Gateways that rewrite tool arguments but pass `content` through are then
   still playable — a materially better outcome than "swap models".
3. **Unique-index resolution.** `ActIndexValidator.RequiresIndex(action)` is true whenever the action *can* take an
   index; that makes `{"action":"play_card"}` always look truncated. Add
   `ActIndexValidator.ResolveUniqueIndex(action, snapshot)` → the index when the action's list has exactly one
   legal/playable entry (one playable card, one available node, one enabled event option). Then
   `{"action":"play_card"}` with a single playable card *works* instead of failing; with two, it still fails and
   the truncation path runs. Same for `end_turn`/`proceed` (already index-free).
4. **Flat/robust argument form**: put the single most decisive field first and keep the object one level deep
   (`{"action":…,"card_index":…,"target_index":…}`), which it already is; do **not** nest `{"args":{…}}`
   (a truncation after `args` would lose everything). Document the truncation signature in
   `docs/model-compatibility-matrix.md` and the compatibility note in `CHANGELOG.md`'s Unreleased section.

Invariants: `ActIndexValidator` is contract-tested (`AgentLoopTests.RejectsMissingAndStaleIndexes`,
`StructuredIndexRejections`); a unique-resolution helper goes beside it and gets its own offline tests;
`ActionSurfaceContractTests` is untouched because availability still comes from `EnumerateAvailableActions`.

### 4.7 Cheap readiness probe for the wait loop

Replace `BuildStatePayload()` inside `WaitUntilActionableAsync` with a readiness-only read:
`GameStateService.CurrentScreenName()` (documented cheap) plus a new `GameStateService.ReadCombatReadiness()`
that builds just the readiness/`available_actions` fragment (the same `EnumerateAvailableActions` walk, no map,
no deck, no party inventory, no glossary). Keep the full build only when the probe says "actionable" (once).
Also give `GameBridge`'s state reads a real deadline (`GetCompactStateJsonAsync` → add an
`ActionableProbeBudget`-style timeout) so a stalled game thread fails the turn visibly instead of hanging.

Expected: removes ~60 full state builds per second of waiting from the game thread; player-visible as no more
micro-stutter while the agent waits, and slightly faster act→next-decision.

### 4.8 Combined target

For the same 305-decision stretch (measured baseline: 662 requests / 7.53 M tokens):

| Change | Requests | Tokens |
|---|---|---|
| baseline | 662 | 7.53 M |
| 4.1 one request per decision | ≈320 | ≈3.6 M |
| 4.2 forced-move resolver (−23 % decisions) | ≈250 | ≈2.8 M |
| 4.4 state diet (−23 % prompt) | ≈250 | ≈2.2 M |
| 4.5 prefix trim (−5 %) | ≈250 | ≈2.1 M |
| 4.3 whole-turn plans (combat −65 %) | **≈140** | **≈1.3 M** |

i.e. **−75 % to −80 % cost and ~4× fewer model round trips**, with the same decision quality, before any model
swap. Latency: a combat turn goes from 4 sequential round trips to 1; a forced move from ~10 s to ~0 s.

---

## 5. Risks / unknowns

1. **Why 2.17 requests/decision is not yet attributed.** The split between the tool-less stream retry and the
   read-tool round is inferred from the code, not measured. §4.1 step 1 exists precisely to measure it; do not
   ship the `stream:false` default before that data lands, or a legitimate streaming-only provider could regress.
2. **No live calibration for a full run.** `release-v0.17.0` records that the full-run request/token calibration
   was blocked by the truncating gateway (`O1` not run). Every per-run number here is either the 52-request
   sample, the local 305-decision ledger, or an estimate. The 1500-request default must be re-derived after §4.
3. **Plan batching and randomness.** Slay the Spire 2 has draw, random targets, random costs and death mid-plan.
   The design stops on divergence instead of predicting, so the risk is *wasted plan steps* (a plan that stops
   after step 2 costs one request and delivers 2 actions — still a win) and *stopping too eagerly* (a plan that
   always stops after one step is just the status quo). Needs a live metric: `plan_steps_executed / requested`.
4. **Acting without the model can be wrong in ways the model would have avoided.** The forced-move rules are
   deliberately narrow (uniqueness), but two deserve scrutiny: (a) `end_turn` when nothing is playable is
   *legally* forced but can still be tactically wrong if the player could have used a potion — the rule must
   check potions; (b) `collect_rewards_and_proceed` skips a card reward by design, so it must only fire when no
   card choice is pending (`reward.pending_card_choice`), never on a first pass.
5. **Prompt-shape changes are behaviour changes.** The contract text is the model's whole playbook; trimming
   §4.5 risks a quality drop that only a live A/B can detect. Keep a `PlayPrompt` variant behind a setting for one
   release so the two can be compared on the same seeds.
6. **Contract tests will fight every change** — deliberately. Files to update together: `docs/project-map.md`
   (new files and flows, `ProjectMap.EverySourceMapped`), `docs/api.md` (+`docs/openapi.json`) for every new
   payload/decision field, `SourceShapeContractTests` line budgets (**budgets go down, never up**: put new logic in
   new files), `SourceCoverageTests.KnownUncompiledSources` for new game-dependent sources,
   `CompactViewFidelityTests`, `McpPlayerSkillTests`, `PlaybookSectionsTests`, `DecisionLogTests`,
   `SessionBudgetGuardTests`, `docs/adr/0002` parity (`test_native_tool_alignment.py`) if any tool changes shape.
7. **Localization.** Every new player-visible string (shortcut reason, plan-stopped reason, stop messages) needs
   its Chinese key + English entry in `Localization/Loc.Strings.*.cs` (`LocalizationTests`), and `PlayerLabels`
   needs labels for the new decision sources, or the overlay shows raw identifiers — a regression v0.17.0
   specifically fixed.
8. **The accounting invariant is easy to break.** A plan that records per-step decision rows *and* per-step
   `requests_spent` would double-count in `/health.session_requests` and in the run spend
   (`RunSpend`). `AccountTurn` is the single writer; any new row must carry `requests_spent = 0` unless it is the
   row that owns the request.
9. **Unknowns worth one live pass each**: (a) does the compact-state diet change live model quality? (b) does
   `stream:false` change latency for a thinking model (non-streaming may return only after the whole thought)?
   (c) do providers cache a prefix that contains a 4 k-token JSON blob at the end? (d) does Jev's option list
   dominate its prompt in late-act combats (255 options)?

---

## Appendix: evidence index

| Fact | Source |
|---|---|
| 2.17 requests / 24,700 tokens per decision; 662 requests / 7,533,746 tokens over 305 `agent_loop` decisions; per-action table | `build/isolated-agent/decisions.jsonl` (gitignored local artifact; script-measured) |
| 11,380 tokens per request | same ledger (7,533,746 / 662) |
| 52 requests / 622,864 tokens (prompt 585,244, completion 37,620) for 45 decisions, floors 1–3 | `docs/live-validation-checklist.md` |
| ≥105 requests / 901,646 tokens for 54 LLM decisions; 22 Jev / 88,451 | `docs/live-validation-2026-09-23-results.md` |
| Default request cap 30 → 1500 "≈ one full run"; 30 ≈ one combat; 800–1300 per run | `CHANGELOG.md` (0.17.0 Changed) |
| Streamed tool args truncated to `{"action":"play_card"}`; non-streaming also truncated; mitigation only | `CHANGELOG.md` (0.17.0 Fixed), `history/release-v0.17.0_2026-10-28.md` |
| Compact frame sizes, `map.nodes` 4,831 chars / 64 nodes, `run.players` 1,763, duplicate piles | `build/live-2026-10-27/a3-combat-readiness.json`, `a2-combat-ready.json`, `a4-reward-5.json`, `a4-step5-map-final.json` |
| Raw `/state` ≈4,000–9,500 tokens | `docs/api.md` |
| `PlaySystem` 11,930 chars / contract 11,146 chars; playbook slices; strategy slices | `skills/sts2-mcp-player/SKILL.md`, `references/*.md`, measured |
| Prompt order / cache contract, Jev branch, vision, memory | `STS2AIAgent/Agent/AgentLoop.Decision.cs` |
| Tool rounds, stream demotion, truncated-act detection, act execution | `STS2AIAgent/Agent/AgentLoop.cs`, `AgentLoop.Arguments.cs`, `ActIndexValidator.cs` |
| Wait budgets and backoff | `AgentRuntime.cs`, `AutoPlayRecovery.cs`, `GameBridge.cs`, `GameThread.cs`, `NoProgressPolicy.cs`, `OpenAiCompatibleClient.cs` |
| Existing no-model paths | `AgentRuntime.TryCompanionImmediateAsync`, `CompanionPlayPolicy`, `AgentLoop.Sandpit.cs`, `RewardChoicePolicy`, `DrainRewardFlowAsync` |
| Index spaces that a plan must not use | `docs/project-map.md` §5, `ActIndexValidator.OptionPaths` |
| Invariants and change checklist | `AGENTS.md`, `docs/project-map.md` §2, `docs/adr/0001-single-action-surface.md`, `docs/adr/0002-two-mcp-surfaces.md` |
