# STS2 AI Agent Mod - 逆向工程笔记

## 目的

记录已确认的游戏运行时事实、后续逆向目标和待验证问题，确保实现阶段建立在真实类型与真实入口之上。

更新时间：2026-03-10

---

## 已确认事实

### 游戏与程序集

- 游戏目录：`C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2`
- 程序集目录：`C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64`
- 主程序集：`sts2.dll`
- 版本信息来源：
  - `release_info.json`
  - `sts2.runtimeconfig.json`

### 已确认运行时信息

| 项目 | 值 |
|------|----|
| 游戏版本 | `v0.98.2` |
| 发布提交 | `f4eeecc6` |
| 发布日期 | `2026-03-06T15:52:37-08:00` |
| 目标框架 | `net9.0` |
| 内置运行时 | `Microsoft.NETCore.App 9.0.7` |
| Harmony | `0Harmony.dll 2.4.2.0` |
| Godot C# 绑定 | `GodotSharp.dll 4.5.1.0` |

### 已确认实现层面的推论

- 游戏不是 Unity + BepInEx 方案，而是 Godot 4.5.1 + C# 运行时
- 使用 Harmony patch 是可行路线，因为游戏目录内已有 `0Harmony.dll`
- 使用本地 HTTP API 也是可行路线，因为游戏目录内已有 `System.Net.HttpListener.dll`
- Mod 的准确加载机制仍需通过实际示例或反编译入口确认

---

## 正式反编译已确认的关键事实

以下内容来自 `ilspycmd` 对 `sts2.dll` 的正式反编译，可信度高于之前的字符串扫描。

### Mod 加载机制

| 事实 | 说明 |
|------|------|
| 扫描目录 | `Path.Combine(directoryName, "mods")` |
| 扫描方式 | 递归遍历目录，寻找 `.pck` 文件 |
| DLL 位置 | 与 `.pck` 同目录、同名，如 `STS2AIAgent.pck` 对应 `STS2AIAgent.dll` |
| 包内要求 | 必须包含 `res://mod_manifest.json` |
| 清单字段 | `pck_name`、`name`、`author`、`description`、`version` |
| 名称约束 | `mod_manifest.json` 中的 `pck_name` 必须和 `.pck` 文件名一致 |
| 加载方式 | `ProjectSettings.LoadResourcePack(...)` 加载 `.pck` |
| 程序集装载 | 先通过 `AssemblyLoadContext.LoadFromAssemblyPath(...)` 加载同名 `.dll`，再加载 `.pck` |
| 用户门槛 | `SaveManager.Instance.SettingsSave.ModSettings?.PlayerAgreedToModLoading` 必须为 `true`，否则跳过加载 |

### Mod 设置持久化

| 事实 | 说明 |
|------|------|
| 设置文件 | `%APPDATA%/SlayTheSpire2/default/1/settings.save` |
| 文件格式 | 明文 JSON |
| 对应字段 | `mod_settings.mods_enabled` |
| 对应属性 | `ModSettings.PlayerAgreedToModLoading` |

### Mod 初始化机制

| 事实 | 说明 |
|------|------|
| 属性类型 | `MegaCrit.Sts2.Core.Modding.ModInitializerAttribute` |
| 作用目标 | 加在类上，不是加在方法上 |
| 传入参数 | 初始化方法名字符串 |
| 方法要求 | `static`，允许 `public` 或 `non-public` |
| 回退路径 | 如果程序集里没有 `ModInitializerAttribute`，则对整个程序集执行 `Harmony.PatchAll(assembly)` |

### 战斗动作机制

| 事实 | 说明 |
|------|------|
| 最小出牌入口 | `CardModel.TryManualPlay(Creature? target)` |
| 实际入队 | `CardModel.EnqueueManualPlay(Creature? target)` |
| 入队实现 | `RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(this, target))` |
| 状态稳定候选点 | `CombatManager.WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction()` |
| 等待机制 | 监听 `ActionExecutor.AfterActionExecuted`，直到没有 ready 的 player-driven action |

### 地图与奖励推进

| 事实 | 说明 |
|------|------|
| 地图动作类 | `MoveToMapCoordAction(Player player, MapCoord destination)` |
| 地图推进方式 | 非测试模式下调用 `NMapScreen.Instance.TravelToMapCoord(_destination)` |
| 奖励后推进 | `RunManager.ProceedFromTerminalRewardsScreen()` |
| 奖励后行为 | 打开地图，或在战斗事件中恢复上一个房间 |

