# STS2 MCP Server

普通玩家一起玩：**不要装本目录**。游戏内 F8 → 自动打 / 邀请队友即可。

外部客户端（Cursor / Claude / Codex）：游戏内 **接入** 页打开 **原生 MCP**，复制页面上的实际地址（与 HTTP API 同一端口，不一定是 8080，更不是 8765）。不需要 Python / uv。

`mcp_server/` 是可选 Python FastMCP sidecar，给开发者做 **stdio**、**layered/full** profile 和契约测试。它把 Mod 的 HTTP API 再包一层。

从仓库或 GitHub 发布包根目录运行 sidecar：`scripts/start-mcp-stdio.ps1`（stdio）或
`scripts/start-mcp-network.ps1`（HTTP）。发布包还带 `scripts/test-mcp-tool-profile.ps1`，
用于不连接游戏地检查 profile 工具表面。

## Tool Profile

`STS2_MCP_TOOL_PROFILE`（或在代码里 `create_server(tool_profile=...)`）选档：

- `guided` — 默认 profile，17 个工具：`health_check`、`get_game_state`、`get_raw_game_state`、
  `get_available_actions`、`get_decision_log`、`get_run_summary`、`get_scene_guidance`、`diff_state`、
  `get_game_data_item`、`get_game_data_items`、`get_relevant_game_data`、`wait_for_event`、
  `wait_until_actionable`、`decide`、`act`、`get_planner_briefing`、`update_play_strategy`。用途见下方「当前工具」表；
  实时清单以 `scripts/test-mcp-tool-profile.ps1` 的 `ESSENTIAL_TOOLS` 为准
- `layered` — 面向主 / 副 Agent 分层编排，在 guided 基础上额外暴露 8 个 handoff / knowledge 工具：
  `get_planner_context`、`create_planner_handoff`、`get_combat_context`、`create_combat_handoff`、
  `complete_combat_handoff`、`append_combat_knowledge`、`append_event_knowledge`、`complete_event_handoff`
- `full` — 包含 layered 工具，另外继续暴露 legacy per-action tools，适合验证和兼容性测试

## 当前工具

下表是 guided 的 17 个工具与一句话用途。**每个工具的逐参数契约不在本文件复述**：以
[src/sts2_mcp/server.py](src/sts2_mcp/server.py) 里它自己的 docstring 与 input schema 为准，那里改了这里不会漂。

| 工具 | 用途 |
|------|------|
| `health_check` | Mod 是否加载且可达 |
| `get_game_state` | compact `agent_view` 状态；`compact_agent_view: false` 表示回退成完整载荷 |
| `get_raw_game_state` | 完整原始 `/state`，查 compact 没有的字段时用 |
| `get_available_actions` | 当前可用动作及其 `requires_index` / `requires_target` / 目标提示 |
| `get_decision_log` | 最近被接受的决策及其理由，最新在最后 |
| `get_run_summary` | 角色、楼层、Act、Boss、HP、金币、能量与牌库 / 遗物 / 药水计数（联机含队伍块） |
| `get_scene_guidance` | 当前屏的策略与 playbook，返回 `screen` / `scene` / `guidance` / `playbook` |
| `diff_state` | 两份 `/state` 的逐路径差异（前后值），`truncated` 标明触顶 |
| `get_game_data_item` | 按 id 查单个游戏元数据条目 |
| `get_game_data_items` | 按逗号分隔的 id 批量查游戏元数据 |
| `get_relevant_game_data` | 按当前场景自动裁剪字段的相关元数据（默认用它） |
| `wait_for_event` | 等一条匹配的 `/events/stream` 事件 |
| `wait_until_actionable` | 等到重新可操作，返回 `matched`（有事件命中）与 `actionable` |
| `decide` | 一次调用给出 `state` + `available_actions` + `scene_guidance`，即一次决策要读的全部内容 |
| `act` | 统一动作入口（动作名 + 参数，附 `reason`）；返回动作之后的 compact `state` |
| `get_planner_briefing` | 双层模式的规划简报：当前策略、双层 / Jev 开关、本局摘要、Jev 最近选择与置信度趋势（转发 `GET /strategy`） |
| `update_play_strategy` | 更新 Jev 遵循的策略：`goal`（一句宏观目标）/ `posture` / `instructions` / 按动作类别的 `option_hints`；省略的字段保留原值 |

