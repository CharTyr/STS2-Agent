# STS2 AI Agent Mod — HTTP API

状态：可实现
协议版本：`2026-03-11-v1`

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
| `forbidden_actor` | 403 | 多人场景下试图为其它角色执行动作 | 否 |
| `mcp_disabled` | 403 | 请求 /mcp 但原生 MCP 未开启 | 否 |
| `session_not_ready` | 409 | 会话尚未就绪（如组队未完成） | 是 |
| `pause_pending` | 409 | 暂停尚未完成，需稍后重试 | 是 |
| `internal_error` | 500 | 服务内部异常 | 否 |
| `listener_error` | 500 | HTTP 监听循环本身抛异常（不是某个路由处理失败）。此时连接可能已经不健康，重试之前先用 `GET /health` 确认服务还在 | 是 |
| `local_only` | 403 | 该端点只接受本机（loopback）请求 | 否 |
| `companion_session_required` | 403 | 需要有效的 AI 队友会话令牌（见下） | 否 |
| `companion_not_ready` | 409 | 队友实例尚未就绪，无法响应控制 | 是 |
| `not_host` | 409 | 在 `companion` 实例上调用了只属于主窗口的 `POST /teammate/control` | 否 |
| `teammate_control_failed` | 409 | `POST /teammate/control` 未能确认队友的开始 / 暂停：还没有组队、队友进程已退出、上一条控制未完成，或队友没有确认。失败原因在 `error.message` 里 | 是 |
| `invite_failed` | 409 | 邀请 AI 队友失败（主菜单状态或配置不满足） | 是 |
| `continue_failed` | 409 | 读档开房流程**已经启动后**失败（读档界面没打开，或本地直连端口 33771 仍被上一局占用，重启游戏后可重试）。前置条件不满足（不是主机主菜单、当前是队友实例、本机角色正在自动游玩、没有联机存档）仍返回 `invalid_action`；双开已经进行中时改为返回 200 `pending`，与并发 `invite_ai_teammate` 一致。游玩模型未验证**不会**挡 `continue_ai_teammate`：与邀请相同，`requireVerifiedPlayModel` 跟 `ReadyToInvite` 走两条路线，未验证时照常拉起队友、只是不自动游玩 | 是 |
| `invalid_action`（`continue_ai_teammate` 的 NetId 前置检查） | 409 | 读档**之前**的只读比对不通过：本机 NetId 不在联机存档 `players[].net_id` 里（游戏会拒绝读档，并把这局存档改名成 `*.VAL.corrupt` 挪走且不还原），或本次要拉起的 AI 队友 NetId 不在存档里（加入会被 `NotInSaveGame` 拒绝）。详情带 `save_player_net_ids`，以及 `local_player_id` 或 `companion_client_id` | 否 |
| `invalid_action`（`run_console_command` 的命令抛异常） | 409 | 命令被游戏接受后实现抛异常（例如 `bestiary` 会 `NullReferenceException`）。消息为 `Console command failed: <异常类型>: <异常消息>`，详情带 `command` | 否 |
| `collection_not_found` | 404 | `GET /data/{collection}` 的集合名不存在 | 否 |
| `export_error` | 500 | 游戏元数据导出失败 | 是 |
| `origin_not_allowed` | 403 | 原生 MCP 请求的 `Origin` 不受信任 | 否 |
| `method_not_allowed` | 405 | 用 POST 以外的方法请求 `/mcp`。原生 MCP 走 Streamable HTTP，只接受 POST 的 JSON-RPC | 否 |
| `payload_too_large` | 413 | `/mcp` 请求体超过 1 MB | 否 |

---

## Screen 枚举

| 值 | 含义 |
| --- | --- |
| `MAIN_MENU` | 主菜单、补丁说明、子菜单、Logo 动画 |
| `CHARACTER_SELECT` | 角色选择界面 |
| `MULTIPLAYER_LOBBY` | 多人联机房间界面（`host_multiplayer_lobby` / `join_multiplayer_lobby` / `ready_multiplayer_lobby` / `disconnect_multiplayer_lobby`） |
| `MULTIPLAYER_LOAD` | 多人读档界面（`continue_ai_teammate` 读入联机存档后、各玩家 `embark` 之前） |
| `BUNDLE_SELECTION` | 开局卡包选择界面（用 `choose_bundle` / `confirm_bundle`） |
| `CAPSTONE_SELECTION` | Capstone 选项界面（用 `choose_capstone_option`） |
| `PAUSE_MENU` | 暂停菜单叠加层（人工按下暂停；agent 在此期间没有可用动作，也拿不到 capstone 选项） |
| `SETTINGS` | 设置页（暂停菜单里的「设置」；只有 `close_main_menu_submenu` 可退回上一级） |
| `COMPENDIUM` | 百科大全 hub（暂停菜单里的「百科大全」，通向下面这些图鉴页） |
| `RELIC_COLLECTION` | 遗物收集页（「百科大全」→「遗物收集」） |
| `POTION_LAB` | 药水研究所页（「百科大全」→「药水研究所」） |
| `BESTIARY` | 怪物图鉴页（「百科大全」→ 怪物图鉴）。hub 只在 `NBestiary.CanBeShown()` 为真时才画出这块磁贴 |
| `STATS` | 角色数据页（「百科大全」→「角色数据」） |
| `RUN_HISTORY` | 历史记录页（「百科大全」→「历史记录」） |
| `MAP` | 地图界面 |
| `COMBAT` | 战斗中 |
| `EVENT` | 事件交互 |
| `CRYSTAL_SPHERE` | 水晶球占卜小游戏 |
| `SHOP` | 商店 |
| `REST` | 休息点 |
| `REWARD` | 奖励结算 / 卡牌奖励选择 |
| `CHEST` | 宝箱房 |
| `CARD_SELECTION` | 牌库选牌界面（删牌等） |
| `CARDS_VIEW` | 看牌浮层，属于可关闭的看牌屏（用 `close_cards_view` 关闭） |
| `CARD_LIBRARY` | 牌库 / 图鉴查看屏（用 `close_main_menu_submenu` 返回，主菜单侧与局内侧一致） |
| `CARD_PILE` | 战斗中打开的抽牌堆 / 弃牌堆 / 消耗堆查看屏（用 `close_cards_view` 返回） |
| `MODAL` | 阻塞中的弹窗 / FTUE |
| `GAME_OVER` | 游戏结束 |
| `UNLOCK` | 解锁弹窗界面（用 `confirm_unlock` 逐层关闭） |
| `TIMELINE` | 时间线界面（用 `choose_timeline_epoch` / `confirm_timeline_overlay`） |
| `FAKE_MERCHANT` | 假商人事件里的商店界面（`open_shop_inventory` 可用） |
| `PATCH_NOTES` | 补丁说明页（用 `close_main_menu_submenu` 关闭） |
| `CARD_INSPECT` | 卡牌查看浮层（用 `close_cards_view` 关闭） |
| `RELIC_INSPECT` | 遗物查看浮层（用 `close_cards_view` 关闭） |
| `FEEDBACK` | 反馈提交页；不提供关闭动作，仅用于诊断 |
| `UNKNOWN` | 无法识别的界面 |

人按下暂停后出现的暂停菜单，以及从它进入的设置页与百科大全各页（卡牌总览、遗物收集、药水研究所、怪物图鉴、角色数据、历史记录）都由同一个 `NCapstoneSubmenuStack` 容器承载。`screen` 报的是容器栈顶那个页面的名字——`PAUSE_MENU`、`SETTINGS`、`COMPENDIUM`、`CARD_LIBRARY`、`RELIC_COLLECTION`、`POTION_LAB`、`BESTIARY`、`STATS`、`RUN_HISTORY`——而不是被这些页面盖住的房间；容器里出现未知页面时仍落到 `CAPSTONE_SELECTION`。

这些都是人工菜单，被暂停吞掉的房间动作不再出现：`end_turn`、`play_card`、`choose_map_node`、`resolve_rewards`、`save_and_quit` 一律不广告，直接调用会得到 409 `invalid_action`；`capstone` 同样不出现——容器里的按钮是导航磁贴、筛选勾选框和控件命中区（`Hitbox`），不是 agent 的选项列表，所以 `choose_capstone_option` 既不广告也调用不了。

除暂停菜单本身以外，这些页面各留一个动作：`close_main_menu_submenu` 退回上一级（等同页面自己的返回按钮 `Stack.Pop()`），例如 `CARD_LIBRARY` → `COMPENDIUM` → `PAUSE_MENU`；退到暂停菜单为止，那一页不再提供任何动作——恢复游戏是人的事，agent 不替人点「继续」。`close_cards_view` 在这些页面上用不上：容器页不是看牌屏，而它的管辖范围只有战斗里的看牌屏、牌堆，以及 `CARD_INSPECT` / `RELIC_INSPECT` 浮层（见下面的动作表）。

## Action Status

动作执行后的 `status` 字段：

| 值 | 含义 |
| --- | --- |
| `completed` | 动作已完成，返回的 `state` 已稳定 |
| `pending` | 动作已提交，但游戏状态尚在过渡中（等待动画/队列清空） |
| `failed` | 动作已执行但失败；`mcp_server/src/sts2_mcp/client.py` 读到该值会抛 `action_failed`，不能当成功处理 |

---

## `GET /health`

返回 Mod 基础状态、本次会话的运行计数与自动游玩状态。用于确认游戏正在运行且 Mod 已加载；自动游玩是否在跑、上次为什么停下，也读这里。

### 响应示例

