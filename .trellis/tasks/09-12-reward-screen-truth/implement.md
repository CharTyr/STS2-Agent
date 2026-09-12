# 执行清单

1. 读 `GameStateService.cs:6800-6870`（`ResolveNonModalScreen` 全文）、
   `STS2AIAgent.Tests/UnlockScreenContractTests.cs`（既有的顺序契约范式）、
   `scripts/check_verification_gates.py` 的 `api-facts` 提取逻辑（`CODE_SCREEN_EARLY_RETURN`）。
2. 插入新早退分支（见 design）。
3. 新建 `RewardScreenContractTests.cs`，3 条断言，**不要**改 TestRunner.cs / csproj（主代理注册）。
4. 可否证自检：把新分支临时挪到通用网格分支**之后** → 你新增的第 1/2 条必须变红 → 还原。
5. 验证：
   ```powershell
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   python scripts/check_verification_gates.py
   ```
6. 不提交、不推送；报告改动文件、红/绿证据、以及"只能实机验证"的清单。
