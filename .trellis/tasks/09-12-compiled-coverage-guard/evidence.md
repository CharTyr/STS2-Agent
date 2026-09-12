# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` | 新增 ItemGroup：`Microsoft.CodeAnalysis` / `Microsoft.CodeAnalysis.CSharp` 两条 `<Reference>`，HintPath 指向 `$(MSBuildSDKsPath)/../Roslyn/bincore/*.dll`（**零 NuGet**；diff `12 0`） |
| `STS2AIAgent.Tests/AgentSourceFixture.cs` | `SourceFiles()` 过滤 `obj/`、`bin/`（新增 `IsBuildOutputPath`，按相对 mod 根的路径段判断） |
| `STS2AIAgent.Tests/SourceCoverageTests.cs` | 新增。`EveryModSourceParsesWithoutSyntaxErrors`（每个源文件 `CSharpSyntaxTree.ParseText`，收集 error diagnostics 并断言为空，失败消息带 `file:line`）+ `UncompiledSourcesMatchTheDeclaredWhitelist`（csproj 编译集展开通配符后与 tracked 源求差集，须等于 16 条显式白名单；双向断言 unexpected 与 missing） |
| `STS2AIAgent.Tests/TestRunner.cs` | 追加 2 行 `SourceCoverage.*` 注册 |

生产代码零残留改动（可否证期间临时改过 `ScreenshotService.cs`，已按原始 SHA256 还原）。

## 验收

| 标准 | 证据 |
|---|---|
| Roslyn 引用可用、零 NuGet | `$(MSBuildSDKsPath)/../Roslyn/bincore` 实存 `Microsoft.CodeAnalysis.dll`、`Microsoft.CodeAnalysis.CSharp.dll`；未新增包或 lock 变更；构建 0 warning。开工前主代理已 spike（`ROSLYN_SPIKE_OK` + 335 测试照常通过，改动回滚） |
| 语法测试可否证 | 在 `STS2AIAgent/Vision/ScreenshotService.cs` 末尾注入非法语法 → `FAIL SourceCoverage.ModSourcesParse ... ScreenshotService.cs:59 CS1026: 应输入 )`，exit 1；还原（SHA256 校验回原始字节）后 PASS |
| `obj/`/`bin/` 过滤 | fixture diff 见上；本机含 `obj/bin` 时测试仍绿 |
| 覆盖面断言可否证 | ① 从白名单移除 `ScreenshotService.cs` → `Unexpected uncompiled source(s)` 红；② 往白名单加 `STS2AIAgent/DoesNotExist.cs` → `no longer uncompiled tracked sources` 红；均还原后绿 |
| 全量门禁 | C# **347 PASS / 0 FAIL**（`SourceCoverage.ModSourcesParse`、`SourceCoverage.UncompiledWhitelist` 在内）；6 道 gate 全绿；preflight exit 0 |

**关于 implement.md 里"临时删掉 csproj 某条 `<Compile Include>`"的替代**：该路线不可用（已实测）——
删掉任一条后 `dotnet run` 直接 exit 1 且**没有任何测试输出**（编译先失败：每条显式 include 都被 ≥2 处测试引用）。
改用直接撼动白名单数据（上面 ①②），命中与"新增未注册文件"完全相同的两个分支。

## 设计边界（必须知悉）

- 覆盖面断言按 design **只比对 tracked 源**（`git ls-files -z -- STS2AIAgent`），取不到 git 时回退为文件系统扫描集。
  这条限制在起作用：本机存在 gitignored 的 `STS2AIAgent/_scratch/Class1.cs`（80 个磁盘 `.cs` vs 79 tracked），
  过滤后仍绿，否则本地会误红。
- 直接后果：**未 `git add` 的新文件不会立刻让覆盖面断言变红**（CI/PR checkout 里它是 tracked → 会红）。
  语法测试走文件系统扫描，未跟踪文件也照样被解析。这是 design 明示的取舍。
- 语法测试是**单文件解析**，不解析引用/类型；跨文件的语义错误仍要靠编译（需游戏 DLL）。

## 只能 CI 验证

1. `windows-latest` + `setup-dotnet@v4 9.0.x` 上 `$(MSBuildSDKsPath)/../Roslyn/bincore/*.dll` 的可解析性（本地只证到 SDK 9.0.311）。
2. CI 容器内 `git ls-files` 可用性（`actions/checkout@v4` 留 `.git`，预期可用；不可用时走回退分支，干净 checkout 下等价）。
3. 本轮无 push，GitHub Actions 未实际触发。

