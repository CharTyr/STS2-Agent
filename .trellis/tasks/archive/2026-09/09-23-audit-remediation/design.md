# 设计：审查问题分层修复与并行集成

## 设计前提

- 在当前 `dev` 脏工作树上**增量修复**；产品代码的原始基线是 17 个修改 + 4 个未跟踪文件（21 项），另有本任务写入的规划文件。开始编码前，集成者在任务目录保存原改动 patch、四个未跟踪文件的副本及 SHA256/文件清单（注意备份不入发行包、不得含明文密钥），再记录 `git status`。worker 只写分配文件；回滚只能定向逆向应用本轮增量或恢复对比基线，绝不能整文件 checkout 覆盖原脏改动。公共 `TestRunner.cs`、`STS2AIAgent.Tests.csproj`、`CHANGELOG.md` 与原需求修订由集成者独占。
- 按现有 `IGameBridge`、`AgentRuntime`、`GameThread`、`DecisionLog.Recorded` 和双 MCP 面工作；新增字段只增不删，确保旧客户端仍能调用 `/companion/control` 和 `/strategy`。
- `run_id` 是当前游戏状态的身份，`_runBoundary.RunId` 只是进程内自动游玩会话的守卫，不能作为外部动作的唯一身份来源。动作日志与当前 UI session 是**两个不同概念**。

## A. 会话归属与延迟日志（worker A；基础层先交付）

`Router` 在动作前同一次游戏线程工作单元中读取状态 run ID、执行一次 `GameActionService.ExecuteAsync`；动作后用**前快照** ID 记账，而不是 `_runBoundary.RunId`。不为日志读取另跑一遍动作，也不重放模糊超时的 HTTP 动作。`OnSessionDecision` 只处理与活跃会话身份匹配的决策；旧局迟到条目仍留全局诊断日志但不得重载旧会话。`/state`/原生 MCP 读取的真实状态可建立当前 run，且 `ObserveSessionState` 来自旧请求的迟到帧也必须避免反向切局。会话切换由新鲜状态确认驱动，不由任意日志事件驱动；测试覆盖未知 run、纯 HTTP、Native MCP、新局、旧局迟到与恢复。

*边界*：worker A 独占 `Router.cs`（限 `/action` 与必要的 `/state` 观察）、`AgentRuntime.Session.cs`、`PlaySessionMemory.cs`，可审视并修补现有脏的 `PlaySessionStore.cs`（仅针对已证实的 legacy/collision 回归），新增名称前缀 `AuditRemediationSession*Tests.cs` 的专项测试；不得改 `AgentRuntime.cs`、公共测试注册或 Native MCP 文件。Router 中规划简报字段及 companion 控制扩展待 A 交付后由集成者串行修改。

## B. Jev 请求、规划、风险读数（worker B）

保持 `JevClient` 游玩实例 `maxRetries: 0`，由 `JevClient` 的 429 `JevException` 附上安全解析的可空 `RetryAfterSeconds`，`JevExecutionDecider` 据此最多**额外调用一次** `IJevClient.SystemOneAsync`；退避最长 30 秒并用整个回合的 linked CTS 截断。统一端到端 deadline 取已配置 Jev 请求超时（默认 90 秒），两次 HTTP + 退避共用，不能按次重新计 90 秒。decider 按真实尝试返回 `RequestsSpent`；取消、失败、回退须保留完整收据。额外请求前经注入的 `Func<int,bool>` 或等价回调检查 `_budgetGuard.CheckBudget(extraRequests: attemptsSoFar + 1)` 是否放行；`_turnGate` 已串行本机游玩/聊天，其他规划请求先占额度，回合结束 `Observe` 统一提交，不预扣也不重复计费。非限流 4xx→配置，408/429/5xx/超时→网络，保留 `RateLimited` 分类以触发退避。设置页 Ping 保持原独立重试策略，不计游玩预算。

策略刷新按换屏/Act/Boss、连续低置信、每固定 N 手触发，prompt 使用当前策略和**同局**近期决策，尽量共享简报投影以避免 MCP 与内置 planner 数据漂移。Choice + 可选 Score 危险度转为可空值；总延迟包括实际请求和等待。不要把 score 当作信心或替代 Choice；新增 `AgentTurnResult` 的可空观察字段应兼容旧 JSON。

*边界*：worker B 独占 `JevClient.cs`、`JevTypes.cs`、`JevExecutionDecider.cs`、`StrategyPlanner.cs`、`AgentLoop.Decision.cs`、`ExecutionDecision.cs`、`IGameBridge.cs`（仅为 `AgentTurnResult` 增加兼容字段），以及名称前缀 `AuditRemediationJev*Tests.cs` 的测试。B 同时负责在 `AgentLoop.Decision.cs` 注入运行中聊天的下一回合指令读取（集成者提供同 run、一次性读取委托）；不得改 `AgentRuntime.Jev.cs`/`AgentLoop.cs`，需将精确签名交集成者。现有 `AuditStateAndPlannerTests.cs`/`AgentTurnIntegrityTests.cs` 由集成者统一升级以适配变更。

## C. 模式控制、聊天与设置 UI（worker C）

由 `AgentRuntime.PlayControl.cs` 暴露可等待的 solo 暂停确认，使用已存在的 companion 暂停协议所采用的 30 秒上限；companion 使用受令牌保护的控制 API 并确认 `phase=paused`。切换操作串行：暂停未确认则保留旧模式与可见状态，不自启另一模式；外部控制路由启动游玩时也需检查当前模式，避免绕过 UI 互斥。队友配置只同步白名单 `DualLayerCoopEnabled`、`JevBaseUrl`、`JevApiKey`、`JevModel`、`JevConfidenceThreshold`、`JevRequestTimeoutSeconds`，不整份覆盖同伴 settings；密钥已经在双开启动时的 settings 副本中共享，本次通过 token+loopback 控制路由传递新增的仅是**运行中变更**，不得记录请求体、响应或错误里的密钥。同步失败可见并保持主实例开关状态，但不得声称队友已切换；从下一回合生效，不中断已开始的动作。旧 `{"running":bool}` 仍有效，扩展可选 `settings` patch 的控制请求由集成者串行在 Router 实施。

