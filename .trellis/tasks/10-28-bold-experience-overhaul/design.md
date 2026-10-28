# 大胆改造：技术设计（design.md）

> 配套：`prd.md`（要什么/验收）、`implement.md`（顺序、命令、勾选清单、台账、决策日志、**恢复指南**）、
> `research/`（六份调研原文，本文引用时写作「autoplay 报告 §x」等）。
> 本文是**实现时的唯一技术依据**。改设计先改本文，再改代码，并在 `implement.md` §7 决策日志记一行。
> 行号会漂移，本文一律用「文件 + 符号名」定位。每个工作流标注 **Tier**：T1 必做 / T2 应做 / T3 可做（时间与预算允许才做）。

---

## 1. 基线事实（2026-10-28 实测；恢复上下文先读这一节）

### 1.1 仓库与工具链
- 仓库 `C:\Users\chart\Documents\project\sp`（GitHub `CharTyr/STS2-Agent`）。基线 `dev@89d5fd3` = `origin/main` = v0.17.0（今天已发布）。
- `stash@{0}`「pre-sync 2026-10-28: local CS0173 fix (already upstream as 8d4e92d)」：上游已含同一修复，**保留不动**。
- 未跟踪 `.trellis/tasks/10-27-live-dev-validation/`：上一会话的任务，**不属于本改造，不提交不删除**。
- 工具链：.NET SDK 9.0.318（`global.json` 锁 9.0.x）、uv 0.11.28、Python 3.11.9、`ilspycmd` 9.1、`gh`（已登录 CharTyr，但 `gh issue view` 缺 scope，用 `gh api repos/CharTyr/STS2-Agent/issues/<n>`）。
- 沙箱：uv 需 `$env:UV_CACHE_DIR = "<repo>\.uv-cache"`。C# 测试在沙箱有 9 个**环境性失败**（`File.Replace`/ACL、`HttpListener` 句柄）：
  `Audit.StoreSavePreservesCollidingLegacyRun`、`SessionRemediation.LegacyCollisionPreservesBothRuns`、`SettingsStore.CorruptBackup`、`SettingsStore.NoticeNoSecret`、
  `Session.CorruptRestoresBackup`、`DevAudit.SaveRetryClearsFailureNotice`、`NativeMcp.OriginHttpPolicy`、`LoopbackListener.RealLoopbackListenerResponds`、
  `ReviewFix.UnusableFileFallsBackInsteadOfThrowing`。基线：C# 1023 PASS / 9 FAIL（维护者机器 1032/1032），Python 439 OK（8 skipped）。
  判断回归只看这 9 个以外的失败；合并前用提升权限复跑一次确认全绿。

