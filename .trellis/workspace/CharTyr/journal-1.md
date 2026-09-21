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


## Session 16: 第一次真机实机会话：清单整理 + 两个只有实机能暴露的发现

**Date**: 2026-09-12
**Task**: 第一次真机实机会话：清单整理 + 两个只有实机能暴露的发现
**Branch**: `main`

### Summary

汇总实机清单成文档，并真跑一次实机会话：验证了奖励屏 REWARD 正名、timeline 全路径与索引契约、7 个数据集合；顺带发现并修复 /data/powers 整集合 500 与两处错误的 timeline 注释。

### Main Changes

- 修 /data/powers 500（本地化键缺失打断整集合导出）
- 修正脚本里错误的 timeline 按钮规则注释
- 新增 docs/live-validation-checklist.md 汇总实机项

### Git Commits

| Hash | Message |
|------|---------|
| `7888566` | (see git log) |
| `6478dae` | (see git log) |
| `4554c8d` | (see git log) |

### Testing

- [OK] 实机：奖励屏 REWARD 正名、timeline 全路径与索引契约负例、7 集合可达、两个脚本通过
- [OK] 离线：C# 351/0、MCP 165 OK、7 gate 绿、preflight 372/0；CI 4554c8d 全绿

### Status

[OK] **Completed**

### Next Steps

- card-viewer-screens 的实机项（牌堆屏/图鉴）需玩家点击或新增 mod 动作；存档待选一张奖励卡


## Session 17: 实机遗留三处收尾：分页 FTUE、场景派生元数据 id、联机基础血量

**Date**: 2026-09-13
**Task**: 实机遗留三处收尾：分页 FTUE、场景派生元数据 id、联机基础血量
**Branch**: `main`

### Summary

收尾 v0.12.1 实机验收留下的三处发现：分页 FTUE 本是正确行为（改为自解释契约）、get_relevant_game_data 的 item_ids 可省略、新增 base_max_hp 分离基础掷血与联机缩放。核对派生路径时又抓出两个缺陷（场景载荷为 null 时踩空崩溃、两份镜像对空串 id 的分歧），一并修掉并实机复验。

### Main Changes

- NCombatRulesFtue 非末页返回 pending 时改为自解释文案（三页是游戏设计，三次点击本来就是对的行为）
- get_relevant_game_data 的 item_ids 可省略：按当前屏幕从实况派生，场景无内容时回落角色级 id，C# 与 MCP 两份表由对齐测试钉死相等
- combat.enemies[].base_max_hp 新增：联机缩放前的基础掷血，与 monsters.min_hp/max_hp 同量纲
- 修掉派生路径踩 JSON null（FAKE_MERCHANT 归为商店却无 shop 载荷）与 C#/Python 对空串 id 的处理分歧
- 证据与契约写进 docs/live-validation-checklist.md 与 docs/api.md，状态页补 PR #101/#102

### Git Commits

| Hash | Message |
|------|---------|
| `d826935` | (see git log) |
| `7714f0b` | (see git log) |

### Testing

- [OK] 实机（隔离档 clientId 2026091001）：FTUE 三击 pending/pending/completed 后 modal 清空、回到 COMBAT
- [OK] 实机：MCP 工具面省略 item_ids 返回手牌/敌人/遗物，显式 id 与空集合行为不变
- [OK] 实机双人局：base_max_hp 9/33/13 对实况 19/72/28（base×人数×act0 系数），两个实例数字一致
- [OK] 离线：C# 382 PASS/0 FAIL、Python 167 OK、七道 gate 全绿、mod 构建 0 警告；玩家真档 183 文件哈希前后一致
- [OK] CI：#101 与 #102 的 contracts + Sourcery 全绿

### Status

[OK] **Completed**

### Next Steps

- FAKE_MERCHANT 的回落目前只有离线单测，未实机走到该屏
- 分页 FTUE 只在单人档复现（联机档该教学已完成）
- 三处修复尚未随任何 tag 发布（晚于 v0.12.1），下次发版时随包验证


## Session 18: 发布 v0.12.2：联机接力（外部接管路线）+ 工坊上传

**Date**: 2026-09-13
**Task**: 发布 v0.12.2：联机接力（外部接管路线）+ 工坊上传
**Branch**: `main`

### Summary

把主线积压的三批改动（#85 外部接管路线、#99 共享模型门禁、#101 三处实机收尾）发成 v0.12.2，并把工坊更新到同一版。打包时被产物检查拦下一个真实缺陷：README 里新增的相对链接没进打包改写表，于是补了链接修复与第八道离线 gate（packaged-links）。工坊上传因 Steam 客户端与 UGC 后端失联失败六次，重启 Steam 后一次成功。

### Main Changes

- 五个版本号文件升到 0.12.2，CHANGELOG 的 Unreleased 定为 v0.12.2 并补齐 #99 / #101 的条目
- 修 README 里没被打包改写的相对链接（#97 引入），不改改写表以免掩盖同类问题
- 新增 packaged-links gate：解析打包改写表 + import 产物清单与链接规则，在源文档上重放改写后校验本地链接是否都在产物里
- history/release-v0.12.2_2026-09-13.md 记录发布、上传与那次 Steam 失联故障的处置
- 状态页基线切到 v0.12.2

### Git Commits

| Hash | Message |
|------|---------|
| `40d1464` | (see git log) |
| `72b2a81` | (see git log) |
| `b64e7e7` | (see git log) |

### Testing

