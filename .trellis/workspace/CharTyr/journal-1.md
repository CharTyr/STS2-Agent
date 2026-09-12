# Journal - CharTyr (Part 1)

> AI development session journal
> Started: 2026-09-08

---



## Session 1: GAME_OVER save wait and reliability merge

**Date**: 2026-09-08
**Task**: GAME_OVER save wait and reliability merge
**Branch**: `main`

### Summary

Fixed GAME_OVER continue so native summary save runs before Return; merged that plus session reliability work to main via PRs 79 and 80. Isolated floor-0 die verified save_verified. Dual GAME_OVER save on the new DLL was not completed.

### Main Changes

- continue_game_over waits for native Return instead of enabling it after 15s
- Session budget, corrupt settings restore, MCP Origin, LLM stall timeout, and play_card cancel landed on main

### Git Commits

| Hash | Message |
|------|---------|
| `4b4da6e` | (see git log) |
| `22907b0` | (see git log) |

### Testing

- [OK] Contract tests including GameOver.NoForcedReturn
- [OK] Isolated singleplayer floor-0 die: continue 2.47s, save_verified, progress.save mtime after continue

### Status

[OK] **Completed**

### Next Steps

- Finish dual-instance GAME_OVER save verification on current main DLL
- Do not archive 00-bootstrap-guidelines until frontend spec files are filled


## Session 2: v0.10.5 release, Workshop public visibility and P3 closeout

**Date**: 2026-09-09
**Task**: v0.10.5 release, Workshop public visibility and P3 closeout
**Branch**: `main`

### Summary

收尾核对 v0.10.5 发布、P3 支持范围与 Workshop 默认公开修复；工作区干净，无活动任务需归档。

### Main Changes

- 此前已完成 v0.10.5 GitHub 发布、Steam 安装、P3.1-P3.4 核查与 Workshop 内容上传；相关证据见工作提交和 PRODUCT_PLAN_CURRENT.md 的 P2/P3 条目。
- 96bd410 将 workshop.json 默认设为 public；打包脚本更新已有 PublishedFileId 时默认 public，首次 ID=0 仍 private，显式 Visibility 参数优先。

### Git Commits

| Hash | Message |
|------|---------|
| `e3412eb` | (see git log) |
| `04d2466` | (see git log) |
| `15e483c` | (see git log) |
| `2838250` | (see git log) |
| `96bd410` | (see git log) |

### Testing

- [OK] 本次直接查询 Steam GetPublishedFileDetails：物品 3796486050，result=1，visibility=0，title=STS2 AI Agent。
- [OK] 本次读取 Steam Workshop 本地订阅目录 STS2AIAgent.json，version=0.10.5；git diff --check 通过，收尾前工作树干净。
- [OK] 此前验收记录：C# 核心测试 180 PASS；preflight-release 和发布产物检查通过；双开 GAME_OVER 短路径 save_verified=true；P1.3 超限 UI 与 P3.3 外部 MCP 客户端通过。本次未重跑这些测试。

### Status

[OK] **Completed**

### Next Steps

- PRODUCT_PLAN_CURRENT.md 尚存过时基线和 P2.5 部分完成描述，需要后续文档同步；当前已确认 Workshop 公开及本地订阅版本，但本次未从订阅目录启动游戏验证加载。


## Session 3: Documentation archive and Workshop loading acceptance

**Date**: 2026-09-09
**Task**: Documentation archive and Workshop loading acceptance
**Branch**: `main`

### Summary

归档五份过时文档并保留兼容入口，对齐当前状态、中英文 README、CHANGELOG 与 Workshop 发布说明，记录用户启用重启后的订阅加载验收。

### Main Changes

- P2.5 按订阅加载冒烟范围标为完成：用户截图与重启说明，本次日志从 Workshop 3796486050 加载 DLL/PCK，PID 40664，health 0.10.5 ready，state/actions MAIN_MENU。

### Git Commits

| Hash | Message |
|------|---------|
| `5ff7303` | (see git log) |

### Testing

- [OK] 18 份变更文档中 59 个本地链接有效；check_release_package.py --source-root . 通过；git diff --cached --check 通过。纯文档提交未重跑构建或游戏测试。

### Status

[OK] **Completed**

### Next Steps

- 完整对局、第二轮重启和升级回退不在本次订阅加载验收范围；文档及日志提交尚未推送。


