# Changelog

## Unreleased

Game-dependent compilation succeeded on 2026-10-27 (Release, 0 warnings, 0 errors, after the CS0173 fix below). Only the tested subset passed live acceptance: access hardening, the overlay checks, the potion-slot reward flow, and the Steam-on dual-window no-model takeover and save-continue. Model-backed items (streaming chat, vision answers, teammate auto-play, DeepSeek variants, non-combat-only mode) could not run because of provider errors and were skipped by the maintainer's decision, so they have no live evidence; the Workshop zh-CN description was not verified, the 3-4 player and LAN checks were not run, and old/duplicate local mod copies, artifact fingerprints, the second-library Workshop install, dynamic-port discovery, handshake-failure paths and slow-child reconnection were not reported as checked. See [PRODUCT_PLAN_CURRENT.md](PRODUCT_PLAN_CURRENT.md).

> Release attribution is recorded against tags or release commits. Post-tag maintenance is listed separately; current validation limits are maintained in [PRODUCT_PLAN_CURRENT.md](https://github.com/CharTyr/STS2-Agent/blob/main/PRODUCT_PLAN_CURRENT.md).

### Added

- Compile model verification and companion settings partials directly in an offline runtime harness. Controlled provider responses and submitted-action barriers cover cancellation, single-flight probes, stale results, authentication/tool failures, pause/apply/resume, failed persistence and fault recovery; this does not replace the game build or live co-op acceptance.
- Add provider quick setup, a visible 30-request trial for new installations, and cancellable model testing (45-second deadline). Preserve existing unlimited choices. Require verified tool calling for auto-play.
- Guard identified local co-op saves against native loading under a mismatched player identity. Add diagnostics file saving and a DLL build fingerprint. Update two-tab setup and usage instructions.
- Discover local HTTP instances for Python MCP by verified PID/role/port hints; support `STS2_API_INSTANCE` and reject ambiguous selection. Explicit base URLs retain priority.
- Add an endpoint thinking-protocol selector for proxy/alias compatibility. Invalidate verification after protocol edits, remove the SDK-only extra_body wrapper from raw HTTP, and stop treating authentication or unrelated request errors as lack of tool support.
- Port #203's model-backed map, explicit reward-screen potion discard, non-potion-first bulk rewards and detached upgrade selection previews. Preserve current native selection handling and avoid the old live-card mutation implementation.
- Detect enemy-owned Sandpit countdowns targeting the local player. Built-in autoplay prioritizes a playable Frantic Escape at the last countdown and stops before a lethal end turn if no escape is playable; the dispatch guard rechecks the latest snapshot.
- **原始 `/state` 新增两个只读数值。** `run.card_rarity_odds_value` 是全局的稀有卡保底偏移（卡牌奖励没出稀有就上涨、出了稀有就重置为 -0.05，也叠加到商店卡牌的稀有率；游戏界面不显示它）；`run.card_shop_removals_used` 是本局已用的商店删牌次数。两者只出现在原始 `/state`，不进入 compact `agent_view`。（#204）

### Changed

- Synchronize model, key, budgets and non-combat-only settings to the running companion with explicit acknowledgement and a pause before applying changes.
- Accept both release manifest names; upgrade only unchanged managed companion copies. Preserve manual/edited installations.
- Integrate main's native power display amounts with dev's Sandpit/upgrade/DeepSeek fixes and Windows prompt normalization; include party inventories from #210 with unavailable-vs-empty and melted-relic semantics.
- Validate and atomically stage the actual loaded mod for the offline companion. Refuse partial or differing local copies without overwriting user files. Report initialization failures and the real API port through a host-selected startup handshake; bound discovery by one total deadline and reconnect a live child without spawning another.
- Complete #202's non-combat-only guards with authoritative compact combat ownership, model/strategy request checks and final dispatch checks for companion shortcuts; retain accepted-action accounting. Compact view version is 12.
- Include power rules and structured live card values in combat context, and build card upgrade previews from an upgraded detached copy. Reward, rest and shop guidance now uses actual deck needs and upgrade data.
- Disable thinking only for official DeepSeek connectivity and required-tool capability probes; ordinary play requests retain the configured thinking mode.

### Fixed

- **修复首次真实构建时的 CS0173 编译错误。** `BuildMapPayload` 里按「地图界面是否打开」在 `List<NMapPoint>` 与数组之间取值的条件表达式，编译器无法推断类型；该处只在带游戏程序集的构建中才会编译，离线套件看不到。现改用 `Enumerable.Empty<NMapPoint>()`，行为不变，`BuildMapPayload` 的契约哈希同步更新。
- **药水栏全满时，奖励界面的药水奖励不再假装领取成功。** 此前游戏吞掉点击，`claim_reward` 仍返回 `completed`，奖励列表和药水栏都没变，客户端会误判已领取（#219）。现在满栏时该药水行 `claimable` 为 `false`，`claim_reward` 直接返回 409 `invalid_action`（`details.reason = "potion_slots_full"`）并提示先 `discard_potion` 腾位；奖励主界面的 `discard_potion` 已在本版开放，所以整条「丢弃再领取」路径可走通。验证脚本里「奖励界面不应出现 discard_potion」的陈旧断言一并删除。
- **游戏内 HTTP 接口拒绝来自网页的请求。** 监听器只绑定 127.0.0.1，`IsLocal` 对任何请求都为真，所以浏览器里的网页能跨站 POST（打牌、启停自动游玩、打开 MCP），DNS rebinding 还能读到状态和截图。现在带 `Origin` 头、`Sec-Fetch-Site` 为跨站，或 `Host` 不是本机回环地址加本端口的请求一律返回 403 `untrusted_origin`；本机脚本、curl 和 MCP 客户端不受影响，MCP 路由仍用自己的 Origin 规则。
- **网络版 MCP 不再静默裸奔。** 绑定非回环地址且没有 bearer token 时启动即报错（`--allow-insecure` 或 `STS2_NETWORK_ALLOW_INSECURE=1` 可显式放行）；设了 token 后，匿名访问 `/healthz` 只返回 200/503 与服务名，不再回显上游地址、错误详情和 mod 健康数据。
- **设置文件含 `null` 条目或形状不可用时不再让 mod 起不来。** 加载时丢弃列表里的 `null` 项；任何读取或校验异常都走「备份损坏文件并恢复上次良好配置」的流程，而不是抛出到 `AgentRuntime` 构造函数。
- **诊断导出补全脱敏。** 现在会遮住 `Authorization: Basic/Token …` 的完整凭证、`OPENAI_API_KEY=…` / `secret_key=…` 这类环境变量式名称、URL 编码的 `access_token%3D…`，以及带空格的引号值。
- **`timeout_seconds` 统一解析。** 字符串 `"NaN"`/`"Infinity"` 回退到默认 20 秒而不是抛 `ArgumentException`，字符串小数按不变区域解析（逗号小数点的系统区域下 `"1.5"` 不再变成 15）。游戏内智能体和原生 MCP 共用 `ToolTimeout`。

- Race follow-up: explicit pause takes precedence over a pending settings auto-resume; stopping/cancelled updates cannot restart the loop. Align companion endpoint ID matching with the host's case-insensitive bindings.
- Serialize and revalidate companion settings acknowledgements against the current connection and fingerprint. Discard old acknowledgements, coalesce queued saves to the latest desired snapshot, and push edits made during startup once the verified companion connection is available. Add 12 more runtime/transport regressions.
- Debug follow-up: discard model tests that finish after the model/key/endpoint changes, preserve explicit thinking/protocol choices when rotating a key, and let an already-faulted play loop acknowledge pause so corrected settings can be applied. Keep the original loop failure observable for diagnostics.
- Stop interpreting incidental numbers in failure messages as HTTP status codes; keep legacy session loads case-compatible on case-sensitive filesystems.
- Avoid a Windows mod-load test deadlock by invoking the game launcher in the same PowerShell session; keep reward-guidance assertions aligned with the new take-or-skip advice.
- Validate Sandpit risk and automatic escape identity inside the shared execution lease; cancel a stale escape when the hand or countdown changes. Localize Sandpit rules and stop messages. Distinguish unavailable upgrade previews from maxed cards and refresh the detached copy's own variables before formatting its description.
- **附魔界面不再空选确认后原地打转。** 附魔的原生偏好允许选 0 张，但游戏的确认按钮会忽略空选择，Agent 以前会反复确认却毫无进展。现在附魔界面的 `selection.min_select` 至少为 1，未选牌时不公开 `confirm_selection`（直接请求返回 409）；选 1 到 `max_select` 张都可提前确认，其它选牌界面不受影响。（#205）

## v0.16.2 - 2026-09-24

> Dual-layer evidence, observability, and the small leaks. The decision log now records what each frame offered the executor and marks LLM fallbacks; the planner's input is always parseable and extreme frames degrade to a skeleton; the overlay marks fallback decisions, shows the session's Jev/fallback split, and serves the redacted diagnostics over HTTP; reasoning streaming and the screenshot path no longer leak work per frame or per failed capture; the width sentinel re-logs on change. Verified offline (882 C# tests, 423 Python tests, all gates), live on yesterday's dual-layer evidence, and smoke-checked today (invite reason, diagnostics route, invite failure feedback).

- **双层决策的规划与证据。** Jev 无法提交而改由 LLM 决策时，回退提示词继承规划器的 `goal`、`posture`、`instructions` 和分类提示。规划器输入按字段优先级裁剪为可解析 JSON，极端大帧保留屏幕、回合及战斗骨架而非退化成 `{}`。决策日志记录 Jev 逐回合选项 ID（至多 48）、概率、危险度、策略更新时间；LLM 接手的行带 `jev_attempt=true`，覆盖层标注「Jev 未提交 → LLM 接手」，Jev 面板显示本局执行次数和回退次数。
- **诊断与资源开销。** 新增仅 loopback 的 `GET /diagnostics`，返回与覆盖层导出按钮相同的脱敏文本。流式思考每次只生成前 600 字符的显示预览，最终完成体仍保留完整推理；截图等待绘制帧时显式断开信号，避免无法渲染时的连接积累；宽度诊断在超宽控件数量变化时再次报告。

## v0.16.1 - 2026-09-24

> A focused fix for the silent invite: in solo mode or while autoplay is running,
> `invite_ai_teammate` and `continue_ai_teammate` would sit at `pending` forever — no teammate
> window, no log, `dual_launch_outcome` stuck at `Idle`. These refusals are now recorded as
> `Rejected` with a readable reason and the call answers `409` pointing at the cause. Verified the
> failing behavior and the fix in the same isolated game session (offline suite: 866 C# tests,
> 423 Python tests, all gates).

- **邀请队友失败现在有明确原因。** 在单人模式或自动游玩进行中调用 `invite_ai_teammate`/`continue_ai_teammate`，以前会一直挂在 `pending`：双开任务在第一道模式门就被无声拒绝，既不写状态也不写日志，`dual_launch_outcome` 永远停在 Idle。现在这类"玩家可修复"的前置失败会记录为 `Rejected` 并附可读的告知文本（切到多人模式 / 先暂停自动游玩），请求返回 409 指向原因；真正并发在途的尝试语义不变，仍是 `pending`。该缺陷在 `--force-steam off` 的隔离档实机中复现（邀请 8 分钟无任何反馈），已加离线回归。

## v0.16.0 - 2026-09-24

> Watch the model think, give the fast executor a goal, and get an overlay that fits. A thinking
> model's reasoning now streams into the conversation while the turn is still running (only when
> "show reasoning" is on; nothing is stored otherwise). In dual-layer mode the LLM planner's advice
> finally reaches Jev: hints are mapped onto the options the frame actually offers, a one-sentence
> macro goal leads every Jev choice, and an external planner can set that goal through MCP without
> wiping the rest of the plan. The overlay's clipped right edge is fixed at its real cause, and
> `/vision/screenshot` no longer returns the overlay in the image. Live-checked in an isolated game on
> 2026-09-24 with no model calls: screenshots and layout (including deleting the first model) pass.
> Streaming reasoning and dual-layer play quality need real model/Jev calls and were not re-run live;
> see [the validation report](docs/live-validation-2026-09-23-results.md).

- **The overlay's right edge is no longer clipped (found live).** The play page's minimum width was 457 px inside the 440 px panel before any model was deleted: the chat card put "reply language", its drop-down, "thinking intensity" and its drop-down in one row, and each label keeps a 120 px floor. That widened the whole page and cut off the right edge of every control on it — the truncation reported after deleting a model. Each label/drop-down pair now has its own row; the width diagnostic reports no offenders on either tab, including after deleting the first model. The Token reading, which has its own row now, shows the whole sentence instead of stopping at 24 characters. `/vision/screenshot` was re-checked in the game: the JPEG no longer contains the overlay, and the overlay reappears after each capture.

- **Review fixes from three independent read-only reviews.** A plan's screen and combat round are now actually recorded: the planner reads a compact state cut at 4,000 characters, which is not valid JSON on nearly every combat frame, so the provenance was always empty and Jev was never told a plan came from an earlier fight; it is now read from the head of the cut summary. An external strategy update is merged inside the store's lock, so a plan the in-game planner lands at the same moment is no longer reverted. `plan_screen` / `plan_round` are marked read-only in the OpenAPI schema. A single step or a proactive chat that fails mid-stream no longer leaves its "thinking" bubble on screen. The settings page's save status wraps, because a rejected budget input puts a sentence wider than the panel into it. A theme or language change during a screenshot keeps the overlay hidden until that capture restores it, and a capture that somehow resumes off the game thread answers `screenshot_unavailable` instead of a 500. Hints past the 64-entry cap are reported as dropped instead of silently ending the scan.

- **外部规划器可以设定宏观目标；局部更新不再清空策略。** MCP `update_play_strategy`（原生与 Python 两面）和 `POST /strategy` 新增 `goal` 字段，外部 Agent 当规划层时也能写一句 Jev 每次选择都先读的宏观目标。修复：`POST /strategy`（Python sidecar 走的就是它）原本把省略字段重置为默认值，只调 `posture` 会顺手清空 `instructions` 与 `option_hints`；原生 MCP 的合并则会丢掉宏观目标。两面现在共用同一合并，省略字段保留原值，空更新返回 400 而不是重置。游玩 skill 同步：guided 工具清单为 17 个，新增「给游戏内自动游玩当规划层」的用法，说明 `play_running` 时外部 `act` 会与自动游玩抢操作，并在地图剧本里写明行进动画期间不要重发选点。

- **双层决策与窄屏布局。** LLM 的宏观目标现在作为独立、长度受限的策略字段传给 Jev；`option_hints` 从动作类型映射到当前帧实际可选的动作 ID，过期提示不再误导选择。策略记录编写时的屏幕/战斗回合，执行端标明跨战斗复用的局限；保留原有 `instructions` 和低置信度回退。设置页删除提示与首次引导句子可换行，游玩页将 Token 指标移到独立的整行，避免模型删除或消耗增长后挤出面板。此前实机观察和复验记录见 [2026-09-23 部分验收报告](docs/live-validation-2026-09-23-results.md)：布局已于 2026-09-24 在新 DLL 上实机复验通过；双层决策的改动需要真实 Jev / 模型调用，尚未实机验收。

- **截图实际绘制帧。** 截图隐藏覆盖层后等待渲染器已绘制的帧，而不是在绘制前的 `ProcessFrame` 立即读出旧画面；并发截图串行、超时/取消后恢复原有可见性。先前返回 200 但 JPEG 里仍含覆盖层的实机失败已记录；2026-09-24 在游戏内复验：连续两次截图均不含覆盖层，截图后覆盖层恢复可见。

- **Live reasoning during auto-play.** A thinking model's reasoning now reaches the in-game conversation
  while the turn is still running instead of only after it lands. The LLM client reads a streamed body as
  it arrives (the SSE/JSON decision is made on the first non-blank line, and complete lines go through the
  same accumulator `ParseSsePayload` uses), reports each reasoning partial to the turn, and the play page
  draws it as a muted "thinking" bubble under the recorded turns. The recorded bubble replaces it: the
  streamed buffer is cleared before the completed turn appends its own reasoning, so one turn never draws
  two reasoning bubbles, and a turn that pauses, cancels or fails clears it too. Unchanged by default:
  the "show reasoning" switch gates storage, so with it off (the default) a thinking model's scratchpad is
  never stored, never drawn, never persisted and never replayed to a provider. Reasoning is still echoed on
  tool-call turns exactly as before.

- **Severe-usability sweep (2026-09-23).** Pause now works even when the game window stops rendering
  frames: every game-thread call the auto-play turn makes (turn snapshot, act, game-data lookups,
  screenshots, companion follow-ups, session observation) carries the pause token and a deadline. An
  act abandoned while still queued never runs later. The map no longer offers `choose_map_node` during
  the travel animation, when the game silently ignores the click and the agent kept re-sending it. The
  AI teammate no longer stops itself after 150 s while the human host is still choosing a map node,
  and it quits by itself when the host window closes, crashes or is killed, instead of running
  invisibly and spending budget. LLM compatibility: DeepSeek/Kimi thinking modes keep working past the
  first tool round (`reasoning_content` is echoed); OpenRouter-style keep-alive comments no longer
  break streamed replies; tool calls with numeric ids, object arguments or no id are no longer dropped
  or fatal. A card-reward skip clicks the enabled Skip, not a disabled alternative. A reward flow
  stopped at a card choice now says so instead of "still transitioning". A finished event's proceed
  during an act transition returns a retryable 503 instead of a 500. In co-op, an `AnyPlayer` potion
  aimed at a teammate who just died fails visibly instead of hitting the local player. In dual-layer
  mode, Jev is no longer offered rest, reward, epoch or shop options the game would reject. The
  per-model Test also verifies the model's roles, so a green badge no longer launches a teammate that
  just stands still, and a failed test shows its reason on the card. `/vision/screenshot` attempted to hide
  the overlay, but the 2026-09-23 live JPEG still contained it; see the screenshot correction above.
  Monster `damage_values`/`block_values` (always null) are no longer projected to agents; live
  damage stays in `combat.enemies[].intents`. The overlay no longer builds a full state payload each
  refresh just for the screen name, and long co-op status lines wrap. Scripts: the network MCP
  launchers honor `STS2_API_BASE_URL`; `start-game-session.ps1` fails when the old port isn't released;
  `build-mod.sh --skip-install` no longer requires a mods directory. Offline build, C# and Python tests,
  and gates pass; live-game validation of these paths is still pending.

- **Audit remediation integrates solo/co-op lifecycle, run-scoped guidance, and Jev status.**
  A mode switch pauses the active loop or companion before saving the new mode; external
  start routes honor the selected mode and do not start a second loop during a switch.
  Messages sent during solo auto-play become bounded, one-time guidance for the same run;
  action receipts expose a short status without dumping raw tool results. A running local
  companion accepts token-protected Jev setting changes and reports its own non-secret
  choice, probability, danger, and latency readings. HTTP and both MCP planner briefing
  surfaces share a run-scoped projection. HTTP actions bind decisions to the pre-action
  game run; late decisions cannot resurrect an old active session. Offline tests and
  static checks cover these paths; live dual-window play and the real Jev service remain
  to be validated.

- **Jev turns keep their full request, token, and action receipts.** Fallback, rejected actions,
  cancellation, and run boundaries retain completed work. Both the execution request and any LLM
  or vision fallback check the remaining budget. Low confidence still reaches the strategy planner
  when the LLM performs the action. Nested snapshots select the correct screen guidance, and
  missing required action parameters no longer become selectable placeholders. Play requests use
  one bounded turn deadline; a Jev 429 can retry only once when time and request budget remain,
  with each actual attempt counted separately. The Jev transport reuses its connection pool,
  validates service URLs, and redacts echoed credentials before formatting errors.
- **Continued-run memory includes subsequent decisions and stays isolated from other runs.**
  All decision writers feed the active run's bounded memory. Chat, single-step, interrupted play,
  strategy updates, and chat clearing participate in persistence. Failed writes retain their latest
  snapshot for retry, and a concurrent update cannot be marked clean by an older save. Session files
  validate run identity, tolerate null entries, redact strategy text, preserve load timestamps, and
  avoid Windows filename collisions while retaining legacy loading and safe deletion.
- **Background plans obey the play lifecycle and budget.** Requests reserve budget under the turn
  gate before dispatch. Failed attempts count once, while a missing model makes no request.
  Pause, run changes, settings changes, and newer external strategies invalidate stale responses.
  Strategy hints use immutable snapshots, and low-confidence refresh streaks reset after triggering.
  The September 23 audit adds 42 offline regressions for these paths; real-game validation remains
  separate from the offline build and test suite.

- **Dual-layer mode actually engages now, and reward card picks are real decisions.** Three
  live-pass fixes: the Jev execution decider was built once at mod startup, so a Jev API key
  entered afterwards never took effect until a restart — the decider is now resolved from the live
  configuration on every turn. The Jev "test connection" button was a configuration stub; it now
  performs the real `GET /v1/models` round trip. And `resolve_rewards` / `collect_rewards_and_proceed`
  called without an explicit `option_index` no longer take the first card silently: the reward flow
  stops at the card selection (`reward.pending_card_choice = true`, action returns `pending`) so the
  model makes the deck-building call with `choose_reward_card` / `skip_reward_cards` (or an explicit
  `option_index`, `-1` to skip). In dual-layer mode the Jev option list expands `resolve_rewards`
  into one option per offered card plus an explicit skip, instead of a bare macro that executed with
  no index.
- **Auto-play stalls are visible and bounded.** Every game-thread call the loop awaits now has a
  deadline and honors cancellation — a game thread that stops pumping used to hang the turn forever,
  leaving the pause button dead. The status line reports the turn's current phase with elapsed time
  instead of sitting on "requesting the model" for the whole turn. A run that waits for an actionable
  state for 150 seconds straight now stops with a visible reason instead of retrying silently
  forever. LLM and Jev per-request timeouts are configurable in settings
  (`LlmRequestTimeoutSeconds` / `JevRequestTimeoutSeconds`), and unexpected turn failures keep their
  exception type instead of a bare message.
- **Per-model verification is a real play probe.** Each model card's Test button runs a connectivity
  ping plus a miniature play decision against the real `act` tool (a mock combat frame whose only
  correct answer is a tool call), and the card wears the verdict as a badge. A transient failure
  (429/5xx/timeout) no longer downgrades a working model's recorded tool capability — only a clean
  "no tool call" or a schema-rejecting 4xx does.
- **The model's real thinking is no longer overwritten by its one-line act reason.** When a
  provider returns `reasoning_content`, that is what the thought view shows; the short reason in the
  act arguments only fills in when the provider sent no reasoning.
- **A run's conversation and decision context now survive "continue game".** Each run's play session
  (the chat history, the recent-decision memory the loop compacts from, and the dual-layer play
  strategy) is persisted to a per-run file under `sessions/` beside the settings, written atomically
  with a last-good backup. Continuing a saved run restores all three: the chat repaints, the decision
  memory resumes, and Jev resumes its strategy. Switching models keeps the context (it is bound to the
  run, not the model), a new run starts a fresh session, and the placeholder pre-run state is never
  persisted. Persistence is best-effort — a corrupt or unwritable file never blocks play — and chat
  text is redacted before it reaches disk so a pasted API key cannot be written out.
- **Dual-layer decision mode (Jev + LLM planner).** A new per-mode toggle (single-player and
  co-op each have their own) hands the actual clicking to the TypeSafe Jev fast model while the
  LLM only plans strategy. When enabled and Jev is configured (base URL + API key in Settings),
  each turn asks Jev which concrete on-screen option to take; a confident-enough answer executes
  immediately, and a low-confidence or failed one falls back to the normal LLM play path, so an
  unsure fast model can never strand a turn. The confidence threshold is configurable. The current
  play strategy is readable and writable from outside via `GET`/`POST /strategy` (loopback only)
  and the MCP tools `get_planner_briefing` / `update_play_strategy`, and the in-game planner and
  an external MCP planner steer the same store — the overlay path and the MCP path are one
  experience. Decisions made by Jev carry their confidence into the decision log.
- **Thinking models stop being misdiagnosed.** DeepSeek-style, Qwen-style, step-5-preview and
  every provider that answers in `reasoning_content` before it produces content used to surface
  in the mod as `模型未给出可执行动作` even when the model had done the thinking and run out of
  the completion budget mid-reasoning. The OpenAI-compatible client now reads `finish_reason`
  (both from the non-streaming payload and from whichever SSE chunk carries the final message),
  and when the loop returns with no action, an empty `content`, and `finish_reason=length` the
  error says exactly that — reasoning budget consumed the turn, not "model did not try".
- **An empty assistant message no longer reads as a generic failure.** When a thinking model
  legitimately leaves `content` empty but filled `reasoning_content`, the failure text names the
  reasoning and says the message was empty, instead of pretending the model said nothing at all.
- **Default completion requests stop sending `max_tokens`.** Only the health probe (`/health`)
  still pins `max_tokens: 16` to keep pings cheap; play and chat requests leave the completion
  budget to the provider, which is what thinking models assume.
- **Normal long thinking no longer triggers the generic three-strike stop.** A streamed response
  with reasoning but no action and `finish_reason=length` is now a bounded, budget-accounted
  continuation state: the agent backs off and tries again instead of classifying the thinking as a
  failed decision. It still stops after five consecutive provider-limited turns with an actionable
  explanation, preventing an endpoint with an impossible output cap from spending forever.
- **Step-5 reasoning settings reach the API.** `step-5-*` models now infer the standard
  `reasoning_effort` request shape, so selecting Low sends `reasoning_effort: "low"` instead of
  merely appending a natural-language hint. Streaming now reads a terminal `finish_reason` from
  the normal delta-shaped final chunk as well as message-shaped chunks.
- **Tool-using play models automatically recover from one stream-only provider limitation.** If a
  gameplay response streams prose instead of a required tool call, the mod makes one safe
  non-streaming retry before treating it as a model failure. No game action has been accepted at
  that point, and both calls remain visible to the session budget. This covers providers whose
  non-streaming compatibility path serializes tools correctly while their SSE path does not.
- **Vision has a read-only live frame.** `GET /vision/screenshot` returns the current game viewport
  as JPEG on loopback. It does not play a card or send the image to a model, so the screenshot path
  can be checked without starting a run.
- **Native MCP can be switched on without restarting the game.** `POST /mcp/control` with
  `{"running": true|false}` is loopback-only and calls the same switch as the overlay Connect tab.
  Editing the settings file no longer has to be followed by a restart before an external client can
  reach `/mcp`. The switch does not start or stop autoplay.
- **A full context window compresses history instead of ending the run.** Each model has its own
  context window, defaulting to 256,000 tokens and editable per model. Once a measured play prompt
  reaches 80% of that window, older accepted decisions are replaced by one continuation summary.
  The latest state and the six newest decisions stay verbatim, and the decision log itself is not
  rewritten. A blank session spend cap still means no cap; filling the context window is not one.
- **The overlay no longer clips long play and decision text.** The pause control gets the full card
  width while autoplay is running, usage sentences stack instead of sharing a tile row, and decision
  reasons wrap inside the 440px panel instead of running off its right edge.

## v0.15.0 - 2026-09-22

> A harness pass over everything an AI pays for when it plays this game through the mod: the state
> payload it reads before every decision, the prompt the in-game loop rebuilds every step, the tool
> surface it calls, and the offline index it looks things up in. The compact `agent_view` drops three
> redundant keys and gains the fields the strategy text already told a model to read; the in-game
> prompt stops re-sending all twenty screens' playbooks and puts the state last so the prefix can be
> cached; `act` stops handing back the raw 4,000-9,500-token state; a rejected index finally says
> which indices would have worked; and the knowledge index gains the relic, power and monster-move
> tables that did not exist. Two defects came out of the same reading: `end_turn_will_kill_player`
> said a turn was safe while poison was about to kill you, and the compact view was leaking the exact
> order of the draw pile. A second pass turned to what an external client pays before it ever reads a
> state: the tool descriptions, which a client re-sends on every request, and which the Python
> sidecar carried at four times the native surface's length for the same tools. A third pass found
> that the strategy text both loops inject still named raw `/state` fields the compact view had
> renamed, so a model following it was reading keys that were not there.

- **The strategy text now names the fields the compact view actually has.** Both the in-game loop
  and `get_scene_guidance` inject `strategy.md` beside a compact `agent_view`, but the rules told a
  model to read `run.current_hp`, `map.available_nodes`, `map.player_votes`, `shop.card_removal` and
  `event.event_id` — raw `/state` names the compact view renames to `run.hp`, `map.options`,
  `map.votes`, `shop.remove` and `event.id`. A read of the old name comes back missing, so a route,
  shop or event decision was being made against a field that was not in the payload. The injected
  sections now use the compact keys, and a contract test fails if a backticked field in those
  sections goes back to a raw name. `get_scene_guidance` also reads `event.id` when `event.event_id`
  is absent, so the per-option risk rows join on the compact view instead of coming back empty.
  `decide` already rewrote that one key; the standalone guidance call did not.

- **Shop relics and potions now carry their ids in the compact view.** `shop.cards[]` already had
  `card_id`, but `shop.relics[]` and `shop.potions[]` had only a display `line`, so asking what a
  stocked relic or potion does meant falling back to the full `/state` payload. They now carry
  `relic_id` and `potion_id`, the same keys `get_game_data_item` looks up. The raw payload already
  had both; only the compact projection was missing them.

- **`end_turn_will_kill_player` now counts Doom and Magic Bomb.** Doom kills at the end of your own
  turn once its amount is at least current HP, and block does not enter that comparison. Magic Bomb
  damages its owner after that same turn and ignores block. Neither was in the intent sum, so the
  flag the strategy text tells a model to trust could stay false on a lethal end turn. The
  player-played bomb stays out: it damages enemies, not the player who played it.

- **The in-game prompt no longer re-sends the whole playbook on every step.** `PlaySystem` used to
  embed `screen-playbooks.md` in full — about 3,070 tokens, re-sent for each decision, covering 20
  screens when one is relevant. The system prompt now carries the play contract, and the current
  screen's section arrives as its own message through the new `PlayPrompt.PlaybookGuidance(screen)`;
  a screen with no section gets a 622-character index of what exists rather than nothing, so the
  guidance is never silently empty. Measured on a COMBAT step: the static block drops from 24,105
  to 14,577 characters, about 2,722 tokens less per decision.

- **All static content now precedes all dynamic content in that message list.** The game state used
  to sit at message position 1, so every later message — guidance, team context, vision — was
  sent after a changing prefix and no provider could cache it. The state and the final "act once"
  instruction are now the last two messages, which makes the static prefix byte-identical across
  consecutive steps on one screen; a contract test pins that stability so a later edit cannot
  quietly move state back to the front.

- **The compact view no longer ships the same data twice.** `agent_view.version` is now `11`. The
  top-level `actions` array was byte-identical to `available_actions`; `profiles` was a constant
  `[{1},{2},{3}]` list that `native_profile_id` already answers; and `combat.draw_cards[]` /
  `discard_cards[]` / `exhaust_cards[]` — duplicated again inside `run.piles` — restated the grouped
  `draw[]` / `discard[]` / `exhaust[]` stacks one object per card. All three are gone from the
  compact view only: no `/state` field was removed, and both MCP surfaces already read
  `available_actions` with `actions` as a legacy fallback, so an older sidecar still works.

- **Decision inputs the strategy files already assumed are now actually in the payload.** The map
  gained `nodes[]` (`coord`, `node_type`, `visited`, `children[]`) plus `boss_node` /
  `second_boss_node`, so route planning across floors no longer requires falling back to
  `get_raw_game_state`. `run` gained `relic_stacks` and `relic_descriptions` — a counter relic's
  charge (Pen Nib at 9/10) exists nowhere else in the compact view. `combat.hand[]` gained
  `upgraded` / `energy_cost` / `star_cost` as numbers instead of leaving them inside the localized
  `line`, `rest.options[]` gained the stable `option_id`, `event.options[]` gained `text_key` (the
  key the offline event-risk index is written against), `shop.cards[]` gained `on_sale`, and
  `run.potions[]` gained `description`. Together these remove the field-hunting round trips that the
  previous payload forced.

- **Reward card options carry their real cost.** `reward.cards[]` passed `null` for the energy and
  star cost of every offered card, so a pick between two cards showed neither one's price; it now
  reports the cost the raw payload always had.

- **`get_relevant_game_data` now derives the ids the offered-choice screens are about.** Omitting
  `item_ids` promised "the ids the current screen is about", but only combat, shop and event had an
  id source: on `REWARD`, `CARD_SELECTION` and `CHEST` the tool fell through to the deck and relics
  the player already owned, so asking about the cards on offer answered with the cards in the deck.
  The scene tables gain `reward` (`reward.card_options[].card_id`, or the compact
  `reward.cards[].card_id`), `card_selection` (`selection.cards[].card_id`), `chest`
  (`chest.relic_options[].relic_id` / `chest.relics[].relic_id`) and `bundle_selection`
  (`bundles[].cards[].card_id`), each projected to the lean offer field set instead of combat's
  larger one. The lookup stays collection-aware — a relic question on a card screen is never
  answered with card ids — and a reward screen's gold/potion/relic rows carry no stable id, so they
  keep the run-level fallback rather than a guessed id; the fallback now runs only when the screen's
  own source for that collection is empty. `CARD_SELECTION` is no longer classified as the generic
  `menu` scene, so `get_scene_guidance`'s `scene` key reports `reward` / `card_selection` / `chest` /
  `bundle_selection` where it used to say `menu`, and a `REWARD` card lookup is projected instead of
  returned whole. Collection names are matched case-insensitively on both sides, as the C# tables
  already did. Both mirrors carry the same tables and the same screen mapping, pinned by the C#
  `DetectScene` expectation table that `tests/test_scene_field_alignment.py` reads back.

- Document the additions, the removals and the version bump in `docs/api.md`, including the
  compatibility note that only this derived view changed.

- **Every HTTP response stopped paying for whitespace.** The wire format was indented JSON, which is
  pleasant to read in a browser and expensive everywhere else: measured against this repository's own
  response examples, indentation was **35.6% of the bytes** — 44% on the nested combat payload — and
  every one of those bytes is re-read by the MCP sidecar, an external agent or a validation script on
  every single state read. Responses are compact now; `curl -s .../state | jq .` gives back the
  pretty form, and field names stay PascalCase, which is the part that is actually a contract. The
  test that used to pin indentation now pins its absence, so turning it back on has to argue with an
  assertion.

- **The offline knowledge index gained the two files an agent asks for most.** There was no
  `relics.md` and no `powers.md`: a model deciding whether to take a relic, or reading a `power_id`
  plus a stack count, had nothing to look the answer up in. `docs/game-knowledge/relics.md` now
  covers 289 relics (`Name | Rarity | Owner | Effect`) and `powers.md` covers 257 powers
  (`Name | Type | StackType | Hook | Effect`), both generated by the existing
  `scripts/generate-sts2-knowledge.ps1` rather than written by hand. Monsters stopped being the gap
  they were: MinHp/MaxHp went from 15 filled and 106 blank to all 121 filled, from single
  declarations only (the `AscensionHelper` fallback, an inherited property with the declaring class
  winning, `MaxInitialHp => MinInitialHp`), with the file stating that A1+ is higher and that the
  live `min_hp`/`max_hp` stays the authority. Nothing was invented to fill a cell: 16 power rows and
  10 relic rows say the effect could not be read off a declaration, and 22 amounts stay `?` because
  they are only computed at run time. Both files are reachable now: `get_planner_context` and
  `get_combat_context` list them in `reference_files` under the ids the state reports (`relic_id`,
  `power_id`), which is the mapping `docs/game-knowledge/agent-reference.md` documents.

- **`act` stopped returning the full state, on both MCP surfaces.** `POST /action` answers with the
  whole raw `/state` payload — roughly 4,000-9,500 tokens — and the Python sidecar handed that back
  verbatim after every single action while the native `act` already answered with the compact
  `agent_view`. The two surfaces are one tool face, so the sidecar now returns the action's own
  post-action snapshot projected to `agent_view`, the same shape `get_game_state` returns, and the
  other keys (`action`, `status`, `stable`, `message`) are untouched. `raw_state=true` is the escape
  hatch for a field the compact view does not carry; it is off by default. The HTTP route is
  unchanged, so REST callers still get the raw payload.

- **Waiting across an animation stopped costing the whole state, and a lost response stopped paying
  for it twice.** `wait_until_actionable` is the tool the skill sends a client through on every
  animation and screen transition, and the Python sidecar answered it with the full raw `/state`
  payload — the 4,000-9,500 tokens `act` had just stopped sending, on the step where they are least
  useful; the native surface had been answering compact all along. Both surfaces now return the same
  compact state `get_game_state` returns, with `raw_state=true` as the escape hatch. The other leak
  was the reliability path: an action whose response was lost embeds the one state reconciliation it
  managed under `reconciliation.state`, and that copy was the raw payload too — raw state embeds its
  own `agent_view`, so an unprojected copy cost those tokens twice. It is now the same compact
  projection, marked, and the block says what it is: `state_read` (the read happened),
  `action_effect_compared` (always false — nothing diffed the state against the action),
  `action_outcome` (always `"unknown"`), and `required: true`, next to the `succeeded` / `status`
  keys that describe the read rather than the action. The top-level `status` stays `outcome_unknown`,
  and the rule is now written where a model reads it: never replay the action automatically.

- **A rejected index is now correctable, not just refused.** `card_index 9 is not in the latest
  combat.hand.` said what was wrong and nothing about what would have worked, while the
  "action is not available" answer next to it already listed `available_actions`. Both surfaces now
  answer an index mistake with the same error object the HTTP API sends — `code`, `message`,
  `details`, `retryable` — whose details name the offending `field`, the `submitted` value, the
  `valid_field` payload path (`combat.hand`, `map.options`, `combat.hand[].targets`, ...) and the
  `valid_indices` that path currently offers. The indices are free: deciding an index is stale means
  the validator is already holding the payload that lists the good ones. The human-readable sentence
  is unchanged and stays at `error.message`. On the Python side the mod's own details
  (`hand_count`, `option_count`, ...) are kept and the same keys are added, derived from those
  counts when the mod reports a count rather than a list.

- **Native MCP tool failures carry the structured envelope.** An exception in a native tool became
  `{"error": "<message>"}`, dropping the `code`, `details`, and `retryable` the HTTP and Python paths
  keep — so the play contract's rule "retry only when `retryable` is true" was unimplementable on
  that surface. Tool errors now serialize the same `{code, message, details, retryable}` object (with
  `status_code` when there is one), an unknown tool name answers `404 unknown_tool`, and an act
  without a name answers `400 invalid_request`.

- **`decide` reads one decision's worth of context in one call.** The documented loop was
  `get_game_state` -> `get_available_actions` -> `act` plus `get_scene_guidance` on the screens with a
  real choice: three or four calls per decision, each rebuilding the whole state on the game thread.
  `decide` returns `state`, `available_actions`, and `scene_guidance` from a single state read on
  both surfaces, and `act` reuses one snapshot for its legality check and its index validator instead
  of building the state three times. The existing tools are unchanged.

- **`get_scene_guidance` answers the same four keys on both surfaces.** The Python tool returned
  `{screen, guidance, event_id, event_options, guidance_source}` and the native tool
  `{screen, scene, guidance}` — same name, different answers depending on which surface served it.
  Both now always answer `screen`, `scene`, `guidance`, and `playbook`, where `playbook` is the
  per-screen section of `screen-playbooks.md` the in-game loop uses and is never empty (an unmapped
  screen gets the index of the sections). The sidecar keeps its three extras, which come from the
  offline event index the mod does not ship. `tests/test_scene_guidance_alignment.py` compares both
  mappings and the shared key set against the C# source.

- **The compact view stopped leaking the draw order.** The removed `*_cards[]` arrays were emitted in
  real pile order, so a model could see the exact sequence it was about to draw — information the
  game's own pile viewer deliberately hides by sorting. The grouped stacks it now reads instead carry
  the same multiset (which the game does show) with no order attached.

- **The crystal sphere stopped giving away what a divination is supposed to buy.** `crystal_sphere.items[]`
  computed `revealed` for every item and then serialized `kind` (`item.GetType().Name`) and `is_good`
  (`item.IsGood`) unconditionally, so an item whose cells were all still hidden shipped its identity
  next to a `revealed` flag saying it was hidden — and the compact `agent_view` passes `crystal_sphere`
  through untouched, so the default MCP read carried it too. An agent could read the reward/curse
  layout at zero divination cost, which is the opposite of what the screen playbook describes. Both
  fields are now nullable and written only when the item is revealed; while it is hidden they are
  `null`, and the keys stay present, so a client that reads them unconditionally still works. `x`, `y`,
  `width`, `height`, `cells` and `hidden_cells` are untouched: they are the board occupancy a client
  plans its clicks against, and no action mechanic changed. `docs/api.md` and `screen-playbooks.md`
  now state the gate the code implements.

- **`end_turn_will_kill_player` now counts damage that lands after you stop acting.** The flag was
  built from enemy intents plus one hand-written Sandpit case, so poison — which resolves at the
  start of your own next turn, ignores block entirely and lands before you can play another card —
  and constrict, which resolves when your own turn ends, were both invisible. A run could end with
  the flag still `false`, and `strategy.md` tells a model to trust it. Both now report a risk row
  (`poison_next_turn`, `constrict_turn_end`) with the damage, the damage after block and the reason;
  the poison total is read from the live power so Accelerant and every damage modifier are already
  folded in rather than guessed from the stack size. The glossary entry for 中毒 also said it
  resolves at end of turn, which is not what the power does; it now matches.

- **The MCP tool surface stops re-explaining the game on every request.** A client re-sends every
  tool's name, description and schema on each model request, and the default `guided` profile was
  sending 11,468 characters of `tools/list` JSON — roughly 2,900 tokens — before a single byte of
  state was read. Most of it was narrative the bundled skill already carries: a six-step usage loop, a
  "Result:" essay, and per-parameter cookbooks. The descriptions are now telegraphic, and the
  per-argument contracts moved into each tool's `inputSchema` property descriptions — where an MCP
  client renders per-argument help, and where the native C# surface kept them all along (`act` carries
  252 characters of description there against the sidecar's 2,744, for the same tool). The default
  profile now sends 8,255 characters: **-3,213 (-28%), about 800 tokens off every single request**,
  with the same 15 tools, names, argument names and return shapes. Every profile drops by that same
  3,213, because the trimmed descriptions are the ones the profiles share. `pydantic` is declared as a
  direct dependency now, since `server.py` imports `Field` to put that contract on the wire.

- **A budget contract test makes the next long docstring fail the build instead of every request.**
  `mcp_server/tests/test_tool_surface_budget.py` pins the `tools/list` envelope for each profile, a
  ceiling for each tool, and the ratchet: every ceiling must sit below what that tool cost before this
  pass, and the two-entry exemption list — for tools whose wire cost genuinely rose, because a nullable
  parameter carries ~38 characters of `anyOf`/`null` scaffolding before any description — fails if it
  ever goes stale. Same shape as the mod's own source-size budgets, and for the same reason.

- **The guided tool list lived in six files and every copy but the canonical one was wrong.**
  `SKILL.md`'s `allowed_tool_names`, `mcp_server/README.md`, `references/debug-and-validation.md` and
  `AGENTS.md` still described a ten- or eleven-tool surface, missing `decide`, `get_scene_guidance`,
  `get_decision_log`, `get_run_summary` and `diff_state` — an agent that filtered its toolset by that
  list could not reach the one call the play loop is built around. The five lists now match
  `ESSENTIAL_TOOLS`; the per-tool parameter cookbook in `mcp_server/README.md` became a table pointing
  at the schemas so it cannot drift again (1,712 characters lighter); `skills/sts2-mcp-player/README.md`
  and both root READMEs were corrected the same way; and ADR 0002 keeps its dated body but gained a
  pointer to the live list rather than being rewritten.

- **Two actions arriving at once no longer run at the same time inside the game.** `HttpServer`
  dispatches every request on its own task, and `POST /action`, the in-game agent, the native MCP `act`
  tool and the teammate coordinator all converge on `GameActionService.ExecuteAsync`, which had no
  execution gate: two callers could each validate their indexes against the same state, mutate the
  game, and then wait for a transition the other one was causing. A new `ActionExecutionGate` holds one
  non-blocking lease for the whole action — every transition wait included — and releases it in
  `finally`, so a thrown or canceled action cannot leave the mod busy. The second request fails
  immediately with 409 `action_in_flight` (retryable; `details.in_flight_action` names the running one)
  instead of queueing behind a snapshot the first action has already invalidated, and the action switch
  and every handler are unchanged. The Python client now surfaces that one server-declared retryable
  flag without adding an automatic POST retry (`retry_count` stays 0, so exactly one POST leaves). Nine
  offline C# tests pin acquire / busy / release, the thrown and canceled release, and the `ExecuteAsync`
  wiring; a Python test pins the single POST with `retryable: true`; `action_in_flight` joins the
  `docs/api.md` error table and the regenerated `docs/openapi.json`.

- **A decision's state and its action list now come from one frame.** `GET /state` and
  `GET /actions/available` are two requests, and two requests are two frames: the game advances in
  between, so the state a client read and the action indices it read next could describe different
  moments — a screen that changed mid-decision left an index validator judging an action against a
  hand the payload it validated never showed. The new `GET /decision-snapshot` answers both halves
  from one game-thread state build: `state` is the compact `agent_view`, and `available_actions` is
  the descriptor list that build's own action-surface walk produced, retained on the payload
  internally so it never serializes into `/state`. Both existing endpoints stay. The in-process
  `GameBridge` snapshot — what the native `act` and `decide` read — built the state and then the
  action surface separately inside one callback, and now uses the same builder, so one snapshot
  enumerates the action surface once instead of twice. Python `decide` reads the new route in one
  call and falls back to the old two reads only against a mod that predates it (404 `not_found`); the
  route is documented in `docs/api.md` and the regenerated `docs/openapi.json`.

- **`get_scene_guidance` returned no event options on every real event.** The offline risk index in
  `docs/game-knowledge/events.md` was keyed by the generator's readable class name (`Neow`) and its
  options by the page-relative key (`INITIAL.options.ARCANE_SCROLL`), while a live `EVENT` payload
  reports the model's id entry (`NEOW`) and the full localization key
  (`NEOW.pages.INITIAL.options.ARCANE_SCROLL`). `event_option_risk` compared the two with `==`, so
  the `event_options` array came back empty on every live payload — the whole per-option risk half
  of the tool was dead on the surface it was written for, and the tests only ever fed it the
  generator's own spelling, so they stayed green. There is now one canonical join: `slugify` ports
  the game's `StringHelper.Slugify` exactly (including its `\G(?!^)` rule, which parts capital runs
  — `ABC` -> `A_B_C`), `canonical_event_id` makes `Neow`, `NEOW` and `neow` one key, and
  `canonical_option_key` strips only the `<EVENT>.pages.` prefix, because `EventModel.OptionKey`
  slugifies only that segment and re-slugging the rest would rewrite `ARCANE_SCROLL` into
  `A_R_C_A_N_E_S_C_R_O_L_L`. The matching stays exact — no prefix, substring or similarity match —
  and canonical ids that would collide are dropped rather than guessed, so two events with
  near-identical names can never be merged. The generator now writes the canonical `event_id`
  straight from the decompiled class name (`ConvertTo-Slug`, the same algorithm), so the index and
  the runtime agree without either side redoing the other's work; only `events.md` was regenerated.
  `docs/api.md` had documented `text_key` as the page-relative index key (`INITIAL.options.IMMERSE`),
  which no payload ever sends, and now states the full shape and the join; a contract test reads that
  page's examples and fails if they stop being keys the committed index carries and the runtime
  accepts. Regression tests use the live values verbatim, cover a later page
  (`ABYSSAL_BATHS.pages.ALL.options.LINGER`), an unknown event, and two names a loose match would
  collide on.

