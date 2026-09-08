# Game State and Action Contracts

## Build state once, expose two views

[GameStateService.BuildStatePayload](../../../STS2AIAgent/Game/GameStateService.cs) is the raw state entry point. It reads the current `IScreenContext`, combat state, and run state, resolves a canonical `screen`, derives `available_actions`, and builds the screen-specific sections (`combat`, `run`, `map`, selection/reward/shop/modal/game-over sections, and multiplayer data). The returned `GameStatePayload` contains a `state_version`, nullable section payloads, and an `agent_view`.

`agent_view` is produced during the same build by `BuildAgentViewPayload`. It is the compact agent-facing projection with its own version, the same screen/run/session identity, compact combat/run data, and both `actions` and `available_actions` aliases. `GameBridge.GetCompactStateJsonAsync` serializes `state.agent_view ?? state`; `GetRawStateJsonAsync` serializes the complete payload. Preserve this relationship: adding a raw field that agents need usually also requires adding a deliberately compact representation, and the two views must describe the same snapshot.

Do not rebuild state in a caller by reaching into native managers. Use `BuildStatePayload` so all screen guards, local-player selection, and payload conventions remain centralized. If a new screen or modal is introduced, update its screen resolution, payload builder, availability predicates, and compact projection together.

## Available actions are the legal action source

`BuildAvailableActionNames` is the canonical name list used in raw state and compact state. `BuildAvailableActionsPayload` adds descriptors such as `requires_index` and `requires_target` for clients that need parameter hints. Both methods use the same `Can*` predicates and early modal/unlock guards in [GameStateService](../../../STS2AIAgent/Game/GameStateService.cs).

Agent and MCP callers must select an action from the latest `available_actions` and recompute indexes from that same state. This is encoded in [AgentTools](../../../STS2AIAgent/Agent/AgentTools.cs), [AgentLoop.ExecuteActAsync](../../../STS2AIAgent/Agent/AgentLoop.cs), and the native/Python `act` implementations. A screen name alone is not permission to act; stale or guessed action names must be rejected.

When adding an action, make the whole path explicit:

1. Add the name and its `CanX` condition to `BuildAvailableActionNames`.
2. Add a corresponding descriptor to `BuildAvailableActionsPayload` for every exposed action, including actions without arguments. `ActionDescriptor` contains only `name`, `requires_index`, and `requires_target`; coordinate/tool requirements belong in the action request schema and handler validation.
3. Add the normalized action name to the `GameActionService.ExecuteAsync` switch.
4. Implement an `ExecuteXxxAsync` method that checks current availability first, validates request fields next, invokes native controls, waits for a stable condition, and returns `ActionResponsePayload` with a fresh `GameStatePayload`.
5. Add or update the compact/raw payload only when the action needs state that is not already represented.
6. Register meaningful deterministic coverage in [TestRunner](../../../STS2AIAgent.Tests/TestRunner.cs) and, when the public tool surface changes, align `AgentTools`, `NativeMcpServer`, and Python `server.py`.

The source-level [UnlockScreenContractTests](../../../STS2AIAgent.Tests/UnlockScreenContractTests.cs) is an example of protecting both action names and screen-specific availability. It is better to assert the observable contract than to duplicate private implementation details in a new test.

## Action-handler shape

[GameActionService.ExecutePlayCardAsync](../../../STS2AIAgent/Game/GameActionService.cs) is the representative handler:

```text
check CanPlayAnyCard for the current screen/state
  -> require card_index
  -> resolve local player and hand
  -> validate card_index against the current hand
  -> validate supported target type and target_index
  -> call card.TryManualPlay(target)
  -> wait for a bounded transition
  -> return action/status/stable/message/fresh state
```

The ordering matters. Availability errors should not be reported as malformed parameters; missing fields should not cause a native lookup; and indexes must be checked against the current collection before indexing it. `TryManualPlay` and similar native methods are the final game-rule gate, so a false result becomes an action failure rather than a fabricated success.

Handlers are invoked inside the Router/GameBridge game-thread boundary described in [architecture.md](architecture.md). They should use `GameThread.WaitForNextFrameAsync` and native calls directly from that context, without adding another `GameThread.InvokeAsync` around the whole handler.

## Error categories

Use the existing [ApiException](../../../STS2AIAgent/Server/ApiException.cs) fields and categories. The current action code demonstrates these mappings:

| Situation | HTTP | Code | Retryable | Local example |
| --- | ---: | --- | :---: | --- |
| Action is not currently legal or not supported | 409 | `invalid_action` | no | `CanPlayAnyCard` is false; `TryManualPlay` returns false; unsupported target type |
| Ordinary request shape is invalid | 400 | `invalid_request` | no | `play_card` without `card_index`; missing required option/coordinate |
| Required action target is missing, or index/target is invalid for current state | 409 | `invalid_target` | no | missing `target_index` for a targeted card; card, enemy, player, option, or reward index out of range |
| Native state needed to perform an otherwise legal action is missing | 503 | `state_unavailable` | usually yes | local player, hand, combat room, or button cannot be resolved |
| Companion requests an actor it does not own | 403 | `forbidden_actor` | no | `CompanionActPolicy` rejection in `ExecuteAsync` |

`retryable: true` is a statement about safe recovery after state availability changes, not a generic flag for every failure. Include action/screen/index details in `Details` when they help a caller diagnose a stale snapshot, but keep secrets and arbitrary native object dumps out of the response. Router serializes `details` and `retryable` consistently through `WriteErrorAsync`.

## Transition and response semantics

Actions that span frames need finite waiting logic, using a `DateTime.UtcNow` deadline or equivalent explicit timeout. Existing code uses both `WaitFor*TransitionAsync` helpers and inline deadline/frame loops, for example `ExecuteChooseCapstoneOptionAsync` and bundle handlers. Existing examples include five seconds for end-turn, twelve seconds for card play, ten seconds for many screen/button transitions, and twenty seconds for some save/reward flows. The exact timeout should reflect the operation, but it must be finite and visible at the call site.

`ActionResponsePayload` has four required user-facing fields plus state: `action`, `status`, `stable`, `message`, and `state`. A stable transition returns `status = "completed"`, `stable = true`, and a fresh state. If the native action was accepted but the post-action condition did not settle before the deadline, return `status = "pending"`, `stable = false`, a truthful transition message, and the freshest available state. Do not claim completion because the click or method call returned.

`GameBridge.ActAsync` preserves `action`, `status`, `stable`, and `message` while replacing `state` with the compact `agent_view` when available. The Python client may reconcile an uncertain HTTP response with a fresh `/state`; this behavior is covered by [test_action_replay_safety.py](../../../mcp_server/tests/test_action_replay_safety.py). Never blindly replay a side-effecting action after a response-read timeout when a state reconciliation is possible.

## Waiting and frame safety

[GameThread.WaitForNextFrameAsync](../../../STS2AIAgent/Game/GameThread.cs) bounds a missing `ProcessFrame` signal at 50 ms, which keeps outer action deadlines effective when the window is occluded. It is a frame primitive, not proof that a transition happened. A transition waiter should:

- capture the relevant pre-action identity or count when necessary;
- await frames while checking a concrete screen/button/state predicate;
- stop at a finite deadline;
- return the best honest settled/pending result; and
- preserve the game-thread context after each await.

The existing card-play fallback illustrates the recovery pattern: after a twelve-second wait, it attempts bounded cancellation/recheck cycles and still returns `pending` if the state is not stable. Copy that shape only when the operation is safe to probe; do not add broad cancellation that could interrupt a native action with irreversible effects.

