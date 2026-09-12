# 收尾两个审计残留

## 背景

上一轮结束时留下两处已被证实的缺口：

1. **`crystal_clear_cell` 的文档欠账**：实现与注册 schema 都接受可选参数 `tool`（`big`/`small`），
   `docs/api.md` 的动作契约块（`docs/api.md:1033`）只写 `x`、`y`。
   上一轮的解锁方式是一条**显式豁免**（`mcp_server/tests/test_native_tool_alignment.py:289-291`），
   并靠"豁免变陈旧即失败"兜住。契约块"冻结"的理由已不成立：api-doc gate 只比对动作名集合
   （`scripts/check_verification_gates.py:326-339` 的正则在反引号闭合处结束，描述与参数不参与校验）。
2. **`select_deck_card` 的可用性/可执行性不对称**：`GameStateService.GetDeckSelectionOptions`
   末尾有一条通用兜底（`STS2AIAgent/Game/GameStateService.cs:1718-1723`）——
   "当前屏幕子树里有可见的 `NGridCardHolder` 就算可选牌"。执行侧
   （`GameActionService.ExecuteSelectDeckCardAsync`，守卫在 :2106-2117）要求战斗手牌元数据 /
   卡牌网格元数据 / `NChooseACardSelectionScreen` 三者之一，否则 409。
   于是 `NCardRewardSelectionScreen`、`NCardPileScreen`、`NCardLibrary` 三类屏幕会出现在
   `available_actions` 里却必然失败，而 `/state.selection` 还会报出一个形态正常的
   `deck_card_select`（`min/max` 缺元数据时取 1/1），把模型骗进去。

## 目标

1. 给 `crystal_clear_cell` 在契约块里补上可选参数 `tool`，并**删除**那条豁免（让测试回到严格对照）。
2. 删掉 `GetDeckSelectionOptions` 的通用兜底分支，使"可用 ⇒ 可执行"在同源处成立；
   执行侧的 409 守卫保留为防御分支。

## 验收标准

- [x] `docs/api.md` 契约块里 `crystal_clear_cell` 行含 `tool`，且该行仍以"反引号动作名"开头
      （否则 api-doc gate 的正则会漏掉它）。
- [x] `_DOC_ARGUMENT_EXEMPTIONS` 不再包含 `crystal_clear_cell`；MCP 全量测试通过。
- [x] `GetDeckSelectionOptions` 不再有 `currentScreen is Node rootNode` 兜底分支；
      前三段（`NCardGridSelectionScreen` / `NChooseACardSelectionScreen` / 战斗手牌）保留。
- [x] 新增可否证回归测试：源码契约断言该兜底分支消失，并断言执行侧守卫仍以同一组判据存在。
- [x] 现有 `selection.cards[]` 不变式仍成立：`BuildSelectionPayload` 与可用性同源，
      兜底删除后这三类屏幕不再产出 `selection` 段（前提不触发，而非断言失败）。
- [x] `check_verification_gates.py`、C# 与 MCP 全量测试、preflight 通过。

## 范围外（明确记录，不本任务修）

- `ResolveNonModalScreen` 的通用分支（`GameStateService.cs:6742-6747`）仍会在奖励选牌浮层上返回
  `CARD_SELECTION`，遮蔽 `NCardRewardSelectionScreen => "REWARD"` 那一臂。改它会改变 `/state.screen`
  的对外语义（文档屏幕表、skill 路由、验证脚本都受影响），需要单独评估与实机确认，本轮只记录证据。
- `NCardPileScreen` / `NCardLibrary` 的"正确处置"（两者都不是选择屏，后者 `HolderPressed` 只打开详情）
  不在本任务范围；本轮只保证它们不再被误报为可选牌。
