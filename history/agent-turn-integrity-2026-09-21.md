# Agent 回合完整性修复记录

Historical snapshot / 历史快照：2026-09-21。设备 CharBook，工作分支 dev。修复前基线为 `00aad146b8bafb27a64aabb806f823f412251998`。

用户要求修复第二轮审查确认的全部 5 项问题。沿用此前要求，本轮不启动游戏、不部署到游戏目录、不调用真实付费模型，也不推送或发布。已有 `cstest.log` 和 `v.log` 保留。

## 五项修复

| 问题 | 修改后的行为 | 主要代码与测试 |
| --- | --- | --- |
| F01：已接受动作之后的状态读取失败引起重发 | 收到 bridge 成功回执即记录动作；后续读状态失败报告 pending / stable=false；本次决策不再提交第二个动作。真正的动作拒绝仍可被纠正。 | `AgentLoop.ExecuteActAsync` / `CompleteWithToolsAsync`；工具调用、聊天、JSON fallback、显式拒绝对照。 |
| F02：当轮已知 Token 未进入下一轮检查 | 每次请求前检查历史用量加当轮已知用量，包含外挂视觉；检查不写账，最终结果只入账一次。 | `SessionBudgetGuard.CheckBudget`；95 + 10 超过上限 100 时拦住下一请求；视觉后同样拦截。 |
| F03：取消导致已完成动作和使用量丢失 | 取消异常携带部分回执，恢复器先记录已发生事实再停止；run boundary 同样保留回执；没有返回的 provider Token 保持未知。 | `AgentTurnCanceledException` / `AutoPlayRecovery` / `AgentRuntime.Accounting`；完成后取消、出牌后取消、后续模型请求、读工具、视觉和 run boundary。 |
| F04：主动发言早于游玩记账导致越过请求上限 | 游玩结果和预算先提交，再进入主动发言；预算已满直接停止；共享回合门锁持续到记账完成，排队请求也不会读到旧账。 | Recovery 的 `afterTurn` / `turnGate` 与 runtime 接线；1 次上限不启动主动发言、2 次上限各记一次、主动发言取消、排队请求测试。 |
| F05：非对象动作参数越过错误处理 | 根节点先验证 Object；`[]` / `null` / 数字 / 布尔 / 字符串 / 损坏 JSON 返回 invalid_request，保留请求用量并允许模型修正。 | `AgentLoop.ParseArgs` / `TryReadActReason`；恢复与结构化错误两组测试。 |

## 记账边界

普通回合由最终结果入账。中断回合由异常携带的结果入账。两条路径互斥。自动游玩使用 recovery 的统一提交步骤；单步、聊天和队友路径在释放共享回合锁之前消费结果。中断自动游玩只更新事实记录，不把旧回合显示成仍在运行。

主动发言在游玩入账之后执行，自己的结果或取消回执单独入账一次。当前回合的预算检查只读取累计值，不提前写入；因此检查重复调用不会重复计数。

本轮新增 `AgentRuntime.Accounting.cs`，将现有会话记账与界面结果投影集中在同一 partial；主文件的大小预算从 1400 下调到 1350。没有调整公开 HTTP/MCP 请求字段、发行版本或第三方依赖。

## 回归与验证

基线为 C# 589 项和 Python 315 项。先只增加 10 项永久回归：9 项在原始代码上失败，正常单动作对照通过。证据在 `build/agent-turn-integrity-2026-09-21/red.log`。

首轮修复的测试仅被新 partial 尚未暂存的源码清单检查拦截，加入索引后通过。扩展验证已经分别达到 609 和 612 项 C# 全部通过。最终完整 `preflight-release.ps1` 已退出 0：C# **615 项通过 / 0 失败**，Python **315 项通过**，Release 构建 **0 警告 / 0 错误**，16 个预检步骤与其中 12 道验证检查全部通过。

本轮新增 **26 项 C# 回归与接线检查**。日志位于 `build/agent-turn-integrity-2026-09-21/preflight.log`；机器可读摘要为同目录的 `verification-summary.json`。`tested-source-hashes.json` 记录接受最终验证的源码摘要，收尾时逐文件核对一致。

最初的失败用例和最终测试共同证明：动作后的读失败从重复执行 2 次变为仅执行 1 次；95 + 10 Token 超过 100 的场景停止后续模型请求；完成后取消保留 1 次请求 / 25 Token；1 次请求上限拦住主动发言；非对象参数会返回结构化错误并保留已知用量。

永久回归入口：`dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release`。具体用例集中在 `STS2AIAgent.Tests/AgentTurnIntegrityTests.cs`，每项均注册到 TestRunner。

## 证据限度

模型和游戏接口使用受控假对象，排队/取消测试使用真实 Task、CancellationToken 与 SemaphoreSlim。涉及 Godot 的 runtime 通过源码接线检查和真实 Mod 编译验证；本轮没有实机线程调度、画面、真实模型用量或完整对局结果。未来实机验证应重点观察暂停恰好发生在出牌收尾、请求结束和主动发言时的界面及决策日志。
