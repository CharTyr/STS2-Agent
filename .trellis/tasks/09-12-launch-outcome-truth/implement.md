# 执行清单

1. 读 `STS2AIAgent/Agent/AgentRuntime.cs:40-60 / :220-230 / :713-765`、
   `STS2AIAgent/Multiplayer/DualInstanceCoordinator.cs`（全文，短）、
   `STS2AIAgent/Multiplayer/LocalDualInstanceLauncher.cs:1-60 / :300-330`、
   `STS2AIAgent/Game/GameActionService.cs:5085-5130`。
2. 新增 `Agent/DualLaunchOutcome.cs`（枚举 + 纯策略），加进测试工程编译清单。
3. 改 `DualInstanceCoordinator`：先加结构化方法，原方法转调（文案不变）。
4. 改 `AgentRuntime`：加字段/属性，逐分支赋值。
5. 改 `GameActionService.ExecuteInviteAiTeammateAsync`：删子串判定，改读枚举。
6. 写 `DualLaunchOutcomeTests.cs`，在 `TestRunner.cs` 的 `AllTests` 注册。
7. 验证：
   ```powershell
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   python scripts/check_verification_gates.py
   ```
8. 不提交、不推送；把改动文件清单与测试输出交给主代理。
