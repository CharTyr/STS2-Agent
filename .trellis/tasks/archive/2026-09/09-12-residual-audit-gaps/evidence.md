# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `docs/api.md:1034` | 契约块里 `crystal_clear_cell` 行补上可选参数：`（\`x\`、\`y\`，可选 \`tool\`：\`big\` / \`small\`）`；行首仍是反引号动作名（api-doc 正则不受影响） |
| `mcp_server/tests/test_native_tool_alignment.py:285-291` | `_DOC_ARGUMENT_EXEMPTIONS` 清空为 `{}`，注释改为"两侧现已一致；陈旧豁免仍会失败" |
| `STS2AIAgent/Game/GameStateService.cs:1711-1725` | 删除 `GetDeckSelectionOptions` 的通用兜底分支（`currentScreen is Node rootNode` → 子树扫描），保留三段判据与末尾空返回，并写明"为什么故意不做子树扫描" |
| `STS2AIAgent.Tests/DeckSelectionAvailabilityTests.cs`（新） | 2 条源码契约断言（`AvailabilityMatchesTheExecutableSet` / `ExecutorKeepsItsGuard`） |

## 验收

| 标准 | 证据 |
|---|---|
| 契约行含 `tool` 且形态未破 | 解析结果 `crystal_clear_cell docs args = ['tool','x','y']`；gate 仍报 `55 actions match` |
| 豁免已删、MCP 全绿 | `_DOC_ARGUMENT_EXEMPTIONS = {}`；`uv run --locked python -m unittest discover -s tests` → 159 OK（当时快照） |
| 兜底分支消失、前三段保留 | `git diff` 为 -7/+6（删分支 + 加说明注释）；三段判据仍在 `:1690-1715` |
| 新增回归测试可跑 | 主代理注册后：`PASS DeckSelectionAvailability.MatchesExecutableSet` / `PASS DeckSelectionAvailability.ExecutorKeepsGuard` |
| 不变式前提不触发 | `CanSelectDeckCard` 与 `BuildSelectionPayload`（`:4323-4329`）同源调用；返回 0 张时 `selection` 为 null，故"selection 非空 ⇒ available_actions 含该动作"的前件不成立 |
| 可否证 | 删文档里的 `tool` → `test_documented_action_arguments_match_registered_legacy_tools` 红；把豁免加回 → "stale exemption" 红；把兜底加回 → `AvailabilityMatchesTheExecutableSet` 红；删 409 守卫 → `ExecutorKeepsItsGuard` 红 |

## 门禁

C# 全量 335 PASS / 0 FAIL；`dotnet build` 0 警告 0 错误；MCP 167 OK；
`check_verification_gates.py`（5 个 gate）`check_release_package.py` preflight 全绿。

## 只能实机验证

- 三类屏幕（`NCardRewardSelectionScreen` / `NCardPileScreen` / `NCardLibrary`）上 `select_deck_card`
  确实不再出现在 `available_actions`、`/state.selection` 不再出现（需 `state-invariants`）。
- 奖励选牌仍可经 `choose_reward_card` 完成。

## 范围外（已记录）

`ResolveNonModalScreen` 的通用分支仍会让奖励选牌浮层报 `CARD_SELECTION`（遮蔽
`NCardRewardSelectionScreen => "REWARD"`）。改它会改变 `/state.screen` 的对外语义，需单独评估。
