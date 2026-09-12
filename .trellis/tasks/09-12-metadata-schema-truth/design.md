# 设计：跨语言对照

## 1. 新测试 `mcp_server/tests/test_scene_field_alignment.py`

解析两份源代码文本（不导入 C#，不依赖游戏）：

```python
CSHARP_FILTER = "STS2AIAgent/Agent/GameDataFilter.cs"
# 仓库根解析照 test_legacy_action_coverage.py:20-25 的 marker 探测（不要信 CWD）
```

C# 侧解析策略：定位 `SceneFieldSets` 的初始化块，按 `["scene"] = new Dictionary<string, string[]>`
与 `["collection"] = new[] { "a", "b", ... }` 两层正则提取。
Python 侧直接 `from sts2_mcp.server import _SCENE_FIELD_SETS`。

断言：

- `set(csharp[(scene, coll)]) == set(python[(scene, coll)])`，差集分别打印
  `only_in_csharp` / `only_in_python`；
- 场景集合本身双向相等（防止新增场景只落在一侧）；
- 失败信息里带上"该 collection 的导出字段全集"（来自 `STS2AIAgent/Agent/GameDataExportSchema.cs`），
  便于判断是"漏了字段"还是"写了不存在的字段"。

Python 侧集合名可能是 `_SCENE_FIELD_SETS`（私有），测试直接导入私有名即可（同包内测试）。

## 2. 修漂移

以 C# 为基准补齐 Python：

- `combat.powers` 补 `allow_negative`；
- `shop.cards` 补 `target` / `is_x_cost` / `star_cost` / `is_x_star_cost` / `keywords`；
- `shop.relics` 补 `is_melted`（与 `pool`）；
- 新增 `combat.potions`（与 `shop.potions` 同字段集）；
- 其余 (scene, collection) 以测试输出的差集为准逐条对齐。

注意：Python 侧多出字段只是让 `get_relevant_game_data` 返回更多键（过滤是 `key in item`），
属安全方向；但**仍以两侧相等**为准，避免"另一侧又慢慢长歪"。

## 3. `test_game_data_tools.py` 的自引用断言

把 `test_get_relevant_game_data_uses_scene_fields_for_combat/_for_shop/_for_event` 的期望值
改成显式字面量（与修复后的表一致），再加一条注释指向跨语言测试。
这样"改表"必须同时改两处，且跨语言测试与 C# 侧测试形成三角约束。

## 4. `mcp_server/data/eng/` 的说真话

- `README.md` 改写为三点：① 这些文件是**某一版本游戏的打包快照**；
  ② 运行时的唯一数据源是 mod 的 `GET /data/{collection}`（`client.py:96-97`）；
  ③ 其 schema 与导出 schema **不一致**（举例：`potions.json` 无 `usage`/`target_type`，
  `relics.json` 无 `is_melted`），不可当作字段依据。
- 新测试 `test_packaged_game_data.py`：遍历 `data/eng/*.json`，
  断言 ① 每个文件可解析为 list 或 dict；② 含条目时每条都有非空 `id`；
  ③ 同一文件内 `id` 唯一。失败信息给出文件名与被破坏的条目索引。

## 风险

- 跨语言解析是文本级的：C# 表格式若与预期不同，以实现时读到的真实形态为准，
  并在 evidence 里贴出解析出的 (scene, collection) 数量与抽样。