- [OK] preflight 全部步骤通过；八道 gate 全绿；gate 自测通过；打包产物检查（目录与 zip）通过
- [OK] gate 破坏性验证：README 写回裸 docs/api.md 时以退出码 1 报出该目标，逐字节还原后恢复通过
- [OK] CI：#104 与 #105 的 contracts 全绿（#105 首次失败是 runner 未能获取，重跑即绿）
- [OK] 工坊复核：file_size 1225733 与本地内容字节和相等、visibility=0、time_updated 2026-09-13 16:51:04、标签不变、英文说明未被覆盖

### Status

[OK] **Completed**

### Next Steps

- 工坊简体中文列表仍未更新（自 v0.11.0 起），需在工坊网页端手工粘贴 steam-workshop/description.zh-CN.txt
- 外部接管路线仍未在 Steam 双开路径复跑；FAKE_MERCHANT 的回落只有离线单测
- 工坊上传若再报 No Connection，先重启 Steam 客户端（已记进发布记录）


## Session 19: 合并 sachi4clover 的 overlay 邀请路线修复（PR #106）+ 记录

**Date**: 2026-09-13
**Task**: 合并 sachi4clover 的 overlay 邀请路线修复（PR #106）+ 记录
**Branch**: `main`

### Summary

把社区 PR #106 补的小修推到作者分支后合并：overlay 的邀请按钮此前没跟上 #85 的路线拆分，模型未验证时点它会走旧门禁，而同一个请求走 API 却能拉起队友。顺带修了错位的注释并新建 Unreleased 段。

### Main Changes

- PR #106：F8 窗口的「邀请 AI 队友」按钮改为按 FirstRunSetup.Evaluate(settings).ReadyToInvite 选路，两条说明随路线切换
- 把作者插错位置的 <summary> 移回 EveryStartEntryPointSharesTheModelGate（原实现让该测试失去注释、新测试挂了两段）
- v0.12.2 用掉了上一段 Unreleased，新建一段并把这次改动写进去
- 状态页：把误标的「v0.12.1 标签后未发布变更」标题改正，并补上 #105 / #106 两笔标签后变更

### Git Commits

| Hash | Message |
|------|---------|
| `0f63d6d` | (see git log) |
| `085c941` | (see git log) |

### Testing

- [OK] 在作者提交上开一次性 worktree：383 PASS / 0 FAIL、八道 gate 全绿
- [OK] 守卫有效性：还原 bug 后 CoopRoute.OverlayInviteRoute 立刻转红，文件逐字节还原后恢复绿
- [OK] CI：#106 与 #107 的 contracts 全绿

### Status

[OK] **Completed**

### Next Steps

- 工坊上的 v0.12.2 产物不含 #106 的 overlay 修复，需下次发版带上
- sachi4clover 提到的后续 PR（Continue 按钮 + 选角勾选框）叠在 #106 之上，CHANGELOG 往新建的 Unreleased 段续写
- 工坊简体中文列表仍未更新；工坊英文列表首屏「测试连接通过后再邀请」的措辞也待随下次上传调整


## Session 20: 工坊更新到 v0.12.3（只发工坊，不打 tag）

**Date**: 2026-09-13
**Task**: 工坊更新到 v0.12.3（只发工坊，不打 tag）
**Branch**: `main`

### Summary

把 #106 的 overlay 邀请路线修复发到工坊。因为 #106 改了 mod 代码，沿用 v0.10.7 先例升补丁号后只更新工坊、不打 tag、不建 Release；一次上传成功并用 Steam Web API 复核。

### Main Changes

- 五个版本号文件升到 0.12.3，CHANGELOG 的 Unreleased 定版为 v0.12.3 并写明「只发工坊、无 tag」
- workshop.json 的 changeNote 更新为 v0.12.3
- history/release-v0.12.3_2026-09-13.md 记录上传与复核
- 状态页：基线改为「GitHub 在 v0.12.2、工坊在 0.12.3」，并修正 #106 那条「尚未发布」的过期说法

### Git Commits

| Hash | Message |
|------|---------|
| `b0217b0` | (see git log) |
| `00d1789` | (see git log) |

### Testing

- [OK] preflight 全部步骤通过；八道 gate 全绿；check_release_metadata 报 0.12.3
- [OK] 工坊复核：file_size 1227269 与本地内容字节和相等、visibility=0、time_updated 22:31:22、标签与英文说明未变
- [OK] 上传一次成功（前置 HTTP_PROXY/HTTPS_PROXY），未复现 v0.12.2 的 No Connection

### Status

[OK] **Completed**

### Next Steps

- sachi4clover 的后续 PR（游戏内 Continue 按钮 + 选角勾选框）到位后，把 #106 与它攒一起做 GitHub 发布，或单独回填 v0.12.3 tag
- 工坊简体中文列表仍未更新，需在工坊网页端粘贴 steam-workshop/description.zh-CN.txt


## Session 21: 补发 v0.12.3 的 GitHub Release（工坊先行的完整化）

**Date**: 2026-09-13
**Task**: 补发 v0.12.3 的 GitHub Release（工坊先行的完整化）
**Branch**: `main`

### Summary

v0.12.3 先是只发工坊，本轮补上 tag 与 GitHub Release，把「工坊先行」变成一次完整发布。发布产物的 DLL 与工坊那份大小相同、PCK 字节一致，仅 70 字节构建元数据不同（PE 时间戳 / MVID / 调试目录），已记进发布记录以免后续把两渠道哈希差异误读成不同构建。

### Main Changes

- git tag -a v0.12.3 b0217b0 并推送；package-release 打包（目录与 zip 产物检查均通过）；gh release create 带 zip 与中文发布说明，GitHub 标记为 Latest
- history/workshop-upload-v0.12.3 合并重写为 history/release-v0.12.3，同时覆盖工坊与 GitHub 两次发布，并记录两渠道产物的 70 字节差异成因
- CHANGELOG 的 v0.12.3 段不再写「尚无 GitHub tag」，改为指向发布记录
- 状态页：v0.12.3 成为双渠道基线、v0.12.2 降为上一版，原「标签后未发布」小节改题为「v0.12.3 的内容」并补一行真正的标签后变更

