# Design

GameThread owns native access; GameStateService and GameActionService own projections/mutations. AgentLoop uses IGameBridge; AgentRuntime owns settings and session. Native/Python MCP remain independent. Scripts own artifacts.

Rank by budget enforcement, false stops, latency, action truthfulness, build completeness. Sequential children; main coordinates and integrates, subagents implement/test. Preserve public contracts.
