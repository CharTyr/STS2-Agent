# 执行清单

1. 读这些（不要只读摘要）：
   - `STS2AIAgent/Game/GameStateService.cs`：`ResolveNonModalScreen`（约 6815-6845）、
     `CanCloseCardsView`（942-946）、`CanCloseMainMenuSubmenu`（1433-1447）、
     `GetDeckSelectionOptions` 的注释（1725-1731）、`GetCardsViewBackButton`（2119-2133）、
     `GetMainMenuSubmenuStack`（6563-6577）；
   - `STS2AIAgent/Game/GameActionService.cs`：`ExecuteCloseMainMenuSubmenuAsync`（600-660）、
     `ExecuteCloseCardsViewAsync`（2221-2265）、`WaitForCardsViewCloseAsync`（2305-2325）、
     `IsCardsViewClosed`（2325-2334）；
   - `STS2AIAgent.Tests/RewardScreenContractTests.cs`（早退契约的既有范式）、
     `STS2AIAgent.Tests/DeckSelectionAvailabilityTests.cs`（"可用即可执行"范式）、
     `.trellis/tasks/09-12-card-viewer-screens/research/viewer-screens.md`；
   - `docs/api.md:80-100` 与 `:1645-1660`；`skills/sts2-mcp-player/SKILL.md:99-120`。
2. 按 design 改 C#（两处早退、栈查找上移、看牌屏集合收敛）。
3. 更新 `docs/api.md` 与 `SKILL.md`。
4. 新建 `STS2AIAgent.Tests/CardViewerScreenContractTests.cs`（3 组断言 + 既有回归），
   在 `TestRunner.cs` 的 `AllTests` **末尾追加**注册行。
5. 可否证自检（每条都要"改坏 → 红 → 还原 → 绿"）：
   - 把两个早退分支挪到 E4 之后 → 顺序断言红；
   - 把 `IsClosableCardViewer` 改回只认 `NCardsViewScreen` → 同源断言红；
   - 把 `GetSubmenuStack` 改回 `is NMainMenuSubmenuStack` → 栈断言红。
6. 验证：
   ```powershell
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   python scripts/check_verification_gates.py
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   ```
7. 不提交、不推送；报告改动文件、红/绿证据、以及"只能实机验证"的清单。