## v0.14.6 - 2026-09-21

> A reliability batch over the agent loop, the budget ledger, the event stream and both MCP
> surfaces — twenty-five defects found by two offline audits of the dev branch, none of them
> reachable without reading the code. Accepted actions are never replayed, canceled turns still
> pay for the work they did, and the two MCP surfaces answer `get_decision_log` with the same
> envelope.

- Preserve accepted action receipts when a follow-up state read fails. The in-game agent now reports the action as unsettled and refreshes state before another decision instead of treating an accepted action as a failed submission and replaying it. Both tool-calling and JSON-fallback models follow this boundary.
- Include the current decision's known token usage, including a separate vision request, in every subsequent model-budget check. Record final usage once; checks do not mutate the ledger.
- Keep completed actions and known usage when a turn is canceled or crosses a run boundary. Interrupted turns carry a receipt through the cancellation exception; autoplay, single-step, regular chat, teammate replies and proactive chat consume it without starting more work or inventing missing provider usage.
- Record gameplay before considering proactive chat, and hold the shared turn gate through accounting. Queued chat or step requests now see the preceding turn's budget usage before they can start; canceled proactive chat cannot discard or double-charge the already-completed gameplay turn.
- Validate the JSON object root before reading tool arguments or action reasons. Malformed/non-object action parameters return a structured invalid-request result while preserving usage and allowing a corrected action.

