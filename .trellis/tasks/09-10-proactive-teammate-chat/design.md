# Design: proactive teammate chat

## Ownership boundary

| Layer | File | Change |
| --- | --- | --- |
| Pure policy | `STS2AIAgent/Agent/ProactiveChatPolicy.cs` (new) | moments, decision, tone table, prompt fragments |
| Settings | `STS2AIAgent/Config/AgentSettings.cs` | opt-in + tone + shape repair |
| Chat contract | `STS2AIAgent/Agent/IGameBridge.cs` | `ChatOptions.ReadOnly`, `ChatOptions.ExtraSystemInstruction` |
| Chat execution | `STS2AIAgent/Agent/AgentLoop.cs` | honour both new options |
| Runtime | `STS2AIAgent/Agent/AgentRuntime.cs` | session counters, trigger hook, send path |
| UI | `STS2AIAgent/Ui/AgentOverlayHost.cs` | checkbox + tone dropdown + harvest/clone |
| Tests | `STS2AIAgent.Tests/ProactiveChatPolicyTests.cs` (new), `TestRunner.cs`, `AgentLoopTests.cs`, `STS2AIAgent.Tests.csproj` | registration + cases |
| Docs | `docs/proactive-chat-review.md`, `PRODUCT_PLAN_CURRENT.md`, `README.md`, `README.zh-CN.md` | capability is implemented behind opt-in, not live-validated |

## The policy contract (implement exactly this surface)

```csharp
internal enum ProactiveChatMoment { None = 0, CombatStart = 1, CombatEnd = 2 }

internal readonly record struct ProactiveChatInput(
    bool Enabled,
    string? Tone,
    bool PlayRunning,
    string? BudgetBlock,
    int MessagesSent,
    TimeSpan? SinceLastMessage,
    ProactiveChatMoment Moment);

internal readonly record struct ProactiveChatDecision(bool Send, string? Reason);

internal static class ProactiveChatPolicy
{
    public const int MaxMessagesPerSession = 6;
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(75);

    public static string SituationKey(string screen, bool inCombat);
    public static ProactiveChatMoment Observe(string? previousKey, string? currentKey);
    public static ProactiveChatDecision Decide(ProactiveChatInput input);
    public static string BuildPrompt(ProactiveChatMoment moment);
}

internal static class ProactiveChatTones
{
    public const string Friendly = "friendly";
    public const string Calm = "calm";
    public const string Terse = "terse";
    public const string Default = Friendly;

    public static IReadOnlyList<(string Id, string Label)> Options { get; }
    public static string Normalize(string? tone);
    public static string Label(string? tone);
    public static string BuildSystemInstruction(string? tone);
}
```

Semantics that are not negotiable:

- `SituationKey(screen, inCombat)` returns `"COMBAT"` when in combat, otherwise the screen name.
- `Observe` returns `None` when the key is unchanged, `CombatStart` when the new key is COMBAT, `CombatEnd` when the previous key was COMBAT, and `None` otherwise.
- `Decide` returns `Send = false` with a stable reason string for: disabled, no moment, not playing, budget blocked, session cap reached, minimum interval not elapsed. Only when every gate passes does it return `Send = true`.
- Check order is fixed: enabled, moment, play running, budget, session cap, minimum interval.
- `Normalize` maps null/empty/unknown to `Default@@ and otherwise to the lower-cased known id; it must never throw.
- `BuildSystemInstruction` returns a distinct non-empty instruction per tone, and every variant repeats the shared rules: at most one short sentence, grounded in current state, no invented outcomes, no acknowledgement of being prompted.

## Trigger placement

The moment is observed inside `AutoPlayLoopAsync`, where the loop already builds a state snapshot under the turn gate (screen, phase, run_id). Extend that snapshot with `payload.in_combat@@, remember the previous situation key on the runtime, and send after the play turn completes so the play decision is never delayed by a chat call.

Because the loop only runs while auto-play is active, "no message while paused" follows from placement, and is additionally asserted by the policy input `PlayRunning`.

## Read-only guarantee

`AgentLoop.ChatAsync@@ currently derives `allowAct@@ from `!TeammateConversation && (AllowAct || PlayIntent.Detect(userText))`. A prompt-shaped proactive message could therefore fall through to acting. Adding `ChatOptions.ReadOnly` and folding it into that expression makes the guarantee structural and directly testable.

## Failure isolation

The proactive send is wrapped so that any exception is recorded as a diagnostic event and the auto-play turn still returns its real result. A broken chat path must not stop the game loop.

## Tradeoffs

- Two moments (combat start, combat end) instead of a rich event taxonomy: both are detected from state the loop already reads, and both are moments a co-op partner would naturally speak. Adding more moments later means extending the enum, not the runtime.
- Fixed per-session cap and interval instead of a user-facing frequency slider: keeps the UI to one checkbox and one dropdown while still being quiet by construction.
- The conversation model handles the message, not the play model, so the play model's context is untouched.

## Rollback

Each layer is independently revertible: disabling the checkbox makes the runtime inert; reverting the runtime hook leaves the policy and settings unused but harmless.
