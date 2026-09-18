# Mod Architecture and Boundaries

## Runtime shape

The mod has one in-process C# runtime and one optional out-of-process Python sidecar. The C# project targets `net9.0`, enables nullable reference types, uses implicit usings, and references the game's `sts2`, `0Harmony`, and `GodotSharp` assemblies in [STS2AIAgent.csproj](../../../STS2AIAgent/STS2AIAgent.csproj). Keep new C# code compatible with nullable analysis instead of suppressing warnings or introducing nullable state that the caller cannot observe.

Startup is intentionally ordered in [ModEntry.Initialize](../../../STS2AIAgent/ModEntry.cs): capture the game-thread context, start event publication, start the loopback HTTP server, initialize `AgentRuntime`, then install the overlay unless this is a companion or headless instance. Shutdown releases resources through `AgentRuntime.Shutdown`, overlay uninstall, event stop, and HTTP stop. Code that needs startup state should use these existing lifecycle owners rather than constructing a second server, runtime, or overlay.

The local request path is:

```text
HttpServer.ListenLoopAsync
  -> Router.HandleAsync
      -> /state, /actions/available: GameThread.InvokeAsync(GameStateService...)
      -> /action: GameThread.InvokeAsync(GameActionService.ExecuteAsync)
      -> /mcp: NativeMcpServer.HandleHttpAsync
```

`HttpServer` only accepts loopback traffic and dispatches each context to `Router`; [Router.HandleAsync](../../../STS2AIAgent/Server/Router.cs) supplies request IDs, route selection, JSON/SSE responses, and the common exception envelope. Keep transport parsing and response formatting in this layer. Do not put route-specific JSON parsing into game state builders or action handlers.

## Game-thread ownership

The compact-state read in `GameBridge.GetCompactStateJsonAsync` is a concrete example:

```csharp
return GameThread.InvokeAsync(() =>
{
    var state = GameStateService.BuildStatePayload();
    return JsonSerializer.Serialize(state.agent_view ?? state, JsonOptions);
});
```

Follow the existing C# naming style: PascalCase methods and types, `_camelCase`
private fields. Wire payload members intentionally retain snake_case names such
as `agent_view`; do not rename them as a style-only cleanup. See
[GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) and
[GameStateService](../../../STS2AIAgent/Game/GameStateService.cs).

[GameThread](../../../STS2AIAgent/Game/GameThread.cs) captures `SynchronizationContext.Current` and the managed thread ID during initialization. Its `InvokeAsync` overloads execute inline when already on the game thread and otherwise post to the captured context, propagating results and exceptions through a `TaskCompletionSource`. Godot objects and game managers must only be inspected or mutated inside that boundary.

The boundary already exists at both external entry points:

- `Router.HandleAsync` wraps `/state`, `/actions/available`, `/data/*`, and `/action` calls in `GameThread.InvokeAsync`.
- [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) wraps its state, action, game-data, and screenshot operations in `GameThread.InvokeAsync` for agent and MCP callers.

An action handler reached through Router or GameBridge therefore runs inside the existing dispatch. Do not wrap the handler again just to satisfy a generic “game thread” rule. Inside a handler, use `GameThread.WaitForNextFrameAsync` for frame synchronization and preserve the current synchronization context. [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs) delegates its private frame helper to this method; the helper deliberately avoids `ConfigureAwait(false)` because resuming on a thread-pool thread would break Godot access.

`WaitForNextFrameAsync` is not an action timeout. When a valid Godot `SceneTree` exists, it waits for `ProcessFrame` but bounds the wait with a 50 ms `Task.Delay` because an occluded/background window may never emit the signal. When the game or tree is unavailable it falls back to a short delay. Each action transition still needs its own deadline, such as `WaitForPlayCardTransitionAsync(card, TimeSpan.FromSeconds(12))` in [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs). A loop that only waits for frames without a deadline is incomplete.

## State, action, and transport ownership

[GameStateService](../../../STS2AIAgent/Game/GameStateService.cs) is the read-side projection of native screen, combat, run, and multiplayer objects. It decides the canonical screen name, builds nested payloads, and derives available action names/descriptors. [GameActionService](../../../STS2AIAgent/Game/GameActionService.cs) owns mutations, native UI calls, argument validation, and transition stabilization. Keep these directions separate: state builders must not click buttons, and action handlers should return a fresh state rather than inventing a parallel state model.

`Router` exposes `/health`, `/state`, `/actions/available`, `/data/{collection}`, `/events/stream`, `/action`, and local session/companion controls. The `GameStateService` and `GameActionService` APIs are the stable seam for those routes. A new route is only warranted for a transport capability that cannot be represented by the existing endpoint; a new game action belongs in `/action` and its action switch.

