# Reliability of Auto-Play, LLM-Provider Robustness, Co-op Teammate, and External-MCP Experience

Status: **complete draft** — read-only investigation against v0.17.0 (branch `dev`, tag `fdc50fd`).
Author: research agent for task `10-28-bold-experience-overhaul`.
Scope: why auto-play stops, why providers break, what co-op and external MCP clients actually feel.

> Method note: every "observed" claim below is tied to a document, a build report, a live-round report or a
> GitHub issue in this repo. Where the only evidence is code plus an offline test, the row says so. No live
> test was run, no model was called, no file outside this report was touched.

---

## 1. Observed failure table

Frequency is stated as evidence counts, not rates: this project's live evidence is one session per round
(2026-09-13 → 2026-10-28), so "1/1 sessions on that gateway" is the honest unit.

| # | Failure mode | Evidence (doc / date) | Frequency signals | Current mitigation | Residual problem |
|---|---|---|---|---|---|
| F1 | **Gateway truncates streamed tool arguments** to `{"action":"play_card"}` and ends the stream with `finish_reason=tool_calls`; the non-streaming body of the same request is truncated too | `history/release-v0.17.0_2026-10-28.md:24` ("流式与非流式都只回 … 连续 3 次后自动停止"), `CHANGELOG.md:46`, `AgentLoop.Arguments.cs:43-55` (comment says "measured live 2026-10-28") | 1 gateway, both stream modes, 3 turns in a row → run stopped; blocked live items O1 and M2 | One per-turn stream demotion (`AgentLoop.cs:535-545`) plus a second one driven by the validator error (`AgentLoop.cs:605-611`); a missing index is treated as truncation | Demotion is **per turn**, not per model: every new turn streams again and re-pays a wasted request. A third truncation shape in the same turn, or truncation that keeps `card_index` but drops `target_index` on a card whose `targets` list is empty, still lands on the 3-failure stop. Recognised as "缓解，非根治" in the release note |
| F2 | **Three consecutive failed decisions stop the run** with `stop_kind=failed` | `AutoPlayRecovery.cs:134-140` | Every stop of this class in every round (F1, F3, F10, F12 all funnel here) | 2 s / 4 s backoff between failures (`AutoPlayRecovery.cs:140`) | Any cause — including ones the program could fix itself — spends the same 3-token budget. Nothing distinguishes "endpoint is dead" from "the model guessed a stale index once too often" |
| F3 | **Play requests carry no output-token cap at all**, so a thinking model can spend the provider's default window on `reasoning_content` and return `finish_reason=length` with no tool call | `OpenAiCompatibleClient.cs:61-91` (body has only model/messages/tools/reasoning_effort/thinking/stream); `docs/model-compatibility-matrix.md:16` ("Only the connectivity ping sets a cap today") | 5 consecutive turns allowed (`NoProgressPolicy.cs:32`) before a stop; no live case recorded, mechanism is code-verified | `CompletionErrors.IsReasoningBudgetExhausted` + dedicated retry lane with backoff; the stop message tells the player to lower reasoning intensity or raise the provider budget | The player has **no UI control** that raises the budget, because the app never sends one. The advice points at a setting that does not exist in this product |
| F4 | **A game window that stops pumping frames stops the run as a "network" failure** | `AgentRuntime.cs:865-869` (15 s `TurnGameThreadBudget`), `GameThread.cs:110` (`TimeoutException("The game thread did not run the posted action in time.")`), `AutoPlayRecovery.cs:195` (`ex.GetType().Name + ": " + ex.Message`), `StopKindPolicy.cs:70-82` (`"Timeout"` ⇒ `network`) | Mechanism only; `docs/validation-severe-usability-2026-09-23.md:47` (A7) was written to test exactly this and was never run | The 15 s deadline makes pause land instead of wedging the loop (v0.16.0 "pause works when the window stops rendering") | The timeout is thrown **into the retry budget** rather than the wait budget, so ~45 s of occlusion (3 × 15 s) can stop the run, and the player is told "network" instead of "the game window stopped rendering". `WaitingForGameLimit` (150 s) never gets a chance to apply |
| F5 | **Waiting for the game is capped at 150 s wall clock** | `NoProgressPolicy.cs:34-40`, `AutoPlayRecovery.cs:39-55` | Designed after "a wait that never ends is indistinguishable from a hang"; no round has recorded hitting it (A8 was NOT_RUN) | Wall-clock rather than per-turn; `WaitingForPlayer`/`WaitingForCombat` exempt | A genuinely slow screen (long FTUE chain, 4-player load, boss-death animation) is a stop, not a wait. The clock starts on the *first* waiting turn and never re-arms while the screen keeps changing |
| F6 | **Companion invite silently does nothing for 8 minutes** (solo mode, or Steam off) | `CHANGELOG.md:84` (v0.16.1, reproduced live 2026-09-24), `docs/live-validation-2026-09-23-results.md:73` (Steam-off, `dual_status=尚未启动双开`, "后台任务无声消失") | 2 independent sessions | v0.16.1 records a `Rejected` outcome with a readable reason and answers 409 | Steam-off still returns `pending` and no window: the answer is correct for "wrong mode" and wrong for "this environment cannot host a lobby". `#211` reports the same shape from the player side |
| F7 | **The companion's own HTTP API never starts on the external-takeover route**, so every `POST /action` for the teammate seat is refused `forbidden_actor` | GitHub issue **#211** (0.16.2, 2026-10) | 1 user, reproducible; matches "`sts2-ai-agent-companion-<pid>.port` never created" | None; `/health.companion` stays `null`, which is at least honest | This is the *documented* external-agent co-op route (`live-validation-checklist.md:524-548`, issue #85). A whole advertised workflow is unusable and the user has no way to tell why |
| F8 | **`wait_until_actionable(timeout_seconds=20)` hangs far past its deadline**, especially with the window in the background | GitHub issue **#6** | 1 report; the analysis in the issue (check state first, absolute deadline, heartbeat revives the socket) matches `server.py:200-227` | The sidecar now polls first and falls back to polling on signature change; a hard `timeout` is clamped (`ToolTimeout`) | The event-stream branch can still be kept alive by heartbeats; the *mod-side* wait is 5 s per probe inside a 20 s loop (`GameBridge.WaitUntilActionableAsync`), so three probes can overshoot |
| F9 | **Codex/opencode stop after 2–3 minutes because the prompt asks for an autonomous loop** — the client's own safety layer cancels the tool call | GitHub issue **#38** (verbatim policy message: "requests long-running, autonomous tool invocation … steer the model to seek confirmation") | 2 clients (Codex, opencode); the maintainer's own conclusion is "不应当推荐 codex 这种 coding agent 用于此项目" | The repo's answer is skill text: planner-role guidance (`SKILL.md:235-244`) and "do not act while a loop is running" (`SKILL.md:224-227`) | Nothing in the *product* offers a non-loop shape. A client that refuses loops must abandon the guided workflow, and the documented loop itself asks for "three or four calls" per decision (`SKILL.md:177-179`) |
| F10 | **Index rejections are correctable but still burn the failure budget when the turn ends on them** | `ActIndexValidator.cs:97-216` (returns `field`/`submitted`/`valid_indices`), `AgentLoop.cs:571-573` (the rejection goes back to the model as the tool result), `AgentLoop.cs:629-643` (round limit → error) | Any turn whose last round is a rejected index | The error is fed back **inside the same turn** (up to 8 tool rounds, `AgentLoop.cs:11`), and the model can correct it | If the 8-round budget is spent on corrections, or the rejection arrives on the final round, the whole turn counts as one failure and nothing was learned across turns |
| F11 | **Prod budget stop is indistinguishable from a failure in the log viewer's eyes until you read `stop_kind`** | `docs/live-validation-2026-09-23-results.md:61`, `PlayerFacingSession`/`AgentRuntime.cs:1124-1125` | Fixed in 0.17.0 (title bar "需处理", `stop_kind` shown) | Typed stop kinds (`StopKindPolicy`) + overlay surfacing | Message-matching fallback still misfires on new wording: `StopKindPolicy` matches substrings (`"connection"`, `"timeout"`), and F4 shows a real misclassification |
| F12 | **Token/session cap stops a run mid-act, with the plan lost** | `docs/live-validation-2026-09-23-results.md:61` (70/70 requests), `PRODUCT_PLAN_CURRENT.md:9-29` | Every capped session | Budget checked before dispatch (`AgentLoop.cs:389-405`), stop kind `budget`, "清零计数" button | Restarting is manual and the model re-derives the plan from a cold start; no "top up and continue this turn" |
| F13 | **A second Jev `end_turn`/low-confidence streak looks like a stall and has no automatic rescue** | `docs/live-validation-2026-09-23-results.md:35-39` (5 consecutive `end_turn`, confidence 0.36–0.52) | 1 in 22 Jev decisions in that run | Nothing; dual-layer only falls **Jev → LLM**, never LLM → Jev | The player cannot ask "try the other brain" without stopping and reconfiguring |
| F14 | **Teammate replies can take minutes or not arrive at all, with no in-flight feedback** | `AgentRuntime.Team.cs:107` (3 min deadline), `TeamConversation.cs` cap, `AgentRuntime.Team.cs:85` ("若它正在行动，会在本次行动完成后回复") | Expected behaviour by design; no round measured delivery latency | Turn gate + 3 min deadline + status line | The human waits up to 3 minutes with a static "消息正在送往队友". No "teammate is in combat, will answer after this fight" state, no cancellation, no queue position |
| F15 | **Garbled Chinese status text inside raw API payloads** | `build/live-2026-10-27/report.md:87` ("部分中文状态消息在 API 原始数据呈替换字符"), `PRODUCT_PLAN_CURRENT.md:29` ("尚未确认是否为新增回归") | 1 round, unclassified | None; codes and paths survive | An external agent reading `/state` strings gets mojibake; nobody has decided whether it is a regression |

