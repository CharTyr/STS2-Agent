# Speed & performance research — game speed / "turbo" for auto-play, and this mod's frame-time cost

> Task `.trellis/tasks/10-28-bold-experience-overhaul` · baseline v0.17.0, branch `dev`
> Method: repo source read (`STS2AIAgent/`), decompiled game sources (`extraction/decompiled/`,
> 2026-03-10), the **installed** `sts2.dll` (2026-08-16) spot-checked type by type with `ilspycmd`,
> and **SpeedX 0.11.7 decompiled** into `build/research-speedx/` (scratch, gitignored).
>
> Headline: **a safe, no-Harmony, one-line "turbo" toggle exists** —
> `SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant` — and the worst frame-time cost in
> this mod is not the 120 ms SSE poll at all: it is `GameBridge.WaitUntilActionableAsync`, which builds
> a **full state payload once per frame** while it waits (up to 60 builds/s, versus 8.3/s for SSE).

---

## 1. How it works today

### 1.1 There is no speed control today — in the mod or the agent

The mod never touches time. `grep -i "FastMode|PrefsSave|TimeScale"` over `STS2AIAgent/` returns
exactly one hit, and it is unrelated (`NativeCoopLoadGuard` imports `HarmonyLib`).
Auto-play speed is therefore bounded by three things, none of which is configurable:

1. the game's own animation/wait durations (usually `FastMode = Normal`, the game's default),
2. the mod's own polling floors and stability windows (200 ms sampler, 24-frame settle, 800 ms tick,
   120 ms event poll),
3. the model's thinking latency.

### 1.2 The game's speed surface (installed `sts2.dll`, verified)

| Member | Kind | Where | What it does |
|---|---|---|---|
| `MegaCrit.Sts2.Core.Settings.FastModeType` | public enum | `.../Settings/FastModeType.cs` | `None = 0, Normal = 1, Fast = 2, Instant = 3`. XML doc on the installed build: *"Enum used for changing how fast the game runs. This is **NOT** a multiplier-based speed setting."* |
| `SaveManager.Instance.PrefsSave.FastMode` | public **instance property, settable** on a public class | `MegaCrit.Sts2.Core.Saves.PrefsSave` (JSON `fast_mode`) | The live speed preference. Default `Normal`. Read by ~114 call sites gated on `== Fast`, `!= Instant`, `< Fast`. |
| `Cmd.Wait(float seconds, CancellationToken, bool ignoreCombatEnd = false)` | public static | `MegaCrit.Sts2.Core.Commands.Cmd` | The **single choke point for every scripted pause in combat.** When `PrefsSave.FastMode == Instant` (and `ignoreCombatEnd` or combat is not ending) the whole body is skipped: `seconds <= 0`-equivalent, returns completed. Otherwise `SceneTree.CreateTimer(seconds)` + `WaitInternal`. |
| `Cmd.Wait(float seconds, bool ignoreCombatEnd = false)` | public static overload | same | Delegates to the 3-arg overload. |
| `Cmd.CustomScaledWait(float fastSeconds, float standardSeconds, …)` | public static | same | Picks the duration by `FastMode` (`Fast`→fast, `Normal`→standard, `Instant`→nothing, `None`→`ArgumentOutOfRangeException`). |
| `CombatState.GodotTimerTask(double timeSec)` | **private static** | `MegaCrit.Sts2.Core.Combat.CombatState` | Only used by `GetCreatureAsync(uint? combatId, double timeoutSec)` as the timeout half of a `Task.WhenAny`: throws `InvalidOperationException("Timed out waiting for creature … to spawn!")`. **This is a *timeout*, not an animation** — shrinking it is the one genuinely dangerous knob (see §2.5). |
| `NGame.DebugModifyTimescale(double offset)` | private instance, debug key only | `MegaCrit.Sts2.Core.Nodes.NGame` | The *only* real `Engine.TimeScale` writer: `Math.Clamp(Engine.TimeScale + offset, 0.1, 4.0)` → caps at **4x**, prints `TimeScale:x` on screen. Not usable as a turbo path (private, debug-bound, 4x ceiling). |
| `NGame` boot (`_Ready`) | — | same | `if (!OS.HasFeature("editor") && PrefsSave.FastMode == FastModeType.Instant) PrefsSave.FastMode = FastModeType.Fast;` — **Instant never survives a restart.** This is the safety net that makes §5/P1 cheap. |
| `AutoSlayer.PlayRunAsync` | private method of the game's own autoplay harness | `MegaCrit.Sts2.Core.AutoSlay.AutoSlayer` | Sets `SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;` at run start — **precedent that a headless/auto player setting this field is the intended use.** |
| `InstantConsoleCmd` | public console command `instant` | `.../DevConsole/ConsoleCommands` | Toggles `FastMode` `Fast ↔ Instant` from the dev console. **Proves the value is safe to flip at runtime with no teardown.** |

Practically, `FastMode = Instant` gives:
* **all** `Cmd.Wait` pauses in combat become zero (that is the bulk of perceived combat slowness),
* FastMode-gated tweens collapse to `0.01f` (`CardPileCmd`, `NTransition`, `NMapScreen` tick waits,
  `NRewardButton`-adjacent flows, `NEventOptionButton`, `NAncientEventLayout`, banners),
* transitions/logo/act banners skip their `TweenInterval` (`NActBanner`: 2.0 s → 0.5 s under `Fast`,
  skipped entirely under `Instant`).

