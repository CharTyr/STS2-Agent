# STS2 MCP Server

`mcp_server/` 提供一个基于 `FastMCP` 的本地 MCP Server，把 `STS2AIAgent` Mod 暴露的 HTTP API 包装成 MCP 工具，供大模型直接调用。

---

## 当前工具

基础状态工具：

- `health_check`
- `get_game_state`
- `get_available_actions`

战斗工具：

- `play_card`
- `end_turn`
- `use_potion`
- `discard_potion`

地图与房间推进：

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

奖励与选牌：

- `claim_reward`
- `choose_reward_card`
- `skip_reward_cards`
- `collect_rewards_and_proceed`
- `select_deck_card`

开局与收尾：

- `select_character`
- `embark`
- `confirm_modal`
- `dismiss_modal`
- `return_to_main_menu`

---

## 环境变量

- `STS2_API_BASE_URL`
  - 默认值：`http://127.0.0.1:8080`
- `STS2_API_TIMEOUT_SECONDS`
  - 默认值：`10`

---

## 本地启动

```powershell
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv sync
uv run sts2-mcp-server
```

默认通过 `stdio` 运行，适合直接接入 MCP 客户端。

---

## 快速自检

只验证 Python 包装层是否可导入：

```powershell
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

在 Mod 已运行时读取状态：

```powershell
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv run python -c "from sts2_mcp.client import Sts2Client; import json; print(json.dumps(Sts2Client().get_state(), ensure_ascii=False, indent=2))"
```

---

## 使用约束

1. 先调用 `health_check`，确认游戏和 Mod 已启动。
2. 每次决策前优先调用 `get_game_state`。
3. 调动作时只使用当前 `available_actions` 中暴露的工具。
4. `MODAL` 出现时，优先处理 `confirm_modal` / `dismiss_modal`，不要继续发普通房间动作。
5. `REWARD`、`CARD_SELECTION`、`SHOP`、`REST`、`EVENT` 都是多阶段流程，动作后要重新读状态。

---

## 发布前最低验证

发布前至少确认以下命令通过：

```powershell
dotnet build "C:/Users/chart/Documents/project/sp/STS2AIAgent/STS2AIAgent.csproj"
python -m py_compile "C:/Users/chart/Documents/project/sp/mcp_server/src/sts2_mcp/client.py" "C:/Users/chart/Documents/project/sp/mcp_server/src/sts2_mcp/server.py"
cd "C:/Users/chart/Documents/project/sp/mcp_server"
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

完整发布验收清单见 [release-readiness.md](/C:/Users/chart/Documents/project/sp/docs/release-readiness.md)。