```json
{
  "ok": true,
  "request_id": "req_20260911_121549_7955_4",
  "data": {
    "service": "sts2-ai-agent",
    "mod_version": "0.12.5",
    "protocol_version": "2026-03-11-v1",
    "game_version": "v0.111.0",
    "status": "ready",
    "api_host": "127.0.0.1",
    "api_port": 8080,
    "process_id": 50708,
    "instance_role": "human",
    "mcp_enabled": false,
    "mcp_url": null,
    "play_running": false,
    "play_phase": "paused",
    "stop_kind": null,
    "session_requests": 12,
    "companion_process_alive": false,
    "companion_process_exited": false,
    "companion": null,
    "dual_status": "尚未启动双开。",
    "dual_launch_outcome": null,
    "team_control_status": "队友控制尚未连接。"
  }
}
```

### 字段说明

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `service` | string | 固定为 `sts2-ai-agent`，用于确认应答的确实是本 Mod 而不是同端口上的别的服务 |
| `mod_version` | string | Mod 版本号，与 `STS2AIAgent/Server/Router.cs` 的 `ModVersion` 常量一致 |
| `protocol_version` | string | HTTP 协议版本 |
| `game_version` | string | 游戏版本；示例值随游戏更新变化 |
| `status` | string | `ready`：Mod 仍能读取这一版游戏的全部内部状态。`degraded`：至少有一处按名字反射的游戏私有成员已经找不到——请求照常受理，但受影响的字段会退回默认值，**具体缺哪些看 `compatibility`**。这个值由启动自检推导，不是写死的 |
| `api_host` / `api_port` | string / integer | **本实例**自己的 HTTP API 地址。端口被占用时会自动递增，所以不要写死 8080——以这里为准 |
| `process_id` | integer | 游戏进程 PID，用于核对双开窗口身份 |
| `instance_role` | string | `human`（玩家窗口）或 `companion`（AI 队友实例） |
| `mcp_enabled` / `mcp_url` | boolean / string\|null | 进程内 MCP 是否开启，以及开启时的地址 |
| `play_running` | boolean | 自动游玩是否正在运行 |
| `play_phase` | string | 自动游玩阶段，常见值 `running` / `paused` / `stopping` |
| `stop_kind` | string\|null | 上次自动游玩停止的类别，未停止过为 `null` |
| `session_requests` | integer | 本会话已消耗的模型请求次数（可由「重置本会话统计」清零） |
| `companion_process_alive` / `companion_process_exited` | boolean | AI 队友进程是否在运行 / 是否已退出 |
| `companion` | object\|null | 仅主窗口、且本次组队的队友进程仍在运行时存在（队友退出后回到 `null`）。`api_host` / `api_port` / `process_id` 是队友实例的 HTTP API，用来直接对队友的 `GET /state` 与 `POST /action` 编程；`auto_play` 说明这次组队走的是 AI 自走（`true`）还是外部接管（`false`）。队友会话令牌**不会**出现在任何响应里 |
| `dual_status` / `team_control_status` | string | 双开与队友控制的人类可读状态 |
| `dual_launch_outcome` | string\|null | 双开结构化结果，给外部客户端做成败分类，**不要**用 `dual_status` 文本。尚未尝试过为 `null`；否则为 `InProgress` / `Succeeded` / `Failed` / `Rejected` / `Canceled`（`DualLaunchOutcome` 枚举名，不含 `Idle`） |
| `compatibility` | object | 启动时对 Mod 依赖的游戏私有成员做的一次自检结果。`reflected_members_checked` 是被检查的成员总数，`reflected_members_missing` 是找不到的个数，`missing_members[]` 逐条给出 `member`（`类型.成员`）与 `feature`（失效的功能）。游戏更新后 Mod 最常见的坏法就是某个私有字段被改名，此时读取悄悄退回默认值——这个区块是唯一的信号 |
| `state_build` | object | 构建状态载荷的耗时，只计构建本身、不含排队等游戏线程。`/state`、每个动作响应与 SSE 刷新都会构建一次，且都跑在游戏线程上——构建期间游戏不出帧。`slow_threshold_ms`（100，约等于 60 帧下 6 帧的卡顿）、`samples`、`slow_builds`、`last_ms`、`max_ms` 与其 `max_screen`、以及最近 `recent_samples`（至多 256）次的 `recent_p50_ms` / `recent_p95_ms`；尚无样本时耗时字段为 `null`。超过阈值的构建会在游戏日志里记 `WARN`，每 30 秒至多一条并注明期间压下的条数 |

### `stop_kind` 取值

| 值 | 含义 |
| --- | --- |
| `null` | 本次自动游玩没有以停止收尾（仍在运行，或尚未开始） |
| `budget` | 命中会话预算或请求次数上限 |
| `run_end` | 已离开当前局，或对局标识发生变化 |
| `config` | 模型配置或认证问题（401/402/403/404/422、密钥无效等） |
| `network` | 网络或超时问题（408/429/5xx、连接被拒绝等） |
| `failed` | 其它原因，包括连续 3 次决策未成功 |

---

## `GET /state`

返回当前游戏状态的完整快照。这是 AI Agent 做出决策前最重要的端点。

### 顶层字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `state_version` | number | 状态模型版本（当前固定为 11） |
| `native_profile_id` | number | 游戏原生存档档位 id（1..3），与 `switch_profile` 的 `option_index` 同一空间 |
| `run_id` | string | 本局运行标识（种子字符串） |
| `screen` | string | 当前逻辑界面（见 Screen 枚举） |
| `session` | object | 对局归属：单人/联机、所处阶段、控制范围。**路由的第一依据**，见下 |
| `in_combat` | boolean | 是否处于战斗流程 |
| `turn` | number \| null | 当前回合数（非战斗时为 null） |
| `available_actions` | string[] | 当前可执行动作名列表 |
| `combat` | object \| null | 战斗状态（仅战斗中存在） |
| `run` | object \| null | 本局运行状态 |
| `multiplayer` | object \| null | 联机连接摘要（仅局内存在） |
| `multiplayer_lobby` | object \| null | 联机大厅状态（仅大厅界面存在） |
| `map` | object \| null | 地图状态（仅地图界面存在） |
| `reward` | object \| null | 奖励状态（仅奖励界面存在） |
| `selection` | object \| null | 选牌状态（仅选牌界面存在） |
| `chest` | object \| null | 宝箱状态（仅宝箱房存在） |
| `event` | object \| null | 事件状态（仅事件房存在） |
| `crystal_sphere` | object \| null | 水晶球棋盘状态（仅占卜小游戏存在） |
| `shop` | object \| null | 商店状态（仅商店房存在） |
| `rest` | object \| null | 休息点状态（仅休息点存在） |
| `character_select` | object \| null | 角色选择状态（仅角色选择界面存在） |
| `timeline` | object \| null | 时间线状态（仅时间线界面存在） |
| `unlock` | object \| null | 解锁覆盖层状态（仅解锁覆盖层存在） |
| `bundles` | object[] \| null | 卡包选择（仅出现卡包时存在） |
| `capstone` | object \| null | 决策型覆盖层的选项集（仅该覆盖层存在；暂停菜单与其页面**不**在此列） |
| `modal` | object \| null | 阻塞弹窗状态（仅 MODAL 界面存在） |
| `game_over` | object \| null | 游戏结束状态（仅 GAME_OVER 界面存在） |
| `agent_view` | object \| null | 同一份状态的紧凑文本化改写，仅在请求时附带；见「compact `agent_view`」一节 |

### `session` 子结构

**这是路由的第一依据**，不要从屏幕名或工具名反推单人 / 联机。`/state` 永远带 `session`，包括主菜单。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `mode` | string | `singleplayer` 或 `multiplayer` |
| `phase` | string | `menu` / `character_select` / `multiplayer_lobby` / `run` |
| `control_scope` | string | 本实例能替谁做决定。目前恒为 `local_player`：即使在联机局里，也只操作自己的角色 |

`mode` 在主菜单阶段固定报 `singleplayer`（还没有连接可言）；进入选角或局内后由该屏自己的
`NetService.Type` 决定。

### `multiplayer` 子结构

局内的联机连接摘要。单人局也会带（`is_multiplayer = false`），大厅阶段请改看 `multiplayer_lobby`。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `is_multiplayer` | boolean | 本局是否为联机局 |
| `net_game_type` | string | 游戏自己的连接类型名 |
| `local_player_id` | string \| null | 本地玩家 id，与 `combat.players[].player_id` 同一空间 |
| `player_count` | number | 当前玩家数 |
| `connected_player_ids` | string[] | 已连接玩家的 id 列表 |

`combat.players[]` / `run.players[]` 与 `target_index` **共用同一个下标空间**：要对第 N 个玩家生效的
动作，`target_index` 就取该玩家在这两个数组里的位置，而不是 `player_id`。

### `multiplayer_lobby` 子结构

仅在 `MULTIPLAYER_LOBBY` 屏存在。`can_*` 是**这一刻按钮真的可点**，不是「原则上允许」——
直接用它决定下一步，不要自己推。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `net_game_type` | string | 连接类型名 |
| `join_host` | string | 加入用的主机地址 |
| `join_port` | number | 加入用的端口 |
| `local_net_id_hint` | string \| null | 本实例的 NetId 提示。本地直连路径下它来自启动参数 `--clientId`，读联机存档时游戏会拿它和存档里的 `players[].net_id` 比对 |
| `has_lobby` | boolean | 大厅是否已建立 |
| `is_host` | boolean | 本实例是否为房主 |
| `is_client` | boolean | 本实例是否为客户端 |
| `local_ready` | boolean | 本地玩家是否已准备 |
| `can_host` | boolean | 此刻可执行「建立房间」 |
| `can_join` | boolean | 此刻可执行「加入」 |
| `can_ready` | boolean | 此刻可执行「准备」 |
| `can_unready` | boolean | 此刻可执行「取消准备」 |
| `can_disconnect` | boolean | 此刻可执行「断开」 |
| `selected_character_id` | string \| null | 本地玩家已选角色 id |
| `player_count` | number | 当前玩家数 |
| `max_players` | number | 房间容量（4） |
| `players` | object[] | 各槽位玩家，字段同 `character_select.players[]` |
| `characters` | object[] | 可选角色，字段同 `character_select.characters[]` |

