# STS2 2代覆盖缺口清单

更新时间：`2026-03-10`

本文档只基于 **Slay the Spire 2** 的 `sts2.dll` 反编译结果整理，**不是 1 代**。

用途：

- 给 Claude 对照当前项目实现，补齐 2 代主流程覆盖
- 明确哪些场景虽然不在当前主线里，但会实际卡住自动流程
- 为后续 Phase 4B / 4C / 5 / 6 提供开发边界

---

## 当前已覆盖到的主流程

当前项目已经覆盖或部分覆盖：

- 主菜单识别
- 地图当前节点与下一步可达节点
- 战斗状态读取
- 战斗动作：
  - `end_turn`
  - `play_card`
- 奖励结算：
  - `claim_reward`
  - `choose_reward_card`
  - `skip_reward_cards`
  - `collect_rewards_and_proceed`
- 牌库选牌：
  - `select_deck_card`
- 运行态信息：
  - `run.deck`
  - `run.relics`
  - `run.potions`
  - `run.gold`
- 地图全图结构导出：
  - `map.nodes`

但这些还不足以覆盖 **STS2 2代完整一局流程**。

---

## 仍未完整覆盖的关键系统

### 1. 宝箱房 / relic 选择

当前状态：

- `screen = "CHEST"` 已有识别
- 但还没有真正覆盖宝箱打开后的 relic 选择子流程

反编译入口：

- [NTreasureRoom.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Rooms/NTreasureRoom.cs)
- [NTreasureRoomRelicCollection.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic/NTreasureRoomRelicCollection.cs)
- [TreasureRoomRelicSynchronizer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Multiplayer.Game/TreasureRoomRelicSynchronizer.cs)

关键事实：

- 开箱后会进入 `NTreasureRoomRelicCollection`
- 需要等待 `RelicPickingFinished()`
- relic 选择通过 `RunManager.Instance.TreasureRoomRelicSynchronizer.PickRelicLocally(holder.Index)` 驱动
- 存在空箱分支：`CompleteWithNoRelics()`

建议补齐：

- `chest` payload
- `chest.relic_options`
- `choose_treasure_relic`
- 空箱处理

风险点：

- 只做 `proceed` 不够，会卡在 relic 选择界面
- 如果只假设“必有 relic”，会在空箱或特殊场景挂死

---

### 2. 事件系统

当前状态：

- `screen = "EVENT"` 已识别
- `event` 仍为 `null`

反编译入口：

- [NEventRoom.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Rooms/NEventRoom.cs)
- [EventOption.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Events/EventOption.cs)
- [NEventOptionButton.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Events/NEventOptionButton.cs)
- [EventSynchronizer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Multiplayer.Game/EventSynchronizer.cs)

关键事实：

- 事件选项不仅有文字，还带这些语义：
  - `IsLocked`
  - `IsProceed`
  - `WillKillPlayer`
  - `Relic`
  - `HoverTips`
- 事件完成后会自动变成 Proceed 按钮
- 有 `EmbeddedCombatRoom`

建议补齐：

- `event.event_id`
- `event.title`
- `event.description`
- `event.options[]`
- `choose_event_option`

推荐 `event.options[]` 至少包含：

- `index`
- `title`
- `description`
- `locked`
- `is_proceed`
- `will_kill_player`
- `has_relic_preview`

风险点：

- 事件不是简单按钮列表
- 某些选项进入嵌套战斗，回来后还要继续事件或结算

---

### 3. 事件嵌套战斗

当前状态：

- 当前项目只覆盖普通战斗房逻辑
- 没有单独验证事件中的嵌套战斗切换

反编译入口：

- [NEventRoom.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Rooms/NEventRoom.cs)
- [NRun.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes/NRun.cs)
- [ActiveScreenContext.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext/ActiveScreenContext.cs)

关键事实：

- `NEventRoom` 提供 `EmbeddedCombatRoom`
- 当前 active screen 可能直接变成事件里的战斗房
- 这意味着 `screen = "COMBAT"` 不一定来自普通战斗房

建议补齐：

- 实机验证：
  - `EVENT -> COMBAT -> REWARD/MAP`
  - `EVENT -> COMBAT -> EVENT`
- 状态机上避免把事件中的战斗误判成普通房间流程结束

风险点：