What it does **not** touch: Spine animations (`MegaAnimationState`/`MegaTrackEntry.SetTimeScale`),
`NHitStop.DoHitStop`, per-frame VFX `_Process` velocities, `NCardFlyVfx` private `_speed/_accel/_duration`,
tween literals not gated on FastMode. That is exactly the gap SpeedX fills with Harmony.

### 1.3 The mod's own timing inventory (all in `STS2AIAgent/`)

| Mechanism | Symbol / file | Cadence / budget | Notes |
|---|---|---|---|
| Frame wait | `GameThread.WaitForNextFrameAsync` (`Game/GameThread.cs`) | one `SceneTree.ProcessFrame`, **bounded at 50 ms** (16 ms `Task.Delay` fallback when the tree/node is gone or the window is occluded) | The only sanctioned "wait one frame". |
| Per-frame state build | `GameBridge.WaitUntilActionableAsync` (`Agent/GameBridge.cs`) | loop: post `GameStateService.BuildStatePayload()` → `await WaitForNextFrameAsync()`; `ActionableProbeBudget = 5 s` per probe, outer 20 s (`AgentLoop.PlayOnceAsync`, `AgentLoop.ExecuteActAsync`), also `NativeMcpServer.Tools` | **One full payload per frame.** See §3.3. |
| Actionability definitions | `AgentLoop.PassiveActions` | — | "actionable" = any available action that is not passive. |
| Combat stability sampler | `GameStateService.CombatActionSnapshotStableDelay = 200 ms`, `IsCombatActionSnapshotStable`, `BuildCombatActionReadinessSignature`, `ResetCombatActionReadiness` (`Game/GameStateService.Combat.cs`) | the readiness signature must be byte-identical for ≥200 ms **and** `GameActionService.AreGameActionsSettled()` | Static fields advanced only by a state build; reason string is `snapshot_stabilizing` / `ready`. |
| Fixed frame-count settles | `TryResolveCardRewardAsync` (`GameActionService.cs`, `for i < 24`), `CardGridScrollSettleFrames = 12` (`GameActionService.CardGrid.cs`), `StageOneConfirmCooldownFrames = 5` (`GameActionService.Rewards.cs`), `FramesToSettleAfterHide = 2` (`Agent/ScreenshotCapturePolicy.cs`) | 24 / 12 / 5 / 2 frames | Duration-based, not event-based. |
| Transition waits | ~70 `WaitForXTransitionAsync` helpers across `GameActionService.*.cs` | each polls `WaitForNextFrameAsync` against a `DateTime` deadline (budget table: `docs/project-map.md` §6 — `play_card` 12 s, end-turn 5 s, most screens 10 s, reward drain 20 s) | Cheap per frame: they call `ActiveScreenContext.Instance.GetCurrentScreen()` / typed predicates, **not** `BuildStatePayload`. Only `WaitUntilActionableAsync` builds. |
| Overlay tick | `AgentOverlayHost.OnProcessFrame` (`Ui/AgentOverlayHost.cs`) | every **800 ms** when the panel is visible; overflow sentinel every 4th tick (3.2 s) | see §3.4. |
| Event stream poll | `GameEventService.DefaultPollIntervalMs = 120` (env `STS2_EVENT_POLL_MS`, 16–5000), `EventPollingCoordinator` (`Server/`) | 120 ms **only while ≥1 SSE subscriber exists** | see §3.2. |
| Turn driver backoff | `AutoPlayRecovery.Observe` (`Agent/AutoPlayRecovery.cs`) | waiting-for-player/combat → 1 s; executed-but-unsettled → `2^min(n,3)` s; thinking-budget → same; repeated no-op → 1 s; failures → `2^n` s | `NoProgressPolicy`: `RepeatThreshold = 3`, `UnsettledLimit = 5`, `ReasoningBudgetLimit = 5`, `WaitingForGameLimit = 150 s`. |
| Per-turn game reads | `AgentRuntime.AutoPlayLoopAsync` (`Agent/AgentRuntime.cs`) | 1 snapshot build + 1 `TryCompanionImmediateAsync` build + `AgentLoop.PlayOnceAsync` (1–2 compact builds + wait loop) + act + post-act settle | ≈ 6 builds minimum, unbounded maximum. |

Long-press / `NEndTurnButton` handling and `GameTaskWaitPolicy` bound the game-`Task` waits.

---

## 2. SpeedX mechanism in detail (`mods/SpeedX/SpeedX.dll`, v0.11.7)

Decompiled to `build/research-speedx/SpeedX/`. Entry point:

```csharp
[ModInitializer("Initialize")] public class MainFile {
  public static void Initialize() {
    new Harmony("sts2.piyixiajiuhenfen.speedx").PatchAll(Assembly.GetExecutingAssembly());
    SettingsPersistence.Load(); I18n.Initialize(); AutoEndTurn.Initialize();
    var root = ((SceneTree)Engine.GetMainLoop()).Root;
    root.CallDeferred(AddChild, new CenterToastOverlay());
    root.CallDeferred(AddChild, new TurboOverlay());   // F9 settings window + per-frame AutoEndTurn.Update
    root.CallDeferred(AddChild, new TurboMenu());
    ModConfigBridge.DeferredRegister();
  }
}
```

**SpeedX never touches `Engine.TimeScale`.** There is not a single `TimeScale` assignment in the
assembly (only Spine `SetTimeScale` *method names*). Speed is bought entirely by (a) shrinking the
duration arguments the game passes to its own wait primitives and (b) deleting frames of animation.