---

## 2. Mechanisms today (paths + symbols)

### 2.1 The auto-play turn and its stop machinery

| File · symbol | What it does | Limit / constant |
|---|---|---|
| `STS2AIAgent/Agent/AgentRuntime.cs` · `AutoPlayLoopAsync` (844-928) | One loop iteration: game-thread snapshot (15 s `TurnGameThreadBudget`, 934) → `CurrentRunBoundary.Check` → `ObserveSessionStateSnapshot` → proactive-chat moment → non-combat gate → `TryCompanionImmediateAsync` → `_loop.PlayOnceAsync` → strategy observation | — |
| `AutoPlayRecovery.RunAsync` (143-216) | Wraps the turn; converts thrown exceptions into `AgentTurnResult.Error`; commits the receipt once (`Commit`); applies `Observe`; enforces budget before and after the turn; holds `turnGate` | — |
| `AutoPlayRecovery.Observe` (18-141) | The whole retry/no-progress policy: configuration stop, wait streak, unsettled streak, reasoning-budget streak, repeat streak, failure streak | see next rows |
| `NoProgressPolicy` | Constants and pure predicates | `RepeatThreshold=3`, `UnsettledLimit=5`, `ReasoningBudgetLimit=5`, `WaitingForGameLimit=150 s` (19/25/32/40); fingerprint = SHA-256 first 16 hex (73-82) |
| `StopKindPolicy` | Maps a stop message to `budget / run_end / config / network / failed`; `Resolve` prefers a thrower-supplied kind | `Classify` is substring matching (`"timeout"`, `"connection"`, `"HTTP 5"`) plus two exact run-boundary messages |
| `CurrentRunBoundary` | Stops when the session leaves its run or the run id changes; lobby/menu states allowed until a run is entered | `LeftRunMessage`, `RunIdentityChangedMessage` are the only strings `StopKindPolicy` can match for `run_end` |
| `AgentLoop.PlayOnceAsync` (151-223) | Non-combat gate → 20 s `WaitUntilActionableAsync` → sandpit escape → Jev (dual layer) → LLM | 20 s wait is reported as `WaitingForGame`, not as a failure |
| `AgentLoop.CompleteWithToolsAsync` (325-659) | Up to 8 tool rounds; one `act` per turn; stream demotion (2 kinds); JSON-act fallback for toolless models; reasoning echo on tool-call turns | `MaxToolRounds=8`; `toolStreamFallbackTried` is **local to one turn** |
| `AgentLoop.ExecuteActAsync` (818-978) | One snapshot for legality + index validation (`GetActionSnapshotJsonAsync`), sandpit re-check, `ActAsync`, unsettled follow-up (20 s settle wait), fingerprint | Accepted action + lost state ⇒ `status=pending`, no failure spent |
| `ActIndexValidator` | Validates `card_index`/`option_index`/`target_index` against the *same* payload that advertised the action; returns `field`, `submitted`, `valid_field`, `valid_indices`, `locked` | `OptionPaths` map is the single source of "which list judges this action" |

