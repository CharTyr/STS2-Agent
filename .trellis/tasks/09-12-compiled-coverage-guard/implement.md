# 执行清单

1. 读：`STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`、`STS2AIAgent.Tests/AgentSourceFixture.cs`、
   `STS2AIAgent.Tests/UnlockScreenContractTests.cs`（测试风格）、`.github/workflows/validate.yml`、
   `STS2AIAgent.Tests/TestRunner.cs` 末尾（注册形态）。
2. 按 design 改 csproj（加 Reference）、修 fixture（过滤 obj/bin）、新增 `SourceCoverageTests.cs`。
3. 注册：在 `TestRunner.cs` 的 `AllTests` 末尾追加你的 `yield return`
   （**其他 worker 也在并行追加同一位置**：patch 失败就重新读取再试，禁止整文件覆写或删除他人行）。
4. 可否证自检：
   - 在任一未编译文件（如 `Server/Router.cs`）末尾临时插入非法语法 → 语法测试必须红 → 还原；
   - 临时删掉 csproj 里某条 `<Compile Include>` → 覆盖面断言必须红 → 还原。
5. 验证：
   ```powershell
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   ```
6. 不提交、不推送；报告改动文件、红/绿证据与"仅在 CI 上才能确认"的清单。

本任务允许改 `STS2AIAgent.Tests.csproj` 与 `AgentSourceFixture.cs`（这是任务核心），但只改这两个
测试工程文件；不要动 `STS2AIAgent/` 下的生产代码。