### 当前 screen 来源

`NetScreenType` 更像多人同步屏幕类型，不足以覆盖完整单机逻辑。  
当前更适合作为 `/state.screen` 识别入口的是：

- `MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext`
- `ActiveScreenContext.GetCurrentScreen()`

该方法会按顺序检查：

1. `FeedbackScreen`
2. `OpenModal`
3. 检视卡牌/遗物屏幕
4. 主菜单及子菜单
5. `NMapScreen`
6. `NOverlayStack`
7. `EventRoom`
8. `CombatRoom`
9. `TreasureRoom`
10. `RestSiteRoom`
11. `MapRoom`
12. `MerchantRoom`

这说明 `/state.screen` 最好建立在 `ActiveScreenContext + NRun` 房间对象上，而不是只靠一个简化枚举。

### 已完成运行验证

- 游戏日志已确认识别 `mods/STS2AIAgent.pck`
- 在设置未放行前，会记录：
  - `Skipping loading mod STS2AIAgent.pck, user has not yet seen the mods warning`
- 将 `settings.save` 中的 `mod_settings.mods_enabled` 置为 `true` 后，日志确认出现：
  - Mod 程序集加载
  - 初始化方法调用
  - `STS2AIAgent` HTTP 服务启动
- 已在真实游戏进程中收到 `/health` 响应

---

## 当前已发现的真实符号

以下内容来自对 `sts2.dll` 的字符串扫描，虽然不等同于正式反编译，但已经足以证明一批关键命名和入口确实存在。

### Modding 相关

| 符号 | 说明 |
|------|------|
| `MegaCrit.Sts2.Core.Modding.ModManifest` | 已确认存在 Mod 清单类型 |
| `MegaCrit.Sts2.Core.Modding.ModManager` | 已确认存在 Mod 管理器 |
| `ModInitializerAttribute` | 已确认存在 Mod 初始化属性 |
| `CallModInitializer` | 已确认存在调用初始化逻辑 |
| `MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen.NModdingScreen` | 已确认存在 Modding UI |

### 战斗与动作相关

| 符号 | 说明 |
|------|------|
| `MegaCrit.Sts2.Core.Combat.CombatManager` | 已确认真实命名空间在 `Core.Combat` |
| `MegaCrit.Sts2.Core.Runs.RunManager` | 已确认真实命名空间在 `Core.Runs` |
| `MegaCrit.Sts2.Core.Entities.Creatures.Creature` | 已确认生物基类存在 |
| `EnqueueManualPlay` | 已确认存在手动出牌入口名 |
| `TryPlayCard` | 已确认存在出牌相关逻辑 |
| `UsePotion` | 已确认存在药水使用逻辑 |
| `MegaCrit.Sts2.Core.GameActions.PlayCardAction` | 已确认存在出牌动作对象 |
| `MegaCrit.Sts2.Core.GameActions.UsePotionAction` | 已确认存在药水动作对象 |
| `ActionQueue` | 已确认存在动作队列 |
| `ActionQueueSynchronizer` | 已确认存在动作队列同步器 |
| `WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction` | 很可能是“状态稳定”候选等待点 |

### 场景与节点相关

| 符号 | 说明 |
|------|------|
| `MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen` | 地图屏幕节点 |
| `MegaCrit.Sts2.Core.Nodes.Rooms.NMapRoom` | 地图房间节点 |
| `MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom` | 事件房间节点 |
| `MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom` | 商店房间节点 |
| `MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom` | 休息点房间节点 |
| `MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom` | 宝箱房间节点 |
| `MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen` | 奖励屏幕节点 |
| `MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton` | 奖励按钮节点 |
| `MegaCrit.Sts2.Core.Nodes.Combat.NIntent` | 战斗意图 UI 节点 |

### 流程候选点

| 符号 | 说明 |
|------|------|
| `RunManager.<EnterMapPointInternal>d__180` | 地图推进候选入口 |
| `RunManager.<ProceedFromTerminalRewardsScreen>d__198` | 奖励结束后继续流程候选入口 |
| `RunManager.<EnterRoomInternal>d__190` | 房间进入候选入口 |
| `CombatManager.<StartTurn>d__92` | 玩家回合开始候选点 |
| `CombatManager.<DoTurnEnd>d__116` | 回合结束候选点 |
| `CombatManager.<EndCombatInternal>d__108` | 战斗结束候选点 |

