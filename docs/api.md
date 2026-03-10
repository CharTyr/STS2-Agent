# STS2 AI Agent Mod - 最小 HTTP API 协议 v0

状态：草案，可实现  
版本：`2026-03-10-v0`

## 约束

- 协议基于 `HTTP + JSON`
- 默认监听 `http://127.0.0.1:8080`
- 响应类型固定为 `application/json; charset=utf-8`
- 新增字段必须向后兼容，不删除已有字段

## 通用响应

成功响应：

```json
{
  "ok": true,
  "request_id": "req_20260310_0001",
  "data": {}
}
```

失败响应：

```json
{
  "ok": false,
  "request_id": "req_20260310_0001",
  "error": {
    "code": "invalid_action",
    "message": "Action is not available in the current state.",
    "details": {
      "action": "end_turn",
      "screen": "MAP"
    },
    "retryable": false
  }
}
```

## 错误码

| 错误码 | 含义 |
| --- | --- |
| `unknown_error` | 未分类错误 |
| `invalid_request` | 请求体不合法 |
| `invalid_action` | 当前状态下不能执行该动作 |
| `invalid_target` | 目标或索引非法 |
| `state_unavailable` | 当前状态暂时不可安全读取 |
| `internal_error` | 服务内部异常 |

## Screen 枚举

- `UNKNOWN`
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

## Action Status

- `completed`
- `pending`
- `rejected`
- `failed`

## `GET /health`

作用：返回 Mod 基础状态。

```json
{
  "ok": true,
  "request_id": "req_20260310_0001",
  "data": {
    "service": "sts2-ai-agent",
    "mod_version": "0.1.0",
    "protocol_version": "2026-03-10-v0",
    "game_version": "v0.98.2",
    "status": "ready"
  }
}
```

## `GET /state`

作用：返回当前最小状态快照。

关键字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `state_version` | number | 状态模型版本 |
| `run_id` | string | 本局运行标识 |
| `screen` | string | 当前逻辑界面 |
| `in_combat` | boolean | 是否处于战斗流程 |
| `turn` | number or null | 当前回合数 |
| `available_actions` | string[] | 当前可执行动作名称 |
| `run.deck` | object[] or null | 当前牌库 |
| `run.relics` | object[] or null | 当前遗物 |
| `run.gold` | number or null | 当前金币 |
| `reward.card_options` | object[] | 卡牌奖励候选 |
| `reward.alternatives` | object[] | 卡牌奖励替代操作，例如跳过 |
| `selection.cards` | object[] or null | 牌库选牌界面候选 |

### 战斗示例

```json
{
  "ok": true,
  "request_id": "req_20260310_0002",
  "data": {
    "state_version": 1,
    "run_id": "WXJVZBQFK2",
    "screen": "COMBAT",
    "in_combat": true,
    "turn": 1,
    "available_actions": [
      "end_turn",
      "play_card"
    ],
    "run": {
      "gold": 99,
      "deck": [
        {
          "index": 0,
          "card_id": "STRIKE_IRONCLAD",
          "name": "打击",
          "upgraded": false,
          "card_type": "Attack",
          "rarity": "Starter",
          "energy_cost": 1,
          "star_cost": 0
        }
      ],
      "relics": [
        {
          "index": 0,
          "relic_id": "BURNING_BLOOD",
          "name": "燃烧之血",
          "is_melted": false
        }
      ]
    }
  }
}
```

### 奖励示例

```json
{
  "ok": true,
  "request_id": "req_20260310_0006",
  "data": {
    "screen": "REWARD",
    "available_actions": [
      "choose_reward_card",
      "skip_reward_cards"
    ],
    "reward": {
      "pending_card_choice": true,
      "can_proceed": false,
      "rewards": [],
      "card_options": [
        {
          "index": 0,
          "card_id": "POMMEL_STRIKE",
          "name": "剑柄打击",
          "upgraded": false
        }
      ],
      "alternatives": [
        {
          "index": 0,
          "label": "跳过"
        }
      ]
    }
  }
}
```

### 删牌界面示例

```json
{
  "ok": true,
  "request_id": "req_20260310_0007",
  "data": {
    "screen": "CARD_SELECTION",
    "available_actions": [
      "select_deck_card"
    ],
    "selection": {
      "kind": "deck_card_select",
      "prompt": "选择一张牌移除",
      "cards": [
        {
          "index": 0,
          "card_id": "STRIKE_IRONCLAD",
          "name": "打击",
          "upgraded": false,
          "card_type": "Attack",
          "rarity": "Starter"
        }
      ]
    }
  }
}
```

## `GET /actions/available`

作用：返回当前状态下允许执行的动作描述。

说明：

- `play_card.requires_target` 固定为 `false`，因为是否需要目标取决于具体卡牌
- 调用方必须结合 `GET /state` 中 `combat.hand[index].requires_target` 决定是否传 `target_index`
- `choose_map_node` 使用 `option_index` 选择 `map.available_nodes[index]`
- `choose_reward_card` 使用 `option_index` 选择 `reward.card_options[index]`
- `claim_reward` 使用 `option_index` 选择 `reward.rewards[index]`
- `select_deck_card` 使用 `option_index` 选择 `selection.cards[index]`

## `POST /action`

作用：执行单个动作。

请求体：

```json
{
  "action": "choose_reward_card",
  "card_index": null,
  "target_index": null,
  "option_index": 1,
  "client_context": {
    "source": "mcp",
    "tool_name": "choose_reward_card"
  }
}
```

### 已实现动作

- `end_turn`
- `play_card`
- `choose_map_node`
- `collect_rewards_and_proceed`
- `claim_reward`
- `choose_reward_card`
- `skip_reward_cards`
- `select_deck_card`
- `proceed`

### `choose_reward_card`

- 前提：当前 `screen = "REWARD"` 且 `reward.pending_card_choice = true`
- 参数：`option_index`
- 行为：选择指定卡牌奖励

### `claim_reward`

- 前提：当前 `screen = "REWARD"` 且 `reward.rewards` 非空
- 参数：`option_index`
- 行为：领取指定奖励按钮，常用于先进入卡牌奖励子界面

### `skip_reward_cards`

- 前提：当前 `screen = "REWARD"` 且 `reward.alternatives` 非空
- 参数：无
- 行为：点击卡牌奖励界面的替代按钮，默认用于跳过拿牌

### `select_deck_card`

- 前提：当前 `screen = "CARD_SELECTION"`
- 参数：`option_index`
- 行为：选择指定牌并自动确认
- 当前目标：优先覆盖删牌场景

### `collect_rewards_and_proceed`

- 前提：当前 `screen = "REWARD"`
- 参数：无
- 行为：自动收取奖励，遇到卡牌奖励默认选第一张，并在可继续时点击继续
- 说明：适合无人值守推进，不适合构筑决策

### `proceed`

- 前提：当前界面存在可用 `ProceedButton`
- 参数：无
- 行为：点击继续按钮