**What a turn costs when things go wrong.** A truncated-stream turn costs 2 requests and ends as 1 failure. Three such turns = 6 requests and a stopped run (F1/F2). Every `act` that the game accepted but never confirmed costs 0 failures but a `2^n` backoff, and 5 of them stop the run (F5 sibling).

### 2.2 Provider robustness

| File · symbol | Behaviour | Fragility |
|---|---|---|
| `Llm/OpenAiCompatibleClient.CompleteAsync` (52-92) | Builds the body: model, messages, tools, `reasoning_effort` or DeepSeek `thinking`, `stream` + `stream_options.include_usage` | **No output-token field** (F3). No `parallel_tool_calls`, no `tool_choice` outside probes |
| `SendCompletionAsync` (192-231) | Two-step demotion loop: any 400/415/422 mentioning `stream` ⇒ retry without streaming; a `max_tokens` "unsupported parameter" 400 ⇒ rename to `max_completion_tokens`. Each at most once, re-classified from the top | Steps are not remembered between requests or between turns; a fresh turn starts at step 0 again |
| `ShouldRetryWithoutStream` (491-499) | Accepts any HTTP 400 because `"HTTP 400"` is always in `FormatError`'s text | Over-broad: **every** 400 demotes the stream first, so a token-field error is answered by a stream demotion on the second attempt |
| `SseCompletionAccumulator` (17-238) | Per-index tool-call accumulation, string-append vs whole-object replace for `arguments`, synthetic `call_N` ids, tolerance for a whole `message` choice, reasoning preview | Cannot detect truncation: a short-but-valid argument object is indistinguishable from a complete one. `Build()` returns `"{}"` when arguments are empty |
| `MaxTokensField` (20-81) | Pure detector + renamer for the cap field | Only exercised by `PingAsync` and `ProbeToolCallingAsync` (103, 134). Since play sends no cap, the branch can never fire during play |
| `ThinkingRequestBuilder` (5-61) | `auto` → infer from model name prefix: `deepseek*` ⇒ deepseek wrapper, `gpt-5|o1|o3|o4|step-5` ⇒ `reasoning_effort`, else prompt suffix | Inference is by name only; a proxied alias silently gets the prompt path, which for a thinking model is the F3 generator |
| `Agent/ActJsonParser` (5-77) | Recovers a JSON action from prose/fences for models without tools | Requires the literal `action` key; a truncated JSON body never parses, and the failure text is generic |
| `Agent/CompletionErrors` (13-43) | Names "empty assistant message", "reasoning spent the budget", `finish_reason=length` | Text only; nothing is retried differently for `length` than for any other empty answer |
| `Config/ModelRoleProbe` (49-372) | Per-role test record: status, **capability** (`supported/unsupported/unverified`), fingerprint over base URL + model + key + protocol + thinking, error classification | Capability is one boolean *for tools*, and it is not consulted when building requests. `Fingerprint` includes the API key, so rotating a key re-asks the same questions |
| `Agent/AgentRuntime.ModelTest.cs` (36-172) | The settings card's test: ping, then a **real** `act` tool probe with `tool_choice=required`, then writes `SupportsTools` from the probe | This is the one place that already knows how to ask a provider what it can do — and it asks only one question |
| `Config/QuickSetup` (29-48) | 9 provider presets + custom: base URL, thinking protocol, suggested models, key URL | Protocol is a static guess per provider; models are documentation snapshots ("没有逐家用真实 Key 实测", `CHANGELOG.md:26`) |