- 如果只按普通战斗处理，事件链可能中途断掉

---

### 4. 休息点系统

当前状态：

- `screen = "REST"` 已识别
- `rest` 仍为 `null`

反编译入口：

- [NRestSiteRoom.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Rooms/NRestSiteRoom.cs)
- [RestSiteOption.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Entities.RestSite/RestSiteOption.cs)
- [NRestSiteButton.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.RestSite/NRestSiteButton.cs)
- [RestSiteSynchronizer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Multiplayer.Game/RestSiteSynchronizer.cs)

关键事实：

- 休息点不是固定两个选项
- 默认就至少有：
  - `Heal`
  - `Smith`
  - 多人时可能还有 `Mend`
- 目录里还存在：
  - `Cook`
  - `Dig`
  - `Hatch`
  - `Lift`
  - `Clone`

建议补齐：

- `rest.options[]`
- 通用动作：
  - `choose_rest_option`
  - 或 `rest_site_action`

推荐 `rest.options[]` 至少包含：

- `index`
- `option_id`
- `title`
- `description`
- `enabled`

风险点：

- 不能只硬编码“休息 / 锻造”
- 2 代特有 rest option 很多，必须按通用 option 模型做

---

### 5. 休息点锻造子流程

当前状态：

- `select_deck_card` 已存在
- 但还没有确认它和休息点升级牌子流程真正打通

反编译入口：

- [SmithRestSiteOption.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Entities.RestSite/SmithRestSiteOption.cs)

关键事实：

- `Smith` 不是房间内立即完成
- 它走的是 `CardSelectCmd.FromDeckForUpgrade(...)`
- 需要手动确认，且可取消

建议补齐：

- 实机验证：
  - `REST -> choose smith -> CARD_SELECTION -> select_deck_card -> confirm -> 回到 REST / proceed`
- 如当前 `select_deck_card` 只适用于删牌，需要收紧或扩展其语义

风险点：

- 如果只做 rest button 点击，不处理升级选牌，流程会卡住

---

### 6. 商店系统

当前状态：

- `screen = "SHOP"` 已识别
- `shop` 仍为 `null`

反编译入口：

- [NMerchantRoom.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Rooms/NMerchantRoom.cs)
- [NMerchantInventory.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.Shops/NMerchantInventory.cs)
- [MerchantInventory.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Entities.Merchant/MerchantInventory.cs)
- [NMerchantSlot.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.Shops/NMerchantSlot.cs)
- [NMerchantCardRemoval.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.Shops/NMerchantCardRemoval.cs)

关键事实：

- 商店是双层流程：
  - 外层房间 `NMerchantRoom`
  - 内层库存 `NMerchantInventory`
- 库存里分为：
  - 角色卡
  - 无色卡
  - relic
  - potion
  - 删牌服务
- `NMerchantInventory.IsOpen` 是关键状态

建议补齐：

- `shop` payload
- `shop.is_open`
- `shop.cards`
- `shop.relics`
- `shop.potions`
- `shop.card_removal`
- 动作：
  - `open_shop_inventory`
  - `close_shop_inventory`
  - `buy_card`
  - `buy_relic`
  - `buy_potion`
  - `remove_card_at_shop`

风险点：

- 如果只导商品列表，不导库存开关状态，AI 很难知道当前能不能买
- 删牌服务不是普通商品 slot，需单独处理

---

### 7. 药水系统

当前状态：

- 只在 `run.potions` 里读到了药水槽
- 没有动作支持

反编译入口：

- [NPotionHolder.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Potions/NPotionHolder.cs)
- [NPotionPopup.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Potions/NPotionPopup.cs)

关键事实：

- 药水至少支持：
  - 使用
  - 丢弃
- 还区分：
  - `CombatOnly`
  - `AnyTime`
  - `Automatic`
- 受这些状态影响：
  - 是否在战斗中
  - 是否轮到玩家
  - `PlayerActionsDisabled`
  - 是否处于卡牌选择界面
  - 是否需要目标

建议补齐：

- `use_potion`
- `discard_potion`
- 必要时在状态里补：
  - `usable`
  - `discardable`
  - `requires_target`

风险点：

- 不支持药水的话，严格说战斗能力仍然不完整
- 目标型药水和任意时机药水会带来额外分支

---

### 8. 角色选择 / 开局

当前状态：

