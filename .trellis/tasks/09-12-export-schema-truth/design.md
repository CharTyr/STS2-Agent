# 设计：导出清单 + 双向漂移测试

## 真实导出字段（来自 `GameDataExportService.cs`）

| collection | 行 | 字段 |
|---|---|---|
| cards | :42-67 | id,name,description,description_raw,type,rarity,target,cost,is_x_cost,star_cost,is_x_star_cost,color,damage,block,keywords,tags,vars,upgrade |
| relics | :79-84 | id,name,description,rarity,pool,is_melted |
| potions | :95-101 | id,name,description,rarity,pool,usage,target_type |
| events | :112-117 | id,name,type,act,description,options |
| powers | :128-133 | id,name,description,type,stack_type,allow_negative |
| characters | :144-155 | id,name,description,starting_hp,starting_gold,max_energy,orb_slots,gender,color,starting_deck,starting_relics,starting_potions |
| monsters | :166-173 | id,name,type,min_hp,max_hp,moves,damage_values,block_values |

## 落点

### 1. 新文件 `STS2AIAgent/Agent/GameDataExportSchema.cs`（纯 BCL）

```csharp
internal static class GameDataExportSchema
{
    public static readonly IReadOnlyDictionary<string, string[]> Collections; // 上表逐字照抄
    public static bool TryGetFields(string collection, out IReadOnlyList<string> fields);
}
```

注释写明：**这是 `GET /data/{collection}` 的字段契约**，改动导出必须同步这里，测试二会兜住反向漂移。
加入 `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` 的 `<Compile Include>`。

### 2. `STS2AIAgent/Agent/GameDataFilter.cs`

- `combat/cards` 补 `is_x_star_cost`；`combat/monsters` 改为
  `id,name,type,min_hp,max_hp,moves,damage_values,block_values`；`combat/powers` 补 `allow_negative`。
- `shop/cards` 补 `target,is_x_cost,star_cost,is_x_star_cost`；`shop/relics` 补 `pool,is_melted`；
  `shop/potions` 改为 `id,name,description,rarity,pool,usage,target_type`。
- 新增 `combat/potions`（与 shop/potions 同字段集）：战斗中用 potion 是常见路径，现在会回退成整条。
- `event/events` 补 `type,act`。
- 暴露只读视图供测试：`internal static IReadOnlyDictionary<string, Dictionary<string, string[]>> SceneFieldSetView => SceneFieldSets;`
  （或把字段改成 `internal static readonly`；不要 public，别扩大 API 面）。

### 3. 测试（`STS2AIAgent.Tests/AgentLoopTests.cs` 的 `GameDataFilterTests` 内新增）

- `SceneFieldsExistInExportSchema`：遍历 `SceneFieldSetView`，每个字段必须 ∈ `GameDataExportSchema`
  对应 collection；收集全部违规字段后一次性报错（便于修复）。**修复前必须红**（`hp`/`damage`/`block`/`target`）。
- `ExportSchemaAppearsInExportCode`：对每个 collection，取
  `AgentSourceFixture.MethodBody("Game/GameDataExportService.cs", "ExportMonsters")` 之类的方法体，
  断言每个字段名以 `\b<field>\s*=` 出现（C# 匿名对象属性名写法）。如遇非字面量字段，
  降级到全文扫描并在实现注释里写明原因。
- `KnownCollectionsMatchExportSchema`：`GameDataFilter.KnownCollections` 与清单键集集合相等。
- 断言自检：把 `monsters` 的 `min_hp` 改回 `hp`，测试必须变红。

## 已知缺口（写进 evidence）

Python 侧字段表与 C# 清单没有共享事实源；本轮靠两侧各自的测试与人工核对保证一致。
若要机器校验，需要新端点（`/data/{collection}/schema`）或仓库级共享文件，属后续任务。
