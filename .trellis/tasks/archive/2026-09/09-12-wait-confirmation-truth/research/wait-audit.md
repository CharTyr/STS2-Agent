# 证据：等待与乐观判定

## 三处危险写法（原文）

```csharp
// WaitForMainMenuSubmenuOpenAsync (GameActionService.cs:5609-5630)
var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
if (currentScreen is TSubmenu) { return true; }
if (!GodotObject.IsInstanceValid(screen)) { return true; }      // ← 主菜单被销毁也算成功

// WaitForCharacterSelectionTransitionAsync (:5826-5829)
if (!GodotObject.IsInstanceValid(screen)) { return true; }      // ← 同款

// WaitForLobbyReadyTransitionAsync (:5880-5883)
if (!GodotObject.IsInstanceValid(screen)) { return ready; }     // ← ready=true 时假成功
```

`WaitForLobbyAscensionTransitionAsync`（:5896+）同位置写的是 `return false;` —— 说明"未确认即失败"
才是本仓库的既定取态，这三处是漏网。

## 调用点

- `WaitForMainMenuSubmenuOpenAsync`：:540（open_character_select）、:588（open_timeline）、
  :4809 / :4816（`StartLocalFourPlayerHostAsync` 邀请链路）。
- `open_timeline` 的响应直接以该 bool 决定 `status`（:588-600）。
- `WaitForLobbyReadyTransitionAsync`：`ready_multiplayer_lobby` / `unready` 路径。

## 四个处理器现状

| 动作 | 行 | 现象 |
|---|---|---|
| `select_deck_card` | :2050-2130 | 守卫用 `CanSelectDeckCard`；执行侧要 `TryGetCombatHandSelectionMetadata` 或 `TryGetCardGridSelectionMetadata` 命中 |
| `confirm_timeline_overlay` | :702-800 | tutorial/unlock/其它三分支，点击前不重校验 |
| `crystal_set_tool` | :1607-1645 | `TrySetCrystalSphereTool` 返回 true 即 `completed` |
| `run_console_command` | :4968-5018 | `consoleTimedOut` 只进 message；`status = stable ? completed : pending` |

## 误报（不要修）

`open_character_select` 的 `mainMenu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>()`
（`GameActionService.cs:545`）**不会返回 null**：反编译
`extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.MainMenu/NMainMenuSubmenuStack.cs:156-207`
显示该方法是懒创建（`if (_x == null) { _x = X.Create(); AddChildSafely(...); } return _x;`）。
E2 审计报告的 B8 是误报。

## 只能实机验证

真实浮层的可达性与点击后行为、timeline 索引契约 —— 离线只能证明"不再乐观"，不能证明"实机成功"。