### 1.2 游戏与环境
- 游戏：`C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`；`data_sts2_windows_x86_64\` 下 `sts2.dll`（2026-08-16）、`0Harmony.dll`、`GodotSharp.dll`。
- `extraction/decompiled/` 是 2026-03-10 的旧反编译；关键成员已用 `ilspycmd -t <类型全名> <sts2.dll>` 对**装着的** dll 复核（见 §1.5 标「已核」）。新用到的成员一律同法复核。
- 第三方 Mod（`mods/`）：`SpeedX`（皮皮极速，Harmony 加速，已反编译到 `build/research-speedx/`——**许可证未知，只学 API 不抄代码**）、`DamageMeter`（历史上抛过 `MissingMethodException`）、`RemoveMultiplayerPlayerLimit`、`另一个更好的储君娘化`；本 Mod 的 `STS2AIAgent.dll/.pck/mod_id.json`（2026-10-28 14:07 部署）。
- 本 Mod 已引用 `0Harmony`，`Game/NativeCoopLoadGuard.cs` 用 `new Harmony("STS2AIAgent.local-save-guard").Patch(...)` 手工打补丁（新补丁沿用此写法）。
- 玩家设置：`%APPDATA%\STS2AIAgent\settings.json`（`STS2_AGENT_SETTINGS_PATH` 可覆盖）。现状：CommandCode 网关 `deepseek/deepseek-v4.1-flash`，`supportsTools=true`、`supportsVision=true` ⇒ **每一步都附截图**；Jev 指 typesafe。**实机测试不改玩家设置**，用测试专用设置文件 + 本地网关（`build/live-config/endpoint.local.json`）。

### 1.3 行数棘轮（`SourceShapeContractTests`：预算只降不升；新逻辑放新文件）
| 文件 | 行数 | 预算 | 余量 |
| --- | --- | --- | --- |
| `STS2AIAgent/Agent/AgentLoop.cs` | 1000 | 1000 | **0**（必须先移出） |
| `STS2AIAgent/Agent/AgentRuntime.cs` | 1236 | 1350 | 114 |
| `STS2AIAgent/Game/GameStateService.cs` | 1388 | 1400 | 12 |
| `STS2AIAgent/Game/GameStateService.Payloads.cs` | 1320 | 1340 | 20 |
| `STS2AIAgent/Game/GameStateService.AgentView.cs` | 1252 | 1400 | 148 |
| `STS2AIAgent/Game/GameActionService.cs` | 1237 | 1300 | 63 |
| `STS2AIAgent/Game/GameActionService.Rooms.cs` | 1148 | 1250 | 102 |
| `STS2AIAgent/Game/GameStateService.Combat.cs` | 904 | 1000 | 96 |
| `STS2AIAgent/Llm/OpenAiCompatibleClient.cs` | 823 | 1000 | 177 |
| `STS2AIAgent/Ui/AgentOverlayHost.Pages.cs` | 890 | 1000 | 110 |
| `STS2AIAgent/Ui/AgentOverlayHost.Settings.cs` | 874 | 1000 | 126 |
| `mcp_server/src/sts2_mcp/server.py` | 699 | 700 | **1** |
| `mcp_server/src/sts2_mcp/client.py` | 696 | 700 | 4 |
`BudgetsStayCloseToTheFilesTheyGuard`：显式条目与实际差 > 500 或文件降到 ≤1000 时必须改/删条目。

### 1.4 现有决策链（符号名）
- `AgentRuntime.StartAutoPlay` → `AutoPlaySession.TryStart(AutoPlayLoopAsync)`。
- `AgentRuntime.AutoPlayLoopAsync`（`AgentRuntime.cs`）每回合：**整份构建原始 `/state`**（只为读 screen/phase/run_id/in_combat/act_id，`TurnGameThreadBudget`=15 s）→ `CurrentRunBoundary.Check` → `ObserveSessionStateSnapshot` → 非战斗门 → `TryCompanionImmediateAsync` → `_loop.PlayOnceAsync(token, checkState, SetPlayPhase, observeDecider)` → `ObserveStrategyContext`；内层 finally 清思考气泡；外层 finally `CancelStrategyRefresh(); FlushSessionIfDirty(); RaiseChanged();`。
- `AutoPlayRecovery.RunAsync/Observe`：拿 `_turnGate` → 回合 → `Commit`（`budgetGuard.Observe` + `ApplyPlayResult`）→ `Observe`：配置错误立停；等待游戏 1 s（150 s 墙钟上限，等玩家/等战斗豁免）；已执行未稳定 2^n s（5 次停）；思考耗尽 2^n s（5 次停）；成功：重复同动作同指纹 3 次停，重复 1 s，否则 0 s；失败 2/4 s，**3 次停**。
- `AgentLoop.PlayOnceAsync`：读 compact（checkState）→ `WaitUntilActionableAsync(20 s)` → 再读 compact → `TryEscapeSandpitAsync` → 双层模式 `TryDecideWithJevAsync` → 否则 `PlayWithModelAsync`。
- `AgentLoop.PlayWithModelAsync`（`AgentLoop.Decision.cs`）消息顺序（缓存契约：静态在前、状态在后）：`PlayPrompt.PlaySystem` → 屏幕 playbook 切片 → 回复语言 → 屏幕策略切片 → 规划器策略 → 队友对话 → **视觉（模型 SupportsVision 就每步截图）** → `ContextCompaction` 记忆 → 玩家指令 → 「Latest compact game state」→「…Call get_game_state if needed, then act exactly once.」→ `CompleteWithToolsAsync(AgentTools.Play, stopAfterAct: true)`。
- `AgentLoop.CompleteWithToolsAsync`（`AgentLoop.cs`）：≤ `MaxToolRounds=8` 轮，每轮先 `CheckBudget`；默认流式；**流式回复没有工具调用就整轮非流式重发一次**（不管是不是流式故障）；`!SupportsTools` 才用 `ActJsonParser`；`LooksLikeTruncatedStreamedAct`/`IsMissingIndexError` 触发一次非流式重放（`toolStreamFallbackTried` **只在本回合有效**）；每决策只许一次 act（第二个回 `only one act is allowed per decision`）。
- `AgentLoop.ExecuteActAsync`：一次决策快照（compact + 描述符）→ 合法性 → `ActIndexValidator.Validate` → Sandpit 校验 → `GameBridge.ActAsync`（执行租约内 `validateState`）→ `onAccepted` → 未稳定则 `WaitUntilActionableAsync(20 s)` 再读 → `(Action, ResultJson, Fingerprint, Unsettled, Error)`。
- `AgentRuntime.RecordTurnReceipt`（`AgentRuntime.Accounting.cs`）：`AccountTurn` + Jev 读数 + `RecordDecision(source: "jev"|"agent_loop", ...)`。
- `GameBridge.WaitUntilActionableAsync`：循环「游戏线程上 `BuildStatePayload()` → 判断非被动动作 → `WaitForNextFrameAsync()`」，每探针 5 s、外层 20 s ⇒ **等待期间每帧整份构建一次**。被动动作集 `PassiveActions` = `discard_potion`、`save_and_quit`。
- 事件流：`EventPollingCoordinator` 已只在有订阅者时轮询，但有订阅者时每 120 ms 整份构建（`GameEventService.DefaultPollIntervalMs`，env `STS2_EVENT_POLL_MS`）。游戏内 Agent 不订阅。

### 1.5 实测数据（来自调研，做前后对比的基准）
- 请求与 token（autoplay 报告 §1.5，305 个已接受决策、662 请求、7,533,746 token 的本地账本）：**每决策 2.17 次请求**（只有 13% 一次完成）、每请求 11,380 token、每决策 24,700 token；`play_card` 2.33 请求/28.9k token；`end_turn` 46 次全是「无牌可出」类强制动作。推算整局 1500–2200 请求、9–15M token。
- 每请求构成（autoplay 报告 §2.2）：固定前缀 ≈5,600 token（`PlaySystem` 共享契约 3,409 + 8 个工具 schema ≈1,400 + 屏幕切片 ≈791）占 49%；compact 状态 3,546–4,753 token，其中 `map.nodes`（64 节点 4,831 字符，**每个屏幕都带**）、单人局 `run.players` 重复本人牌组遗物药水（1.2–1.8k 字符）、战斗中 `run.piles` 与 `combat.draw/discard/exhaust` 重复（≈936）、非战斗屏的 `action_readiness`（≈600）、每帧重复的 `card_value_note`（227）、每张手牌 ~10 个 null 字段 ⇒ 状态里 2,500–2,900 token 与当前决策无关。
- 状态构建耗时（speed 报告 §3.1，2026-09-18 实机）：3,049 次，p50 2.8 ms、p95 16.8 ms（p95 = 一整帧）。
- 失败表（reliability 报告 §1）：F1 网关截断流式工具参数（2026-10-28 实机连续 3 次停机）；F2 所有失败共用「3 次就停」；F3 游玩请求**不发输出上限**；F4 窗口后台不渲染 → 15 s 游戏线程超时被记为「网络」失败；F6 Steam 关闭时邀请永远 `pending`；F7 #211 队友接口未起；F9 #38 Codex 拒绝无限循环；F14 发给队友的消息无进度；F15 原始 API 部分中文成替换字符。
- 体验缺陷（UX 报告 §2）：**P1 首次配置成功后下次启动面板不再自动出现**（`OverlayVisibleOnStart` 默认 false，`HasSeenFirstRunGuide` 被任意一次面板切换与测试通过写成 true → 只剩 34×64 的「AI」边缘标签）；标题行显示原始 id「已执行 play_card」；看不到计划/理由默认折叠；局末只有一句「对局已结束」；**F8 是开关面板不是暂停**。

### 1.6 已核对的游戏成员（「已核」= 已用 ilspycmd 对装着的 sts2.dll 核对）
- 加速：`MegaCrit.Sts2.Core.Settings.FastModeType { None, Normal, Fast, Instant }`；`SaveManager.Instance.PrefsSave.FastMode`（public get/set，已核）。`Cmd.Wait(float, CancellationToken, bool)` 在 `Instant` 时跳过；`Cmd.CustomScaledWait` 按档位；`NGame._Ready` 启动时 `Instant → Fast`；控制台 `instant` 命令即时切换；`AutoSlayer` 用 `Fast`。写盘：`SaveManager.SavePrefsFile()`（已核）→ `MegaCrit.Sts2.Core.Saves.Managers.PrefsSaveManager.SavePrefs()`（public；`PrefsSaveManager` 内部也直接调用）。游戏在 `NSettingsScreen.OnSubmenuClosed`、`NGame.Quit` 写盘。不受 FastMode 影响：Spine、`NHitStop.DoHitStop`、逐帧特效、`NCardFlyVfx` 私有速度；`NMapScreen._mapAnimStartDelay/_mapAnimDuration` 为构造时 readonly。
- 卡牌稳定 id：`MegaCrit.Sts2.Core.GameActions.Multiplayer.NetCombatCardDb.Instance.TryGetCardId(CardModel, out uint)`（已核），战斗内稳定、联机同步同源。
- 伤害：敌人意图 `AttackIntent.GetSingleDamage(targets, owner)` 已经过 `Hook.ModifyDamage`（力量、虚弱、**玩家易伤**、上限都已计入），`total_damage = 单次 × Repeats`。手牌 `values[].preview` 用 `card.CurrentTarget`（通常 null）⇒ **对易伤敌人偏小**（易伤只在 `target == Owner` 时生效）。按目标精确预览：`card.UpdateDynamicVarPreview(CardPreviewMode.Normal, enemyCreature, card.DynamicVars.Clone(card))`（`UpdateDynamicVarPreview(CardPreviewMode, Creature?, DynamicVarSet)` 已核；克隆不污染 UI）读 `"Damage"`/`"CalculatedDamage"` 的 `PreviewValue`、`Props`（`ValueProp.Unblockable` 不吃格挡），段数 `"CalculatedHits"`/`"Repeat"`/`"Times"`，X 费按能量，附魔重放 `card.GetEnchantedReplayCount()`（已核）。格挡预览 `BlockVar` 已精确（敏捷/脆弱已计入）。毒 `PoisonPower.CalculateTotalDamageNextTurn()`，灾厄 `GetPowerAmount<DoomPower>()`。**不存在 `IsLethal`**：自己算 `damage >= hp + block`（不可格挡则不加 block）。每次预览用 try/catch 包住，失败该格为 null，绝不猜数。
- 画面节点：`NCombatRoom.Instance`（static）→ `Ui.Hand`（`NCombatUi.Hand : NPlayerHand`）→ `NPlayerHand.ActiveHolders`（`IReadOnlyList<NHandCardHolder>`，仅可见）→ `NCardHolder.CardModel`（= `CardNode?.Model`，用 `ReferenceEquals` 与手牌 `CardModel` 匹配）→ `GetGlobalRect()`；生物 `NCombatRoom.Instance.GetCreatureNode(creature)` → `NCreature.Hitbox` / `GetTopOfHitbox()`；地图 `NMapScreen.Instance`、`NMapPoint`（`Point.coord`）。游戏本身**没有 CanvasLayer**（UX 报告 §3.1），`GetGlobalRect()` 与悬浮窗同一坐标空间。截图读整个根视口 ⇒ HUD 必须在截图时隐藏。
- 悬浮窗：`AgentOverlayHost` 的 `CanvasLayer Layer=128`；截图通过 `ScreenshotService.BeginCapture/EndCapture`（`Action?`，由悬浮窗**赋值**而非 `+=`）与 `OverlayVisibleProbe`。

---

## 2. 总体架构（改造后）

```
AgentRuntime.AutoPlayLoopAsync
 ├─ TurboController.Engage()                        W1：进入循环切游戏加速档；外层 finally 恢复（+写盘守卫+崩溃恢复）
 ├─ GameStateService.BuildTurnProbe()                W9：轻量回合探针替代整份构建
 ├─ 边界 / 会话 / 非战斗门 / 队友即时决策（不变）
 └─ AgentLoop.PlayOnceAsync
     ├─ WaitUntilActionableWithStateAsync            W9：等待用 ProbeActionable（不构建），成功时只构建一次并返回 compact
     ├─ Sandpit 紧急逃离（不变）
     ├─ AutopilotPolicy（强制动作）→ 零请求执行        W2
     ├─ Jev（双层，不变；选项文本加数字 W4.3）
     └─ PlayWithModelAsync
          ├─ 静态前缀：PlaySystem + 计算器/计划说明 + 复盘摘要（W5）+ 原 card_value_note
          ├─ VisionPolicy（默认按需）                  W6
          ├─ PromptStateDiet（只瘦提示词里的状态，API 不变）  W6
          ├─ ProviderCapabilities（流式/输出上限/JSON）    W7
          └─ CompleteWithToolsAsync（AgentTools.PlayInGame：act + 元数据查询；不再有读状态工具）  W6
               ├─ act（可带 card_cid 与 then[]）
               └─ TurnPlanRunner：逐步 TurnPlanPolicy.Resolve → ExecuteActAsync → 偏离即停   W3
 回执：AgentTurnResult + Source / FailureKind / PlanSteps / 工具轮计数 → RecordTurnReceipt 逐步记决策（费用只记第一步）
 观看：LiveIntent（执行**前**上报）→ AgentHud（游戏内 CanvasLayer）+ 悬浮窗「正在做/为什么」+ 局末成绩单   W8
 状态：compact 新增 hand[].cid/type/vs、combat.threat、reward/shop 稀有度类型价格、run.deck_stats、map.route_advice  W3/W4/W5
 MCP：两面 act 支持 card_cid/then（共享向量测试）；skill 改为「模式 A 我来出手 / 模式 B Mod 自打我规划」        W10
