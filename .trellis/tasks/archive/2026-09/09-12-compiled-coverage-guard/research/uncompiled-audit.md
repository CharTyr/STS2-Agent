# 证据：未编译的 16 个源文件

## 覆盖面对比（git ls-files vs csproj Compile Include）

- tracked `STS2AIAgent/**/*.cs`：79 个（含 `ModEntry.cs`）；
- csproj `<Compile Include>`：49 条（含 3 条通配符），展开后 63 个；
- 差集 16 个，合计 18007 行：

```
  6850  STS2AIAgent/Game/GameStateService.cs
  5786  STS2AIAgent/Game/GameActionService.cs
  1482  STS2AIAgent/Ui/AgentOverlayHost.cs
  1108  STS2AIAgent/Agent/AgentRuntime.cs
   474  STS2AIAgent/Game/GameDataExportService.cs
   417  STS2AIAgent/Server/Router.cs
   366  STS2AIAgent/Server/GameEventService.cs
   317  STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs
   240  STS2AIAgent/Agent/GameBridge.cs
   227  STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs
   190  STS2AIAgent/Server/HttpServer.cs
   160  STS2AIAgent/Ui/UiFactory.cs
   145  STS2AIAgent/Game/GameThread.cs
   125  STS2AIAgent/Localization/LocSource.cs
    73  STS2AIAgent/ModEntry.cs
    47  STS2AIAgent/Vision/ScreenshotService.cs
```

## 根因

以上文件 `using` Godot / `MegaCrit.Sts2.*`（游戏 DLL），离线编译器拿不到引用，所以从来没进过
测试工程的编译集。既有的源码契约测试（`AgentSourceFixture.Read`）只能断言"文件里的文本形状"，
断言不了"文件本身是否是可解析的 C#"。

## 已有部分缓解（不代表覆盖）

部分测试用字符串断言覆盖了未编译文件的**局部**：
`TimelineIndexContractTests`（GameActionService.ResolveTimelineSlot）、
`UnlockScreenContractTests` / `RewardFlowContractTests`（GameStateService 的分支）、
`McpPlayerSkillTests`（SKILL.md 与内嵌契约一致）——但都依赖文件"恰好在作者写断言的那一处没错"，
文件其余 99% 的语法错误无人拦。

## spike 记录（本任务开工前由主代理执行）

1. csproj 加 Roslyn Reference（上文 design 的片段）；
2. `_RoslynSpike.cs`：`[ModuleInitializer]` 调 `CSharpSyntaxTree.ParseText("class A { void B() { } }")`
   并断言 0 error；
3. `dotnet run --project STS2AIAgent.Tests -c Release` → 输出 `ROSLYN_SPIKE_OK`，335 个测试全 PASS；
4. 两处改动已回滚（`git checkout -- STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` + 删除 spike 文件）。

## 待确认（只能 CI 侧确认）

上面只在本地 Windows + SDK 9.0.311 验证；CI 的 `windows-latest` + `dotnet-version: 9.0.x`
装配同一形态的 SDK（`Roslyn/bincore` 随 SDK 分发），但本仓库本轮没有 push，CI 未运行。

