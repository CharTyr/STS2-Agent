# 给牌库/牌堆查看屏正名，并让它们能被逃出去

## 背景

`NCardLibrary`（图鉴 / 牌库合集）与 `NCardPileScreen`（战斗中点开的抽牌堆/弃牌堆/消耗堆）
都经 `NCardGrid` 创建可见的 `NGridCardHolder`，于是被 `ResolveNonModalScreen` 的通用网格早退分支
（E4，`GameStateService.cs:6829-6832`）吃掉，`/state.screen` 报 **假** `CARD_SELECTION`。

而 4c4 之后的 `GetDeckSelectionOptions` **故意**对它们返回空
（`GameStateService.cs:1725-1731` 有注释说明），`DeckSelectionAvailabilityTests` 钉住这一点。
所以屏幕上既没有 `select_deck_card`、也没有 `confirm_selection`，`selection` 段为 null，
但 `skills/sts2-mcp-player/SKILL.md:105` 的 `CARD_SELECTION` 路由却叫模型去调 `select_deck_card`。

可达性是实证的，不是推测：

- `NCombatCardPile.ShowScreen(_pile, ...)` 把 `NCardPileScreen`（`Control, ICapstoneScreen`）
  压进 `NCapstoneContainer`；
  `ActiveScreenContext.GetCurrentScreen()` 在 `NRun.Instance != null` 分支里**先**看
  `NCapstoneContainer.Instance.CurrentCapstoneScreen`（`ActiveScreenContext.cs:66-69`）⇒ 牌堆屏会被当当前屏；
- `NCardLibrary : NSubmenu`，由 `NMainMenuSubmenuStack` **和** `NRunSubmenuStack` 两处
  `GetSubmenuType(typeof(NCardLibrary))` 创建（`NRunSubmenuStack.cs:155-164`）；
  `GetCurrentScreen` 在 `submenuStack.SubmenusOpen` 时 `Peek()` 到它（`ActiveScreenContext.cs:56-60`）。

逃逸能力两屏不同，这是本任务的核心：

| 屏 | 今天能逃出去吗 |
|---|---|
| `NCardLibrary`（主菜单侧） | 能：`close_main_menu_submenu` 经 `CanCloseMainMenuSubmenu` 的 `NSubmenu` + `NMainMenuSubmenuStack` 判定生效 |
| `NCardLibrary`（局内侧） | **不能**：`GetMainMenuSubmenuStack` 只认 `NMainMenuSubmenuStack`，而局内用的是 `NRunSubmenuStack`（两者是兄弟，都继承 `NSubmenuStack`）⇒ `available_actions` 为空 ⇒ 死路 |
| `NCardPileScreen` | **不能**：`CanCloseCardsView` 要求 `is NCardsViewScreen`；`GetProceedButton` 走 `IRoomWithProceedButton` / 子节点搜索，该屏只有名为 `"BackButton"` 的按钮（`NCardPileScreen.cs:115-117` 已 `Enable()`）⇒ `available_actions` 为空 ⇒ 死路 |

## 目标

1. 两个屏各自报**专用**屏幕名，不再冒充 `CARD_SELECTION`；
2. 两个屏都有真实可用的关闭动作，`available_actions` 不再为空。

## 验收标准

- [x] `ResolveNonModalScreen` 里 `NCardLibrary`、`NCardPileScreen` 的判定早于 E4 通用网格分支
      （与 `NCardRewardSelectionScreen`/`NUnlockScreen` 同款早退形态）。
- [x] 屏幕名取专用值 `CARD_LIBRARY` / `CARD_PILE`，**不要**报 `MAIN_MENU`
      （理由见 design：仓库对"只能关闭的覆盖层"一律给专用名，先例是 `PATCH_NOTES`/`TIMELINE`）。
- [x] 局内 `NCardLibrary` 能拿到 `close_main_menu_submenu`：submenu 栈查找改为基类 `NSubmenuStack`。
- [x] `NCardPileScreen` 能拿到 `close_cards_view`：探针与执行端对"可关闭的看牌屏"用**同一个集合**，
      执行端点它的 `BackButton`，等待条件也能识别该屏已关闭。
- [x] `docs/api.md` 的 Screen 枚举补两个名字（`api-facts` gate 从源码提取枚举，不补会红）；
      `close_cards_view` 一节的前提与 `CARDS_VIEW` 描述同步更新。
- [x] `SKILL.md` 路由补 `CARD_LIBRARY` / `CARD_PILE` 两行；marker 对保持成对，
      `McpPlayerSkillTests`、`HealthCheckParityTests` 保持绿（内嵌契约仍与 `PlayPrompt.PlayContract` 一致）。
- [x] 新增源码契约测试（可否证）：早退顺序、`close_cards_view` 探针/执行端同源、栈查找用基类。
- [x] C# / MCP 全量测试、7 道 gate、preflight 全绿。

## 范围外

- 不改动作名、不新增动作（`close_cards_view` / `close_main_menu_submenu` 已存在）。
- `NCardLibrary` 不走 `close_cards_view`（它由 submenu 栈承载，两条关闭路径会让模型困惑）。
- `NCardPileScreen` 之外不新增 capstone 屏支持。
- 实机验证：按钮点击是否真的把屏关掉，只能上真机确认（见 evidence 的"只能实机验证"清单）。

