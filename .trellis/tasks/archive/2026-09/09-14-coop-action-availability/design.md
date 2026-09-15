# Design

Centralize invite availability in a CanInviteAiTeammate probe that reuses CoopLaunchPolicy.GetStructuralError after checking visible main-menu. Reuse structural predicate for Continue without dropping save or companion checks. Preserve modal/overlay early returns and no model-ready requirement. Prefer existing pure policy truth-table plus source wiring tests.

Preserve public wire/settings contracts. Rollback via goal commit; preserve previous changes.
