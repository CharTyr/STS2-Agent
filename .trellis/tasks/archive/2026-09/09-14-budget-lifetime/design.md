# Design

Prefer stable guard identity with a focused synchronized limit-update operation, preserving totals and updating limits under the same guard lock used by Record/CheckBudget. Check runtime initialization/reset separately; explicit session reset remains gated. Avoid merely swapping a provider after report, which can double count if SaveSettings seeds a new guard from the just-reported turn. Preserve public wire/settings shape.

Preserve public wire/settings contracts. Rollback via goal commit; preserve previous changes.