### 2.1 The two speed patches (the actual "up to 20x")

```csharp
[HarmonyPatch(typeof(Cmd), "Wait", new[]{ typeof(float), typeof(CancellationToken), typeof(bool) })]
static class WaitPatch {
  [HarmonyPrefix] static void Prefix(ref float seconds) {
    if (!TurboSettings.TurboEnabled || seconds <= 0) return;
    var m = TurboRuntimeGuards.GetGameplaySpeedMultiplier();          // default 0.1
    if (CombatManager.Instance is not { IsInProgress: true }) m = Math.Max(m, 0.5f); // ≤2x outside combat
    seconds *= m;
  }
}

[HarmonyPatch(typeof(CombatState), "GodotTimerTask")]
static class TimerSpeedPatch {
  [HarmonyPrefix] static void Prefix(ref double timeSec) {
    if (TurboSettings.TurboEnabled && TurboSettings.TimerSpeedEnabled) timeSec *= TurboRuntimeGuards.GetGameplaySpeedMultiplier();
  }
}
```

* `SpeedMultiplier` is a **duration multiplier**, default `0.1f` (10x). The on-disk setting is stored
  inverted — `1/N`, or `N/100` when > 20 (`SettingsPersistence.Load`) — so "20x" = `0.05f`.
* Non-combat waits are floored at `0.5` (2x), so map/menu animations are only doubled.
* Every patched wait also credits a "time saved" counter (`TimeSavedTracker.Add`).

### 2.2 The rest of the patch surface (31 Harmony attributes)

| Group | Target type / method | Kind | Effect |
|---|---|---|---|
| Combat VFX | `NDamageNumVfx._Process`, `NHealNumVfx._Process`, `NBezierTrail._Process`, `NCardTrail._Process` | prefix on `ref double delta` | `CombatVfxSpeedHelper.TryScaleDelta`: `delta /= SpeedMultiplier` (i.e. 10x) but **only while `CombatManager.Instance.IsInProgress`** |
| Card fly | `NCardFlyVfx._Ready`, `NCardFlyShuffleVfx._Ready` | prefix + reflection | `CardFlySpeedHelper` sets private `_speed *= 3`, `_accel *= 3`, `_duration /= 3` (fixed 3x, not the multiplier) |
| Card fly (power) | `NCardFlyPowerVfx.GetDurationInternal` | postfix | duration divided (fixed factor) |
| Hit-stop | `NHitStop.DoHitStop` | prefix, return false | removes the freeze frame entirely |
| Spine | `MegaTrackEntry.SetTimeScale`, `MegaAnimationState.SetTimeScale` | prefix on `ref float` | multiplies animation timescale; **off by default** (`SpineSpeedEnabled = false`) |
| Banners | `NPlayerTurnBanner.Display`, `NEnemyTurnBanner.Display`, `NActBanner.AnimateVfx`, `NCombatStartBanner.AnimateVfx` | prefix, `QueueFree()` + `__result = Task.CompletedTask`, return false | banner never plays; the awaited `Task` completes immediately, so **the game's own sequencing does not stall** |
| Auto-proceed | `NProceedButton.OnEnable` (postfix) | schedules a click | `AutoProceedScheduler`: waits 0.5 s, retries at 0.2 s, then `EmitSignal(Released)`; skips `NRewardsScreen`, `RestSiteRoom`, certain events |
| Auto-proceed (screens) | `NEventRoom.SetOptions`, `NRewardsScreen.TryEnableProceedButton`, `RelicCmd.Obtain` | postfix | same scheduler for event/reward/treasure |
| Auto-target | `NTargetManager.StartTargeting` | prefix | picks the first valid target for single-target cards |
| QuickPlay | `NMouseCardPlay.Start`, `NMouseCardPlay._ExitTree`, `NMouseCardPlay.IsCardInPlayZone`, `NCardPlay.TryPlayCard`, `NCardPlay.CancelPlayCard`, `NCardHolder._GuiInput`, `NPlayerHand._UnhandledInput`, `NPlayerHand.Add`, `NPlayerHand.ReturnHolderToHand` | mixed | spacebar insta-play + visual-state repair |
| Housekeeping | `NCombatRoom._Ready` | postfix | force-closes a lingering map/capstone overlay on combat entry |

Auto-end-turn is **not** a patch: `AutoEndTurn.Initialize()` subscribes
`CombatManager.Instance.CombatSetUp/CombatEnded/TurnStarted`, and `TurboOverlay` calls `Update(delta)`
each frame from its own `_Process`. It ends the turn through the **game's action queue**, not the UI:

```csharp
RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(_localPlayer, round));
// and, when new playable cards appear after firing:
RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new UndoEndPlayerTurnAction(_localPlayer, round));
```

with a damage forecast (`ShouldBlockForVisibleLethalThreat`, simulating hand DoT cards, enemy intents,
poison/constrict/magic-bomb/doom) as the death guard, plus 0.8 s hand-change and energy-change
cooldowns and a 0.8 s turn-start grace.

### 2.3 Settings and persistence

