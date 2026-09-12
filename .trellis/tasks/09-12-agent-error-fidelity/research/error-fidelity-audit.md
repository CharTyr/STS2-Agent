# 证据：游戏内错误的保真度落差

## HTTP 侧（保真）

`Router.WriteErrorAsync` 输出 `{ok:false, request_id, error:{code,message,details,retryable}}`，
状态码来自 `ApiException.StatusCode`。MCP 客户端（`client.py`）与文档（`docs/api.md:46` 错误码表）
都按这套字段消费。

## 游戏内侧（失真）

- `GameBridge.ActAsync`（`GameBridge.cs:73-99`）成功时序列化
  `{action, status, stable, message, state}`；异常不在此处理。
- `AgentLoop` 的 `RunActAsync`（`AgentLoop.cs` 约 :600-670）在 `catch (Exception ex)` 里
  **只**序列化 `new { error = ex.Message }`（`:665-667`），返回的 error 文本同样是 `ex.Message`。
- 因此 `ApiException` 的 `StatusCode`/`Code`/`Details`/`Retryable`（`ApiException.cs:5-20`）在
  游戏内链路上被丢弃；模型看不到 `invalid_action` vs `state_unavailable` vs `invalid_target`，
  也不知道 `retryable`。

## 为什么重要

- 代码里大量位置显式标注 `retryable: true`（例如 `GameActionService.cs` 的多个 `503 state_unavailable`），
  这个信号在游戏内链路上完全无效。
- `skills/sts2-mcp-player/SKILL.md` 的决策规则依赖"动作是否合法/是否可重试"的区分；
  只有一句 message 时模型只能靠猜。
- 两条链路（HTTP 与 in-process）本应表达同一契约，这属于契约精度漂移。

## 待实现时核实

- `error` 字段的消费点：需要在实现时用 `rg` 穷举（`STS2AIAgent/Agent`、`skills/`、`docs/api.md`），
  确认把 `error` 从字符串改成对象是否会破坏任何读取方。
- `AgentLoop` 返回的第三个值（错误文本）在 `AutoPlayRecovery` / UI 日志中的用法。
