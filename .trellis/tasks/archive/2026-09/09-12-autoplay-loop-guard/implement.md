# 执行清单

1. 读 `Agent/AutoPlayRecovery.cs`（全文，短）、`Agent/AgentLoop.cs:600-680`、
   `Agent/AgentRuntime.cs` 里 `AccountTurn`/`ApplyPlayResult`/`ObservePlayCompletionAsync` 段、
   `Agent/SessionBudgetGuard.cs`、`Agent/StopKindPolicy.cs`，以及 `AgentTurnResult` 的定义处。
2. 先写 `NoProgressPolicy.cs` + `NoProgressPolicyTests.cs`（行为测试）。
3. 再改 `AgentTurnResult` + `AgentLoop` 的 pending 返回路径，然后改 `AutoPlayRecovery` 状态机。
4. 写 `AutoPlayRecoveryTests.cs`（若已有同名测试类，追加方法而不是新建重复类）。
5. 验证：
   ```powershell
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   python scripts/check_verification_gates.py
   ```
6. 自检可否证：把阈值判断去掉 → 空转测试变红；把未确认分支算作失败 → 误停测试变红；还原。
7. 不提交、不推送；报告改动文件、状态机分支表、每条验收证据，并明确标注
   "真实游戏里是否真的会出现长结算导致 pending"属实机项。