## Session 4: Repo hardening: dep security, proactive chat, doc contract, verification gates

**Date**: 2026-09-10
**Task**: Repo hardening: dep security, proactive chat, doc contract, verification gates
**Branch**: `main`

### Summary

Closed issues #50/#51 by refreshing uv.lock (fastmcp 3.4.7) and package-lock.json (fast-uri 3.1.7, npm audit 0). Implemented off-by-default proactive teammate chat with selectable tone behind a read-only chat path, covered by 17 new C# core tests. Documented all 55 mod actions in docs/api.md with a set-equality guard. Archived the stale coverage gap list to history/ with a redirect and marked five dated records. Added check_verification_gates.py plus a negative-path self-test and wired both into preflight and CI. Full offline sweep green; proactive chat remains not live-validated.

### Git Commits

| Hash | Message |
|------|---------|
| `2bc857d` | (see git log) |

### Status

[OK] **Completed**


## Session 5: v0.10.6/v0.10.7/v0.11.0 releases, acceptance hardening, and game-language localization

**Date**: 2026-09-12
**Task**: v0.10.6/v0.10.7/v0.11.0 releases, acceptance hardening, and game-language localization
**Branch**: `main`

### Summary

Continued the thread that started in Session 4: acceptance hardening (card-grid selection metadata, combat-hand pick settling, run-boundary stop, locale-proof gate self-test), then three releases. v0.11.0 makes the mod follow the games language - Chinese stays the source text, every other language reads an English table, and the overlay rebuilds on a mid-session language change. Three places that keyed behaviour off Chinese wording were fixed. All three releases went to GitHub and the Steam Workshop; the v0.11.0 Workshop upload was first misjudged as failed, then confirmed by Steam own workshop_log.

### Main Changes

- v0.11.0 localization: Loc (table, language detection) split from LocSource (Godot side) so the lookup stays testable offline; five shards, ~336 entries; a call-site coverage test fails any Chinese literal with no English entry; a frozen-text test fails any field or computed-once property that resolves text at construction.
- Three behaviour bugs that only surfaced on an English client: the glossary keyword match, the co-op invite result, and the failure classifier all read Chinese wording; they now key off explicit flags and English candidates.
- Acceptance hardening: card-grid selection metadata read from the base screen type (enchant 1/1/0 -> 0/3/0, first pick 10036 ms -> 151 ms); a combat-hand pick reports pending until confirmed; the run-boundary stop restored; the gate self-test made locale-proof; the pytest-style invocation replaced with the runner the project actually uses.
- Three releases shipped to GitHub and the Steam Workshop: v0.10.6, v0.10.7, v0.11.0 (Workshop manifest 3781676487912021003).

### Git Commits

| Hash | Message |
|------|---------|
| `2f75e4a` | (see git log) |
| `f9330ba` | (see git log) |
| `84631b9` | (see git log) |
| `d77982a` | (see git log) |
| `e565073` | (see git log) |
| `bd93662` | (see git log) |
| `1ce254b` | (see git log) |
| `6aabb4f` | (see git log) |
| `a1bd5b5` | (see git log) |
| `06924e4` | (see git log) |
| `ed1c65f` | (see git log) |
| `dd71769` | (see git log) |

### Testing

- [OK] dotnet run --project STS2AIAgent.Tests -c Release: 232 PASS, exit 0
- [OK] uv run --locked python -m unittest discover -s tests: 55 tests OK
- [OK] check_verification_gates.py pass; preflight-release.ps1 exit 0
- [OK] Live on an isolated game copy: Chinese regression reads as before; English cold start and mid-session language switch verified by screenshot; state payload glossary and deck lines read English

### Status

[OK] **Completed**

### Next Steps

- The English wording is machine-translated and was not reviewed by a native speaker; only Chinese and English were exercised.
- v0.10.7 has no GitHub tag - its Workshop upload predates the release, so the Releases list jumps from v0.10.6 to v0.11.0. Backfill the tag if the list should be contiguous.
- Steam Workshop uploads stall on manifest fetches from steampipe-partner.akamaized.net; judge success from Steam workshop_log.txt, never from the uploader stdout.


## Session 6: Agent trust hardening: 5 goals from the 2026-09-12 audit

**Date**: 2026-09-12
**Task**: Agent trust hardening: 5 goals from the 2026-09-12 audit
**Branch**: `main`

### Summary

