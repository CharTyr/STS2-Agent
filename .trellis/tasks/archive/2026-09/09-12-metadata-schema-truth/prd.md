# 让场景字段表成为跨语言的一致事实

## 背景

同一份"场景 → 该查哪些字段"的知识，在两条消费链上各写了一遍，**已经漂移**：

| 面 | 位置 | 现状 |
|---|---|---|
| C#（游戏内 Agent + native MCP） | `STS2AIAgent/Agent/GameDataFilter.cs:12-33` 的 `SceneFieldSets` | 已对齐导出 schema（上一轮修的），并有双向漂移测试 |
| Python（外部 MCP，默认路径） | `mcp_server/src/sts2_mcp/server.py:150` 的 `_SCENE_FIELD_SETS` | 与 C# 表实测不一致：`combat.powers` 5 项 vs C# 6 项（缺 `allow_negative`）；`shop.cards` 6 项 vs C# 11 项；`shop.relics` 缺 `is_melted`；C# 新增的 `combat/potions` 对 Python 未知 |

更麻烦的是两边测试的自证方式：

- C# 侧（`AgentLoopTests.cs:895-970` 的 `GameDataExportSchemaTests`）拿 `GameDataExportSchema` 做双向比对，**是真对照**；
- Python 侧（`mcp_server/tests/test_game_data_tools.py:114-172`）的断言值来自
  `",".join(_SCENE_FIELD_SETS[...])`——**自引用**，改表即跟改，抓不到任何拼写或遗漏。

于是"同一个动作返回不同字段"这种跨语言不一致，永远不会有人发现。

## 目标

1. 在 `mcp_server/tests/` 增加一条**跨语言对齐测试**：解析 C# `GameDataFilter.SceneFieldSets`
   与 Python `_SCENE_FIELD_SETS`，按 (scene, collection) 做双向集合断言，并给出差集明细。
2. 修掉当前漂移（以 C# 表为基准，因为它是与本轮已验证的导出 schema 对齐的那一份）。
3. 修掉 `mcp_server/tests/test_game_data_tools.py` 的自引用断言：至少让"场景字段表"的
   断言不再从被断言的表本身取值，而是引用一个独立的期望值（可以是场景字段表的快照副本，
   但必须与 C# 对齐测试形成交叉约束）。
4. `mcp_server/data/eng/README.md` 说真话：它是**打包快照**，不是运行时数据源，
   其 schema 与 `GET /data/{collection}` 的导出 schema 并不一致（缺 `usage`/`target_type`/`is_melted` 等）。
   同时加一条浅测试（每个 JSON 可解析、条目有唯一 `id`），保证这份数据不会静默损坏。

## 验收标准

- [x] 新的跨语言测试在**修复前必须红**（记录真实红色输出，列出每个 (scene, collection) 的差集）。
- [x] 修复后两侧集合相等；测试对"只在 C# 加字段"与"只在 Python 加字段"两个方向都变红。
- [x] `test_game_data_tools.py` 的场景字段断言不再自引用（改为独立期望 + 跨语言交叉约束）。
- [x] `mcp_server/data/eng/README.md` 明确写出"快照、非运行时数据源、schema 与导出不一致"三件事。
- [x] 新增的数据浅测试对"删掉某个 JSON 的 id 字段"或"写入非法 JSON"变红。
- [x] `uv run --locked python -m unittest discover -s tests` 全绿且计数增加；
      `check_verification_gates.py`、`check_release_package.py --source-root .` 通过。

## 范围外

- 不改 `GameDataFilter`（C# 侧已对齐，且有自己的双向测试）。
- 不改导出端点或导出字段。
- **不删** `mcp_server/data/eng/`（1.2 MiB 的已跟踪数据，删除是不可逆决定，留给用户拍板；
  本任务只让它不再误导人）。