### `character_select` 子结构

仅在 `CHARACTER_SELECT` 屏存在。联机局里**两侧都要各自选角并 Ready** 才能开局，所以
`is_waiting_for_players` 为 true 时正确动作是等待而不是重复点 `embark`。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `selected_character_id` | string \| null | 本地玩家已选角色 id；未选时为 null |
| `is_multiplayer` | boolean | 本次选角是否属于联机局 |
| `net_game_type` | string | 连接类型名 |
| `can_embark` | boolean | 此刻可执行 `embark`（出发） |
| `can_unready` | boolean | 此刻可执行 `unready`。本地玩家已 Ready 之后 `select_character` 不再被广告，`unready` 才是该状态下的动作 |
| `can_increase_ascension` | boolean | 此刻可提升进阶等级 |
| `can_decrease_ascension` | boolean | 此刻可降低进阶等级 |
| `local_ready` | boolean | 本地玩家是否已准备 |
| `is_waiting_for_players` | boolean | 本地已就绪、正在等其他玩家 |
| `player_count` | number | 当前玩家数 |
| `max_players` | number | 房间容量 |
| `ascension` | number | 当前进阶等级 |
| `max_ascension` | number | 本地玩家可选的最高进阶等级 |
| `seed` | string \| null | 指定的种子；未指定时为 null |
| `modifier_ids` | string[] | 已启用的对局修正 id |
| `players` | object[] | 各槽位玩家，见下 |
| `characters` | object[] | 可选角色，见下 |

#### `character_select.characters[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 下标，`select_character` 的 `option_index` 取这里 |
| `character_id` | string | 角色 id |
| `name` | string | 角色显示名 |
| `is_locked` | boolean | 未解锁；选它会被拒 |
| `is_selected` | boolean | 本地玩家当前选中的就是它 |
| `is_random` | boolean | 这一项是「随机」而不是具体角色 |

#### `character_select.players[]`

`multiplayer_lobby.players[]` 用同一组字段。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `player_id` | string | 玩家 id |
| `slot_index` | number | 槽位下标 |
| `is_local` | boolean | 是否为本实例操作的玩家 |
| `character_id` | string \| null | 该玩家已选角色 id |
| `character_name` | string \| null | 该玩家已选角色名 |
| `is_ready` | boolean | 该玩家是否已准备 |
| `max_multiplayer_ascension_unlocked` | number | 该玩家已解锁的最高联机进阶等级 |

### `combat` 子结构

#### `combat` 顶层字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `action_readiness` | object | 执行器此刻是否接受战斗动作，不接受时卡在哪一步。见下 |
| `player` | object | 本地玩家的战斗状态 |
| `players` | object[] | 本局全部玩家的战斗血线（含本地玩家）。联机时用来判断队友是否需要救援；单人局只有一项 |
| `hand` | object[] | 本地玩家手牌 |
| `enemies` | object[] | 场上敌人 |
| `end_turn_will_kill_player` | boolean | 此刻直接结束回合是否会打死本地玩家 |
| `lethal_risks` | object[] | 致命风险逐条拆解，见下 |

#### `combat.action_readiness`

一份 `/state` 响应里，`available_actions`、`combat.action_readiness` 与药水可用标记**出自同一次门禁求值**
（v0.12.4 起）。门禁带 200ms 稳定采样窗口，此前它被逐个动作重复求值，同一份响应会跨过采样窗口而自相矛盾。
因此现在：`can_use_combat_actions = true` 的快照必然同时带着 `play_card` / `end_turn`，反之亦然——**不要把这
两者当成两个独立信号去交叉验证**，它们是同一个结论的两种投影。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `can_use_combat_actions` | boolean | 执行器此刻是否接受出牌 / 结束回合 |
| `reason` | string | 门禁结论的原因码，取值见下表 |
| `actions_settled` | boolean | 动作队列已排空（无正在执行、无就绪、无执行中占用） |
| `running_action_type` | string \| null | 正在执行的 GameAction 类型名 |
| `ready_action_type` | string \| null | 队列里已就绪待执行的 GameAction 类型名 |
| `modal_open` | boolean | 是否有阻塞弹窗盖在战斗上 |
| `modal_type` | string \| null | 该弹窗的类型名 |
| `player_actions_disabled` | boolean | 战斗管理器禁用了玩家输入 |
| `is_paused` | boolean | 战斗被暂停 |
| `local_ready_to_end_turn` | boolean | 本地玩家已按下结束回合 |
| `all_players_ready_to_end_turn` | boolean | 全部玩家都已结束回合（联机） |
| `ending_turn_phase_one` | boolean | 回合结算第一阶段进行中 |
| `ending_turn_phase_two` | boolean | 回合结算第二阶段进行中 |
| `end_turn_kick` | string \| null | 结束回合被外力触发时的来源说明 |
| `combat_in_progress` | boolean | `CombatManager.IsInProgress` |
| `combat_over_or_ending` | boolean | 战斗已结束或正在收尾 |
| `combat_room_mode` | string \| null | 战斗房当前模式名 |
| `hand_in_card_play` | boolean \| null | 手牌处于出牌动画中；手牌不可读时为 null |
| `hand_in_card_selection` | boolean \| null | 手牌处于选牌模式中；手牌不可读时为 null |
| `hand_mode` | string \| null | 手牌当前模式名 |
| `local_turn_ready` | boolean | 本地玩家的回合已开始（`TurnNumber > 0`） |
| `snapshot_stable` | boolean | 连续两次采样一致，已越过 200ms 稳定窗口 |
| `player_action_phase` | boolean | 当前处于玩家行动阶段 |

`reason` 取值（按门禁的求值顺序列出，先命中先返回；只有全部不命中才是 `ready`）：

| 取值 | 含义 |
| --- | --- |
| `modal_open` | 有阻塞弹窗盖着，先 `confirm_modal` |
| `combat_screen_unavailable` | 不在战斗屏，或战斗状态为空 |
| `combat_not_in_progress` | 战斗未开始 |
| `combat_over_or_ending` | 战斗已结束或正在收尾 |
| `combat_paused` | 战斗被暂停（例如打开了暂停菜单） |
| `player_actions_disabled` | 战斗管理器暂时禁用了玩家输入 |
| `combat_room_not_active` | 战斗房不在活动模式 |
| `hand_unavailable` | 手牌节点不可读 |
| `hand_in_card_play` | 上一张牌的出牌动画还没放完 |
| `hand_in_card_selection` | 手牌处在选牌模式（例如弃牌、消耗选择） |
| `hand_mode_not_play` | 手牌模式不是「可出牌」 |
| `local_player_dead` | 本地玩家已阵亡 |
| `local_turn_not_ready` | 本地玩家的回合尚未开始 |
| `game_action_running` | 有 GameAction 正在执行 |
| `game_action_queued` | 队列里有已就绪的 GameAction |
| `action_queue_unsettled` | 队列报告仍有执行中的动作 |
| `not_player_action_phase` | 不在玩家行动阶段（敌方回合、结算中） |
| `snapshot_stabilizing` | 其余条件都满足，但还没越过 200ms 稳定窗口 |
| `ready` | 可以出牌 / 结束回合 |

以上除 `ready` 外都是**暂时**状态，正确反应是等待（`wait_until_actionable`）而不是重试或换动作；
`scripts/run_sts2_validation.py state-invariants` 也按 `can_use_combat_actions` 判断该不该要求 `play_card`。

#### `combat.lethal_risks[]`

`end_turn_will_kill_player` 的逐条依据。没有风险时为空数组。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `risk_id` | string | 风险标识 |
| `source` | string | 风险来源（敌人意图 / 自身 Power 等） |
| `will_kill_player` | boolean | 这一条是否单独就足以致死 |
| `reason` | string | 判定说明 |
| `incoming_damage` | number \| null | 来袭伤害 |
| `damage_after_block` | number \| null | 扣除格挡后的伤害 |
| `player_hp` | number \| null | 结算时的玩家生命值 |
| `player_block` | number \| null | 结算时的玩家格挡 |
| `power_id` | string \| null | 来源 Power 的 ID（来源是 Power 时） |
| `power_amount` | number \| null | 该 Power 的层数 |

#### `combat.player`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `current_hp` | number | 当前生命值 |
| `max_hp` | number | 最大生命值 |
| `base_max_hp` | number \| null | **联机缩放之前**掷出的基础最大生命值，与 `GET /data/monsters` 的 `min_hp` / `max_hp` 同一量纲；`max_hp` 是缩放后的实时值。玩家、宠物或该值尚未设置时为 `null` |
| `block` | number | 当前格挡值 |
| `energy` | number | 当前能量 |
| `stars` | number | 当前星星数 |
| `focus` | number | 当前集中（Defect 的球加值；无该资源的角色为 0） |
| `powers` | object[] | 玩家当前持有的 Power / Buff / Debuff 列表 |
| `base_orb_slots` | number | 角色的基础球槽数（Defect 为 3，其余角色为 0） |
| `orb_capacity` | number | 本场战斗的实际球槽容量（含增益后） |
| `empty_orb_slots` | number | 当前空余球槽数 |
| `orbs` | object[] | 当前球列表，见下 |
| `pets` | object[] | 玩家己方的宠物列表（亡灵契约师的奥斯提、鸟宠等），见下 |
| `pet_missing` | bool | 该角色的遗物会生成宠物、但此刻场上没有宠物时为 true（宠物已阵亡或尚未召唤）。不会凭空判断"本来就没有宠物"的角色 |
| `cards_played_this_turn` | number | 本回合已打出的牌数 |
| `attacks_played_this_turn` | number | 本回合已打出的攻击牌数 |
| `skills_played_this_turn` | number | 本回合已打出的技能牌数 |

