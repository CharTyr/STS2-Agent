# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `scripts/check_verification_gates.py` | 新增 `api-facts` gate（mod_version / 屏幕清单 / 默认端口），注册进 `GATES` |
| `scripts/test-verification-gates.ps1` | fixture 补 `GameStateService.cs`/`HttpServer.cs`，新增 3 条自测用例（BOM 保持） |
| `.github/workflows/validate.yml` | 增加静态 `check_release_package.py --source-root .` 步骤 |
| `mcp_server/README.md` | 补齐 14 个漂移工具（合计 54），并用 `BEGIN/END LEGACY ACTION TOOLS` 标记界定契约区 |
| `mcp_server/tests/test_legacy_action_coverage.py` | 追加 README ↔ `_LEGACY_ACTION_TOOLS` 双向集合测试 |

## 验收

| 标准 | 证据 |
|---|---|
| 新 gate 输出 | `[gate] api-facts: ok` + 4 条明细（mod_version 0.11.0 一致；24 个屏幕全部在文档枚举；`MODAL` 由其它生产者产出故只提示；端口 8080 一致） |
| 可否证（三条） | 改 mod_version → `states mod_version 0.0.1 but … declares 0.11.0`；删文档 `CARDS_VIEW` → `can emit screens missing from …: CARDS_VIEW`；改 `DefaultPort = 9999` → 端口不一致报错。均已还原 |
| 门禁自测 | `test-verification-gates.ps1` 15 条用例全 PASS（含新 3 条与 restored） |
| README 绑定 | 删 `confirm_selection` 条目 → `does not list these legacy tools: confirm_selection` 红；还原绿 |
| CI 步骤 | 本地逐字复跑 `python scripts/check_release_package.py --source-root .` → exit 0 |

## 门禁

`check_verification_gates.py` 5 个 gate 全过（`api-doc` 仍 55 actions match）；MCP 167 OK；
preflight（含新 gate 的自测步骤）`Static preflight complete.`

## 未证明

GitHub Actions 未触发（本轮无 push），新步骤未在真实 runner 上跑过；命令与工作目录已逐字核对。
屏幕断言是单向（代码 ⊆ 文档），文档多列不会红（`MODAL` 属此类）。