### 2.3 Co-op teammate

| File · symbol | Behaviour | Player-visible effect |
|---|---|---|
| `Multiplayer/LocalDualInstanceLauncher.LaunchCompanionAsync` (108-150) / `LaunchCoreAsync` (152-298) | Single-flight gate; stages the mod, seeds a companion profile and settings, writes `steam_appid.txt`, sets `STS2_AGENT_*` env, starts the child, waits for `/health` | Up to **90 s** of "正在邀请…" with no progress detail (275). A retry cannot start a third window (273) |
| `WaitForHealthAsync` (300-321) | Startup handshake file + port-file candidates + `CompanionHealth.IsExpectedProcess` | Failure text distinguishes "exited (code N)" from "running but unconfirmed"; both ask the player to read logs |
| `CompanionDiscovery.WaitAsync` | One total deadline across candidates | — |
| `DualInstanceCoordinator.RunCompanionBootstrapAsync` (123-196) | 5-minute *machine* budget, re-armed while a human is choosing a character; walks lobby → character → ready → run | 5 min `TimeoutException("Timed out joining the local room.")` is logged, **not** shown to the host |
| `DualInstanceCoordinator.ContinueLocalCoopResultAsync` (65-121) | Save-identity prechecks (`CoopSaveProbe`, `CoopSavePrecheckPolicy`) before `EnableFastMpENetHost` + load, then launches the companion | Prevents the game renaming a mismatched save to `.VAL.corrupt` (issue #89) |
| `Agent/AgentRuntime.Team.cs` (76-158) | Host→teammate message (single-flight), teammate reply with a **3-minute** deadline, shared turn gate, budget gate | "消息正在送往队友；若它正在行动，会在本次行动完成后回复。" |
| `Multiplayer/CompanionPlayPolicy` (44-111) | Pure immediate decisions: confirm an FTUE modal; follow the human's map vote, else wait | "等待你选择地图节点，随后投同一格。" — the deliberate `WaitingForPlayer` exemption from the 150 s clock |
| `Multiplayer/TeammateStatus` (40-146) | Host parses the companion's own `/state` for HP/block/energy/hand; never throws | One line in the party panel; refreshed at most every 2 s, fire-and-forget (`AgentRuntime.Team.cs:34-63`) |
| `Agent/ProactiveChatPolicy` (26-96) | Opt-in, combat start/end moment, ≤6 messages/session, ≥75 s apart, budget-gated | The AI speaks first at most a handful of times per session |

### 2.4 External MCP

| File · symbol | Behaviour | Friction |
|---|---|---|
| `Server/NativeMcpServer.cs`, `NativeMcpServer.Tools.cs` (27-76) | Streamable-HTTP MCP on `/mcp`, 16 tools, default **off**, `POST /mcp/control` toggles it without restart | Must be enabled before a client can see anything; the port is the mod's own (8080 → next free) |
| `mcp_server/src/sts2_mcp/server.py` · `create_server` | Same tool face; profiles `guided` (17) / `layered` / `full` (legacy per-action tools) | Two surfaces must be kept in lockstep (ADR 0002, `test_native_tool_alignment.py`) |
| `server.py` · `decide` (298-306) | One call returns `state` + `available_actions` + `scene_guidance` | The intended fix for "three or four calls per decision" (`SKILL.md:177-179`), but nothing forces a client to prefer it |
| `server.py` · `wait_until_actionable` (549-560) + `_wait_until_actionable_impl` (200-227) | Event stream, then polling fallback, signature-change escape | Issue #6: no hard deadline guarantee; background window worsens it |
| `server.py` · `act` (562-671) | One action at a time, returns the next compact state, structured index errors, `outcome_unknown` + one reconciliation | **No batch/multi-action tool anywhere** (grep for batch/sequence/loop finds only prose) |
| `client.py` | Instance discovery by PID/role/port hints, explicit base URL wins | Discovery could time out on stale instance files — fixed in 0.17.0 (`CHANGELOG.md:51`) |
| `skills/sts2-mcp-player/SKILL.md` | State-first decision rules, per-screen playbooks, "check `play_running` before `act`", planner role | The guided narrative is a **loop** ("keep going until…"), which is exactly what Codex's safety layer blocks (F9) |

---

## 3. Pain points ranked

Ranked by "how bad does this feel to a player who just wants to watch the game play itself", with the
engineering cost as a tiebreak.

**P0-1 — The run stops for reasons the program could fix itself.** Every stop funnels through one 3-strike
counter (`AutoPlayRecovery.cs:134-140`), and the counter does not know *why*. The 2026-10-28 gateway case is
the archetype: the provider is reachable, the model answered, the game is fine, and the player gets
"连续 3 次决策未成功". A stopped run also costs the plan, the Jev strategy and the session's context.

**P0-2 — Nothing in the product remembers what a provider does.** The one place that could — the per-model
test — asks a single question (`ProbeToolCallingAsync`) and stores one boolean. Every fragile difference
(streamed arguments intact? parallel tool calls? reasoning echo mandatory? which token-cap field? does
`tool_choice` work? does a 400 mean stream or field?) is re-learned from the error, one request at a time,
and then forgotten when the turn ends. This is why F1 needed a per-turn hack instead of a one-line policy.

**P0-3 — A player who looks away gets a failure, not a pause.** Occlusion throws a `TimeoutException`, which
is charged to the retry budget and classified as `network` (F4). The 150 s wait budget, the piece of
machinery built for exactly this, never runs because the exception is thrown one layer above it.

**P0-4 — "Stopped" has no next step in the product, only in the message.** Stop messages are good prose
("检查当前局面后可手动继续") but the overlay offers a generic resume; nothing says "this endpoint truncates
tool arguments; switch to mode X" or "retry after 40 s". F3 is worse: it advises raising a budget the app
never sends.

**P1-1 — Co-op's failure surface is the emptiest screen in the product.** 90 s with no progress; a Steam-off
invite that resolves to `pending` forever; a bootstrap timeout that only reaches the log; a 3-minute message
deadline with a static status; and #211, where the advertised external-takeover route has no companion API at
all and the host's `/health` just says `companion: null`.

**P1-2 — External-agent workflows are shaped to trigger client safety limits.** The recommended flow is a
loop; Codex and opencode refuse loops (#38). The two fixes already exist conceptually (mod-side autoplay,
`step`, planner role) but the skill still narrates a loop and there is no one-call "advance one turn" tool.

**P1-3 — The evidence loop is broken by design.** The 2026-09-23 report states plainly that raw prompts, SSE
deltas and full provider responses are not persisted (`docs/live-validation-2026-09-23-results.md:28`), so
every diagnosis in this table had to be reconstructed from 79 accepted decisions. That is why "是否新增回归"
is still open for F15 and why F1 took a release to characterise.

**P2-1 — Waiting machinery is turn-shaped, not progress-shaped.** `UnsettledLimit=5`, `RepeatThreshold=3`,
`WaitingForGameLimit=150 s` are all counters, not evidence of forward motion. A slow-but-moving screen and a
frozen screen look the same until the counter runs out.

**P2-2 — Message-matching classification.** `StopKindPolicy` will keep producing one misclassification per new
wording (F4 today). Typed kinds exist and are used for budget/run-end; the rest is still text.

---

## 4. Bold proposals (ranked by impact / effort / risk)

Each sketch names the files it touches and how it keeps the invariants: game thread only, bounded waits,
no action replay except `action_in_flight`, one request = one accounting, budget before dispatch, ADR 0001
(one action surface), ADR 0002 (two MCP surfaces), additive API, `Loc.T` with English entries.

### 4.1 **Provider capability profile** — ask once, remember, apply automatically (impact ★★★★★, effort ★★★, risk ★★)

*Problem solved:* F1 (root cause), F3, and most of the "provider is weird" class in §2.2.

*Sketch.*
1. Extend the existing per-model test (`AgentRuntime.ModelTest.cs:74-85`) into a **capability interview** of
   2–3 cheap requests, reusing the `act` tool already in use:
   - streamed `act` probe with an action that *requires* an index (`end_turn` is useless here — use a
     `play_card`-shaped argument) → compare streamed arguments with the non-streamed body;
   - one request with two `act` calls in `parallel_tool_calls` → does the provider honour or reject it;
   - one request with a deliberately truncated-looking `max_tokens`/`max_completion_tokens` (existing
     `MaxTokensField.IsUnsupportedParameterError` supplies the detector) → which field it accepts;
   - optionally a tool-call turn followed by a second tool round → is the reasoning echo mandatory
     (this is exactly the DeepSeek/Kimi 400 the client already guards with `LlmMessage.Assistant(..., reasoning)`).
2. Persist the answers where the fingerprint already lives: extend `ModelTestRecord`
   (`Config/AgentSettings.cs:461-476`) with a `Capabilities` object — `stream_tool_args`,
   `parallel_tool_calls`, `max_tokens_field`, `reasoning_echo`, `tool_choice_supported`, `probe_version`.
   Additive JSON, old files load unchanged, `ModelRoleProbe.Fingerprint` gets `probe-v3` so old records
   invalidate cleanly.
3. Consume it in `AgentLoop.CompleteWithToolsAsync` and `OpenAiCompatibleClient.CompleteAsync`:
   `Stream = caps.StreamToolArgs && !caps.KnownTruncating`; `body[capField] = ConfiguredOutputBudget`;
   `parallel_tool_calls=false` when unsupported; omit/repeat the reasoning echo per the profile.
4. Make the runtime **learn at play time too**: when `LooksLikeTruncatedStreamedAct` fires twice for the
   same endpoint fingerprint, write `stream_tool_args=false` into the persisted record (one write, on the
   settings thread, not per turn) and surface it in the model card as "此服务商的流式工具参数不完整，已改用非流式".
   That single line converts the current silent 2× cost into a permanent fix.
5. UI: the model card gains one collapsed line ("能力：流式参数 ✓ / 并行调用 ✗ / 输出预算字段 max_completion_tokens"),
   localised with `Loc.T`, and the settings page's existing badge logic (`IsModelVerified`) keeps working.

*Invariants:* detection is a probe, not an action — it never touches the game; each probe request is counted
once and goes through `SessionBudgetGuard.CheckBudget` in the existing test path; the profile is a *request
shape*, so ADR 0001 is untouched; nothing is replayed.

*Why bold:* it turns every provider quirk from a runtime surprise into a stored fact, and it is the only
proposal that removes the F1 class instead of absorbing it.

### 4.2 **Self-healing decision ladder instead of a 3-strike stop** (impact ★★★★★, effort ★★★, risk ★★★)

*Problem solved:* F2, F1's tail, F10, F13.

*Sketch.* Replace "count failures" with "classify, then try the cheapest repair once each, in order, and only
count what survives the ladder".

```
turn fails
 ├─ transport (timeout/5xx/429, no HTTP answer)      → wait retry-after or backoff, NEVER count as a decision failure
 ├─ config (4xx except 408/429)                       → stop (config) — the player must fix a key/model
 ├─ arguments (truncated / missing index / bad JSON)  → 1) re-read state  2) demote stream  3) JSON-act mode
 │                                                      4) feed ActIndexError.details back verbatim
 ├─ empty answer (no content, no tool call)           → 1) add "call act exactly once" nudge  2) raise output budget once
 └─ actionable-but-illegal (action not in list)       → re-read state, publish available_actions, retry once
```

Concretely:
- Add `DecisionFailureKind` to `AgentTurnResult` (a typed field beside `Error`, populated where the error is
  *created*: `LlmException.StatusCode`, `ActIndexError.Code`, `CompletionErrors`), and let
  `AutoPlayRecovery.Observe` switch on it instead of on the message. This also retires most of
  `StopKindPolicy.Classify`'s substring matching (P2-2).
- Keep a small per-session `RepairLedger` (a dictionary of "repair already used for this streak") so each
  repair is tried at most once per streak, and the 3-failure cut applies only to failures that the ladder
  could not classify.
- Add the missing **LLM → Jev rescue**: when the LLM path fails and a decider is configured, spend one Jev
  request (`PlayOnceAsync` already has the decider) before declaring the turn failed. This is the symmetric
  half of the existing Jev → LLM fallback and directly answers F13.
- Nothing here replays an action: every repair either re-reads state or issues a *new* decision.

*Invariants:* each repair request goes through the same `requestsSpent++` before dispatch and the same budget
check (`AgentLoop.cs:389-405`); waits stay bounded; the `action_in_flight` exception remains the only
retryable action refusal.

### 4.3 **One-call turn trace, and a "why did it stop" card** (impact ★★★★☆, effort ★★, risk ★)

*Problem solved:* F15, the whole diagnosis loop, and player trust ("it just stopped").

*Sketch.*
- A bounded in-memory ring buffer (last ~12 turns) of a **sanitised** trace: screen, phase, run id, the
  decision (action + indices + reason), requests/tokens for the turn, the failure kind + first 200 chars of
  the error, the repairs attempted, and the fingerprint delta. No prompts, no keys, no raw SSE — reusing
  `DiagnosticExport.Redact`.
- Expose it additively: `GET /diagnostics` (already exists) gains a `recent_turns` section, `/health` gains
  `stop_detail`'s turn index, and both MCP surfaces gain it inside the existing `get_decision_log` envelope
  (`{"decisions": [...], "turns": [...]}` — additive, ADR 0002 applies, tests updated).
- Overlay: the stop banner gets a "为什么停了" expander that renders the last trace line in player language:
  "服务商把工具参数截断了（第 2 次）→ 已改用非流式；第 3 次仍截断，已暂停。建议换模型或开启兼容模式。"
- Persist it into the session file (already redacted) so a stopped run can be exported after the fact — the
  exact gap the 2026-09-23 report describes.

*Invariants:* read-only surface, additive fields (API gate wants a `docs/api.md` row per field), no reasoning
content (the "reasoning is display-only" invariant survives because this is *decisions*, not thoughts).

### 4.4 **Progress-based waits, and a pause that is not a failure** (impact ★★★★☆, effort ★★★, risk ★★)

*Problem solved:* F4, F5, the tail of F8, and P2-1.

*Sketch.*
- Teach `GameThread.InvokeAsync` callers to translate a frame-stall `TimeoutException` into
  `AgentTurnResult { WaitingForGame = true }` at the two call sites that have a natural "the game is not
  rendering" meaning (`AgentRuntime.cs:865-869`, `TryCompanionImmediateAsync` 939-954). The 150 s wall clock
  then governs, the retry budget is untouched, and `stop_kind` can be a new **`paused_for_game`**-shaped
  state rather than `network`. Detect occlusion on purpose: `DisplayServer.WindowGetMode` /
  `OS.LowProcessorUsageMode`-style checks belong in the overlay, not the loop, so the UI can say
  "游戏窗口在后台，已暂停" and resume the moment frames return.
- Replace the flat `WaitingForGameLimit` with an **evidence clock**: keep a `progressFingerprint`; reset the
  wait clock whenever the screen, phase, or fingerprint changes. A 150 s limit that only counts *no change*
  never punishes a slow animation.
- Replace `UnsettledLimit` turns with a settle deadline: if `ActResultJson` says pending, wait (bounded) for
  the fingerprint to change rather than asking the model again. Today a pending act costs a fresh model
  request per retry (`AgentLoop.cs:925-943` already re-reads state — the waste is the extra turn).
- Make every stop resumable: a `stop_kind` in `{failed, network}` renders a **Resume** button that re-enters
  `StartAutoPlay` without resetting the session counters (the 3-strike counter is per `AutoPlayRecovery`
  instance, so this is already true — it just needs a button and copy).

*Invariants:* bounded waits everywhere (`GameTaskBoundingContractTests`); pause still lands because the
timeout stays; no unbounded `await`; game objects only via `GameThread`.

### 4.5 **Co-op session state machine with per-state budgets and honest failure text** (impact ★★★★☆, effort ★★★, risk ★★)

*Problem solved:* F6, F7, F14, and the 90 s / 5 min / 3 min waits.

*Sketch.*
- One `TeamSession` state machine (`Idle → LobbyHosted → ChildLaunching → ChildHealth → ChildJoined →
  Playing ↔ WaitingForHuman → Degraded → Ended`), each state with its own deadline and its own localised
  line, replacing the pile of independent booleans and the 90 s single wait
  (`LocalDualInstanceLauncher.cs:275`). States become `dual_status` values (additive on `/health`).
- **Preflight instead of hope:** before launching, the host asserts the environment can host — Steam on,
  `--force-steam` not off, no second-client conflict, the companion port free — and refuses with a specific
  reason. Steam-off co-op must say "此环境无法联机（Steam 已关闭）", not `pending` (F6).
- **Companion API self-check:** the companion writes its port file *and* the host verifies `/health` reports
  `instance_role=companion` with a live `api_port`; if not, `dual_status` says "队友窗口已入队，但它的
  HTTP 接口没有起来（这是联机外部接管不可用的原因）" and the diagnostics route carries the child's last
  log lines. This is the missing half of #211.
- **Message latency made explicit:** `SendTeamMessageAsync` publishes
  `queued | teammate_in_combat | generating | answered`, with the queue position and the elapsed time, and
  inherits the same 3-minute deadline. Proactive messages (`ProactiveChatPolicy`) reuse the same channel so
  the human sees one continuous conversation instead of two.
- **Turn-latency budget:** measure and display the teammate's per-turn wall clock (`TeammateStatus` already
  reads the companion's `/state` every 2 s; add the turn's elapsed time from `/health`), so "the teammate is
  thinking for 40 s" is visible rather than inferred.

