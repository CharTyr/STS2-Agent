# 设计

## ① `selection.can_confirm`

现状：

- payload（`:4439-4441`）：`can_confirm = hasCombatHandSelection ? combatHandSelection.CanConfirm : hasCardGridSelection && cardGridSelection.CanConfirm`
- 动作（`CanConfirmSelection`:942-954）：额外要求 `(RequiresConfirmation || MinSelect < MaxSelect)`。

修法（实现时取破坏面小者，并写进 evidence）：

- **A**：payload 的 `can_confirm` 改为复用 `CanConfirmSelection(currentScreen)`；
- **B**：把 `CanConfirmSelection` 里"是否值得确认"的附加条件去掉，两边同为 `CanConfirm`。

判定边界：若走 A，单选且无需确认的网格会从 `can_confirm=true` 变为 `false`——这是语义收敛
（"现在有 confirm 动作吗"）；若走 B，模型可能被引导去点一个不需要确认的屏。先读
`DeckSelectionAvailabilityTests` / `RewardFlowContractTests` / `CombatDiagnosticsContractTests`
里的既有断言，再决定。无论走哪条，payload 与动作必须来自同一处判定。

## ② `modal.can_confirm`

payload `:4946` `can_confirm = confirmButton != null` → 改为 `CanConfirmModal(currentScreen)`。
先读 `FtueModalPolicyTests`：`ExposeConfirm = hasUsableConfirmButton || IsFtueType` 意味着 FTUE
弹窗在**没有**可用确认按钮时也放行 `confirm_modal`（`confirm_modal` 会走 `CloseFtueDirectly` 等路径）。
payload 与它对齐后，"FTUE 无按钮 + can_confirm=true + confirm_modal 可用"三者一致。

## ③ descriptor

`:397` `requires_index = true` → `false`（`resolve_rewards` 不消费 index）。

## ④ reward 按钮过滤

`CanChooseRewardCard` / `CanSkipRewardCards` 增加 `IsEnabled`（与 `CanClaimReward` 一致），
必要时 `IsVisibleInTree`。先确认 `GetCardRewardOptions` / `GetCardRewardAlternativeButtons` 的节点类型
是否有这两个成员；没有的就不加，并把结论写进 evidence。

## ⑤ 水晶球

`GetCrystalSphereMinigame` 已要求 `currentScreen is NCrystalSphereScreen`，因此"屏幕存在但不可见"
的可达性存疑。评估后给结论：若可达性不成立则**不改**（写进 evidence）；若有反例则补可见性过滤。

## 测试（`SurfacedActionParityTests.cs`，源码契约）

- payload `can_confirm` 与 `CanConfirmSelection(` 同源（按 ① 选定的形态断言）；
- modal payload 的 `can_confirm` 引用 `CanConfirmModal(`；
- `resolve_rewards` descriptor 片段含 `requires_index = false`；
- `CanChooseRewardCard`/`CanSkipRewardCards` 方法体含 `IsEnabled`；
- 每条断言可否证（把对应改动还原 → 红 → 恢复）。

## 陷阱

- `GameStateService.cs` 不在测试工程编译集内：改完必须
  `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental` 做类型检查。
- **并行编辑**：`09-12-reward-screen-truth` 的 worker 同时改同一文件的 `ResolveNonModalScreen`
  （6802-6866）。本任务只改 390-400 / 900-960 / 1563-1567 / 1595-1610 / 4420-4445 / 4930-4950，
  绝不触碰 6802-6866；用精确上下文的 apply_patch，不要整文件覆写。
- 既有测试（`CompactViewFidelityTests`、`FtueModalPolicyTests`、`RewardFlowContractTests`、
  `DeckSelectionAvailabilityTests`）可能覆盖这些分支，先读再改。

