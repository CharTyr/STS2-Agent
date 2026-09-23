# 修复 v0.12.4 以来审查问题

## Goal

修复以 `v0.12.4` 至 `dev` HEAD 的审查中发现的可验证偏差；保留现有 21 项未提交改动，不让并行 worker 互相覆盖，恢复双模式互斥、对话引导、Jev 规划与会话记忆的产品契约。

## Background / evidence

- 截止本轮基线：`dev` HEAD `e11e265`，21 项未提交改动，包含 `AgentLoop.Decision.cs`、`PlaySessionMemory.cs` 及新审计测试；这些文件是现有工作，不得重置或覆盖。
- Mod Release 编译 0 警告；Python MCP 使用现有 `.venv` 执行 416 项通过、8 跳过。此前受限环境下 C# 离线测试 7 败，涉及 `File.Replace` 与 `HttpListener`；2026-09-23 权限变化后以同一 `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release --no-restore` 重跑，退出码 0、无 FAIL 行。该结果是修复前的离线基线，不代表实机通过。`python scripts/check_verification_gates.py` 的 12 项离线闸门亦全部通过（2026-09-23，修复前基线）。
- 需求基线：`.trellis/tasks/09-22-dual-mode-ui-jev-two-layer/prd.md` 及三个子任务 PRD、design；本任务是修复与集成，不替代原需求。

## Requirements

### R1 模式、对话与决策流（P1/P2）
- 模式滑块不只是显隐，单人自动游玩与本地队友自动游玩不能同时处于活动状态；切换时先等待当前模式暂停确认，再更新显示与设置，暂停失败则保持原模式，不自动启动另一模式；不破坏外部 MCP 只读/接管能力。依据父 PRD:20,40；代码 `AgentOverlayHost.Pages.cs:90-99`。
- 玩家在单人自动游玩期间能发指令影响下一次决策，也能从对话入口暂停/继续；指令不调用 `PlayIntent` 即时出牌，仅消费一次、有容量和长度上限、按同一 run 隔离，切局不复用，取消/暂停后不丢未消费指令，不重放已接受动作、不打断正在执行的动作；原 PRD:21；当前 `AgentRuntime.cs:619-623` 拒收，`AgentLoop.Decision.cs:92-148` 不消费历史。
- **不恢复独立 `PlayModelId`。** 用户在 2026-09-23 确认以较新的 `.trellis/tasks/09-22-pr196-live-fixes/plan.md:30-35` 为准：聊天/游玩共用一个主模型；`AgentOverlayHost.Settings.cs:761-768` 清除旧绑定是预期迁移，而不是本轮缺陷。旧 UI PRD:29 的“角色绑定”需按该后续决定解释；验证旧配置迁移不造成不可见的错误引用。
- 对话流展示操作/工具结果，不仅动作名和理由；原 UI PRD:17；当前 `AgentRuntime.Accounting.cs:78-92` 未写入 `ActResultJson`。用户选择维持“明确短语才可代打一手”，不恢复“允许代打”勾选；自动游玩进行中的聊天仅作为下一手策略指令，不并发执行代打，需同步修订旧 UI PRD:18 与其 design.md:31。

### R2 Jev 执行与规划（P1/P2）
- 已启动的 companion 能即时接收多人双层开关变更，且与主实例的 Jev 配置一致；不能只更新主实例 settings。父 PRD:43；`AgentOverlayHost.Pages.cs:340-345`、`CoopLaunchPolicy.cs:80-94`。
- `get_planner_briefing` 两个 MCP 面及底层 HTTP 应提供同源 run 摘要、当前屏、当前策略、近期 Jev 决策和置信趋势；仅新增可空字段，不移除既有字段。引擎 PRD:30；`NativeMcpServer.Tools.cs:125-142`、`Router.cs:246-258`。
- 规划器使用近期决策和当前策略，上下文切换/连续低置信/N 手周期触发；保持只读、预算归属、取消与版本保护。引擎 PRD:17；`StrategyPlanner.cs:45-56,85-95`。
- 对齐 Choice + 可选 Score 危险度展示；Jev UI 两种模式可见且显示动作、置信度、概率、延迟，不能显示旧实例伪数据。引擎 PRD:11、UI PRD:25。
- Jev 429 最多按有上限的 `retry-after` 在同一手重试一次，**每次实际网络尝试单独计入请求预算**，整个 Jev 回合共享总超时；不足预算不得重试，失败/取消保留尝试收据。普通 4xx/408/429/5xx 与超时分类明确；当前 `AgentRuntime.Jev.cs:159-163`、`JevClient.cs:186-218,284-289`。此决定取代旧 Unreleased 中“单次 transport attempt”的口径，需同步 CHANGELOG。

### R3 按 run 会话持久化（P1/P2）
- 外部 HTTP `/action` 不启动自动游玩也能按真实游戏 run 归属决策，供近期记忆和磁盘 session 使用；不要仅使用 `_runBoundary.RunId`。`Router.cs:461-467`、`AgentRuntime.Session.cs:22-29`。
- 延迟到达的旧局日志不能将当前 session 切回旧 run；按真实当前局切换，按日志所属局记录并隔离。`AgentRuntime.Session.cs:22-29,77-102`。
- 会话保存备份/旧文件迁移与删除保持可恢复，不因环境受限的 `File.Replace` 测试失败而掩盖实际代码问题；在正常环境复核新增 `Audit.StoreSavePreservesCollidingLegacyRun`。

## Constraints / out of scope

- 不重置用户脏工作树，不提交、不发布、不部署覆盖游戏 DLL；任务开始前以时间戳/校验清单保存原 21 项（17 已修改、4 未跟踪）工作树快照在任务目录或隔离备份内。worker 独占生产文件和各自的新测试文件，集成者负责跨域代码、公共注册及定向回滚；不能按文件整体撤销覆盖原脏改动。
- HTTP/MCP 状态与字段向后兼容；两 MCP 面保持双向对齐；保持游戏线程边界、有界等待、同一回合请求/Token 记账及取消回执。
- 不把离线编译/单测称为实机验收；实机与真实 Jev 服务若不可安全执行，应列人工复验清单。

## Acceptance Criteria

- [ ] R1 单人/多人选择运行时互斥、运行中指令按 run 排队并参与下一决策、暂停/继续、主模型迁移与对话结果有可编译的纯逻辑/源码回归；Godot UI 及双实例行为另列实机验收。
- [ ] R2 companion 双层设置即时生效；原生与 Python MCP 简报字段一致、风险分数/延迟 UI 可见；规划刷新/记账和 429 策略有确定性测试。
- [ ] R3 HTTP/原生 MCP/自动游玩各入口的 run 归属一致，旧局延迟记录不污染新局，继续游戏可恢复决策/聊天/策略。
- [ ] C# 单测、Mod Release build、Python MCP 单测及相应静态闸门通过；环境受限项目单列；`CHANGELOG.md` 的 Unreleased 与 API 文档同步。

## Product decisions

- 模式滑块先暂停确认再切换，不自动启动另一个模式。
- 保留明确短语代打一手，不恢复“允许代打”复选框；运行中聊天只影响后续决策。
- Jev 429 最多重试一次，逐次计费、预算前置并保持单回合总超时。
- 延续 09-22 较新实机需求：单一主模型用于聊天/游玩，不恢复独立游玩模型选择器。