*Invariants:* all cross-process reads bounded and cancellable; the `X-STS2-Companion-Session` token contract
unchanged; the companion keeps its own settings (`CompanionPlaySettings`); `Loc.T` for every new line.

### 4.6 **A non-loop shape for external agents: `step`, and planner-first as the recommended path** (impact ★★★★☆, effort ★★, risk ★★)

*Problem solved:* F9, F8, and the "three or four calls per decision" friction.

*Sketch.*
- Add **`step`** to both MCP surfaces (ADR 0002 — implement in `NativeMcpServer.Tools.cs` *and*
  `server.py`, plus `test_native_tool_alignment.py`): it drives **one** in-process turn
  (`AgentRuntime.PlayOnceAsync` on the mod side, i.e. the mod's own model decides, or `decide` + `act`
  atomically when the caller supplies an action) and returns the decision, its reason, the new compact
  state, and `would_stop` — the failure-ladder verdict for that turn. Bounded by the existing turn gate and
  budget. One tool call = one turn = one accounting, which is the invariant already in place.
- Add **`step_until`**? No. Deliberately not: a server-side loop reintroduces the autonomy trigger inside the
  game process, where the client cannot cancel it. `step` repeated by the *client* is the shape a coding
  agent's policy accepts (each call is an explicit, consented action), and the mod-side `/session/control`
  loop remains the answer for "I want it to play while I watch".
