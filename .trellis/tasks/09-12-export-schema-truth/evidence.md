# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Agent/GameDataExportSchema.cs`（新） | 7 个 collection 的真实导出字段清单（逐字抄自 `GameDataExportService.cs` 的 `ExportXxx()`）+ `TryGetFields` |
| `STS2AIAgent/Agent/GameDataFilter.cs` | `SceneFieldSets` 修正：monsters 改 `min_hp/max_hp/moves/damage_values/block_values`；potions 的 `target` → `pool/usage/target_type`；新增 `combat/potions`；补 `is_x_star_cost`/`allow_negative`/`pool`/`is_melted`/`target`/`type`/`act`；新增 internal 只读视图供测试 |
| `STS2AIAgent.Tests/AgentLoopTests.cs` | 追加 3 条测试（+78 行，未改既有测试） |

## 验收

| 标准 | 证据 |
|---|---|
| 修复前红 | `FAIL SceneFieldsExistInTheExportSchema`：`combat/monsters: 'hp'/'damage'/'block' is not exported`、`shop/potions: 'target' is not exported` |
| 修复后绿 | `PASS GameDataExportSchema.SceneFields / ExportCode / KnownCollections` |
| 双向漂移检测 | 清单加 `ghost_field` → `ExportCode` 红；清单删 `characters` → `KnownCollections` 红；`min_hp`→`hp` → `SceneFields` 红 |
| collection 键集一致 | `KnownCollectionsMatchTheExportSchema` PASS |

## 门禁

C# **PASS=311 FAIL=0**；`dotnet build` 0 警告 0 错误；MCP 159 OK；`check_verification_gates.py` 通过；`check_release_package.py` 通过；preflight 通过。

## 只能实机验证 / 已知缺口

- 运行时导出内容未验证（清单是源码静态转写；真实 `/data/{collection}` 返回值需游戏在线）。
- 反向检测只覆盖顶层字段；`vars[]`/`moves[]` 等嵌套子对象的内部字段名不在清单内。
- Python `_SCENE_FIELD_SETS` 与 C# 清单仍无共享事实源（跨语言机器校验属后续任务）。
- 新增 `combat/potions` 的实际收益需运行时确认。

## 备注

录制"修复前红"时使用了仓库外的临时 harness（`%TEMP%\sts2-schema-redcheck`，仅一次性构建产物，未清理成功，与仓库无关）。
