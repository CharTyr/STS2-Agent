# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Game/GameStateService.cs` | `ResolveNonModalScreen` 内新增早退分支（`CARDS_VIEW` 之后、通用可见网格分支之前）：`if (currentScreen is NCardRewardSelectionScreen) return "REWARD";`（+9 行，含 4 行注释） |
| `STS2AIAgent.Tests/RewardScreenContractTests.cs` | 新增。3 条源码契约断言：奖励分支索引 < 通用网格索引；两者之间含 `return "REWARD";`；通用网格分支之后的 switch 里 `NChooseACardSelectionScreen` / 四个 `NDeck*Select` / `NCardGridSelectionScreen` 仍为 `=> "CARD_SELECTION"` |
| `STS2AIAgent.Tests/TestRunner.cs` | 追加 1 行注册 `RewardScreen.BranchPrecedesGrid` |

## 验收

| 标准 | 证据 |
|---|---|
| 奖励分支早于通用网格 | `GameStateService.cs:6820-6827`（早退）在 `:6829-6832`（`GetVisibleGridCardHolders(rootNode).Count > 0`）之前；`git diff` 只有这一个 hunk |
| 顺序被钉住且可否证 | 把新分支临时挪到通用网格分支之后：`FAIL RewardScreen.BranchPrecedesGrid`，且**只有它**失败（`UnlockScreen.MixedCardGrid`、`ScreenResolution.MappingTable` 仍 PASS）；还原后 347/0 |
| 其它被遮蔽 arm 值不变 | 第 3 条断言在 `visibleGridIndex` 之后的切片里逐条要求三个 `=> "CARD_SELECTION"` 形态；`ScreenResolution.MappingTable` 全表（25 条）继续 PASS |
| 既有测试全绿 | C# 347 PASS / 0 FAIL；MCP 167 OK；6 道 gate 全绿（`api-facts` 复核 `24 screens ... all appear in the docs/api.md enum`）；`check_release_package.py --source-root .` 通过；`preflight-release.ps1` exit 0 |
| 文档/脚本核对结论 | **不需要改文档**（详见下节） |

## 文档核对（prd 要求落盘的结论）

修复后既有资产**变准**，无需修改：

- `docs/api.md`：`:85` 的屏幕枚举已含 `REWARD`；`:1320/:1342/:1359/:1377/:1391` 的奖励动作前提本就写 `screen = "REWARD"`——修改前运行期与该契约相反，修改后才一致。
- `skills/sts2-mcp-player/SKILL.md`：`:104` 有 `REWARD` 路由，`:105` 把 `CARD_SELECTION` 关联到 `select_deck_card`。奖励浮层此前误报 `CARD_SELECTION` 却**不提供**该动作，修复后不再误导。
- `scripts/run_sts2_validation.py`：`collect_rewards_if_needed` 的 `while screen == "REWARD"` 循环重新对得上该浮层。

`api-facts` gate 用 `CODE_SCREEN_EARLY_RETURN` 提取早退分支，新分支只多产出已存在的 `REWARD`，集合比较不触发。

## 只能实机验证

- 奖励选牌浮层实机上 `/state.screen` 确实回报 `REWARD`（含多奖励项、bundle、多项 card choice 等变体）。
- 语义翻转 `CARD_SELECTION → REWARD` 对模型路由的真实影响（不再去找不存在的 `select_deck_card`）。
- `collect_rewards_if_needed` 的循环在新的屏幕名下是否改变行为（取决于进入点）。

## 范围外（已记录未动）

`NCardPileScreen`（看牌堆）与 `NCardLibrary`（图鉴）没有 switch arm，仍解析为 `UNKNOWN`。给它们新增屏幕名需要先定义 skill 侧路由语义，本轮只记录。