`F9` → `TurboMenu`; state lives in `TurboSettings` (plain static properties, `TurboEnabled = true`,
`SpeedMultiplier = 0.1f`, `UnsafeMultiplayerSpeedEnabled = true`, `HitStopDisabled = true`,
`SkipBannersEnabled = true`, `TimerSpeedEnabled = true`, `CombatVfxSpeedEnabled = true`,
`CardFlySpeedEnabled = true`, `AutoEndTurnEnabled = true`, `AutoLootEnabled`, `AutoProceedEnabled`,
`AutoTargetEnabled`, `DamagePreviewEnabled`, `QuickPlayEnabled`, keys…).
Persisted as JSON at `user://ModConfig/sts2.piyixiajiuhenfen.speedx.json` (debounced writes).

### 2.4 The multiplayer guard (important precedent)

```csharp
public static float GetGameplaySpeedMultiplier() {
  if (!TurboSettings.TurboEnabled) return 1f;
  if (!IsRealMultiplayerRun()) return TurboSettings.SpeedMultiplier;
  if (TurboSettings.UnsafeMultiplayerSpeedEnabled) { /* warn once */ return TurboSettings.SpeedMultiplier; }
  return 1f;                       // suppress entirely
}
public static bool IsRealMultiplayerRun() =>
  RunManager.Instance.IsRunning &&
  NetGameTypeExtensions.IsMultiplayer(RunManager.Instance.NetService.Type);
```

Note the default: SpeedX ships with the **unsafe** override **on**, and logs
*"risking desync if player mods/settings differ."* Its own author does not consider combat timers
safe in co-op.

### 2.5 What this means for us

* The mechanism is **duration-argument scaling through Harmony prefixes on two members**, plus
  "return false + completed Task" on banner methods whose return value the game awaits. It is not a
  global clock change, so it does not touch physics, tweens we don't patch, or the render loop.
* `CombatState.GodotTimerTask` is a **timeout for a networked creature spawn**; scaling it to 0.1x
  turns a 10 s network budget into 1 s and would throw `InvalidOperationException("Timed out waiting
  for creature …")` on a laggy co-op peer. This is the one patch that a co-op-safe implementation
  should drop or clamp hard (≥0.5, or skip in multiplayer entirely).
* Because SpeedX is **installed on this machine**, a second Harmony prefix on `Cmd.Wait` would
  **multiply**: `0.1 × 0.1 = 100x`. Any turbo we ship must detect an existing prefix on the target
  (`Harmony.GetPatchInfo(method)?.Prefixes`) or, better, cooperate through the game's own
  `FastMode` where possible (§5/P1) and only fall back to Harmony behind an explicit opt-in.

---

## 3. Costs

### 3.1 The one real measurement we have

`docs/live-validation-checklist.md` (2026-09-18, 8th live round): `GET /health → state_build`
reported **3,049 builds, p50 2.8 ms, p95 16.8 ms, 1 build over the 100 ms threshold (113.8 ms,
`MAIN_MENU`)**. So one `GameStateService.BuildStatePayload` is ~1/6 of a 60 fps frame at p50 and
**a whole frame at p95**. Everything below multiplies that number.

### 3.2 Event stream (SSE) — already subscriber-gated, still fixed-cadence

`GameEventService` + `EventPollingCoordinator` (rewritten for the #33 report; commits `524107b`,
`c017302`, `c3c531e`):

* **No subscriber → zero work.** The loop waits on a demand signal (`WaitForDemandAsync`) and
  `Start()` is a no-op until `SetSubscriberCount(>0)`. Measured: `samples 0 → 0` over 12 s idle,
  `354 → 354` across a later idle window.
* **One subscriber → one loop at 120 ms**, and every iteration does
  `await GameThread.InvokeAsync(GameStateService.BuildStatePayload)`, i.e. **a full payload built on
  the game thread, ~8.3 times per second** while a client is attached — ~23 ms/s of game thread at
  p50, ~140 ms/s at p95, plus the GC churn of the whole object graph (dozens of records, arrays and
  strings per build).
* The payload is **not serialized** in this path (only `StateDigest.FromState` reads ~15 fields), so
  the cost is construction + allocation, not JSON.
* Snapshot is recorded, not re-announced (a previous bug broadcast `stream_ready` every poll).
* Disconnect lag: ~33 s (two 15 s heartbeats) before the poll loop notices a dropped client.
* The in-game agent **never subscribes** (`AgentRuntime` only registers
  `_decisions.Recorded += GameEventService.Instance.PublishDecision`), so solo auto-play pays **zero**
  SSE cost. This is why #33-style stutter only shows up for MCP clients.

**Verdict on issue #33:** the "120 ms polling with no subscriber" half is **fixed**; the
"full state build every 120 ms while a client is attached" half is **still true**, and it is now
the dominant idle-with-client cost.

### 3.3 The per-frame build storm — bigger than the SSE poll

`GameBridge.WaitUntilActionableAsync` (`Agent/GameBridge.cs`):

```
while (UtcNow < deadline) {
    actionable = await GameThread.InvokeAsync(() => {
        var state = GameStateService.BuildStatePayload();   // <-- per frame
        ...
        return available_actions.Any(a => !PassiveActions.Contains(a));
    }, probeBudget, token);
    if (actionable) return true;
    await GameThread.WaitForNextFrameAsync();
}
```

At 60 fps that is **up to 1,200 full builds inside one 20 s wait** — 7x the rate of the SSE poll at
p50, 60x at worst — and it runs exactly when the game is mid-animation and the player is most
sensitive to hitching (turn transitions, reward drains, post-`act` settling). Callers:

