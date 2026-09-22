# 设计：Jev 双层决策引擎

## 架构总览

```
┌────────────────────────────────────────────────────────┐
│ 规划层（LLM，低频）                                     │
│  游戏内：StrategyPlanner（只读 ILlmClient）             │
│  外部：  coding agent 经 MCP get_planner_briefing /     │
│          update_play_strategy                          │
│  二者都写同一个 StrategyStore                           │
└──────────────────────┬─────────────────────────────────┘
                       │ PlayStrategy（posture/instructions/hints）
                       ▼
┌────────────────────────────────────────────────────────┐
│ 执行层（Jev，每手）                                     │
│  AgentLoop.PlayOnceAsync 分支：                         │
│   GetActionSnapshotJsonAsync（同帧 state+actions）      │
│   → JevOptionEnumerator 展开具体选项                    │
│   → JevExecutionDecider.DecideAsync（IJevClient）       │
│   → confidence≥阈值 ? ExecuteActAsync : 回退 LLM 路径   │
└────────────────────────────────────────────────────────┘
```

## 新增类型（全部新文件，遵守行数 ratchet）

### Llm 层
- `Llm/JevTypes.cs`：DTO。请求 `{state, model, questions{id:{type,instructions,criteria}}}`；响应 `{model, answers{id:{type, choice|score|noul, probabilities, confidence}}, usage{input_tokens, output_tokens}}`。System.Text.Json 序列化，纯 DTO 可单测。
- `Llm/IJevClient.cs`：`Task<JevResponse> SystemOneAsync(JevRequest, CancellationToken)` + `Task<string> PingAsync(CancellationToken)`（列模型 `/v1/models` 验证密钥）。
- `Llm/JevClient.cs`：实现。自持 `HttpClient`（`DefaultLlmClientFactory.SharedHttp` 是 private static，不强行复用）；显式 `Timeout`；429 读 `retry-after` 退避重试（上限次数）；错误映射 `JevException{Kind}`（config/network），对齐 `ModelRoleProbe.FailureKind`。

### Agent 层
- `Agent/IActionDecider.cs`：`Task<ExecutionDecision> DecideAsync(string snapshotJson, PlayStrategy strategy, CancellationToken ct)`。
- `Agent/ExecutionDecision.cs`：record `{Action, CardIndex, TargetIndex, OptionIndex, X, Y, Tool, Reason, Confidence, Probabilities, Usage, RequestsSpent, Error}`，字段对齐 `AgentTurnResult` 便于回填。
- `Agent/JevOptionEnumerator.cs`：纯逻辑。解析 action snapshot JSON → `List<JevOption>{Id, Description, Action, CardIndex, TargetIndex, OptionIndex, X, Y, Tool}`。按屏展开：
  - COMBAT：`play_card`×手牌×合法目标、`end_turn`、药水；
  - MAP：`choose_map_node`×`map.options`；
  - REWARD/CARD_SELECTION/SHOP/REST/CHEST/EVENT/CAPSTONE/BUNDLE 等：按 `available_actions` 的 `requires_index/requires_target` 与 state 中对应列表展开；
  - 兜底：无参数动作各一项。超 255 项时按优先级截断（战斗出牌优先）。
- `Agent/JevExecutionDecider.cs`：`IActionDecider` 默认实现。构造 Choice（criteria=选项Id→Description，instructions 分面：goal/当前屏 playbook `PlayPrompt.PlaybookGuidance(screen)`/策略 strategy/合法性提醒）+ 可选 Score(danger)。调 `IJevClient`，把 choice 映回 `ExecutionDecision`。
- `Agent/PlayStrategy.cs`：record `{Posture, Instructions, OptionHints, UpdatedAt, Source}`；`PlayStrategy.Default` 内置。
- `Agent/StrategyStore.cs`：线程安全持有当前策略（`Current`/`Update`）。
- `Agent/StrategyPlanner.cs`：触发判断（屏/Act/Boss 变化、低置信连续≥N、每 M 手）+ 调只读 LLM 产出策略 JSON → 解析 → `StrategyStore.Update`。复用 `AgentLoop.ChatAsync` 只读模式（`ChatOptions{ReadOnly=true}`）。

## 接入点（AgentLoop.PlayOnceAsync 分支）

在 `AgentLoop.cs:120` 取到 `stateJson` 后、`:129` 组装 prompt 前：

```csharp
if (_decider is { } decider && _strategyStore is { } store)
{
    var snapshotJson = await _bridge.GetActionSnapshotJsonAsync(ct);   // 同帧 state+actions
    var decision = await decider.DecideAsync(snapshotJson, store.Current, ct);
    if (decision.Error == null && decision.Confidence >= threshold)
    {
        var actArgs = JsonSerializer.Serialize(decision.ToActArguments());
        var outcome = await ExecuteActAsync(actArgs, ct, checkState, onAccepted);
        return new AgentTurnResult { Acted=..., ActResultJson=..., StateFingerprint=..., // 必须填
                                     Reasoning=decision.Reason, Usage=decision.Usage,
                                     RequestsSpent=decision.RequestsSpent, Confidence=decision.Confidence };
    }
    // 低置信或失败：触发 StrategyPlanner 刷新，落入现有 LLM 路径
}
```

- `AgentLoop` 构造函数加可选 `IActionDecider` + `StrategyStore` + threshold 提供者（现有已有 4 个可选 `Func<>`，风格一致）。
- `AgentRuntime` 构造处注入；按 `AgentSettings` 的 `DualLayerSoloEnabled`/`DualLayerCoopEnabled` 与当前实例角色决定是否启用。
- `AgentTurnResult` 加可空 `Confidence`（init-only，向后兼容）。
- `AgentRuntime.RecordDecision` 透传 confidence；`DecisionLogEntry` 加可空 `confidence`。

## MCP parity

- HTTP：`GET /strategy`（当前策略+简报）、`POST /strategy`（写策略），均 `IsLocal`。
- 原生面：`AgentTools.Mcp` 加两工具 schema；`NativeMcpServer.Tools.cs` 加 dispatch。
- Python：`client.py` 加 `get_strategy`/`update_strategy`；`server.py` 在 guided 注册两工具。
- `test_native_tool_alignment.py` 通过（豁免表为空）。

## 错误与边界

- Jev 失败/超时/低置信 → 回退 LLM 路径，不抛停整局；连续失败由 `AutoPlayRecovery` 既有策略兜底。
- 取消：全程传 `CancellationToken`；`AgentTurnCanceledException` 交回 receipt。
- 预算：每次 Jev 调用 `RequestsSpent+=1`；usage 未知保持 null。
- no-progress：Jev 路径必须填 `StateFingerprint`。

## 测试

- C#（`STS2AIAgent.Tests`，`dotnet run`）：JevTypes 序列化、JevClient（注入 fake HttpMessageHandler：URL/请求体/429 退避/取消/错误分类）、JevOptionEnumerator（各屏快照 fixture）、JevExecutionDecider（fake IJevClient）、置信度门控分支（fake bridge）、StrategyStore/PlayStrategy 解析、DecisionLog confidence 字段、SettingsClone 新字段。
- Python（`mcp_server/tests`，`uv run --locked python -m unittest discover -s tests -v`）：新工具 DummyClient 测试 + `test_native_tool_alignment.py`。

## 兼容与回滚

- 全部新增为可选路径；双层开关默认关，现有行为不变。
- API 只增不删；`confidence` 可空。
- 回滚 = revert；无持久化破坏性变更。
