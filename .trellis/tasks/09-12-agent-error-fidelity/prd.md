# 让游戏内动作失败保留错误码与可重试信号

## 背景

游戏内 Agent 走的是进程内桥接，不经过 HTTP：

`STS2AIAgent/Agent/GameBridge.cs:73-99` 的 `ActAsync` 调 `GameActionService.ExecuteAsync`，
只序列化 `{action, status, stable, message, state}`。
异常在 `STS2AIAgent/Agent/AgentLoop.cs:665-667` 被压成 `{ error = ex.Message }`：

```csharp
catch (Exception ex)
{
    return (null, JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions), ex.Message);
}
```

于是 `ApiException` 的 `StatusCode` / `Code` / `Details` / `Retryable`
（`STS2AIAgent/Server/ApiException.cs:5-20`）**全部丢失**：

- 走 HTTP 的 MCP 客户端能拿到 `error.code = invalid_action / invalid_target / state_unavailable`
  与 `retryable`（`STS2AIAgent/Server/Router.cs:259-263` 的信封）；
- 游戏内模型只拿到一句人话，**无法区分 400 / 403 / 409 / 503**，也不知道某个 503 是否
  `retryable: true`（代码里大量位置显式标注了 retryable）。

同一份状态、同一套动作，两条链路的错误信息保真度不同——这是"同一契约两种精度"的典型漂移。

## 目标

1. 游戏内动作失败的错误对象带上与 HTTP 信封**同名同义**的字段：
   `code` / `message` / `details` / `retryable`（`status_code` 可选，供模型参考）。
2. 非 `ApiException` 的意外异常也要归类（例如 `code = "internal_error"`、`retryable = false`），
   不允许退化成只有一句 message。
3. `skills/sts2-mcp-player/SKILL.md` 的共享契约块里补上"按 code 判断是否重试"的指引
   （marker 边界不动，该文件被 mod 构建期内嵌）。

## 验收标准

- [x] `AgentLoop` 的失败分支产出的 JSON 含 `error.code`、`error.message`、`error.retryable`，
      `ApiException` 时还含 `error.details` 与 `error` 顶层的 `status_code`。
- [x] 既有键 `error`（字符串）**不再**作为唯一形态；若为兼容保留，必须与结构化字段并存
      （由实现决定，但要在 evidence 里写明兼容取舍）。
- [x] 纯策略：把"异常 → 错误对象"的映射抽到可离线编译的类型里（无 Godot / 无 MegaCrit 引用），
      并附真值表测试（`ApiException` 四要素、普通异常、取消异常的分类）。
- [x] 源码契约测试：`GameBridge.ActAsync` 的序列化对象包含 `status_code`/`code`/`retryable`
      或与之等价的字段名（以实现为准），且 `AgentLoop` 的 catch 不再只序列化 `ex.Message`。
- [x] 与 HTTP 侧的信封字段名一致（测试断言两边用同一组字段名，防止再次分叉）。
- [x] C# / MCP 全量测试、`check_verification_gates.py`、preflight 通过。

## 范围外

- 不改 HTTP 信封（`Router.WriteErrorAsync`）的既有形态。
- 不改 `docs/api.md` 的动作契约块。
- 不新增错误码（沿用 `invalid_action` / `invalid_request` / `invalid_target` /
  `state_unavailable` / `invite_failed`，新增的只有"非 ApiException 兜底"这一个分类）。