| Caller | Budget | Frequency |
|---|---|---|
| `AgentLoop.PlayOnceAsync` | 20 s | **every auto-play turn** |
| `AgentLoop.ExecuteActAsync` (unsettled act) | 20 s | after any `pending` action |
| `AgentLoop.WaitUntilActionableJsonAsync` (the `wait_until_actionable` tool) | caller's `timeout_seconds` (1–120 s) | per tool call |
| `NativeMcpServer.Tools` `act` / `wait_until_actionable` | 20 s | per MCP action |

Because the build runs *inside* the posted callback on the game thread, it does not overlap with the
frame — it delays it. A 2.8 ms build is 17 % of a 16.7 ms frame; a 16.8 ms build is a dropped frame,
every frame, for as long as the wait lasts.

### 3.4 Overlay tick — 800 ms, moderate, mostly allocation

`AgentOverlayHost.OnProcessFrame` (visible panel only):

| Work | Cost |
|---|---|
| hotkey poll + `_placement.FollowViewport()` | cheap, every frame (not just the tick) |
| `PlaySummaryText()` | small string |
| `StatusWithElapsed` | string |
| `RefreshChatLog()` (`ChatTailLimit = 80`) | rebuilds **up to 82 BBCode strings** every tick, then `RichLog.Show` diffs → usually no node rebuild, but the strings + list are re-allocated each time |
| `RefreshContinueAvailability()`, `RefreshJevReading()`, `RefreshModeControls()` | a few settings reads |
| `RequestTeammateStatusRefresh()` + `TeammateLiveText()` | throttled inside the runtime |
| `RefreshDecisionPage` | **only when the decision log is open** and only via `RichLog` diffing |
| `_overflow.Report` (every 4th tick ≈ 3.2 s) | walks the whole visible control tree — explicitly amortised for this reason |

So ~1.2 ticks/s × a handful of strings: not the stutter source, but it is **unconditional** — it
does the same work when nothing changed, and it is the one place that re-reads settings and the chat
history on a timer instead of on an event.

### 3.5 Per-action cost

Every `Execute<X>Async` returns `ActionResponsePayload { status, stable, message, state }` — `state`
is a **fresh full payload** (`state = GameStateService.BuildStatePayload()` appears 70+ times in
`GameActionService.*.cs`). Add the pre-action decision snapshot
(`BuildDecisionSnapshotPayload`, a full build) and the post-action settle (`WaitUntilActionableAsync`).
So **one agent action ≈ 3–6 full builds plus one per-frame wait loop**, on the game thread.

`docs/project-map.md` §4 "Traps" already records the related known cost:
*"Session persistence currently runs inside the game-thread read at a run boundary (one frame hitch
per boundary, not per frame)."* Confirmed: `ObserveSessionStateSnapshot` →
`ObserveSessionRunBoundary` → `FlushSessionIfDirty()` (JSON build + `PlaySessionStore.Save` file IO)
→ `_sessionStore.Load(runId)` all execute inside `GameThread.InvokeAsync`.
`AgentRuntime.ObserveCurrentSessionAsync` adds **one more full build on the game thread** at the
start of single-step/chat/instruction paths.

### 3.6 Small stuff worth naming

* **Reflection** is cheap per call (registry resolved once at startup, then a dictionary lookup),
  but the accessors allocate on every read: `ReflectedGameMembers.UnlockItemFields` builds a new
  array + LINQ chain **per access**, and `GameStateService.StartRunLobbyMaxPlayersField` is a
  property that re-does a dictionary lookup per read. Both are inside `BuildStatePayload`.
* **Screenshots** (`Vision/ScreenshotService.CaptureJpeg`): `viewport.GetTexture().GetImage()` +
  `Image.Resize(Lanczos)` + JPEG encode **on the game thread**, with a 3 s capture deadline and 2
  drawn frames of settle. One-shot per request, so acceptable, but it is a guaranteed multi-frame
  stall when used.
* **`/state`, `/action`, `/actions/available`** each build once and serialize; agent-view rewriting
  (`GameStateService.AgentView.cs`) runs a second pass over the payload.

### 3.7 Frame-time budget summary

| Source | Builds/s (steady state) | Game-thread ms/s @p50 | When active |
|---|---|---|---|
| `WaitUntilActionableAsync` per-frame build | up to 60 | up to ~168 ms/s | every turn while waiting |
| SSE poll @120 ms, 1 subscriber | 8.3 | ~23 ms/s | MCP client attached |
| Overlay 800 ms tick | 0 builds (string churn only) | ~0 | panel visible |
| Per-turn snapshot/response builds | ~6 per turn | ~17 ms per turn | each turn |
| Session flush at run boundary | 1 (payload) + file IO | 3–20 ms spike | run start/end only |

---

## 4. Pain points, ranked

1. **`WaitUntilActionableAsync` builds the world once per frame.** Worst offender by an order of
   magnitude; it converts an idle wait into a continuous frame tax and is invisible in `state_build`
   only because it is spread thin.
2. **Full payloads as the unit of every read and every action response.** 90 call sites; the p95
   build is a whole frame. Nothing in the agent's decision path needs 95 % of the payload
   (`agent_view` is already a projection — but it is computed *after* the full build).
3. **Double/triple reads per turn** (raw snapshot → compact state → decision snapshot → response
   state), each of which can also observe a *different* frame, which is precisely why the
   index-validator machinery exists.
4. **Fixed-cadence SSE** (120 ms, unchanged payload included) while a client is attached, with
   ~33 s to notice a disconnect.