```

**统一开关（W0，全部默认开启，设置可关；关掉即回到 v0.17.0 行为）**见 §3.4。

---

## 3. W0 地基（T1）

### 3.1 分支与提交
- 从 `dev@89d5fd3` 切 `feature/bold-overhaul`。第一个提交是本任务目录（计划文档）。每个工作流至少一个提交，信息风格 `feat(scope): ... [Wn]`（英文前缀，正文中英均可）。
- 结束：推分支 → `gh pr create --base dev` → CI `contracts` 绿 → 合并（用户已授权）。**不碰 main、不发版、不改版本号。**

### 3.2 给 `AgentLoop.cs` 腾位置
- 把 `TestConnectionAsync`、`TestConfiguredRolesAsync`、`CopyForRole`（约 85 行）移到新分部 `STS2AIAgent/Agent/AgentLoop.RoleProbes.cs`。
- 先确认 5 处读 `AgentLoop.cs` 源码的契约测试（`AutoPlayRecoveryTests`、`AgentErrorEnvelopeTests`、`LiveReasoningStreamTests`、`OverlayChatStreamContractTests`、`SevereBugSweepTests`）不读这几个方法；若读，改读新文件并在 remarks 说明。
- 新分部文件加入 `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` 的 `Compile Include`（AgentLoop 全部分部都编进离线测试工程）；`docs/project-map.md` §4 登记。后续新增的 AgentLoop 分部同理。

### 3.3 测试与登记规矩
- 纯逻辑：Godot 无关的 `internal static` 类（`*Policy.cs` 等），加入测试工程 `Compile Include`，测试类在 `STS2AIAgent.Tests/`，**在 `TestRunner.cs` 手工注册**。
- 依赖游戏的新 `.cs`：登记 `SourceCoverageTests.KnownUncompiledSources`。
- 每个新文件：`docs/project-map.md` 登记（`ProjectMap.EverySourceMapped`/`NoDanglingPaths`）。
- 新字段：`docs/api.md` 表格行（`api-facts` 闸门）；compact 结构变化升 `AgentViewVersion`（本改造只升一次：12 → 13，在 M4）。
- 新玩家可见字符串：`Loc.T("中文")` + `Localization/Loc.Strings.*.cs` 英文（`LocalizationTests` 扫 300+ 调用点）。
- 改到有哈希契约的方法体（`GameStateServiceRelocationContractTests`、`PredicateRelocationContractTests`）：重算哈希并在 remarks 写日期与原因。

### 3.4 新设置项（`Config/AgentSettings.cs`；**每项同时改** `SettingsClone.Clone`、`EnsureValidShape`、`HarvestSettings`（`Ui/AgentOverlayHost.Settings.cs`）、设置页控件、`Loc`；标「同步」的加入 `CompanionPlaySettings` 允许名单/`CompanionSettingsPatch`）
| JSON 键 | 类型/默认 | 含义 | 同步队友 |
| --- | --- | --- | --- |
| `turboMode` | string `"instant"`（`off`/`fast`/`instant`） | 自动游玩时的游戏加速档；联机局封顶 `fast` | 是 |
| `turboRestoreFastMode` | string? null | 加速生效期间记录玩家原档位（`Normal`/`Fast`/`Instant`/`None`），崩溃后恢复用；恢复后清空 | 否 |
| `autopilotEnabled` | bool true | 强制动作不问模型 | 是 |
| `turnPlansEnabled` | bool true | 允许整回合规划（`then`） | 是 |
| `combatMathEnabled` | bool true | compact 输出 `combat.threat`、`hand[].vs` | 是 |
| `visionMode` | string `"on_demand"`（`off`/`on_demand`/`every_step`） | 截图策略 | 是 |
| `hudMode` | string `"full"`（`off`/`minimal`/`full`） | 游戏内 HUD；minimal=只横幅 | 否 |
| `pauseHotkey` | string `"F7"`（空=无） | 暂停/继续 AI 的热键（W8.6；实施时先确认游戏不占用） | 否 |
| `runJournalEnabled` | bool true | 跨局战绩摘要注入提示词 | 是 |
| `maxOutputTokens` | int? 8192（null/0=不发送） | 游玩请求输出上限（W7） | 是 |
| `providerCapabilities` | 字典（模型 id → 记录）null | W7 能力记录（带指纹） | 否 |
另：`OverlayVisibleOnStart` 新安装默认改 `true`（W8.1，修 P1）。
迁移：旧文件缺键 → 上表默认；`EnsureValidShape` 把非法字符串修成默认。测试：往返、非默认值克隆（`SettingsClone` 漏字段是历史坑）、非法值修复。

---

## 4. W1 游戏加速 Turbo（T1）

### 4.1 机制：官方 `PrefsSave.FastMode`，不做 Harmony 时长缩放
- 进入 `AutoPlayLoopAsync`：游戏线程上由 `TurboPolicy` 决定目标档 → 记录原值（内存 + 设置 `turboRestoreFastMode`，经 `AgentRuntime.SaveSettings` 保存）→ 设置目标档。
- `AutoPlayLoopAsync` 外层 finally（覆盖暂停/停止/失败/离开对局）→ 恢复：仅当当前值仍等于我们设的值才写回原值（玩家中途手动改过就尊重玩家），然后清空标记。
- Mod 启动：`ModEntry` 在游戏读完 prefs 后（复用悬浮窗安装的「等 `NGame.Instance` 就绪」重试时机）若 `turboRestoreFastMode` 非空 ⇒ 上次异常退出 ⇒ 恢复并清空。
- 进程退出：`ModEntry` 关停路径尽力恢复（不在游戏线程时不碰游戏对象，靠下次启动恢复）。
- **写盘守卫**：Harmony 前缀 + finalizer 挂内层 `PrefsSaveManager.SavePrefs()`（id `STS2AIAgent.turbo-prefs-guard`）：前缀若加速中则临时写回原值，finalizer 改回加速档 ⇒ 玩家的档位永远不会以加速值写入文件。我们**自己永不调用**写盘。找不到方法 ⇒ `Log.Warn` + `/health.turbo.prefs_guard="unavailable"` + 默认档降为 `fast`。兜底：`NGame._Ready` 会把 `Instant` 降为 `Fast`。

### 4.2 `TurboPolicy`（新 `STS2AIAgent/Agent/TurboPolicy.cs`，纯）
输入：设置档、是否真实联机局、当前游戏档、是否已接管。输出 `{Target: Unchanged|Normal|Fast|Instant, Reason}`：
1. 设置 `off` → Unchanged。2. 联机局（`RunManager.Instance.NetService.Type.IsMultiplayer()`）→ 最多 `Fast`。3. 当前档 ≥ 目标 → Unchanged。4. 恢复只在「当前 == 我们设的」时发生。
SpeedX 共存：我们不打 `Cmd.Wait` 补丁，与 SpeedX 无相乘；设置页提示「检测到 SpeedX：两者可同时使用」（检测 = 已加载程序集名 `SpeedX`）。

### 4.3 文件与接口
- `STS2AIAgent/Game/TurboController.cs`（依赖游戏，KnownUncompiledSources）：`Engage(AgentSettings)`/`Release()`/`RecoverAtStartup()`/`InstallPrefsGuard()`；日志前缀 `[STS2AIAgent.Turbo]`。
- `/health.turbo`：`{mode, applied, original, current, prefs_guard, reason}`（additive，`docs/api.md`）。
- UI：设置页「游戏速度」分段开关（关闭 / 快速 / 极速），说明进 `HelpCard`；播放页标题行档位药丸；HUD 横幅显示档位。
- Loc：「游戏速度」「关闭」「快速」「极速」「AI 自动游玩时加速动画，暂停后恢复你原来的设置」「联机时最多使用快速档」等。

### 4.4 测试与实机
- `TurboPolicyTests`：四条规则 + 恢复不覆盖玩家改动 + 崩溃恢复标记；源码契约：外层 finally 调 `Release`、守卫补丁存在。
- 实机：各屏（战斗/奖励/地图/事件/商店/休息/宝箱/Boss/换幕）在 `Instant` 下无连续 `pending`；暂停后游戏设置页 Fast Mode 勾选与原值一致；退出重开仍一致。不稳 ⇒ 默认改 `fast`（记决策日志）。

### 4.5 明确不做
SpeedX 式 `Cmd.Wait`/`CombatState.GodotTimerTask` 时长缩放、横幅跳过、去 hit-stop：`Instant` 已让 `Cmd.Wait` 归零；`GodotTimerTask` 是联机生成生物的超时；本机装有 SpeedX，叠加会相乘。

---

## 5. W2 强制动作自动处理 Autopilot（T1）

### 5.1 `AutopilotPolicy`（新 `STS2AIAgent/Agent/AutopilotPolicy.cs`，纯；输入 compact JSON + 描述符 JSON；输出 `{Kind: None|Act, ArgumentsJson, ReasonZh, RuleId}`）
规则自上而下，首个命中即返回（综合 autoplay 报告 §4.2.1 与本设计）：
```
0  守卫：NonCombatOnlyPolicy 认定战斗且开启非战斗模式 → None；SandpitSafetyPolicy 危急 → None（逃离逻辑先跑）；
       有排队的玩家指令 → None（玩家的话要送到模型）