Ran six parallel scouts over the mod, MCP sidecar, docs, tests, and gameplay skill, then turned the findings into five independently verified deliverables executed in importance order. 1) Action trust: resolve_rewards rejects an explicit out-of-range index instead of quietly taking the first card, an open modal no longer counts as a finished continue_run/embark/open_character_select transition, remove_card_at_shop surfaces a failed purchase, bundle actions fail with 503 instead of a fabricated empty state, and a play_card that never left the hand rolls its counters back (72c96fd). 2) Bounded waits: nine handlers awaited a native task with no deadline while Router waits on the handler; each now goes through WaitForGameTaskAsync with a background observer and an honest pending, plus a source contract proven by mutation probe (12c35b3). 3) Agent contract: get_game_state carries compact_agent_view, wait_until_actionable returns actionable on every path, the full-profile resolve_rewards stops requiring option_index, and the gameplay skill stops naming raw-only fields for the compact view it reads (33b137e). 4) Screens and indexes: choose_timeline_epoch shares the state index space, and FAKE_MERCHANT / PATCH_NOTES / CARD_INSPECT / RELIC_INSPECT / FEEDBACK stop being dead ends (5457e0d). 5) Docs baseline: the state page cites v0.11.0, AGENTS.md lists all five version files, the route lists match the router, and preflight now runs the CI-only gate self-tests (3f55a3f). C# tests 232 -> 272, MCP tests 55 -> 76, gates and a 12-step preflight green. No version bump, tag, or Workshop upload.

### Git Commits

| Hash | Message |
|------|---------|
| `3f55a3f` | (see git log) |

### Status

[OK] **Completed**


## Session 7: Reward choice threading: remove the cross-request static card choice

**Date**: 2026-09-12
**Task**: Reward choice threading: remove the cross-request static card choice
**Branch**: `main`

### Summary

Follow-up to the action-trust child, chosen by the user as option A. resolve_rewards kept its card choice in a process-wide static field that only the card-reward consumer cleared; a drain that never reached a card-reward screen left the value behind, so a later collect_rewards_and_proceed could inherit it and silently skip a card reward or pick a card the caller never asked for. The choice now lives in a per-request RewardFlowChoiceState passed down through DrainRewardFlowAsync, collect_rewards_and_proceed asks for the automatic choice explicitly, and consuming once per drain keeps the within-call behavior identical including the out-of-range 409. _cardRewardSkipped was deliberately left byte-identical because the skip flow needs it to outlive the request; its timeout-exit staleness is recorded as a separate follow-up. A semantic change is documented in docs/api.md: an explicit choice belongs to the call that carries it, so retrying a pending resolve_rewards with collect_rewards_and_proceed resolves automatically. Review fixed two vacuous tests, one of which would have let the explicit index be dropped silently. C# 278 PASS, MCP 76 OK, gates and a 12-step preflight green.

### Git Commits

| Hash | Message |
|------|---------|
| `abc195f` | (see git log) |

### Status

[OK] **Completed**


## Session 8: Reward skip scope: bind the skip intent to the reward set that recorded it

**Date**: 2026-09-12
**Task**: Reward skip scope: bind the skip intent to the reward set that recorded it
**Branch**: `main`

### Summary

Second follow-up to the action-trust work, chosen by the user as option B. The card-reward skip lived in a process-wide bool that only the drain branch observing a screen change cleared; a drain ending through its own proceed click, the empty-reward escape, or the timeout left it set, and the reward-button filter then excluded the CardReward button from the next reward set, silently dropping that set card reward. The intent genuinely must outlive its request (skip_reward_cards is one call and the collect that follows is another), so instead of threading it the fix keys it to the owning NRewardsScreen instance id: recorded on the selection screen by finding the sibling that shares the overlay stack, honored only for that same set, and ignored when the owner cannot be resolved. The fail-safe direction is deliberate: a missing identity re-shows a visible card reward rather than dropping one. Decompiled evidence confirms the selection overlay is pushed onto the same stack while the owning rewards screen stays there as a live sibling, so the recorded and read ids name the same object. Review found no defect and used mutation probes to prove the new tests are not vacuous (renaming the predicate to an unscoped bool still fails; removing the id-zero guard fails). C# 290 PASS, MCP 76 OK, gates and a 12-step preflight green.

### Git Commits

| Hash | Message |
|------|---------|
| `fa7ffbe` | (see git log) |

### Status

[OK] **Completed**