#### `combat.player.orbs[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `slot_index` | number | 球槽序号，0 在最左 |
| `orb_id` | string | 球的模型 id（如 `LIGHTNING_ORB`） |
| `name` | string | 球的显示名 |
| `passive_value` | number | 被动数值 |
| `evoke_value` | number | 激发数值 |
| `is_front` | bool | 是否为最前面的球（会被下一个激发效果取用） |

#### `combat.player.pets[]`

己方宠物不会出现在 `combat.enemies[]` 里，因此单独列出。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 列表序号 |
| `pet_id` | string | 宠物的模型 id（如 `OSTY`） |
| `name` | string | 宠物显示名 |
| `current_hp` | number | 当前生命值 |
| `max_hp` | number | 最大生命值 |
| `block` | number | 当前格挡值 |
| `powers` | object[] | 宠物身上的 Power（如 `DIE_FOR_YOU_POWER`） |

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
| `rules_text` | string | 原始兼容规则文本 |
| `resolved_rules_text` | string | 按当前实例动态变量展开后的规则文本 |
| `dynamic_values` | object[] | 当前实例的动态变量列表 |
| `playable` | boolean | **当前是否可打出** |
| `unplayable_reason` | string \| null | 不可打出原因（`not_enough_energy`, `not_enough_stars`, `no_living_allies`, `blocked_by_hook`, `unplayable`, `unsupported_target_type`） |
| `unplayable_reason_raw` | string \| null | 游戏侧 `UnplayableReason` 枚举的原始名，供排查用；稳定分支请用 `unplayable_reason` |
| `unplayable_preventer_id` | string \| null | 阻止出牌的来源 id（某个 Power 或遗物） |
| `unplayable_preventer_type` | string \| null | 该来源的游戏侧类型全名，排查用 |
| `can_play_result` | boolean | 游戏自己的可打出判定。与 `playable` 的区别是它**不含目标类型是否受支持**这一层：`target_type` 本 mod 尚未支持时 `can_play_result` 仍为 true 而 `playable` 为 false。**决策请用 `playable`** |
| `target_index_space` | string \| null | `target_index` 落在哪个数组的下标空间（如 `combat.enemies[].index` / `combat.players[].slot_index`）。需要目标时才有值 |
| `valid_target_indices` | number[] | 在上述空间里此刻合法的 `target_index`。传这个列表之外的值是 409 `invalid_target` |

#### `*.dynamic_values[]`（适用于 `combat.hand[]`、`run.deck[]`、`selection.cards[]`、`reward.card_options[]`、`shop.cards[]`）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `name` | string | 动态变量名（如 `Damage`、`Block`、`CalculatedDamage`、`Repeat`） |
| `base_value` | number | 该变量的基础值 |
| `current_value` | number | 当前预览值，通常对应 UI 正在显示的数值 |
| `enchanted_value` | number | 附魔/永久修正后的值，不含本次预览变化时通常与基础值相同 |
| `is_modified` | boolean | 当前值或附魔值是否相对基础值发生变化 |
| `was_just_upgraded` | boolean | 该变量是否刚因升级变化 |

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
| `floor` | number | 当前楼层 |
| `current_hp` | number | 当前生命值 |
| `max_hp` | number | 最大生命值 |
| `gold` | number | 当前金币 |
| `max_energy` | number | 基础最大能量 |
| `act_id` | string \| null | 当前 Act 序号 |
| `boss_id` | string \| null | 本 Act 的 Boss id；尚未确定时为 null |
| `deck[]` | object[] | 当前牌库 |
| `relics[]` | object[] | 当前遗物 |
| `potions[]` | object[] | 当前药水槽 |
| `ascension` | number | 当前 run 的 Ascension 等级 |
| `ascension_effects[]` | object[] | 当前 Ascension 等级已生效的累计效果列表 |
| `players[]` | object[] | 队伍摘要（含本地玩家），字段同 `combat.players[]` 另加 `gold` |

#### `run.ascension_effects[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | string | Ascension 效果 ID，例如 `LEVEL_08` |
| `name` | string | Ascension 效果名称 |
| `description` | string | Ascension 效果描述 |

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
| `rules_text` | string | 原始兼容规则文本 |
| `resolved_rules_text` | string | 按当前实例动态变量展开后的规则文本 |
| `dynamic_values` | object[] | 当前实例的动态变量列表 |

#### `run.relics[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 遗物索引 |
| `relic_id` | string | 遗物内部 ID |
| `name` | string | 遗物名称 |
| `description` | string \| null | 遗物描述（若可读取） |
| `stack` | number \| null | 遗物图标上显示的计数（游戏的 `DisplayAmount`，仅当该遗物显示计数时有值），其余为 `null`。2026-09-18 之前此字段对所有遗物恒为 `null`：它读的是游戏里不存在的 `Amount` |
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
| `target_index_space` | string \| null | `target_index` 落在哪个数组的下标空间；需要目标时才有值 |
| `valid_target_indices` | number[] | 在上述空间里此刻合法的 `target_index` |

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
| `local_vote` | object \| null | 本地玩家已投的坐标 `{ row, col }`；**非 null 就表示已投票，此时应等待而不是再投一次** |
| `player_votes[]` | object[] | 联机局各玩家的投票，见下 |

#### `map.available_nodes[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 用于 `choose_map_node` 的 `option_index` |
| `row` | number | 行坐标 |
| `col` | number | 列坐标 |
| `node_type` | string | 节点类型（`Monster`, `Elite`, `Boss`, `Rest`, `Shop`, `Event`, `Treasure` 等） |
| `state` | string | 节点状态（`Travelable`, `Traveled` 等） |
| `vote_count` | number | 投给该节点的玩家数（联机局） |
| `has_local_vote` | boolean | 本地玩家投的就是这个节点 |
| `voted_player_ids` | string[] | 投给该节点的玩家 id 列表 |

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

#### `map.player_votes[]`

仅联机局非空。地图推进需要**全员投票**，所以本地投完之后要等其他人，不要重复 `choose_map_node`。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `player_id` | string | 玩家 id |
| `slot_index` | number | 槽位下标 |
| `is_local` | boolean | 是否为本地玩家 |
| `coord` | object \| null | 该玩家投的坐标 `{ row, col }`；尚未投票为 null |

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
| `rules_text` | string | 原始兼容规则文本 |
| `resolved_rules_text` | string | 按当前实例动态变量展开后的规则文本 |
| `dynamic_values` | object[] | 当前实例的动态变量列表 |

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
| `selected` | boolean | 该卡当前是否已被选中（多选屏用它对照 `selection.selected_count`） |
| `card_id` | string | 卡牌 ID |
| `name` | string | 卡牌名称 |
| `upgraded` | boolean | 是否已升级 |
| `card_type` | string | 卡牌类型 |
| `rarity` | string | 稀有度 |
| `costs_x` | boolean | 是否为能量 X 费卡 |
| `star_costs_x` | boolean | 是否为星星 X 费卡 |
| `energy_cost` | number | 能量消耗（含修正） |
| `star_cost` | number | 星星消耗（含修正） |
| `rules_text` | string | 原始兼容规则文本 |
| `resolved_rules_text` | string | 按当前实例动态变量展开后的规则文本 |
| `dynamic_values` | object[] | 当前实例的动态变量列表 |

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

### `crystal_sphere` 子结构

当 `screen` 为 `CRYSTAL_SPHERE` 时存在。完整 `/state` 与 compact
`agent_view` 都包含同一棋盘信息，因此默认 MCP `get_game_state` 可以直接规划点击。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `divinations_left` | number | 剩余占卜次数；必须用完才会出现 `proceed` |
| `tool` | string | 当前工具：`big`（3×3）或 `small`（单格） |
| `is_finished` | boolean | 是否已用完占卜次数 |
| `grid_width` / `grid_height` | number | 棋盘尺寸 |
| `hidden_cells` | number[][] | 尚未清开的 `[x,y]` 坐标 |
| `items[]` | object[] | 已成功放置的奖励/诅咒及其占格信息 |

`items[]` 包含 `kind`、`is_good`、`x/y/width/height`、`revealed`、
`cells` 和 `hidden_cells`。物品的全部占格被清开后才会揭示；结束时所有已揭示物品都会发放，
包括诅咒。

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
| `requires_target` | boolean | 该操作是否需要指定目标（联机局的部分休息点操作要选队友） |
| `target_index_space` | string \| null | `target_index` 落在哪个数组的下标空间；需要目标时才有值 |
| `valid_target_indices` | number[] | 在上述空间里此刻合法的 `target_index` |
| `valid_target_player_ids` | string[] | 与上一行同序的玩家 id，便于核对选中的是谁 |

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
| `is_stocked` | boolean | 该货位还有货（买走之后为 false） |
| `enough_gold` | boolean | 还有货**且**金币够。**这一项才是「现在能不能买」**；单看 `is_stocked` 会在钱不够时报 409 |

> 这三张表此前写的是一个 `available` 字段。`shop.cards[]` / `shop.relics[]` / `shop.potions[]`
> 从来没有过这个字段——只有 `shop.card_removal` 有。按旧文档分支会读到 `undefined`，
> 请改用 `enough_gold`。

#### `shop.cards[]` 附加字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `category` | string | 该卡所在的货架分类 |
| `on_sale` | boolean | 该卡正在打折 |
| `card_id` | string | 卡牌内部 ID |
| `upgraded` | boolean | 是否已升级 |
| `card_type` | string | 卡牌类型 |
| `rarity` | string | 稀有度 |
| `costs_x` | boolean | 是否为能量 X 费卡 |
| `star_costs_x` | boolean | 是否为星星 X 费卡 |
| `energy_cost` | number | 能量消耗（含修正） |
| `star_cost` | number | 星星消耗（含修正） |
| `rules_text` | string | 原始兼容规则文本 |
| `resolved_rules_text` | string | 按当前实例动态变量展开后的规则文本 |
| `dynamic_values` | object[] | 当前实例的动态变量列表 |

