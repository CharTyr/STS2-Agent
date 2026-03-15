# STS2 AI Agent Mod — HTTP API

状态：可实现
协议版本：`2026-03-10-v0`

---

## 约束

- 协议基于 `HTTP + JSON`
- 默认监听 `http://127.0.0.1:8080`
- 响应类型固定为 `application/json; charset=utf-8`
- 新增字段必须向后兼容，不删除既有字段

---

## 通用响应格式

### 成功

```json
{
  "ok": true,
  "request_id": "req_20260310_120000_1234",
  "data": { ... }
}
```

### 失败

```json
{
  "ok": false,
  "request_id": "req_20260310_120000_1234",
  "error": {
    "code": "invalid_action",
    "message": "Action is not available in the current state.",
    "details": { "action": "end_turn", "screen": "MAP" },
    "retryable": false
  }
}
```

---

## 错误码

| 错误码 | HTTP 状态码 | 含义 | 可重试 |
| --- | --- | --- | --- |
| `invalid_request` | 400 | 请求体缺少必要字段或格式非法 | 否 |
| `not_found` | 404 | 路由不存在 | 否 |
| `invalid_action` | 409 | 当前状态下不能执行该动作 | 否 |
| `invalid_target` | 409 | 目标索引超出范围 | 否 |
| `state_unavailable` | 503 | 游戏状态暂时不可安全读取（如正在过渡） | 是 |
| `internal_error` | 500 | 服务内部异常 | 否 |

---

## Screen 枚举

| 值 | 含义 |
| --- | --- |
| `MAIN_MENU` | 主菜单、补丁说明、子菜单、Logo 动画 |
| `CHARACTER_SELECT` | 角色选择界面 |
| `MAP` | 地图界面 |
| `COMBAT` | 战斗中 |
| `EVENT` | 事件交互 |
| `SHOP` | 商店 |
| `REST` | 休息点 |
| `REWARD` | 奖励结算 / 卡牌奖励选择 |
| `CHEST` | 宝箱房 |
| `CARD_SELECTION` | 牌库选牌界面（删牌等） |
| `MODAL` | 阻塞中的弹窗 / FTUE |
| `GAME_OVER` | 游戏结束 |
| `UNKNOWN` | 无法识别的界面 |

## Action Status

动作执行后的 `status` 字段：

| 值 | 含义 |
| --- | --- |
| `completed` | 动作已完成，返回的 `state` 已稳定 |
| `pending` | 动作已提交，但游戏状态尚在过渡中（等待动画/队列清空） |

---

## `GET /health`

返回 Mod 基础状态。用于确认游戏正在运行且 Mod 已加载。

### 响应示例

```json
{
  "ok": true,
  "request_id": "req_20260310_120000_1234",
  "data": {
    "service": "sts2-ai-agent",
    "mod_version": "0.4.0",
    "protocol_version": "2026-03-11-v1",
    "game_version": "v0.98.2",
    "status": "ready"
  }
}
```

---

## `GET /state`

返回当前游戏状态的完整快照。这是 AI Agent 做出决策前最重要的端点。

### 顶层字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `state_version` | number | 状态模型版本（当前固定为 6） |
| `run_id` | string | 本局运行标识（种子字符串） |
| `screen` | string | 当前逻辑界面（见 Screen 枚举） |
| `in_combat` | boolean | 是否处于战斗流程 |
| `turn` | number \| null | 当前回合数（非战斗时为 null） |
| `available_actions` | string[] | 当前可执行动作名列表 |
| `combat` | object \| null | 战斗状态（仅战斗中存在） |
| `run` | object \| null | 本局运行状态 |
| `map` | object \| null | 地图状态（仅地图界面存在） |
| `reward` | object \| null | 奖励状态（仅奖励界面存在） |
| `selection` | object \| null | 选牌状态（仅选牌界面存在） |
| `chest` | object \| null | 宝箱状态（仅宝箱房存在） |
| `event` | object \| null | 事件状态（仅事件房存在） |
| `shop` | object \| null | 商店状态（仅商店房存在） |
| `rest` | object \| null | 休息点状态（仅休息点存在） |
| `character_select` | object \| null | 角色选择状态（仅角色选择界面存在） |
| `modal` | object \| null | 阻塞弹窗状态（仅 MODAL 界面存在） |
| `game_over` | object \| null | 游戏结束状态（仅 GAME_OVER 界面存在） |

### `combat` 子结构

#### `combat.player`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `current_hp` | number | 当前生命值 |
| `max_hp` | number | 最大生命值 |
| `block` | number | 当前格挡值 |
| `energy` | number | 当前能量 |
| `stars` | number | 当前星星数 |
| `powers` | object[] | 玩家当前持有的 Power / Buff / Debuff 列表 |

#### `combat.player.powers[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | Power 在当前列表中的索引 |
| `power_id` | string | Power 内部 ID |
| `name` | string | Power 显示名称 |
| `amount` | number \| null | Power 层数/数值（部分 Power 可能为空） |
| `is_debuff` | boolean | 是否为 Debuff |

