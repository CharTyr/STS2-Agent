# 执行清单

1. 读：`STS2AIAgent/Game/GameStateService.cs` 的 390-400、895-960、1091-1095、1500-1570、
   4430-4445、4930-4950；`STS2AIAgent/Game/FtueModalPolicy.cs`；
   `STS2AIAgent.Tests/FtueModalPolicyTests.cs`、`DeckSelectionAvailabilityTests.cs`、
   `RewardFlowContractTests.cs`。
2. 按 design 改（① ② 必须同源；③ 直接改；④ 按节点能力加过滤；⑤ 给结论）。
3. 撰写 `SurfacedActionParityTests.cs`，注册到 `TestRunner.cs` 的 `AllTests` 末尾
   （**其他 worker 也在并行追加**：patch 失败就重新读取重试，禁止整文件覆写或删除他人行）。
4. 可否证自检：逐条把改动还原 → 对应断言必须红 → 恢复。
5. 验证：
   ```powershell
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   python scripts/check_verification_gates.py
   ```
6. 不提交、不推送；报告改动文件、红/绿证据与"只能实机验证"的清单。

**并行编辑边界**：`09-12-reward-screen-truth` 同时改 `ResolveNonModalScreen`（6802-6866）。
不要动那段；改动只落在你自己列出的区域。

