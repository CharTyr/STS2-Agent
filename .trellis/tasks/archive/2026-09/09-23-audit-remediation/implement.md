# 实施计划：v0.12.4 后问题修复

## 门槛与基线

- [ ] 等用户在本**修订版计划**公布后另发消息明确批准实施；任务创建同意、前述三项产品选择与“单一主模型”选择不等于实施批准。
- [ ] 获批后先记录 `git status --short --branch`，将原产品代码 17 项修改的 `git diff --binary HEAD` 与 4 个未跟踪产品文件完整备份到 `.trellis/tasks/09-23-audit-remediation/` 下不打包的基线目录，列 SHA256、日期和文件清单，检查无明文密钥；备份无效则不派 worker。随后 `python ./.trellis/scripts/task.py start .trellis/tasks/09-23-audit-remediation`；禁止 reset/clean/整文件覆盖。
- [ ] worker 同步读取 PRD、design、implement 与 `.trellis/spec/mod/`、MCP、operations 指南；不提交、不 push、不部署，所有工作共享同一工作树、严格文件独占。

## 分工和依赖

| 顺序 | Worker | 修复 | 独占生产文件 | 交付 |
| --- | --- | --- | --- | --- |
| 1，并行启动 | A—会话归属 | 外部 HTTP run 归属、旧局迟到日志与同局恢复 | `Server/Router.cs` 的 `/action`、`Agent/AgentRuntime.Session.cs`、`Agent/PlaySessionMemory.cs`、必要时 `PlaySessionStore.cs`，新 `AuditRemediationSession*Tests.cs` | Run-ID 决策矩阵、恢复与 legacy/collision 测试、Router 同屏快照说明 |
| 1，并行启动 | B—Jev 引擎 | 429 一次重试逐次计费、统一 90 秒默认总期限、错误分类、Choice+Score、规划触发；在自身决策文件消费由集成者提供的下回合聊天指令 | `Llm/Jev{Client,Types}.cs`、`Agent/{JevExecutionDecider,StrategyPlanner,AgentLoop.Decision,ExecutionDecision,IGameBridge}.cs`，新 `AuditRemediationJev*Tests.cs` | 真实 attempts 收据、score/latency DTO、所需预算与指令委托签名 |
| 1，并行启动 | C—体验控制 | 切换先暂停、运行中同伴白名单配置同步、维护单一主模型、聊天/结果展示与双模式 Jev 面板 | `Ui/AgentOverlayHost.{Pages,Settings,ChatCard}.cs`、`Multiplayer/CompanionConnection.cs`、新的控制 partial/纯策略/`AuditRemediationUi*Tests.cs` | UI/控制源码与纯策略测试、主 runtime/Router 接线清单 |
| 1，并行启动 | D—简报两面 | 同源 MCP 简报投影、Python 消费、API 文档和对齐测试 | `Server/NativeMcpServer.Tools.cs`、`Agent/AgentTools.cs`、Python MCP client/server 与测试、`docs/api.md`、新纯投影 | 字段 schema、原生/sidecar 测试、Router 所需接线 |
| 2，汇合 | Integrator（主代理） | 修正共享接口并串联；注册测试、生成 OpenAPI 和本地化 | `AgentRuntime{,.Jev,.Accounting}.cs`、`AgentLoop{,.Probes}.cs`、`Tests/{TestRunner.cs,STS2AIAgent.Tests.csproj}`、`Loc.Strings.*.cs`、`scripts/api_schema.py`、`docs/openapi.json`、`CHANGELOG.md`，A 完成后串行接 `Router.cs` | 无重叠覆盖的完整链路、旧 PRD 修订、全部回归 |

同名文档或局部源冲突一律由主代理处理，不派两个 worker 写同一文件。worker 完成时写清修改清单、测试命令与输出、仍未接的接口；共享工作树不得并发 `dotnet build/run`，因为 `obj/bin` 可能相互锁住且新增测试尚未由集成者注册。worker D 可独立运行 Python 专项测试；C# 新测试均由集成者加入 `.csproj`（新纯模块才可编译进入无游戏项目）及 `TestRunner.AllTests` 后统一运行。`AgentRuntime` 基文件行数接近 ratchet，新增功能应优先落新 partial，Pages/Settings 页亦需避免新增超过行数上限。

## 验收场景

1. 未开启自动游玩，经 HTTP `/action` 操作时记录真实 run；下一局迟到事件不会切回旧 run；MCP、单步、自动游玩各入口近期记忆一致。
2. Solo→Coop 切换中 solo 正在出牌：先完成/确认暂停才更新滑块；超时或队友拒绝时保持原模式；Coop→Solo 同理；新的模式不自动开始。
3. Solo 自动游玩中发送“下次优先防御”：确认排队，后续同局 prompt 消费一次；暂停后仍可继续消费、下一局不复用，当前动作不被重放。对话动作结果简短可见；仍使用单一主模型，旧 PlayModelId 迁移至默认主模型须可见且无悬挂引用。
4. 已连接队友时切换双层与 Jev 参数，下一手生效且能够确认 companion 的真实配置；失败提示不谎报已生效。面板读取对应实例而不是主实例旧数据。
5. Jev 429→成功最多两次网络尝试，预算计数=2；预算不足/取消/总超时仅实际发生的次数；400 配置错误不重试，408/5xx 分类网络；score 与 elapsed 可用。
6. 规划层使用同 run 最近决策和当前策略，屏/Act/Boss 改变、连续低置信、N 手触发；请求受预算/取消控制；外部 MCP 两面字段/值源相符。
7. C# 测试编译并运行（修复前基线在权限变化后已重跑通过；若再次出现 `File.Replace`/监听失败仍须区分环境限制）；`dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-restore`；`mcp_server/.venv/Scripts/python.exe -m unittest discover -s tests -q`；`python scripts/check_verification_gates.py --only api-facts,api-schema` 与必要的全量静态闸门，`git diff --check`；新响应字段和控制路由须同步 `scripts/api_schema.py` 再生成 `docs/openapi.json`，不可手改生成物。仅有环境可用时可运行 `uv run --locked`。不擅自运行会启动/修改游戏的命令。

## 回滚与人工验证

- 回滚以获批后写入的原始 WIP patch + 四个未跟踪文件备份为参照，定向移除本轮增量；禁止整文件 checkout 或 reset 擦除原改动。不 commit、push、release。
- 双开/游戏线程/真实 Jev 429 和风险读数需要隔离存档与受控额度实机复测；无证据时最终报告写“未验证”，不宣称线上验收完成。
