# 让自动游玩既不会空转，也不会把"执行成功但很慢"当失败

## 背景

两个方向相反的缺陷，都出自同一处重试策略 `STS2AIAgent/Agent/AutoPlayRecovery.cs`：

### A. 空转：没有任何"没进度"检测

`AutoPlayRecovery.Observe`（`:20-24`）只要 `Error == null && Acted != null` 就把 `_failures` 清零，
**不比较动作前后的状态是否变化**：

```csharp
if (result.Error == null && result.Acted != null)
{
    _failures = 0;
    return (null, null, TimeSpan.Zero);
}
```

配合默认**没有请求上限**（`AgentSettings.cs:39-41` 的 `MaxSessionRequests` 默认 null，
UI 文案「不限」），"动作合法但不产生状态变化"的情形会永远被当作成功，
失败计数永不增长。全仓也没有状态指纹：`rg 'state_hash|no_progress|unchanged' STS2AIAgent/Agent` 无命中。

### B. 误停：`pending` 超时被当成失败

`STS2AIAgent/Agent/AgentLoop.cs:633-651`：动作返回 `pending`/`stable=false` 时等
`WaitUntilActionableAsync(20s)`，仍未 settle 就 `return (action, result, "Timed out waiting for a stable state after act.")`
——第三个值非 null ⇒ `Error != null` ⇒ 计入 3 连败停机。

但 `SKILL.md:84` 的契约是：「Treat `pending` responses as an instruction to stay inside the returned screen flow」，
而且这个动作**确实执行了**（返回的 JSON 里带 `previous` 与最新 state）。
慢动画（例如长结算）会让自动游玩在 3 次之后自行停机。

## 目标

1. **空转可检测**：`AutoPlayRecovery` 记录每次成功动作的"动作 + 状态指纹"，连续重复同一指纹达阈值即
   按"无进度"升级处理（沿用既有 3 连败停机语义，或新增一个专门的停机原因——以实现为准，但不能永久空转）。
2. **慢不等于失败**：`pending` 超时不再算硬失败；它走"等待"通道（不消耗失败额度），
   但**必须有上界**（连续 N 次未 settle 仍要停机），否则等于把误停换成静默无限等待。
3. 两条规则都放进**可离线编译的纯策略**，并有可否证的测试。

## 验收标准

- [x] `AutoPlayRecovery` 的成功分支不再无条件清零：重复同一 (动作, 指纹) 达阈值会升级为停机/计失败。
- [x] 指纹对"真实进度"不误报：换动作、或同一动作但状态变化，都会重置计数（测试逐场景断言）。
- [x] `AgentTurnResult` 增加一个区分"执行了但未确认 settle"的状态；`AutoPlayRecovery` 对它
      不增加失败计数、但累计到上限必停机。
- [x] 两条规则都有真值表测试（每个分支都要断言；改坏即红）。
- [x] `AutoPlayRecovery.RunAsync` 的取消/预算/配置路径行为不变（既有测试保持绿）。
- [x] C# / MCP 全量测试、`check_verification_gates.py`、preflight 通过。

## 范围外

- 不改 `AgentSettings` 的默认值（是否给用户默认加请求上限属产品决策，需要用户拍板）。
- 不改 `WaitingForGame` 的语义（它已经正确地不消耗失败额度）。
- 不改 UI 文案。
