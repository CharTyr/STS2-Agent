# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Game/GameStateService.cs` | 4 处：`:397` `resolve_rewards` → `requires_index = false`；`CanSkipRewardCards` → `GetCardRewardAlternativeButtons(...).Any(button => button.IsEnabled)`（并给 `CanChooseRewardCard` 加注释说明为何**不加**过滤）；`:4448` `selection.can_confirm` → `CanConfirmSelection(currentScreen)`；`:4955` `modal.can_confirm` → `CanConfirmModal(currentScreen)` |
| `STS2AIAgent.Tests/SurfacedActionParityTests.cs` | 新增，6 条源码契约测试 |
| `STS2AIAgent.Tests/TestRunner.cs` | 追加 6 行 `SurfacedAction.*` 注册 |

## 逐条结论

| # | 结论 | 依据 |
|---|---|---|
| ① | payload 改为直接调用 `CanConfirmSelection(currentScreen)`（design 的 A 方案） | 同源即消除"payload 说能确认、动作不暴露"的窗口；`docs/api.md:1428` 与 `screen-playbooks.md:55` 本就按 `CanConfirmSelection` 规则描述，是旧代码偏离文档 |
| ② | payload 改为 `CanConfirmModal(currentScreen)` | FTUE 弹窗无自带按钮时仍可确认（`FtueModalPolicy.ExposeConfirm`），旧的 `confirmButton != null` 会低报 |
| ③ | `requires_index = false` | 执行端不消费 index；`docs/api.md:1028` 的"可带 option_index"描述继续成立 |
| ④ | `CanSkipRewardCards` 加 `IsEnabled`；`CanChooseRewardCard` **不加** | 见下节 |
| ⑤ | **不改** | 见下节 |

## ④ / ⑤ 的硬证据（prd 要求落进 evidence）

**④ `choose_reward_card` 加不上、也不该加过滤**：

1. `MegaCrit.Sts2.Core.Nodes.Cards.Holders.NCardHolder : Control`，`Control` 没有 `IsEnabled`；
   奖励选牌的可点状态是私有 `_isClickable`，由 `NCardRewardSelectionScreen.DisableCardsForShortTimeAfterOpening()` 翻转。
2. 实测编译：改成 `.Any(holder => holder.IsEnabled)` →
   `GameStateService.cs(913,73): error CS1061: "NCardHolder"未包含"IsEnabled"的定义`。
3. 执行端 `ExecuteChooseRewardCardAsync` 用 `selected.EmitSignal(NCardHolder.SignalName.Pressed, selected)` 选牌，
   **绕过** `_isClickable` ⇒ `Count > 0` 正是执行端能兑现的集合，加任何 enabled/visible 过滤只会制造假阴性。

对照组（skip 能加）：`NCardRewardAlternativeButton : NButton : NClickableControl`，`IsEnabled` 是公开属性，
与 `CanClaimReward` 读 `NRewardButton.IsEnabled` 同源；`GetCardRewardAlternativeButtons` 已过滤 `IsVisibleInTree()`。

**⑤ 水晶球不加可见性过滤（不可达）**：

- `ActiveScreenContext.GetCurrentScreen()` 只在 `NOverlayStack.Peek()` 处产出 `NCrystalSphereScreen`；
  该屏 `Push` 时 `Visible` 保持 true（`AfterOverlayOpened` 只 tween `modulate:a`，`AfterOverlayHidden` 只 `_proceedButton.Disable()`）。
- 地图/轮拱盖住 overlay 栈时，`GetCurrentScreen` 更早返回 `NMapScreen` / `CanstoneContainer`。
- `GetCrystalSphereMinigame` 首行已是 `is not NCrystalSphereScreen → null`，屏幕类型判断就是全部闸门。
- `SurfacedAction.CrystalSphereScreenGuard` 钉住这一形态：日后有人真加了 `IsVisibleInTree` 过滤会红（已在可否证自检中验证）。

## 验收

| 标准 | 证据 |
|---|---|
| ① ② 同源 | 断言 `can_confirm=CanConfirmSelection(currentScreen)` 与 `can_confirm=CanConfirmModal(currentScreen)` |
| ③ | 断言 `name="resolve_rewards",requires_target=false,requires_index=false` |
| ④ 是否可证 | `SurfacedAction.SkipRewardCardsEnabledFilter`；`SurfacedAction.ChooseRewardCardCollection` 钉住"不加过滤"的现状 |
| 全量门禁 | 把 6 处形态逐一还原 → `PASS=341 FAIL=6`（6 条对应断言全红）→ 恢复后 **347 PASS / 0 FAIL**；构建 0 warning；6 gate 全绿；preflight exit 0 |

## 收尾修正（主代理追加，见下一节提交）

`CanSkipRewardCards` 收紧后，执行端 `ExecuteSkipRewardCardsAsync` 仍写 `alternatives.First()` ⇒
"暴露说可以、执行可能点到被禁用的第一个按钮"的新窗口。已把执行端改为
`alternatives.First(button => button.IsEnabled)`（该集合非空由 409 守卫保证），
并加断言 `SurfacedAction.SkipTargetsEnabledAlternative` 钉住两端口径同源。

## 只能实机验证

- FTUE 弹窗下 `modal.can_confirm=true` 时 `confirm_modal` 实机确实能收尾。
- 单选、`RequiresConfirmation=false` 的网格，`selection.can_confirm` 变 false 后模型不再多发 `confirm_selection`。
- `skip_reward_cards` 的 `IsEnabled` 过滤在真实奖励屏是否恒等价于旧行为。
- `resolve_rewards` 不带 index 的实机表现。
- 水晶球"不可见但被解析"的不可达性（结论基于反编译路径，非运行时观测）。

