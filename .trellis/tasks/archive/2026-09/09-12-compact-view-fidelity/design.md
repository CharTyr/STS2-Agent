# 设计：compact 视图的增补

## 落点一：战斗（`BuildAgentCombatPayload`，`GameStateService.cs:3110-3168`）

- `player` 增加 `powers`：把 raw `combat.player.powers` 压成简短行（复用既有的 power 行格式化，
  没有现成函数就取 `power_id` + `amount` 两个字段）。
- 每个 `enemies[]` 增加：
  - `powers`：同上；
  - `intents`：`intent_type` / `label` / `damage` / `hits` / `total_damage` / `status_card_count`。
- 数据来源就是 raw 的 `combat` 变量（`BuildAgentCombatPayload` 已持有），**不需要新的反射或游戏 API**。
- 以实现时的真实字段名为准；若 raw 形状与本文件的示例不同，在 evidence 里给出原文字段名。

## 落点二：id 回归

| 位置 | 增加 |
|---|---|
| `BuildAgentHandCardPayload`（`:3544-3559`） | `card_id` |
| `BuildAgentChoiceCardPayload`（`:3577-3584`） | `card_id`（选择屏 / 奖励卡） |
| `BuildAgentPricedCardPayload`（`:3635-3642`） | `card_id`（商店） |
| `BuildAgentCardStacks`（`:3663-3682`） | 每个合并组暴露 `card_id` 与 `count`；若一个组可能合并不同 id，则暴露 `card_ids` 数组 |
| 遗物（`:3205-3207`） | `relic_id` |
| 宝箱遗物选项（`:3487-3491`） | `relic_id` |
| 卡包（bundle）选项 | 与 raw 的 `card_id` / `relic_id` 对齐 |

## 落点三：覆盖层与队友

- `BuildAgentModalPayload`（`:3502-3509`）加 `underlying_screen`（raw `modal.underlying_screen`）。
- compact 顶层（`:3003-3038`）加 `unlock`（复用 `BuildUnlockPayload`，`:4722-4736`）。
- `BuildAgentCombatPayload` 加 `players`（raw `:7607-7634`）；`BuildAgentRunPayload` 加 `players`（`:7636-7657`）。

## 测试策略（GameStateService.cs 不进测试工程，只能源码契约 + 反向对照）

新增 `STS2AIAgent.Tests/CompactViewFidelityTests.cs`（由主代理注册），每条形如：

1. 用 `AgentSourceFixture.MethodBody(Read("STS2AIAgent/Game/GameStateService.cs"), "BuildAgentCombatPayload")`
   断言出现 `powers`、`intents`、`total_damage`、`players`；
2. 对每个新增字段名，断言它在 raw 的对应类型定义处**也**出现（例如 `CombatEnemyIntentPayload` 段里出现
   `total_damage`），把"发明字段"变成红色；
3. 断言 compact 没有被整体倾倒成 raw（例如 `BuildAgentCombatPayload` 里不出现 `valid_target_indices`
   这类 raw 专用字段）。

写断言后必须自检可否证：临时删掉一个新增字段 → 对应测试变红 → 还原。

## 兼容与回滚

- 纯增量字段，旧消费者不会因为多出键而失败。
- 回滚 = 单个方法级 diff，可整体 revert。
