# Update budget-guard spec after UpdateLimits

## Goal
Keep .trellis/spec/mod/agent-and-ui.md aligned with the current AgentRuntime budget identity.

## Requirements
- Replace the claim that SaveSettings swaps in a new budget guard.
- Document SessionBudgetGuard.UpdateLimits: limits change, accumulated usage does not reset, the same guard instance is kept across SaveSettings and Reload.
- Mention that Observe records Math.Max(0, RequestsSpent) so immediate zero-request actions cannot decrement the ledger.
- Do not change product code.

## Acceptance Criteria
- [x] The spec no longer says SaveSettings replaces the budget guard.
- [x] UpdateLimits and non-negative Observe are described next to the AgentRuntime orchestration section.