### Git Commits

| Hash | Message |
|------|---------|
| `b0217b0` | (see git log) |
| `10c9135` | (see git log) |

### Testing

- [OK] Release 资产 digest 与本地 zip 一致（549933 字节，sha256 b9dc1a07…），asset 已上传、release 标记 Latest
- [OK] 八道离线 gate 全绿、check_release_metadata 报 0.12.3；#108 与 #110 的 CI 均 success
- [OK] DLL 逐字节比对：1187840 字节中仅 70 字节不同、7 段连续区间，PCK 完全一致

### Status

[OK] **Completed**

### Next Steps

- 工坊简体中文列表仍未更新，需在工坊网页端手工粘贴 steam-workshop/description.zh-CN.txt
- sachi4clover 叠在 #106 之上的后续 PR（游戏内 Continue 按钮 + 选角勾选框）尚未合并，将是下一版内容
- 若希望两渠道 DLL 哈希完全一致，可给 STS2AIAgent.csproj 开确定性构建后验证


## Session 22: 审阅并合并 sachi4clover 的联机界面 PR #111（续档按钮 + 自动选角开关）

**Date**: 2026-09-13
**Task**: 审阅并合并 sachi4clover 的联机界面 PR #111（续档按钮 + 自动选角开关）
**Branch**: `main`

### Summary

审阅 #111：两个 overlay 入口接在同一条 runtime 路由与同一份存档保护上；Sourcery 的 blocking finding 判为误报并逐条回帖；head 上 384 PASS / 0 FAIL、八道 gate 全绿、破坏性验证三次都转红，squash 合入 6a327d3，随后 PR #112 把状态页与实机清单补上。

### Main Changes

- 审阅 PR #111（作者 sachi4clover）：AI 队友页在邀请按钮上方新增「禁用自动选角」勾选框（勾上 = CompanionAutoSelectCharacter false，toggle 即存）、下方新增「继续上次联机对局」按钮（走 AgentRuntime.ContinueDualInstanceAsync，仅在 host 主菜单且有联机存档时可用）
- 反证 Sourcery 的 blocking finding 是误报：CoopLaunchPolicy.GetError 在 AgentRuntime.cs:804-815 就是同一份判断，两个 NetId 预检查在 DualInstanceCoordinator.cs:74-94 有 backstop，协调器还自己复核 MAIN_MENU——overlay 绕不开任何一处；已在 PR 下回帖并附原文
- PR #112：状态页新增「v0.12.3 标签后主线未发布变更」段（含这一笔改了 mod 代码、0.12.3 产物里没有它的提醒），docs/live-validation-checklist.md 的 [coop] 段登记待实机点击两项
- 验证细节：新增契约测试 CoopRoute.OverlayEntries 已注册、每个 <summary> 紧贴自己的方法、5 条新中文 key 都有英文词条、busy 只作用于被按下的按钮

### Git Commits

| Hash | Message |
|------|---------|
| `6a327d3` | (see git log) |
| `044d117` | (see git log) |

### Testing

- [OK] worktree b42bc5a：dotnet run --project STS2AIAgent.Tests = 384 PASS / 0 FAIL，scripts/preflight-release.ps1 全绿（八道 gate 逐条 OK）
- [OK] CI：#111 与 #112 的 contracts 均 success；Sourcery 在 #111 上 pass
- [OK] 破坏性验证三次（去掉勾选框回写 / Continue 改走非路线重载 / 勾选框语义取反）均让契约测试转红，随后文件逐字节还原

### Status

[OK] **Completed**

### Next Steps

- #111 改了 mod 代码，工坊与 GitHub 上的 0.12.3 里都没有它：是否发 v0.12.4（或攒一批）待定
- Continue 按钮与勾选框需要一次实机（真实联机存档）点击验收：按钮时机、读档接回队友、子进程读到的勾选值
- 工坊简体中文列表仍未更新（自 v0.11.0 起），待工坊网页端粘贴 steam-workshop/description.zh-CN.txt


## Session 23: v0.12.3 同号重发：把 #111 的两个联机界面入口补进已发布版本

**Date**: 2026-09-14
**Task**: v0.12.3 同号重发：把 #111 的两个联机界面入口补进已发布版本
**Branch**: `codex/record-recut-details`

### Summary

版本号保持 0.12.3，只换构建：工坊于 09-14 00:09 重新上传（file_size 1229829、内容 id 6028841468497339213），GitHub 的 v0.12.3 tag 与 Release 重切到 0f60ec4（新资产 552014 字节，SHA256 B7684C9F…）。五处版本号未动，CHANGELOG 的 Unreleased 段并入 v0.12.3 段并写明两份构建只能靠大小/哈希区分。

### Main Changes

- 发布准备：CHANGELOG 把 Unreleased 并入 v0.12.3 段（段首横幅记录 09-14 同号重发与区分口径）、workshop.json 的 changeNote 改为本轮文案；五处版本号刻意不动（check_release_metadata.py 只比对五处彼此一致，不比对 tag 与上一版）
- 重发执行：把两次旧产物目录改名留档（两个打包脚本都用 Get-UniquePath，不清旧目录会打出 -2 后缀）→ package-steam-workshop.ps1 → 分离进程 ModUploader 上传（一次成功）→ 工坊 GetPublishedFileDetails 复核（file_size 与本地内容字节和 1229829 完全相等）→ package-release.ps1 → 删旧 tag/Release 并按 0f60ec4 重建
- 载荷交叉核对：两渠道 DLL 大小相同（1190400），只有 72 字节、5 段不同（PE 时间戳与 MVID/调试目录），PCK 与 mod_id.json 逐字节一致
- 文档：#113 是发布提交，记录由 #114 合入——状态页与 history/release-v0.12.3 都改成「第一次（09-13）/ 第二次（09-14）」两段口径，原「标签后未发布变更」段改为重发内容

