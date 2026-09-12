# 证据：两个查看屏的可达性与逃逸能力

## 谁会把它们变成当前屏（实证，非推测）

`ActiveScreenContext.GetCurrentScreen()`（`extraction/decompiled/.../ActiveScreenContext.cs`）顺序：
FeedbackScreen → OpenModal → InspectCard → InspectRelic → LogoAnimation → MainMenu(PatchNotes/SubmenuStack.Peek/自身)
→ **NRun 分支**：

```
66: if (NCapstoneContainer.Instance.CurrentCapstoneScreen != null) return ...CurrentCapstoneScreen;
70: if (NMapScreen.Instance.IsOpen) return NMapScreen.Instance;
74: if (NOverlayStack.Instance.ScreenCount > 0) return NOverlayStack.Instance.Peek();
...
```

- **牌堆屏**：`NCardPileScreen : Control, ICapstoneScreen`（`NCardPileScreen.cs:28`），
  由 `NCombatCardPile` 调 `NCapstoneContainer.ShowScreen(_pile, Hotkeys)` 压入
  （`NCapstoneContainer.ShowScreen` 内 `CurrentCapstoneScreen = screen`，`:139`）
  ⇒ 命中第 66 行。
- **图鉴**：`NCardLibrary : NSubmenu`（`NCardLibrary.cs:33`），
  `NMainMenuSubmenuStack` 与 `NRunSubmenuStack` 各持一份（`NRunSubmenuStack.cs:155-164`）
  ⇒ 命中 MainMenu 分支的 `submenuStack.Peek()`（`:56-60`）。

## 今天的屏幕名与可用动作

两屏都创建可见 `NGridCardHolder`（`NCardLibraryGrid : NCardGrid`，`NCardLibraryGrid.cs:21`；
`NCardPileScreen` 的 `_grid = GetNode<NCardGrid>("CardGrid")`，`:118`）
⇒ 被 E4 通用网格分支捕获 ⇒ `/state.screen = "CARD_SELECTION"`。

但 `GetDeckSelectionOptions` 对它们的返回是空（`GameStateService.cs:1725-1731` 的注释明确点名
`NCardRewardSelectionScreen, NCardPileScreen, NCardLibrary`），
`DeckSelectionAvailabilityTests` 钉住这一点 ⇒ 该屏**没有** `select_deck_card` / `confirm_selection`。

其余候选动作逐个核过：

| 动作 | 探测函数 | 为什么在今天不成立 |
|---|---|---|
| `close_cards_view` | `CanCloseCardsView` → `GetCardsViewBackButton` | 第一行 `is not NCardsViewScreen → null` |
| `close_main_menu_submenu`（图鉴·主菜单侧） | `CanCloseMainMenuSubmenu` | 成立（`NSubmenu` + `NMainMenuSubmenuStack.SubmenusOpen`） |
| `close_main_menu_submenu`（图鉴·局内侧） | 同上 | **不成立**：`GetMainMenuSubmenuStack` 只认 `NMainMenuSubmenuStack`，局内栈是 `NRunSubmenuStack` |
| `proceed` | `GetProceedButton` | 两屏都不是 `IRoomWithProceedButton`，子节点里也没有 `NProceedButton`（牌堆屏只有 `BackButton`） |

⇒ 牌堆屏与局内图鉴的 `available_actions` 为空，是死路。

## 两屏的 BackButton（说明为什么点击可行）

- `NCardPileScreen._Ready`：`_backButton = GetNode<NButton>("BackButton");` 并
  `_backButton.Enable()`（`:115-117`）。
- `NCardLibrary` 由 submenu 栈承载，退栈用 `NSubmenuStack.Pop()`（基类，`:95`）。

## 栈的类层级（决定改法）

```
NSubmenuStack (abstract, : Control)   — SubmenusOpen :50, Push :77, Pop :95, Peek :125
├── NMainMenuSubmenuStack             — 主菜单侧
└── NRunSubmenuStack                  — 局内侧（承载 card library / compendium / bestiary /
                                        relic collection / potion lab / run history / settings /
                                        stats / pause menu，见 :120-200）
```

两者是**兄弟**，所以 `is NMainMenuSubmenuStack` 查不到局内栈；`is NSubmenuStack` 两者都命中。

## 只能实机验证

- 点 `NCardPileScreen` 的 BackButton 后屏幕是否真的关闭（`ForceClick` 绕过输入状态，与其它执行端一致，
  但只有真机能证明该按钮的 `Released` 信号真的退栈）。
- 局内图鉴 `Pop()` 后是否回到正确的前一屏。
- 顺带修好的其它局内 submenu（compendium / bestiary / …）在真机上的实际表现。

