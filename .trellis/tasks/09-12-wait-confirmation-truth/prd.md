# 节点消失不等于目标达成：修正菜单等待与四个动作处理器

## 背景

`GameActionService.cs` 的菜单等待把"我盯着的那个节点被销毁了"当成"目标状态已达成的证据"：

| 等待助手 | 行 | 危险写法 | 后果 |
|---|---|---|---|
| `WaitForMainMenuSubmenuOpenAsync` | :5609-5630 | `if (!IsInstanceValid(screen)) return true;` | 主菜单被销毁（例如直接进了游戏）也算"子菜单已打开" |
| `WaitForCharacterSelectionTransitionAsync` | :5826-5829 | 同款 `return true;` | 选人界面消失 ≠ 已选中该角色 |
| `WaitForLobbyReadyTransitionAsync` | :5880-5883 | `return ready;` | `ready=true` 时房间销毁被报成"已准备" |

`open_timeline`（:588 `PushSubmenuType<NTimelineScreen>` → 等待）是当前唯一的直接受害动作：
主菜单一旦被销毁，`status` 直接报 `"completed"`。`WaitForMainMenuSubmenuOpenAsync` 另有
`open_character_select`（:540）与邀请链路（:4809/:4816）两个调用点。

同批要一起收口的四个处理器（都已定位、都属"报的结果比事实更乐观"）：

1. `select_deck_card`（:2028+）守卫宽于执行识别：`CanSelectDeckCard` 放行但战斗手牌/卡牌网格
   两条元数据都不匹配时会点空，然后只回 `pending`。
2. `confirm_timeline_overlay`（:702+）三个浮层分支复用同一点击路径，点击前不重新校验可用性。
3. `crystal_set_tool`（:1607+）`status` 硬编码 `"completed"`，需以证据确认 `TrySetCrystalSphereTool`
   的返回值是否真的等价于"工具已切换"。
4. `run_console_command`（:4924+）`consoleTimedOut` 只进 message 不进 `status`：命令任务超时后
   只要屏幕稳定就报 `completed`。

## 目标

1. 三条等待规则改为"成功必须被**观察到**"：源节点消失只能让等待**停止**，不能让它**成功**。
2. 规则以纯策略表达并离线测试（当前测试工程无法编译 `GameActionService.cs`）。
3. 四个处理器：要么给出可观测的成功证据，要么显式报 `pending`/409/503，不允许静默乐观。

## 验收标准

- [x] 三处危险写法消失，且替换逻辑在"目标状态未观察到"时返回 false/pending。
- [x] 新增/扩展纯策略（`STS2AIAgent/Game/MenuTransitionPolicy.cs`，已编入测试工程）承载判定规则，
      并有真值表测试（每种输入组合都断言；改坏即红）。
- [x] `select_deck_card`：无法定位到任何可点击选项时抛 409 `invalid_action`（不得返回 `pending` 掩盖）。
- [x] `confirm_timeline_overlay`：每次点击前重校验目标可用；点击后以等待确认，未确认即 `pending`。
- [x] `crystal_set_tool`：若 `TrySetCrystalSphereTool` 的返回值不构成"工具已切换"的证据，
      改为回读确认（读 `GameStateService` 暴露的当前工具），并保持成功时仍报 `completed`。
- [x] `run_console_command`：命令任务超时时不得报 `completed`。
- [x] 全量门禁通过（C# 单测、MCP 单测、`check_verification_gates.py`、`check_release_package.py`、preflight）。

## 范围外

- 不新增错误码（沿用 `invalid_action` / `invalid_request` / `invalid_target` / `state_unavailable`）。
- 不改 `open_character_select` 的 `GetSubmenuType<NCharacterSelectScreen>()`——反编译证实它是懒创建，
  不会返回 null（见 research 文件），此前审计的 B8 是误报。
- 不改 `docs/api.md` 的动作契约块。
