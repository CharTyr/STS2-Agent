# Phase 5 全链路补齐

更新时间：`2026-03-11`

这份文档只覆盖 Phase 5 新增的能力：角色选择、药水、阻塞弹窗、Game Over 收尾。完整基础协议仍以 [api.md](../docs/api.md) 为准。

---

## 新增状态

### `character_select`

仅当 `screen = CHARACTER_SELECT` 时存在。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `selected_character_id` | string \| null | 当前本地玩家已选择的角色 |
| `can_embark` | boolean | 当前是否可以点击开始 |
| `local_ready` | boolean | 本地玩家是否已 ready |
| `is_waiting_for_players` | boolean | 是否还在等待其他玩家 / UI 过渡 |
| `ascension` | number | 当前天梯等级 |
| `max_ascension` | number | 当前可选的最大天梯等级 |
| `characters[]` | object[] | 可见角色列表 |

`character_select.characters[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `select_character` 的 `option_index` |
| `character_id` | string | 角色内部 ID |
| `name` | string | 角色显示名称 |
| `is_locked` | boolean | 角色是否未解锁 |
| `is_selected` | boolean | 是否为当前已选角色 |
| `is_random` | boolean | 是否为随机位 |

### `modal`

仅当存在阻塞型弹窗或 FTUE 时存在；此时 `screen = MODAL`，可执行动作会收窄为弹窗动作。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `type_name` | string | Godot 节点类型名 |
| `underlying_screen` | string \| null | 弹窗下方原始界面，如 `MAP` / `CHARACTER_SELECT` |
| `can_confirm` | boolean | 是否可执行 `confirm_modal` |
| `can_dismiss` | boolean | 是否可执行 `dismiss_modal` |
| `confirm_label` | string \| null | 确认按钮文案 |
| `dismiss_label` | string \| null | 取消 / 关闭按钮文案 |

### `game_over`

仅当 `screen = GAME_OVER` 时存在。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `is_victory` | boolean | 是否胜利结算 |
| `floor` | number \| null | 到达层数 |
| `character_id` | string \| null | 本局角色 ID |
| `can_continue` | boolean | UI 是否仍可继续翻页 / 进入总结 |
| `can_return_to_main_menu` | boolean | 是否可执行 `return_to_main_menu` |
| `showing_summary` | boolean | 是否正在显示总结页 |

### `run.potions[]` 增量字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `usage` | string \| null | 药水使用方式 |
| `target_type` | string \| null | 目标类型 |
| `is_queued` | boolean | 是否已排入使用队列 |
| `requires_target` | boolean | 是否必须提供 `target_index` |
| `can_use` | boolean | 当前能否使用 |
| `can_discard` | boolean | 当前能否丢弃 |

---

## 新增动作

### `select_character`

- 前提：`screen = CHARACTER_SELECT`
- 参数：`option_index` 必填，对应 `character_select.characters[]`
- 失败场景：索引越界、角色锁定、按钮不可用

请求示例：

```json
{ "action": "select_character", "option_index": 0 }
```

### `embark`

- 前提：`screen = CHARACTER_SELECT` 且 `character_select.can_embark = true`
- 参数：无
- 稳定条件：进入地图、打开阻塞弹窗、或 lobby 状态明显推进

```json
{ "action": "embark" }
```

### `use_potion`

- 前提：药水槽位存在药水，且对应 `run.potions[i].can_use = true`
- 参数：`option_index` 必填；当 `requires_target = true` 时，`target_index` 也必填
- 当前支持：无目标药水、单体敌方目标药水

```json
{ "action": "use_potion", "option_index": 1 }
{ "action": "use_potion", "option_index": 0, "target_index": 0 }
```

### `discard_potion`

- 前提：对应 `run.potions[i].can_discard = true`
- 参数：`option_index` 必填

```json
{ "action": "discard_potion", "option_index": 2 }
```

### `confirm_modal`

- 前提：`screen = MODAL` 且 `modal.can_confirm = true`
- 参数：无

```json
{ "action": "confirm_modal" }
```

### `dismiss_modal`

- 前提：`screen = MODAL` 且 `modal.can_dismiss = true`
- 参数：无

```json
{ "action": "dismiss_modal" }
```

### `return_to_main_menu`

- 前提：`screen = GAME_OVER` 且 `game_over.can_return_to_main_menu = true`
- 参数：无

```json
{ "action": "return_to_main_menu" }
```

---

## 推荐链路

### 开局

```text
CHARACTER_SELECT
  -> select_character
  -> embark
  -> MODAL? -> confirm_modal / dismiss_modal
  -> MAP
```

### 战斗内药水

```text
COMBAT
  -> 查看 run.potions[]
  -> use_potion / discard_potion
  -> 继续 play_card / end_turn
```

### 收尾

```text
GAME_OVER
  -> 查看 game_over
  -> return_to_main_menu
```

---

## 实机验收重点

1. 角色选择后 `selected_character_id` 是否立即更新。
2. `embark` 后是否可能先进入 `MODAL`，再到 `MAP`。
3. FTUE / 教学弹窗出现时，普通动作是否正确被拦截，只暴露 `confirm_modal` / `dismiss_modal`。
4. 单体敌方目标药水是否要求并正确消费 `target_index`。
5. 药水使用后 `run.potions[]` 的 `occupied` / `is_queued` / `can_use` 是否同步变化。
6. `return_to_main_menu` 是否稳定离开 `GAME_OVER`，不会卡在总结页。