5. **Duration/frame-based settles** — 200 ms stability sampler, 24-frame reward settle, 12-frame
   scroll settle, 5-frame confirm cooldown. These are floors the agent pays on every action, and
   they are exactly what a faster game would make *relatively* more expensive.
6. **Overlay re-derives everything every 800 ms** (chat strings, settings, teammate status) instead
   of on change.
7. **Session file IO on the game thread** at run boundaries.
8. **No speed control at all.** Every visible second of auto-play is spent watching animations a
   model already decided about, and the agent's own waits (20 s budgets) exist mainly to outlast them.
9. **Backoffs with no wake-up**: 1 s / 2 s / 4 s `Task.Delay` between turns even when the game has
   already moved on; nothing can shorten them.

---

## 5. Bold proposals (ranked by impact / effort / risk)

Ranked by expected player-perceived gain. Every sketch respects the repo invariants: game objects
only through `GameThread.InvokeAsync`, every await bounded, one action surface
(`EnumerateAvailableActions`), additive API only, `Loc.T("中文")` for any new string, and new
source files registered in `docs/project-map.md`, `SourceShapeContractTests` budgets and
`SourceCoverageTests.KnownUncompiledSources` (a Harmony patch file is game-dependent).

### P1 — "Turbo" toggle via the game's own `FastMode` (**impact ★★★★★ / effort ★ / risk ★★**)

One public, already-runtime-toggled property; no Harmony, no new dependency, proven safe by the
game's own `instant` console command and by `AutoSlayer` setting it for its own runs.

```csharp
// STS2AIAgent/Game/GameSpeedService.cs  (new, game-dependent -> register in project map + KnownUncompiledSources)
internal static class GameSpeedService
{
    private static FastModeType? _original;   // captured once, never written to disk by us

    internal static bool Enabled => _original != null;

    internal static void Enable()      // call on the game thread
    {
        var prefs = SaveManager.Instance.PrefsSave;
        _original ??= prefs.FastMode;
        prefs.FastMode = FastModeType.Instant;
    }

    internal static void Disable()     // idempotent; safe from ProcessExit
    {
        if (_original is { } original) SaveManager.Instance.PrefsSave.FastMode = original;
        _original = null;
    }
}
```

* **Restore points (all of them):** auto-play pause/stop (`StopAutoPlay` / `PauseForModeSwitchAsync`
  `finally`), overlay "turbo" switch off, run end (`AutoPlayStoppedException` handler), settings mode
  switch, `ModEntry.Shutdown` (already runs on `ProcessExit`/`DomainUnload`), and a defensive
  `try/finally` around each agent turn so a thrown turn cannot leave it on.
* **Crash safety:** we never call `SaveManager.SavePrefsFile()`, so a hard crash leaves the on-disk
  `fast_mode` untouched. If the *game* writes prefs while turbo is on (`NSettingsScreen.OnSubmenuClosed`,
  `NGame.Quit`), the file would record `instant` — and `NGame._Ready` **downgrades `Instant` to
  `Fast` at boot**, so the blast radius is "player gets Fast instead of Normal next launch".
  Belt-and-braces (bold, cheap, and I recommend it): add a Harmony **prefix on
  `SaveManager.SavePrefsFile`** that restores `_original` for the duration of the write and re-applies
  `Instant` afterwards — the player's preference is then never written incorrectly.
* **UI:** a three-position control on the Play page — 关 / 快 (game `Fast`) / 极速 (our `Instant`) —
  saved as a normal setting (touches `AgentSettings`, `SettingsClone`, `EnsureValidShape`,
  `HarvestSettings` per the documented trap), labelled through `Loc.T`.
* **Honest framing:** this is the game's official fast mode, not our speed hack. It removes the
  combat `Cmd.Wait` pauses that dominate auto-play wall clock. Expect a large, immediately visible
  win at essentially zero maintenance cost.
