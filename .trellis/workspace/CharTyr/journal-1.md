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