Errors cross the HTTP boundary through [ApiException](../../../STS2AIAgent/Server/ApiException.cs). The exception carries `StatusCode`, machine-readable `Code`, optional `Details`, and `Retryable`; Router serializes those fields under `error` and adds `request_id`. Preserve this shape so the Python [Sts2ApiError](../../../mcp_server/src/sts2_mcp/client.py) can classify the same failure. Unexpected exceptions become a generic `500 internal_error` at the Router boundary; do not leak an unrelated ad-hoc JSON shape from a handler.

## Agent and MCP boundaries

[IGameBridge](../../../STS2AIAgent/Agent/IGameBridge.cs) is the pure agent-facing interface. It exposes compact/raw state, available actions, screen and game-data queries, action execution, bounded actionable waits, and optional screenshots. [AgentLoop](../../../STS2AIAgent/Agent/AgentLoop.cs) consumes that interface together with `ILlmClientFactory`; it does not need to know Godot types. [GameBridge](../../../STS2AIAgent/Agent/GameBridge.cs) is the production adapter that serializes `GameStateService` results and invokes `GameActionService` on the game thread.

The native MCP server is an in-process JSON-RPC/HTTP implementation. [NativeMcpServer](../../../STS2AIAgent/Server/NativeMcpServer.cs) receives `/mcp` traffic from Router, lists `AgentTools.Mcp`, calls the bridge, and implements local origin/session policy. Its tools are aligned with the C# tool definitions in [AgentTools](../../../STS2AIAgent/Agent/AgentTools.cs). Keep native MCP behavior inside these C# classes.

The Python sidecar is a separate implementation. [Sts2Client](../../../mcp_server/src/sts2_mcp/client.py) speaks the local HTTP API and maps transport/API failures to `Sts2ApiError`; [create_server](../../../mcp_server/src/sts2_mcp/server.py) registers FastMCP tools and profile-specific surfaces; [network_server.py](../../../mcp_server/src/sts2_mcp/network_server.py) owns the sidecar's HTTP transport options. Do not make the C# native server import Python behavior or make the Python client depend on C# internals. When the common guided tool contract changes, update both implementations and verify [test_native_tool_alignment.py](../../../mcp_server/tests/test_native_tool_alignment.py), which intentionally permits Python's `wait_for_event` convenience tool in addition to the native surface.

## Cross-layer change rule

For a change crossing state, action, agent, UI, or MCP, trace it in both directions before editing. A useful local example is the `play_card` path: `GameStateService` advertises it, `AgentTools` describes its indexes, `AgentLoop` validates against the latest state and delegates through `IGameBridge`, `GameBridge` calls `GameActionService`, and the action handler returns `status`, `stable`, `message`, and a fresh state. Contract tests in [AgentLoopTests](../../../STS2AIAgent.Tests/AgentLoopTests.cs), [McpServiceTests](../../../STS2AIAgent.Tests/McpServiceTests.cs), and [CombatDiagnosticsContractTests](../../../STS2AIAgent.Tests/CombatDiagnosticsContractTests.cs) show the expected seams.

## Code shape and its known debts

Measured 2026-09-18 across 92 mod source files totalling 32,312 lines (git-tracked only, which is what the gate counts -- a working tree also holds whatever the developer left in it). These numbers are here
because nobody was counting, and that is how a codebase stops being navigable -- not through a bad
commit, but through a thousand good ones. The `arch-facts` gate checks this table against the
files, so it cannot quietly go stale the way it did between ADR 0001 and the splits below.

| File | Lines |
| --- | ---: |
| [GameStateService.cs](../../../STS2AIAgent/Game/GameStateService.cs) | 5,872 |
| [AgentOverlayHost.cs](../../../STS2AIAgent/Ui/AgentOverlayHost.cs) | 1,829 |
| [AgentRuntime.cs](../../../STS2AIAgent/Agent/AgentRuntime.cs) | 1,377 |
| [GameStateService.AgentView.cs](../../../STS2AIAgent/Game/GameStateService.AgentView.cs) | 1,347 |
| [GameStateService.Payloads.cs](../../../STS2AIAgent/Game/GameStateService.Payloads.cs) | 1,251 |
| [GameActionService.cs](../../../STS2AIAgent/Game/GameActionService.cs) | 1,203 |
| [GameActionService.Rooms.cs](../../../STS2AIAgent/Game/GameActionService.Rooms.cs) | 1,150 |