#### `combat.hand[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 手牌索引（用于 `play_card` 的 `card_index`） |
| `card_id` | string | 卡牌内部 ID |
| `name` | string | 卡牌显示名称 |
| `upgraded` | boolean | 是否已升级 |
| `target_type` | string | 目标类型枚举（`None`, `AnyEnemy`, `AnyAlly` 等） |
| `requires_target` | boolean | 是否需要指定目标 |
| `costs_x` | boolean | 是否为能量 X 费卡 |
| `star_costs_x` | boolean | 是否为星星 X 费卡 |
| `energy_cost` | number | 能量消耗（含修正） |
| `star_cost` | number | 星星消耗（含修正） |
| `playable` | boolean | **当前是否可打出** |
| `unplayable_reason` | string \| null | 不可打出原因（`not_enough_energy`, `not_enough_stars`, `no_living_allies`, `blocked_by_hook`, `unplayable`） |

#### `combat.enemies[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 敌人索引（用于 `play_card` 的 `target_index`） |
| `enemy_id` | string | 敌人内部 ID |
| `name` | string | 敌人显示名称 |
| `current_hp` | number | 当前生命值 |
| `max_hp` | number | 最大生命值 |
| `block` | number | 当前格挡值 |
| `is_alive` | boolean | 是否存活 |
| `is_hittable` | boolean | 是否可被攻击 |
| `powers` | object[] | 敌人当前持有的 Power / Buff / Debuff 列表 |
| `intent` | string \| null | 兼容旧字段，等同于怪物下一招的原始 `move_id` |
| `move_id` | string \| null | 怪物下一招的内部状态 ID，例如 `PECK_MOVE` |
| `intents` | object[] | 怪物下一招拆解出的具体意图列表，顺序与游戏 UI 一致 |

#### `combat.enemies[].powers[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | Power 在当前列表中的索引 |
| `power_id` | string | Power 内部 ID |
| `name` | string | Power 显示名称 |
| `amount` | number \| null | Power 层数/数值（部分 Power 可能为空） |
| `is_debuff` | boolean | 是否为 Debuff |

#### `combat.enemies[].intents[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 意图索引 |
| `intent_type` | string | 具体意图类型，如 `Attack`、`Buff`、`StatusCard` |
| `label` | string \| null | UI 上显示的意图文字，如 `7`、`7x2` |
| `damage` | number \| null | 单次伤害，非攻击意图时为 null |
| `hits` | number \| null | 攻击次数，非攻击意图时为 null |
| `total_damage` | number \| null | 总伤害，非攻击意图时为 null |
| `status_card_count` | number \| null | 塞入状态牌数量，仅 `StatusCard` 意图时存在 |

### `run` 子结构

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `current_hp` | number | 当前生命值 |
| `max_hp` | number | 最大生命值 |
| `gold` | number | 当前金币 |
| `max_energy` | number | 基础最大能量 |
| `deck[]` | object[] | 当前牌库 |
| `relics[]` | object[] | 当前遗物 |
| `potions[]` | object[] | 当前药水槽 |

#### `run.deck[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 牌库中的索引 |
| `card_id` | string | 卡牌内部 ID |
| `name` | string | 卡牌名称 |
| `upgraded` | boolean | 是否已升级 |
| `card_type` | string | 类型（`Attack`, `Skill`, `Power`, `Status`, `Curse`） |
| `rarity` | string | 稀有度（`Starter`, `Common`, `Uncommon`, `Rare`） |
| `costs_x` | boolean | 是否为能量 X 费卡 |
| `star_costs_x` | boolean | 是否为星星 X 费卡 |
| `energy_cost` | number | 能量消耗 |
| `star_cost` | number | 星星消耗 |

#### `run.relics[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 遗物索引 |
| `relic_id` | string | 遗物内部 ID |
| `name` | string | 遗物名称 |
| `description` | string \| null | 遗物描述（若可读取） |
| `stack` | number \| null | 遗物层数/计数（若适用） |
| `is_melted` | boolean | 是否已熔炼 |

#### `run.potions[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 药水槽索引 |
| `potion_id` | string \| null | 药水 ID（空槽为 null） |
| `name` | string \| null | 药水名称（空槽为 null） |
| `description` | string \| null | 药水描述（空槽为 null） |
| `rarity` | string \| null | 药水稀有度（空槽为 null） |
| `occupied` | boolean | 是否有药水 |
| `usage` | string \| null | 药水使用时机（如 `CombatOnly`, `AnyTime`） |
| `target_type` | string \| null | 药水目标类型 |
| `is_queued` | boolean | 是否已入队等待生效 |
| `requires_target` | boolean | 是否需要额外目标 |
| `can_use` | boolean | 当前是否可手动使用 |
| `can_discard` | boolean | 当前是否可丢弃 |