- Rewrite the skill's guided section into two explicit modes — **Mode A: I drive** (`decide` → `act`, one
  at a time, with a per-turn line of narration) and **Mode B: the mod drives, I plan**
  (`/session/control` + `get_planner_briefing` + `update_play_strategy`, no `act` at all). Delete the
  "keep calling in a loop until termination" phrasing that #38 quotes verbatim, and add a one-line note in
  `mcp_server/README.md` and the skill: "some clients cap autonomous loops; use Mode A or B."
- Keep the index-error contract as is (`error.details.valid_indices`) — it is already the best part of this
  surface — but have `step` include the *repair* it applied, so an external agent learns the same lesson the
  in-game loop did.

*Invariants:* one action at a time; no replay; budget before dispatch; the two surfaces stay aligned.

### 4.7 **Batch actions for MCP** (impact ★★☆, effort ★★, risk ★★★★) — *recommend against, for now*

The temptation is an `act_batch` that plays a whole turn's cards in one call (fewer round trips, fewer
"decision" prompts). It collides with three invariants at once: the state each action must be validated
against changes between actions (ADR 0001's single walk), `outcome_unknown` forbids replaying an action whose
response was lost, and a mid-batch failure leaves the client with a partial effect it cannot see. If it is
ever built, it must be `[{action, index}]` with a fresh snapshot before *each* element, a stop-on-first-
non-`completed` rule, and a per-element `outcome_unknown` report — i.e. the same cost as separate calls with
more ways to be wrong. `step` (4.6) delivers most of the latency win without the risk.

### 4.8 **Provider-aware onboarding: one screen that cannot be wrong** (impact ★★★☆, effort ★★, risk ★)

*Problem solved:* F3's advice pointing at a nonexistent control, and the "没有逐家用真实 Key 实测" gap
(`CHANGELOG.md:26`).

- Add a per-preset `OutputBudgetField` and a tested default (e.g. 4096 output tokens for a play turn) to
  `QuickSetup.ProviderPreset`, and actually send it in play requests (`OpenAiCompatibleClient.CompleteAsync`).
  A thinking model then cannot silently eat the whole window (F3).
- When the model test fails, show the *capability interview's* findings, not just the HTTP status: "此端点
  拒绝 tools / 此端点要求 max_completion_tokens / 此端点流式参数不完整（已自动关闭流式）".
- Ship the presets with a `verified_at` stamp and render "未实测" honestly, so a player knows whether the
  suggestion is documentation or evidence.

---

## 5. Risks and unknowns

**Unknowns that change the ranking**
1. **How often does F1 actually happen in the field?** One gateway, one session. If truncation is rare outside
   that gateway, 4.1 is still right but its impact claim drops; the turning point is whether the profile
   survives as a *learned* fact (step 4) rather than only a test-time one.
2. **Is F4 reachable in normal play?** Nobody has run A7. If occlusion of 45 s is common (laptop lid, alt-tab
   to a browser), this is a P0 today. If the 15 s post usually succeeds while occluded (Godot keeps pumping at
   low rate — the 2026-09-23 note says the window still rendered when minimized), it is a rare edge.
3. **Does any provider 400 the second tool round without the reasoning echo?** The mitigation exists
   (`OpenAiCompatibleClient.cs:568-573`), the failure has never been captured with a raw response (A6 remains
   partly unverified). 4.1's fourth probe would settle it.
4. **Is the `SupportsTools=false` JSON-act path good enough to be an automatic fallback?** It exists and is
   pinned by tests, but has never been the *primary* mode of a live run. Making it a repair (4.2) uses it more
   aggressively than today's opt-in.
5. **Co-op turn latency is unmeasured.** No round recorded how long the teammate takes per turn; `TeammateStatus`
   reads state, not timing. 4.5's third bullet depends on that number existing.

**Risks in the proposals themselves**
- **4.1 persistence** touches `AgentSettings` and therefore `SettingsStore` backup/corruption handling and the
  companion settings synchroniser's fingerprint. A capability written mid-run must not race the settings save
  that the overlay's form owns; it also must not invalidate a verified badge (fingerprint must exclude the
  capability block, or `IsModelVerified` will flip on every learning event).