layered / full 的额外工具见上文「Tool Profile」；legacy per-action tools 见下方区块。

`get_game_data_item`、`get_game_data_items`、`get_relevant_game_data` 读取的元数据全部来自运行中的 Mod（`GET /data/{collection}`），包内不再附带任何游戏数据快照。

`get_scene_guidance` 与 `decide` 里的策略正文与游戏内循环注入的是同一份 `skills/sts2-mcp-player/references/strategy.md`（外加同一份 `screen-playbooks.md`），两侧的「屏 → 章节」映射与共享键由 `tests/test_scene_guidance_alignment.py` 逐条比对（C# 为准）；`EVENT` 屏的逐选项 `event_id` / `event_options` 只有 sidecar 有，原生 MCP 面只回四个共享键。

<!-- BEGIN LEGACY ACTION TOOLS -->
<!-- The bullets below are the full profile's per-action tools. They are bound to
     _LEGACY_ACTION_TOOLS in src/sts2_mcp/server.py by tests/test_legacy_action_coverage.py,
     so adding or removing a legacy tool without updating both sides fails the MCP tests. -->

战斗：

- `play_card`
- `end_turn`
- `use_potion`
- `discard_potion`

房间 / 流程推进：

- `continue_run`
- `continue_game_over`
- `abandon_run`
- `save_and_quit`
- `open_character_select`
- `open_timeline`
- `close_main_menu_submenu`
- `choose_timeline_epoch`
- `confirm_timeline_overlay`
- `select_character`
- `embark`
- `unready`
- `increase_ascension`
- `decrease_ascension`
- `switch_profile`
- `choose_map_node`
- `proceed`
- `dismiss_game_over_wait`
- `confirm_unlock`
- `close_cards_view`
- `open_chest`
- `choose_treasure_relic`
- `choose_event_option`
- `crystal_set_tool`
- `crystal_clear_cell`
- `choose_rest_option`
- `open_shop_inventory`
- `close_shop_inventory`
- `buy_card`
- `buy_relic`
- `buy_potion`
- `remove_card_at_shop`
- `return_to_main_menu`

多人 / 组队：

- `host_multiplayer_lobby`
- `join_multiplayer_lobby`
- `ready_multiplayer_lobby`
- `disconnect_multiplayer_lobby`
- `invite_ai_teammate`
- `continue_ai_teammate`

奖励 / 选牌：

- `claim_reward`
- `choose_reward_card`
- `skip_reward_cards`
- `collect_rewards_and_proceed`
- `resolve_rewards`
  - 可省略 `option_index`（默认取第一张奖励牌），也可改用向后兼容的 `card_index` 别名
- `select_deck_card`
- `confirm_selection`
- `choose_capstone_option`
- `choose_bundle`
- `confirm_bundle`

Modal：

- `confirm_modal`
- `dismiss_modal`
<!-- END LEGACY ACTION TOOLS -->

开发期调试（不受 profile 影响，只在 `STS2_ENABLE_DEBUG_ACTIONS=1` 时注册，默认关闭）：

- `run_console_command` — 跑一条游戏 dev-console 命令，只用于开发和验证
- `inject_event_churn` — 发 `option_index` 条 `debug_churn` 合成事件，把 `/events/stream` 慢订阅者的队列顶满，
  验证「满队列关闭该订阅者」而不是静默丢事件；`option_index` 必须大于单订阅者队列容量（256），否则 Mod 返回 400 `invalid_request`

参数细节见两者各自的 docstring。

## 使用要点

这个 MCP 已经不算小，所以真正影响稳定性的，不只是“工具有没有”，还包括“模型是不是按正确节奏调用”：