### `map` 子结构

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `current_node` | object \| null | 当前所在坐标 `{ row, col }` |
| `is_travel_enabled` | boolean | 地图是否允许移动 |
| `is_traveling` | boolean | 是否正在移动中 |
| `map_generation_count` | number | 地图生成计数 |
| `rows` | number | 地图总行数 |
| `cols` | number | 地图总列数 |
| `starting_node` | object \| null | 起点坐标 `{ row, col }` |
| `boss_node` | object \| null | Boss 坐标 `{ row, col }` |
| `second_boss_node` | object \| null | 双 Boss Act 的第二个 Boss |
| `available_nodes[]` | object[] | 当前可前往的节点 |
| `nodes[]` | object[] | 完整地图图结构 |

#### `map.available_nodes[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `choose_map_node` 的 `option_index` |
| `row` | number | 行坐标 |
| `col` | number | 列坐标 |
| `node_type` | string | 节点类型（`Monster`, `Elite`, `Boss`, `Rest`, `Shop`, `Event`, `Treasure` 等） |
| `state` | string | 节点状态（`Travelable`, `Traveled` 等） |

#### `map.nodes[]`

完整图结构，用于路线规划。`available_nodes` 仅用于执行当前一步。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `row` | number | 行坐标 |
| `col` | number | 列坐标 |
| `node_type` | string | 节点类型 |
| `state` | string | 节点状态 |
| `visited` | boolean | 是否已访问 |
| `is_current` | boolean | 是否为当前节点 |
| `is_available` | boolean | 是否为当前可前往节点 |
| `is_start` | boolean | 是否为起点 |
| `is_boss` | boolean | 是否为 Boss 节点 |
| `is_second_boss` | boolean | 是否为第二 Boss 节点 |
| `parents[]` | object[] | 父节点坐标列表 `[{ row, col }]` |
| `children[]` | object[] | 子节点坐标列表 `[{ row, col }]` |

### `reward` 子结构

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `pending_card_choice` | boolean | 是否正在卡牌奖励选择子界面 |
| `can_proceed` | boolean | 是否可点击继续 |
| `rewards[]` | object[] | 奖励按钮列表（主奖励界面） |
| `card_options[]` | object[] | 卡牌奖励候选（卡牌选择子界面） |
| `alternatives[]` | object[] | 替代按钮（如"跳过"） |

#### `reward.rewards[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `claim_reward` 的 `option_index` |
| `reward_type` | string | 奖励类型（`Gold`, `Card`, `Potion`, `Relic`, `RemoveCard`, `SpecialCard`, `LinkedRewardSet`） |
| `description` | string | 奖励描述文本 |
| `claimable` | boolean | 是否可领取 |

#### `reward.card_options[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `choose_reward_card` 的 `option_index` |
| `card_id` | string | 卡牌 ID |
| `name` | string | 卡牌名称 |
| `upgraded` | boolean | 是否已升级 |

#### `reward.alternatives[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 替代按钮索引 |
| `label` | string | 按钮文字（如"跳过"） |

### `selection` 子结构

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `kind` | string | 选牌类型（`"deck_card_select"`、`"deck_upgrade_select"`、`"deck_transform_select"`、`"deck_enchant_select"`） |
| `prompt` | string | 提示文字（如"选择一张牌移除"） |
| `cards[]` | object[] | 可选卡牌列表 |

#### `selection.cards[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `select_deck_card` 的 `option_index` |
| `card_id` | string | 卡牌 ID |
| `name` | string | 卡牌名称 |
| `upgraded` | boolean | 是否已升级 |
| `card_type` | string | 卡牌类型 |
| `rarity` | string | 稀有度 |
| `costs_x` | boolean | 是否为能量 X 费卡 |
| `star_costs_x` | boolean | 是否为星星 X 费卡 |
| `energy_cost` | number | 能量消耗（含修正） |
| `star_cost` | number | 星星消耗（含修正） |

### `chest` 子结构

当 `screen` 为 `CHEST` 时存在。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `is_opened` | boolean | 宝箱是否已打开 |
| `has_relic_been_claimed` | boolean | 是否已选择遗物 |
| `relic_options[]` | object[] | 可选遗物列表（宝箱打开后才有内容） |

#### `chest.relic_options[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `choose_treasure_relic` 的 `option_index` |
| `relic_id` | string | 遗物内部 ID |
| `name` | string | 遗物名称 |
| `rarity` | string | 稀有度（`Common`, `Uncommon`, `Rare` 等） |

### `event` 子结构

当 `screen` 为 `EVENT` 时存在。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `event_id` | string | 事件内部 ID |
| `title` | string | 事件标题 |
| `description` | string | 事件描述文本 |
| `is_finished` | boolean | 事件是否已完成（完成时仅剩 proceed 选项） |
| `options[]` | object[] | 当前可选选项列表 |

#### `event.options[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `choose_event_option` 的 `option_index` |
| `text_key` | string | 选项文本键（内部标识） |
| `title` | string | 选项标题 |
| `description` | string | 选项描述 |
| `is_locked` | boolean | 选项是否被锁定（锁定选项不可选） |
| `is_proceed` | boolean | 是否为继续/离开选项 |
| `will_kill_player` | boolean | 该选项是否会导致玩家死亡（若模型提供） |
| `has_relic_preview` | boolean | 该选项是否包含遗物预览（若模型提供） |

