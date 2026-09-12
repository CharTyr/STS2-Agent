# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `mcp_server/tests/test_scene_field_alignment.py`（新） | 5 条：解析 C# `SceneFieldSets` 与 Python `_SCENE_FIELD_SETS` 按 (scene, collection) 双向比对；另含"Python 字段必须在导出 schema 内"与解析健全性守卫 |
| `mcp_server/tests/test_packaged_game_data.py`（新） | 2 条浅测试：`data/eng/*.json` 可解析；条目 `id` 非空且唯一（用 `object_pairs_hook` 抓重复键）；目录缺失则 skip（不阻碍未来删除决定） |
| `mcp_server/src/sts2_mcp/server.py` | 仅 `_SCENE_FIELD_SETS` 字段集 + 3 行指向跨语言测试的注释（`+24/-0`） |
| `mcp_server/tests/test_game_data_tools.py` | 3 条场景字段断言由自引用改为独立字面量；event 断言补 `type` 并新增 `act` 丢弃断言 |
| `mcp_server/data/README.md` | 重写为三点事实：打包快照 / 运行时唯一数据源是 mod 的 `GET /data/{collection}` / schema 与导出不一致、不可作字段依据 |

## 差集（修复前真实红色输出）

```
- combat.potions   整组缺失（C# 有、Python 无）
- combat/powers    ← allow_negative
- event/events     ← act, type
- shop/cards       ← is_x_cost, is_x_star_cost, keywords, star_cost, target
- shop/potions     ← pool, target_type, usage
- shop/relics      ← is_melted
```

（比调研文档多发现两处：`event/events` 与 `shop/potions`。）修复后差集为 0。

## 验收

| 标准 | 证据 |
|---|---|
| 先红后绿 | 修复前 2 failures；修复后 `Ran 5 tests ... OK` |
| 双向反向检测 | 只在 C# 加 `color` → `only_in_csharp=['color']`；只在 Python 加 → `only_in_python=['color']` |
| 数据浅测试可证伪 | 探针文件缺 `id` → 报条目索引；非法 JSON → 报解析失败；探针已删除 |
| 自引用断言已修 | `test_game_data_tools.py` 三条断言改为独立字面量 |
| 完整性 | 临时反证编辑全部还原（SHA256 与动手前一致：`GameDataFilter.cs 7E18E918…`、`keywords.json 4549E411…`、`potions.json CE3F2424…`） |

## 门禁

MCP 167 OK；`check_verification_gates.py` 5 gate 全过；`check_release_package.py --source-root .` 通过；
preflight 全绿。

## 遗留决策项

`mcp_server/data/eng/`（20 个 JSON、约 1.2 MiB、无代码读取、随包发布）**去留由用户拍板**；
README 已写明其为快照而非数据源，且测试在目录缺失时自动跳过。