#### `shop.card_removal`

`available` 在这里**是**真实字段（上面三张商品表没有它）。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `price` | number | 当前删牌服务价格 |
| `available` | boolean | 当前是否可购买删牌服务 |
| `used` | boolean | 本次商店的删牌服务已用掉（每家商店一次） |
| `enough_gold` | boolean | 还可用**且**金币够 |

### `timeline` 子结构

仅在时间线界面存在。角色解锁走时间线，不走 `GAME_OVER`。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `back_enabled` | boolean | 返回按钮可用 |
| `inspect_open` | boolean | 详情覆盖层打开中 |
| `unlock_screen_open` | boolean | 解锁界面打开中 |
| `tutorial_open` | boolean | 教学覆盖层打开中 |
| `can_choose_epoch` | boolean | 此刻可执行 `choose_timeline_epoch` |
| `can_confirm_overlay` | boolean | 此刻可执行 `confirm_timeline_overlay` |
| `slots` | object[] | 时间线槽位，见下 |

#### `timeline.slots[]`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 槽位下标。**`choose_timeline_epoch` 的 `option_index` 取的就是这个值**，不是数组位置；越界返回 409 `invalid_target` 并带 `option_index_space = "timeline.slots[].index"` |
| `epoch_id` | string | 该槽位的纪元 id |
| `title` | string | 槽位标题 |
| `state` | string | 槽位状态（游戏自身枚举的小写形式） |
| `is_actionable` | boolean | 该槽位此刻可选；不可选的槽位传进去是 409 `invalid_target` |

### `unlock` 子结构

解锁覆盖层。死亡后回主菜单的路径上会连续出现多层，逐层 `confirm_unlock` 直到它变回 null。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `unlock_type` | string | 解锁类型 |
| `items` | string[] | 本层展示的解锁条目 |
| `can_confirm` | boolean | 此刻可执行 `confirm_unlock` |

### `bundles` 子结构

卡包选择。存在时为数组，每个元素是一个卡包。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `index` | number | 卡包下标，`confirm_bundle` 的 `option_index` 取这里 |
| `cards` | object[] | 包内卡牌，字段同 `reward.card_options[]` |

### `capstone` 子结构

决策型覆盖层（设置 / 图鉴 / 反馈这类容器页）的选项集，`choose_capstone_option` 的 `option_index`
取选项在数组里的位置。

**暂停菜单与它的各个页面不属于这里**：它们各自有自己的屏幕名（`PAUSE_MENU` / `SETTINGS` /
`COMPENDIUM` / `CARD_LIBRARY` / `RELIC_COLLECTION` / `POTION_LAB` / `BESTIARY` / `STATS` /
`RUN_HISTORY`），`capstone` 在这些屏上是 `null`，`choose_capstone_option` 返回 409 `invalid_action`。
曾经不是这样——暂停菜单一度被报成 `capstone`，把「放弃本局」当成一个可选项交给模型。

### `modal` 子结构

阻塞弹窗。它盖住的房间仍在下面，所以**先解弹窗再规划房间**。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `type_name` | string | 弹窗类型名（游戏侧类名） |
| `underlying_screen` | string \| null | 弹窗底下的逻辑界面名，用于「先解覆盖层再规划房间」 |
| `can_confirm` | boolean | 此刻可执行 `confirm_modal` |
| `can_dismiss` | boolean | 此刻可执行 `dismiss_modal` |
| `confirm_label` | string \| null | 确认按钮文案 |
| `dismiss_label` | string \| null | 取消按钮文案 |

分页教学弹窗（`NCombatRulesFtue` 共三页）需要**三次** `confirm_modal`：前两次返回 200 `pending`
并附文案 `Tutorial page advanced; the modal is still open. Call confirm_modal again.`，第三次才
`completed`。把第一个 `pending` 当失败会卡在这里。

### `game_over` 子结构

仅在 `GAME_OVER` 屏存在。这一屏分三个阶段，`phase` 说的就是现在在哪个阶段。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `is_victory` | boolean | 本局是否通关 |
| `floor` | number \| null | 结束时的总层数 |
| `character_id` | string \| null | 本局角色 id |
| `phase` | string | `intro`（结算动画尚未播完，可 `continue_game_over` 推进）/ `summary_animating`（两个按钮都不可点，只能等）/ `summary_ready`（结算已写盘，可回主菜单） |
| `can_continue` | boolean | 此刻可执行 `continue_game_over` |
| `can_return_to_main_menu` | boolean | 此刻可执行回主菜单 |
| `showing_summary` | boolean | 结算摘要已显示 |
| `waiting_for_other_players` | boolean | 联机局里在等其他玩家推进 |
| `save_status` | string | 存档写入核对结果：`pending`（尚未到核对时机）/ `verified`（已确认落盘）/ `error`（核对失败，原因见 `save_error`） |
| `save_verified` | boolean | `save_status == "verified"` 的布尔投影 |
| `save_error` | string \| null | 核对失败原因，例如 `progress_profile_not_initialized` / `progress_save_missing` |

`continue_game_over` 会**等到原生结算真的写进 progress 存档**才返回，所以它比其它动作慢；
不要因为一次超时就重复点。判断是否真的收尾完成看 `save_verified`，不要只看屏幕变化。

### compact `agent_view` 的增补字段

默认 MCP `get_game_state` 返回 compact `agent_view`。它不是 `/state` 的字段子集，而是同一份状态
的文本化重写（例如 `combat.player.hp` 是 `"12/70"`、`run.relics` 只有名字），因此下表字段在
compact 里的位置与 `/state` 不同，但同名同源、同为新增键；`/state` 既有字段与 compact 既有键
的形状都没有变化。