* **Known limits:** `NMapScreen._mapAnimStartDelay`/`_mapAnimDuration` are **`readonly` fields
  initialised at construction**, so a map screen opened before the toggle keeps its old speed;
  Spine, hit-stop and per-frame VFX are unaffected (that is P2's job).

### P2 — Optional Harmony duration scaling, SpeedX-style, explicitly opt-in (**impact ★★★★ / effort ★★★ / risk ★★★**)

Only for players who want > official-`Instant` speed and understand the trade. Two patches, both
`ref`-argument multipliers; **skip `CombatState.GodotTimerTask` in co-op entirely** (it is a network
timeout, not an animation — see §2.5).

```csharp
// TurboPatches.Install(Harmony harmony)
// 1. Cmd.Wait(float, CancellationToken, bool) -- PUBLIC static, but OVERLOADED:
var wait = typeof(Cmd).GetMethod("Wait",
    BindingFlags.Public | BindingFlags.Static, null,
    new[] { typeof(float), typeof(CancellationToken), typeof(bool) }, null);
// 2. (opt-in, single-player only) CombatState.GodotTimerTask(double) -- PRIVATE static:
///    -> register in ReflectedGameMembers.Entries so a rename shows up as "degraded" on /health.

[HarmonyPrefix] static void ScaleWait(ref float seconds)
{
    if (!TurboRuntime.Enabled || seconds <= 0f) return;
    if (TurboRuntime.IsRealMultiplayerRun()) return;             // never in co-op
    seconds *= TurboRuntime.DurationMultiplier;                   // e.g. 0.25f
}
```

Hard requirements before this ships:

* **Refuse to arm when another prefix already exists** on the target —
  `Harmony.GetPatchInfo(method)?.Prefixes` non-empty → log and stay off. SpeedX is installed on this
  very machine and would otherwise stack to `0.1 × 0.25 = 40x`.
* `CombatManager.Instance == null || !IsInProgress` → treat as non-combat and clamp to ≥0.5 (SpeedX
  learned this: it floors non-combat waits at 0.5).
* `ReflectedMemberResolver.Method` calls `GetMethod(name, flags)` and throws `AmbiguousMatchException`
  on overloads, and `ReflectedGameMembers` is keyed by `(Type, name)` — so the **overloaded**
  `Cmd.Wait` cannot go through that registry; only the private `GodotTimerTask` can.
* Everything is bounded anyway: `Cmd.Wait` only *shortens* an await that the game owns.

### P3 — Kill the per-frame build: an event-driven, allocation-free actionable wait (**impact ★★★★★ / effort ★★★ / risk ★★**)

Replace the `BuildStatePayload` inside `WaitUntilActionableAsync` with a cheap probe, and give the
loop something to wake on instead of the frame clock.

```csharp
// Cheapest correct probe: same data the gate's `reason` already encodes, no payload.
private static bool ProbeActionable()          // must run on the game thread
{
    var gate = GameStateService.EvaluateCombatActionGate(...);   // already exists; ~µs
    if (screen == "COMBAT") return gate.Usable;
    return GameStateService.HasAnyNonPassiveAction(...);          // thin wrapper over the one walk
}
```

* Publish a **monotonic `StateChangeSignal`** (an `AsyncManualResetEvent`-style
  `TaskCompletionSource` bumped by a single `GameStateService.NotifyStateMayHaveChanged()`): call it
  from the existing event sites (screen push/pop, `CombatManager.TurnStarted`, `ActionExecutor`
  action start/finish, `Cmd.Wait` completion if P2 is on, and the 200 ms sampler transition to
  `ready`). The wait becomes
  `await signal.WaitAsync(min(remaining, nextFrame))` — bounded, still frame-paced, but a pure
  wait instead of a build.
* Keep a **full build only on entry and exit** of the loop (the caller needs the payload anyway),
  so per-turn build count drops from *hundreds/thousands* to ~2.
* Fallback contract: if the signal cannot be wired for a screen, fall back to a **throttled** build
  (e.g. every 4th frame / 66 ms) — still an 8–16x cut. Add a `state_build`-style counter
  (`actionable_probes`, `actionable_builds`) to `/health` so the win is measurable on the next live
  round.

### P4 — One build per turn: reuse the turn's snapshot (**impact ★★★★ / effort ★★★ / risk ★★**)

The turn already takes a raw snapshot in `AutoPlayLoopAsync` and a compact state in `PlayOnceAsync`.
Give `GameStateService` a **frame generation counter** and let `BuildDecisionSnapshotPayload` /
`GetCompactStateJsonAsync` reuse a payload built in the same generation:

* Sketch: `GameStateCache` (Godot-free, offline-testable) holding `(generation, payload)`; every
  game-thread mutation entry point the mod already owns bumps `generation`; a reader within the same
  generation gets the cached object. Correctness stays: nothing is served across a generation.
* Add a `?view=` / `fields=` projection to `/state` (additive; old shape unchanged) so MCP clients
  can ask for the digest only — the SSE path then stops building the world at 8.3 Hz
  (see P5). The `docs/api.md` + `api-facts` gate requirement applies.

### P5 — Adaptive, change-aware SSE (**impact ★★★ / effort ★★ / risk ★**)

Three cheap changes to `GameEventService`/`EventPollingCoordinator`:

1. **Idle backoff**: no `screen_changed`/turn change/action change for N polls → interval
   `120 → 250 → 500 ms`; snap back to 120 ms on any game-thread signal (P3's signal is exactly this
   hook). A stationary `MAIN_MENU` costs 1/4 as much.
2. **Digest-only polls**: build a *deliberately reduced* payload (run id, screen, phase, turn,
   action names, map/reward/event counts) instead of the full payload; escalate to a full build only
   when the digest changes. The digest is exactly `StateDigest`'s field set — factor that out so one
   definition serves both.
3. **Faster disconnect detection**: shorten the heartbeat (or count a write failure as a strike
   immediately) so a closed tab stops the poll loop in ~5 s instead of ~33 s.

### P6 — Overlay: render on change, tick only what must move (**impact ★★★ / effort ★★ / risk ★**)

* Subscribe `RefreshDynamic` to a runtime `Revision` counter (the runtime already has
  `RaiseChanged`), and let the 800 ms tick do **only** the elapsed-time label that must animate.
* `RefreshChatLog` → skip when `_history` revision and `LiveThought.Revision` are unchanged (saves
  82 string allocations per tick).
* When the panel is hidden, drop the tick entirely (currently only the body is gated).
* Bump the overflow walk from every 4th tick to on-demand (debug action) — it is a diagnostic, and it
  is the only O(visible-tree) work in the tick.

### P7 — Speed-aware waits: shrink the mod's own floors when turbo is on (**impact ★★★ / effort ★★ / risk ★★**)

Once the game runs at `Instant`, the mod becomes the bottleneck. Make the fixed floors a function of
the active speed, with the constants kept in a Godot-free policy so the offline suite can pin them:

* `CombatActionSnapshotStableDelay` 200 ms → `200 × factor` (e.g. 50 ms at turbo) — but never below
  one frame, and keep the "signature unchanged" requirement (it is a correctness check, not a timer).
* `TryResolveCardRewardAsync`'s 24-frame settle → wait for a **condition** ("card holders exist" /
  "option count > 0") with a frame floor of 2 and the same deadline; 24 frames was an
  "expose-for-a-few-frames" workaround (`GameActionService.Rewards.cs` comment), not a duration.
