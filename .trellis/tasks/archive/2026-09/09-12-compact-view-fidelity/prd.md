# 让 compact 视图带上决策真正需要的字段

## 背景

游戏内 Agent 每步只读 compact 视图（`get_game_state` 的 `agent_view`，由
`GameStateService.BuildAgentViewPayload` 构造，`STS2AIAgent/Game/GameStateService.cs:2973`）。
它**不是 raw 的字段子集**，而是一份"文本化重写"：多个决策必需信息被替换成人类可读字符串或直接丢掉，
而 raw 里同一份数据是完整的。已证实的具体丢失：

| 丢失内容 | compact 位置 | raw 里有 |
|---|---|---|
| 双方 powers（力量 / 易伤 / 虚弱 / 荆棘 / 仪式…） | `:3116-3128`（player）、`:3154-3165`（enemies）都没有 | `CombatPlayerPayload.powers`（`:7586`）、`CombatEnemyPayload.powers`（`:7754`），由 `BuildCreaturePowerPayloads`（`:5042`）填充 |
| 敌方意图数值 | 只剩 `intent`（= moveId 字符串）与 `move_id` | `CombatEnemyPayload.intents[]`（`:7760-7778`：`intent_type/label/damage/hits/total_damage/status_card_count`） |
| 卡牌 id（手牌 / 牌组 / 奖励 / 商店 / 选择 / 卡包） | `BuildAgentHandCardPayload`（`:3544-3559`）等只有 `i/line/selected/keywords/mods` | `:3831/4987/5387/5667/5747/5770` 均有 `card_id` |
| 遗物 id | `:3205-3207` 只有名字 | `RunRelicPayload.relic_id`（`:8001-8014`） |
| 覆盖层底下的屏幕 | `BuildAgentModalPayload`（`:3502-3509`）无 | `ModalPayload.underlying_screen`（`:7833`） |
| 解锁 payload | compact 顶层没有 `unlock` 键 | `GameStatePayload.unlock`（`:6925`，`BuildUnlockPayload` `:4722-4736`） |
| 队友血线 | compact 的 combat / run 都没有 `players` | `CombatPlayerSummaryPayload` / `RunPlayerSummaryPayload`（`:7607-7657`） |

后果（对自动游玩是致命的）：

- 算不出致死线、不知道该挡多少、看不懂力量 / 易伤，战斗决策退化成"凭记忆猜"；
- `skills/sts2-mcp-player/SKILL.md:55-58`、`:71-77` 要求"先查元数据再决策"，而元数据查询按 id 精确匹配
  （`GameDataFilter.FindItem` 走 id 键），compact 下模型**根本没有卡牌 / 遗物 id**；
- 覆盖层下不知道自己在哪个房间，与 `SKILL.md:83`「先解覆盖层」的规则冲突。

## 目标

在**不把 compact 变回 raw** 的前提下，补回"决策必需、且 raw 已经算好"的信息：

1. `combat.player.powers` 与 `combat.enemies[].powers`（沿用 raw 的 power 形状，保持紧凑）。
2. `combat.enemies[].intents[]`（数值：`damage` / `hits` / `total_damage` / `intent_type` / `label`）。
3. id 回归：手牌、牌组（每个合并组要暴露它代表哪些卡）、奖励卡、商店卡、选择卡、卡包、遗物、宝箱遗物选项。
4. `modal.underlying_screen`、compact 顶层的 `unlock` 段、`combat.players` 与 `run.players`（队友血线）。

## 验收标准

- [x] 上述字段全部出现在对应 compact 构造方法体内（源码契约测试逐条断言）。
- [x] 每个新增字段名都能在 raw 的同类 payload 里找到出处（测试断言 raw 侧存在同名字段，防止"发明字段"）。
- [x] compact 体积受控：powers 用简短行，intents 只带数值与类型，不把 raw 的整段结构与全量牌堆倒进去。
- [x] 文档同步：`docs/api.md` 的 agent_view 说明与 `skills/sts2-mcp-player/SKILL.md` 的相关段落随之更新；
      `<!-- BEGIN SHARED PLAY CONTRACT -->` / `<!-- END SHARED PLAY CONTRACT -->` 两个 marker 本身不动
      （该文件被 mod 构建期内嵌，marker 决定内嵌切片的边界）。
- [x] `docs/api.md` 动作契约块（55 个动作名）逐字不动。
- [x] C# 与 MCP 全量测试、`check_verification_gates.py`、`check_release_package.py`、preflight 通过。

## 范围外

- 不改 raw 的任何字段（`/state` 的既有消费者不受影响）。
- 不改 `ResolveNonModalScreen` 的屏幕名语义。
- 不引入新的状态端点或版本号变更。
