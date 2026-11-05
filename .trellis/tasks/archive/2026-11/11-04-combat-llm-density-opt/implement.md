# Implementation Plan: Combat Prefix Cache Split, Boss Turn-Plan Divergence & Decision Pacing (#234)

1. **Prefix-Cache State Split (`STS2AIAgent/Agent/PlayStateSplit.cs` + `AgentLoop.Decision.cs` + `AgentLoop.cs`)**
   - Implement `PlayStateSplit` & `PlayStateSplitCache` in `STS2AIAgent/Agent/PlayStateSplit.cs`.
   - Wire into `AgentLoop` (`_stateSplitCache`) and `AgentLoop.Decision.cs` (`PlayWithModelAsync`).
2. **Boss Turn-Plan Divergence (`TurnPlanPolicy.cs` + `turn_plan.py` + `turn_plan_vectors.json`)**
   - Add `EnemiesChanged` / `IntentChanged` stop reasons and checks in C# `TurnPlanPolicy.Diverged` and Python `turn_plan.diverged`.
   - Add shared Boss summon, revive, intent-shift, damage-shift, and killed-enemy non-regression vectors to `mcp_server/tests/fixtures/turn_plan_vectors.json`.
3. **Minimum Decision Interval (`DecisionPacingGate.cs` + Config + UI)**
   - Add `MinDecisionIntervalSeconds` to `AgentSettings.Experience.cs`, `SettingsClone.cs`, `CompanionPlaySettings.cs`, `Loc.Strings.Overhaul.cs`, and `AgentOverlayHost.Settings.cs`.
   - Implement `DecisionPacingGate.cs` and wire into `AgentLoop` before primary LLM request dispatch.
4. **Tests & Documentation**
   - Add `PlayStateSplitTests.cs`, `DecisionPacingTests.cs`, plus updates to `ExperienceSettingsTests.cs`, `TurnPlanVectorTests.cs`, and `STS2AIAgent.Tests.csproj`.
   - Update `docs/project-map.md`, `.trellis/spec/mod/agent-and-ui.md`, and `CHANGELOG.md`.
   - Run full offline verification suite, push branch, and open PR targeting `dev`.
