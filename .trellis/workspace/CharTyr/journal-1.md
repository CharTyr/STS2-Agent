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
