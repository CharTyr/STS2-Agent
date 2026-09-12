# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Game/MenuTransitionPolicy.cs` | 新增纯策略 `IsSubmenuObserved`（null → false；目标类型含子类才算观察到）与 `IsFlagObserved`（源节点失效 → false） |
| `STS2AIAgent/Game/GameActionService.cs` | 三处等待助手改为"节点销毁 → `break` + 统一终判"；四个处理器收口 |
| `STS2AIAgent.Tests/MenuWaitObservationTests.cs`（新） | 4 条测试（2 条真值表 + 2 条源码契约） |

### 具体改动

- `WaitForMainMenuSubmenuOpenAsync`：`return true` → `break`，终判 `IsSubmenuObserved(当前屏幕类型, typeof(TSubmenu))`；目标确实成为活动屏幕时仍 `true`。
- `WaitForCharacterSelectionTransitionAsync`：`return true` → `break`，终判要求节点仍有效且角色 id 与请求一致。
- `WaitForLobbyReadyTransitionAsync`：`return ready` → `break` + `IsFlagObserved(sourceNodeValid, observed, requested)`；读 `isReady` 前先判 `IsInstanceValid`。
- `select_deck_card`（:2097-2118）：无任何可点击目标（战斗手牌元数据 / 卡牌网格元数据 / `NChooseACardSelectionScreen`）→ 409 `invalid_action`，不再回 `pending`。
- `confirm_timeline_overlay`（:732-805）：三分支点击前重取按钮并要求 `IsInstanceValid`+`IsVisibleInTree`+`IsEnabled`，取不到抛 503；tutorial 分支的静默空点改为显式报错。
- `crystal_set_tool`（:1658-1689）：点击后回读 `minigame.CrystalSphereTool == tool` 才 `completed`，否则 `pending`。
- `run_console_command`（:5046-5064）：`completed = screenStable && !consoleTimedOut`，`status`/`stable` 同步派生。

## 验收

| 标准 | 证据 |
|---|---|
| 三处危险写法消失 | `MenuWaitObservation.NoLostNodeSuccess` PASS（源码契约断言） |
| 纯策略 + 真值表 | `SubmenuType`（5 组合）、`SurvivingSource`（8 组合）PASS |
| 四处理器 | `ConsoleTimeout` PASS；其余三处由源码核对 + `dotnet build` 类型检查覆盖 |
| 可否证 | 同时改坏四处（两条策略返回 true、子菜单等待 `break`→`return true`、去掉 `!consoleTimedOut`）→ 4 条测试全红（`PASS=307 FAIL=4`），还原后 311 PASS |

## 门禁

C# 单测 **PASS=311 FAIL=0**；`dotnet build` 0 警告 0 错误；`check_verification_gates.py` 通过；
MCP 159 OK；`check_release_package.py` 通过；preflight 通过。

## 只能实机验证 / 已知偏离

- 主菜单被销毁时目标子菜单是否已成为活动屏幕（决定 `open_timeline` 是 `completed` 还是 `pending`）、真实浮层可达性、timeline 索引契约。
- `select_deck_card` 的 409 是调用方可见的行为变化；`GameStateService.CanSelectDeckCard` 仍会在同类屏幕把它列进 `available_actions`（**可用性与可执行性仍不对称**，彻底收紧需要改 `GameStateService.cs`，超出本任务范围）。
- 对 design 字面要求的一处有意偏离：保留 `NChooseACardSelectionScreen` 为合法目标（它有自己的 `WaitForChooseCardSelectionResolutionAsync` 观测路径），否则会打死一条合法点击通道。