## Session 9: Close seven audit gaps and harden the offline test floor

**Date**: 2026-09-12
**Task**: Close seven audit gaps and harden the offline test floor
**Branch**: `main`

### Summary

Eight tasks from the residual audit: invite_ai_teammate now classifies on a structured DualLaunchOutcome instead of Chinese substrings, three menu waits stop treating a destroyed node as proof of success, the scene field sets match the real export schema, the dead client.py block is gone, the knowledge root no longer guesses outside a checkout, and the previously untested ApiException/JsonHelper/HttpServer-policy/network_server/knowledge/handoff surfaces have tests.

### Main Changes

- AgentRuntime records a per-branch DualLaunchOutcome; the invite handler maps it to completed/pending/409 invite_failed (was: substring match on localized text)
- MenuTransitionPolicy gains IsSubmenuObserved/IsFlagObserved; select_deck_card, confirm_timeline_overlay, crystal_set_tool and run_console_command stop reporting optimistic success
- GameDataFilter scene fields corrected against a new GameDataExportSchema constant, pinned by a bidirectional drift test
- knowledge.py stops guessing the repo root (parents[3]) and degrades reference_files to an empty list outside a checkout
- Removed an unreachable execute_action block in client.py; added an AST guard for all 55 per-action methods

### Git Commits

| Hash | Message |
|------|---------|
| `206a0e8` | (see git log) |
| `adcb49b` | (see git log) |
| `f57cb04` | (see git log) |
| `c08d764` | (see git log) |
| `92f67a2` | (see git log) |
| `52bafd0` | (see git log) |
| `26da6bc` | (see git log) |
| `e373c90` | (see git log) |

### Testing

- [OK] C# offline runner: 311 PASS / 0 FAIL (was 290); each of the eight commits re-verified in a detached worktree (296/300/303/311)
- [OK] MCP unittest: 159 OK (was 76); stage commits verified at 82/121/156/159
- [OK] check_verification_gates.py, check_release_package.py --source-root ., preflight-release.ps1 all pass

### Status

[OK] **Completed**

### Next Steps

- Residual small issues: zero-reference scripts, mcp_server/data/eng packaging, select_deck_card availability asymmetry, crystal_clear_cell doc gap
- Decide whether to push the 26 commits and whether to tag v0.10.7


## Session 10: Give the compact view what it needs, keep errors truthful, and gate the drift

**Date**: 2026-09-12
**Task**: Give the compact view what it needs, keep errors truthful, and gate the drift
**Branch**: `main`

### Summary

Six tasks: two residual audit gaps (select_deck_card availability, the crystal_clear_cell doc exemption), the compact agent view finally carries powers / intent numbers / card and relic ids / overlay context / party, the in-game path keeps ApiException code and retryable, autoplay stops both spinning and false-stopping, three drift-prone facts got gates, and the scene field tables agree across languages.

### Main Changes

- compact agent_view gains powers (both sides), enemy intents with numbers, card_id/relic_ids/card_ids, modal.underlying_screen, unlock, and party summaries
- GetDeckSelectionOptions loses its generic subtree fallback so availability equals executability; the executor guard stays as defence
- AgentErrorEnvelope gives the in-process path the same error fields as the HTTP envelope; skills contract explains how to use them
- NoProgressPolicy + AutoPlayRecovery: repeat threshold on (action, state fingerprint) and a bounded unsettled budget instead of counting pending as failure
- check_verification_gates.py gains api-facts (mod_version, screen enum, default port); README tool list bound to the registry; static packaging check now runs in CI
- Cross-language scene field alignment test closed the C#/Python drift; data/eng README now says it is a snapshot, not a source

### Git Commits

| Hash | Message |
|------|---------|
| `ca12a4f` | (see git log) |
| `79d8747` | (see git log) |
| `f7dccfe` | (see git log) |
| `40735b5` | (see git log) |
| `ed1b810` | (see git log) |
| `98fca75` | (see git log) |

### Testing

- [OK] C# offline runner: 335 PASS / 0 FAIL (was 313); every commit re-verified in a detached worktree (313/317/325/335/335/335)
- [OK] MCP unittest: 167 OK (was 159); stage commits verified at 159/159/159/159/160/167
- [OK] verification gates (now 5), check_release_package, preflight all pass

### Status

[OK] **Completed**

### Next Steps

