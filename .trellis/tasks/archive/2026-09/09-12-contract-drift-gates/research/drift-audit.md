# 证据：会静默漂移的契约

## 门禁能力矩阵（已核实）

| 脚本 | CI | 本地 preflight | 说明 |
|---|---|---|---|
| `check_verification_gates.py` | ✓ `validate.yml:33` | ✓ `preflight-release.ps1:132` | 四个 gate：lockfile / api-doc / doc-marks / script-encoding |
| `check_release_metadata.py` | ✓ `:30-32` | ✓ `:96` | 五个文件版本一致 |
| `check_release_package.py` | ✗ | ✓ `:128`（`--source-root`） | 纯静态，本可进 CI |
| `test-verification-gates.ps1` | ✓ `:35-36` | ✓ `:136` | 门禁自测 |

## 已漂移/裸奔的三条

1. `docs/api.md:124` `"mod_version": "0.11.0"`，文档 `:150` 自述要与 `Router.cs` 一致；
   没有任何 gate 读它。`rg 'mod_version' docs` 只命中这两处。
2. 屏幕清单：`ScreenResolutionContractTests` 只覆盖 `return currentScreen switch` 里的 26 项，
   `GameStateService.ResolveNonModalScreen` 的早退分支（`UNLOCK` / `CARD_SELECTION` /
   `CARDS_VIEW` / `MULTIPLAYER_LOBBY` / `COMBAT`）不在其内；文档侧（`docs/api.md:70-95` 25 行、
   SKILL.md 的 Screen Routing）无脚本对齐。
3. `mcp_server/README.md` 的"当前工具"节缺 14 项（对照 `_LEGACY_ACTION_TOOLS` 的 54 项），
   没有测试读 README。

## 已受保护的（对照）

- 动作契约：`GameActionService` switch ↔ `docs/api.md` 契约块（api-doc gate，55 个动作）。
- 版本五处一致：`check_release_metadata.py`（CI + preflight）。
- 依赖下限与 lock 一致：`check_verification_gates.py` 的 lockfile gate。
- SKILL.md 共享契约 ↔ `PlayPrompt`：`McpPlayerSkillTests`（测试工程内）。
