# Phase 1C Status

> 历史记录，2026-09-09 归档。以下状态和下一步仅属于 2026-03-10，不代表当前任务；当前状态见 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

更新时间：`2026-03-10`

## 已实现接口

- `GET /health`
- `GET /state`
- `GET /actions/available`
- `POST /action`

## 已实装并验证

- `end_turn`
- `play_card`
- `choose_map_node`
- `claim_reward`
- `choose_reward_card`
- `collect_rewards_and_proceed`
- `proceed`
- MCP `health_check`
- MCP `get_game_state`
- MCP `get_available_actions`
- MCP `end_turn`
- MCP `play_card`
- MCP `choose_map_node`
- MCP `claim_reward`
- MCP `choose_reward_card`
- MCP `collect_rewards_and_proceed`
- MCP `proceed`

## 已实装但尚未实机验证

- `skip_reward_cards`
- `select_deck_card`
- `map.nodes`

## `GET /state` 当前覆盖

- 顶层字段：`state_version`、`run_id`、`screen`、`in_combat`、`turn`、`available_actions`
- 战斗字段：`combat.player`、`combat.hand`、`combat.enemies`
- 构筑字段：`run.gold`、`run.deck`、`run.relics`、`run.potions`
- 地图字段：`map.current_node`、`map.available_nodes`、`map.rows`、`map.cols`、`map.starting_node`、`map.boss_node`、`map.second_boss_node`、`map.nodes`
- 奖励字段：`reward.pending_card_choice`、`reward.can_proceed`、`reward.rewards`、`reward.card_options`、`reward.alternatives`
- 选牌字段：`selection.kind`、`selection.prompt`、`selection.cards`

## 已覆盖的 `screen`

- `MAIN_MENU`
- `CHARACTER_SELECT`
- `MAP`
- `COMBAT`
- `EVENT`
- `SHOP`
- `REST`
- `REWARD`
- `CHEST`
- `CARD_SELECTION`
- `GAME_OVER`
- `UNKNOWN`

## 当前限制

- Windows 下不能热替换已加载的 Mod DLL，安装新版后必须重启游戏
- `end_turn`、`choose_map_node`、`proceed` 的稳定态判断仍偏保守，需要更多实机覆盖
- `collect_rewards_and_proceed` 仍保留“自动收奖励并选第一张”的最小策略，适合无人值守推进，不适合作为构筑决策接口
- `claim_reward` 已补齐奖励按钮显式入口，用于先进入卡牌奖励子界面，再交给 `choose_reward_card` / `skip_reward_cards`
- `skip_reward_cards` 当前按界面第一个替代按钮处理，默认对应跳过奖励；若后续有 mod 改写奖励替代项顺序，需要再收紧识别方式
- `select_deck_card` 当前按单张选择并自动确认实现，适用于删牌；多选场景后续再扩
- `map.nodes` 当前导出的是图结构和运行时状态快照，尚未提供内建路线评分逻辑；路线决策应由上层 LLM 或策略器完成

## 下一步

1. 重启游戏并实机验证 `map.nodes` 的完整性
2. 对照 UI 核对起点、Boss 点、分叉和连线顺序
3. 在 MCP 侧补一份路线规划提示模板，让 LLM 优先基于 `map.nodes` 做决策
4. 继续推进商店购买和删牌场景回归