- Deferred to the user: whether to delete mcp_server/data/eng (1.2 MiB, no reader, now honestly documented), whether to keep the zero-reference scripts, and whether to push the accumulated commits or tag v0.10.7
- ResolveNonModalScreen still shadows NCardRewardSelectionScreen => REWARD behind a generic CARD_SELECTION branch; changing it moves /state.screen semantics and needs live confirmation
- Live-only: compact field values, invite outcome, timeline overlays, and the port/dual-instance paths


## Session 11: Take docs under version control, wire the orphan self-test, guard packaging

**Date**: 2026-09-12
**Task**: Take docs under version control, wire the orphan self-test, guard packaging
**Branch**: `main`

### Summary

Four residuals closed: docs/ became a controlled directory with a docs-tracked gate (two documents had drifted out of version control and the local doc-marks gate was checking a file CI could not see), the operations spec was refreshed against the six gates and five version sources, the zero-reference budget-proxy self-test was wired into preflight and CI, and package-release now refuses to build on a version mismatch. Wiring the self-test surfaced that it was 5-8 percent flaky - the mock upstream spoke HTTP/1.0 and never read the request body, which Windows turns into an RST the proxy reports as a 502.

### Main Changes

- docs-tracked gate plus .gitignore cleanup; the two untracked pages are now committed
- preflight gains a budget-proxy step (12 to 13 OK steps) and CI runs the same command
- selftest harness fixed: MockUpstream protocol_version HTTP/1.1 plus _drain_body; 0 failures in 400 posts and 15/15 full runs, versus 2/40 and 2/150 before
- package-release.ps1 asserts five-way version agreement before building anything
- operations spec and index refreshed: six gates, five version sources, a script inventory naming the deliberately unwired scripts

### Git Commits

| Hash | Message |
|------|---------|
| `6e56e1d` | (see git log) |

### Testing

- [OK] C# 335 PASS / 0 FAIL; MCP 167 OK; six gates pass in a clean checkout; preflight 353 PASS / 0 FAIL with 13 OK steps

### Status

[OK] **Completed**

### Next Steps

- Screen-name truth: ResolveNonModalScreen's generic grid-holder branch returns CARD_SELECTION for the reward-card overlay, shadowing its own REWARD arm and telling the model to use an action that screen no longer offers
- Live-only: compact field values, invite outcome, package-rollout paths


## Session 12: 五个新目标：屏幕名遮蔽、内嵌契约、实机资产、编译覆盖、暴露口径

**Date**: 2026-09-12
**Task**: 五个新目标：屏幕名遮蔽、内嵌契约、实机资产、编译覆盖、暴露口径
**Branch**: `main`

### Summary

把探子给出的 5 个发现逐个立项、派 worker 实现、按任务拆提交，并在临时 worktree 里逐个提交验证树可构建；全部归档。

### Main Changes

- ResolveNonModalScreen：奖励选牌浮层改报 REWARD（此前被通用网格分支遮蔽成 CARD_SELECTION）
- 内嵌游玩契约不再要求游戏内调用 health_check，debug 指令标注为外部 MCP 专用，README 两语对齐
- 新增 Roslyn 语法覆盖：16 个未编译源文件（18k 行）进入 CI 保护，未编译集合由白名单断言钉住
- 三个实机脚本与当前契约对齐（open_timeline 断言、timeline 索引、lobby flow 屏幕分支）
- payload 派生值与执行口径同源；skip_reward_cards 两端都改为只认 enabled 的替代按钮

### Git Commits

| Hash | Message |
|------|---------|
| `eec80b9` | (see git log) |
| `c060554` | (see git log) |
| `335ba0a` | (see git log) |
| `94765bc` | (see git log) |
| `492722a` | (see git log) |

### Testing

- [OK] 5 个提交各在临时 worktree 验证：336/339/341/341/348 PASS，0 FAIL；6 gate 全绿
- [OK] 最终态：C# 348 PASS/0 FAIL、MCP 167 OK、gates 6 道、check_release_package 通过、preflight exit 0

### Status

[OK] **Completed**

### Next Steps

- 用户拍板：累积提交是否 push、mcp_server/data/eng 快照去留、准孤儿脚本、NCardPileScreen/NCardLibrary 是否新增屏幕名


## Session 13: 移除随包游戏数据快照（v0.5.0 遗留、v0.6.1 已被 Mod 导出取代）

