# 证据：屏幕名遮蔽

## 早退分支顺序（`GameStateService.cs:6802-6866`）

| 序 | 条件 | 返回 | 行 |
|---|---|---|---|
| E1 | `currentScreen is NUnlockScreen` | UNLOCK | 6804-6807 |
| E2 | `TryGetCombatHandSelection(...)` | CARD_SELECTION | 6809-6813 |
| E3 | `currentScreen is NCardsViewScreen` | CARDS_VIEW | 6815-6818 |
| E4 | 通用可见网格 holder（排除 bundle 屏） | CARD_SELECTION | 6820-6825 |
| E5 | `GetMultiplayerTestScene() != null` | MULTIPLAYER_LOBBY | 6827-6830 |
| E6 | `FindActiveCombatRoom(...) != null` | COMBAT | 6832-6835 |

switch 里 `NCardRewardSelectionScreen => "REWARD"` 在 `:6840`，被 E4 永久遮蔽。

## 被 E4 遮蔽但"值相同"的 arm（无影响）

`NChooseACardSelectionScreen`（6841）、四个 `NDeck*Select`（6842）、`NCardGridSelectionScreen`（6843）
都返回 `CARD_SELECTION`，与 E4 同值。

## 未被遮蔽的关键屏幕（否定结论的依据）

`rg -n "NGridCardHolder" extraction/decompiled` → 运行期创建 holder 的只有 4 处：
`NCardRewardSelectionScreen.cs:188`、`NChooseACardSelectionScreen.cs:181`、`NUnlockCardsScreen.cs:93`、
`NCardGrid.cs:787`（被 selection 屏 / pile 屏 / library 复用）。
`NRewardsScreen` / `NTreasureRoom` / `NMerchantRoom` / `NMerchantInventory` / `NRestSiteRoom` /
`NEventRoom` / `NCombatRoom` 逐个 `rg` 无命中 ⇒ 它们的 arm 可达。

## 影响面

- `skills/sts2-mcp-player/SKILL.md:105`：`CARD_SELECTION` → `select_deck_card`（该屏不提供）。
- `docs/api.md:85/890/1359/1377`：奖励动作的前提写 `REWARD`，运行期却报 `CARD_SELECTION`。
- `scripts/run_sts2_validation.py:1334` 的 `while screen == "REWARD"` 循环在该浮层上条件转假
  （是否真出问题取决于进入点，属实机项）。

## 修复后不需要改的既有资产（已核实）

- `ScreenResolutionContractTests.cs:12-40` 只解析 `return currentScreen switch` 之后的文本；新增早退分支不进该切片。
- `UnlockScreenContractTests.cs:21-33` 断言 `unlockScreenIndex < visibleGridIndex` 且两者间含 `return "UNLOCK";`，
  新增分支夹在中间仍成立。
- `check_verification_gates.py` 的 api-facts 会用 `CODE_SCREEN_EARLY_RETURN` 多抓到 `REWARD`，
  但 `REWARD` 已在 `docs/api.md` 屏幕枚举内，集合比较不触发。
