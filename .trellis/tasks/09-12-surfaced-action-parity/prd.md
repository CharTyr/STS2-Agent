# 让每个被暴露的动作/字段与执行口径一致

## 背景

探子穷举后的结论：动作级"暴露 vs 可执行"已经同源（同一个 `CanX` 同时驱动 `available_actions`
与 descriptor 列表），残留矛盾都在 **payload 派生值** 与 **动作口径** 之间：

| # | 现象 | 证据 |
|---|---|---|
| ① | `selection.can_confirm=true` 但 `confirm_selection` 不暴露 | payload `GameStateService.cs:4439-4441` 用 `gridMetadata.CanConfirm`；动作 `CanConfirmSelection`（`:942-954`）还要求 `RequiresConfirmation \|\| MinSelect < MaxSelect` |
| ② | FTUE modal 的 `modal.can_confirm=false` 但 `confirm_modal` 可用 | payload `:4946` 只看 `confirmButton != null`；动作 `CanConfirmModal`（`:1563-1567`）用 `FtueModalPolicy.ExposeConfirm`（FTUE 无按钮也放行） |
| ③ | `resolve_rewards` descriptor 谎报 `requires_index=true` | `:395-397`；对照 `collect_rewards_and_proceed`（`:402-404`）是 `false` |
| ④ | `skip_reward_cards` / `choose_reward_card` 不过滤按钮可用性 | `CanChooseRewardCard`（`:907-910`）只看 `GetCardRewardOptions().Count > 0`；`CanSkipRewardCards`（`:912-915`）只看替代按钮数量；同文件惯例（`CanClaimReward` `:902-905`）要求 `IsEnabled` |
| ⑤ | 水晶球暴露只看 `IsFinished` | `CanPlayCrystalSphere`（`:1091-1095`）`minigame is { IsFinished: false }`，不看屏幕可见性 |

## 目标

模型读到的每个"可以做"信号都能被 `act` 接受；每个"必须带 index"的 descriptor 都真的需要 index。

## 验收标准

- [ ] ① ② 的 payload 与动作判定同源（同一函数/同一表达式），并有可否证的源码契约测试。
- [ ] ③ 修正为 `requires_index = false`。
- [ ] ④ 过滤条件与同文件惯例一致（`IsEnabled`，必要时 `IsVisibleInTree`）；可否证。
- [ ] ⑤ 给出结论（改或不改 + 理由）并写进 evidence。
- [ ] 全量 C# 测试 / gates / preflight 绿。

## 范围外

- 不改 `GameActionService.cs`（执行端已同源；本轮只让 payload 与它对齐）。
- 若既有 `docs/api.md` 描述与修正后的口径冲突才改文档，不做无关重写。

