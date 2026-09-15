# Update budget-guard spec after UpdateLimits

Date: 2026-09-15

## Change
`.trellis/spec/mod/agent-and-ui.md` no longer says `SaveSettings` swaps in a new budget guard.

The AgentRuntime orchestration section now documents:

- `SaveSettings` / `ReloadSettings` keep the same `SessionBudgetGuard` and call `UpdateLimits`.
- New limits take effect immediately; accumulated tokens/requests are not reset.
- `Observe` records `Math.Max(0, RequestsSpent)`.
- Constructor and `TryResetSessionStats` still create a fresh guard.

The SettingsStore paragraph matches that wiring. No product code changed.

## Files
- .trellis/spec/mod/agent-and-ui.md

## Commands
```
cd mcp_server
uv run --locked python -m unittest discover -s tests -v
```

Spec-only change; the suite was run for the sibling POSIX task on the same worktree.

## Results
Spec no longer contains "swaps the in-memory settings and budget guard". UpdateLimits and non-negative Observe sit next to the AgentRuntime orchestration section. Unittest 206/206 PASS.

## Limits
Documentation only. No C# edits, no live game, no commit/push/release.
