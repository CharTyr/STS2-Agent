# 设计

## 1. 早退分支（`ResolveNonModalScreen`）

在 `CARDS_VIEW` 早退之后、E4 通用网格早退之前插入两条，形态与 `NCardRewardSelectionScreen` 完全一致：

```csharp
// Both of these carry visible grid card holders, so the generic grid branch below would call them
// CARD_SELECTION and send the model after select_deck_card, an action they deliberately do not offer.
if (currentScreen is NCardLibrary)
{
    return "CARD_LIBRARY";
}

if (currentScreen is NCardPileScreen)
{
    return "CARD_PILE";
}
```

**为什么用专用名而不是 `MAIN_MENU`**：switch 里确实有 `NSubmenu => "MAIN_MENU"` 这个笼统 arm，
但主菜单家族里"只能关闭的覆盖层"在这个仓库一律给专用名——`PATCH_NOTES` 就是先例，
`SKILL.md:116` 专门为它写了"用 `close_main_menu_submenu` 关闭"。
报 `MAIN_MENU` 会让模型看到"继续游戏/开新局"的指引，对该屏完全不适用。

## 2. 局内 `NCardLibrary` 的逃逸：栈查找上移一层

`GetMainMenuSubmenuStack` 现在写死 `is NMainMenuSubmenuStack`。局内库挂在 `NRunSubmenuStack` 上，
而两者都是 `NSubmenuStack` 的兄弟子类（`NMainMenuSubmenuStack : NSubmenuStack`、
`NRunSubmenuStack : NSubmenuStack`）。`SubmenusOpen` 与 `Pop()` 都定义在基类上
（`NSubmenuStack.cs:50,95`），所以：

- 把查找改成 `is NSubmenuStack`，并重命名为 `GetSubmenuStack`（旧名字会对局内栈撒谎）；
- 三处引用同步：`GameStateService.cs:1445`（`CanCloseMainMenuSubmenu`）、
  `GameActionService.cs:643`（执行端），以及签名本身。

**范围坦白**：这一改也让 `NRunSubmenuStack` 承载的其它 submenu（compendium / bestiary /
relic collection / potion lab / run history / settings / stats / pause menu，
见 `NRunSubmenuStack.cs:120-200`）拿到 `close_main_menu_submenu`。它们今天同样是死路或误报，
`Pop()` 对它们同样正确；但这属于本次顺带修好、需实机确认的邻域，必须写进 evidence。

## 3. `NCardPileScreen` 的逃逸：扩大 `close_cards_view` 的可关集合

现状：`GetCardsViewBackButton` 第一行 `is not NCardsViewScreen → null`；`IsCardsViewClosed` 也写死
`currentScreen is not NCardsViewScreen`。

改法：把"可关闭的看牌屏"收敛成**一处**判定，探针、执行端、等待条件都调它：

```csharp
// Screens that draw the same visible card grid and are left with their BackButton.
private static bool IsClosableCardViewer(IScreenContext? screen) =>
    screen is NCardsViewScreen or NCardPileScreen;
```

- `CanCloseCardsView` → 用 `GetCardsViewBackButton(...) != null`（该 helper 内部改用上面的判定）；
- `IsCardsViewClosed` → `!IsClosableCardViewer(currentScreen)`（inspect 屏那两支不变）；
- `ExecuteCloseCardsViewAsync` 的 else 分支**不改逻辑**——它已经 `ForceClick()` 取到的 BackButton，
  两屏的 BackButton 节点名都是 `"BackButton"`、都已在 `_Ready` 里 `Enable()`。
- 注意 `NCardPileScreen` 的 BackButton 是 `NButton`，`NCardPileScreen.cs:115-117` 已 `Enable()`；
  `ForceClick()` 绕过输入状态，与该仓库其它执行端一致。

## 4. 文档

- `docs/api.md`：Screen 枚举表（`:87` 附近）补 `CARD_LIBRARY` / `CARD_PILE` 两行；
  `:88` 的 `CARDS_VIEW` 与 `:1650` 的 `close_cards_view` 一节改成"可关闭的看牌屏"口径。
- `SKILL.md`（**在共享契约块内**，marker `<!-- BEGIN/END SHARED PLAY CONTRACT -->` 之间）：
  在 `CARDS_VIEW`/`CARD_INSPECT` 附近补两行路由：
  - `CARD_LIBRARY`: 用 `close_main_menu_submenu` 返回；
  - `CARD_PILE`: 用 `close_cards_view` 返回。

## 测试（`STS2AIAgent.Tests/`，可否证）

1. 早退顺序：`NCardLibrary` / `NCardPileScreen` 的索引 < E4 的 `GetVisibleGridCardHolders` 索引，
   且各自到 E4 的切片里含对应 `return "...";`（与 `RewardScreenContractTests` 同形）。
2. `close_cards_view` 同源：探针 `CanCloseCardsView` 与执行端 `IsCardsViewClosed` 都只经
   `IsClosableCardViewer` / `GetCardsViewBackButton` 判定，且该判定同时覆盖 `NCardsViewScreen` 与 `NCardPileScreen`
   （照 `DeckSelectionAvailabilityTests` 的"可用即可执行"写法）。
3. 栈查找用基类：断言 `GetSubmenuStack` 体内是 `is NSubmenuStack` 而**不是** `is NMainMenuSubmenuStack`，
   且 `CanCloseMainMenuSubmenu` 与执行端都调它。
4. 保持既有绿：`RewardScreenContractTests`、`DeckSelectionAvailabilityTests`、`ScreenResolutionContractTests`。

## 陷阱

- `GameStateService.cs` / `GameActionService.cs` 不在测试工程编译集内（它们是
  `SourceCoverageTests.KnownUncompiledSources` 的成员），改完必须跑
  `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental` 做类型检查。
- `api-facts` gate 会从 `ResolveNonModalScreen` 提取屏幕枚举并与 `docs/api.md` 比集合，**两处必须同时改**。
- `SKILL.md` 契约块被 mod 构建期内嵌（`csproj:32-33`），marker 不能删；改完跑
  `dotnet build STS2AIAgent/STS2AIAgent.csproj` 确认内嵌仍成立。
- 本仓库在 Windows 上 `core.autocrlf=true`；只做最小 hunk，不要整文件重写。

