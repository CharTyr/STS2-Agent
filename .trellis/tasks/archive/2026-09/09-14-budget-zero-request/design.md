# Design

Use actual RequestsSpent as the authoritative count after auditing production AgentTurnResult producers; distinguish unknown accounting only if a real producer requires it. Do not infer a paid request from Acted or error text. Retain zero-safe/negative-clamped accounting. This task depends on goal 1 and must preserve its stable-ledger change.

Preserve public wire/settings contracts. Rollback via goal commit; preserve previous changes.