1  MODAL：confirm_modal 可用 且 dismiss_modal 不可用 且（类型名含 "Ftue" 或 不在战斗中）→ confirm_modal
       （与 CompanionPlayPolicy.DecideBlockingModal 同一判定：直接调用它，不复制）
2  GAME_OVER：可用动作（去掉被动）只有 continue_game_over → continue_game_over
3  COMBAT：action_readiness 可行动 且 end_turn 可用 且 每张手牌 playable=false 且 每瓶药水 usable=false
       → end_turn；若 end_turn_will_kill_player，理由写明「无牌无药可用，回合结束将受致命伤害」
4  MAP：map.available_nodes（compact options）恰 1 个 → choose_map_node(option_index=该 i)
5  REWARD：无待选卡牌（pending_card_choice≠true 且 card_options 为空）且 没有被满栏卡住的药水行
       （claimable=false 的药水行）→ collect_rewards_and_proceed（抽干会领取金币/遗物/药水并前进）
   REWARD（有卡牌待选时）：若存在 claimable=true 的金币/遗物行 → claim_reward(该行)（一次一个；卡牌与满栏药水留给模型）
6  单元素索引列表：动作自己的索引列表（ActIndexValidator.OptionPaths）恰有 1 个可用元素，且没有其它可改变选择的动作：
       event.options 仅 1 个未锁 → choose_event_option；capstone.options 1 → choose_capstone_option；
       rest.options 仅 1 个 enabled → choose_rest_option；chest 仅 1 个遗物 → choose_treasure_relic
   宝箱未开且 open_chest 可用 → open_chest
7  通用：去掉被动（discard_potion、save_and_quit）与管理/菜单动作（abandon_run、return_to_main_menu、close_main_menu_submenu、
       switch_profile、unready、所有大厅/调试动作）后只剩 1 个动作且不需要索引 → 执行它
