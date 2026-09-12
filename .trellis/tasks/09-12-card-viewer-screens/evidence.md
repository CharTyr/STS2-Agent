# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Game/GameStateService.cs` | 新增 `using ...Screens.CardLibrary;`；`ResolveNonModalScreen` 在 `CARDS_VIEW` 之后、通用网格之前补两条早退（`NCardLibrary → "CARD_LIBRARY"`、`NCardPileScreen → "CARD_PILE"`）；新增共享判定 `IsClosableCardViewer`（`NCardsViewScreen or NCardPileScreen`）；`GetCardsViewBackButton` 改用该判定 + `Node` 取 `BackButton`；`GetMainMenuSubmenuStack` → `GetSubmenuStack`（匹配基类 `NSubmenuStack`） |
| `STS2AIAgent/Game/GameActionService.cs` | `ExecuteCloseMainMenuSubmenuAsync` 改用 `GetSubmenuStack`；`WaitForMainMenuSubmenuCloseAsync` 形参上移到 `NSubmenuStack`（编译必需）；`IsCardsViewClosed` 在既有 `NCardsViewScreen` 判定**之前**插入经共享判定的提前返回，使等待条件覆盖 `CARD_PILE` |
| `docs/api.md` | Screen 枚举补 `CARD_LIBRARY` / `CARD_PILE`；`CARDS_VIEW` 描述、动作清单行与 `close_cards_view` 一节改为"可关闭的看牌屏"口径 |
| `skills/sts2-mcp-player/SKILL.md` | 共享契约块内补两行路由；marker 对保持成对 |
| `STS2AIAgent.Tests/CardViewerScreenContractTests.cs` | 新增 3 条源码契约测试 |
| `STS2AIAgent.Tests/TestRunner.cs` | 追加 3 行注册 |

## 验收

| 标准 | 证据 |
|---|---|
| 两条判定早于通用网格 | `GameStateService.cs:6840-6850` 早于 `:6861` 的 `GetVisibleGridCardHolders`；红/绿见下 |
| 用专用名不用 MAIN_MENU | 返回值为 `CARD_LIBRARY` / `CARD_PILE`；理由见 design（`PATCH_NOTES` 先例） |
| 局内图鉴可关闭 | `GetSubmenuStack` 匹配基类后，局内 `NRunSubmenuStack` 也命中 `CanCloseMainMenuSubmenu` |
| 牌堆屏可关闭 | 探针/执行端/等待条件三处同源 `IsClosableCardViewer`；执行端 `ForceClick()` 其 `BackButton` |
| `docs/api.md` 枚举同步 | `api-facts`: **26 screens** 全部出现在文档枚举里 |
| `SKILL.md` + 内嵌契约 | `Skill.McpPlayerContract`、`Parity.*` 全绿；mod 构建 0 warning |
| 可否证 | 三组"改坏 → 红 → 还原 → 绿"（见下） |
| 全量门禁 | C# **351 PASS / 0 FAIL**；MCP 165 OK；7 道 gate 全绿；preflight **372 PASS / 0 FAIL** |

## 红/绿证据

命令均为 `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release`：

1. 两个早退分支挪到通用网格之后 →
   `FAIL CardViewer.BranchesPrecedeGrid / NCardLibrary must resolve before its own visible card grid can report CARD_SELECTION.` → 还原后绿
2. `IsClosableCardViewer` 改回只认 `NCardsViewScreen` →
   `FAIL CardViewer.ClosableViewerSameSource / Expected '{returnscreenisNCardsViewScreen;}' to contain 'screenisNCardsViewScreenorNCardPileScreen'.` → 还原后绿
3. `GetSubmenuStack` 改回 `is NMainMenuSubmenuStack` →
   `FAIL CardViewer.SubmenuStackBase / ... to contain 'currentisNSubmenuStacksubmenuStack'` → 还原后绿

## 两处实现选择（需要复核者知悉）

1. `IsCardsViewClosed` 的**最后一行保留** `return currentScreen is not NCardsViewScreen;`——
   `ScreenResolutionContractTests.InspectOverlaysCloseThroughTheirOwnClose` 钉住了该字面量，
   而该测试不在本次写范围。实现改为在其**之前**插入经 `IsClosableCardViewer` 的提前返回，
   既把新屏纳入同一集合，又不破坏既有断言。后续若允许改该测试，可把它收敛成单行。
2. 除 design 列出的同步点外，另有 2 处**编译必需**的小改动：
   `using ...Screens.CardLibrary;`，以及 `WaitForMainMenuSubmenuCloseAsync` 形参类型上移。

## 顺带修好的邻域（范围坦白）

栈查找上移到基类后，`NRunSubmenuStack` 承载的其它 submenu
（compendium / bestiary / relic collection / potion lab / run history / settings / stats / pause menu，
见 `NRunSubmenuStack.cs:120-200`）也拿到 `close_main_menu_submenu`。
它们此前同样是死路（或误报屏幕名），`Pop()` 对它们同样正确——但**这属于本次顺带修好、需要实机确认的邻域**。

## 只能实机验证

- 点 `NCardPileScreen` 的 BackButton 后屏幕是否真的关闭（`ForceClick()` 绕过输入状态，
  只有真机能证明 `Released` 真的退栈）。
- 局内 `NCardLibrary` 经 `NSubmenuStack.Pop()` 后是否回到正确的前一屏。
- 上述其它局内 submenu 的真机表现。
- 真机上 `/state.screen` 与 `available_actions` 是否与新屏幕名/新动作一致。

