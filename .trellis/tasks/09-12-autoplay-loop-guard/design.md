# 设计：两个方向相反的守卫

## 1. 纯策略：`STS2AIAgent/Agent/NoProgressPolicy.cs`（新，纯 BCL）

```csharp
internal static class NoProgressPolicy
{
    public const int RepeatThreshold = 3;   // 同一 (动作, 指纹) 连续出现多少次算空转

    // 指纹只在"成功执行"时更新；换动作或状态变化都视为进度
    public static bool IsRepeat(string? previousAction, string? previousFingerprint,
                                string? action, string? fingerprint);

    // 连续未确认（pending 超时）的上界
    public const int UnsettledLimit = 5;
}
```

阈值必须是常量且被测试断言（避免实现里写魔数）。

## 2. `AutoPlayRecovery` 的状态机

```csharp
private int _failures;          // 既有：硬失败
private string? _lastAction;    // 新
private string? _lastFingerprint;
private int _repeats;           // 新
private int _unsettled;         // 新
```

`Observe` 分支表（实现时逐条落地）：

| 输入 | 行为 |
|---|---|
| `RequiresConfiguration` | 不变：立即停（Configuration） |
| `WaitingForGame` | 不变：1s 延迟、不消耗失败 |
| 成功且有进度（动作变了或指纹变了） | 清零 `_failures`/`_repeats`/`_unsettled`，记下新动作+指纹 |
| 成功但同一 (动作, 指纹) 重复 < 阈值 | 延迟（沿用退避或 1s），`_repeats++` |
| 成功但重复 ≥ 阈值 | 停机（`StopKindPolicy` 里选一个已有 kind 或复用现有"连续未成功"文案），并给出可读原因 |
| 执行了但未确认（新字段） | `_unsettled++`，不消耗 `_failures`；达 `UnsettledLimit` 停机；延迟沿用退避 |
| `Error != null` | 不变：`_failures++`，≥3 停机 |

## 3. `AgentTurnResult`（`STS2AIAgent/Agent/AgentLoop.cs` 或定义处）

新增一个布尔字段表达"已执行但未确认 settle"，由 `AgentLoop` 在 `!settled` 分支置位并把
`Error` 留空（返回第三个值用 null 或保留文案但不作为失败依据——实现时按"谁读第三个值"决定，
必须在 evidence 里列出该值的消费点）。

`AgentLoop.cs:633-651` 的返回需要相应调整：状态仍是 `pending`（JSON 不变），
但**不再**让上游把它算成失败。

## 4. 指纹来源

指纹要便宜且稳定：对 `GetCompactStateJsonAsync` 的字符串做一次哈希即可
（例如 `System.Security.Cryptography.SHA256` 前 16 字节的十六进制，或直接比较长度+字符串相等）。
**不要**引入新的游戏 API；不要对每个 token 做序列化比较（compact 已经是字符串）。
实现时注意：`AgentLoop` 已经在若干路径取了 compact state（`checkState?.Invoke(...)`），
尽量复用它而不是重复请求。

## 5. 测试

- `NoProgressPolicyTests`（行为，离线）：`IsRepeat` 真值表（null 处理、动作相同指纹相同、
  动作相同指纹不同、动作不同指纹相同）、两个常量被显式断言。
- `AutoPlayRecoveryTests`（行为，离线）：喂 `AgentTurnResult` 序列，断言
  ① 重复同一指纹达阈值 → 停机；② 换动作 → 不停机；③ 未确认状态连续 N 次 → 停机且不影响
  `_failures`（用随后一次硬失败是否仍需 3 次来间接断言）；④ 既有 3 连败语义不变。

`AutoPlayRecovery` 已在测试工程编译清单里（`..\\STS2AIAgent\\Agent\\AutoPlayRecovery.cs`），
所以这些必须是**真行为测试**，不是源码契约断言。