### Git Commits

| Hash | Message |
|------|---------|
| `6a327d3` | (see git log) |
| `0f60ec4` | (see git log) |
| `541734f` | (see git log) |

### Testing

- [OK] preflight-release.ps1 全绿（八道 gate 逐条 OK）；#113 与 #114 的 push / pull_request 四个 Validate run 均 success
- [OK] 工坊复核：file_size 1229829 与本地内容字节和相等、time_updated 2026-09-14 00:09:26、visibility=0、tags 与英文说明未变
- [OK] GitHub 复核：资产 552014 字节、上报 digest sha256:b7684c9f… 与本地一致、Latest 指向 v0.12.3；zip 内 CHANGELOG 已含 republished 2026-09-14 横幅

### Status

[OK] **Completed**

### Next Steps

- #111 的两个界面入口仍缺实机点击验收：按钮时机、读档接回队友、子进程读到的勾选值
- 工坊简体中文列表仍未更新（自 v0.11.0 起），待工坊网页端粘贴 steam-workshop/description.zh-CN.txt
- 同号重发的代价已写进 CHANGELOG 与状态页：0.12.3 指两份构建，下一版若升号即可恢复一一对应


## Session 24: 2026-09-14 v0.12.3 third build: live-pass fix plus the ten-goal sweep

**Date**: 2026-09-14
**Task**: 2026-09-14 v0.12.3 third build: live-pass fix plus the ten-goal sweep
**Branch**: `main`

### Summary

