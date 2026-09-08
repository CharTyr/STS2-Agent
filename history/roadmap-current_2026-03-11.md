# STS2 AI Agent 当前开发路线图

> 历史路线图，2026-09-09 归档。标题中的“当前”、任务清单和验收状态仅属于 2026-03-11；当前状态唯一入口为 [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md)。

更新时间：`2026-03-11`

---

## 总体进度

| 阶段 | 描述 | 状态 |
| --- | --- | --- |
| Phase 0A | 环境搭建 | 已完成 |
| Phase 0B | 反编译与逆向勘察 | 已完成 |
| Phase 1A | 协议冻结 | 已完成 |
| Phase 1B | Mod 骨架 + `/health` | 已完成 |
| Phase 1C | 最小纵切 | 已完成 |
| Phase 2 | 战斗状态提取 | 已完成 |
| Phase 3 | 战斗动作执行 | 已完成 |
| Phase 4A | 地图 / 奖励 / 宝箱 | 代码已完成，待统一实机验证 |
| Phase 4B | 事件 / 休息点 | 代码已完成，待统一实机验证 |
| Phase 4C | 商店 | 代码已完成，待统一实机验证 |
| Phase 5 | 全链路补齐（角色选择 / 药水 / Modal / Game Over） | 代码已完成，待统一实机验证 |
| Phase 6 | 集成与回归 | 未开始 |

---

## 当前能力盘点

### HTTP API

| 端点 | 状态 |
| --- | --- |
| `GET /health` | 已验证 |
| `GET /state` | 已验证 |
| `GET /actions/available` | 已验证 |
| `POST /action` | 已验证 |

### 已实现动作

| 动作 | 状态 |
| --- | --- |
| `end_turn` | 已验证 |
| `play_card` | 已验证 |
| `choose_map_node` | 已验证 |
| `claim_reward` | 已验证 |
| `choose_reward_card` | 已验证 |
| `collect_rewards_and_proceed` | 已验证 |
| `proceed` | 已验证 |
| `continue_run` | 代码已完成，待实机验证 |
| `abandon_run` | 代码已完成，待实机验证 |
| `open_character_select` | 代码已完成，待实机验证 |
| `open_timeline` | 代码已完成，待实机验证 |
| `close_main_menu_submenu` | 代码已完成，待实机验证 |
| `choose_timeline_epoch` | 代码已完成，待实机验证 |
| `confirm_timeline_overlay` | 代码已完成，待实机验证 |
| `skip_reward_cards` | 代码已完成，待实机验证 |
| `select_deck_card` | 代码已完成，待实机验证 |
| `open_chest` | 代码已完成，待实机验证 |
| `choose_treasure_relic` | 代码已完成，待实机验证 |
| `choose_event_option` | 代码已完成，待实机验证 |
| `choose_rest_option` | 代码已完成，待实机验证 |
| `open_shop_inventory` | 代码已完成，待实机验证 |
| `close_shop_inventory` | 代码已完成，待实机验证 |
| `buy_card` | 代码已完成，待实机验证 |
| `buy_relic` | 代码已完成，待实机验证 |
| `buy_potion` | 代码已完成，待实机验证 |
| `remove_card_at_shop` | 代码已完成，待实机验证 |
| `select_character` | 代码已完成，待实机验证 |
| `embark` | 代码已完成，待实机验证 |
| `use_potion` | 代码已完成，待实机验证 |
| `discard_potion` | 代码已完成，待实机验证 |
| `confirm_modal` | 代码已完成，待实机验证 |
| `dismiss_modal` | 代码已完成，待实机验证 |
| `return_to_main_menu` | 代码已完成，待实机验证 |

### 已实现状态字段

| 字段 | 状态 |
| --- | --- |
| `combat.player` / `combat.hand` / `combat.enemies` | 已实现并验证 |
| `run.deck` / `run.relics` / `run.potions` / `run.gold` | 已实现，待统一实机验证 |
| `map.current_node` / `map.available_nodes` | 已验证 |
| `map.rows` / `map.cols` / `map.starting_node` / `map.boss_node` / `map.second_boss_node` / `map.nodes` | 已实现，待统一实机验证 |
| `reward.*` / `selection.*` | 已实现，待统一实机验证 |
| `chest.*` | 已实现，待统一实机验证 |
| `event.*` | 已实现，待统一实机验证 |
| `rest.*` | 已实现，待统一实机验证 |
| `shop.*` | 已实现，待统一实机验证 |
| `character_select.*` | 已实现，待统一实机验证 |
| `modal.*` | 已实现，待统一实机验证 |
| `game_over.*` | 已实现，待统一实机验证 |