Until 2026-09-17 two files held 49% of the mod: `GameStateService.cs` at 8,559 lines and
`GameActionService.cs` at 7,008. The largest file today is 18% of the mod, the second largest 6%,
and nothing else reaches 1,500 lines.

`SourceShapeContractTests` caps every file -- 1,000 lines unless it has a named budget -- and budgets
go down, never up. Needing more room than a budget allows is the signal to move something out, not
to raise the number. Its second test fails a budget that has drifted far above the file it guards,
so a file that shrinks drags its own ceiling down with it.

### What was split, and what the splits were not

Both monoliths came apart on 2026-09-17, and neither split was a judgement call about where lines
belong. In each case a member joined a group only when **every reference to it came from inside that
group**; anything two groups both reached stayed in the base file. That is a computation, and it is
reproducible.

- `GameActionService.cs` (7,061 lines) is now the base file plus eight partials -- combat, rewards,
  rooms, shop, menus, embark, run, co-op. 5,697 of 6,756 member lines landed in exactly one room;
  1,059 were genuinely shared. Six of the eight came in under the default budget.
- `GameStateService.cs` (8,295 lines) gave up two concerns that shared nothing with the raw builders
  except their output: the compact `agent_view` rewrite (`BuildAgent*`, plus the formatters and
  glossary only it reaches) and the 60 payload type declarations.

What is left in `GameStateService.cs` is one concern in 286 members: the raw `/state` payload
builders and the predicates they read. It is still the largest file in the mod, and the next split,
if someone wants one, is the `Can*` / `Is*` predicate layer.

A pure relocation is verifiable, and both were verified the same way: the base file's diff carries
exactly one genuinely new line (the `partial` keyword), and every removed non-blank line appears
verbatim in exactly one new file.

### The one thing the compiler cannot check for us

This project references the game's `sts2.dll`, so almost everything it touches is compile-checked:
rename a public game type or member and the build turns red before anyone runs anything.

**Twenty-seven private game members are the exception.** They are found by name at runtime, and they
fail in the opposite way -- quietly. `GetField` returns null, the call site falls back to a default,
and an agent is handed `max_players: 0` with no way to tell that from a lobby that really holds
nobody. Slay the Spire 2 is in early access; a patch renames a private field and the mod keeps
answering, confidently, with numbers it invented.

`ReflectedGameMembers` closes that. Every one of those members is resolved once when the mod loads,
the misses are written to the game log and named on `GET /health` under `compatibility`, and
`status` is derived from the result rather than the literal `"ready"` it used to be. The declaring
types are `typeof` expressions, so the compiler still checks them; only the member names -- the part
that actually rots -- are text.

**It is also the only place those members are looked up**, and that is the part that matters. The
first version resolved its own copy of each member while the call sites resolved theirs, with their
own binding flags, so the probe could report `ready` while a reader was broken: reintroducing the
`_longPressDuration` bug with a correct registry left every offline test and gate green. Call sites
now ask `ReflectedGameMembers.Field` / `Method` / `Property` for the member, so what the probe
reports is what the mod actually gets.

Duck-typed probing is a separate, sanctioned channel: `TryGetMemberValue` tries lists of candidate
names across object shapes (`"Cards", "CardModels", "Entries", "List"`) and accepts whichever
exists, so no one name in it is a dependency. `ReflectedMembers.*` names that channel and the few
public members read by name across types, so neither is invisible any more.

Making the registry the only lookup also exposed four reflective reads that had never resolved in the
installed game: `continue_game_over` tried `OnContinueButtonPressed`, `OnPressed` and
`OnContinueButtonPressedAsync` on a button (none exists on one), and the game-over overlay tried
`SetWaitingForOtherPlayersOverlayVisible` and `HideWaitingForPlayersScreen` on `NGameOverScreen`
(they live on `NCombatRoom` and `NRewardsScreen`). Each sat before a path that did the real work, so
nothing broke; the dead halves are gone. The fourth, `MonsterModel.MoveNames`, was not harmless:
the game removed that property, so every monster in `GET /data/monsters` exported `moves: []`. It
had only ever wrapped a public localization query (`LocTable.GetLocStringsWithPrefix`), so the
export now calls that directly -- no reflection at all, and a future rename breaks the build instead
of emptying the export.

That is the general move worth remembering: before registering a member, check whether the thing it
wraps is public. A compile-checked call beats the best-guarded reflection.