Finished the ten goals, merged seven PRs (#116-#122), then republished v0.12.3 a third time to both channels and recorded it.

### Main Changes

- MCP: keep the error envelope on game-data failures, stop hiding a broken event stream behind wait_until_actionable, correct the skill AnyPlayer target rule (#117)
- Release tooling: destructive case for the packaged-links gate, preflight runs the shared metadata checker, artifact check compares the three version sources inside the artifact (#118, #119)
- Diagnostics: a thrown room probe no longer looks like an empty room, and twelve empty catches in GameActionService now log with their chain name (#120, #121)
- Await contract reads the syntax tree across four game-driving files instead of one line shape in one file (#122)
- Republished v0.12.3 a third time: CHANGELOG second-re-cut paragraph, Workshop changeNote, tag moved to c2630a8, asset 555061 bytes, Workshop file_size 1232901 (#123, #124)

### Git Commits

| Hash | Message |
|------|---------|
| `6a4b3a0` | (see git log) |
| `3d2eefb` | (see git log) |
| `74d03c9` | (see git log) |
| `052dac8` | (see git log) |
| `68bbd0a` | (see git log) |
| `c2630a8` | (see git log) |
| `0d42f94` | (see git log) |

### Testing

- [OK] dotnet run --project STS2AIAgent.Tests -c Release: 386 PASS / 0 FAIL; three destructive checks red before restore
- [OK] preflight-release.ps1 exit 0; eight verification gates green including their self-tests
- [OK] Steam Web API GetPublishedFileDetails: file_size 1232901 equals the local content byte sum; GitHub asset digest matches the local SHA256

### Status

[OK] **Completed**

### Next Steps

- Paste steam-workshop/description.zh-CN.txt in the Workshop web editor; ModUploader cannot set a language
- The Steam-networked save path still has no live evidence; the 2026-09-14 pass covered the local-connection path only
- GameStateService.cs keeps seven empty catches of its own kind; read them one by one before extending the no-wordless-catch rule


## Session 25: POSIX path parity: one resolver, 29 offline assertions, and the defects that exposed

**Date**: 2026-09-14
**Task**: POSIX path parity: one resolver, 29 offline assertions, and the defects that exposed
**Branch**: `main`

### Summary

Turned the macOS/Linux path handling from three unexercised copies into one resolver with an offline test that CI enforces, fixed five real defects on the way, and merged both PRs.

### Main Changes

这轮把 macOS/Linux 的路径解析从「三个副本、从未运行过」变成「一份实现 + 29 条离线断言 + CI 闸门」，
并在过程中修掉 5 个真缺陷（其中 1 个是我自己引入的）。

起因是用户问「macOS 那两条默认路径是不是早期一个兼容 PR 带来的」。查证结果比预期散：

- `build-mod.sh:91-92` 来自 PR #3（cherry-pick 了从未合并的 PR #2，外部贡献者 Vinluo），
  该文件从落地至今只被一个 commit 碰过
- `lib-sts2.sh:34-35` 来自 PR #9（外部贡献者 WILLOSCAR，分支上提交署名是占位符 test@example.com）
- 同一串路径还有第三份，在进程匹配器的内嵌 Python heredoc 里
- 引入后路径文本一字未改，CI 只有 windows-latest，全仓找不到任何 macOS 实机证据

关键技术发现：整条 macOS 探测链可以在 Windows 的 Git Bash 上真跑（把 $HOME 指向夹具即可）。
这是「可验证」的落脚点 —— 不需要 Mac、不需要游戏、不需要联网。

实现：

- `scripts/lib-sts2-paths.sh`（新）：唯一知道安装布局的地方。解析顺序与 Windows 侧一致
  （参数 → 环境变量 → 探测 → 约定默认），探测覆盖三个约定 Steam 根 + 每个安装自己的
  `libraryfolders.vdf`（所以第二个库里装的游戏也能找到）。不起游戏、不开 socket。
- `scripts/test-lib-sts2-paths.sh`（新）：29 条断言。precedence、vdf 读取（转义反斜杠/尾斜杠/
  空文件/垃圾内容）、.app bundle 布局、data 与 mods 目录、进程匹配候选路径。
- `sh-syntax` 闸门：所有 .sh 过 bash -n，并执行上面那个测试。CI 与 preflight 都跑。
  选 bash 时优先 Git Bash —— Windows 上裸 `bash` 常是 WSL 启动器，能在 PATH 上解析成功、
  却读不了 C:/ 路径。
- `mcp_server/tests/test_posix_script_portability.py`（新）：Windows 侧守卫的镜像。安装布局
  只允许出现在解析器里；每个脚本必须 source 共享库而不是「提到」它；四条破坏性用例。

修掉的缺陷：

- 空/截断的 libraryfolders.vdf 直接进正则 → 抛异常，四个脚本全废
- 进程匹配器的 pgrep 回退在「已知 exe 路径」时没有 pattern → 无 ps 的机器上静默返回空
  （这一条是我自己在重构中引入的，被复核探子抓到）
- build-mod.sh 继承一个不存在的安装路径：下游全部退化成空串，报错说的是 data 目录
- 候选列表不去重，同一库经两个根列两次
- sts2_infer_game_root_from_executable 目录不存在时返回空串却算成功

两个独立探子复核第一版，除了上面的 pgrep 回归，还找出若干「注释声称 A、代码实际 B」的地方
（已逐条改）以及两处弱断言（已加强并补破坏性用例）。变异验证真的改坏源码再还原，五条变异
全部被对应层抓到。

合并时的一个坑：本地实测证明 squash 合 #127 会让 #128 在两个文档文件上冲突（两个 PR 改了同
一段），而 rebase/merge 零冲突。于是 #127 用 rebase 合、#128 本地 rebase 到新 main（git 自动
跳过已应用的提交）后改 base 再合，main 保持线性历史。



### Git Commits

| Hash | Message |
|------|---------|
| `5d53d50` | (see git log) |
| `ca0a9cd` | (see git log) |

### Testing

- [OK] bash scripts/test-lib-sts2-paths.sh: all 29 checks passed (needs no game, Steam or network)
- [OK] check_verification_gates.py: nine gates pass, including the new sh-syntax which parses 19 scripts and runs that test
- [OK] MCP suite 198 tests OK; C# suite 386 PASS; preflight exits 0
- [OK] Mutation check against the real sources: five mutations, each turned the responsible guard red
- [OK] Merge rehearsal: squash of #127 conflicts with #128 in two files, rebase/merge is conflict-free

### Status

[OK] **Completed**

### Next Steps

- Real-machine macOS acceptance is still the only way to promote path resolution to proven script usability
- lib-sts2.sh still hardcodes python3 in six places; on macOS and Linux that name is the one that exists, so it was left alone
- CONTRIBUTING.md still describes feature -> dev -> main while dev is 196 commits behind main and the last three PRs went straight to main


## Session 26: Revive dev: fast-forward it back and write down the rule that keeps it from drifting

**Date**: 2026-09-14
**Task**: Revive dev: fast-forward it back and write down the rule that keeps it from drifting
**Branch**: `main`

### Summary

Fast-forwarded dev from 196 commits behind main, added the resync rule to CONTRIBUTING, and merged the change through the revived flow (PR #129 targeting dev).

### Main Changes

用户选择「把 dev 重新拉起来」而不是改文档 —— 也就是让 CONTRIBUTING 描述的那套流程重新成立。

先前的事实：`origin/dev` 落后 `origin/main` 196 个提交，且 0 个独有提交 —— 严格祖先，所以快进
是无损的。CI（validate.yml）本来就覆盖 `dev` 的 push，没有 PR 指向 dev，dev 也没有分支保护。

做法：

- 先把本地那个未推送的 journal 提交推上 main（`4a01e22`）
- `dev` 快进到 main（`22907b0..4a01e22`），验证 origin/main 与 origin/dev 的 tree 完全相同、
  且旧 tip `22907b0` 仍是祖先（什么都没丢）
- 补上「何时把 dev 拉回来」这条规则：CONTRIBUTING.md 里写明每次有东西进 main 之后要
  `git push origin origin/main:dev`，并写清原因（每次合进 main 都会多出一个 dev 没有的提交，
  直接进 main 的 PR 更是把它的全部提交都加进去 —— 这就是 dev 变成旧 main 分叉的机制）
- 两处文档：CONTRIBUTING.md（受版本控制，走 PR）与 AGENTS.md（本地 gitignore，直接改）
- 这条改动走**恢复后的流程**提交：分支 → PR #129 → base 是 `dev` → 合入。也是第一个进 dev 的 PR

教训：规则要连原因一起写，否则第一个被丢掉的就是它。196 个提交的漂移之所以发生，是因为仓库里
没有任何地方说过什么时候该把 dev 拉回来。



### Git Commits

| Hash | Message |
|------|---------|
| `4a01e22` | (see git log) |
| `a92f5a6` | (see git log) |

### Testing

- [OK] git merge-base --is-ancestor 22907b0 origin/dev: exit 0, so the old dev tip is still reachable and the fast-forward lost nothing
- [OK] origin/main and origin/dev trees are identical after the fast-forward
- [OK] check_verification_gates.py on dev: nine gates pass; offline resolver test 29/29
- [OK] PR #129 CI: contracts and Sourcery both green, merged into dev

### Status

[OK] **Completed**

### Next Steps

- Next release goes dev -> main; after that merge, fast-forward dev back (the new rule)
- dev has no branch protection while main has a PR requirement; worth deciding whether dev should require the contracts check too

### Correction (same day)

那条规则的第一版是错的，而且**在写下的几分钟内就被现实驳回**。第一版说的是拿 ref 直接快进：
`git push origin origin/main:dev`。这只在 `dev` 是 `main` 的严格祖先时成立 —— 一旦有 feature PR
合进 dev（那正是 dev 存在的意义），dev 就有了 main 没有的提交，推送被拒：

```text
! [rejected]        origin/main -> dev (non-fast-forward)
```

修正后（PR #130）改成陈述不变式「dev 包含 main 的全部提交」配一个永远可用的操作：
`git fetch origin; git checkout dev; git merge origin/main; git push origin dev`。用 merge 不用
rebase，所以已发布的历史只会被追加；而当 dev 恰好没有独有提交时，这个 merge 自己会退化成快进，
也就是旧措辞描述的那个场景 —— 现在是被覆盖而不是被假设。

教训：规则要连原因一起写，而且**写完要真跑一次**。我这轮两个错都是同一类 —— 先写结论、后验证，
结果第一个错（squash 会让叠放的 PR 冲突）是在动手前实测发现的，第二个错（这条规则）是在应用时
被 git 拒绝发现的。两次都是「跑一次」救的，不是「想一遍」救的。


## Session 27: Close the drift that forced four re-cuts, ship v0.12.5, and collapse the action surface

**Date**: 2026-09-17
**Task**: Close the drift that forced four re-cuts, ship v0.12.5, and collapse the action surface
**Branch**: `dev`

### Summary

Worked back from a symptom: four same-version re-publishes in three days (v0.12.3 twice, v0.12.4 twice). The second v0.12.4 re-cut fixed a regression the first had introduced -- the combat gate read RunManager.ActionExecutor on the main menu, where there is no executor, so every /state answered 500 and the mod was unusable outside combat. 408 offline tests and nine gates passed that build. The question for this session was therefore not "what else is broken" but "what else is nobody watching".

Three answers, shipped as PR #141. (1) A source contract pins the in-combat guard around the queue read and keeps the gate the only place in the state builder that touches those members. (2) docs/api.md was missing a quarter of the state surface -- an audit found 91 fields across 23 payload records named nowhere a client could read, including seven whole sub-structures (session, multiplayer, multiplayer_lobby, character_select, timeline, modal, game_over) and the 43 key renames the compact agent_view performs, which is what MCP get_game_state returns by default. It also documented an `available` field on the three shop tables that none of those records has ever had. The api-facts gate now pins 16 payload records field-for-field, every GET /health key, the rename table against the builders, and a coarse net requiring every serialized field of every /state payload record to be named somewhere. (3) Nobody was counting the codebase: 82 files, 31,632 lines, two of them holding 49%. Line budgets now ratchet in both C# and Python, and a second check tightens a budget when its file shrinks.

CI caught a real defect in that work: gate messages quote Chinese headings from docs/api.md and the Windows runner's stdout is cp1252, so a gate that PASSED still exited 1 -- and a gate that failed would have had its message replaced by an encoding traceback. Fixed by writing UTF-8 with a replacing handler; self-test case 9i runs the suite under PYTHONIOENCODING=cp1252.

Then the live work. A baseline pass captured 2,346 back-to-back /state + /actions/available samples across 12 screens, which proved the two action surfaces agree at runtime and, separately, turned up two defects offline tests could not find. /state.screen never reported GAME_OVER after a death (the combat branch of ResolveNonModalScreen claimed it because the combat room stays active); eight samples in an unambiguous game-over state all reported COMBAT and the name never appeared once in 2,346. And a faulted game task said "the game task faulted" for twelve different actions, so a request the game refused read identically to a request that broke the mod -- live, a repeated `room Treasure` now names InvalidOperationException: Attempted to start new relic picking session while one was already occurring. Both fixed (#142, #143) and both re-verified on the patched build.

v0.12.5 shipped from f361bdb: GitHub and the Workshop, same source, one upload each, no re-cut. The Unreleased section added this session is where those post-tag fixes waited instead of forcing a fifth re-publish. Packaging emitted its first build-fingerprint.json, so the release record's hashes were copied from the build rather than collected by hand afterwards, and the Workshop's file_size 1238533 matched the local content bytes exactly.

Finally ADR 0001, implemented (#147). BuildAvailableActionNames and BuildAvailableActionsPayload were 301 and 609 lines consulting the same 50 Can* predicates to emit the same 55 names; they now both report one EnumerateAvailableActions walk, as a 12-line projection and a 14-line wrapper. GameStateService.cs 8,559 -> 8,295, budget lowered to match. Verified by replaying the baseline: 45 samples, all 12 screens, zero disagreements, twelve action sets matching exactly including PAUSE_MENU's empty set; the one difference traced to the run holding no potions, confirmed from run.potions. The contracts changed shape rather than being deleted -- ActionSurface.* now asserts neither surface has anywhere to put a predicate or name of its own, which is stronger than "two implementations agree".

The lesson worth keeping: both runtime defects were found by driving a real game, and the refactor was only safe because the baseline existed first. Doing it in the other order is exactly what produced the 0.12.4 regression.

### Git Commits

| Hash | Message |
|------|---------|
| `8e8835b` | (see git log) |
| `3825ead` | (see git log) |
| `abcd0db` | (see git log) |
| `d4af047` | (see git log) |
| `1b39c97` | (see git log) |
| `a4bff32` | (see git log) |
| `e644300` | (see git log) |
| `9bcaee6` | (see git log) |
| `dfd61bf` | (see git log) |
| `f1b2de7` | (see git log) |
| `0324285` | (see git log) |
| `ea732af` | (see git log) |

### Status

[OK] **Completed**


## Session 28: 收尾 PR #148 verification gates CI

**Date**: 2026-09-17
**Task**: 收尾 PR #148 verification gates CI
**Branch**: `refactor/split-monoliths`

### Summary

接手未完成的 CI 修复：让 arch-facts 与 doc-links 以 Git tracked 内容为准，移除 fresh checkout 中不可解析的本地链接，补未跟踪文件与目录回归测试；本地完整 gates/self-test 和 GitHub contracts 全绿。

### Git Commits

| Hash | Message |
|------|---------|
| `bbd1635` | (see git log) |

### Status

[OK] **Completed**


## Session 29: 发布 v0.13.0 并完成收尾

**Date**: 2026-09-19
**Task**: 发布 v0.13.0 并完成收尾
**Branch**: `main`

### Summary

接手中断的 v0.13.0 发布：验证代理连通，完成最终发布 DLL 隔离实机冒烟，创建 GitHub Release，上传并通过 Steam Web API 核对工坊内容，补齐发布记录与构建指纹，经 PR #162、#163 合入 main，并将 dev 快进同步到 main。

### Git Commits

| Hash | Message |
|------|---------|
| `dfb5659` | (see git log) |

### Status

[OK] **Completed**


## Session 30: Companion health semantics, demand-driven SSE, predicate split, and the debug churn hook

**Date**: 2026-09-20
**Task**: Companion health semantics, demand-driven SSE, predicate split, and the debug churn hook
**Branch**: `main`

### Summary

Five tasks, all merged through PRs #166-#171. Companion identity now accepts ready and degraded while keeping pid/port/role/service checks (live-verified 14/14 against a real degraded payload produced by forcing one registry lookup to miss). Companion /health returns null for the host-only keys instead of the host state machine's idle text, same key shape. The event poller is demand-driven (0 subscribers built nothing over 12s in the game) with a generation fence so a poll in flight when Stop runs cannot commit, and it will not restart if a poll refuses to end inside the shutdown budget. Full subscriber queues now fail the write and drop that subscriber instead of silently evicting the oldest event; a live run produced 'Disconnected 2 slow event subscriber(s)'. Two bugs were found by the live runs and fixed: every poll re-announced stream_ready (~60 frames in 10s on a stationary screen), and a suppressed repeat still consumed an event_id, which would have left gaps the docs read as loss. GameStateService predicates moved to GameStateService.Predicates.cs: 61 members, 771 lines, base 765 lines smaller with no line added, 4 shared helpers left in the base file, and PredicateRelocationContractTests stores a SHA-256 per moved declaration from the parent commit. A new debug-gated inject_event_churn action makes the overflow path reachable in a live run, which is how the last offline-only item was closed. Verification: 487 C# tests, 226 Python tests, 11/11 gates, MCP tool profile clean, gate self-test, preflight complete.

### Git Commits

| Hash | Message |
|------|---------|
| `c017302` | (see git log) |
| `aac032a` | (see git log) |
| `62972c7` | (see git log) |
| `c3c531e` | (see git log) |
| `2a1bd2a` | (see git log) |

### Status

[OK] **Completed**

## Session 31: v0.14.0: offline plan items, live validation, overlay redesign, and the release

**Date**: 2026-09-21
**Task**: v0.14.0: offline plan items, live validation, overlay redesign, and the release
**Branch**: `main`

### Summary

Finished the plan's offline track, validated the candidate on a real game with a real model, redesigned the in-game overlay, and shipped v0.14.0 to GitHub and the Steam Workshop.

### Main Changes

- 4.1: docs/openapi.json is generated by a stdlib deep module (scripts/api_schema.py) from Router routes and C# wire records; a twelfth gate rejects stale bytes.
- 1.6: an offline decision-quality benchmark (11 snapshot-evidenced cases, deterministic scoring) plus a read-only /state capture seam; no model or network.
- 4.2: sts2_mcp/payloads.py types the two payloads with a fixed field set; the old dict getters are unchanged.
- 5.3: max_tokens vs max_completion_tokens resolved from the server's own error, retried once.
- 6.1 + 6.4: GameStateService.cs 5037 -> 1336 lines, split by screen into 8 partials, proven byte-identical by 218 declaration hashes; ratchet lowered 5150 -> 1400.
- Overlay redesign: spacing and type scales, section cards, primary/secondary buttons, an active-tab pill, and four colour themes selectable in Settings.
- Fixed two live-found launcher defects: the isolated profile enabled only the first STS2AIAgent entry (game then read the mod as disabled), and a non-numeric --clientId silently validated a different profile. Both platforms now agree.
- Released v0.14.0: tag 5f9ea72 via PR #173 -> #174, GitHub asset 637550 bytes, Steam Workshop item 3796486050 updated (file_size 1360359 equals the local byte sum).
- Recorded the Workshop upload root cause: the Windows system proxy stalls SteamPipe content transfer while Steam still logs on; clearing it finished the upload in 8 seconds.

### Git Commits

| Hash | Message |
|------|---------|
| `a3edf1b` | (see git log) |
| `d39ef61` | test(ui): whitelist the three overlay files split out this session |

### Testing

- [OK] preflight-release.ps1 exit 0 - 12 static gates ok
- [OK] 572 core unit and contract tests pass (5 new OverlayLayout.* contracts added)
- [OK] SourceCoverage.UncompiledWhitelist caught the three split files only after they were committed, since it checks tracked sources
- [OK] Live game: mod loaded as 0.14.0, 27/27 reflected members, state-invariants failure_count 0, screenshots confirmed each visual fix
| `4cab34f` | (see git log) |
| `58a08ea` | (see git log) |
| `b32d56d` | (see git log) |
| `4151a37` | (see git log) |
| `e7520f8` | (see git log) |
| `c3118f1` | (see git log) |
| `9d3bc3b` | (see git log) |
| `9b92a8d` | (see git log) |
| `bd45774` | (see git log) |
| `2b7610a` | (see git log) |
| `51828ad` | (see git log) |
| `8bc7d2d` | (see git log) |
| `46d0da9` | (see git log) |

### Testing

- [OK] C# core suite exit 0; Python MCP 302 tests OK; all 12 verification gates pass; preflight-release.ps1 exit 0 with 0 build warnings.
- [OK] Live on game v0.111.0 in an isolated profile: 27/27 reflected members, mod-load --deep-check, state-invariants 0/0, patch-check replay with no mismatches.
- [OK] Real model (CommandCode / deepseek-v4.1-flash): connection test passed for both roles, unattended autoplay reached floor 3 with 45 reasoned decisions, and a local dual-instance co-op run played the teammate with the model.

### Status

[OK] **Completed**

### Next Steps

- Open a PR from feat/overlay-visual-redesign into dev; origin/dev has diverged so the documented fast-forward catch-up needs a human decision.
- Enable the mod in the game settings was needed to test at all: the Steam profile had both STS2AIAgent entries disabled, and start-game-session.ps1's --clientId seeding is ignored when the game launches through Steam.
- Steam two-instance co-op, a complete natural run, and the Vision path still need the game in front of a person.
- Provider sampling for SiliconFlow / OpenRouter / vLLM needs a key and a budget cap; only the CommandCode endpoint was sampled.
- Paste steam-workshop/description.zh-CN.txt into the item's Simplified Chinese listing; Steam exposes no API for non-default descriptions.


## Session 32: Overlay visual redesign and live-test defect fixes

**Date**: 2026-09-21
**Task**: Overlay visual redesign and live-test defect fixes
**Branch**: `feat/overlay-visual-redesign`

### Summary

Redesigned the in-game overlay (themes, layout, controls), split AgentOverlayHost into pages and settings partials, and fixed six defects that only a running game could show.

### Main Changes

- Seven colour themes instead of four, including the first light one (Ivory), with a swatch-grid picker that previews each palette.
- Layout pass: play tab became a dashboard, settings gained a jump bar and a fixed footer, co-op tab inverted so actions precede status, chat footer compacted.
- Split AgentOverlayHost.cs into Pages/Settings partials and extracted OverlaySwatch.cs, lowering the base file's size budget from 1,420 to 1,150.
- Fixed: swatch preview never drew (custom _Draw never called); theme switch skipped PanelContainers; long labels widened the panel to 691px and clipped every card; theme selection was overwritten by the rebuild a theme change triggers; jump buttons landed sections at the viewport bottom; ivory's drop shadow read as a smudge.

### Git Commits

| Hash | Message |
|------|---------|
| `a3edf1b` | (see git log) |
| `736f173` | test(ui): whitelist the three overlay files split out this session |

### Testing

- [OK] preflight-release.ps1 exit 0 - 12 static gates ok
- [OK] 572 core unit and contract tests pass (5 new OverlayLayout.* contracts added)
- [OK] SourceCoverage.UncompiledWhitelist caught the three split files only after they were committed, since it checks tracked sources
- [OK] Live game: mod loaded as 0.14.0, 27/27 reflected members, state-invariants failure_count 0, screenshots confirmed each visual fix

### Status

[OK] **Completed**

### Next Steps

- Open a PR from feat/overlay-visual-redesign into dev. The dev-resync rule from session 26 only applies while main is a strict ancestor of dev, and it is not right now: origin/dev carries the session-31 journal that main does not, so a fast-forward would drop it. Merge instead of reset, and the rule is lossless again once these commits reach main.
- The mod was disabled in the Steam profile, so testing needed it enabled by hand; start-game-session.ps1's --clientId seeding is ignored when the game launches through Steam, which is why the isolated-profile path did not reach it.


## Session 32: Dev audit: 16 fixes and offline verification

**Date**: 2026-09-21
**Task**: Dev audit: 16 fixes and offline verification
**Branch**: `dev`

### Summary

Repaired 16 confirmed dev defects; full offline preflight passed. No live-game validation or remote publication.

### Main Changes

- Fixed settings, teammate HTTP state, MCP diff, event lifecycles and cancellation; recorded findings and prevention rules.

### Git Commits

| Hash | Message |
|------|---------|
| `a4463fe` | (see git log) |

### Testing

- [OK] 589 C# tests; 315 Python tests; 12 verification gates; all 16 preflight steps; 165 cross-language cases matched.

### Status

[OK] **Completed**

### Next Steps

- Live-game validation remains deferred by the user.


## Session 33: Fix all five agent turn integrity findings

**Date**: 2026-09-21
**Task**: Fix all five agent turn integrity findings
**Branch**: `dev`

### Summary

Preserve accepted actions and partial receipts; enforce pending token and proactive budgets; validate action argument objects.

### Main Changes

- Added 26 regression and runtime-wiring tests; kept the shared turn gate through receipt accounting.

### Git Commits

| Hash | Message |
|------|---------|
| `763eabddb10fdb52ff941df6715014bc468a4f8e` | (see git log) |

### Testing

- [OK] C# 615 PASS/0 FAIL; Python 315 OK; full offline preflight exit 0; build 0 warnings/errors.

### Status

[OK] **Completed**

### Next Steps

- Live-game validation remains deferred; no push or release performed.