8  其它 → None（交给模型）
```
**永不自动**：选卡（choose_reward_card/skip_reward_cards）、买卖与删牌、多选项事件/地图/休息、牌组选择与确认、药水使用/丢弃、选角/出发/难度、联机大厅、时间线、Boss 遗物、非新手弹窗的确认（有取消选项的）。

### 5.2 接入与回执
- 新分部 `STS2AIAgent/Agent/AgentLoop.Autopilot.cs`：`TryAutopilotAsync(stateJson, token, checkState, reportPhase)`，在 `PlayOnceAsync` 的 Sandpit 之后、Jev 之前调用（`AgentLoop.cs` 只加 1–2 行）。执行走 `ExecuteActAsync`（同一套合法性/索引/Sandpit/非战斗门）。门：`autopilotEnabled`。
- `AgentTurnResult.Source`（新，`"autopilot"` | `"plan"` | null），`RequestsSpent=0`，`Reasoning=ReasonZh`；`RecordTurnReceipt` 用 `result.Source ?? (Confidence != null ? "jev" : "agent_loop")`。`PlayerLabels.Source("autopilot")` →「自动处理」/"Auto"。`/decisions` 的 source 新取值写进 `docs/api.md`。
- 无进展保护不变：自动 `end_turn` 在不变的局面上重复 3 次仍会停（加测试）。
- 队友实例同用 `AgentLoop`，自动受益；`TryCompanionImmediateAsync`（跟房主投票）仍在前。
- 测试：`AutopilotPolicyTests`（每条正反例 + 永不自动清单逐项反例 + 有可用药水不结束回合 + 满栏药水时不自动收尾）；`AgentLoopTests`（零请求、来源、玩家指令让位、开关关闭走模型）。

---

## 6. W3 整回合规划 Turn Plan（T1；MCP 两面为 T2，见 §13）

### 6.1 状态字段（M4 一次升 `AgentViewVersion` 13）
- 原始 `/state`：`combat.hand[].combat_card_id`（uint?，`NetCombatCardDb.Instance.TryGetCardId`；失败 null）、`combat.hand[].card_type`（`attack`/`skill`/`power`/`status`/`curse`，来自 `CardModel` 的类型枚举，实施时核对成员名）。
- compact：`combat.hand[].cid`、`combat.hand[].type`。`docs/api.md` 两处表 + compact 重命名表。

### 6.2 工具协议
`act` 新增可选参数（向后兼容）：
- `card_cid`（integer）：用稳定 id 指定手牌；与 `card_index` 同给时以 `card_cid` 为准；解析失败拒绝该步。
- `then`（array，≤ 9）：后续步骤 `{action, card_cid?, card_index?, target_index?, option_index?, reason?}`；不允许嵌套 `then`；未知键忽略并在结果报告。
描述追加（英文）：「In combat, plan the whole turn: put the first action here and the rest of the turn (ending with end_turn when you are done) in `then`. Refer to hand cards by `card_cid`. Each step is re-checked against fresh state; the plan stops early when something changes and you will be asked again.」
结果追加 `plan: {requested, executed, stopped_reason, steps: [{i, action, status, message}]}`；原有字段不变。

### 6.3 `TurnPlanPolicy`（新 `STS2AIAgent/Agent/TurnPlanPolicy.cs`，纯；Python 镜像 `mcp_server/src/sts2_mcp/turn_plan.py`）
- `Parse(argumentsJson)`：步数上限、字段校验、动作名规范化。
- `Resolve(step, compactJson)` → 已解析 act 参数或停止原因：动作须在 `available_actions`；`card_cid` → 当前 `combat.hand[]` 中同 `cid` 的 `i`（无 → `card_gone`；`playable=false` → `card_unplayable`）；敌方目标须 `alive && hittable`（否则 `target_gone`）；`option_index` 原样交 `ActIndexValidator` 再校验。
- `Diverged(planTimeCompact, currentCompact)`：屏幕变（`screen_changed`）、回合号变（`turn_changed`）、手牌出现计划时没有的 cid（`new_cards`）、玩家 HP 下降（`took_damage`）、出现待处理选择/弹窗（`pending_choice`）。
- 停止原因枚举（C#/Python 同名）：`completed`、`card_gone`、`card_unplayable`、`target_gone`、`illegal_action`、`screen_changed`、`turn_changed`、`new_cards`、`took_damage`、`pending_choice`、`unsettled`、`rejected`、`outcome_unknown`、`canceled`、`player_instruction`。
- **共享测试向量** `mcp_server/tests/fixtures/turn_plan_vectors.json`：C# `TurnPlanVectorTests` 与 Python `test_turn_plan_vectors.py` 读同一文件，保证两份实现等价。

### 6.4 执行器与账务
- 新分部 `STS2AIAgent/Agent/AgentLoop.TurnPlan.cs`：`RunPlanAsync(plan, planTimeCompact, token, checkState, reportPhase, reportIntent)`；`CompleteWithToolsAsync` 主 act 成功（且 `stopAfterAct`）后调用。每步：取消检查 → 读 compact（一次）→ `Diverged` → `Resolve` → `reportIntent` → `ExecuteActAsync`（全部既有校验）→ 记步骤 → 任一步非 `completed`（含 pending/拒绝/丢失）立即停止，**绝不重放**。执行中出现排队玩家指令 → `player_instruction` 停止。
- `ExecuteActAsync` 支持 `card_cid`（先经 `TurnPlanPolicy.Resolve` 转 `card_index`）。
- 回执：`AgentTurnResult.PlanSteps`（每步 Action/Reason/Fingerprint/Unsettled/Error）、`PlanStopReason`；`Acted/StateFingerprint/ExecutedUnsettled` 取**最后一步**（无进展判定看最终局面）。
- 决策记录：每步一行，additive 字段 `turn_id`（同一请求共享）、`plan_index`、`plan_size`；**第一行**带本回合请求/token，后续行 0（`AccountTurn` 仍是唯一记账者，「一次请求一次记账」不变）。`docs/api.md` + `DecisionLogTests` 钉住。
- 提示词：`skills/sts2-mcp-player/SKILL.md` 共享契约段（`PlayPrompt.PlayContract` 由此抽取）+ `references/screen-playbooks.md` 战斗段：整回合规划、`card_cid`、`combat.threat`/`vs`。`PlaybookSections` 切片 ≤2400 字符；`scene_guidance.py` 镜像同步。
- 测试：`TurnPlanPolicyTests`、`TurnPlanVectorTests`、`AgentLoopTests`（三步计划只记 1 次请求；抽到新牌即停；目标死亡即停；主动作被拒不执行 then；暂停中途取消；cid 解析）。

---

## 7. W4 战斗算术（T1；Jev 选项加数字 T2）

### 7.1 `combat.threat`（永远开启，零额外游戏调用）
```
"threat": { "incoming": 18, "incoming_unblockable": 0, "block": 5, "block_needed": 13,
            "unblocked": 13, "hp_after": 11, "lethal": false,
            "per_enemy": [ {"i":0,"incoming":10,"hits":2}, {"i":1,"incoming":8,"hits":1} ],
            "end_of_turn_self_damage": 0 }
