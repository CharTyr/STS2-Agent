# 证据：invite_ai_teammate 的成败判定

## 现状

- `GameActionService.cs:5102-5106`：`message.Contains("失败")||("请先")||("找不到")`。
- `AgentRuntime.LaunchDualInstanceAsync(AgentSettings, CancellationToken)`（:519-522）返回非泛型
  `Task`，**无结果值**；全仓仅 3 处命中（定义 + 两个调用点）。
- `_dualStatus` 赋值点（`AgentRuntime.cs`）：:722 进行中、:728 拒绝、:735 拒绝、
  :742 进行中、:744 `await HostLocalCoopAsync`（成功/失败混在同一字符串里）、:748 取消、:752 异常。
- `:715-718` gate 抢占失败 → 静默 return，**不更新 `_dualStatus`**，调用方读到旧文案。

## 被判成"成功"的实际失败文案（`LocalDualInstanceLauncher.cs`）

| 行 | 文案 | 现有子串能否命中 |
|---|---|---|
| :155 | 正在邀请 AI 队友，请等待连接结果。 | 否（"请等待"≠"请先"） |
| :166 | AI 队友窗口已经在运行。… | 否 |
| :187 | 找不到游戏可执行文件。 | 是 |
| :212 | 无法计算队友启动参数：{0} | 否 |
| :241 | 无法配置队友设置文件路径：{0} | 否 |
| :289 | 启动第二实例失败。Steam 可能阻止了双开：{0} | 是 |
| :306 | AI 队友进程已退出（退出码 {0}）。 | 否 |
| :307 | AI 队友进程仍在运行（PID {0}），但未能确认连接。 | 否 |

英文客户端下 `Loc.T` 输出英文 ⇒ 三关键词全部不命中 ⇒ **一切失败都报成功**。

## 协调器返回面（`DualInstanceCoordinator.cs:14-45`）

- `:16-19` 非 MAIN_MENU → 失败文案；`:29-33` 大厅创建异常 → 失败文案；
  `:25-28` 取消 → **rethrow**；`:35-39` `!launch.Ok` → `launch.Message`；
  `:41` 成功 → 成功文案（`LocalDualInstanceLauncher.cs:317`）。
- 已存在结构化类型 `DualLaunchResult { bool Ok; string Message; int CompanionPort; int? CompanionPid; }`
  （`LocalDualInstanceLauncher.cs:11-20`），但 :38 把它拍平成字符串。

## 现有测试覆盖

- `LaunchDualInstance` 在 `STS2AIAgent.Tests` 内 0 命中；`DualStatus` 仅作 fixture 文本
  （`PlayerExperienceTests.cs:111/306`、`RuntimeExperienceRegressionTests.cs:34`）。
- `ExecuteInviteAiTeammateAsync` 的成败判定**无任何测试**。

## 消费点（本次不得改动其行为）

`AgentOverlayHost.cs:1265`；`Router.cs:299`（/health 的 `dual_status`）；
`PlayerFacingSession.cs:19/96/106/260`；`DiagnosticExport.cs:19/92`；
`AgentRuntime.cs:1076/1110`；`docs/api.md:134/156`。