### 备注

- `CombatManager` 的真实命名空间与原计划中的 `MegaCrit.Sts2.Core` 不完全一致，当前扫描结果更支持 `MegaCrit.Sts2.Core.Combat.CombatManager`
- 这说明正式反编译前，不能继续把旧需求文档里的类型名当成事实
- 当前最值得优先验证的四个符号是：
  - `ModInitializerAttribute`
  - `CallModInitializer`
  - `EnqueueManualPlay`
  - `WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction`

---

## 当前阻塞

虽然已经完成第一轮正式反编译，但以下内容仍需继续确认：

- 最小可运行 Mod 的完整目录结构在更复杂资源场景下是否仍成立
- 非战斗场景的动作执行入口
- 休息点、商店、事件各自对应的具体执行方法
- 玩家、敌人、牌堆状态提取的最短访问链

---

## 逆向目标清单

### 一级目标

这些对象是最小纵切必须确认的：

| 目标 | 用途 |
|------|------|
| `RunManager` | 运行状态、动作队列、全局上下文 |
| `CombatManager` | 战斗开始、战斗状态入口 |
| `Player` | 玩家状态、资源、牌组 |
| `Creature` | 敌人与玩家共享属性 |
| `CardModel` | 出牌入口与卡牌元数据 |
| `ModelDb` | 模型数据库与 ID 解析 |
| screen 枚举或场景对象 | `/state.screen` 的真实来源 |

### 二级目标

这些对象是主线推进和 Phase 4 所需：

| 目标 | 用途 |
|------|------|
| 地图节点类型 | 地图导航 |
| 奖励界面对象 | 奖励状态与选择 |
| 事件对象 | 事件选项 |
| 商店对象 | 商店商品与购买行为 |
| 休息点对象 | 休息、升级等操作 |
| 怪物 Intent 类型 | 战斗意图与伤害预览 |

---

## 计划输出格式

正式反编译后，本文件至少补齐以下内容：

### 类型索引表

| 逻辑名称 | 真实类型名 | 命名空间 | 访问方式 | 备注 |
|----------|------------|----------|----------|------|
| 运行管理 | 待补充 | 待补充 | 待补充 | 待补充 |

### 关键入口表

| 逻辑动作 | 类型 | 方法/字段 | 说明 |
|----------|------|-----------|------|
| 获取当前运行 | 待补充 | 待补充 | 待补充 |

### Patch 候选点

| 位置 | 目的 | 类型 |
|------|------|------|
| `CombatManager.SetUpCombat` | 缓存战斗引用 | Postfix 候选 |

---

## 建议的反编译步骤

1. 执行以下命令：

```powershell
ilspycmd -p -o "<repo-root>/extraction/decompiled" "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll"
```

2. 重点搜索以下关键词：
   - `ModInitializer`
   - `RunManager`
   - `CombatManager`
   - `SetUpCombat`
   - `ActionQueue`
   - `CardModel`
   - `Intent`
   - `Reward`
   - `Shop`
   - `Map`

3. 将确认结果按“类型索引表”和“关键入口表”补入本文件

### 临时替代方案

在需要快速补充符号时，可使用仓库脚本进行字符串扫描：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/scan-assembly-strings.ps1" -AssemblyPath "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll" -Patterns ModManager,ModInitializerAttribute,CombatManager,RunManager,EnqueueManualPlay,UsePotion
```

该脚本只能用于快速发现符号，不能替代正式反编译。

---

## 第一轮重点验证问题

1. Mod 的入口属性或接口到底是什么
2. `screen` 如何从 `ActiveScreenContext` 映射为稳定 API 值
3. `end_turn` 是否应直接借助 `CombatManager` 内部流程，而不是模拟按钮输入
4. 地图节点选择是否应优先复用 `MoveToMapCoordAction`
5. 奖励、商店、休息点分别应该以房间对象还是屏幕对象为入口
6. `/state` 最小实现先从哪些对象读取最稳

---

## 与实现阶段的衔接

在 `Phase 1B` 开工前，最少要先确认以下事项：

- Mod 入口
- 最小可用 patch 点
- 状态读取入口
- 最小动作入口

如果以上四项中有任意一项未确认，Mod 骨架可以先搭，但业务实现不应继续扩展。
