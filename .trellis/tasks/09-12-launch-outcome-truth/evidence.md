# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent/Agent/DualLaunchOutcome.cs`（新） | 枚举 `Idle/InProgress/Succeeded/Rejected/Failed/Canceled` + 纯策略 `IsFailure`/`IsInProgress`；无 Godot / MegaCrit 引用 |
| `STS2AIAgent/Agent/AgentRuntime.cs` | `_dualLaunchOutcome` + `DualLaunchOutcome` 属性；`LaunchDualInstanceCoreAsync` 每个 return/catch 分支显式赋值（gate 抢占失败 → `InProgress`，不再沿用上次结果） |
| `STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs` | 新增 `HostLocalCoopResultAsync` 返回 `(bool Ok, string Message)`；原 `HostLocalCoopAsync` 转调，签名与文案逐字不变 |
| `STS2AIAgent/Game/GameActionService.cs` | `ExecuteInviteAiTeammateAsync` 删除中文子串判定：`Succeeded→completed`、`InProgress→pending`、其余（含 `Idle`）→ 409 `invite_failed`，details 带 `outcome` |
| `STS2AIAgent.Tests/DualLaunchOutcomeTests.cs`（新）+ csproj include + TestRunner 注册 | 6 条测试 |

## 验收

| 标准 | 证据 |
|---|---|
| 枚举/纯策略可离线编译并入库编译清单 | `DualLaunchOutcome.OfflineCompilable` PASS（断言无 `using Godot`/`using MegaCrit`）；csproj 已加 `<Compile Include>` |
| 每个分支都写结果 | `DualLaunchOutcome.EveryBranchRecords` PASS：`_dualLaunchOutcome =` 在方法体内出现 6 次，gate 分支为 `InProgress` |
| 不再有对 `DualStatus` 的子串匹配 | `DualLaunchOutcome.LanguageIndependent` PASS；`rg 'Contains\("(失败|请先|找不到)"'` 全仓 0 命中 |
| 行为测试（真值表 / 语言无关 / 取消异常归类） | `TruthTable`（6 值逐断言）、`OnlySuccessCompletes`、`LanguageIndependent`、`EveryBranchRecords` 全 PASS |
| 协调器结构化且原签名文案不变 | `DualLaunchOutcome.CoordinatorContract` PASS；文案字符串 diff 逐字未改 |
| 可否证 | 把 `IsFailure` 改成包含 `Succeeded` → 2 条测试变红（`exit=1`），还原后 311 PASS |

## 门禁（2026-09-12 全量复跑）

`dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` → **PASS=311 FAIL=0**
`dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental` → 0 警告 0 错误
`python scripts/check_verification_gates.py` → 通过（api-doc 55 动作）
`python scripts/check_release_package.py --source-root .` → 通过
`powershell -File scripts/preflight-release.ps1` → `Static preflight complete.`
`uv run --locked python -m unittest discover -s tests`（mcp_server）→ 159 tests OK

## 只能实机验证

- 端到端 HTTP 语义（真实双开成功 = `completed`；英文客户端下失败必须 = 409 `invite_failed`；并发邀请 = `pending`）。
- 待实机覆盖三条路径：成功 / 英文客户端失败 / 重复邀请。

## 备注

`HostLocalCoopAsync` 现在在生产代码中已无调用点，作为兼容入口保留（由 `CoordinatorContract` 测试锁定其转调关系）。