1. 会话开始先调 `health_check`。
2. 每次决策前读一次状态：`decide`（一次拿到状态、动作与指引）或 `get_game_state`。
3. 只调用当前 `available_actions` 里出现的动作；`act` 附一条简短的 `reason`，供玩家界面与决策日志解释本步选择。
4. `act` 返回的 `state` 就是下一个决策的输入，不必再读一次；只有 `pending` / 屏幕变化时才重读，要完整载荷才传 `raw_state=True`（一次约 4,000–9,500 token）。
5. `wait_until_actionable` 的 `state` 与 `act` / `get_game_state` 同形（compact），同样支持 `raw_state=True`：跨动画等待不需要完整载荷。
6. 索引被拒时错误对象带 `field` / `submitted` / `valid_indices` / `valid_field`：按它改正索引，而不是重发同一个值。
7. 优先用高层动作，不要把可合并流程拆碎（优先级见下）。

**`outcome_unknown`（动作响应丢失）**：`status` 为 `outcome_unknown` 时，动作可能已经执行，只是响应没回来。
客户端只做**一次** `/state` 对齐，读到的状态放在 `reconciliation.state`（compact，带 `compact_agent_view` 标记）；
`reconciliation.state_read` / `action_effect_compared`（恒为 `false`）/ `action_outcome`（恒为 `"unknown"`）说明
「读了状态、但没有把动作效果与它比对」，`succeeded` / `status` 描述的是那次读取而不是动作本身。
**绝不要自动重放该动作**：先看对齐到的状态，再决定下一步。

`get_game_state` 默认回 compact `agent_view`；Mod 未暴露 `agent_view` 时回退完整 `/state` 并带 `compact_agent_view: false`，那是降级信号而不是常规契约。compact 里一批键改过名（商店打开标志在 compact 里是 `shop.open`，raw state 里是 `shop.is_open`），读 compact 前先看 `docs/api.md` 的「compact 的字段改名对照表」。

`guided` / `layered` 用统一 `act` 时，水晶球动作额外接受 `x`、`y`、`tool`：`crystal_clear_cell` 必须传坐标（可同时传 `tool="big"|"small"`），`crystal_set_tool` 只传 `tool`；完整棋盘来自 `get_game_state().crystal_sphere`。

高层动作优先级：奖励房 `collect_rewards_and_proceed`；休息点 `choose_rest_option`；商店先 `open_shop_inventory`、离开内层库存先 `close_shop_inventory`；宝箱 `open_chest -> choose_treasure_relic -> proceed`；`MODAL` 优先 `confirm_modal` / `dismiss_modal`。

## 推荐配套 Skill

用外部 AI Agent 经 MCP 操作本 Mod 时，请同时加载 [sts2-mcp-player](../skills/sts2-mcp-player/SKILL.md)：游戏内自动游玩就是按这份合同决策的。只接 MCP 工具、不加载 skill 的客户端也能点合法动作，但达不到游戏内自动游玩的效果。

## 费用字段说明

卡牌 payload 同时暴露 `costs_x` / `star_costs_x`（是否 X 费卡）与 `energy_cost` / `star_cost`（当前消耗，含战斗中的临时修正），因为 STS2 里这两类费用会被动态改写：能量费如 `Bullet Time`，星星费 / 星星 X 费如 `Stardust`。按静态费用表判断会误判。

## 环境变量

| 变量 | 默认 | 作用 |
|------|------|------|
| `STS2_API_BASE_URL` | `http://127.0.0.1:8080` | Mod API 地址 |
| `STS2_AGENT_REPO_ROOT` | 自动探测（从 `sts2_mcp` 包位置向上找含 `mcp_server/pyproject.toml` 的目录） | 定位仓库根与 `reference_files` 的 `docs/game-knowledge/*.md`；wheel / pipx 安装态探测不到时返回空列表而不是猜路径 |
| `STS2_AGENT_KNOWLEDGE_DIR` | 仓库根下的 `agent_knowledge/`；不在检出内时回退当前工作目录并打 WARNING | 保存 combat / event 的运行时知识文件（不写进 Python 安装目录） |
| `STS2_API_READ_TIMEOUT` | `10`（秒） | `GET` 请求与状态对账的读取超时 |
| `STS2_API_ACTION_TIMEOUT` | `75`（秒） | `POST /action` 的读取超时；动作要等游戏稳定后才返回（`continue_game_over` 最多等 60 秒），必须明显长于读取超时 |
| `STS2_API_MAX_RETRIES` | `2` | 可重试读取类请求的重试次数；动作请求从不自动重放 |
| `STS2_ENABLE_DEBUG_ACTIONS` | 未设置 / `0` | 启用开发期 debug 工具（`run_console_command`、`inject_event_churn`）；发布建议保持关闭 |

