# 给 16 个未编译的 mod 源文件补上 CI 安全网

## 背景

CI（`.github/workflows/validate.yml`）只跑 `STS2AIAgent.Tests`，而该测试工程只
`<Compile Include>` 了 79 个 tracked `.cs` 里的 63 个（含 `Config/*`、`Llm/*`、
`Loc.Strings.*` 通配符）。**16 个文件、18007 行在 CI 里完全不可见**——连语法错误都不会被任何
自动化检查拦住：

| 文件 | 行数 |
| --- | --- |
| Game/GameStateService.cs | 6850 |
| Game/GameActionService.cs | 5786 |
| Ui/AgentOverlayHost.cs | 1482 |
| Agent/AgentRuntime.cs | 1108 |
| Game/GameDataExportService.cs | 474 |
| Server/Router.cs | 417 |
| Server/GameEventService.cs | 366 |
| Multiplayer/LocalDualInstanceLauncher.cs | 317 |
| Agent/GameBridge.cs | 240 |
| Multiplayer/DualInstanceCoordinator.cs | 227 |
| Server/HttpServer.cs | 190 |
| Ui/UiFactory.cs | 160 |
| Game/GameThread.cs | 145 |
| Localization/LocSource.cs | 125 |
| ModEntry.cs | 73 |
| Vision/ScreenshotService.cs | 47 |

根因：它们依赖游戏 DLL（Godot/sts2），无法离线编译。但**语法层**检查零依赖可做：.NET SDK 自带
Roslyn（`$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll`），
本任务开工前已 spike 证实该引用可用（`ROSLYN_SPIKE_OK`，335 个既有测试照常通过，spike 已回滚）。

## 目标

CI 对每个 tracked `.cs` 至少做一次语法解析；新文件不能同时绕开"编译"与"解析"两条路。

## 验收标准

- [x] `STS2AIAgent.Tests.csproj` 引用 SDK 自带 Roslyn（零 NuGet），干净机器 `dotnet run` 可用。
- [x] 新增语法测试：对 `AgentSourceFixture.SourceFiles()` 的每个文件
      `CSharpSyntaxTree.ParseText(...)` 断言 0 error；可否证（给任一未编译文件注入语法错误 → 红 → 还原）。
- [x] `AgentSourceFixture.SourceFiles()` 过滤 `obj/` 与 `bin/`（否则本地会多出生成文件）。
- [x] 覆盖面断言：csproj 未编译的文件集合 == 显式白名单（上表 16 个）；新增未注册文件时测试红。
- [x] 全量 C# 测试 / gates / preflight 绿。

## 范围外

- 不把 16 个文件真正编译进测试工程（需要游戏 DLL）。
- 不做跨文件语义/类型检查（单文件 Roslyn 解析不解析引用）。