运行中聊天入队为按 run 绑定、受容量/长度限制的一次性下一手指令，不在本轮调用 `PlayIntent` 或 play tool；取消/暂停不吞掉未消费内容，切局清理，空闲聊天维持已有“明确短语才代打一手”。显示“下一手生效”/失败回执，进 prompt 的用户指令须以非特权内容标记且脱敏。对话内展示压缩后的动作执行结果；维持较新实机需求的**单一主模型**，不恢复独立游玩选择器。Jev 面板显示对应实例真实动作、分布、Score、延迟：companion 读数经已有 token 校验的本机连接扩展 `/companion/control` 响应或独立受保护 GET 传递，未配置/断线时空态，不读取主实例旧数据。

*边界*：worker C 独占 `AgentOverlayHost.Pages.cs`、`AgentOverlayHost.Settings.cs`、`AgentOverlayHost.ChatCard.cs`、`CompanionConnection.cs`，可新建 `AgentRuntime.PlayControl.cs`、`AgentRuntime.CompanionSettings.cs`、纯策略 `PlayModeSwitchPolicy.cs` / `CompanionSettingsPatch.cs`、新的 `AgentOverlayHost.PlayControl.cs`（避免 Pages.cs 930/1000、Settings.cs 922/1000 的 ratchet），及前缀 `AuditRemediationUi*Tests.cs` 的测试。Godot 依赖的 runtime partial **不得直接加入** C# 测试项目，测试编译只加纯策略/patch 文件，运行时 wiring 用源码契约测试。新增文案找集成者在 `Loc.Strings.Ui/Runtime.cs` 同步英文。不得改 `AgentRuntime.cs`、`AgentRuntime.Session.cs`、`AgentRuntime.Jev.cs` 或 `TestRunner.cs`；提交接线清单。

## D. 原生与 Python MCP 简报（worker D）

先定义统一且向后兼容的 projection：保留 `strategy`、`dual_layer`、`jev_configured`，新增 `run_summary`、`screen`、`recent_jev_decisions`、`confidence_trend`（缺失时 null/空集合，不伪造）；B 的内置 planner 暂时从同一 `GameBridge` 状态与同局 DecisionLog 读上下文，D 的外部简报使用统一的纯 C# DTO/projection。原生 MCP 通过 `IGameBridge` 只读当前状态，与 run-filtered decision log 生成简报；Python sidecar 从 `GET /strategy` 获得同名字段并包装而不是自己编造。新增响应字段的真实等价测试（不仅工具名/schema 对齐）及脱敏测试；文档 API 和 OpenAPI 同步。

*边界*：worker D 独占 `NativeMcpServer.Tools.cs`、`AgentTools.cs`（仅简报说明/schema）、MCP Python client/server、`test_native_tool_alignment.py` 与新专项测试、`docs/api.md`；可增加新的纯 `PlannerBriefingProjection.cs` 及测试，先与集成者约定同名字段。不得写 `Router.cs` 或原生 transport 基文件；把需要的 HTTP 响应字段、投影签名、base wiring 交给集成者，待 A 完成后串行接 Router。

## 集成与所有权

集成者负责 `AgentRuntime.cs`、`AgentRuntime.Jev.cs`、`AgentRuntime.Accounting.cs`、`AgentLoop.cs`、`AgentLoop.Probes.cs`、`PlaySessionRecord.cs`、`StrategyStore.cs`、`JevOptionEnumerator.cs`、`Companion/CoopLaunchPolicy.cs`（若确需）、`NativeMcpServer.cs`（若 D 提出签名需求）、`TestRunner.cs`、`.csproj`、`Loc.Strings.Ui/Runtime.cs`、`scripts/api_schema.py` 与生成的 `docs/openapi.json`、`CHANGELOG.md`、现有 `Audit*Tests.cs` 及其余共享源码契约。本任务既有脏文件基线须先备份。聊天指令队列属于新 `AgentRuntime.PlayInstructions.cs`（集成者），B 负责 `AgentLoop.Decision.cs` 的 prompt 读取委托，A 负责防止旧局迟到帧重置队列；需明确预留进入策略/状态摘要的优先级且不调用 PlayIntent。先合 A 的 run 归属，再接 C 的模式与聊天、B 的 Jev 预算，最后 D 的简报；所有共享文件由集成者串行改，worker 不跨边界。新专项测试文件由集成者在 `.csproj`/`TestRunner` 注册后统一编译运行，worker 仅提供独立的纯逻辑检验或 Python 单测；不能将其未编译测试宣称通过。

## 安全、兼容与回滚

- 不打开第二个游戏或替换 live DLL；不调用线上 Jev API；密钥永远不入日志、session、测试 fixture、GET 简报。
- 模式暂停失败是失败关闭，保留原模式；429 第二次请求在预算/总期限任一不足时不发，留下第一次真实收据。
- HTTP/MCP 新增字段可空，旧请求维持原 schema；会话存储写失败 best-effort、备份及旧路径恢复保持既有契约。
- 回滚只逆向应用本轮增量并对比任务起始 patch/未跟踪文件快照，不整文件还原原脏工作树；输出真实离线验证与实机待测表。