### `rest` 子结构

当 `screen` 为 `REST` 时存在。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `options[]` | object[] | 可选休息点操作列表 |

#### `rest.options[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `choose_rest_option` 的 `option_index` |
| `option_id` | string | 操作类型（`HEAL`, `SMITH`, `MEND`, `LIFT`, `COOK`, `DIG`, `HATCH`, `CLONE` 等） |
| `title` | string | 操作标题 |
| `description` | string | 操作描述 |
| `is_enabled` | boolean | 操作是否可用（如 `SMITH` 需要有可升级卡牌） |

### `shop` 子结构

当 `screen` 为 `SHOP` 时存在。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `is_open` | boolean | 商店库存面板是否已打开 |
| `can_open` | boolean | 当前是否可以打开库存面板 |
| `can_close` | boolean | 当前是否可以关闭库存面板 |
| `cards[]` | object[] | 可购买卡牌列表 |
| `relics[]` | object[] | 可购买遗物列表 |
| `potions[]` | object[] | 可购买药水列表 |
| `card_removal` | object \| null | 删牌服务状态 |

#### `shop.cards[]` / `shop.relics[]` / `shop.potions[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 对应 `buy_card` / `buy_relic` / `buy_potion` 的 `option_index` |
| `name` | string | 商品名称 |
| `price` | number | 当前价格 |
| `available` | boolean | 当前是否仍可购买 |

#### `shop.cards[]` 附加字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `card_id` | string | 卡牌内部 ID |
| `upgraded` | boolean | 是否已升级 |
| `card_type` | string | 卡牌类型 |
| `rarity` | string | 稀有度 |
| `costs_x` | boolean | 是否为能量 X 费卡 |
| `star_costs_x` | boolean | 是否为星星 X 费卡 |
| `energy_cost` | number | 能量消耗（含修正） |
| `star_cost` | number | 星星消耗（含修正） |

#### `shop.card_removal`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `price` | number | 当前删牌服务价格 |
| `available` | boolean | 当前是否可购买删牌服务 |

### 状态示例：战斗中

```json
{
  "ok": true,
  "request_id": "req_20260310_120000_1234",
  "data": {
    "state_version": 6,
    "run_id": "WXJVZBQFK2",
    "screen": "COMBAT",
    "in_combat": true,
    "turn": 1,
    "available_actions": ["end_turn", "play_card"],
    "combat": {
      "player": {
        "current_hp": 72,
        "max_hp": 80,
        "block": 0,
        "energy": 3,
        "stars": 0
      },
      "hand": [
        {
          "index": 0,
          "card_id": "STRIKE_IRONCLAD",
          "name": "打击",
          "upgraded": false,
          "target_type": "AnyEnemy",
          "requires_target": true,
          "costs_x": false,
          "energy_cost": 1,
          "star_cost": 0,
          "playable": true,
          "unplayable_reason": null
        },
        {
          "index": 1,
          "card_id": "DEFEND_IRONCLAD",
          "name": "防御",
          "upgraded": false,
          "target_type": "None",
          "requires_target": false,
          "costs_x": false,
          "energy_cost": 1,
          "star_cost": 0,
          "playable": true,
          "unplayable_reason": null
        }
      ],
      "enemies": [
        {
          "index": 0,
          "enemy_id": "CULTIST",
          "name": "邪教徒",
          "current_hp": 50,
          "max_hp": 50,
          "block": 0,
          "is_alive": true,
          "is_hittable": true,
          "intent": "PECK_MOVE",
          "move_id": "PECK_MOVE",
          "intents": [
            {
              "index": 0,
              "intent_type": "Attack",
              "label": "7",
              "damage": 7,
              "hits": 1,
              "total_damage": 7,
              "status_card_count": null
            }
          ]
        }
      ]
    },
    "run": {
      "current_hp": 72,
      "max_hp": 80,
      "gold": 99,
      "max_energy": 3,
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
      ],
      "potions": [
        {
          "index": 0,
          "potion_id": "FIRE_POTION",
          "name": "火焰药水",
          "occupied": true
        },
        {
          "index": 1,
          "potion_id": null,
          "name": null,
          "occupied": false
        }
      ]
    },
    "map": null,
    "selection": null,
    "character_select": null,
    "event": null,
    "shop": null,
    "rest": null,
    "reward": null,
    "modal": null,
    "game_over": null
  }
}
```

### 状态示例：地图界面

```json
{
  "ok": true,
  "request_id": "req_20260310_120001_5678",
  "data": {
    "screen": "MAP",
    "available_actions": ["choose_map_node"],
    "map": {
      "current_node": { "row": 1, "col": 3 },
      "starting_node": { "row": 0, "col": 3 },
      "boss_node": { "row": 14, "col": 3 },
      "second_boss_node": null,
      "rows": 15,
      "cols": 7,
      "is_travel_enabled": true,
      "is_traveling": false,
      "map_generation_count": 1,
      "available_nodes": [
        {
          "index": 0,
          "row": 2,
          "col": 2,
          "node_type": "Monster",
          "state": "Travelable"
        },
        {
          "index": 1,
          "row": 2,
          "col": 4,
          "node_type": "Event",
          "state": "Travelable"
        }
      ],
      "nodes": [
        {
          "row": 1,
          "col": 3,
          "node_type": "Monster",
          "state": "Traveled",
          "visited": true,
          "is_current": true,
          "is_available": false,
          "is_start": false,
          "is_boss": false,
          "is_second_boss": false,
          "parents": [{ "row": 0, "col": 3 }],
          "children": [{ "row": 2, "col": 2 }, { "row": 2, "col": 4 }]
        }
      ]
    }
  }
}
```

