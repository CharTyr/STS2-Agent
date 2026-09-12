# 证据

## crystal_clear_cell 的两侧事实

| 面 | 事实 |
|---|---|
| 契约块 | `docs/api.md:1033`：动作名 + 描述，括号里只有 `x`、`y` |
| 契约块之外 | `docs/api.md:1072`（请求体表的 `tool`）、`:1478-1488`（参数说明与示例）都已写 `tool` |
| 注册 schema | `server.py:77` 的 `ActionToolSpec("crystal_clear_cell", …)` + `:426-433` 的 `action_tool(x, y, tool=None)` |
| 实现 | `GameActionService.cs:1679-1765`；`:1715-1736` 可选分支 `if (request.tool != null)`；非法值 → `:1777` |
| 豁免 | `test_native_tool_alignment.py:289-291` + 注释 `:285-288` |

`check_verification_gates.py:336` 的正则只取动作名，所以补参数不影响 api-doc gate；
但 `test_native_tool_alignment.py:415-435` 会因"豁免变陈旧"而失败，因此两处必须同时改。

## select_deck_card 的不对称

- 可用性：`CanSelectDeckCard`（`GameStateService.cs:916-923`）= `GetDeckSelectionOptions(...).Count > 0`。
- `GetDeckSelectionOptions`（`:1688-1726`）四段：`NCardsViewScreen` → 空；
  `NCardGridSelectionScreen` → 可见网格 holder；`NChooseACardSelectionScreen` → 同；
  战斗手牌 → `hand.ActiveHolders`；**通用兜底 → `GetVisibleGridCardHolders(rootNode)`**。
- 执行侧守卫：`GameActionService.cs:2106-2117`（`!isCombatHandSelection && !isCardGridSelection &&
  currentScreen is not NChooseACardSelectionScreen` → 409 `invalid_action`）。
- 被误放行的屏幕：`NCardRewardSelectionScreen`（正确入口是 `choose_reward_card`）、
  `NCardPileScreen`（纯查看器，未连 `HolderPressed`）、
  `NCardLibrary` / `NCardLibraryGrid`（`HolderPressed → ShowCardDetail`）。
- 不变式来源：`scripts/run_sts2_validation.py:464-471` 与 `scripts/test-state-invariants.ps1:252-259`
  断言"selection 非空 ⇒ available_actions 含 `select_deck_card`"。
  `BuildSelectionPayload` 与可用性同源（`GameStateService.cs:4325-4345`），所以删兜底会让前提不触发，
  而不是让断言失败。

## 已确认的边界

- 反向不对称不存在：执行入口第一行就是 `CanSelectDeckCard` 守卫（`GameActionService.cs:2069`），
  执行集必为可用集的子集。
- `NCardGridSelectionScreen` 的 5 个 sealed 子类（deck/simple/upgrade/transform/enchant）
  都自声明 `_prefs`/`_selectedCards`，不走兜底。
