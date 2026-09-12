# 证据：实机资产的契约矛盾

## 屏幕全集（`GameStateService.ResolveNonModalScreen`:6802-6866）

UNLOCK、CARD_SELECTION、CARDS_VIEW、MULTIPLAYER_LOBBY、COMBAT、GAME_OVER、REWARD、CHEST、REST、
SHOP、EVENT、MAP、CHARACTER_SELECT、BUNDLE_SELECTION、CAPSTONE_SELECTION、CRYSTAL_SPHERE、TIMELINE、
FAKE_MERCHANT、PATCH_NOTES、CARD_INSPECT、RELIC_INSPECT、FEEDBACK、MAIN_MENU、UNKNOWN（24 个）。

python 侧出现过的 screen 比较：MAIN_MENU / MODAL / REWARD / COMBAT / CARD_SELECTION /
CHARACTER_SELECT / GAME_OVER / MULTIPLAYER_LOBBY / UNKNOWN。其余 15 个非 modal 屏幕没有分支。

## 1. open_timeline 的互斥断言

| 文件 | 行 | 内容 |
|---|---|---|
| `scripts/test-main-menu-active-run.ps1` | 92 | `Assert-ActionAvailable -State $state -ActionName "open_timeline"` |
| 同上 | 114 | 等待条件 `... -contains "open_timeline"` |
| 同上 | 117-152 | 调用 `open_timeline`/`choose_timeline_epoch`/`confirm_timeline_overlay`/`close_main_menu_submenu` |
| `scripts/run_sts2_validation.py` | 1548-1550 | 注释：有存档时 timeline 按钮被禁用（`NMainMenu.UpdateTimelineButtonBehavior`），`open_timeline` 不属于 active-run 契约 |
| 同上 | 1827 | 注释：timeline 只在没有存档时打开 |

## 2. epoch 索引

`TimelineIndexContractTests.ExecutorIndexesTheSameSlotListTheStateExposes`：
`ResolveTimelineSlot` 用 `GameStateService.GetTimelineSlots`（`GameStateService.cs:6353-6366`，
只过滤 `NotObtained`）且断言不得二次过滤；`NonActionableSlotsAreRejectedExplicitly`：
非 actionable 槽 409 `invalid_target`，`option_index_space = "timeline.slots[].index"`。
python 的 `option_index=0`（`:1937`）因此不保证命中。

## 3. lobby flow 的 switch

`scripts/test-multiplayer-lobby-flow.ps1:655-733`：BUNDLE_SELECTION / EVENT / CARD_SELECTION / REWARD / MAP
五个分支，其余 `throw`（`:733`）。CAPSTONE_SELECTION（`NCapstoneSubmenuStack`）与 UNLOCK（`NUnlockScreen`）
在正常推图会出现（参见 `skills/sts2-mcp-player/references/screen-playbooks.md` 的屏幕表）。

## 4. python 分支缺口的影响面

- `settle_main_menu`（`:1283-1307`）只处理 MODAL 与 timeline 动作，遇到 UNLOCK/CAPSTONE 会等到超时；
- `collect_rewards_if_needed`（`:1332-1345`）只认 REWARD；
- `suite_new_run_lifecycle` 主要靠 debug 命令绕开 UI，因此对这些屏幕无感——但任何需要真实 UI 推进的段落
  （如 `suite_main_menu_active_run`）会受影响。

## 未经实机确证

脚本改动只做语法/编码/契约层验证；"实际不再超时/不再崩溃"只能在有游戏的机器上确认。

