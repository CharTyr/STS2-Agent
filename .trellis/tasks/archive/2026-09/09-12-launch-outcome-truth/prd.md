# 让 invite_ai_teammate 报出真实的启动结果

## 背景

`POST /action {"action":"invite_ai_teammate"}` 用**中文子串**判断邀请成败
（`STS2AIAgent/Game/GameActionService.cs:5102-5106`）：

```csharp
var message = AgentRuntime.Instance.DualStatus ?? string.Empty;
var failed = message.Contains("失败") || message.Contains("请先") || message.Contains("找不到");
```

`DualStatus` 是 `Loc.T(...)` 产出的展示文案，英文客户端下全部变英文 ⇒ 判定恒为 false。
即便中文客户端，`LocalDualInstanceLauncher` 的多数失败文案（"无法计算队友启动参数"、
"AI 队友进程已退出"、"AI 队友进程仍在运行…但未能确认连接"、"正在邀请 AI 队友，请等待连接结果"）
也不含这三个关键词。结果是**失败被当成成功**返回 `status:"completed"`，调用方（游戏内 Agent 循环、
MCP 客户端）据此认为队友已就位。

另一条静默路径：`AgentRuntime.cs:715-718` 的 `_dualLaunchGate.WaitAsync(0)` 抢占失败会直接 return，
既不更新 `_dualStatus` 也不报告——调用方读到的是**上一次**的旧文案，进而按旧文案判定。

## 目标

1. `AgentRuntime` 暴露结构化的启动结果，调用方不再解析展示文案。
2. `invite_ai_teammate` 只在确实启动成功时返回 `completed`；被拒绝/失败/取消返回 409
   `invite_failed`；仍在进行中返回 `status:"pending"`（`stable:false`）。
3. 展示文案 `DualStatus` 与其全部消费点（UI、`/health.dual_status`、`PlayerFacingSession`、
   `DiagnosticExport`）行为不变。

## 验收标准

- [x] 结果枚举与纯策略位于**可离线编译**的文件中，并加入 `STS2AIAgent.Tests` 编译清单。
- [x] `LaunchDualInstanceCoreAsync` 的每个分支都显式写入结果；gate 抢占失败不再沿用旧结果。
- [x] `GameActionService.cs` 中**不再存在**对 `DualStatus` 的子串匹配（有可否证的源码契约断言）。
- [x] 新增行为测试覆盖：结果分类真值表（每个枚举值都要断言）、文案语言无关性、取消与异常路径归类。
- [x] `DualInstanceCoordinator` 暴露结构化结果；原 `HostLocalCoopAsync` 的签名与返回文案逐字不变。
- [x] 全量门禁通过：C# 单测、MCP 单测、`check_verification_gates.py`、
      `check_release_package.py`、`preflight-release.ps1`。

## 范围外

- 不改任何 `Loc.T` 文案，不改 `/health` 字段名与 UI 呈现。
- 不改 MCP 侧工具名/参数（409 语义已足够表达失败）。
- 不动 `CoopLaunchPolicy.GetError` 的前置校验逻辑（它已是结构化的）。
