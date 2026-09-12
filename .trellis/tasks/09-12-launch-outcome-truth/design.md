# 设计：结构化启动结果

## 数据流

```
GameActionService.ExecuteInviteAiTeammateAsync (GameActionService.cs:5085-5130)
  └─ await AgentRuntime.LaunchDualInstanceAsync        ← 目前无返回值，只能回读文案
        └─ LaunchDualInstanceCoreAsync (AgentRuntime.cs:713-765)
              ├─ gate 抢占失败 (_dualLaunchGate.WaitAsync(0) 失败) → InProgress
              ├─ 队伍消息 / 队伍控制 pending          → Rejected
              ├─ CoopLaunchPolicy.GetError != null    → Rejected
              ├─ await HostLocalCoopAsync(...)        → Succeeded / Failed（需结构化）
              ├─ OperationCanceledException           → Canceled
              └─ 其它异常                              → Failed
```

## 落点

### 1. 新文件 `STS2AIAgent/Agent/DualLaunchOutcome.cs`（纯 BCL，无 Godot / 无 MegaCrit 引用）

```csharp
internal enum DualLaunchOutcome { Idle, InProgress, Succeeded, Rejected, Failed, Canceled }

internal static class DualLaunchOutcomePolicy
{
    public static bool IsFailure(DualLaunchOutcome outcome);    // Rejected / Failed / Canceled
    public static bool IsInProgress(DualLaunchOutcome outcome); // InProgress
}
```

加入 `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` 的 `<Compile Include>`（紧跟同类纯策略文件）。

### 2. `STS2AIAgent/Agent/AgentRuntime.cs`

- 字段与属性：`private DualLaunchOutcome _dualLaunchOutcome = DualLaunchOutcome.Idle;` /
  `public DualLaunchOutcome DualLaunchOutcome => _dualLaunchOutcome;`
- `LaunchDualInstanceCoreAsync`（:713-765）**每个 return / catch 路径**都写入结果：
  - gate 抢不到 → `InProgress`（并发启动在途，既非成功也非失败）
  - 队伍消息 pending（:726-729）、`CoopLaunchPolicy` 拒绝（:731-737）→ `Rejected`
  - 协调器返回（:744）→ `Ok ? Succeeded : Failed`
  - 取消（:746-749）→ `Canceled`；其它异常（:750-753）→ `Failed`
- 写入位置与对应 `_dualStatus` 赋值同处，不允许只写文案不写结果。

### 3. `STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs`

- 新增 `public static async Task<(bool Ok, string Message)> HostLocalCoopResultAsync(CancellationToken)`，
  分支映射：非 MAIN_MENU `(false, msg)`；4 人大厅创建异常 `(false, msg)`；`!launch.Ok`
  `(false, launch.Message)`；成功 `(true, 成功文案)`；**取消仍然 rethrow**（不吞）。
- 现有 `HostLocalCoopAsync` 改为转调并返回 `Message`，签名与文案逐字不变。

### 4. `STS2AIAgent/Game/GameActionService.cs:5088-5130`

- 保留 `CoopLaunchPolicy.GetError` 前置校验（409 `invalid_action`）。
- 删除 :5103-5106 的子串判定，改为读 `AgentRuntime.Instance.DualLaunchOutcome`：

| 结果 | 响应 |
|---|---|
| `Succeeded` | 200，`status:"completed"`，`message = DualStatus` |
| `InProgress` | 200，`status:"pending"`，`stable:false`，`message = DualStatus` |
| `Rejected`/`Failed`/`Canceled` | 409 `invite_failed`，details 带 `action`/`screen`/`outcome` |
| `Idle`（理论上不可达） | 409 `invite_failed`（不谎报成功） |

错误码沿用既有 `invite_failed`，不新增错误码。

### 5. 测试 `STS2AIAgent.Tests/DualLaunchOutcomeTests.cs` + `TestRunner.cs` 注册

- 真值表：6 个枚举值逐个断言 `IsFailure`/`IsInProgress`（必须可否证：改一个分支即变红）。
- 语言无关性：一条源码契约断言（`AgentSourceFixture.Read("Game/GameActionService.cs")`）
  证明 `Contains("失败"` 已消失、且存在 `DualLaunchOutcome` 读取；用反射/枚举真值表证明判定不吃文案。
- 补一条"每个分支都写结果"的契约断言（`_dualLaunchOutcome =` 出现次数 ≥ 5，且 gate 分支内为 `InProgress`）。

## 兼容与回滚

- HTTP 面：成功路径响应体字段不变；失败路径由"假成功"变为 409 —— 正是本次要修的行为。
- 回滚：四个文件 + 一个新测试文件，独立提交，可整体 revert。
