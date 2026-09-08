# Source Evidence

Reviewed on 2026-09-08. Discovery used fast_context_search, followed by targeted source reads.

- STS2AIAgent/Game/GameThread.cs: InvokeAsync owns game-thread dispatch; frame waits are bounded even when the window is occluded.
- STS2AIAgent/Agent/GameBridge.cs: state reads and action execution enter GameThread; compact responses retain status/stable.
- STS2AIAgent/Game/GameStateService.cs: BuildStatePayload builds raw state and agent_view together.
- STS2AIAgent/Game/GameActionService.cs: ExecutePlayCardAsync validates availability and indexes before manual play, then waits for settlement.
- STS2AIAgent.Tests/STS2AIAgent.Tests.csproj: executable net9.0 harness links pure production source files.
- mcp_server/src/sts2_mcp/client.py: synchronous urllib transport; action requests must not be automatically replayed after ambiguous outcomes.
- mcp_server/src/sts2_mcp/server.py: guided/layered/full profiles and separately gated debug tools.
- mcp_server/tests/test_action_replay_safety.py: mocked transport verifies one action POST and state reconciliation.
- mcp_server/tests/test_native_tool_alignment.py: verifies Python/native tool alignment.
- scripts/preflight-release.ps1: canonical Python tests use unittest discovery; build and offline checks do not prove live gameplay.
- scripts/build-mod.ps1: default installation has filesystem side effects; SkipInstall retains local build/staging only.
- scripts/package-release.ps1: packaging builds with SkipInstall and checks the release artifact.

The original frontend scaffold and shared guides described web frontend and upstream Trellis maintenance. The replacement preserves shared checklists while grounding them in this repository. No product behavior or tool configuration change is needed.