### 状态示例：奖励主界面

```json
{
  "ok": true,
  "request_id": "req_20260310_120002_9012",
  "data": {
    "screen": "REWARD",
    "available_actions": ["claim_reward", "collect_rewards_and_proceed"],
    "reward": {
      "pending_card_choice": false,
      "can_proceed": true,
      "rewards": [
        {
          "index": 0,
          "reward_type": "Gold",
          "description": "获得 25 金币",
          "claimable": true
        },
        {
          "index": 1,
          "reward_type": "Card",
          "description": "选择一张卡牌",
          "claimable": true
        },
        {
          "index": 2,
          "reward_type": "Potion",
          "description": "获得火焰药水",
          "claimable": true
        }
      ],
      "card_options": [],
      "alternatives": []
    }
  }
}
```

### 状态示例：卡牌奖励选择

```json
{
  "ok": true,
  "request_id": "req_20260310_120003_3456",
  "data": {
    "screen": "REWARD",
    "available_actions": ["choose_reward_card", "skip_reward_cards"],
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
        },
        {
          "index": 1,
          "card_id": "SHRUG_IT_OFF",
          "name": "耸肩",
          "upgraded": false
        },
        {
          "index": 2,
          "card_id": "CARNAGE",
          "name": "大屠杀",
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

### 状态示例：删牌界面

```json
{
  "ok": true,
  "request_id": "req_20260310_120004_7890",
  "data": {
    "screen": "CARD_SELECTION",
    "available_actions": ["select_deck_card"],
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
        },
        {
          "index": 1,
          "card_id": "DEFEND_IRONCLAD",
          "name": "防御",
          "upgraded": false,
          "card_type": "Skill",
          "rarity": "Starter"
        }
      ]
    }
  }
}
```

---

## `GET /actions/available`

返回当前状态下允许执行的动作及其参数需求。

### 响应字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `screen` | string | 当前界面 |
| `actions[]` | object[] | 动作描述列表 |

#### `actions[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 动作名称 |
| `requires_target` | boolean | 是否需要 `target_index` |
| `requires_index` | boolean | 是否需要 `card_index` 或 `option_index` |

### 响应示例

```json
{
  "ok": true,
  "request_id": "req_20260310_120005_1111",
  "data": {
    "screen": "COMBAT",
    "actions": [
      {
        "name": "end_turn",
        "requires_target": false,
        "requires_index": false
      },
      {
        "name": "play_card",
        "requires_target": false,
        "requires_index": true
      }
    ]
  }
}
```

> **注意**：`play_card.requires_target` 固定为 `false`。是否需要目标取决于具体卡牌的 `combat.hand[].requires_target` 字段。

---

## `POST /action`

执行单个游戏动作。

### 请求体

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `action` | string | **必填**。动作名称 |
| `card_index` | number \| null | 手牌索引（`play_card` 时使用） |
| `target_index` | number \| null | 目标索引（需要指定目标的卡牌使用） |
| `option_index` | number \| null | 选项索引（地图/奖励/选牌等使用） |
| `client_context` | object \| null | 可选的客户端上下文（如调用来源标识） |

### 通用响应结构（ActionResponsePayload）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `action` | string | 执行的动作名 |
| `status` | string | `"completed"` 或 `"pending"` |
| `stable` | boolean | 状态是否已稳定 |
| `message` | string | 人类可读的结果描述 |
| `state` | object | 执行后的最新游戏状态快照（同 `GET /state` 的 `data`） |

---

## 已实现动作详细说明

### `end_turn`

结束当前战斗回合。

- **前提**：`screen = "COMBAT"`，处于玩家出牌阶段
- **参数**：无
- **稳定条件**：回合数变化 或 不再是玩家阶段 或 战斗已结束
- **超时**：5 秒

```
请求: { "action": "end_turn" }
```

```json
{
  "ok": true,
  "request_id": "req_20260310_120010_0001",
  "data": {
    "action": "end_turn",
    "status": "completed",
    "stable": true,
    "message": "Action completed.",
    "state": { "screen": "COMBAT", "turn": 2, "..." : "..." }
  }
}
```

### `play_card`

打出当前手牌中的一张牌。

- **前提**：`screen = "COMBAT"`，手牌中有 `playable = true` 的卡
- **参数**：
  - `card_index`（必填）：`combat.hand[]` 的索引
  - `target_index`（条件必填）：当卡牌 `requires_target = true` 时，为 `combat.enemies[]` 的索引
- **稳定条件**：卡牌离开手牌 且 玩家驱动的动作队列清空
- **超时**：5 秒

```json
{
  "action": "play_card",
  "card_index": 0,
  "target_index": 0
}
```

**错误场景**：

| 场景 | 错误码 | 说明 |
| --- | --- | --- |
| 缺少 `card_index` | `invalid_request` | "play_card requires card_index." |
| `card_index` 越界 | `invalid_target` | "card_index is out of range." |
| 卡牌需要目标但未传 `target_index` | `invalid_target` | "This card requires target_index." |
| `target_index` 越界 | `invalid_target` | "target_index is out of range." |
| 卡牌不可打出 | `invalid_action` | "Card cannot be played in the current state." |

### `choose_map_node`

在地图界面选择一个节点前往。

- **前提**：`screen = "MAP"`，`map.available_nodes` 非空
- **参数**：`option_index`（必填）：`map.available_nodes[]` 的索引
- **稳定条件**：房间已进入 或 地图坐标发生变化 或 界面切换
- **超时**：10 秒

路线规划应基于 `map.nodes[]` 的全图父子连线；`map.available_nodes[]` 只用于执行当前一步。

```json
{
  "action": "choose_map_node",
  "option_index": 0
}
```

### `claim_reward`

> Note (`2026-03-11`): when the claimed reward is a card reward, `skip_reward_cards` only closes the current card-selection overlay. The underlying reward may still remain in `reward.rewards[]`, so callers should always re-read state after skipping.

在奖励主界面领取一个奖励。

- **前提**：`screen = "REWARD"`，`reward.rewards[]` 中有 `claimable = true` 的项
- **参数**：`option_index`（必填）：`reward.rewards[]` 中可领取项的索引
- **行为**：点击奖励按钮。如果是卡牌奖励，界面会切换到卡牌选择子界面（`pending_card_choice = true`），此时应接着调用 `choose_reward_card` 或 `skip_reward_cards`
- **稳定条件**：奖励按钮数量变化 或 界面切换
- **超时**：10 秒

```json
{
  "action": "claim_reward",
  "option_index": 1
}
```

### `choose_reward_card`

在卡牌奖励子界面选择一张卡加入牌库。

- **前提**：`screen = "REWARD"`，`reward.pending_card_choice = true`，`reward.card_options[]` 非空
- **参数**：`option_index`（必填）：`reward.card_options[]` 的索引
- **稳定条件**：离开卡牌选择子界面 或 卡牌数量变化
- **超时**：10 秒

```json
{
  "action": "choose_reward_card",
  "option_index": 0
}
```

### `skip_reward_cards`

> Note (`2026-03-11`): this action dismisses the current card-reward selection overlay. It does not guarantee that the underlying reward is consumed. After calling it, inspect `reward.rewards[]` and `reward.can_proceed` again.

在卡牌奖励子界面跳过拿牌。

- **前提**：`screen = "REWARD"`，`reward.pending_card_choice = true`，`reward.alternatives[]` 非空
- **参数**：无
- **行为**：点击第一个替代按钮（通常是"跳过"）
- **超时**：10 秒

```
请求: { "action": "skip_reward_cards" }
```

### `collect_rewards_and_proceed`

自动收取全部奖励并点击继续。

- **前提**：`screen = "REWARD"`
- **参数**：无
- **行为**：
  1. 逐个领取可领取的奖励（跳过无空位的药水）
  2. 遇到卡牌选择时**自动选择第一张**
  3. 点击继续按钮
- **超时**：20 秒
- **注意**：适合无人值守推进。如需精确控制构筑决策，请用 `claim_reward` + `choose_reward_card` / `skip_reward_cards` 组合

```
请求: { "action": "collect_rewards_and_proceed" }
```

### `select_deck_card`

在牌库选牌界面选择一张牌。

- **前提**：`screen = "CARD_SELECTION"`，`selection.cards[]` 非空
- **参数**：`option_index`（必填）：`selection.cards[]` 的索引
- **行为**：选择牌并自动确认。当前已验证**删牌**、**升级牌**场景；变化牌、附魔牌等单选牌库选择也复用此接口
- **稳定条件**：离开选牌界面
- **超时**：10 秒

```json
{
  "action": "select_deck_card",
  "option_index": 0
}
```

### `open_chest`

打开宝箱房中的宝箱，触发开箱动画并展示可选遗物。

- **前提**：`screen` = `CHEST`，宝箱尚未打开（`chest.is_opened` = false）
- **参数**：无
- **稳定条件**：宝箱房内的遗物选择界面已展开，`chest.relic_options[]` 可读
- **超时**：10 秒

```
请求: { "action": "open_chest" }
```

### `choose_treasure_relic`

从打开的宝箱中选择一个遗物。

- **前提**：`screen` = `CHEST`，宝箱已打开，`chest.relic_options` 非空
- **参数**：

```json
{
  "action": "choose_treasure_relic",
  "option_index": 0
}
```

| 字段 | 必须 | 说明 |
| --- | --- | --- |
| `option_index` | 是 | `chest.relic_options[]` 的索引 |

- **稳定条件**：遗物被授予，界面回到宝箱房主界面
- **超时**：10 秒

### `choose_event_option`

选择事件房中的一个选项。

- **前提**：`screen` = `EVENT`，`event.options` 非空
- **参数**：

| 字段 | 必须 | 说明 |
| --- | --- | --- |
| `option_index` | 是 | `event.options[]` 的索引 |

- 选择普通选项时，事件可能进入下一阶段（新选项出现）、结束（`is_finished`=true）、或触发战斗
- 选择 `is_proceed`=true 的选项时，返回地图
- 事件完成后（`is_finished`=true），仅 `option_index`=0（proceed）有效
- **稳定条件**：事件选项变化 / 事件完成 / 界面切换
- **超时**：10 秒

```
请求: { "action": "choose_event_option", "option_index": 0 }
```

### `choose_rest_option`

选择休息点的一个操作。

- **前提**：`screen` = `REST`，`rest.options` 中存在 `is_enabled`=true 的选项
- **参数**：

| 字段 | 必须 | 说明 |
| --- | --- | --- |
| `option_index` | 是 | `rest.options[]` 的索引 |

- `HEAL`：恢复约 30% HP，完成后 ProceedButton 出现，调用 `proceed` 离开
- `SMITH`：界面切换到 `CARD_SELECTION`，使用 `select_deck_card` 选牌升级
- 其他选项：行为因圣物/游戏状态而异
- **稳定条件**：界面切换（如卡牌选择）或 ProceedButton 出现
- **超时**：10 秒

```
请求: { "action": "choose_rest_option", "option_index": 0 }
```

### `open_shop_inventory`

打开商店库存面板。

- **前提**：`screen` = `SHOP`，`shop.is_open` = false，`shop.can_open` = true
- **参数**：无
- **稳定条件**：库存面板打开，`shop.is_open` 变为 true
- **超时**：10 秒

```
请求: { "action": "open_shop_inventory" }
```

### `close_shop_inventory`

关闭商店库存面板。

- **前提**：`screen` = `SHOP`，`shop.is_open` = true，`shop.can_close` = true
- **参数**：无
- **稳定条件**：库存面板关闭，`shop.is_open` 变为 false
- **超时**：10 秒

```
请求: { "action": "close_shop_inventory" }
```

### `buy_card`

购买商店中的一张卡牌。

- **前提**：`screen` = `SHOP`，`shop.is_open` = true，`shop.cards[]` 中存在 `available`=true 的条目
- **参数**：`option_index`（必填）：`shop.cards[]` 的索引
- **稳定条件**：金币变化、商品消失/失效，或界面切换
- **超时**：10 秒

```
请求: { "action": "buy_card", "option_index": 0 }
```

### `buy_relic`

购买商店中的一个遗物。

- **前提**：`screen` = `SHOP`，`shop.is_open` = true，`shop.relics[]` 中存在 `available`=true 的条目
- **参数**：`option_index`（必填）：`shop.relics[]` 的索引
- **稳定条件**：金币变化、商品消失/失效，或界面切换
- **超时**：10 秒

```
请求: { "action": "buy_relic", "option_index": 0 }
```

### `buy_potion`

购买商店中的一瓶药水。

- **前提**：`screen` = `SHOP`，`shop.is_open` = true，`shop.potions[]` 中存在 `available`=true 的条目
- **参数**：`option_index`（必填）：`shop.potions[]` 的索引
- **稳定条件**：金币变化、商品消失/失效，或界面切换
- **超时**：10 秒

```
请求: { "action": "buy_potion", "option_index": 0 }
```

### `remove_card_at_shop`

购买商店删牌服务，进入牌库选牌界面。

- **前提**：`screen` = `SHOP`，`shop.is_open` = true，`shop.card_removal.available` = true
- **参数**：无
- **行为**：动作本身采用 fire-and-forget，避免 HTTP 调用阻塞在后续选牌流程
- **稳定条件**：界面切换到 `CARD_SELECTION`，或库存状态发生变化
- **超时**：10 秒

```
请求: { "action": "remove_card_at_shop" }
```

### `proceed`

> Note (`2026-03-11`): `proceed` can also appear on the main `REWARD` screen when the game's own proceed button is enabled. It is still not applicable while `reward.pending_card_choice = true`.

点击当前界面的"继续"按钮。

- **前提**：界面存在可用的 `ProceedButton`（宝箱房、休息点结束后等）
- **参数**：无
- **不适用于**：奖励界面（应使用 `collect_rewards_and_proceed` 或手动流程）
- **稳定条件**：界面切换 或 按钮消失/禁用
- **超时**：10 秒

```
请求: { "action": "proceed" }
```

---

## 典型调用流程

### 战斗回合

```
1. GET /state                          → 获取手牌、敌人、能量
2. 选择可打出的卡牌（playable=true）
3. POST /action { play_card, card_index, target_index? }  → 出牌
4. 重复 1-3 直到没有可打出的卡或决定结束
5. POST /action { end_turn }           → 结束回合
6. 重复 1-5 直到战斗结束
```

### 战斗结算 → 地图推进

```
1. GET /state                          → screen=REWARD
2a. POST /action { collect_rewards_and_proceed }  → 自动收取（简单模式）
--- 或 ---
2b. POST /action { claim_reward, option_index=0 }  → 手动领取金币
    POST /action { claim_reward, option_index=1 }  → 点击卡牌奖励
    GET /state                                     → 确认 pending_card_choice=true
    POST /action { choose_reward_card, option_index=2 }  → 选卡
    POST /action { proceed }                       → 继续（如果有按钮）
3. GET /state                          → screen=MAP
4. POST /action { choose_map_node, option_index=0 }  → 选路
```

### 宝箱房

```
1. GET /state                          → screen=CHEST, chest.is_opened=false
2. POST /action { open_chest }         → 打开宝箱，等待遗物展示
3. GET /state                          → chest.relic_options[] 列出可选遗物
4. POST /action { choose_treasure_relic, option_index=0 }  → 选择遗物
5. GET /state                          → chest.has_relic_been_claimed=true
6. POST /action { proceed }            → 继续到地图
```

### 事件房

```
1. GET /state                          → screen=EVENT, event.options[] 列出可选选项
2. 选择 is_locked=false 的选项
3. POST /action { choose_event_option, option_index=0 }  → 选择选项
4. GET /state                          → 事件可能更新选项或完成
5. 若 event.is_finished=true，选项仅剩 proceed（index=0）
6. POST /action { choose_event_option, option_index=0 }  → 离开事件，返回地图
```

### 休息点（恢复 HP）

```
1. GET /state                          → screen=REST, rest.options[] 列出可选操作
2. 选择 is_enabled=true 的 HEAL 选项
3. POST /action { choose_rest_option, option_index=0 }  → 恢复 HP
4. GET /state                          → ProceedButton 可用
5. POST /action { proceed }            → 继续到地图
```

### 休息点（升级牌）

```
1. GET /state                          → screen=REST, rest.options[] 列出可选操作
2. 选择 SMITH 选项（is_enabled=true 表示有可升级卡牌）
3. POST /action { choose_rest_option, option_index=N }  → 进入卡牌选择
4. GET /state                          → screen=CARD_SELECTION, selection.cards[]
5. POST /action { select_deck_card, option_index=M }    → 选择要升级的卡牌
6. GET /state                          → 回到 REST，ProceedButton 可用
7. POST /action { proceed }            → 继续到地图
```

### 商店（购买商品）

```
1. GET /state                          → screen=SHOP, shop.is_open=false
2. POST /action { open_shop_inventory } → 打开库存面板
3. GET /state                          → shop.cards[] / shop.relics[] / shop.potions[]
4. POST /action { buy_card, option_index=0 } 或 buy_relic / buy_potion
5. GET /state                          → 金币和库存更新
6. POST /action { close_shop_inventory } → 关闭库存
7. POST /action { proceed }            → 离开商店，返回地图
```

### 商店（删牌）

```
1. GET /state                          → screen=SHOP, shop.card_removal.available=true
2. POST /action { open_shop_inventory } → 打开库存面板
3. POST /action { remove_card_at_shop } → 进入 CARD_SELECTION
4. GET /state                          → screen=CARD_SELECTION, selection.cards[]
5. POST /action { select_deck_card, option_index=M } → 选择要移除的卡牌
6. GET /state                          → 返回 SHOP 或可继续离开
7. POST /action { proceed }            → 返回地图
```

---

## 补充说明

截至 `2026-03-11`，此前列为后续计划的以下能力都已经落地到代码与 MCP：

| 功能 | 对应字段 / 动作 | 当前状态 |
| --- | --- | --- |
| 主菜单续局 / 放弃 | `continue_run` / `abandon_run` | 已实现，待实机验证 |
| 主菜单开局入口 | `open_character_select` | 已实现，待实机验证 |
| 主菜单时间线入口 | `open_timeline` / `close_main_menu_submenu` | 已实现，待实机验证 |
| 时间线交互 | `timeline` / `choose_timeline_epoch` / `confirm_timeline_overlay` | 已实现，待实机验证 |
| 角色选择 | `character_select` / `select_character` / `embark` | 已实现，待实机验证 |
| 药水系统 | `run.potions[*].can_use` / `use_potion` / `discard_potion` | 已实现，待实机验证 |
| 阻塞弹窗 | `modal` / `confirm_modal` / `dismiss_modal` | 已实现，待实机验证 |
| 游戏结束 | `game_over` / `return_to_main_menu` | 已实现，待实机验证 |

新增字段与动作的详细说明见 [phase-5-full-chain.md](../docs/phase-5-full-chain.md)。

### Debug 动作

`run_console_command` 仅用于开发期调试 / 验证，默认关闭。

- 启用方式：设置环境变量 `STS2_ENABLE_DEBUG_ACTIONS=1`
- 发布建议：不要在正式发布默认配置中启用
- 设计目标：用于本地实机验证提速，不应成为正式游玩 agent 的常规依赖