- **4.2 retyping failures** is the largest blast radius in the codebase: `AgentTurnResult.Error` is read by the
  overlay, `/health`, the decision log, the session records and many tests. Additive typed fields plus keeping
  `Error` as the human string is the only backward-compatible route; the delete-everything version would
  violate the additivity rule.
- **4.3 trace** risks becoming a second reasoning store. The invariant is explicit: no `reasoning_content` is
  persisted. The trace must be decisions + errors only, and the redaction path must be the same function, not a
  copy.
- **4.4 `paused_for_game`** adds a `play_phase`/`stop_kind` value that external clients and the Python client's
  typed payloads may not expect. Additive but semantically new: `PLAY_PHASES` in the Python client and
  `docs/api.md` must gain it in the same commit.
- **4.5 `TeamSession`** replaces fields that the overlay, `/health`, the MCP `health_check` consumers and two
  offline harness suites read (`dual_status`, `dual_launch_outcome`, `team_control_status`). The state machine
  must *render into* those fields first and only later retire the internal booleans.
- **4.6 `step`** must not become a second `try/catch`-shaped `/action`: it is a *turn*, so it must respect the
  turn gate, the non-combat gate, the companion's "external takeover" mode, and the `play_running` race the
  skill already warns about. The riskiest part is the interaction with a concurrently running in-game loop —
  `step` should refuse with a clear 409 rather than race.