**A name in a variable is a name the scan cannot see.** Four private methods --
`NMultiplayerTest.StartHost`, `ReadyButtonPressed`, `Disconnect` and `NPauseMenu.CloseToMenu` --
went unprobed for as long as the registry existed, because they reached `GetMethod` through
`InvokePrivateTask` / `InvokePrivateVoid`, helpers that took the method name as a parameter. The
contract looked for literals passed to `GetMethod`, and these never were. They are registered now,
the helpers are gone, and `ReflectedMembers.NoNameTakingHelpers` fails on any lookup by a variable
name outside the few helpers `NameTakingChannels` argues for by name.

**Godot has its own by-name entry points, and they rot the same way.** `Node.Call("Name")`,
`EmitSignal("name")` and `Set("name", ...)` are as unchecked as `GetMethod("Name")`. Each has a
compile-checked form -- the generated `MethodName` / `SignalName` / `PropertyName` constant of the
node's own type, or a direct call when the method is public -- and `ReflectedMembers.NoGodotCallByString`
requires it. The sweep found `confirm_bundle` calling `OnConfirmPressed`, which the installed game does
not declare, on every use.

**The game's buttons are not Godot buttons.** `NButton` derives from `NClickableControl` -> `Control`,
not from `BaseButton`: it has no `pressed` signal and no `disabled` property. Emitting
`BaseButton.SignalName.Pressed` on one compiles -- the constant names `BaseButton`, not the button's
own type -- and does nothing. `choose_capstone_option` did only that; `continue_game_over` did that and
`Set("disabled", false)` before a `ForceClick` that was the only step that worked. Game buttons are
clicked with `ForceClick()`, and `ReflectedMembers.GameButtonsAreClicked` holds that.

`ReflectedMembers.*` keeps the registry honest: a new reflection site with no entry fails, and an
entry nothing reads fails too. Two names are deliberately not probed and say why -- the card-grid
`_prefs` / `_selectedCards` are declared per concrete screen, so a probe would need a subclass list
and would raise a false alarm the day the game adds or drops one.

### `AgentOverlayHost.cs` is not going to be split, and that is a decision

It is 1,829 lines and the table above names it, which makes it look like an oversight. It is not.
The two files that came apart could come apart because their parts did not share mutable state:
`GameActionService`'s sixty handlers touch the game, not each other.

This one was measured rather than guessed. Of its **85 instance fields, 78 are touched by more
than one method**, 2.6 methods each on average; only 7 belong to a single method, and `_panel`
alone is touched by twelve. That is one stateful Godot object, not several concerns sharing a
file. Splitting it into partials would scatter shared mutable state across files and make every
one of those 78 fields harder to reason about -- worse to read, and easier to break, in exchange
for smaller files.

If it is ever worth changing, the move is to extract a *tab* into its own class with its own
state and a narrow interface to the host -- which is design work with a behaviour risk, not a
mechanical split. Do not do it to satisfy a line count.

### What the splits broke, which is the part worth remembering

Moving members between the files of one class is invisible to the compiler and **not** invisible to
tooling that reads source text -- which, in this repo, is most of the verification:

- `AgentSourceFixture.MethodBody` resolved a name by its last occurrence. That is correct only while
  a method is declared before it is called, which holds inside one file and fails across a partial
  class. It failed *quietly*: the body of whatever enclosed a call site came back and the contract
  kept asserting, against the wrong method. It now prefers a declaration.
- `GameTaskBoundingContractTests` scanned a hard-coded list of files for unbounded awaits. Left
  alone it would have kept scanning the base file only, so the first bare `await` written in a room
  file would have wedged a request with nothing red to show for it. It enumerates now.
- The gate self-test reported PASS for three cases that were failing with "missing required file".
  `Assert-Case` only checked for a non-zero exit, so any failure looked like the failure the case was
  written to provoke. Every case now declares the message it expects.

The lesson generalises past this refactor: **a check that reads source by path carries a dependency
the compiler will not enforce.** When a file moves, go looking for the checks that named it.

### Where new code goes

- A new **action**: handler and stabilizer in the `GameActionService` partial for its room, following
  the existing `Execute*` / `WaitFor*` pair, and its name in the dispatch switch in
  `GameActionService.cs`. Its availability goes in `EnumerateAvailableActions` -- **one place**,
  since [ADR 0001](../../../docs/adr/0001-single-action-surface.md) collapsed the two action
  surfaces; `ActionSurface.*` fails if either surface starts deciding for itself again.
- A new **state field**: the payload record in `GameStateService.Payloads.cs`, its builder in
  `GameStateService.cs`, the compact projection in the matching `BuildAgent*` method in
  `GameStateService.AgentView.cs`, and a row in `docs/api.md` -- the `api-facts` gate refuses a
  field that no table names.
- Anything **not** state-building or action-executing: a new file. Every file in the table above is
  already past the point where adding to it is free, and the budgets say so out loud.
