# 证据：compact 视图的字段缺口

## 结论

compact 视图（`BuildAgentViewPayload`，`GameStateService.cs:2973-3040`）不是 raw 的子集裁剪，
而是文本化重写。下列决策必需信息在 compact 中不存在，而 raw 已经算好：

1. **双方 powers 缺失**：compact 的 `player`（`:3116-3128`）与 `enemies`（`:3154-3165`）都不含 powers；
   raw 有 `CombatPlayerPayload.powers`（`:7586`）与 `CombatEnemyPayload.powers`（`:7754`），
   由 `BuildCreaturePowerPayloads`（`:5042`）填充。
2. **敌方意图数值缺失**：compact 只留 `intent` / `move_id`；raw 的 `CombatEnemyIntentPayload`
   （`:7760-7778`）带 `intent_type/label/damage/hits/total_damage/status_card_count`，
   由 `BuildEnemyIntentPayload`（`:5159-5187`）计算。compact 里唯一的聚合量是
   `combat.lethal_risks[].incoming_damage`（`:2868-2890`）与 `end_turn_will_kill_player`。
3. **卡牌 id 全缺**：`card_id` 的赋值点全部在 raw 构造器（`:3831/4987/5387/5667/5747/5770`）；
   compact 构造器 `BuildAgentHandCardPayload`（`:3544-3559`）、`BuildAgentChoiceCardPayload`（`:3577-3584`）、
   `BuildAgentPricedCardPayload`（`:3635-3642`）都没有；牌组还被 `BuildAgentCardStacks`（`:3663-3682`）
   按 `GroupKey`（`:8058`）合并。
4. **遗物只有名字**：`:3205-3207`；raw 的 `RunRelicPayload`（`:8001-8014`）带
   `relic_id/description/stack/is_melted`。宝箱同理：raw `ChestRelicOptionPayload.relic_id`（`:7302`）→
   compact 只有 `"name [rarity]"`（`:3487-3491`）。
5. **覆盖层缺 `underlying_screen`**：raw `ModalPayload.underlying_screen`（`:7833`，`:4866` 填充）→
   compact `BuildAgentModalPayload`（`:3502-3509`）没有。
6. **整块 `unlock` 被丢**：raw `GameStatePayload.unlock`（`:6925`，`BuildUnlockPayload` `:4722-4736`）；
   compact 顶层（`:3003-3038`）没有该键。
7. **队友摘要缺失**：raw `CombatPayload.players`（`:7607-7634`）与 `RunPayload.players`（`:7636-7657`）；
   compact 的 `BuildAgentCombatPayload` / `BuildAgentRunPayload` 都没有。

## 为什么它要紧

- `skills/sts2-mcp-player/SKILL.md:55-58`、`:71-77` 要求"查元数据再决策"，而元数据查询按 id 匹配
  （`GameDataFilter.FindItem`）；compact 无 id ⇒ 该工作流对卡牌 / 遗物失效。
- `SKILL.md:83`「Resolve overlays before room flow」需要知道覆盖层下面的房间。
- 战斗数值缺失 ⇒ 算不出致死线、无法按意图分配格挡；`lethal_risks` 只给全体意图合计。

## 反向（compact 有、raw 没有）

`glossary`、`profiles`、`actions`、`draw/discard/exhaust` 文本栈、`combat.hand[].can_play_result`
（`:3549`）—— 说明 compact 有自己的加工，不是纯裁剪；本次只做增量，不动这些。
