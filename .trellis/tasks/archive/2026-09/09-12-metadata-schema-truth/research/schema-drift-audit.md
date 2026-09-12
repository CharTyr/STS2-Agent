# 证据：两份场景字段表的漂移

## 两侧位置

- C#：`STS2AIAgent/Agent/GameDataFilter.cs:12-33` `SceneFieldSets`，
  消费于 `GameBridge.GetRelevantGameDataJsonAsync`（`GameBridge.cs:130-142`）→
  `AgentLoop`（游戏内）与 `NativeMcpServer`（原生 MCP，默认关闭）。
- Python：`mcp_server/src/sts2_mcp/server.py:150` `_SCENE_FIELD_SETS`，
  消费于 `get_relevant_game_data`（`:758-785`）→ `get_game_data_items_fields`（`:342-369`）→
  `client.get_game_data_collection` → `GET /data/{collection}`。

## 实测差集（调研所得）

| (scene, collection) | C# | Python | 缺口 |
|---|---|---|---|
| `combat.powers` | 6 项（含 `allow_negative`） | 5 项 | C# 多 `allow_negative` |
| `shop.cards` | 11 项 | 6 项 | C# 多 `target`/`is_x_cost`/`star_cost`/`is_x_star_cost`/`keywords` |
| `shop.relics` | 含 `is_melted` | 无 | C# 多 `is_melted` |
| `combat.potions` | 有（上一轮新增） | 无该键 | Python 缺整个组合 |

（其余组合以实现时的实测差集为准；上表来自本轮只读调研。）

## 为什么测试没抓到

- Python 侧 `test_game_data_tools.py:114-172` 的期望值取自 `",".join(_SCENE_FIELD_SETS[...])`，
  **自引用**；`:200-224` 那条唯一的"real schema"断言只覆盖 `events`。
- C# 侧上一轮新增的 `GameDataExportSchemaTests` 只约束 C# 表 ↔ 导出代码，不涉及 Python。
- 全仓没有任何测试同时读两侧（`SceneFieldSetView` 只出现在 C# 与 `AgentLoopTests`）。

## `mcp_server/data/eng/` 的现状（用于 README 说真话）

- 20 个 JSON + README，约 1.2 MiB，`git ls-files mcp_server/data` = 21 个文件（已跟踪）。
- 打包路径：`mcp_server/pyproject.toml:23,26`（wheel/sdist force-include）、
  `scripts/package-release.ps1:148`、`scripts/check_release_package.py:57`（只校验目录存在）。
- **无任何代码读取**（多轮 `rg` 检索未见读取点）；运行时的唯一数据源是 mod 的 HTTP 导出。
- 与导出 schema 的差异举例：`potions.json` 无 `usage`/`target_type`；`relics.json` 无 `is_melted`；
  `cards.json` 多出 `image_url`/`beta_image_url` 等。
- 现有 README 声称它"用于 MCP 服务器的数据查询和 AI 决策支持"——**与代码不符**。
