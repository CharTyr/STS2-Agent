# 执行清单

1. 读 `docs/api.md:1003-1061`（契约块）、`mcp_server/tests/test_native_tool_alignment.py:240-300 / :380-436`、
   `STS2AIAgent/Game/GameStateService.cs:1688-1726`、`STS2AIAgent/Game/GameActionService.cs:2064-2120`。
2. 改 `docs/api.md` 那一行（保持动作名 + 破折号 + 描述的形态，参数与取值用反引号包裹）。
3. 删豁免条目与其注释；把注释改写成"两侧现已一致，不再需要豁免"的说明。
4. 删 `GetDeckSelectionOptions` 的通用兜底分支（只删该分支，保留其前三段与末尾的
   `return Array.Empty<NCardHolder>();`）。
5. 在 `STS2AIAgent.Tests/` 补源码契约断言（新建测试类 `DeckSelectionAvailabilityTests`，两个静态方法；
   **不要自己改 TestRunner.cs 或 csproj**，由主代理注册）：
   - `AvailabilityMatchesTheExecutableSet`：断言 `GetDeckSelectionOptions` 方法体内**不出现**
     `currentScreen is Node rootNode`，且仍包含三段判据的识别符号
     （`NCardGridSelectionScreen` / `NChooseACardSelectionScreen` / `TryGetCombatHandSelection`）。
   - `ExecutorKeepsItsGuard`：断言 `ExecuteSelectDeckCardAsync` 方法体仍包含那三类判据与 409 分支
     （防止有人只删守卫不删兜底，导致又一次假成功）。
   写完后自检可否证（临时把兜底加回去 → 变红 → 还原）。
6. 验证：
   ```powershell
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   python scripts/check_verification_gates.py
   ```
7. 不提交、不推送；报告改动文件、每条验收的证据、以及"只能实机验证"的项。
