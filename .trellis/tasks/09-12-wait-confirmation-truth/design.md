# 设计：可观测的成功

## 规则（写成纯策略，别再散落在等待循环里）

1. **目标状态观察到才算成功**：失败的等待只有两种诚实收尾——"观察到目标"或"超时未确认"。
2. **源节点消失只是失去观测能力**：此时必须回到"当前活动屏幕"重新判定，而不是直接成功。
3. **请求值与观测值一致才算成功**：`ready` 这类布尔目标在源节点消失时必须报未确认。

## 落点

### 1. `STS2AIAgent/Game/MenuTransitionPolicy.cs`（纯策略，已在测试工程编译清单）

新增两个可测判定：

```csharp
// 目标子菜单是否被观察到：观察到的屏幕类型必须是目标类型的实例（含子类）
public static bool IsSubmenuObserved(Type? observedScreenType, Type targetSubmenuType);

// 布尔目标是否被观察到：源节点必须仍然有效，且观测值与请求值一致
public static bool IsFlagObserved(bool sourceNodeValid, bool observedValue, bool requestedValue);
```

语义要求：`sourceNodeValid == false` 时 `IsFlagObserved` 必须返回 `false`；
`observedScreenType == null` 时 `IsSubmenuObserved` 必须返回 `false`。

### 2. `STS2AIAgent/Game/GameActionService.cs`

- `WaitForMainMenuSubmenuOpenAsync<TSubmenu>`（:5609-5630）：
  循环体改为——每帧先跑现有 `props` 检查；当 `!IsInstanceValid(screen)` 时 **break**（不再 `return true`），
  循环结束后统一 `return MenuTransitionPolicy.IsSubmenuObserved(ActiveScreenContext.Instance.GetCurrentScreen()?.GetType(), typeof(TSubmenu));`
  要求：节点消失但目标确实已经成为活动屏幕时**仍返回 true**（不引入误报 pending）。
- `WaitForCharacterSelectionTransitionAsync`（:5826+）：同样把 `return true;` 换成 break +
  统一终判（终判是"当前角色 id == 目标 id"，节点消失时该表达式无从判断 → false）。
- `WaitForLobbyReadyTransitionAsync`（:5879-5894）：节点消失时不再 `return ready;`，
  循环结束统一 `return MenuTransitionPolicy.IsFlagObserved(IsInstanceValid(screen), ..., ready)`。
  注意读 `screen.Lobby.LocalPlayer.isReady` 前仍必须先判 `IsInstanceValid`（Godot 对象销毁后访问会抛）。
- `select_deck_card`（:2028+）：在计算 `TryGetCombatHandSelectionMetadata` /
  `TryGetCardGridSelectionMetadata` 之后，若两者都不成立（即没有可点击目标），
  抛 `ApiException(409, "invalid_action", ...)`，details 带 `action/screen/option_index/option_count`。
  先用 `CanSelectDeckCard` 与 `GetDeckSelectionOptions` 的现状确认这条路径确实可达（见 research）。
- `confirm_timeline_overlay`（:702+）：三分支（tutorial / unlock / 其余浮层）在**每次点击前**
  用 `GameStateService` 的对应 CanXxx/Getter 重新取值；取不到就抛 409/503；点击后沿用已有
  `WaitFor…` 确认，未确认返回 `pending`。
- `crystal_set_tool`（:1607-1645）：先读 `GameStateService.TrySetCrystalSphereTool` 的实现；
  若返回值已蕴含"当前工具 == 请求工具"，保持 `completed` 并在注释里写明证据；
  否则点击后回读当前工具，未变成目标值则 `status:"pending"`（不谎报 completed）。
- `run_console_command`（:4924-5018）：`status = stable && !consoleTimedOut ? "completed" : "pending"`；
  `stable` 字段同步（超时后不得 `stable:true`）。

### 3. 测试 `STS2AIAgent.Tests/`

- `MenuTransitionPolicy` 新判定的真值表（含 `sourceNodeValid=false`、类型不匹配、子类匹配、null）。
- 源码契约断言（`AgentSourceFixture`）：三处等待助手的循环体内**不再出现**
  `IsInstanceValid(screen))s*{?s*return true` 之类写法；`run_console_command` 的 status 表达式包含
  `consoleTimedOut`。写断言后自检可否证（把代码改回旧写法应立刻变红）。
