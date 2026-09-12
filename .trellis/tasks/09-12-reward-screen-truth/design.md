# 设计

## 改动（单点，4 行）

在 `GameStateService.cs` 的 `ResolveNonModalScreen` 里，`CARDS_VIEW` 早退之后、通用网格早退之前插入：

```csharp
// The reward-card overlay carries visible grid card holders, so the generic grid branch below
// would name it CARD_SELECTION and send the model after select_deck_card, an action this screen
// deliberately does not offer. Its own switch arm (REWARD) can only be reached from here, the
// same way NUnlockScreen claims UNLOCK ahead of the same generic branch.
if (currentScreen is NCardRewardSelectionScreen)
{
    return "REWARD";
}
```

## 测试

新建 `STS2AIAgent.Tests/RewardScreenContractTests.cs`（由主代理注册），断言：

1. 在 `ResolveNonModalScreen` 的方法体内，`NCardRewardSelectionScreen` 的索引 **小于**
   通用网格判据（`GetVisibleGridCardHolders`）的索引；
2. 两者之间含 `return "REWARD";`（用 `WithoutWhitespace` 后的 Contains，避免空白敏感）；
3. 通用网格分支之后，`NChooseACardSelectionScreen` 与 `NCardGridSelectionScreen` 的 arm 仍解析为
   `CARD_SELECTION`（防止有人顺手把它们也抬到前面去改语义）。

第 1、2 条与 `UnlockScreenContractTests` 同源同形，参考它的写法而不是发明新范式。

## 风险与实机项

- 这是对**一段 overlay 状态**的 `screen` 语义翻转：`CARD_SELECTION` → `REWARD`。
  离线只能证明"代码顺序正确、既有测试不红"；真正影响面（skill 路由、`docs/api.md` 契约、
  `scripts/run_sts2_validation.py:1332-1345` 的奖励清空循环）需要实机确认。
- `api-facts` 门禁会重新提取早退分支的屏幕名；`REWARD` 已在 `docs/api.md` 的屏幕枚举里，不会变红
  （实现时用一次 `--only api-facts` 证实）。
