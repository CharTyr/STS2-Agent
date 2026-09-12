# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Agent/NoProgressPolicy.cs`（新） | `RepeatThreshold=3`、`UnsettledLimit=5`、`IsRepeat(...)`、`Fingerprint(...)`（SHA-256 截断 16 位，纯 BCL） |
| `STS2AIAgent/Agent/AutoPlayRecovery.cs` | 新增 `_lastAction/_lastFingerprint/_repeats/_unsettled`，`Observe` 加"重复无进度"与"未确认"两条分支；`RunAsync` 未动 |
| `STS2AIAgent/Agent/AgentLoop.cs` | `ExecuteActAsync` 返回增加指纹与未确认标志；pending 未 settle **不再**返回错误 |
| `STS2AIAgent/Agent/IGameBridge.cs` | `AgentTurnResult` 新增 `ExecutedUnsettled`、`StateFingerprint` |
| `STS2AIAgent/Localization/Loc.Strings.Support.cs` | 2 条新停机文案的英文条目（否则 `Loc.Coverage` 红） |
| 测试 | `NoProgressPolicyTests.cs`（新，3 条）+ `AutoPlayRecoveryTests.cs`（追加 7 条行为 + 1 条形状契约） |

## 状态机（`AutoPlayRecovery.Observe`，自上而下）

| 输入 | 行为 |
|---|---|
| `RequiresConfiguration` | 不变：立即停（config） |
| `WaitingForGame` | 不变：1s 延迟，不动任何计数 |
| 成功但未确认（`ExecutedUnsettled`） | `_unsettled++`，**不**动 `_failures`；≥5 停机；否则退避 |
| 成功且已 settle | 清零 `_unsettled`/`_failures`；同一 (动作, 指纹) 重复 → `_repeats++`，≥3 停机；否则 1s 延迟；真进度零延迟 |
| `Error != null` / 无动作 | 不变：`_failures++`，≥3 停机 |

## 验收

| 标准 | 证据 |
|---|---|
| 空转可检测 | `PASS NoProgressPolicy.IsRepeatTruthTable / Thresholds / Fingerprint`；`PASS Recovery.UnchangedActionStops` |
| 真实进度不误报 | `PASS Recovery.ProgressResetsRepeat` |
| 慢 ≠ 失败 | `PASS Recovery.UnsettledBudget / UnsettledRunCleared / PendingReturnShape` |
| 未确认有上界 | `PASS Recovery.UnsettledLimit` |
| 既有语义不变 | `PASS Recovery.UnsettledKeepsFailures` 与 4 条既有 `Recovery.*`、`Loc.*` 保持绿 |
| 可否证 | 短路 `_repeats >= RepeatThreshold` → 仅 `Recovery.UnchangedActionStops` 红；未确认分支改走失败通道 → 4 条红；均还原 |

## 门禁

C# 335 PASS / 0 FAIL；`dotnet build` 0 警告 0 错误；MCP 167 OK；gates / package / preflight 全绿。

## 行为变化（须知）

pending 现在被视作"已执行"，因此 `PlayOnceAsync(stopAfterAct)` 在该轮直接结束，不再让模型于同一轮二次 `act`。

## 只能实机验证

真实慢结算是否真会触发 `ExecutedUnsettled`；compact 指纹在真实对局中是否稳定（若 `agent_view` 含易变字段会漏检）；
5 次上限与 `2^n` 退避的手感；pending 分支的端到端链路。