**Date**: 2026-09-12
**Task**: 移除随包游戏数据快照（v0.5.0 遗留、v0.6.1 已被 Mod 导出取代）
**Branch**: `main`

### Summary

考证 mcp_server/data/ 的设计意图后按用户决定移除：删目录与唯一校验它的测试，拆掉四处打包挂钩，README 与 AGENTS.md 的陈旧表述同步修正；用解包 wheel/sdist 断言产物里确实没有它。

### Main Changes

- 删除 mcp_server/data/（21 个文件）与 tests/test_packaged_game_data.py
- pyproject.toml 去掉两个 hatch force-include；package-release.ps1 去掉复制 data 的行
- check_release_package.py 删除 ARTIFACT_DIRECTORIES 与其遍历循环（has_directory 保留）
- README 两语目录树、mcp_server/README.md 数据来源说明、AGENTS.md 本地指引同步

### Git Commits

| Hash | Message |
|------|---------|
| `5cc314f` | (see git log) |

### Testing

- [OK] 构建产物解包断言：wheel 11 项 / sdist 29 项，零 data/ 路径
- [OK] 干净 worktree 验证：C# 0 FAIL、6 gate 绿、package source 契约通过、MCP 165 OK

### Status

[OK] **Completed**

### Next Steps

- 用户拍板：累积提交是否 push（本地 main 已领先 origin/main）


## Session 14: 首次推送与 CI 暴露的两个环境耦合问题（.NET SDK 选择、8.3 短路径）

**Date**: 2026-09-12
**Task**: 首次推送与 CI 暴露的两个环境耦合问题（.NET SDK 选择、8.3 短路径）
**Branch**: `main`

### Summary

推送 64 个提交后 CI 连红两次：Roslyn 覆盖测试被 .NET 10 SDK 的 CS1705 打断、knowledge 路径测试撞上 Windows 8.3 短名。两者都只在真实 CI 暴露，均已修复并可否证验证，最终 CI 全绿 13/13。

### Main Changes

- 新增 global.json：把 SDK feature band 钉到工作流声明的 9.0.x，避免 pick 到 .NET 10 的 Roslyn
- knowledge 参考文件路径测试改为两侧统一 realpath+normcase 比较（真实 8.3 别名下复现并验证）

### Git Commits

| Hash | Message |
|------|---------|
| `82c804f` | (see git log) |
| `61146a5` | (see git log) |
| `746f154` | (see git log) |

### Testing

- [OK] CI 746f154c 全绿：13 步全过（含此前从未在 CI 运行的 11 步）
- [OK] local：C# 348/0 FAIL、MCP 165 OK、6 gate、mod build、package source 契约全绿

### Status

[OK] **Completed**

### Next Steps

- 可选：是否为本轮补 tag / 是否把分支保护改成允许直推


## Session 15: 牌库/牌堆查看屏正名与逃逸 + PowerShell 语法 gate

**Date**: 2026-09-12
**Task**: 牌库/牌堆查看屏正名与逃逸 + PowerShell 语法 gate
**Branch**: `main`

### Summary

两屏不再冒充 CARD_SELECTION，并各自获得真实可用的关闭动作；新增 ps1-syntax gate 覆盖全部 30 个 .ps1，它首次运行就抓到 serve-sts2-network-mcp.ps1 一个自提交起就存在的真实语法错误。

### Main Changes

- NCardLibrary/NCardPileScreen 早退报 CARD_LIBRARY/CARD_PILE，不再被通用网格分支遮蔽
- submenu 栈查找上移到基类 NSubmenuStack，局内图鉴因此可关闭
- 可关闭看牌屏收敛为单一判定，探针/执行端/等待条件同源，牌堆屏经 close_cards_view 可退
- 新增 ps1-syntax gate（只解析不执行，无 .ps1 或无解释器时跳过说明）并修复它抓到的真实故障脚本

### Git Commits

| Hash | Message |
|------|---------|
| `5b3439c` | (see git log) |
| `abb99c5` | (see git log) |

### Testing

- [OK] 5 组改坏-红-还原-绿；C# 351 PASS/0 FAIL；MCP 165 OK；7 gate 全绿；preflight 372 PASS/0 FAIL
- [OK] 两个提交各在临时 worktree 验证树可构建

### Status

[OK] **Completed**

### Next Steps

- 推送两个提交；实机确认牌堆屏 BackButton 与局内图鉴 Pop 的行为，以及顺带修好的其它局内 submenu
