# 设计：错误对象的结构化

## 1. 新文件 `STS2AIAgent/Agent/AgentErrorEnvelope.cs`（纯 BCL，可离线编译）

```csharp
internal static class AgentErrorEnvelope
{
    public const string InternalErrorCode = "internal_error";

    // ApiException 以外的一切异常都归到这里；取消由调用方先行处理，不应到达这里。
    public static bool IsRetryable(Exception exception);          // ApiException.Retryable，否则 false
    public static string CodeFor(Exception exception);            // ApiException.Code，否则 InternalErrorCode
    public static int? StatusCodeFor(Exception exception);        // ApiException.StatusCode，否则 null
    public static object? DetailsFor(Exception exception);        // ApiException.Details，否则 null
    public static object ToPayload(Exception exception);          // 上面四者组成的匿名对象
}
```

**不能**引用 `ApiException` 吗？可以——`ApiException` 本身是纯 BCL（`Server/ApiException.cs`），
而且已经被编译进测试工程（上一轮的 `ApiExceptionTests`）。所以本文件也可以进测试工程，
由主代理注册。

若实现时发现把 `StatusCode` 放进 payload 会与既有消费者冲突，可只在 `ApiException` 时输出，
但要在 evidence 里给出理由与实测。

## 2. `AgentLoop.cs:665-667`

```csharp
catch (Exception ex)
{
    var payload = AgentErrorEnvelope.ToPayload(ex);
    var json = JsonSerializer.Serialize(new { error = payload }, JsonOptions);
    return (null, json, AgentErrorEnvelope.CodeFor(ex));
}
```

注意返回的第三个值（错误文本）会被 `AutoPlayRecovery` 之类的地方当作"失败原因"使用，
实现时要确认它仍是非空字符串（用 `ex.Message`，不要改成 code）。

## 3. `GameBridge.ActAsync`

成功路径不变。若实现选择在桥接层也捕获异常，必须与 `AgentLoop` 用**同一个** `AgentErrorEnvelope`，
不允许两处各写一份字段名。

## 4. 测试

- `AgentErrorEnvelopeTests`（行为测试，离线）：
  `ApiException(409, "invalid_action", "m", details, retryable: true)` →
  `CodeFor`/`StatusCodeFor`/`DetailsFor`/`IsRetryable` 四要素逐个断言；
  `new InvalidOperationException("x")` → `internal_error` / `null` / `null` / `false`；
  真值表必须逐值断言（改一个分支就变红）。
- 源码契约：`AgentLoop` 的 catch 块含 `AgentErrorEnvelope`；`GameBridge.ActAsync` 的
  序列化对象与 HTTP 信封字段名一致（把 `Router` 的字段名与桥接侧字段名做集合比对，
  以 `AgentSourceFixture` 读文本）。

## 兼容

- agent 侧唯一的消费者是 LLM（读 JSON），多出字段只增不减，安全。
- `error` 从字符串变对象属**破坏性**变化；若 `skills/` 或 `PlayPrompt` 里有地方把 `error` 当字符串读，
  实现时必须一并修正（在 evidence 里列出全部消费点）。
