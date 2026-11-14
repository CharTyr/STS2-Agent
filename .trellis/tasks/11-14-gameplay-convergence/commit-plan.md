# 已批准的本地提交清单

分支：`codex/gameplay-convergence`；基线：`94ef838`。工作开始时仓库干净，以下均为本轮改动；没有未识别的脏文件。只创建本地提交，不 push、合并或发布。

## 1. chore(agents): pin Luna research and Terra implementation models

- `.codex/config.toml`
- `.codex/agents/trellis-research.toml`
- `.codex/agents/trellis-implement.toml`
- `.codex/agents/trellis-check.toml`

## 2. fix(agent): converge play guidance and preserve unknown plan outcomes

- `CHANGELOG.md`
- `STS2AIAgent/Agent/AgentTools.cs`
- `STS2AIAgent/Agent/PlayPrompt.cs`
- `STS2AIAgent/Agent/PlaybookSections.cs`
- `STS2AIAgent/Server/NativeMcpServer.cs`
- `STS2AIAgent.Tests/HealthCheckParityTests.cs`
- `STS2AIAgent.Tests/LoopbackListenerTests.cs`
- `STS2AIAgent.Tests/McpPlayerSkillTests.cs`
- `STS2AIAgent.Tests/McpServiceTests.cs`
- `STS2AIAgent.Tests/PlaybookSectionsTests.cs`
- `STS2AIAgent.Tests/TestRunner.cs`
- `mcp_server/src/sts2_mcp/play_tools.py`
- `mcp_server/src/sts2_mcp/scene_guidance.py`
- `mcp_server/src/sts2_mcp/server.py`
- `mcp_server/tests/test_game_data_tools.py`
- `mcp_server/tests/test_knowledge.py`
- `mcp_server/tests/test_play_plans.py`
- `mcp_server/tests/test_scene_guidance_alignment.py`
- `mcp_server/README.md`
- `skills/sts2-mcp-player/SKILL.md`
- `skills/sts2-mcp-player/README.md`
- `skills/sts2-mcp-player/references/remote-connection.md`
- `skills/sts2-mcp-player/references/screen-playbooks.md`
- `skills/sts2-mcp-player/references/strategy.md`
- `docs/project-map.md`
- `.trellis/spec/mod/agent-and-ui.md`
- `.trellis/spec/mcp/contracts-and-tests.md`
- `.trellis/tasks/11-14-gameplay-convergence/prd.md`
- `.trellis/tasks/11-14-gameplay-convergence/design.md`
- `.trellis/tasks/11-14-gameplay-convergence/environment-fix.md`
- `.trellis/tasks/11-14-gameplay-convergence/implement.md`
- `.trellis/tasks/11-14-gameplay-convergence/implement.jsonl`
- `.trellis/tasks/11-14-gameplay-convergence/check.jsonl`
- `.trellis/tasks/11-14-gameplay-convergence/research/flow-audit.md`
- `.trellis/tasks/11-14-gameplay-convergence/review.md`
- `.trellis/tasks/11-14-gameplay-convergence/verification.md`
- `.trellis/tasks/11-14-gameplay-convergence/task.json`
- `.trellis/tasks/11-14-gameplay-convergence/commit-plan.md`

## 验证边界

计划测试 13/13、传输安全 25/25、Python 全套 461 项通过；C# 全套 1149 通过、0 失败、2 跳过，两项真实 HTTP 测试因原生 HttpListener 不可用而明确跳过。12 项静态闸门通过，最终相关文档、架构、打包链接和空白检查通过。详见 verification.md 和 environment-fix.md；没有真实游戏或模型对局验收。

提交后再按 Trellis 流程归档本任务并记录 journal；不归档另外两个仍在进行中的历史任务。
