# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Agent/AgentErrorEnvelope.cs`（新） | 纯 BCL 的"异常 → 错误对象"映射；字段名与 HTTP 信封一致 |
| `STS2AIAgent/Agent/AgentLoop.cs` | `ExecuteActAsync` / `ExecuteReadToolAsync` 的 catch 改用共享信封；新增"桥接错误信封不得被当成成功"的判定 |
| `STS2AIAgent/Agent/GameBridge.cs` | `ActAsync` 捕获 `ApiException` 并复用同一信封；成功载荷与其它异常语义不变 |
| `STS2AIAgent.Tests/AgentErrorEnvelopeTests.cs`（新） | 8 条（真值表 + 字段名对照 + 源码契约） |
| `skills/sts2-mcp-player/SKILL.md` | 共享契约块新增 `## Action Failure Rules`（marker 未动） |

## 验收

| 标准 | 证据 |
|---|---|
| 失败 JSON 带 code/message/retryable（ApiException 另带 details/status_code） | `PASS AgentErrorEnvelope.ApiExceptionKeepsItsHttpMetadata / ApiExceptionDefaultsArePreserved` |
| 非 ApiException 归类不丢 | `PASS UnexpectedFailureIsClassifiedNotDropped`（`internal_error` / null / null / false） |
| 纯策略可离线测 | `PASS CancellationIsClassifiedWithoutRetry / EnvelopeReadsBackAsTheFailureText` |
| 与 HTTP 信封字段名一致 | `PASS EnvelopeFieldNamesMatchTheHttpRouter`：解析 `Router.WriteErrorAsync` 得 `[code,details,message,retryable]`，运行时序列化得 `[code,details,message,retryable,status_code]`，集合相等 |
| 源码契约 | `PASS GameBridgeActFailureCarriesTheEnvelope / AgentLoopFailureCarriesTheEnvelope`（catch 不再只序列化 `ex.Message`） |
| 可否证 | `CodeFor` 恒返回 `internal_error` → 1 条红；`IsRetryable` 恒 true → 3 条红；均已还原 |

### error 字段消费点（穷举后的兼容结论）

`NativeMcpServer.LooksLikeError` 只看"有没有 error 键"；`AgentTurnResult.Error`（string）仍保留
`ex.Message`，所以 `AutoPlayRecovery` / `StopKindPolicy` 行为不变；`ExecuteActAsync` 的三处参数预校验
仍用字符串 error（它们不是异常、没有 code 语义，且 PRD 不新增错误码）。没有任何消费点把 `error` 当字符串解析。

## 门禁

C# 335 PASS / 0 FAIL；`dotnet build` 0 警告 0 错误；MCP 167 OK；gates / package / preflight 全绿。

## 只能实机验证

真实 `state_unavailable (retryable:true)` 经 `GameBridge → AgentLoop` 后端到端形态；
原生 MCP 客户端对"错误对象信封"的可读性；新 SKILL.md 规则对模型重试决策的实际影响。
