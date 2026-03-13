# STS2 MCP Server

`mcp_server/` 提供一个基于 `FastMCP` 的本地 MCP Server，把 `STS2AIAgent` Mod 暴露的 HTTP API 包装成可直接给大模型调用的工具。

它的目标不是“把所有底层按钮都暴露出去”，而是给 agent 一套足够完整、但仍然有状态约束的游玩接口。

## 当前工具

基础状态：

- `health_check`
- `get_game_state`
- `get_available_actions`
- `wait_for_event`
- `wait_until_actionable`

战斗：

- `play_card`
- `end_turn`
- `use_potion`
- `discard_potion`

房间 / 流程推进：

- `continue_run`
- `abandon_run`
- `open_character_select`
- `open_timeline`
- `close_main_menu_submenu`
- `choose_timeline_epoch`
- `confirm_timeline_overlay`
- `select_character`
- `embark`
- `choose_map_node`
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
- `return_to_main_menu`

奖励 / 选牌：

- `claim_reward`
- `choose_reward_card`
- `skip_reward_cards`
- `collect_rewards_and_proceed`
- `select_deck_card`

Modal：

- `confirm_modal`
- `dismiss_modal`

开发期调试：

- `run_console_command`
  - 仅当 `STS2_ENABLE_DEBUG_ACTIONS=1` 时注册
  - 默认关闭
  - 只用于开发和验证，不应成为正式游玩流程的常规依赖

## 事件流回调（SSE）

Mod 侧现在提供 `GET /events/stream`，用于推送阶段变化回调，减少高频轮询。

常见事件：

- `screen_changed`
- `combat_started`
- `combat_ended`
- `combat_turn_changed`
- `player_action_window_opened`
- `player_action_window_closed`
- `route_decision_required`
- `reward_decision_required`
- `event_state_changed`
- `available_actions_changed`

Python 客户端可以直接消费：

```python
from sts2_mcp.client import Sts2Client

client = Sts2Client()

# 持续监听
for evt in client.iter_events():
    print(evt["event"], evt["data"])

# 等待某类事件（例如下一次可操作窗口开启）
evt = client.wait_for_event(
    event_names={"player_action_window_opened", "route_decision_required"},
    timeout=20,
)
print(evt)
```

## 降低模型误调用的建议

这个 MCP 已经不算小，所以真正影响稳定性的，不只是“工具有没有”，还包括“模型是不是按正确节奏调用”。

推荐约束：

1. 会话开始先调 `health_check`。
2. 每次决策前都调 `get_game_state`。
3. 只调用当前 `available_actions` 里出现的动作。
4. 每次动作后重新读取状态，不复用旧索引。
5. 优先用高层动作，不要把可合并流程拆碎。

高层动作优先级：

- 奖励房间优先 `collect_rewards_and_proceed`
- 休息点优先 `choose_rest_option`
- 商店先 `open_shop_inventory`，离开内层库存先 `close_shop_inventory`
- 宝箱必须 `open_chest -> choose_treasure_relic -> proceed`
- `MODAL` 出现时优先 `confirm_modal` / `dismiss_modal`

## 推荐配套 Skill

如果上层 agent 支持 Codex Skill，推荐同时加载：

- [sts2-mcp-player](../skills/sts2-mcp-player/SKILL.md)

这个 skill 会强制 agent 采用“状态优先、按房间推进、只用可用动作”的工作流，能明显减少误调用和索引漂移。

## 费用字段说明

所有主要卡牌 payload 现在都同时暴露：

- `costs_x`
  - 是否为能量 X 费卡
- `star_costs_x`
  - 是否为星星 X 费卡
- `energy_cost`
  - 当前能量消耗，包含战斗中的临时修正
- `star_cost`
  - 当前星星消耗，包含战斗中的临时修正

这很重要，因为 STS2 里有两类容易让模型误判的动态情况：

- 能量费在战斗中被临时改写，例如 `Bullet Time`
- 星星费 / 星星 X 费会随当前星数变化，例如 `Stardust`

## 环境变量

- `STS2_API_BASE_URL`
  - 默认：`http://127.0.0.1:8080`
- `STS2_API_TIMEOUT_SECONDS`
  - 默认：`10`
- `STS2_ENABLE_DEBUG_ACTIONS`
  - 默认：未设置 / `0`
  - 作用：启用开发期 debug 工具，例如 `run_console_command`
  - 发布建议：保持关闭

## 本地启动

```powershell
cd "<repo-root>/mcp_server"
uv sync
uv run sts2-mcp-server
```

默认通过 `stdio` 运行，适合直接接入 MCP 客户端。

## 开发期验证脚本

启动游戏并保持运行：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/start-game-session.ps1" -EnableDebugActions
```

验证 debug 工具默认关闭 / 显式开启：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1"
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1" -EnableDebugActions
```

## 快速自检

只验证 Python 包装层可导入：

```powershell
cd "<repo-root>/mcp_server"
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

在 Mod 已运行时读取状态：

```powershell
cd "<repo-root>/mcp_server"
uv run python -c "from sts2_mcp.client import Sts2Client; import json; print(json.dumps(Sts2Client().get_state(), ensure_ascii=False, indent=2))"
```

## 发布前最低要求

```powershell
dotnet build "<repo-root>/STS2AIAgent/STS2AIAgent.csproj" -c Release
python -m py_compile "<repo-root>/mcp_server/src/sts2_mcp/client.py" "<repo-root>/mcp_server/src/sts2_mcp/server.py"
cd "<repo-root>/mcp_server"
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

完整发布清单见 [release-readiness.md](../docs/release-readiness.md)。
