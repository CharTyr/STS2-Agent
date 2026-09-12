# 让 GameDataFilter 的场景字段集与真实导出对齐

## 背景

`STS2AIAgent/Agent/GameDataFilter.cs:12-30` 的 `SceneFieldSets` 决定游戏内 Agent 循环
（`AgentLoop.cs:529`）与原生 MCP（`NativeMcpServer.cs:474-479`）拿到的元数据字段。
`ProjectFields`（:125-137）对不存在的字段**静默跳过**，所以写错的字段名不会报错，只会让
模型看不到信息：

| scene/collection | 表里写了但导出根本没有 | 导出有但没选（信息缺失） |
|---|---|---|
| combat/monsters（:17） | **`hp` / `damage` / `block`** | `min_hp` / `max_hp` / `damage_values` / `block_values` |
| shop/potions（:24） | **`target`**（导出是 `target_type`） | `target_type` / `usage` / `pool` |
| combat/cards（:16） | — | `is_x_star_cost` |
| combat/powers（:18） | — | `allow_negative` |
| shop/relics（:23） | — | `pool` / `is_melted` |
| shop/cards（:22） | — | `target` / `is_x_cost` / `star_cost` / `is_x_star_cost` |
| event/events（:28） | — | `type` / `act` |

monsters 三项全错意味着**战斗中最关键的 HP/伤害信息一个都没传下去**。
现有测试抓不到：`AgentLoopTests.cs:8-32` 只断言 combat/cards 的 `name`；Python 侧
`test_game_data_tools.py` 用自引用的字段表做断言（改表即跟改）。

## 目标

1. 修好 C# 场景字段集，使其全部是真实导出字段。
2. 把"导出字段清单"变成**可被测试引用的事实源**，并加一条"表里字段必须存在于导出清单、
   清单字段必须出现在导出代码里"的双向回归测试（旧代码必须变红）。
3. 顺带补上决策真正需要的字段（见上表右列），不引入任何不存在的字段名。

## 验收标准

- [x] `SceneFieldSets` 不再含 `hp`/`damage`/`block`/`target` 这类不存在的名字。
- [x] 新增导出清单常量（可离线编译进测试工程），逐 collection 列出真实字段。
- [x] 测试一：`SceneFieldSets` 的每个 (scene, collection, field) 都存在于导出清单 —— 修复前必须红。
- [x] 测试二：导出清单的每个字段都以字面量形式出现在 `GameDataExportService.cs` 对应的
      `ExportXxx()` 方法体内 —— 反向漂移检测；若某字段确实不是字面量，允许降级为全文扫描并记录原因。
- [x] 测试三：`GameDataFilter.KnownCollections` 与导出清单的 collection 键集一致。
- [x] 现有 C# / MCP 测试全绿；`check_verification_gates.py`、preflight 通过。

## 范围外

- 不改 Python 侧 `server.py:_SCENE_FIELD_SETS`（逐字段核对后**没有**不存在的字段名，无需修改；
  跨语言一致性以 research 文件记录为本轮已知缺口）。
- 不改 `/data/{collection}` 的响应结构，不新增 schema 端点。
- 不改 `ProjectFields` 的静默跳过语义（只是不再喂它错名字）。
