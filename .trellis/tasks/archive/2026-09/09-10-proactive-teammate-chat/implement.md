# Implement: proactive teammate chat

Implement in this order so each step stays compilable.

## Step 1 - pure policy (no dependencies)

- [ ] 1.1 Create `STS2AIAgent/Agent/ProactiveChatPolicy.cs` with the exact surface from `design.md`.
- [ ] 1.2 Keep it free of Godot, game and IO types.

## Step 2 - settings

- [ ] 2.1 Add `ProactiveChatEnabled` (default false) and `ProactiveChatTone` (default `ProactiveChatTones.Default`) to `AgentSettings`.
- [ ] 2.2 Normalise the tone in `EnsureValidShape` and leave enabled false when the file has no value.
- [ ] 2.3 Do not change any existing default.

## Step 3 - chat contract

- [ ] 3.1 Add `bool ReadOnly` and `string? ExtraSystemInstruction` to `ChatOptions` in `IGameBridge.cs`.
- [ ] 3.2 In `AgentLoop.ChatAsync`, append the extra system instruction when present, and make `allowAct` false when `ReadOnly` is set.
- [ ] 3.3 Do not change the teammate-conversation behaviour.

## Step 4 - runtime

- [ ] 4.1 Add session fields: previous situation key, proactive message count, last proactive timestamp.
- [ ] 4.2 Extend the auto-play snapshot with `in_combat`; observe the situation key and derive the moment.
- [ ] 4.3 Add `TryProactiveChatAsync`, called after the play turn while the turn gate is still held.
- [ ] 4.4 Gate on the policy decision; on send, call `_loop.ChatAsync` with `ReadOnly = true`, `AttachState = true` and the tone instruction; then append the reply to the chat history and account the turn with budget recording.
- [ ] 4.5 Wrap the whole proactive path so a failure only records a diagnostic event.
- [ ] 4.6 Reset the proactive counters in `TryResetSessionStats`.

## Step 5 - UI

- [ ] 5.1 Add the opt-in checkbox and the tone dropdown to the settings tab, next to the existing advanced controls.
- [ ] 5.2 Read them in `HarvestSettings` and copy them in `CloneSettings`, otherwise saving or rebuilding the form loses them.
- [ ] 5.3 Persist through the existing save path; do not write JSON from a control callback.

## Step 6 - tests

- [ ] 6.1 Create `STS2AIAgent.Tests/ProactiveChatPolicyTests.cs` covering: disabled by default; unknown tone falls back; the three tone instructions differ and are non-empty; session cap; minimum interval; paused; budget blocked; moment None; the four `Observe` transitions; `SituationKey`.
- [ ] 6.2 Add a case proving a read-only chat cannot act even when the text contains play intent, mirroring the existing teammate-chat test.
- [ ] 6.3 Register every new test in `TestRunner.cs`.
- [ ] 6.4 Add the new Agent source to `STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` compile links. The mod project globs its own sources; the test project does not.

## Step 7 - docs

- [ ] 7.1 Update `docs/proactive-chat-review.md`: the "no proactive speaking path" finding is superseded by an opt-in implementation; state the trigger, the tone values, the volume bounds and the read-only guarantee.
- [ ] 7.2 Update the current-status page capability row and the README lines so they no longer say the capability is missing.
- [ ] 7.3 Every claim carries the offline boundary: implemented and unit-tested, not live-validated.

## Validation Commands

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
```

The test executable is the acceptance gate. Do not describe it as in-game proof.

## Review Gates

- Default settings never enable proactive chat.
- The proactive path cannot dispatch a game action under any input.
- No new timer, thread or polling loop is introduced.
- Auto-play behaviour is unchanged when the opt-in is off.

## Rollback Points

- After step 3: revert `IGameBridge.cs` and `AgentLoop.cs` together.
- After step 4: reverting the runtime hook makes the feature inert without touching tests or docs.
