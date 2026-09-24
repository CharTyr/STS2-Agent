# 实施计划：Jev 双层决策引擎

## 前置

- 分支：`feature/jev-two-layer-engine`（可与子任务1的 UI 分支并行，最后在 dev 汇合）。
- 读 `.trellis/spec/mod/agent-and-ui.md`、`architecture.md`、`game-actions.md`。
- 依赖子任务1的设置字段（JevBaseUrl/JevApiKey/JevModel/JevConfidenceThreshold/DualLayer*Enabled）。若并行开发，先在本任务内落地这些设置字段并与子任务1对齐，避免冲突——约定：**设置字段由子任务2（本任务）落地**，子任务1只做 UI 绑定。

## 步骤

### 1. Llm 层：JevClient
- [ ] `Llm/JevTypes.cs`：请求/响应 DTO + 序列化。
- [ ] `Llm/IJevClient.cs` + `Llm/JevClient.cs`：systemone 调用、超时、429 退避（retry-after）、错误分类、PingAsync。
- [ ] 单测：fake HttpMessageHandler 覆盖请求形/退避/取消/错误。

### 2. 设置字段落地
- [ ] `AgentSettings` 加 Jev 与双层开关字段 + `EnsureValidShape` + `SettingsClone`。
- [ ] `ModelRoleProbe.ModelRoleNames` 加 `jev`；`AgentLoop.TestConfiguredRolesAsync` 角色数组加 jev。
- [ ] 单测：round-trip / clone / 归一化。

### 3. Agent 层：决策器与策略
- [ ] `Agent/ExecutionDecision.cs`、`Agent/IActionDecider.cs`。
- [ ] `Agent/JevOptionEnumerator.cs` + 单测（各屏快照 fixture）。
- [ ] `Agent/PlayStrategy.cs`、`Agent/StrategyStore.cs` + 单测。
- [ ] `Agent/JevExecutionDecider.cs` + 单测（fake IJevClient）。
- [ ] `Agent/StrategyPlanner.cs` + 单测（fake LLM）。

### 4. 接入 AgentLoop
- [ ] `AgentLoop` 构造函数加可选 decider/store/threshold；`PlayOnceAsync` 加分支。
- [ ] `AgentTurnResult` 加 `Confidence`；`AgentRuntime` 注入并透传；`DecisionLog(Entry)` 加 `confidence`。
- [ ] 单测：置信度门控（fake bridge）、指纹填充、回退路径。

### 5. HTTP 路由
- [ ] `GET /strategy`、`POST /strategy`（IsLocal）。
- [ ] `docs/api.md` 加行（api-facts 闸门）。

### 6. MCP parity
- [ ] `AgentTools.Mcp` + `NativeMcpServer.Tools.cs` 加两工具。
- [ ] `mcp_server` client.py + server.py 加对应。
- [ ] `test_native_tool_alignment.py` + Python 工具单测。

### 7. CHANGELOG
- [ ] `CHANGELOG.md` `## Unreleased` 记录。

## 验证

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
cd mcp_server; uv run --locked python -m unittest discover -s tests -v
powershell -ExecutionPolicy Bypass -File "scripts/preflight-release.ps1"
```

- [ ] C# 测试全绿（含 GameTaskBounding 无裸 await）。
- [ ] Python 测试全绿、对齐测试通过。
- [ ] preflight 通过。
- [ ] 实机：配 Jev key → 开双层 → 单人与 companion 自动游玩由 Jev 驱动、低置信回退 LLM、决策日志带 confidence；外部 MCP agent 能读简报/调策略。

## 回滚点

- 步骤 1-3 为纯新增，独立可回滚。
- 步骤 4 接入主循环，是关键评审点（先自测门控与回退再继续）。
- 步骤 5-7 为面扩展，回滚不影响引擎。

## 评审闸

- 步骤 4 后：门控/回退/指纹/no-progress 自测。
- 步骤 7 后：trellis-check 全量 + preflight。