**What I could not check (read-only limits)**
- No live game, no model call, so F1/F3/F4 frequencies are documented, not measured by me.
- `docs/live-validation-checklist.md` and `PRODUCT_PLAN_CURRENT.md` disagree about which version is "current"
  (0.16.2 vs 0.17.0) — I treated `history/release-v0.17.0_2026-10-28.md` as the newest release record.
- The three `build/live-2026-10-27-r*` rounds are partial by construction (R4 FAIL, R6/C3 NOT_RUN), so
  several rows above rest on a single observation.

---

## Appendix: one-line map of "where to change what"

| To change… | Start at |
|---|---|
| Stop/no-progress policy | `Agent/NoProgressPolicy.cs`, `Agent/AutoPlayRecovery.cs`, `Agent/StopKindPolicy.cs` |
| The decision turn | `Agent/AgentLoop.cs` (`CompleteWithToolsAsync`, `ExecuteActAsync`), `Agent/AgentLoop.Arguments.cs` |
| Request shape per provider | `Llm/OpenAiCompatibleClient.cs`, `Llm/ThinkingRequestBuilder.cs`, `Llm/MaxTokensField.cs` |
| What a provider can do | `Config/ModelRoleProbe.cs`, `Agent/AgentRuntime.ModelTest.cs`, `Config/AgentSettings.cs` (`ModelTestRecord`) |
| Presets / onboarding | `Config/QuickSetup.cs`, `Ui/AgentOverlayHost.Setup.cs` |
| Co-op launch and health | `Multiplayer/LocalDualInstanceLauncher.cs`, `Multiplayer/DualInstanceCoordinator.cs`, `Multiplayer/CompanionDiscovery.cs` |
| Teammate conversation | `Agent/AgentRuntime.Team.cs`, `Multiplayer/TeamConversation.cs`, `Agent/ProactiveChatPolicy.cs` |
| MCP surface (both) | `Server/NativeMcpServer*.cs` + `Agent/AgentTools.cs` **and** `mcp_server/src/sts2_mcp/server.py` (ADR 0002) |
| Skill / external UX | `skills/sts2-mcp-player/SKILL.md`, `docs/live-validation-checklist.md` (co-op + external rows) |
