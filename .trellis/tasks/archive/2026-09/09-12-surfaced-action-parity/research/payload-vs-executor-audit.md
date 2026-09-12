# 证据：payload 与执行口径的差异

## 探子穷举结论

动作级"暴露 vs 可执行"已同源：`BuildAvailableActionNames`（`GameStateService.cs:2528-2810`）与
`BuildAvailableActionsPayload`（`~330-560`）都调同一组 `CanX`；`GameActionService` 的
`ExecuteAsync` 分支各自复检同一 `CanX`。残留差异在 payload 的**派生值**。

## 逐条 file:line

| # | 位置 | 读到的代码 |
|---|---|---|
| ① | `GameStateService.cs:943-954` | `CanConfirmSelection`：combat 分支要求 `RequiresConfirmation && CanConfirm`；grid 分支要求 `CanConfirm && (RequiresConfirmation \|\| MinSelect < MaxSelect)` |
| ① | `GameStateService.cs:4439-4441` | `can_confirm = hasCombatHandSelection ? combatHandSelection.CanConfirm : hasCardGridSelection && cardGridSelection.CanConfirm` |
| ② | `GameStateService.cs:1563-1567` | `CanConfirmModal` → `FtueModalPolicy.ExposeConfirm(GetOpenModal()?.GetType().Name, hasButton)` |
| ② | `GameStateService.cs:4946` | `can_confirm = confirmButton != null` |
| ② | `FtueModalPolicy.cs` | `ExposeConfirm = hasUsableConfirmButton \|\| IsFtueType(modalTypeName)` |
| ③ | `GameStateService.cs:395-397` | `resolve_rewards` descriptor `requires_index = true` |
| ③ | `GameStateService.cs:402-404` | `collect_rewards_and_proceed` descriptor `requires_index = false`（对照） |
| ④ | `GameStateService.cs:902-905` | `CanClaimReward` = `GetRewardButtons(...).Any(button => button.IsEnabled)` |
| ④ | `GameStateService.cs:907-910` | `CanChooseRewardCard` = `GetCardRewardOptions(currentScreen).Count > 0` |
| ④ | `GameStateService.cs:912-915` | `CanSkipRewardCards` = `GetCardRewardAlternativeButtons(currentScreen).Count > 0` |
| ⑤ | `GameStateService.cs:1091-1095` | `CanPlayCrystalSphere` = `GetCrystalSphereMinigame(...) is { IsFinished: false }` |
| ⑤ | `GameStateService.cs:1049-1053` | `GetCrystalSphereMinigame` 首行即 `currentScreen is not NCrystalSphereScreen → null` |

## 影响

模型按 `selection.can_confirm`/`modal.can_confirm` 决策时，可能遇到"信号说可以、`act` 说不行"
（① ② 的反向不一致会诱发多余或缺失的确认动作）。descriptor（③）的 `requires_index` 被 MCP
guided 工具当作参数校验依据，谎报会让调用方多传一个被忽略的 index。

## 未经实机确证

修好后的运行期行为（模型是否因此少犯错）只能在实机上观察；离线只能钉住"两处判定同源"。