* `CardGridScrollSettleFrames = 12` → wait until the scroll offset stops moving, 2-frame floor.
* `AutoPlayRecovery` backoffs → replace `Task.Delay` with "wait for the state signal **or** the
  delay, whichever first" so a fast game does not pay a 2 s sleep for an animation that finished in
  200 ms.

### P8 — Move session persistence off the game thread (**impact ★★ / effort ★★★ / risk ★★**)

`ObserveSessionStateSnapshot` should hand an immutable `(runId, screen, phase, recordSnapshot)` to a
single-threaded writer task; the game-thread post returns immediately. This is the existing
project-map trap, and P3's signal work makes the boundary detection cheaper anyway.
Keep the invariant that only a **fresh raw game-thread frame** may switch/clear the session — the
decision stays on the game thread, only the IO moves.

### P9 — Make the win measurable and visible (**impact ★★★ / effort ★ / risk ★**)

Extend `/health.state_build` (additive) with `actionable_probes`, `actionable_builds`,
`event_poll_builds`, `turbo` (`{enabled, mode, original, restored_at}`) and an overlay reading of
"game speed: 极速 (Instant)". Without this the overhaul cannot be validated the way the 2026-09-18
round validated `state_build`, and regressions in a per-frame loop are invisible.

### Suggested order

`P1 → P3 → P9` (visible win, worst cost gone, measurable), then `P4 → P5 → P6 → P7`, then `P2` behind
a settings toggle, then `P8`.

---

## 6. Risks and unknowns

1. **Preference corruption (P1).** `NSettingsScreen.OnSubmenuClosed` and `NGame.Quit` call
   `SaveManager.SavePrefsFile()`, which would persist our in-memory `Instant`. Mitigations: never
   call it ourselves; restore on every exit path; `NGame._Ready` downgrades `Instant → Fast` at boot
   (so a leak degrades the player from `Normal` to `Fast`, not to `Instant`); optionally the
   `SavePrefsFile` prefix from §5/P1.
2. **`readonly` FastMode captures.** `NMapScreen._mapAnimStartDelay` / `_mapAnimDuration` are set in
   field initialisers, so a screen already open when turbo is enabled keeps old timings. Not a
   correctness issue, but "turbo didn't apply" reports will come from it.
3. **Co-op.** `FastMode` looks purely local (`Cmd.Wait` awaits a local `SceneTreeTimer`; the action
   queue is authoritative), so P1 *should* be co-op-safe — but SpeedX ships its combat-timer scaling
   **off by default for real multiplayer** and calls it a desync risk, and its
   `CombatState.GodotTimerTask` patch converts a 10 s network allowance into 1 s. **P2 must be
   single-player only, and P1 needs a real two-window co-op test before it defaults on.**
4. **Mod conflict / double scaling.** SpeedX is installed on this box and patches the same methods.
   Any Harmony we add must detect an existing prefix (`Harmony.GetPatchInfo`) and refuse; otherwise
   `0.1 × ours` compounds.
5. **Patch-target drift.** `Cmd.Wait` is overloaded, and `ReflectedMemberResolver.Method` uses
   `GetMethod(name, flags)` → `AmbiguousMatchException`; `ReflectedGameMembers` is keyed by
   `(Type, name)` and cannot express overloads or `ref` parameters. A `Cmd.Wait` patch therefore
   needs its own explicit resolution + a `Log.Warn` miss path (and its own `/health` line), or the
   registry needs an overload-aware overload of `Entry`. `CombatState.GodotTimerTask` *can* go
   through the registry (private static, single overload).
6. **`Instant` removes observation windows.** The reward/selection flows were tuned against
   `Normal`; with transitions skipped, a screen can appear and be dismissed between two frames, and
   the 24-frame settle assumption ("no holders for a few frames") may need re-measuring. Treat any
   new "pending" streak under turbo as a settle-policy bug, not a game bug — `UnsettledLimit = 5`
   will surface it as a visible stop.
7. **The 200 ms sampler is static and build-driven.** It only advances when something builds a
   payload. P3 (probe without build) must keep advancing it, or `snapshot_stabilizing` could latch
   forever — this is the single most likely bug in P3.
8. **`Engine.TimeScale` is a dead end** (private `DebugModifyTimescale`, clamp `0.1..4.0`, no
   persistence, debug-bound). Do not propose it as the turbo path; it also scales Godot physics and
   every tween, including the overlay's own.
9. **Unmeasured:** per-screen build cost distribution (only `MAIN_MENU` 113.8 ms is on record);
   `WaitUntilActionableAsync`'s real duty cycle (needs the P9 counters); whether `Instant` changes
   `NCardRewardSelectionScreen`/`NDeckSelection` expose timing; whether co-op peers tolerate one
   client on `Instant`.
10. **Test-surface cost.** New game-thread or policy files must be added to `docs/project-map.md`
    (two freshness tests), `SourceCoverageTests.KnownUncompiledSources` (game-dependent files), the
    per-file line budgets, and — if any pinned method body changes (`GameStateService` build,
    `GameActionService` handlers) — the relocation/predicate hash contract tests need their hashes
    regenerated with a documented reason.
