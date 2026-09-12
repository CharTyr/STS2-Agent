# 设计

## 机制（已 spike 验证，非推测）

csproj 加：

```xml
<ItemGroup>
  <Reference Include="Microsoft.CodeAnalysis">
    <HintPath>$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.dll</HintPath>
  </Reference>
  <Reference Include="Microsoft.CodeAnalysis.CSharp">
    <HintPath>$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll</HintPath>
  </Reference>
</ItemGroup>
```

spike 证据：临时加上述引用 + 一个 `[ModuleInitializer]` 调 `CSharpSyntaxTree.ParseText`，
`dotnet run` 输出 `ROSLYN_SPIKE_OK` 且 335 个测试照常通过；改动已回滚。
零 NuGet：DLL 由 SDK 自带，CI 的 `setup-dotnet@v4` 装的就是同一个 SDK。

## 测试形状（`SourceCoverageTests.cs`）

1. `EveryModSourceParsesWithoutSyntaxErrors`：遍历 `AgentSourceFixture.SourceFiles()`，
   `CSharpSyntaxTree.ParseText(File.ReadAllText(path))`，收集 error diagnostics 并断言为空
   （失败消息带首个 `file:line`）。
2. `UncompiledSourcesMatchTheDeclaredWhitelist`：解析
   `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` 的 `<Compile Include>`（展开通配符），
   与 `SourceFiles()` 求差集，断言等于显式白名单（16 个相对路径，归一化分隔符后比较）。
   新增未编译文件会让该断言红——作者必须显式决定"注册进编译"或"确认由语法测试覆盖"。

## 陷阱

- `AgentSourceFixture.SourceFiles()` 当前不过滤 `obj`/`bin`：本地会多出生成文件
  （`.NETCoreApp,Version=v9.0.AssemblyAttributes.cs`、`*.AssemblyInfo.cs` 等），CI 上干净但本地会红。
  先修 fixture；白名单比对只针对 tracked 源文件。
- 路径归一化：csproj 用 `\` 且以 `..\` 开头，展开后的相对路径要统一成 `/` 再比较。
- 既有源码契约测试（`TimelineIndexContractTests` 等）读文件的方式不要改。
- 新增测试要在 `TestRunner.cs` 注册。

