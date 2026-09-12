# 证据：空转与误停

## 空转侧

- `AutoPlayRecovery.Observe`（`AutoPlayRecovery.cs:20-24`）：`Error == null && Acted != null` → `_failures = 0`。
  **不比较状态变化**。
- 无状态指纹：`rg 'state_hash|stateHash|no_progress|unchanged|SameState|duplicate' STS2AIAgent/Agent` → 无命中。
- 默认无预算兜底：`AgentSettings.cs:39-41` `MaxSessionTokens/MaxSessionRequests` 默认 null；
  `SessionBudgetGuard.CheckBudget`（`:26-41`）仅在有限值时才停。
- `AgentRuntime` 侧只把 `_lastAction` 用于 UI 显示（`AgentRuntime.cs:42/182/602/1053`）。

## 误停侧

- `AgentLoop.cs:633-651`：`IsUnsettled(result)`（`ActIndexValidator.cs:137-166`：
  `status=="pending"` 或 `stable==false`）→ `WaitUntilActionableAsync(20s)`；
  仍未 settle 就返回第三个值 `"Timed out waiting for a stable state after act."` ⇒ `Error != null` ⇒ 计失败。
- 契约参照：`skills/sts2-mcp-player/SKILL.md:84`「Treat `pending` responses as an instruction to
  stay inside the returned screen flow」——pending 是"留在该流程里"，不是"失败"。

## 判据来源（既有实现可参照）

- `AutoPlayRecovery.RunAsync` 已区分 `AutoPlayStoppedException` / `OperationCanceledException` /
  其它异常（`AutoPlayRecovery.cs:52-58`）。
- `StopKindPolicy` 提供停机种类；实现新停机原因时应优先复用既有 kind，避免新增对外枚举值。

## 待实现时确认

- `AgentLoop` 返回的第三个值被谁消费（`AgentRuntime` 的 `AccountTurn`/`ApplyPlayResult`、
  日志、UI 文案），确认"pending 不再算失败"不会让某处显示成成功。
- `WaitingForGame` 的既有语义（1s 延迟、不消耗失败、不清零既有失败）必须保持不变。
