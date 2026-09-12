# 奖励选牌浮层的屏幕名不再被遮蔽

## 背景

`GameStateService.ResolveNonModalScreen`（`STS2AIAgent/Game/GameStateService.cs:6802-6866`）里，
通用可见网格分支（E4，`:6820-6825`）排在 switch 之前：

```csharp
if (currentScreen is Node rootNode &&
    currentScreen is not NChooseABundleSelectionScreen &&
    GetVisibleGridCardHolders(rootNode).Count > 0)
{
    return "CARD_SELECTION";
}
```

而 `NCardRewardSelectionScreen => "REWARD"`（`:6840`）在 switch 里，永远到不了。

已证实的证据链：

- 奖励选牌浮层由 `NOverlayStack.Instance.Push(...)` 压栈（`extraction/.../NCardRewardSelectionScreen.cs:149`），
  `ActiveScreenContext.GetCurrentScreen` 会 `Peek()` 到它（`ActiveScreenContext.cs:74-77`）——所以 E4 看的就是这个浮层自己；
- 它在 `_Ready` 里 `NGridCardHolder.Create(nCard)` + `AddChildSafely`（`:156, 187-189`），`CardModel` 非空
  （`NGridCardHolder.cs:94-106`），动画只改 `modulate`（`:196-197`）不影响 `IsVisibleInTree()`；
- 因此 E4 命中，`/state.screen` 报 `CARD_SELECTION`。

后果（已核实）：`available_actions` 在这个屏幕上只有
`resolve_rewards`/`collect_rewards_and_proceed`/`choose_reward_card`/`skip_reward_cards`，
**没有** `select_deck_card`/`confirm_selection`，`selection` 段为 null——但
`skills/sts2-mcp-player/SKILL.md:105` 的 Screen Routing 写着「`CARD_SELECTION`：用 `select_deck_card`」，
`docs/api.md:1359/1377` 也把两个奖励动作的前提写成 `screen="REWARD"`。模型照屏幕名去调，只会拿到 409。

这不是新发现：`.trellis/tasks/archive/2026-09/09-12-residual-audit-gaps/prd.md:41-43` 与
`journal-1.md:346,384` 都把它记为"需单独评估与实机确认"的推迟项。

## 目标

在 E4 之前加一条与 E1 同形的早退分支，让奖励选牌浮层报 `REWARD`：
`UNLOCK` → 战斗手牌 → `CARDS_VIEW` → **`REWARD`（新增）** → 通用网格 → `MULTIPLAYER_LOBBY` → `COMBAT`。

这正是仓库里已有的正确先例：`NUnlockCardsScreen` 同样含可见 `NGridCardHolder`
（`NUnlockCardsScreen.cs:93`），靠 E1 抢在 E4 之前拿到 `UNLOCK`，且被
`STS2AIAgent.Tests/UnlockScreenContractTests.cs:21-33` 钉住"unlock 分支必须早于可见网格分支"。

## 验收标准

- [x] `ResolveNonModalScreen` 里 `NCardRewardSelectionScreen` 的判定早于通用网格分支。
- [x] 源码契约测试钉住这条顺序（与 `UnlockScreenContractTests` 同款断言形态），并可否证
      （把新分支挪到通用分支之后 → 变红）。
- [x] 其它被遮蔽的 arm 值不变（`NChooseACardSelectionScreen`、四个 `NDeck*Select`、
      `NCardGridSelectionScreen` 都仍解析为 `CARD_SELECTION`），有测试或断言证明没有连带改动。
- [x] 既有测试全绿：`ScreenResolutionContractTests`（它只解析 switch 之后的 arm，不应受影响）、
      `UnlockScreenContractTests`、`check_verification_gates.py` 的 `api-facts`（屏幕集合与文档一致性）。
- [x] 文档与脚本核对结论写进 evidence：`docs/api.md`、`SKILL.md` 的既有措辞在修改后**变准**，
      因此不需要改文档；若发现需要改的，一并改并说明。
- [x] C# / MCP 全量测试、gates、preflight 通过。

## 范围外

- `NCardPileScreen`（看牌堆）与 `NCardLibrary`（图鉴）没有对应 switch arm，改它们会让屏幕名退化成
  `UNKNOWN`（skill 把 UNKNOWN 当瞬态，更糟）。本轮只记录证据，不动。
- 不改 `GetDeckSelectionOptions`（上一轮已删掉它的通用兜底）。
