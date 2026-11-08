# Implementation Plan: MCP Payload Slimming & Session Static Deduplication (#233)

1. **C# Native MCP (`STS2AIAgent/`)**
   - Create `STS2AIAgent/Server/McpStateSlimmer.cs` with stateless compact slimming (`SlimCompactState`, reusing `PromptStateDiet.Apply` with `DietRules.All & ~DietRules.DropCardValueNote` while preserving `compact_agent_view`) and thread-safe bounded `McpSessionDedup`.
   - Update `STS2AIAgent/Agent/AgentTools.cs` (`get_game_state` `full_state` property) and wire `McpSessionDedup` into `NativeMcpServer.cs`, `NativeMcpServer.Tools.cs` (`get_game_state`, `wait_until_actionable`, `act`, `decide`), and `NativeMcpServer.TurnPlan.cs` (strip `state` from `plan.steps[]`, slim/dedup `final_state`).
2. **Python MCP (`mcp_server/src/sts2_mcp/`)**
   - Create `mcp_server/src/sts2_mcp/state_diet.py` mirroring `slim_compact_state` and `SessionStateDedup`.
   - Apply `slim_compact_state` in `action_results.project_agent_state` and `decision._snapshot_state`; strip intermediate `state` from `plan["steps"]` in `play_tools.py` when `not raw_state`; wire `SessionStateDedup` and `get_game_state(full_state: bool = False)` in `server.py`.
3. **Tests & Documentation**
   - Add C# tests in `STS2AIAgent.Tests/McpServiceTests.cs` and Python tests in `mcp_server/tests/test_mcp_state_diet.py`.
   - Update `docs/project-map.md`, `.trellis/spec/mcp/contracts-and-tests.md`, `skills/sts2-mcp-player/SKILL.md` (outside `SHARED PLAY CONTRACT` markers), and `CHANGELOG.md`.
   - Run all offline verification suites before committing and opening PR targeting `dev`.