```
- 来源：compact 构建时已有的 `enemies[].intents[].total_damage` 与 `player.block`；`end_of_turn_self_damage` 来自 `CombatRisks`（毒/灾厄/束缚/魔法炸弹/沙坑）现有计算——**同一份定义**：把 `CombatRisks` 里求和的部分抽成纯 `CombatThreatPolicy.cs`，`lethal_risks` 与 `threat` 都读它，二者永不矛盾。
- `incoming_unblockable`：意图的 `ValueProp` 若可得则计入（实施时核对意图载荷能否拿到；拿不到则为 null，不猜）。
- 联机注意：意图是对本地玩家计算的（字段说明写明）。

### 7.2 `hand[].vs`（每张攻击牌对每个存活可攻击敌人的精确伤害）
- `GameStateService.CombatMath.cs`（新分部，依赖游戏）：对 `type == attack`（或含伤害变量）的可打出手牌 × 每个 `alive && hittable` 敌人：克隆 `DynamicVars` → `UpdateDynamicVarPreview(CardPreviewMode.Normal, enemy, clone)` → 读伤害/段数/Props → `{"enemy":i,"damage":总伤,"after_block":扣除敌方格挡后,"kills":bool}`；每格 try/catch，失败该格省略。
- 缓存：以「战斗回合号 + 手牌 cid 序列 + 每个敌人 (hp, block, powers 摘要) + 玩家 powers 摘要 + 能量」为键缓存一份，键不变直接复用（只在玩家行动阶段计算）。
- 纯算术 `STS2AIAgent/Game/CombatMathPolicy.cs`：输入数值 → `threat` 汇总、`kill_options`（每个敌人：当前能量下能否击杀，0/1 背包，列出牌 cid 与耗能；忽略抽牌与出牌中途增益——字段 `estimate: true`）、`max_block`（背包）。结果放 `combat.threat.kill_options` 与 `combat.threat.max_block`。
- 开关 `combatMathEnabled`；关时这些字段为 null。
- 载荷声明：`Payloads.cs` 只剩 20 行；`api_schema.py`/`api-facts` 从该文件读线类型。实施时先量：若放不下，**先把与本改造无关的、不被 schema 读取的辅助声明移出**（需确认闸门读取范围），不抬预算。
- 提示词：静态前缀加「`combat.threat` 与 `hand[].vs` 是游戏自身数值预览（易伤/力量/格挡已计入）；`kill_options` 与 `max_block` 是忽略抽牌的估算」。

### 7.3 Jev 选项文本加数字（T2，decision 报告 P2）
`JevOptionEnumerator` 的 `play_card` 选项描述追加一段（≤60 字符，只对可打出牌）：`→ 大颚虫 (9 伤 · 击杀 · 溢出 3)` / `(格挡 5 · 来袭 18 · 仍受 13)`；`end_turn` 追加 `(将受 13 · HP 24→11)`。数字由同帧 `threat`/`vs` 传入（枚举器保持纯）。

### 7.4 基准 v2（T2，decision 报告 P7）
`docs/decision-benchmark.json` 升 `schema_version: 2`，增加 8–12 个「算术用例」（该打死残血就别格挡、恰好格挡、易伤目标优先、不要在 `end_turn_will_kill_player` 时结束回合）；新增计划类期望（`must_kill_enemy`、`max_damage_taken`），并用 `CombatMathPolicy` 跑一个零成本的「本地计算器答案」作为对照。v1 用例继续计分。

---

## 8. W5 策略大脑（T2）

### 8.1 奖励/商店字段（先做，最便宜）
compact `reward.cards[]`、`shop.cards[]` 增加 `rarity`、`type`；商店条目增加数字 `price`（目前只在 `line` 文本里）。`run.deck_stats`：`{count, attacks, skills, powers, curses, statuses, upgraded, avg_cost, zero_cost, starter_strikes, starter_defends}`（只在奖励/商店/休息/牌组选择屏输出）。

### 8.2 地图路线建议 `map.route_advice`
`STS2AIAgent/Game/MapRoutePolicy.cs`（纯）：以 `map.nodes[]` 图为输入（可移植 `mcp_server/src/sts2_mcp/knowledge.py` 的 `_enumerate_paths` 思路），对每个当前可选节点枚举到 Boss 的路径（带环路防护、节点数上限），汇总精英/休息/商店/问号/宝箱/普通战斗的 [最少,最多]，按 HP 比例、金币、幕数打分，输出 `{options:[{i, elites:[a,b], rests:[a,b], shops:[a,b], unknowns:[a,b], score, why}], recommended_i, note:"advisory"}`。仅在 MAP 屏、可选 ≥ 2 时输出。

### 8.3 跨局战绩 Run Journal（事实，不让模型写教训）
- `STS2AIAgent/Agent/RunJournalStore.cs`：与 `PlaySessionStore` 同目录族（随 `STS2_AGENT_SETTINGS_PATH` 迁移）的 `journal.jsonl`；局末（`GAME_OVER` 观察到或 `run_end` 停机）追加一条：`{run_id, character, ascension, result, floor, act, boss_id, died_to:{enemy_id?, inferred:true}, hp_at_end, deck_size, key_relics(≤5), requests, tokens, finished_at}`；最多 50 条；原子写；脱敏（复用 `PlaySessionStore` 规范化）。
- 注入：`RunJournalStore.Digest(character, maxChars:600)` 生成「近期战绩」事实摘要（同角色最近 5 局：层数、结果、倒在谁手里）放静态前缀（每局只变一次，利于缓存）。SpireBench 发现模型自写「记忆」17% 是错的且过度保守 ⇒ 只给事实，不给结论。
- 设置页「清空战绩记录」按钮；`runJournalEnabled` 关时不写不注入。

### 8.4 角色/Boss 策略页（T3）
在 `strategy.md` 增加按角色的切片（7 个角色，每页 ≤1200 字符，替换而非追加通用战斗段），数字类内容从 `docs/game-knowledge/*` 生成；`PlaybookSectionsTests` 保证没有悬空标题。

---

## 9. W6 省钱瘦身（T1）

### 9.0 干掉「每个决策第二次请求」（autoplay 报告 §3.1/§4.1：2.17 → ≈1.05 请求/决策）
1. **先计量**：决策记录 additive 字段 `tool_rounds`、`read_tool_rounds`、`stream_demoted`（`AgentTurnResult.ToolRounds` 已有），`docs/api.md` 行。
2. **流式降级只在真故障时**：`CompleteWithToolsAsync` 中「流式回复无工具调用 → 整轮非流式重发」改为：仅当回复**完全为空**（无内容、无思考、无工具调用）才降级重发；回复有正文时先尝试 `ActJsonParser`（W7.3），解析不出才追加一句「You must call act now」再来一轮（仍计一次请求，但只在模型真的没出手时发生）。
3. **游戏内工具清单去掉读状态工具**：新增 `AgentTools.PlayInGame` = `act` + `get_game_data_item(s)` + `get_relevant_game_data` + `wait_until_actionable`；`AgentTools.Mcp` 与 `AgentTools.Play`（若其它路径用）保持不变（ADR 0002 对齐测试）。`PlayPrompt.PlaySystem` 的 Tools 行仍列全部工具名（`McpPlayerSkillTests.SkillTracksLivePlayContract` 钉住可达性——实施时读该测试，必要时有意更新并写原因）。
4. **末句**改为「The state above is fresh. Do not call get_game_state; call act now (with `then` for the rest of a combat turn).」

### 9.1 `PromptStateDiet`（新 `STS2AIAgent/Agent/PromptStateDiet.cs`，纯；**只瘦提示词里的状态，API 的 compact 视图不变**）
对插入提示词的 compact JSON 做投影（Jev 请求同样使用）：
- 非 MAP 屏：`map` 只留 `{open,current,options,votes}`（删 `nodes`）。
- 单人局：`run.players` 只留 `{player_id,is_local,hp,gold}`。
- 战斗中：删 `run.piles`（与 `combat.draw/discard/exhaust` 重复）。
- 非战斗屏：删 `combat.action_readiness`；战斗中只留 8 个决策会读的键（`can_use_combat_actions`、`reason`、`running_action_type`、`modal_open`、`modal_type`、`player_actions_disabled`、`hand_in_card_selection`、`local_turn_ready`——实施时以实际字段名为准）。
- 删 `combat.card_value_note`（原文移入静态前缀）。
- 手牌：值为 null 的键与空数组删掉（`why`/`unplayable_*` 只在不可打出时保留，`target/targets` 只在需要目标时保留）。
- 预计 COMBAT 帧 −50%、REWARD 帧 −57%，整请求 ≈ −23% token。测试：每条规则、投影后仍是合法 JSON、决策关键键仍在（`CompactViewFidelityTests` 思路）。

### 9.2 视觉按需
`STS2AIAgent/Agent/VisionPolicy.cs`（纯）：`off` 否；`every_step` 是；`on_demand`（默认）：屏幕 `UNKNOWN`、上回合失败/未行动、连续未稳定 ≥ 2 时才截图。`PlayWithModelAsync` 把 `attachRequested: true` 改为策略结果（`AgentRuntime` 经回调提供最近失败/未稳定计数）。设置页三选一。

### 9.3 缓存命中与指标
`LlmUsage.CachedPromptTokens`（解析 `prompt_tokens_details.cached_tokens` 与 DeepSeek `prompt_cache_hit_tokens`；SSE 与非流式两条路径都解析）；会话统计显示「缓存命中 x%」与「每层平均请求」；`/health.session` additive 字段。

### 9.4 一次读取可操作状态
`IGameBridge.WaitUntilActionableWithStateAsync(timeout, token) → (bool actionable, string compactJson)`；`PlayOnceAsync` 的「读→等→读」合并；`GameBridge` 状态读取改用带期限的 `GameThread.InvokeAsync` 重载（autoplay 报告 §3.7：现为仅令牌无期限）。

---

## 10. W7 自愈与兼容（T1，输出上限/轨迹 T2）

0. **失败分类代替数失败**：`AgentTurnResult.FailureKind`（在产生错误处赋值）：`transport`（超时/5xx/429/连接）、`config`（4xx 非 408/429）、`arguments`（截断/缺索引/坏 JSON）、`empty`（无内容无工具）、`illegal`（不在列表/索引被拒）、`game_stalled`（游戏线程超时 `TimeoutException("The game thread did not run the posted action in time.")`）。
   `AutoPlayRecovery.Observe`：`transport` 退避 2/4/8/16/30 s、**5 次**停（`network`）；`config` 立停；`game_stalled` 计入**等待时钟**而非失败（状态「游戏窗口可能在后台没有渲染，已暂缓」）；其它仍 3 次停。等待时钟改「证据时钟」：屏幕/阶段/指纹变化即重置 150 s。`StopKindPolicy.Classify` 子串匹配保留为兜底，有 `FailureKind` 时优先。
1. **能力探测**：`AgentRuntime.ModelTest.cs` 现有「ping + `tool_choice=required` 的 act 探针」之后加一次**流式**同款探针（需要索引的 `play_card` 形参数），比较参数完整性 → `providerCapabilities[modelId] = {fingerprint, tools_ok, stream_tool_args_ok, max_tokens_field, tested_at, probe_version}`（指纹沿用 `ModelRoleProbe.Fingerprint`，模型/端点/密钥/协议一变即失效）。
2. **按能力选形状**：`stream_tool_args_ok == false` ⇒ act 回合从第一次就非流式（不再每回合白付一次截断请求）。
3. **运行时学习**：同一指纹在游玩中第 2 次出现截断 ⇒ 写 `stream_tool_args_ok=false`（一次写入），模型卡片显示「此服务商的流式工具参数不完整，已改用非流式」。
4. **正文里的动作也认**：`SupportsTools=true` 但无工具调用、正文可被 `ActJsonParser` 解析 ⇒ 当作 act 执行（目前只在 `!SupportsTools` 时）。
5. **唯一索引补全**（autoplay 报告 §4.6.3）：`ActIndexValidator.ResolveUniqueIndex(action, snapshot)`：动作的列表恰有 1 个合法元素时补全缺失索引（例如只有一张可打出的牌时 `{"action":"play_card"}` 也能执行）；有 2 个以上仍按截断处理。
6. **输出上限**（T2，F3）：游玩请求带 `maxOutputTokens`（字段名由 `MaxTokensField`/能力记录决定；默认 8192；先用本地网关实测一次，计台账）；`ReasoningBudgetExhausted` 停机提示指向这个真实设置。
7. **停机说人话**：每条停机消息 =「发生了什么 + 你可以做什么」；`PlayerFacingSession` 的「下一步」同步；停机后「继续游玩」按钮不清计数直接续上。
8. **回合轨迹 + 为什么停了**（T2）：内存环形缓冲最近 12 回合（屏幕、动作+索引+理由、请求/token、失败类型、已用修复、指纹变化；不含提示词/密钥/思考），`GET /diagnostics` 与诊断导出加 `recent_turns`；悬浮窗停机行「为什么停了」展开。
9. **乱码排查**（F15）：实机时取 `/state` 原始字节定位；确认是本 Mod 才修并加回归测试。
10. 测试：注入 `HttpMessageHandler` 模拟截断流、无工具调用但正文 JSON、5xx 序列、游戏线程超时；`AutoPlayRecoveryTests` 新增按类处理、退避阶梯、证据时钟。

---

## 11. W8 观看体验（T1：8.1–8.6；T2：8.7–8.8）

### 8.1 修「面板消失」（UX P1，T1，最小改动最大收益）
- 新安装 `OverlayVisibleOnStart` 默认 `true`（`AgentSettings.CreateDefault`）；`AgentRuntime.PersistOverlayVisible` 不再写 `HasSeenFirstRunGuide`（两个事实无关）。
- 未配置时边缘「AI」标签带提示色点（用 `UiFactory.Status` 的色调，**不用 emoji**——视觉契约禁止符号色调）。
- 未配置时单人卡一行：「窗口已收起，按 F8 或点右边的 AI 标签打开」/ "The window is hidden — press F8 or click the AI tab on the edge."

### 8.2 标题行说人话（UX P2/P8，T1）
`ApplyPlayResult` 改 `Loc.T("已执行：{0}", PlayerLabels.Action(result.Acted))`（有牌名时带牌名）；`DecisionLogView.FormatLine` 的 action/source 走 `PlayerLabels`；同步更新 `OverlayTabContractTests` 里断言 `"1. play_card"` 的两处（有意更新，写原因）。

### 8.3 卡住时给提示（UX P4/P7，T1）
`STS2AIAgent/Agent/StallHintPolicy.cs`（纯）：某阶段超过阈值追加第二句到 `_soloNext`（已是 `Wrapped`，**不新增 Label**）：主菜单等待 →「还没开始一局：AI 会自己选角出发，也可以你先开一局」；等玩家 →「等你在自己的窗口里操作」；请求模型 > 30 s →「模型响应较慢，可以暂停后换模型」。

### 8.4 实时意图 `LiveIntent` + 游戏内 HUD（T1：H1/H2/H3/H5；T2：H4 地图）
- `STS2AIAgent/Agent/LiveIntent.cs`（纯，不可变快照）：`{state: intent|executing|done|cancelled|failed, source, action, card_cid, card_name, target_index, target_name, option_label, reason, plan_steps:[{label, done}], turbo, updated_at}`。
- **执行前上报**：`AgentLoop` 新增可选回调 `Action<LiveIntentUpdate>? reportIntent`（与 `reportPhase` 并列），在 Jev（`AgentLoop.Decision.cs` 的 `ExecutingAction` 处）、LLM（`CompleteWithToolsAsync` 两处 `ExecutingAction`）、自动处理、计划每步执行**之前**上报；完成/取消/失败/暂停/运行边界/房间变化清除。`AgentRuntime` 持有并 `RaiseChanged`。
- `STS2AIAgent/Ui/AgentHud.cs`（依赖 Godot）：独立 `CanvasLayer`（Layer 120，低于悬浮窗 128；游戏本身没有 CanvasLayer），全部 `MouseFilter.Ignore`；由 `AgentOverlayHost.OnProcessFrame` 驱动（每 100 ms 节流），只在主窗口安装（同悬浮窗条件）。节点定位放 `STS2AIAgent/Game/HudAnchors.cs`（依赖游戏；`GameStateService` 余量不够且不应承担）：
  - H1 要出的牌描边：`NCombatRoom.Instance` → `Ui.Hand.ActiveHolders` → `ReferenceEquals(holder.CardModel, card)`（card 由 cid 经 `NetCombatCardDb.TryGetCard` 取得）→ `GetGlobalRect()`；
  - H2 目标连线：`GetCreatureNode(enemy)` → `GetTopOfHitbox()`；
  - H3 理由气泡：本地玩家生物头顶，≤120 字，2.5 s 淡出；
  - H4 地图（T2）：即将选择的 `NMapPoint` 脉冲圈；
  - H5 横幅（顶部居中）：「AI 正在游玩 · 极速 · F7 暂停 · F8 打开面板」/「AI 已暂停」/「AI 正在思考…（6 s）」/ 计划预览「本回合：① 打击→大颚虫 ✓ ② 防御 ③ 结束回合」。
  - 一切查找 `GodotObject.IsInstanceValid` 保护，找不到就不画，绝不抛异常。
- 截图：`AgentOverlayHost.HideForCapture/RestoreAfterCapture` 同时隐藏/恢复 HUD；`OverlayVisibleProbe` 改为「面板可见 || HUD 可见」，保证只有 HUD 时也会隐藏。扩展 `Screenshot.*` 契约测试。
- 纯逻辑 `STS2AIAgent/Agent/HudModel.cs`（横幅文本、计划步状态、节流、清除规则）离线测试；渲染靠实机截图验证。
- `hudMode`：off/minimal（只横幅）/full。

### 8.5 局末成绩单（UX P5，T1）
- `STS2AIAgent/Agent/RunSummary.cs`（纯）：从 `GameOverPayload`、`CurrentRunSpend()`、决策记录、Run Journal 记录生成：结果/角色/层数/用时、决策数（模型/自动处理/计划步/快速判断）、请求/token/缓存命中、每层请求、加速档、三条高光理由。
- 悬浮窗 `STS2AIAgent/Ui/AgentOverlayHost.RunSummary.cs`：局末自动展开 `HelpCard` 卡片「本轮成绩单」，「复制成绩单」按钮；`/health.last_run_summary`（additive）。

### 8.6 暂停热键（T1）
- `pauseHotkey`（默认 F7，实施前在反编译源与实机确认游戏未占用 F7；占用则默认空并只在设置里可选）：`AgentOverlayHost` 的热键轮询（已有 F8 逻辑，文本框聚焦时忽略）增加：自动游玩中 → `StopAutoPlay()`；已暂停且可开始 → `StartAutoPlay()`。HUD 横幅显示热键。

### 8.7 「为什么」可展开（T2）
决策记录行可展开：完整理由、置信度/危险度、当时可选动作（`PlayerLabels.Action`）、计划步序号；展开状态存字段，`RichLog` 只在 id/条数变化时重绘。

### 8.8 术语与公开文档对齐（T2）
「单步」→「走一步」；请求上限提示「≈ 一局」；快速判断卡首行说明；README/工坊描述去掉「对话」页、更正 token 说法；新增「加速会改我的游戏设置吗？——不会，暂停即恢复」。

---

## 12. W9 性能（T1：9.0–9.2；T2：其余）

0. **等待时不再每帧整份构建**（speed 报告 P3，最高优先）：`GameStateService.Probe.cs`（新分部）`ProbeActionable()`：游戏线程、不构建载荷，复用唯一动作遍历判断「是否有非被动动作」，战斗中复用 `CombatActionGate`。**陷阱**：200 ms 稳定采样器的静态字段只在构建时推进 ⇒ 抽出 `AdvanceCombatActionGate()` 由构建与探针共用。`GameBridge.WaitUntilActionableAsync` 每帧用探针，只在进入与成功时各构建一次；探针不可用回退每 4 帧构建。`/health.state_build` 加 `actionable_probes`、`actionable_builds`、`event_poll_builds`。
1. **回合轻量快照**：`GameStateService.BuildTurnProbe()`（screen/phase/run_id/in_combat/act_id）替代 `AutoPlayLoopAsync` 的整份构建。会话规则「只有新鲜原始帧可切换/清空会话」：探针读同一帧同源字段；`run_id` 变化时补一次整份构建再调 `ObserveSessionStateSnapshot`（remarks 写明）。
2. **一次读取可操作状态**：见 §9.4。
3. 事件流自适应：`EventPollingCadence.cs`（纯）连续无变化把 120 ms 逐级放大到 250/500/1000 ms，有变化或有动作执行回到 120 ms；断线检测缩短。
4. 悬浮窗：`RefreshChatLog` 在历史与思考气泡版本号都没变时直接返回；面板隐藏时整个 tick 跳过。
5. 加速档下的自身等待地板：`TryResolveCardRewardAsync` 固定 24 帧 → 条件 + 最少 2 帧；`CardGridScrollSettleFrames=12` → 偏移不再变化 + 最少 2 帧；回合间退避改「状态信号或延时先到」。涉及哈希契约按规矩重算。
6. 会话文件 IO 移出游戏线程（T3）。

---

## 13. W10 联机与 MCP（T2）

- 队友实例与主窗口共用 `AgentLoop` ⇒ W2/W3/W4/W6/W7 自动生效；队友 `turboMode` 经设置同步，联机封顶 `fast`。
- 原生 MCP：`AgentTools.ActParameters` 增加 `card_cid`、`then`；`NativeMcpServer.Tools.ExecuteActAsync` 主动作成功后用同一 `TurnPlanPolicy` 循环（每步 `_decisions?.Record("native_mcp", ...)`）。
- Python：`server.py` 只剩 1 行 ⇒ 新建 `mcp_server/src/sts2_mcp/play_tools.py` 承载 `act`（迁出）与计划循环，`turn_plan.py` 镜像 `TurnPlanPolicy`；`server.py` 改为调用注册函数；`card_cid` 在 Python 端解析为 `card_index` 再调 `/action`（`client.py` 不动）。**主动作或任一步丢失响应（`outcome_unknown`）立即停止，绝不继续/重放**。
- 对齐：`test_native_tool_alignment.py` 零豁免通过（`act` 参数两面一致）；`scripts/test-mcp-tool-profile.ps1` 工具清单不变。
- reliability 报告 4.7 反对批量动作的三条担忧，本设计逐条化解：每步新快照校验；非 `completed` 立即停；逐步结果返回（部分效果可见）。
- skill 文案（issue #38）：分「模式 A：我来出手（decide → act，可带 then）」与「模式 B：Mod 自己打，我当规划师（/session/control + get_planner_briefing + update_play_strategy）」；删除「一直循环直到结束」措辞；`mcp_server/README.md` 注明部分客户端限制自动循环。**不新增 `step` 工具**。
- 联机补强：邀请前环境预检（Steam 关闭/`--force-steam off` 等明确拒绝原因，F6）；队友入队后自检其 `/health`（`instance_role=companion` 且有 `api_port`），否则主窗口明确提示（#211 另一半）；发给队友的消息显示状态（排队中/队友战斗中/正在回复/已回复 + 等待秒数，F14）。

---

## 14. W11 文档（T1）
- `docs/project-map.md`：所有新文件；Turn 流程（含 Turbo/探针/Autopilot/TurnPlan/LiveIntent/HUD）；§10「改 X 去哪」加：加速、自动处理、计划、算术、HUD、成绩单、提示词瘦身、能力记录。
- `docs/api.md`：所有新字段（compact `hand[].cid/type/vs`、`combat.threat.*`、`reward/shop` 新字段、`run.deck_stats`、`map.route_advice`、`/health.turbo`、`/health.state_build` 新计数、`/health.session` 新字段、`/health.last_run_summary`、决策记录新字段与 source 新取值、`act` 新参数与结果 `plan`）；compact 重命名表；`AgentViewVersion` 13。
- `docs/openapi.json`：用 `scripts/api_schema.py` 重新生成（若覆盖）。
- `CHANGELOG.md` `## Unreleased`：玩家视角中文为主，注明「未发版」。
- `README.md`/`README.zh-CN.md`：亮点、核心玩法、FAQ（加速会不会改我设置、F7/F8、成绩单）。
- `.trellis/spec/mod/agent-and-ui.md` 等：新约定（自动处理永不做取舍；计划回执只第一步记费；HUD 不挡点击且截图隐藏；Turbo 恢复与写盘守卫；提示词瘦身不改 API）。
- 实机报告：`build/live-2026-10-28-overhaul/report.md`（不入库）+ `docs/live-validation-checklist.md` 新条目（入库摘要）。

---

## 15. 兼容与回滚
| 变化 | 旧客户端 | 回滚 |
| --- | --- | --- |
| compact 新字段 / version 13 | 只增不删 | — |
| `act` 新参数 | 可选，不传则不变 | `turnPlansEnabled=false`（游戏内） |
| 加速 | 不影响 API | `turboMode=off` |
| 自动处理 | 决策记录多 `source:"autopilot"` | `autopilotEnabled=false` |
| 提示词瘦身 / 去掉读状态工具 | 仅游戏内提示词；API 不变 | 代码回滚（无开关；离线测试钉住关键键） |
| 视觉按需 | 截图变少 | `visionMode=every_step` |
| HUD | 无 | `hudMode=off` |
| 算术 | 字段为 null | `combatMathEnabled=false` |
| 战绩记录 | 无 | `runJournalEnabled=false` / 清空 |
| 面板默认显示 | 只影响新安装 | — |

## 16. 待实机确认（结果写回 implement.md 决策日志）
1. `Instant` 各屏稳定性（否则默认 `fast`）。2. `hand[].vs` 数值与画面一致（抽查 3 牌 × 2 敌）。3. 算术缓存后 `state_build` p95 不升。4. 本地网关模型是否愿意用 `then`（否则调提示词/示例）。5. HUD 定位在加速档下是否抖动。6. F7 是否被游戏占用。7. `stream:false`/降级规则对思考模型延迟的影响。8. 输出上限字段在本地网关的兼容性。