compact 的 `combat` 原样携带 `/state` 的 `action_readiness`、`end_turn_will_kill_player` 与
`lethal_risks[]`（这三处不做文本化改写，字段名与类型逐一相同），因此上面 [`combat.action_readiness`](#combataction_readiness)
一节对 MCP `get_game_state` 的返回同样适用——**这是 agent 判断「该等还是该动」的首选字段**。

| compact 位置 | 字段 | 说明 |
| --- | --- | --- |
| `combat.player` | `powers` | 己方 Power 短行：`power_id` + 层数，Debuff 追加 `[debuff]` |
| `combat.enemies[]` | `powers` | 敌方 Power 短行，同上 |
| `combat.enemies[]` | `intents[]` | 怪物下一招的数值拆解：`i`（意图序号）、`intent_type`、`label`、`damage`、`hits`、`total_damage`、`status_card_count` |
| `combat.enemies[]` | `base_max_hp` | 联机缩放前的基础血量；元数据的 `min_hp` / `max_hp` 与它同量纲，实况 `max_hp` 是缩放后的值 |
| `combat.players[]` | `player_id` / `slot_index` / `is_local` / `is_connected` / `character_id` / `character_name` / `current_hp` / `max_hp` / `block` / `energy` / `stars` / `focus` / `is_alive` | 队伍血线（含本地玩家），用于判断队友是否需要救援 |
| `combat.hand[]` | `card_id` | 手牌内部 ID，用于 `get_game_data_item` 精确查询 |
| `combat.draw[]` / `combat.discard[]` / `combat.exhaust[]` / `run.deck[]` / `run.piles.*` | `card_ids` | 合并组代表的卡牌 ID（去重、升序）；组内若含不同 ID 会全部列出，`line` 仍带 `*N` 数量后缀 |
| `selection.cards[]` / `reward.cards[]` / `shop.cards[]` / `bundles[].cards[]` | `card_id` | 选择屏 / 奖励 / 商店 / 卡包的卡牌 ID |
| `run` | `relic_ids` | 与 `run.relics` 同序、等长的遗物 ID 列表（`relics` 保持原有名字数组不变） |
| `run` | `players[]` | 队伍摘要，字段同 `combat.players[]`，另有 `gold` |
| `chest.relics[]` | `relic_id` | 宝箱遗物 ID，配合 `i` 供 `choose_treasure_relic` 使用 |
| `modal` | `underlying_screen` | 覆盖层底下的逻辑界面名，用于「先解覆盖层再规划房间」 |
| 顶层 | `unlock` | 解锁覆盖层快照（`unlock_type` / `items` / `can_confirm`）；无解锁覆盖层时为 `null` |

#### compact 的字段改名对照表

compact 不是 `/state` 的子集，**很多键换了名字**。MCP `get_game_state` 默认返回的就是 compact，
所以按本文档其余部分的 `/state` 名去取 compact 的值会取到 `undefined`。下表是全部改名；
未列出的键与 `/state` 同名。

| compact 位置 | `/state` 字段 | compact 键 |
| --- | --- | --- |
| 所有带下标的数组元素 | `index` | `i` |
| `combat.enemies[]` | `is_alive` / `is_hittable` | `alive` / `hittable` |
| `run` | `character_name` | `character` |
| `run.potions[]` | `can_use` / `can_discard` / `valid_target_indices` | `usable` / `discard` / `targets` |
| `run.piles.*` / `run.deck[]` | 合并组 | `card_ids`（组代表的卡牌 ID，去重升序） |
| `map` | `player_votes` | `votes` |
| `map.votes[]` | `is_local` | `local` |
| `selection` | `min_select` / `max_select` / `selected_count` / `can_confirm` | `min` / `max` / `selected` / `confirm` |
| `shop` | `is_open` | `open` |
| `shop.*[]` | `is_stocked` / `enough_gold` | `stocked` / `affordable` |
| `rest.options[]` | `is_enabled` | `enabled` |
| `chest` | `is_opened` / `has_relic_been_claimed` | `opened` / `claimed` |
| `event` | `event_id` / `is_finished` | `id` / `finished` |
| `event.options[]` | `is_locked` / `is_proceed` / `will_kill_player` | `locked` / `proceed` / `kill` |
| `reward.alternatives[]` | `label` | `line` |
| `character_select` | `can_embark` / `selected_character_id` | `embark` / `selected` |
| `character_select.characters[]` | `is_locked` / `is_selected` | `locked` / `selected` |
| `multiplayer_lobby.*[]` | `name` / `is_locked` / `is_ready` / `is_selected` | `line` / `locked` / `ready` / `selected` |
| `timeline` | `back_enabled` / `tutorial_open` / `can_confirm_overlay` | `back` / `tutorial` / `confirm` |
| `timeline.slots[]` | `is_actionable` | `actionable` |
| `modal` | `type_name` / `can_confirm` / `can_dismiss` | `type` / `confirm` / `dismiss` |
| `game_over` | `is_victory` / `character_id` / `can_return_to_main_menu` | `victory` / `character` / `can_return` |

`timeline.slots[].i` 仍然是 `/state` 里 `timeline.slots[].index` 的那个值——
**`choose_timeline_epoch` 取的就是它，不是数组位置**。同理 `map.options[].i` 是
`map.available_nodes[].index`。

### 状态示例：战斗中

```json
{
  "ok": true,
  "request_id": "req_20260310_120000_1234",
  "data": {
    "state_version": 11,
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
| `requires_coordinates` | boolean | 是否需要 `x` / `y`（目前只有 `crystal_clear_cell` 为 true） |
| `requires_tool` | boolean | 是否需要 `tool`（目前只有 `crystal_set_tool` 为 true） |

每个 descriptor 都会带上全部五个字段，缺省一律为 `false`。

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
        "requires_index": false,
        "requires_coordinates": false,
        "requires_tool": false
      },
      {
        "name": "play_card",
        "requires_target": false,
        "requires_index": true,
        "requires_coordinates": false,
        "requires_tool": false
      }
    ]
  }
}
```

> **注意**：`play_card.requires_target` 固定为 `false`。是否需要目标取决于具体卡牌的 `combat.hand[].requires_target` 字段。

---

## `POST /action`

执行单个游戏动作。

### 动作总表（与代码强一致）

<!-- BEGIN ACTION CONTRACT -->
本区块由 `scripts/check_verification_gates.py` 与 `GameActionService.ExecuteAsync` 的 action switch 做集合比对：代码新增动作而这里没有登记时，预检与 CI 会失败。

- `resolve_rewards` — 奖励结算界面（可带 `option_index`，或 `card_index`）
- `end_turn` — 结束当前战斗回合
- `play_card` — 打出手牌（`card_index` 必填，需要目标时再加 `target_index`）
- `switch_profile` — 切换存档位（`option_index`，1–3）
- `continue_run` — 主菜单继续当前局
- `continue_game_over` — 结算界面继续，等待原生存档写入
- `dismiss_game_over_wait` — 关闭结算等待提示
- `abandon_run` — 放弃当前局
- `save_and_quit` — 保存并退出
- `open_character_select` — 打开角色选择
- `open_timeline` — 打开时间线
- `confirm_unlock` — 确认解锁弹窗
- `close_main_menu_submenu` — 关闭主菜单子菜单（也关闭补丁说明页 `PATCH_NOTES`）
- `choose_timeline_epoch` — 选择时间线纪元（`option_index` = `timeline.slots[].index`）
- `confirm_timeline_overlay` — 确认时间线浮层
- `choose_map_node` — 选择地图节点（`option_index`）
- `collect_rewards_and_proceed` — 领取后继续
- `claim_reward` — 领取单条奖励（`option_index`）
- `choose_reward_card` — 选择奖励卡（`option_index`）
- `skip_reward_cards` — 跳过卡牌奖励
- `select_deck_card` — 选择牌组中的牌（`option_index`）
- `close_cards_view` — 关闭可关闭的看牌屏 `CARDS_VIEW` / `CARD_PILE`（也关闭 `CARD_INSPECT` / `RELIC_INSPECT` 浮层）
- `confirm_selection` — 确认选择
- `proceed` — 推进到下一步
- `open_chest` — 打开宝箱
- `choose_treasure_relic` — 选择宝箱遗物（`option_index`）
- `choose_event_option` — 选择事件选项（`option_index`）
- `crystal_set_tool` — 设置水晶球工具（`tool`：`big` / `small`）
- `crystal_clear_cell` — 清理水晶球格子（`x`、`y`，可选 `tool`：`big` / `small`）
- `choose_capstone_option` — 选择 Capstone 选项（`option_index`）
- `choose_bundle` — 选择卡包（`option_index`）
- `confirm_bundle` — 确认卡包
- `choose_rest_option` — 选择休息点选项（`option_index`，部分多人选项需 `target_index`）
- `open_shop_inventory` — 打开商店库存
- `close_shop_inventory` — 关闭商店库存
- `buy_card` — 购买卡牌（`option_index`）
- `buy_relic` — 购买遗物（`option_index`）
- `buy_potion` — 购买药水（`option_index`）
- `remove_card_at_shop` — 商店删牌
- `select_character` — 选择角色（`option_index`）
- `embark` — 出发
- `unready` — 取消准备
- `host_multiplayer_lobby` — 创建多人房间
- `join_multiplayer_lobby` — 加入多人房间
- `ready_multiplayer_lobby` — 多人准备
- `disconnect_multiplayer_lobby` — 断开多人连接
- `increase_ascension` — 提高进阶等级
- `decrease_ascension` — 降低进阶等级
- `use_potion` — 使用药水（`option_index`，需要目标时再加 `target_index`）
- `discard_potion` — 丢弃药水（`option_index`）
- `run_console_command` — 调试控制台命令，仅在 `STS2_ENABLE_DEBUG_ACTIONS=1` 时注册
- `confirm_modal` — 确认阻塞弹窗
- `dismiss_modal` — 关闭阻塞弹窗
- `return_to_main_menu` — 返回主菜单
- `invite_ai_teammate` — 邀请 AI 队友（拉起第二个游戏实例）。游玩模型已验证时队友自动打；未配置或未验证时队友照常拉起、照常进图，但停在原地等外部接管，见「两条组队路线」。首次调用若双开尚未完成，会立刻返回 200、`status: "pending"`、`stable: false`，不必等队友窗口连上；`message` 是「进行中」语义，不要把可能过期的 `dual_status` 当做成败。双开进行中不再出现在 `available_actions`。
- `continue_ai_teammate` — 继续上次的联机存档并重新拉起 AI 队友。出现在 `available_actions` 的条件：主机（非 companion）主菜单、当前角色没有自动游玩、没有正在进行的双开、且磁盘上有联机存档。读档流程已经启动后失败返回 `continue_failed`。读档前会只读比对存档 `players[].net_id` 与本机 NetId（离线／`-fastmp` 主机即启动参数 `--clientId`，未传为 1）和队友 NetId（主机 id + 1）：任一不匹配返回**不可重试**的 `invalid_action`，以免触发游戏把该存档改名成 `*.VAL.corrupt` 的破坏性读档。首次调用同样可能立刻返回 200 `pending`。
<!-- END ACTION CONTRACT -->

这两个动作共用同一档拆分：游玩模型未验证时，`invite_ai_teammate` 与 `continue_ai_teammate` 照常拉起队友，只是队友不自动开始游玩，等你接管。两条路线各自的前置要求见「两条组队路线」。

### 请求体

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `action` | string | **必填**。动作名称 |
| `card_index` | number \| null | 手牌索引（`play_card` 时使用） |
| `target_index` | number \| null | 目标索引（需要指定目标的卡牌使用） |
| `option_index` | number \| null | 选项索引（地图/奖励/选牌等使用） |
| `x` | number \| null | 水晶球格子的 X 坐标（`crystal_clear_cell`） |
| `y` | number \| null | 水晶球格子的 Y 坐标（`crystal_clear_cell`） |
| `tool` | string \| null | 水晶球工具：`big` 或 `small` |
| `client_context` | object \| null | 可选的客户端上下文（如调用来源标识） |
| `command` | string \| null | 控制台命令（仅 `run_console_command`） |
| `player_id` | string \| null | 多人场景下的行动归属校验；不属于本机角色时返回 `forbidden_actor` |

### 通用响应结构（ActionResponsePayload）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `action` | string | 执行的动作名 |
| `status` | string | `"completed"`、`"pending"` 或 `"failed"` |
| `stable` | boolean | 状态是否已稳定 |
| `message` | string | 人类可读的结果描述 |
| `state` | object | 执行后的最新游戏状态快照（同 `GET /state` 的 `data`） |

---

## `POST /session/control`

启动或暂停**本机角色**的自动游玩。游戏内悬浮窗的「开始自动游玩」按钮走的是同一个入口，脚本与验收流程用它替代鼠标点击。

- 仅接受 loopback 请求，非本机来源返回 403 `local_only`
- 请求体 `{"running": true}` 启动，`{"running": false}` 暂停；字段缺失或不是布尔值返回 400 `invalid_request`
- 在 `companion` 实例上等价于控制该实例自身，见下方 `/companion/control`。`running: true` 与游戏内「继续游玩」、`POST /teammate/control` 是同一道门禁：游玩模型未验证时返回 409 `session_not_ready`，消息就是「测试连接」那条提示；`running: false` 任何时候都可用，暂停不受门禁影响

### 响应示例

```json
{
  "ok": true,
  "request_id": "req_20260911_121549_7955_4",
  "data": {
    "phase": "running",
    "play_running": true,
    "play_phase": "running"
  }
}
```

暂停请求在当轮任务尚未结束时返回 409 `pause_pending`；实例尚未进入可游玩状态时返回 409 `session_not_ready`。返回 `play_running: true` 只表示会话已启动，随后可能因为对局边界、预算或连续决策失败而停止，停止原因读 `/health` 的 `stop_kind`。

### 典型用法

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:8080/session/control' -Method POST `
  -Body '{"running":true}' -ContentType 'application/json'
```

---

## `GET /events/stream`

服务端推送的事件流，用于等待状态变化，而不是反复轮询 `/state`。

- `Content-Type: text/event-stream`，分块传输
 - 心跳：每 15 秒发送一行 SSE 注释（`:` 开头），连接保持打开
 - 建立连接时先发一行 `: stream opened` 注释

### 帧格式

一帧由四部分组成，以空行结束：

```
id: 897
event: combat_started
data: {
data:   "event_id": 897,
data:   "type": "combat_started",
data:   "timestamp_utc": "2026-09-11T12:45:53.9202914Z",
data:   "data": {
data:     "run_id": "C9LRZTK3L1B4",
data:     "turn": 1
data:   }
data: }

```

注意：JSON 是**多行**输出的，同一帧会有多行 `data:`。按 SSE 规范，客户端必须把所有 `data:` 行用换行符拼接后再整体解析，不能只读第一行。`mcp_server/src/sts2_mcp/client.py` 的 `wait_for_event` 就是这样处理的。

帧内容：`id:` 为事件序号，`event:` 为事件类型，`data:` 为完整事件信封，含 `event_id`、`type`、`timestamp_utc` 与事件特有的 `data`。

事件类型：

| 类型 | 触发时机 |
| --- | --- |
| `stream_ready` | 连接建立后立刻补发一次当前状态（`run_id`、`screen`、`in_combat`、`turn`、`action_window_open`），让新客户端不必先轮询 `/state` |
| `session_started` | 会话开始 |
| `screen_changed` | 界面切换 |
| `combat_started` / `combat_ended` | 进入 / 离开战斗 |
| `combat_turn_changed` | 战斗回合变化 |
| `player_action_window_opened` / `player_action_window_closed` | 玩家可操作窗口开 / 关 |
| `route_decision_required` | 地图需要选路 |
| `reward_decision_required` | 奖励需要选择 |
| `event_state_changed` | 事件内部状态变化 |
| `available_actions_changed` | 可用动作集合变化 |

2026-09-11 在隔离副本上实测：45 秒窗口内收到 `: stream opened`、2 次 `: heartbeat` 与 123 行帧内容，事件类型覆盖 `stream_ready`、`screen_changed`（SHOP→COMBAT）、`combat_started`、`player_action_window_opened`、`available_actions_changed`。证据 `build/validation-2026-09-11/sse-frames.jsonl`（gitignore）。

---

## 两条组队路线

「邀请 AI 队友」有两条路线，前置要求不同。走哪条由**游玩模型是否已验证**决定（代码里就是 `FirstRunSetup.Evaluate(settings).ReadyToInvite`），调用方不需要额外参数。

| | AI 自走 | 外部接管 |
| --- | --- | --- |
| 前置要求 | 主菜单、本窗口不是队友实例、当前角色没有自动游玩，**并且**游玩模型已配置、端点合法、`测试连接` 已通过 | 只要前三条结构条件；**模型可以完全没配** |
| 队友进程 | 加入大厅 → 点开局 → 进图后自动出牌 | 加入大厅 → 点开局 → 进图后**停在原地**：子进程拿到 `STS2_AGENT_AUTOPLAY=0`，`/health` 报 `play_phase: "paused"` 与 `session_requests: 0`，从不出牌也从不调用模型 |
| 谁在打 | 队友进程里的模型循环 | 外部 agent 自己调队友实例的 `GET /state` 与 `POST /action` |
| 开始 / 暂停 | 主窗口的「暂停队友 / 继续游玩」，或 `POST /teammate/control` | 同上；之后若补齐并验证了游玩模型，`running: true` 会让队友开始自己打 |
| 怎么识别 | `/health` 的 `companion.auto_play` 为 `true` | 同一字段为 `false` |

两条路线共用同一组结构条件，所以拆分只在模型这一档，不构成绕过：不是队友实例、当前角色没有正在跑的自动游玩、必须在主菜单，缺一条都拉不起来。

外部接管路线下，队友自己的 `POST /action` 一直可用，并且只作用于**它自己的角色**（`CompanionActPolicy`，越界返回 403 `forbidden_actor`）。队友起来后也不会因为没有模型而报错：这条路线根本不调用模型。

```powershell
# 未配置任何模型也能拉起队友；它起来后停在原地等接管
Invoke-RestMethod -Uri 'http://127.0.0.1:8080/action' -Method POST -ContentType 'application/json' `
  -Body '{"action":"invite_ai_teammate"}'

# 找到队友实例的 HTTP API（端口通常不是 8080，以响应为准）
(Invoke-RestMethod -Uri 'http://127.0.0.1:8080/health').data.companion

# 让队友开始 / 暂停自动游玩：走主窗口，不需要队友会话令牌
Invoke-RestMethod -Uri 'http://127.0.0.1:8080/teammate/control' -Method POST -ContentType 'application/json' `
  -Body '{"running":true}'
```

---

## `POST /teammate/control`

主窗口上的受支持队友控制入口：外部 agent 用它开始 / 暂停本次组队的队友实例，**不需要**持有 `X-STS2-Companion-Session`——主窗口自己拿着该令牌，并由它去调队友实例的 `POST /companion/control`。

- 鉴权：仅 loopback（非本机 403 `local_only`），且仅主窗口（在 `companion` 实例上 409 `not_host`）。与其他本机端点同级；令牌不下发给调用方，也不出现在任何响应里。
- 请求体：`{"running": true|false}`，`running` 必须是布尔值
- 响应：`data.phase` 是队友回报的阶段（`running` / `paused` / `stopping`），`data.play_running` / `data.play_phase` 是主窗口视角，`data.companion_auto_play` 是本次组队的路线
- 失败：未组队、队友进程已退出、上一条控制未完成、或队友没有确认时返回 409 `teammate_control_failed`（可重试）
- `running: true` 需要已验证的游玩模型，与游戏内「继续游玩」按钮同一道门禁；未验证时该请求失败，队友保持暂停

### 响应示例

```json
{
  "ok": true,
  "request_id": "req_20260913_101500_0001_7",
  "data": {
    "phase": "paused",
    "play_running": false,
    "play_phase": "paused",
    "companion_auto_play": false
  }
}
```

---

## `POST /companion/control` 与 `POST /companion/message`

AI 队友实例上的受控端点，由宿主进程在本地调用，普通玩家窗口不使用。

- 仅在 `companion` 实例、loopback，且请求头 `X-STS2-Companion-Session` 与实例持有的会话令牌一致时可用；否则返回 403 `companion_session_required`
- 令牌由宿主拉起队友进程时生成，通过环境变量传给队友，只存在于本机双开场景
- `POST /companion/control`：请求体 `{"running": true|false}`，响应 `data.phase`；队友被远程暂停后不会再自行启动
- `POST /companion/message`：请求体 `{"message": "..."}`（1–2000 字符），响应 `data.reply` 为队友的回复

---

## `GET /data/{collection}`

导出游戏元数据集合，供 MCP 侧做卡牌 / 遗物 / 怪物等查询。集合名不区分大小写，未知集合返回 404 `collection_not_found`。

| 集合 | 内容 |
| --- | --- |
| `cards` | 卡牌：id、名称、费用、稀有度、类型、关键词、标签、动态数值变量等 |
| `relics` | 遗物：id、名称、稀有度、描述 |
| `monsters` | 怪物：id、名称、生命范围、意图 |
| `potions` | 药水：id、名称、稀有度、目标类型 |
| `events` | 事件：id、标题、选项 |
| `powers` | 增益 / 减益：id、名称、类型、描述 |
| `characters` | 角色：id、名称、初始牌组、初始遗物、初始药水 |

导出需要读取游戏对象，因此经游戏线程执行；数据规模较大（卡牌集合数百 KB），不适合每次决策都拉取。MCP 侧（`sts2_mcp.client.Sts2Client.get_game_data_collection`）会在进程内缓存。

**元数据是单机量纲。** `monsters` 的 `min_hp` / `max_hp` 是该怪掷出的基础血量范围，而联机对局里游戏会按 `玩家数 × 章节系数`（act 0 为 1.1、act 1 为 1.2、act 2 为 1.2，act 2 的 Boss 房为 1.3）放大后再落到实况敌人身上，所以两者常常对不上。要对齐时看实况 `combat.enemies[].base_max_hp`：它与元数据同量纲，`max_hp` 才是缩放后的值。

MCP 侧的 `get_relevant_game_data` 读取这份元数据时，`item_ids` 可以省略：省略后由当前屏幕决定要查哪些 id（战斗看手牌与敌人、商店看货架、事件看当前事件），并把字段裁剪到该场景需要的子集。屏幕归类到某场景但该场景的载荷在这块屏上没有内容时（例如 `FAKE_MERCHANT` 归为商店却没有 `shop` 载荷），回落到牌库 / 遗物 / 药水这些角色级 id；确实没有该集合的 id 时返回 `{}`，不臆造。要问特定 id 时照常传 `item_ids`。

### 典型用法

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:8080/data/cards' | ConvertTo-Json -Depth 3 -Compress
```

---

## `POST /mcp`（原生 MCP）

进程内 MCP 端点，路径为 `/mcp`（`/mcp/` 等价，路由匹配不区分大小写，也不限制 HTTP 方法；客户端按 Streamable HTTP 语义发 `POST` 即可），与其它路由共用同一个 HTTP 监听端口，默认 `http://127.0.0.1:8080/mcp`。

- 需在游戏内悬浮窗「接入」页勾选开启（settings 的 `mcpEnabled`）；未开启时返回 403 `mcp_disabled`
- 走 MCP Streamable HTTP 语义：请求体为 JSON-RPC，响应为 JSON 或 SSE
- **Origin 策略**：带 `Origin` 头的请求必须与端点 authority 一致（可另带 `Host` 头，同样需匹配），否则 403 `origin_not_allowed`。缺少 `Origin` 的请求放行（原生客户端）。`Origin: null` 一律拒绝
- 该端点使用自己的错误信封（`{ok:false, error:{code,message}}`）与 JSON-RPC 错误码，不套用本文档其余路由的 `request_id` 信封


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

### `resolve_rewards`

一次性推进整个奖励流程：逐个领取可领取的奖励、处理遇到的卡牌奖励选择、最后点击继续。

- **前提**：`screen = "REWARD"`（`reward.rewards[]` 中有可领取项，或已处于卡牌奖励子界面）
- **参数**（两者都可省略）
  - `option_index`：`-1` 跳过卡牌奖励；`0/1/2...` 选择对应位置的卡牌；缺省为自动（第一张）
  - `card_index`：`option_index` 的向后兼容别名，语义相同
- **行为**：与 `collect_rewards_and_proceed` 共用同一套奖励推进流程；显式选择只作用于本次调用遇到的第一处卡牌奖励选择，同一次调用内后续卡牌奖励按自动（第一张）处理
- **稳定条件**：奖励流程结束或界面切换
- **超时**：20 秒
- **重试语义**：显式选择属于**携带它的那一次调用**。若本次调用返回 `pending`，用相同参数重试 `resolve_rewards` 会重新携带该选择；若改用 `collect_rewards_and_proceed` 重试，卡牌奖励按**自动（第一张）**处理，显式选择不会跨调用保留

```json
{
  "action": "resolve_rewards",
  "option_index": 1
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
- **跳过范围**：跳过意图绑定到记录它的那次奖励集合（同一个奖励屏实例），不会影响之后其他奖励集合的卡牌奖励；若无法解析出所属奖励集合，该意图不生效——宁可不跳过（重新出现选牌屏，可恢复），也不静默丢弃奖励
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

在选牌界面选择一张牌。牌库网格的每一次点击都会结算并返回 `completed`；战斗手牌多选只累积当前这一步并返回 `pending`，需要再用 `confirm_selection` 收尾。

- **前提**：`screen = "CARD_SELECTION"`，`selection.cards[]` 非空
- **参数**：`option_index`（必填）：`selection.cards[]` 的索引
- **行为**：按 `selection.kind` 分两种
  - 牌库网格（`deck_card_select`、`deck_upgrade_select`、`deck_transform_select`、`deck_enchant_select`、`choose_card_select`）：点击被原生界面确认后返回 `completed`。**这不代表界面已关闭**：`min_select < max_select` 时界面会保持打开继续收集，读 `selection.selected_count` / `max_select` 决定是否还要继续点；点满或想提前结束时用 `confirm_selection` 收尾（见下）
  - 战斗手牌多选（`combat_hand_select`、`combat_hand_upgrade_select`）：只计入这一步并返回 `pending`，界面保持打开。读 `selection.selected_count` / `max_select` / `requires_confirmation` 判断是否还需要继续选，选完用 `confirm_selection` 结束
  - 已实机验证：删牌、升级（单选）、附魔（0/3，多选）、变化（0/6，多选）、事件多选（2/2）。附魔/变化这类 `min_select < max_select` 的界面每次点击在约 0.15 秒内返回，不再空转超时
- **稳定条件**：牌库网格的点击被原生界面接受（`selected_count` 变化）；战斗手牌多选在 `confirm_selection` 之后离开选牌界面
- **超时**：10 秒

```json
{
  "action": "select_deck_card",
  "option_index": 0
}
```

### `confirm_selection`

结束一次需要确认的选牌。一次调用会走完整段原生流程：需要时先点界面的确认按钮打开预览，再点预览里的确认。

- **前提**：`selection.can_confirm = true`（原生确认按钮可用时才会出现在 available_actions）
- **参数**：无
- **行为**：牌库网格的 `min_select < max_select` 场景（附魔、变化，以及 `min_select` 为 0 的奖励选牌）与战斗手牌多选都用它收尾。选满 `max_select` 时 `select_deck_card` 通常会自行收尾，因此它主要用于**提前结束**
- **稳定条件**：离开选牌界面
- **超时**：10 秒。实机验证：附魔（0/3）与变化（0/6）都在一次调用内完成（约 0.17–0.19 秒），不需要第二次调用

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

### `crystal_set_tool`

切换水晶球占卜工具，并同步游戏画面中大小占卜按钮的 active 状态。

- **前提**：`screen = "CRYSTAL_SPHERE"` 且 `is_finished = false`
- **参数**：`tool`（必填），只能是 `big` 或 `small`

```json
{ "action": "crystal_set_tool", "tool": "small" }
```

### `crystal_clear_cell`

在指定坐标消耗一次占卜。可以在同一请求中原子切换工具，避免“先切工具、后点格子”之间读取到
过期状态。

- **前提**：`screen = "CRYSTAL_SPHERE"` 且 `is_finished = false`
- **参数**：`x`、`y` 必填；`tool` 可选（`big` 或 `small`）
- **稳定条件**：剩余次数实际下降、奖励子屏接管，或最后一次占卜后 `proceed` 可用

```json
{ "action": "crystal_clear_cell", "x": 5, "y": 6, "tool": "big" }
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

### `choose_timeline_epoch`

在时间线界面选择一个纪元。

- **前提**：`timeline.can_choose_epoch = true`（界面里存在状态为 `obtained` / `complete` 的槽位）
- **参数**：`option_index`（必填）：`timeline.slots[].index`，即完整槽位列表中的位置；紧凑视图里同一个位置是 `slots[].i`。执行侧读的是同一份列表，不再套用"只含可操作槽位"的过滤下标
- **行为**：点击该槽位并要求点击后状态稳定
- **错误**：缺 `option_index` 返回 400 `invalid_request`；越界或槽位不可操作（`timeline.slots[].is_actionable = false`）返回 409 `invalid_target`，两种情况都带 `option_index` 与 `option_index_space: "timeline.slots[].index"`（越界另带 `slot_count`，槽位不可操作另带 `slot_state` 与 `is_actionable: false`），且在点击之前抛出
- **稳定条件**：槽位状态不再变化
- **超时**：15 秒

### `close_main_menu_submenu`

关闭主菜单子菜单。

- **前提**：`screen = "MAIN_MENU"` 且子菜单打开（存在可弹出的子菜单栈），或 `screen = "PATCH_NOTES"`
- **参数**：无
- **行为**：子菜单弹出子菜单栈栈顶；补丁说明页 `PATCH_NOTES` 也由这个动作关闭（优先点返回按钮，按钮不可用时调界面自身的 `Close()`）
- **稳定条件**：子菜单栈收起 / 离开补丁说明页；关闭无效时返回 `pending` 而不是 `completed`
- **超时**：10 秒

### `close_cards_view`

关闭可关闭的看牌屏。

- **前提**：当前是可关闭的看牌屏 `CARDS_VIEW` / `CARD_PILE` 且存在可用的返回按钮，或当前是 `CARD_INSPECT` / `RELIC_INSPECT` 浮层
- **参数**：无
- **行为**：`CARDS_VIEW` / `CARD_PILE` 点各自的返回按钮（两屏的按钮节点都是 `BackButton`）；`CARD_INSPECT` / `RELIC_INSPECT` 两个查看浮层也由这个动作关闭（调用浮层自身的 `Close()`）
- **稳定条件**：离开原界面 / 浮层不再是当前界面；`CARDS_VIEW` / `CARD_PILE` 仍为当前屏时返回 `pending` 而不是 `completed`
- **超时**：10 秒

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

### 水晶球占卜

```
1. GET /state                          → screen=CRYSTAL_SPHERE
2. 读取 crystal_sphere.items / hidden_cells，规划不完整揭示坏物品的坐标
3. POST /action { crystal_clear_cell, x, y, tool="big" }
4. 重复 1-3，直到 divinations_left=0
5. 处理可能出现的奖励子屏；回到占卜屏后 POST /action { proceed }
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
| 主菜单时间线入口 | `open_timeline` / `close_main_menu_submenu`（也关闭补丁说明页 `PATCH_NOTES`） | 已实现，待实机验证 |
| 时间线交互 | `timeline` / `choose_timeline_epoch`（索引空间 = `timeline.slots[].index`） / `confirm_timeline_overlay` | 已实现，待实机验证 |
| 查看浮层 | `CARD_INSPECT` / `RELIC_INSPECT`；用 `close_cards_view` 关闭 | 已实现，待实机验证 |
| 假商人事件商店 | `FAKE_MERCHANT`；`open_shop_inventory` 可打开 | 已实现，待实机验证 |
| 角色选择 | `character_select` / `select_character` / `embark` | 已实现，待实机验证 |
| 继续联机存档 | `MULTIPLAYER_LOAD` / `continue_ai_teammate` / `embark` | 已实现，2026-09-12 本地双实例实测通过 |
| 药水系统 | `run.potions[*].can_use` / `use_potion` / `discard_potion` | 已实现，待实机验证 |
| 阻塞弹窗 | `modal` / `confirm_modal` / `dismiss_modal` | 已实现，待实机验证 |
| 游戏结束 | `game_over` / `return_to_main_menu` | 已实现，待实机验证 |

新增字段与动作的详细说明见 [phase-5-full-chain.md](../docs/phase-5-full-chain.md)。

### Debug 动作

`run_console_command` 仅用于开发期调试 / 验证，默认关闭。

- 启用方式：设置环境变量 `STS2_ENABLE_DEBUG_ACTIONS=1`
- 发布建议：不要在正式发布默认配置中启用
- 设计目标：用于本地实机验证提速，不应成为正式游玩 agent 的常规依赖
