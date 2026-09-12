# 执行清单

1. 读 `GameBridge.cs:60-100`、`AgentLoop.cs:600-680`、`Server/ApiException.cs`、
   `Server/Router.cs` 的 `WriteErrorAsync` 段（约 :310-340）、
   `skills/sts2-mcp-player/SKILL.md` 的共享契约块（第 48-131 行，含 error 相关措辞）。
2. 先查清 `error` 字段的全部消费点：`rg -n '"error"|error\.' STS2AIAgent/Agent skills/ docs/api.md`，
   列出会因"字符串变对象"而受影响的每一处（这是本次唯一的破坏性风险）。
3. 新建 `Agent/AgentErrorEnvelope.cs`（纯 BCL），加进测试工程编译清单**由主代理做**；
   你只写文件与测试类（`STS2AIAgent.Tests/AgentErrorEnvelopeTests.cs`）。
4. 改 `AgentLoop` 的 catch；如需要也改 `GameBridge`，保持字段名单一来源。
5. 更新 SKILL.md 的共享契约块措辞（不动 marker）。
6. 验证：
   ```powershell
   dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental
   dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   python scripts/check_verification_gates.py
   ```
7. 自检可否证：把 `CodeFor` 改成恒返回 `internal_error` → 真值表变红 → 还原。
8. 不提交、不推送；报告改动文件、"error 字段消费点"清单与兼容结论、每条验收证据、实机项。
