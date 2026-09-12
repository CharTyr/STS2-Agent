# 执行清单

1. 读 `STS2AIAgent/Server/ApiException.cs`、`Server/JsonHelper.cs`、
   `Server/HttpServer.cs:30-60 / :160-221`、`STS2AIAgent.Tests/AgentSourceFixture.cs`。
2. 加两个 `<Compile Include>`，写两个测试文件，在 `TestRunner.cs` 的 `AllTests` 注册。
3. 写 `HttpServerPortPolicyTests.cs`（源码契约）。
4. `docs/api.md` 补屏幕名（先核实缺失）。
5. 验证：
   ```powershell
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   python scripts/check_verification_gates.py
   python scripts/check_release_package.py --source-root .
   powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
   ```
6. 反证自检：临时破坏被测行为，确认新测试变红，然后还原。
7. 不提交、不推送；报告新增/修改文件与测试计数。