- Preserve the selected overlay theme in settings copies. Theme selection saves only the theme, keeps unfinished form input and scroll position, repaints endpoint/model panels, and reports persistence failures.
- Unwrap the companion's actual HTTP state response and read its local AI player, so health, energy and hand count describe the same character. Reject failed or malformed response envelopes.
- Fix the Python MCP `diff_state` tool recursively calling itself. Both diff implementations now report actual truncation and depth limits, escape ambiguous property paths, distinguish empty objects from strings, and apply the same 200-entry cap. Native diffs preserve large integers and compare equivalent decimal number spellings correctly.
- Clear event snapshots when the last subscriber leaves or overflows. Ignore late idle samples, update polling demand on snapshot overflow, and reset duplicate suppression for a new subscriber lifecycle.
- Return structured errors for broken event streams and unreadable HTTP error bodies. Bound connection/read waits by the overall deadline, distinguish connection timeouts from idle reads, and back off repeated empty-stream reconnects.
- Propagate caller cancellation through model probes, vision and read tools. Canceling a model test does not cache a connectivity failure. Clear a stale save-failure notice after a successful retry.
- Add regressions for these boundaries and execute the registered diff tool in all three Python MCP profiles. This maintenance batch is validated offline; live-game checks are tracked in the dev audit.
- Extend the JSON object-root validation to the three readers the previous batch missed: a non-object act result now reads as settled instead of throwing out of `ActIndexValidator.IsUnsettled`, a stringified non-object MCP `arguments` value degrades to an empty object instead of crashing the native argument readers, and a non-object state payload is ignored by the run boundary instead of throwing.
- Align the `get_decision_log` return shape across both MCP surfaces: the Python sidecar now returns the same `{"decisions": [...]}` envelope the native surface returns, so a client can target either surface with one parser.
