# 执行清单

1. 逐行读 `STS2AIAgent/Game/GameDataExportService.cs` 的 7 个 `ExportXxx()`（:35-180），
   把字段名抄成清单；不要照抄 `GameDataFilter` 现有表。
2. 新增 `Agent/GameDataExportSchema.cs`，加进测试工程编译清单。
3. 先写三条测试（此时应因 `hp`/`damage`/`block`/`target` 变红），记录红色输出。
4. 改 `GameDataFilter.cs` 的 `SceneFieldSets`，让测试转绿。
5. 验证：
   ```powershell
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   python scripts/check_verification_gates.py
   ```
6. 反证自检：临时把某个字段名改坏，确认测试变红，再改回。
7. 不提交、不推送；报告"修复前红 → 修复后绿"的实际输出片段。