- `screen = "CHARACTER_SELECT"` 已识别
- 没有开局动作

反编译入口：

- [NCharacterSelectScreen.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs)

关键事实：

- 存在角色按钮
- 存在 `OnEmbarkPressed`
- 有 Ascension 面板
- 有随机角色
- 有 ready / unready 逻辑

如果目标只是“从地牢中途开始接管”，这块可以后做。  
如果目标是“从主菜单全自动打一整局”，这块必须补。

建议补齐：

- `character_select` payload
- 至少支持：
  - 选角色
  - 开始运行（Embark）

风险点：

- 不支持开局，就不能称为“从主菜单打完整局”

---

### 9. Game Over / 结算收尾

当前状态：

- `screen = "GAME_OVER"` 已识别
- `game_over` 仍为 `null`

反编译入口：

- [NGameOverScreen.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen/NGameOverScreen.cs)

关键事实：

- 结算界面至少有：
  - continue
  - view run
  - main menu
  - leaderboard
  - timeline

建议补齐：

- `game_over` payload
- 至少包含：
  - `win`
  - `score`
  - `floor`
  - `character`
- 最小动作：
  - `return_to_main_menu`
  - 或 `continue_from_game_over`

风险点：

- 没有 game over 收尾，完整 run 闭环仍不成立

---

## 容易漏掉但会卡自动流程的暗线

### A. Overlay 层

反编译入口：

- [NOverlayStack.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.Overlays/NOverlayStack.cs)
- [ActiveScreenContext.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext/ActiveScreenContext.cs)
- [AutoSlayer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.AutoSlay/AutoSlayer.cs)

关键事实：

- 当前 screen 可能优先来自 overlay，而不是房间本体
- 奖励、选牌、某些事件后续、宝箱 relic 选择都可能跑在 overlay 上

建议：

- 所有新功能都按“当前 active screen”而不是“当前 room 类型”来判断
- 每个动作都验证 screen 切换后的稳定态

---

### B. Modal / FTUE

反编译入口：

- [NModalContainer](../extraction/decompiled/Properties/AssemblyInfo.cs)
- [AutoSlayer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.AutoSlay/AutoSlayer.cs)
- [SaveManager.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Saves/SaveManager.cs)

关键事实：

- FTUE 和各种确认弹窗会真实挡输入
- 这些场景在战斗、地图、休息点、商店、洗牌、药水等地方都可能出现

建议二选一：

1. 运行前统一关闭 FTUE  
2. 增加最小 `modal` / `confirm` 处理能力

风险点：

- 如果正式测试环境没关 FTUE，很容易出现“接口都对，但 AI 被弹窗卡死”

---

### C. 同步器入口

反编译入口：

- [EventSynchronizer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Multiplayer.Game/EventSynchronizer.cs)
- [RestSiteSynchronizer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Multiplayer.Game/RestSiteSynchronizer.cs)
- [TreasureRoomRelicSynchronizer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Multiplayer.Game/TreasureRoomRelicSynchronizer.cs)
- [MapSelectionSynchronizer.cs](../extraction/decompiled/MegaCrit.Sts2.Core.Multiplayer.Game/MapSelectionSynchronizer.cs)

关键事实：

- 2 代很多非战斗动作最终都通过 synchronizer 驱动
- 这往往是比“模拟点击某个按钮”更稳定的动作入口

建议：

- Claude 开发时优先找 synchronizer / action queue 入口
- UI 点击只作为最后兜底

---

## 对 Claude 的建议开发顺序

推荐顺序：

1. 宝箱 relic 选择
2. 事件
3. 休息点
4. 商店
5. 药水
6. Game Over
7. 角色选择 / Embark

同时注意这三个交叉验证项：

- `EVENT -> embedded combat -> reward/map`
- `REST -> smith -> card selection -> confirm`
- `CHEST -> empty chest / relic pick -> proceed`

---

## Phase 6 完整流程验收标准

如果未来要宣布“Phase 6 完成”，至少应满足：

- 能从主菜单开始进入一局
- 能跨越地图、战斗、奖励、事件、休息点、商店、宝箱
- 能使用或处理药水
- 能处理结算与 game over
- 不依赖人工点击临场补操作
- 有端到端回归验证

否则只能算“主流程大部分已打通”，不能算真正完整覆盖。
