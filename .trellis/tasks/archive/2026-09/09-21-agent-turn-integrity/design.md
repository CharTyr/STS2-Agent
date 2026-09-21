# Design

Keep the action acceptance point before follow-up observations. Remember acceptance in the caller immediately; an observation failure returns an unsettled accepted receipt. Argument JSON must have an object root before any property access.

Add pending token totals to the existing read-only budget check. Keep final accounting separate so partial checks never double-charge. Carry partial receipts in an OperationCanceledException subtype; attach a receipt to run-boundary stops. Recovery commits returned or interrupted receipts before honoring cancellation. Runtime chat, step and teammate paths consume interrupted receipts as well.

Move optional proactive work to an after-turn callback on AutoPlayRecovery, after play accounting and before the next iteration. Acquire the runtime turn gate for proactive work and recheck budget afterward. Preserve completed-play reporting before a proactive cancellation.

Add executable fake-bridge/provider tests and source-wiring contracts. No live-game evidence is claimed. Move runtime accounting methods into a focused partial if needed to stay below the existing size budget.

The shared turn gate now spans receipt commit, not just the model call. Recovery owns it for autoplay; chat and single-step commit before releasing it. This also closes the window in which a queued call could read the old ledger.
