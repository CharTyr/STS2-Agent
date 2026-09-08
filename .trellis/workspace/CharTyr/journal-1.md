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