### MCP 工具

当前 MCP 已注册并可用的工具：

- `health_check`
- `get_game_state`
- `get_available_actions`
- `end_turn`
- `play_card`
- `continue_run`
- `abandon_run`
- `open_character_select`
- `open_timeline`
- `close_main_menu_submenu`
- `choose_timeline_epoch`
- `confirm_timeline_overlay`
- `choose_map_node`
- `claim_reward`
- `choose_reward_card`
- `skip_reward_cards`
- `select_deck_card`
- `collect_rewards_and_proceed`
- `proceed`
- `open_chest`
- `choose_treasure_relic`
- `choose_event_option`
- `choose_rest_option`
- `open_shop_inventory`
- `close_shop_inventory`
- `buy_card`
- `buy_relic`
- `buy_potion`
- `remove_card_at_shop`
- `select_character`
- `embark`
- `use_potion`
- `discard_potion`
- `confirm_modal`
- `dismiss_modal`
- `return_to_main_menu`

---

## 任务清单

### T-1: Phase 4A 实机回归

- [ ] 验证 `map.nodes` 全图结构
- [ ] 验证 `skip_reward_cards`
- [ ] 验证 `select_deck_card`
- [ ] 验证宝箱 relic 选择后再 `proceed`

### T-2: Phase 4B 事件系统

- [x] `EventPayload`
- [x] `choose_event_option`
- [x] MCP / 文档同步
- [ ] 验证事件分支切换
- [ ] 验证事件完成后 proceed 选项
- [ ] 验证事件嵌套战斗链路

### T-3: Phase 4B 休息点系统

- [x] `RestPayload`
- [x] `choose_rest_option`
- [x] `select_deck_card` 覆盖升级牌选择
- [ ] 验证普通休息链路
- [ ] 验证 SMITH 进入选牌并返回

### T-4: Phase 4C 商店系统

- [x] `ShopPayload`
- [x] `open_shop_inventory` / `close_shop_inventory`
- [x] `buy_card` / `buy_relic` / `buy_potion`
- [x] `remove_card_at_shop`
- [ ] 验证库存开关
- [ ] 验证买卡 / 买 relic / 买药水
- [ ] 验证删牌进入选牌后返回商店或地图

### T-5: Phase 5 全链路补齐

- [x] `continue_run`
- [x] `abandon_run`
- [x] `open_character_select`
- [x] `open_timeline`
- [x] `close_main_menu_submenu`
- [x] `choose_timeline_epoch`
- [x] `confirm_timeline_overlay`
- [x] `CharacterSelectPayload`
- [x] `ModalPayload`
- [x] `GameOverPayload`
- [x] `select_character`
- [x] `embark`
- [x] `use_potion`
- [x] `discard_potion`
- [x] `confirm_modal`
- [x] `dismiss_modal`
- [x] `return_to_main_menu`
- [ ] 验证角色选择到开局
- [ ] 验证 FTUE / Modal 拦截与放行
- [ ] 验证战斗内药水使用 / 丢弃
- [ ] 验证 Game Over 返回主菜单

### T-6: Phase 6 集成与回归

- [ ] 统一跑一遍完整 run 的关键链路
- [ ] 记录失败场景与日志
- [ ] 收敛 API / MCP / 文档剩余漂移

---

## 推荐执行顺序

1. 先做统一实机验证，不再继续加新功能。
2. 验证顺序按阻塞性排：角色选择 / Modal / 地图 / 事件 / 休息点 / 商店 / 奖励 / 宝箱 / Game Over。
3. 战斗内把 `use_potion` / `discard_potion` 插入回归脚本。
4. 发现问题后只修阻塞完整 run 的缺陷，避免顺手重构。

---

## 当前风险

1. Windows 下无法热替换已加载的 Mod DLL，所有实机验证都依赖“关游戏 -> 安装 -> 重开”。
2. `GameStateService.cs` 仍是高冲突文件，后续修 bug 时要避免顺手扩大改动面。
3. 事件、休息点、商店、Modal 都依赖 UI 树结构，游戏更新后最容易失效。
4. `map.nodes` 只提供结构和运行时状态，不负责路线评分；路径规划仍应留在上层 agent。
5. 当前代码已完成全链路覆盖，但 Phase 6 之前都不能算“实机闭环已验收”。
