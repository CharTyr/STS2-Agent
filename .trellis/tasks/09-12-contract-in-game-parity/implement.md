# 执行清单

1. 读：`skills/sts2-mcp-player/SKILL.md`（全文，重点 25 与 48-141）、
   `skills/sts2-mcp-player/references/screen-playbooks.md`（重点 85-100）、
   `STS2AIAgent/Agent/AgentTools.cs`、`STS2AIAgent/Agent/PlayPrompt.cs`、
   `STS2AIAgent.Tests/McpPlayerSkillTests.cs`、`README.md:175-190`、`README.zh-CN.md:175-190`、
   `mcp_server/src/sts2_mcp/server.py` 的 guided 工具注册处（确认 health_check 在 MCP 侧存在）。
2. 按 design 改 SKILL.md 措辞与 playbooks 标注；readme 两语对齐。
3. 写新测试类 `HealthCheckParityTests.cs`（或按 design 的白名单形态），**不要**改 TestRunner.cs / csproj。
4. 可否证自检：把措辞改回祈使句 → 新测试红 → 还原。
5. 验证：
   ```powershell
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   python scripts/check_verification_gates.py
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   ```
6. 不提交、不推送；报告改动文件、红/绿证据、以及"只能实机验证"的项（模型是否真的因此少走一轮）。
