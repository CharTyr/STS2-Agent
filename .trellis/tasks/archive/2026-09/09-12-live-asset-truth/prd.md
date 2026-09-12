# 让实机验证资产与当前契约一致

## 背景

`scripts/` 下的实机验证资产在若干处断言了 mod 当前并不提供的契约。它们只在有游戏时才能跑，
矛盾长期潜伏，会在真机上以"失败"或"崩溃"的形式暴露。已确证四处：

1. `test-main-menu-active-run.ps1` 硬断言活动存档主菜单必有 `open_timeline`（`:92`、
   `:114` 的等待条件、`:117-152` 的完整 timeline 交互序列）。python 版在同一状态给出相反结论
   （`run_sts2_validation.py:1548-1550`）：有存档时主菜单 timeline 按钮被禁用
   （`NMainMenu.UpdateTimelineButtonBehavior`），`open_timeline` 不属于 active-run 契约；
   `:1827` 再次说明 timeline 只在无存档时打开。两个脚本对同一状态给出互斥断言。
2. `run_sts2_validation.py:1935-1946` 把 `choose_timeline_epoch` 的 `option_index` 写死为 0。
   执行端契约（`TimelineIndexContractTests` 钉住 `GameActionService.ResolveTimelineSlot`）要求
   index 命中 `timeline.slots[]` 里的 actionable 槽位；槽位列表只过滤 `NotObtained`，
   因此 0 可能是非 actionable 槽 → 409 `invalid_target`。
3. `test-multiplayer-lobby-flow.ps1:655-733` 的屏幕 switch 只列了 5 种屏幕，其余一律
   `throw "Unsupported run progression state"`；正常推图会遇到 `CAPSTONE_SELECTION`（章节选择）
   与 `UNLOCK`，脚本会中途崩溃。
4. python 侧的屏幕名覆盖缺口：`run_sts2_validation.py` 只识别 9 个屏幕名
   （MAIN_MENU / MODAL / REWARD / COMBAT / CARD_SELECTION / CHARACTER_SELECT / GAME_OVER /
   MULTIPLAYER_LOBBY / UNKNOWN），而 `ResolveNonModalScreen` 现在返回 24 个（对照表见 research）；
   其余屏幕没有分支，遇到即超时或误判。

## 目标

让脚本的断言与它读到的 state 同源：不再要求 mod 不提供的动作，索引不再写死，
屏幕分支与 `ResolveNonModalScreen` 的返回集合对照。

## 验收标准

- [x] `test-main-menu-active-run.ps1` 与 python 版对活动存档主菜单的 timeline 结论一致
      （两边都不断言 `open_timeline` 必可用），并保留 `open_timeline_available` 这类诊断输出。
- [x] `choose_timeline_epoch` 的 index 取自 state 的 `timeline.slots[].index` 中 `is_actionable`
      的第一个槽，不再写死；脚本里留注释指向 `TimelineIndexContractTests`。
- [x] `test-multiplayer-lobby-flow.ps1` 的屏幕 switch 覆盖正常推图会遇到的屏幕
      （至少 `CAPSTONE_SELECTION`、`UNLOCK`），兜底错误带屏幕名与可用动作。
- [x] python 侧把会在正常流程出现的屏幕补上处理或显式说明（写进脚本注释）；
      最终"哪个屏幕由哪条流程消费"的清单写进 evidence。
- [x] PowerShell 脚本含非 ASCII 时保持 UTF-8 BOM（`script-encoding` gate），
      `python -m py_compile` 通过，全量门禁绿。
- [x] C# / MCP 全量测试、gates、preflight 通过。

## 范围外

- 不重写脚本的推进策略（不把 python 的 debug-command 路线改成逐屏 UI 路线）。
- 不为"只能实机验证"的行为新建离线测试骨架；本轮保证语法、编码、契约注释与分支可达。