## 运行时知识库

`layered` / `full` profile 会按稳定 id 自动维护一个简单知识库：

```text
agent_knowledge/
  combat/global/solo/cultist_x1.md
  combat/global/groups/cultist_x2+slime_large_x1.md
  events/global/cleric.md
```

- 战斗文件按 `enemy_id_xcount` 聚合并排序，不依赖本地化名字；事件文件按 `event_id` 命名
- 还没有 chapter 字段时目录先落在 `global/`；追加内容自动带上 `run_id`、`floor`、`screen`、UTC 时间戳
- 不在仓库检出内时（wheel / pipx 安装态）不再猜路径：知识库落到当前工作目录的 `agent_knowledge/`，可用 `STS2_AGENT_KNOWLEDGE_DIR` 固定

`get_planner_context` / `get_combat_context` 返回的 `reference_files` 是离线知识库的入口，键就是状态里对应的 id 字段（映射见 `docs/game-knowledge/agent-reference.md`）：`cards`（`card_id`）、`monsters` / `monster_behaviors`（`enemy_id`）、`potions`（`potion_id`）、`events`（`event_id`）、`relics`（`relic_id`）、`powers`（`power_id`）、`characters`、`playbook`。

## 主 / 副 Agent 交接

主 Agent 负责路线和房间决策、副 Agent 专管战斗时，推荐这样接：

1. 主 Agent 每次非战斗决策前调 `create_planner_handoff`；`screen=COMBAT` 时改调 `create_combat_handoff`，把返回包整体交给战斗 Agent。
2. 战斗 Agent 战斗结束后调 `complete_combat_handoff`，主 Agent 把 `planner_summary` 当作上一场战斗的压缩记忆，再继续下一次 `create_planner_handoff`。
3. 事件同理：按 `create_planner_handoff` 里的 `event` 和 `event_knowledge` 决策，结算后调 `complete_event_handoff` 写回结果。

## 本地启动

```powershell
cd "<repo-root>/mcp_server"
uv sync
uv run sts2-mcp-server
```

默认通过 `stdio` 运行，适合直接接入 MCP 客户端。

## 本地验证与发布前检查

单元测试与导入自检（标准库 unittest，不需要游戏；CI 执行的就是同一条命令）：

```powershell
cd "<repo-root>/mcp_server"
uv run --locked python -m unittest discover -s tests -v
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

Mod 在线时读一次状态，验证 HTTP 通路：

```powershell
cd "<repo-root>/mcp_server"
uv run python -c "from sts2_mcp.client import Sts2Client; import json; print(json.dumps(Sts2Client().get_state(), ensure_ascii=False, indent=2))"
```

发布前：

```powershell
dotnet build "<repo-root>/STS2AIAgent/STS2AIAgent.csproj" -c Release
python -m py_compile "<repo-root>/mcp_server/src/sts2_mcp/client.py" "<repo-root>/mcp_server/src/sts2_mcp/server.py"
```

源码仓库还能跑实机脚本：`scripts/start-game-session.ps1 -EnableDebugActions` 启动游戏，
`scripts/test-debug-console-gating.ps1`（加 `-EnableDebugActions` 跑开启态）验证 debug 工具门控。
发布包只带 sidecar 的启动 / profile 检查脚本，不要在发布包里找这些实机脚本。

发布流程入口见 [release-readiness.md](../docs/release-readiness.md)；当前版本、验收证据与剩余缺口见 [当前状态页](https://github.com/CharTyr/STS2-Agent/blob/main/PRODUCT_PLAN_CURRENT.md)。
