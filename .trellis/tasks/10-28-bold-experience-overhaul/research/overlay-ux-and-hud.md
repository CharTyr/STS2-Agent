# Overlay UX audit + in-world HUD feasibility

> Read-only research for `.trellis/tasks/10-28-bold-experience-overhaul`.
> Baseline: **v0.17.0**, branch `dev`. Every claim below cites a file + symbol + the literal string,
> so a later implementer can verify without re-discovering.
>
> Scope read: `STS2AIAgent/Ui/*` (18 files), `Config/QuickSetup.cs`, `Config/FirstRunSetup.cs`,
> `Config/SessionBudgetLimits.cs`, `Agent/PlayerFacingSession.cs`, `Agent/PlayerLabels.cs`,
> `Agent/PlayPhases.cs`, `Agent/AgentRuntime.Status.cs` / `.Accounting.cs`,
> `Localization/Loc.cs` + `Loc.Strings.*.cs`, `Vision/ScreenshotService.cs`,
> `STS2AIAgent.Tests/Overlay*ContractTests.cs`, `docs/project-map.md` §3,
> `.trellis/spec/mod/agent-and-ui.md`, and the decompiled game sources under `extraction/decompiled/`.

---

## 1. Current experience walkthrough

### 1.0 Before the overlay exists (the part the mod cannot see, but owns the churn)

`steam-workshop/description.zh-CN.txt` asks for **7 ordered steps and two full game restarts**:
subscribe → `带 Mod 启动 / Play with Mods` → accept the untrusted-code prompt → *fully quit and
relaunch* → open the Mods menu → enable **STS2 AI Agent** → *restart again* → `F8`.
`README.zh-CN.md` §第 1 步 compresses this to 3 steps and adds the duplicate-install warning
(「如果已经订阅工坊，又手动拷过 GitHub 文件…否则可能看到两份 Mod」).

A non-technical player who skips the second restart, or who double-clicks the exe, sees **no mod at
all** and has no in-product signal that anything is wrong. Nothing in the overlay can help here, but
it is where the funnel leaks hardest.

### 1.1 In-game: launch → first card (step by step, with the real strings)

| # | What the player must do | Control | Cost | What can go wrong (evidence) |
|---|---|---|---|---|
| 1 | Launch with Mods, wait for the mod to attach | — | — | `AgentOverlayHost.TryBuildOrRetry` polls `NGame.Instance` and gives up after 180 deferred attempts (`Log.Warn(... "NGame never became ready; overlay unavailable this session")`). Nothing is shown to the player if it happens. |
| 2 | Notice the panel | auto-opened **only on the very first launch** (`_panel.Visible = startupSettings.OverlayVisibleOnStart \|\| !startupSettings.HasSeenFirstRunGuide;`, `AgentOverlayHost.Build`), otherwise the `F8` hotkey or the 34×64 px `AI` edge tab (`UiFactory.Button("AI", … ButtonKind.Floating)`) | 0–1 click | **See P1.** `OverlayVisibleOnStart` defaults `false` (`AgentSettings.cs:41`) and `HasSeenFirstRunGuide` is flipped by *any* panel toggle (`AgentRuntime.PersistOverlayVisible`) **and** by a passing model test (`AgentRuntime.ModelTest.cs:152`). So the second launch after a successful setup opens with **no panel**, only the edge tab. |
| 3 | Read the settings page and find 「快速配置」 | `HelpCard` titled `Loc.T("快速配置")` on the settings tab | 0 | The card sits inside a long settings form with a jump bar (`SettingsSections()`, 4+ sections). First-run lands on Settings but the jump bar already lists everything (模型 / 快速判断 / 高级 / MCP). |
| 4 | Pick 服务商 | `OptionButton` `_quickProvider`, 10 presets in `QuickSetup.Providers` (DeepSeek, 智谱 GLM / Zhipu, Kimi / Moonshot, 通义千问 / Qwen, OpenRouter, OpenAI API, 硅基流动 / SiliconFlow, Ollama, LM Studio, 自定义 / Custom) | 1 click | Local runtimes (`Ollama`, `LM Studio`) suggest **no** model (`Array.Empty<string>()`), so the 模型 field stays empty and the next step silently requires typing a name the player must know. |
| 5 | Paste API Key | `_quickKey = UiFactory.Line(..., "API Key", secret: true)`; 「获取 Key」 ghost button `OS.ShellOpen(provider.KeyUrl)` | 1 paste | `通义千问 / Qwen` and both local runtimes have `KeyUrl = ""`, so the button is hidden (`_quickKeyLink.Visible = preset.KeyUrl.Length > 0`) — a Qwen player gets no hint where a key comes from. |
| 6 | Confirm / edit 模型 | `_quickModel` pre-filled per provider; `_quickModelPicker` shows the provider's other suggestions (`Visible = preset.Models.Length > 1`) | 0–1 | Presets are hand-maintained and go stale (`QuickSetup.cs` remark: "providers rename models faster than this list can follow"). A stale default fails at step 7 with a raw HTTP error. |
| 7 | Confirm 请求上限 | `_quickBudget`, placeholder `Loc.T("不限（留空或0）")`, default 1500 for new installs | 0 | A non-numeric value throws `ArgumentException` out of `HarvestQuickSetup` → the save is refused; the reason lands in the settings footer only (see P6). |
| 8 | Press 保存设置 | footer button; `SaveSettingsFromFooter` → `SaveSettingsFromUi()` → **auto-tests** when the quick card is dirty (`retest = !_showAdvancedValue && _quickDirty`) | 1 click | If the save fails the page stays (by design, `ShowTab` refuses to leave Settings while `_settingsDirty`), but the player who came from 「去设置」 sees no obvious "you are stuck here" affordance. |
| 9 | Wait up to 45 s for the test | `SetSaveStatus(Loc.T("正在测试…（最多 45 秒）"))`, button becomes 「取消测试」 | 0 | The verdict line under the card only says 「已修改，需重新测试」 / 「未测试」 / 「已验证（HH:mm）」 / 「测试失败（HH:mm）：…」 / 「不支持工具调用，无法自动游玩（HH:mm）」 (`QuickTestBadge`, `ModelTestBadge`). Tool-calling failure is the single most common wall and the copy never says *which* model to try instead. |
| 10 | Go to Play | 「去游玩」 footer button appears only after a **completed** passing test (`_offerGoPlay = completed && …ReadyToInvite`) | 1 click | A passing test that the player cancels at the end never offers the jump. |
| 11 | Understand the readiness banner | `BuildReadinessBanner` + `FirstRunSetup.PlayBlocker`: 「还没配置模型」/「接口地址无效」/「模型测试没通过」/「模型不支持工具调用」/「模型还没测试」 + a 「去设置」 primary button | 0 | The banner names the *class* of blocker, never the field. "模型测试没通过" gives no reason (the reason lives in the settings footer, `SetSaveStatus`). |
| 12 | Have a run to play | nothing on the Play page says this | — | `StartAutoPlay` does **not** require an in-run screen (`AgentRuntime.StartAutoPlay` only gates on `FirstRunSetup.Evaluate` + mode + `_modeSwitching`), so the loop *will* start from the main menu and drive it. But the README tells the player to start a run manually first (「开始一局正常的单人游戏」), and the UI never says either way. **See P4.** |
| 13 | Press 开始自动游玩 | `_playToggle`, label flips to 「暂停」 / 「继续游玩」 | 1 click | Disabled while `!PlayRunning && !_playReady` (`RefreshModeControls`) — with a blocker present the button is dead and only the banner explains why. |
| 14 | Watch the first card | hero status `_playStatus = Loc.T("状态：-")` → `AgentRuntime.StatusWithElapsed` → 「已执行 play_card」 | 0 | **See P2** — the first thing the hero line ever says after a card is a raw wire identifier. |

**Counted cost for a returning player who already configured once:** launch → find the AI edge tab
(1) → 开始自动游玩 (1). For a brand-new player: ~4 clicks + 1 paste + up to 45 s, **plus two game
restarts before the overlay is reachable at all**, and 6 distinct places to get stuck (steps 2, 5, 6,
8, 9, 12).

### 1.2 What the player sees while the AI plays

Solo page, top to bottom (`BuildPlayPage` → `BuildSoloSection`):

1. `SegmentedSwitch` 「单人」/「多人」.
2. `_modeSwitchStatus` (usually blank).
3. `_readinessBanner` — hidden once verified.
4. `HelpCard("play.auto", "自动游玩", …)` containing:
   - `_playStatus` — the hero line, `StatusWithElapsed`: 「正在读取游戏状态…」, 「正在等待游戏可操作…」,
     「正在请求快速判断…」, 「正在请求模型…」, 「正在执行动作…」, 「已执行 {0}」,
     each with 「（已等待 {0} 秒）」 appended after 2 s. Green while playing.
   - `_soloNext` — only when playback *stopped for a reason* (「下一步：{0}」).
   - `_playSummary` — `Loc.T("{0} · 最近动作：{1}", PlayerLabels.Screen(screen), Trim(PlayerLabels.Action(LastAction), 48))`,
     e.g. 「战斗 · 最近动作：出牌」.
   - `_playUsage` — `PlayerFacingSession.FormatUsage`: 「{0} Token · {0} 次请求」 (total only; no
     prompt/completion split, unlike README §核心玩法 1 which claims 「Prompt Tokens、Completion
     Tokens、总 Token 数以及请求次数」).
   - Buttons 开始自动游玩 / 暂停 / 继续游玩, 单步, 清零计数.
5. Checkbox 「用快速判断出牌（LLM 只做规划）」 (hidden unless Jev configured).
6. `HelpCard("play.chat", "对话", …)`: empty state 「可以问：这手牌怎么打、遗物值不值得拿。」, the
   `RichTextLabel` log (turns labelled 「你」/「助手」/「动作」/「思考」/「思考中」), switches
   「附带局面」「附带截图」「显示思考」, combos 「回复语言」「思考强度」, 输入消息… + 发送/清空.
7. 「战斗中不自动操作」 card.
8. `HelpCard("play.jev", "快速判断", …)`: 「最近选择」「概率」「危险度」「耗时」 tiles +
   `FormatJevRate` 「快速判断 {0} 次 · LLM 接手 {1} 次」. Hidden unless dual-layer is on.
9. `HelpCard("play.decisions", "决策记录", …)`: 「本次会话」/「本局」 tiles, then the log **collapsed
   behind 「展开记录」** (`_decisionLogOpen` defaults false), then 清零计数 / 复制诊断 / 保存诊断.

**Is it clear WHAT the AI does?** Yes, at a one-line, after-the-fact level: the summary line names the
screen and a translated last action. **Is it clear WHY?** Only if the player opens the decision log
(one extra click, and the log is a dense `RichTextLabel` of `理由：…` + `来源：…` + `Token N`), and only
when the model actually supplied a reason — `DecisionLogEntry.reason` is nullable and `PlayWithModel`
does not require it. **Is there a plan?** No. `PlayStrategy` (goal / posture / instructions) is
plumbed to Jev and exposed over `POST /strategy` and MCP, but **nothing in `Ui/*` renders it** — grep
for 「策略」/「规划」 in `STS2AIAgent/Ui` returns only two checkbox/help strings about Jev. **Is there a
next intended move?** No: intent exists only after the fact (`Acted`). **Run progress?** Only
`RunSpend` (「{1} 次决策 · {2} Token」) — no floor, no act, no boss, no HP. **End-of-run summary?**
None: `PlayerFacingSession` collapses the whole run end into 「对局已结束」 / 「新开一局后，再继续或重新
邀请。」.

**Overload?** Not visually — the page is calm, arguably too calm. The overload is *cognitive on the
settings page* (jump bar + quick card + advanced card + Jev card + MCP card in a 440 px column) and
*absence on the play page* (a player watching a 20-minute combat gets one 12-px caption line).

### 1.3 Confusing terminology (verbatim inventory)

| Where | String | Why it confuses a non-technical player |
|---|---|---|
| Hero status | 「已执行 play_card」 (`AgentRuntime.Accounting.ApplyPlayResult`) | Raw API identifier in the biggest, greenest line on the page. The line *below* it says 「最近动作：出牌」 — the same fact in two languages, one of them not Chinese. |
| Decision log (Godot-free path) | `DecisionLogView.FormatLine` → `$"{entry.id}. {entry.action}"`, `Loc.T("来源：{0}", entry.source)` | Not reachable from the live UI any more (`RefreshDecisionPage` renders its own lines through `PlayerLabels`), but it is still compiled and still asserted by `OverlayTabContractTests` (`Assert.Contains("1. play_card", …)`, `… $"{RecentLimit + 10}. play_card"`). Two formatters, one of them wrong. |
| Play page | 「用快速判断出牌（LLM 只做规划）」 | "快速判断" = a second, cheaper model; "LLM" = the model the player already configured; "只做规划" means nothing to someone who has never seen a plan object. |
| Settings | 「快速判断」 card, 「思考参数格式」「思考强度」「思考协议」 | Three different "thinking" knobs, none of which changes what the player sees on screen. |
| Budget | 「请求上限」 vs 「会话预算」 vs 「清零计数」 | 1500 requests is meaningless without "≈ 1 run" (the *help text* says 「新安装默认 1500 次，大约够打完一整局」; the field itself does not). |
| Actions | 「单步」 | Reads as a debug control. (It is one.) |
| Docs vs UI | README §3 「切换到「对话」页面」; workshop 「所有事都在游戏内一个窗口里办：「游玩」和「设置」两个页签；游玩里有单人/多人、对话和决策记录」 | The 对话 tab was removed; the conversation is a card. A returning player hunts for a tab that does not exist. |
| Docs vs UI | README 「实时显示…Prompt Tokens、Completion Tokens、总 Token 数以及请求次数」 | UI shows total + request count only. |

---

## 2. Pain points, ranked

Ranked by *(players affected) × (how badly it blocks)*, not by effort.

### P1 — After the first successful setup, the window disappears on the next launch
`AgentOverlayHost.Build`: `_panel.Visible = startupSettings.OverlayVisibleOnStart || !startupSettings.HasSeenFirstRunGuide;`
`AgentSettings.cs:41` `public bool OverlayVisibleOnStart { get; set; }` (default `false`),
`AgentSettings.cs:43` `HasSeenFirstRunGuide`, flipped by `AgentRuntime.PersistOverlayVisible`
(any panel toggle) and by `AgentRuntime.ModelTest.cs:152` `MarkFirstRunGuideSeen()` on the first
passing play-model test.
**Effect:** the player configures the model, plays, quits, relaunches → no panel. The only affordances
are `F8` (documented) and a 34×64 px `AI` tab pinned to the screen edge (`_edgeTab`, `ButtonKind.Floating`).
It is not a crash, but it is the #1 "the mod broke" report shape. **Fix is 3 lines** (see Q1).

### P2 — The hero status line speaks API
`AgentRuntime.Accounting.ApplyPlayResult`:
`Loc.T("已执行 {0}", result.Acted)` where `Acted` is the wire action name (`accepted = action` in
`AgentLoop.ExecuteActAsync` / `decision.Action`). English entry: `map["已执行 {0}"] = "Executed {0}"`.
Meanwhile `PlaySummaryText()` on the very next line does `PlayerLabels.Action(AgentRuntime.Instance.LastAction)`
→ 「出牌」. **The same fact is shown twice in two terminologies, and the wrong one is the hero.**
`PlayerLabels.Action` already covers 40+ actions; the fix is to call it.

### P3 — Nothing tells the player WHY, and the plan is invisible
- Reasons are optional (`DecisionLogEntry.reason` is `string?`; `CleanOptional(reason, 500)`).
- The only UI surface for them is inside 「展开记录」, collapsed by default
  (`_decisionLog = UiFactory.Rich(); … _decisionLog.Visible = _decisionLogOpen;`).
- `PlayStrategy` (goal/posture/instructions) never reaches `Ui/*`.
**Effect:** the player watches a card get played and cannot answer "why that card". For a
player-developer who wants to *learn* from the AI, this is the product's central miss.

### P4 — The Play page never says whether a run must already be running
`BuildReadinessBanner` only covers model readiness. `StartAutoPlay` does not check the screen. The
README says 「开始一局正常的单人游戏」 first; the workshop description says 「切到「游玩」页点开始」.
Two documents, two behaviours, and the UI says nothing. A player on the main menu who presses
开始自动游玩 gets `Status` 「自动游玩中」 and then 「正在等待游戏可操作…」 with no explanation of what
the game is waiting for.

### P5 — End of run is a dead end
`PlayerFacingSession.Compose`: `StopKind == "run_end"` → headline 「对局已结束」, next action
「新开一局后，再继续或重新邀请。」. No floor, no victory/defeat, no cause, no cost. The data is
already there (`GameOverPayload.is_victory` / `.floor` / `.character_id`, `RunManager.Instance.History`
→ `RunHistory.KilledByEncounter` / `KilledByEvent`, `AgentRuntime.CurrentRunSpend()`), and the mod
even has a `game_over` payload with a `summary_ready` phase — but the overlay shows none of it.

### P6 — Failure reasons are one screen away from the failure
`FirstRunSetup.PlayBlocker` returns class-level text (`「模型测试没通过」`), while the actual error is
in `ModelTestBadge` on the settings page or in `SetSaveStatus`. The readiness banner's only action is
「去设置」, which lands at the top of a long form. `_quickTestBadge` shows the reason, but only inside
快速配置.

### P7 — No feedback for "it is waiting" vs "it is stuck"
`StatusWithElapsed` adds 「（已等待 {0} 秒）」 after 2 s — good. But there is no second-stage hint at
30/60 s, and the three most common stalls (main menu with no run; a modal the loop cannot dismiss;
teammate window gone) all read as the same 「正在等待游戏可操作…」.

### P8 — Two decision-log formatters disagree
`DecisionLogView.FormatLine` renders `play_card` + raw `entry.source`; `AgentOverlayHost.Session.RefreshDecisionPage`
renders `PlayerLabels.Action(entry.action)` + `PlayerLabels.Source(entry.source)` + bold id. v0.17.0
shipped the second one (「决策记录的动作和来源也换成可读的名字」). The first is dead-ish but still
compiled, still exported (`DecisionLogView.RecentLimit` is used), and still pinned by tests that assert
the *raw* form — so the next person who reuses it silently regresses the fix.

### P9 — Model/test vocabulary is unbounded
「不支持工具调用，无法自动游玩（HH:mm）」 is the wall most players hit with chat-only models, and the
copy offers no next step. `ModelRoleProbe.FormatLine` + `FirstRunSetup.PlayBlocker` never suggest a
concrete alternative (DeepSeek / GLM / GPT-5-mini all pass tool calling; the preset list already knows
this).

### P10 — Watching is thin, not busy
`ChatTailLimit = 80`, `DecisionLogView.RecentLimit = 50`, the log collapsed, two caption lines,
no run context. Nothing on screen scales up for "I am sitting back watching" — no large type, no
persistent "AI is playing" indicator outside the 440 px window (the edge tab only changes colour and
tooltip: `var tone = playing ? UiFactory.Palette.Positive : …`).

### P11 — README / workshop drifted from the shipped UI
「对话」 tab, prompt/completion token split, 「测试主模型」 flow described as a separate manual step
(the quick card now auto-tests on save). A player following the workshop page hunts for controls that
moved.

### P12 — Copy is duplicated on one screen
`BuildCoopSection`'s help text 「你打自己的角色，AI 打另一个角色，同一座塔往上爬。…」 plus
`DualHintText()` 「在主菜单邀请，队友会自动选角并游玩。」 plus `FirstRunSetup.InviteHint`
(「主模式已验证。到「游玩」页点「开始自动游玩」…」) — three sentences about the same prerequisite.

---

## 3. In-world HUD feasibility

**Verdict: feasible, with two real constraints** — (a) the intent must be published *before* the
action is executed (today it is only published after), and (b) anything drawn on a `CanvasLayer`
lands inside vision screenshots unless explicitly hidden.

### 3.1 The exact game node types and members

All paths below are under `extraction/decompiled/`; ⚠ the decompiled tree may be older than the
installed game (v0.17.0 targets game `0.111.0`), so treat member *shapes* as the contract and verify
names against the installed `sts2.dll` before shipping.

#### Hand cards (combat)

| Type | File | Members that matter |
|---|---|---|
| `NPlayerHand : Control` | `MegaCrit.Sts2.Core.Nodes.Combat/NPlayerHand.cs` | `public Control CardHolderContainer { get; private set; }` (L250), `public IReadOnlyList<NHandCardHolder> ActiveHolders => Holders.Where(h => h.Visible).ToList();` (L298), `private IReadOnlyList<NHandCardHolder> Holders => CardHolderContainer.GetChildren().OfType<NHandCardHolder>().ToList();` (L300), `public void FlashPlayableHolders()` (L994) |
| `NHandCardHolder : NCardHolder : Control` | `MegaCrit.Sts2.Core.Nodes.Cards.Holders/NHandCardHolder.cs` | `public Vector2 TargetPosition { get; }`, `public void Flash()`, `public void SetIndexLabel(int i)`, `public void UpdateCard()`, `private NPlayerHand _hand` |
| `NCardHolder : Control` (abstract) | `…/Holders/NCardHolder.cs` | `public NClickableControl Hitbox { get; }` (L120), `public NCard? CardNode { get; protected set; }` (L122), `public virtual CardModel? CardModel => CardNode?.Model;` (L124) |
| `NCard : Control` | `MegaCrit.Sts2.Core.Nodes.Cards/NCard.cs` | `public CardModel? Model` (L355), `public event Action<CardModel?>? ModelChanged` (L381), `CardHighlight` (`NCardHighlight.playableColor` / `.gold` / `.red`, `AnimShow()` / `AnimHide()`) |

**Mapping `card_index` → holder** (the HUD's core lookup): the action path uses
`me.PlayerCombatState.Hand.Cards[index]` (`GameActionService.Combat.ExecutePlayCardAsync`, L331–348)
and plays via `card.TryManualPlay(target)` — *no UI node is involved*. So map by model identity:

```csharp
// on the game thread, inside the combat room
var hand = combatRoom.Ui.Hand;                       // NCombatRoom.Ui : NCombatUi, .Hand : NPlayerHand
var wanted = me.PlayerCombatState.Hand.Cards[i];     // CardModel
var holder = hand.ActiveHolders.FirstOrDefault(h => ReferenceEquals(h.CardModel, wanted));
var rect   = holder?.GetGlobalRect();                // Rect2, canvas space
```

⚠ `ActiveHolders` filters on `Visible`; during the deal/return animations a holder can be visible but
still lerping (`AnimPosition` lerps to `_targetPosition`). Read `GetGlobalRect()` *fresh each frame*
and it will follow the animation — which is what you want for a highlight ring.

#### Creatures (target of an arrow / speech bubble)

| Type | File | Members |
|---|---|---|
| `NCreature : Control` | `MegaCrit.Sts2.Core.Nodes.Combat/NCreature.cs` | `public Creature Entity { get; private set; }` (L195), `public Control Hitbox { get; private set; }` (L189), `public NCreatureVisuals Visuals`, `public Node2D Body`, `public Control IntentContainer`, `public bool IsInteractable`, `public bool IsFocused`, `public Vector2 GetTopOfHitbox()` (L879), `public Vector2 GetBottomOfHitbox()` (L884), `ShowSingleSelectReticle()` / `HideSingleSelectReticle()` (L906/L911) |
| `NCombatRoom : Control, IScreenContext` | `MegaCrit.Sts2.Core.Nodes.Rooms/NCombatRoom.cs` | `public static NCombatRoom? Instance => NRun.Instance?.CombatRoom;` (L155), `public IEnumerable<NCreature> CreatureNodes => _creatureNodes;` (L159), `public NCreature? GetCreatureNode(Creature? creature)` (L541 — `CreatureNodes.FirstOrDefault(c => c.Entity == creature)`), `public CombatRoomMode Mode`, `public NCombatUi Ui` |

`GetTopOfHitbox()` returns `Hitbox.GlobalPosition + (Hitbox.Size.X * 0.5f, 0)` — *exactly* the anchor a
thought/speech bubble wants (centred, above the creature).

**Do not** call `ShowSingleSelectReticle()` from the HUD: it toggles game-owned targeting state
(`_isInMultiselect`, `_selectionReticle`) and the loop's own `TryCancelRunningPlayerAction` /
`ArePlayerDrivenActionsSettled` logic reads game UI state. Draw our own ring on our own layer.

#### Map points

| Type | File | Members |
|---|---|---|
| `NMapScreen : Control` | `…/Screens.Map/NMapScreen.cs` | `public static NMapScreen? Instance => NRun.Instance?.GlobalUi.MapScreen;` (L331), `private readonly Dictionary<MapCoord, NMapPoint> _mapPointDictionary` (L277 — **private, don't reflect**), `public bool IsNodeOnScreen(NMapPoint)` (L1372), `public void HighlightPointType(MapPointType)` (L1289), `public void OnMapPointSelectedLocally(NMapPoint point)` (L633) |
| `NMapPoint : NButton` (abstract) | `…/Screens.Map/NMapPoint.cs` | `public MapPoint Point { get; protected set; }` (L129), `public MapPointState State`, `public NMultiplayerVoteContainer VoteContainer`, `protected bool IsTravelable` (L112), `public virtual void OnSelected()`, `protected sealed override void OnRelease()` |
| `MapPoint` | `MegaCrit.Sts2.Core.Map/MapPoint.cs` | `public MapCoord coord;` (L14), `public MapPointType PointType`, `public HashSet<MapPoint> Children` |
| `MapCoord` | `MegaCrit.Sts2.Core.Map/MapCoord.cs` | `new MapCoord(int col, int row)`; `NMapScreen` reads `coord.row` |

**The mod already has the lookup**: `GameStateService.GetAvailableMapNodes(screen, runState)` returns
`IReadOnlyList<NMapPoint>` (via `FindDescendants<NMapPoint>(mapScreen)` + `IsMapNodeClickable`), and
`GameActionService.Run.ExecuteChooseMapNodeAsync` resolves `availableNodes[request.option_index]` and
calls `selectedNode.ForceClick()`. So the HUD's "preview the chosen path" is the *same* index →
`NMapPoint` → `GetGlobalRect()`, available at the intent seam with zero new reflection.

⚠ The game only knows the *next* node (`MapPoint.Children` of the chosen point are the following
choices). A multi-step path preview would be a guess; preview **one hop** and label it as such.

#### Getting screen positions — the coordinate story

- `extraction/decompiled/` contains **no `CanvasLayer` at all** (grep: 0 hits), so all game UI lives on
  the root viewport's default canvas.
- The window is content-scaled: `NGame.ApplyDisplaySettings` sets `Window.ContentScaleAspect` /
  `Window.ContentScaleSize` (1920×1080, 1680×1260, 1920×1200, 2580×1080 depending on aspect).
- The overlay already lives in that same scaled space and treats `_host.Size` (FullRect) as the
  viewport size (`OverlayPlacement.ReadViewportSize`, `PanelSizeFor`).
- Therefore `Control.GetGlobalRect()` on a hand holder / `NCreature.Hitbox` / `NMapPoint` is in the
  **same coordinate space** a new `CanvasLayer` child uses. Precedent in-repo:
  `GameStateService.GetVisibleGridCardHolders` already sorts by `node.GlobalPosition.Y/.X`.

**Rule for the implementer:** one `GetGlobalRect()` per visible target per frame; never cache a
position across frames (hand re-layout, creature scaling `AdjustCreatureScaleForAspectRatio`, map
scroll `IsNodeOnScreen`), and re-validate with `GodotObject.IsInstanceValid(node) && node.IsInsideTree()`
before every read (the room can be freed between frames — the overlay already guards this way,
`WarnOnce` + `GodotObject.IsInstanceValid`).

### 3.2 How the mod already locates nodes (copy this, don't invent)

- `GameStateService.FindDescendants<T>(Node root)` — private recursive walk with
  `GodotObject.IsInstanceValid` on each node (`GameStateService.cs:1107–1130`). Used for
  `NMapPoint`, `NGridCardHolder`, `NCombatRoom`, `NClickableControl` families.
- `GameStateService.FindActiveCombatRoom(currentScreen)` — prefers the current screen, falls back to a
  `FindDescendants<NCombatRoom>` walk filtered by `IsVisibleInTree()` + `Mode == ActiveCombat`.
- `GameStateService.GetCombatHandSelection` / `GameActionService.Rewards` reach the hand via
  `NPlayerHand` + `NHandCardHolder` and call `NPlayerHand.MethodName.SelectCardInSimpleMode` etc.
- `ReflectedGameMembers.Field(typeof(NPlayerHand), "_prefs")` is the sanctioned way to read a private
  field (`ReflectedGameMembers.cs:80–81`), with a registry test (`ReflectedMemberRegistryTests`).
  **Prefer public members; if a private one is unavoidable, register it there — not ad-hoc reflection.**

### 3.3 How to draw on top safely

The existing host is the template (`AgentOverlayHost.Build`):

```csharp
_layer = new CanvasLayer {
    Name  = "STS2AIAgentOverlay",
    Layer = 128,
    ProcessMode = Node.ProcessModeEnum.Always          // keeps ticking while the game pauses
};
var host = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
host.Theme = OverlayGodotTheme.Build();
root.CallDeferred(Node.MethodName.AddChild, _layer);   // deferred: NGame is mid-build
```

For the HUD: a **second** `CanvasLayer`, `Name = "STS2AIAgentHud"`, `Layer = 120`
(below the panel at 128 so the window still wins when both are up, above the game), added from the
same deferred call site and torn down in `TearDown()`. Then:

- Every HUD control: `MouseFilter = Ignore` (a HUD that swallows a click makes the *game* feel broken;
  the panel deliberately uses `Stop`, the HUD must not).
- Draw with `ColorRect` / `Panel` / `Line2D`-free 2D primitives on a `Control` (`_Draw` override or
  cheap child nodes), colours from `UiFactory.Palette` (`Positive`, `Warning`, `Accent`, `Muted`) so
  the HUD matches the 8 themes — `UiFactory.UseTheme` is already called before the first control.
- Sizing: the HUD must scale with the content-scale size, so position from `GetGlobalRect()` in
  content units and size in content units (`* 1.0f`), never in physical pixels.
- Update cadence: the overlay already installs `_tree.ProcessFrame += OnProcessFrame` (game thread).
  Add a HUD pass there, throttled to ~20 Hz (`now - _lastHudMs > 50`), and **early-return unless
  `AgentRuntime.Instance.PlayRunning` or the HUD setting is on** so an idle session costs nothing.
- Nothing in the HUD may call `QueueFree`/`Reparent`/`AddChild` on a game node.

### 3.4 Screenshot path — should the HUD hide too? **Yes, by default.**

`ScreenshotService.CaptureJpeg` reads `NGame.Instance.GetViewport().GetTexture()` — the **whole root
viewport**, i.e. every `CanvasLayer`. The hide hook is:

```csharp
ScreenshotService.BeginCapture = HideForCapture;
ScreenshotService.EndCapture   = RestoreAfterCapture;
ScreenshotService.OverlayVisibleProbe = IsPanelVisible;   // "did the player have the window open"
```

`HideForCapture` today hides only `_panel` and `_edgeTab`. A HUD on layer 120 would be **burned into
the JPEG** that the vision role and 「附带截图」 chat read.

Recommendation:

1. `HideForCapture` sets the HUD layer's `Visible = false` unconditionally (not gated on the panel's
   visibility), and `RestoreAfterCapture` restores it to whatever the setting says.
2. Keep `OverlayVisibleProbe` meaning **the panel**, because `ScreenshotCapturePolicy` uses it to
   decide whether a hide/restore pair is needed at all (`GodotCaptureHost.IsOverlayVisible`); changing
   its meaning silently changes that policy.
3. Add a setting 「截图时保留 HUD」 (default off) for players who *want* the trace in the model's view.
   Do **not** default it on: the vision prompt is about the game, and a highlight ring over a card is
   a fact the text state already carries.
4. Note the two-frame settle (`FramesToSettleAfterHide = 2`) applies to the HUD too — it is on the same
   viewport, so hiding both in the same callback costs nothing extra.

### 3.5 The "AI is playing — press F8 to pause" banner: **the copy in the brief is wrong**

`F8` is `_hotkey` → `ToggleVisible()` — it shows/hides the **window**. It does not pause
(`OverlayHotkey` accepts F1–F12 or a key name; `OnProcessFrame` calls `ToggleVisible`). A banner saying
"F8 暂停" would teach a falsehood at the exact moment the player wants to intervene. Two honest options:

- **(a) zero new input plumbing:** banner text 「AI 正在出牌 · F8 打开控制台」 /
  `"AI is playing · F8 for the console"`.
- **(b) add a real pause hotkey** (e.g. `PauseHotkey = "F9"`, default off): one more persisted field,
  and the banner becomes 「AI 正在出牌 · F9 暂停」. This is the better product answer *if* the settings
  field is added properly (see the settings checklist in §4).

### 3.6 Run-end "summary card"

Data is already in the process; no new game access is needed:

| Fact | Source |
|---|---|
| victory / floor / character | `GameOverPayload { is_victory, floor, character_id, phase }` (`GameStateService.Payloads.cs:1113`), built by `GameStateService.Rooms.BuildGameOverPayload` from `RunManager.Instance.History.Win` and `runState.TotalFloor` |
| cause of death | `RunManager.Instance.History` → `RunHistory.KilledByEncounter` / `.KilledByEvent` (`ModelId`, `ModelId.none` when not applicable), `.WasAbandoned`, `.Ascension`, `.RunTime`, `.Acts`, `.MapPointHistory` |
| decisions / tokens | `AgentRuntime.CurrentRunSpend()` → `RunSpend(RunId, Decisions, Tokens, TokensKnown)`; per-decision rows from `DecisionLog.Snapshot` filtered by `run_id` |
| Jev vs LLM split | `PlayerFacingSession.FormatJevRate(jevAccepted, fallbacks)` |

Design: a **pure formatter** (`Agent/RunSummaryCard.cs`, Godot-free like `DecisionLogView` /
`PlayerFacingSession`) producing `IReadOnlyList<string>` lines, plus one `HelpCard` on the play page
shown when `facing.Kind == "run_ended"` **or** `screen == GAME_OVER`, plus a 「复制成绩单」 ghost button
(`DisplayServer.ClipboardSet`, same pattern as `CopyDiagnostics`). Rendering it in-world at
`GAME_OVER` is a *nice-to-have* second step (the game-over screen is itself a full-screen Control, so
z-order is the only risk).

### 3.7 Streamer / spectator mode

Two separable ideas; keep them as two settings or the naming will fight:

- **Streamer mode** (privacy): hide provider names, the 「获取 Key」 link, `_apiAddressLabel`
  (「本机 API：{0}」), `_mcpUrlLabel`, endpoint URLs, the diagnostics buttons, and collapse the
  decision log's 「Token N」 to a coarse band. Risk: the panel becomes less useful for support; it must
  be a *temporary, visibly-flagged* mode (a small band 「直播模式」).
- **Spectator mode** (readability): a large-format overlay — bigger type, current intent + last three
  decisions + run line (floor/act/HP), no settings access. This is the natural home for the "watching
  experience" that today is 12-px captions.

Both are UI-only and can reuse the same refresh pass; neither needs game nodes.

---

## 4. Ranked proposals

Legend: **[Q]** quick win (≤ ~1 day, low risk), **[B]** bold move (needs a design + contract-test pass).
Every proposal respects: Godot only on the game thread (`GameThread.InvokeAsync` for post-await
writes), `UiFactory` conventions (`Wrapped` not `Label` for anything that can hold a sentence; one
label+control per row at the 440 px panel width; `ClipText` off on naturally-sized buttons), `Loc.T("中文")`
with an English entry in `Loc.Strings.*.cs`, and the overlay contract tests
(`OverlayLayoutContractTests`, `OverlayTabContractTests`, `OverlayVisualContractTests`,
`OverlayChatStreamContractTests`) + `OverlayOverflowSentinel` (`[STS2AIAgent.Wide] … offenders=0`).

---

### 1. **[Q] Make the window come back, and make its absence loud** — fix P1

- **Where:** `STS2AIAgent/Config/AgentSettings.cs` (`OverlayVisibleOnStart` default), `AgentRuntime.PersistOverlayVisible`,
  `AgentOverlayHost.Build` (`_panel.Visible = …`), `AgentOverlayHost.Pages.RefreshHeader` (edge-tab tone).
- **Change:**
  1. `OverlayVisibleOnStart` default → `true` for a *fresh* file (`CreateDefault`), and
     `PersistOverlayVisible` stops writing `HasSeenFirstRunGuide` (the two facts are unrelated).
  2. Edge tab grows a warning dot when `!HasSeenFirstRunGuide` (unconfigured) — use
     `UiFactory.StatusPill`/`PillBadge` tones, **not** an emoji (visual contract forbids glyph tones).
  3. One-time line in the solo card until configured: 「窗口已收起，按 F8 或点右边的 AI 标签打开」
     / `"The window is hidden — press F8 or click the AI tab on the edge."`
- **Chinese/en:** above, plus `Loc.T("窗口已收起")` / `"Window hidden"`.
- **Why first:** smallest change, largest drop in "the mod broke" reports.

### 2. **[Q] Speak Chinese in the hero line** — fix P2 + P8

- **Where:** `Agent/AgentRuntime.Accounting.cs` `ApplyPlayResult`; `Ui/DecisionLogView.cs` `FormatLine`.
- **Change:** `Loc.T("已执行：{0}", PlayerLabels.Action(result.Acted))` (and add the card name when the
  receipt carries one later). In `DecisionLogView.FormatLine`, route `entry.action` through
  `PlayerLabels.Action` and `entry.source` through `PlayerLabels.Source`, then update the two assertions
  in `OverlayTabContractTests` (`"1. play_card"`, `$"{RecentLimit + 10}. play_card"`).
- **Zh/en:** `Loc.T("已执行：{0}")` → `"Done: {0}"`; `Loc.T("已执行 {0}")` entry can be retired.
- **Why:** one-line fix removes the single most jarring text on the page and closes a two-formatter split.

### 3. **[Q] Explain the stall** — fix P4 + P7

- **Where:** `Agent/AgentRuntime.Status.cs` (`StatusWithElapsed`), plus a new pure
  `Agent/StallHintPolicy.cs` (Godot-free, testable) consulted by
  `AgentOverlayHost.Pages.RefreshSoloSection`.
- **Change:** after N seconds in a phase, append a second sentence:
  - `ReadingState`/`WaitingForGame` + `screen == MAIN_MENU` → 「还没开始一局：点「开始自动游玩」后 AI 会
    自己选角进图，也可以你先开一局」 / `"No run yet — the AI can start one itself, or start one first."`
  - `WaitingForPlayer` → 「等你在自己窗口里操作」 / `"Waiting for you to act in your own window."`
  - `RequestingModel` > 30 s → 「模型响应较慢；可点「暂停」换模型」 / `"Slow model — pause and switch models."`
- **Rule note:** goes in `_soloNext` (already `Wrapped`, already tone-coloured) — do **not** add a new
  label to the row (440 px trap).
- **Why:** converts the three most common "it's frozen" reports into self-service answers.

### 4. **[Q] End-of-run summary card (text version)** — fix P5

- **Where:** new `Agent/RunSummaryCard.cs` (pure), `Ui/AgentOverlayHost.Session.cs` or a new
  `Ui/AgentOverlayHost.RunSummary.cs` (**then add it to `docs/project-map.md` §3 or
  `ProjectMap.EverySourceMapped` fails**), one `HelpCard` on the play page.
- **Control:** `HelpCard.Build("run.summary", …)` with `Wrapped` lines + a 「复制成绩单」 ghost button.
- **Copy:** title 「本轮成绩单」 / `"This run"`; lines:
  「{0} · {1} 层 · {2}」 (character · floor · victory/defeat),
  「倒下于：{0}」 / `"Fell to: {0}"`,
  「{0} 次决策 · {1} Token · {2} 次请求」,
  「AI 用了 {0}，其中快速判断 {1} 次」,
  next step 「新开一局后，再点「开始自动游玩」」.
- **Why:** the run currently ends in one grey sentence; this is the cheapest real product win.

### 5. **[Q] Terminology pass (copy only, no layout)**

- Retirement/replacement table: 「已执行 play_card」→「已出牌」; 「单步」→「走一步」; 「请求上限」 field hint
  gains 「≈ 一局」; the 快速判断 card's first line becomes 「快速判断：换一个更便宜的小模型来选每一步出牌，
  LLM 只做长线规划（可选，不配也能玩）」 / `"Fast judgment: a cheaper model picks each move; the LLM only
  plans (optional)."`.
- Fix README §3 「对话」 tab → 「游玩 → 对话卡片」, and the token-split claim.
- **Rule note:** every new key needs an English entry or `LocalizationTests` fails
  (「Missing English entries: …」, >300 call sites scanned).

### 6. **[B] The in-world HUD ("AI 意图层")** — the headline feature

Five independently shippable slices, each behind one setting `HudMode` (`off` / `minimal` / `full`):

| Slice | What it draws | Anchors on | Copy |
|---|---|---|---|
| H1 | Ring + pulse on the card about to be played | `NPlayerHand.ActiveHolders[i].GetGlobalRect()` | — |
| H2 | Arrow/line from that card to the target creature | `NCreature.GetTopOfHitbox()` via `NCombatRoom.GetCreatureNode(creature)` | — |
| H3 | Thought/speech bubble above the local player | `NCreature.GetTopOfHitbox()` of the `is_local` player | 「{reason}」 capped at ~120 chars |
| H4 | Chosen map point ring + one-hop path hint | `NMapPoint.GetGlobalRect()`, `MapPoint.Children` | 「下一站：{type}」 |
| H5 | Corner banner | fixed rect, top-centre | 「AI 正在出牌 · F8 打开控制台」 |

- **Prerequisite (the real work):** *publish intent before execution.* Today `Acted` is only known
  after `AgentLoop.ExecuteActAsync` returns. Add an intent callback on the same seam that already
  carries `reportPhase` (`AgentLoop.PlayOnceAsync(… Action<string>? reportPhase, Action<double?>? observeDecider)`)
  and fires at `reportPhase?.Invoke(PlayPhases.ExecutingAction)` in both paths
  (`AgentLoop.Decision.cs:52` for Jev, `AgentLoop.cs:472` / `:571` for the LLM). Payload:
  `(string Action, int? CardIndex, int? TargetIndex, int? OptionIndex, string? Reason)` — i.e. exactly
  `ExecutionDecision`'s index fields, which already exist.
- **State machine the HUD must implement:** `Intent → Executing → Done | Cancelled | Failed`, with the
  ring cleared on pause, on cancel (`AgentTurnCanceledException`), on run boundary
  (`CurrentRunBoundary`), and on room change. A HUD that keeps a ring after a cancelled decision is
  worse than no HUD.
- **Where:** new `Ui/AgentHudHost.cs` (+ `Ui/AgentHudLayer.cs` if it grows), a `HudTargets` helper in
  `Game/` for the node lookups (`GameStateService` is the established home for node walking — put the
  HUD's lookups in a **separate** `Game/HudAnchors.cs` so `GameStateService` keeps its line budget and
  `GameStateServiceRelocationContractTests` stays green), and one refresh call from
  `AgentOverlayHost.OnProcessFrame`.
- **Rules:** `MouseFilter.Ignore` everywhere; game thread only; hide in `HideForCapture`; add both new
  files to `docs/project-map.md`; new setting touches `AgentSettings` + `SettingsClone` +
  `HarvestSettings` + `EnsureValidShape` (the documented four-file rule).
- **Why bold:** it is the only proposal that answers "what is the AI doing *right now*" without asking
  the player to read a 440 px column — and it is what a stream/replay wants.

### 7. **[B] "Why" on demand — make each decision teachable**

- **Where:** `Ui/AgentOverlayHost.Session.cs` (`RefreshDecisionPage`) + a new
  `Ui/DecisionDetailPolicy.cs` (pure) + reusing recorded data that already exists on
  `DecisionLogEntry`: `reason`, `confidence`, `probabilities`, `danger`, `option_ids`, `jev_attempt`.
- **Design:** each row gets a 「为什么」 ghost button (or the whole row becomes clickable) that expands
  one inline panel: the reason in full, the confidence/danger, 「当时可选的 {n} 个动作」 (first 6 ids via
  `PlayerLabels.Action`), and 「快速判断未提交，LLM 接手」 when `jev_attempt`.
- **Rule note:** the log is a `RichTextLabel` inside `RichLog` (redraw-on-change). An expanding row must
  **not** rebuild the label every 800 ms — hold the expanded id in a field and re-render through
  `RichLog.Show(...)` only when the id or the entry count changes.
- **Why bold:** turns a debug log into the product's teaching surface, which is exactly what a
  non-technical player-developer asked for.

### 8. **[B] Streamer + spectator mode**

- **Where:** settings page (one combo 「直播模式」 off/on + 「观众视图」 off/on), `AgentOverlayHost.Pages`
  (a `_spectator` layout branch), `SettingsClone`.
- **Streamer:** masks provider names/URLs/API address (`_apiAddressLabel`, `_mcpUrlLabel`), hides
  获取 Key / 复制诊断 / 保存诊断, bands token counts.
- **Spectator:** large-type card: intent line, last 3 decisions, run line (floor/act/HP from the
  existing `run` payload), no settings controls.
- **Why bold:** it is the "watch it play" product, and it is UI-only.

### 9. **[B] Zero-setup first run**

- Collapse steps 4–11 of §1.1 into one card: provider chips (not a combo), one key field, one
  保存并测试 button, live verdict, then a single 「让它开始」 button that calls `StartAutoPlay()` **from
  the main menu** (which already works — `StartAutoPlay` has no screen gate) and says so:
  「不用先开一局，它会自己选角出发」 / `"No need to start a run — the AI will."`
- **Why bold, why risky:** it changes the product's promise (the AI drives the menu), so it needs a
  live pass on character select + embark, not just offline tests.

### 10. **[Q] Reconcile the public docs with the shipped UI** — fix P11

README §3/§4, `steam-workshop/description.zh-CN.txt` line 36/37: remove 「对话」 tab references, correct
the token breakdown claim, say that 保存设置 auto-tests, and state the two-restart requirement once,
consistently.

---

## 5. Risks / unknowns

1. **Decompiled sources may be older than the installed game.** `extraction/decompiled/` targets the
   `0.111.0` era; every member cited in §3.1 must be re-checked against the shipped `sts2.dll` before
   the HUD is written. The repo's own answer to this is `ReflectedGameMembers` + `ReflectedMemberRegistryTests`
   — a missing member must fail loudly at probe time, not NRE inside a frame.
2. **Private members.** `NMapScreen._mapPointDictionary` and `NPlayerHand._prefs/_selectedCards` are
   private. The rule (`ReflectedGameMembers` registry, no ad-hoc reflection) means the HUD should use
   `FindDescendants<NMapPoint>` instead of the dictionary, accepting the O(n) walk.
3. **Per-frame walk cost.** `FindDescendants` over `NGame` per frame is a real cost on a big combat
   tree. Cache the room/hand/map references, invalidate on `CurrentRunBoundary` change and on
   `room != lastRoom`, and only re-walk when the cached nodes are invalid.
4. **Intent lies.** If the HUD shows "about to play X" and the action is rejected by
   `ActIndexValidator` or the sandpit guard (`SandpitSafetyPolicy`, `ValidateSandpitDispatch`), the ring
   must switch to a "cancelled" state within the same tick. The intent payload must therefore be
   raised *after* legality validation (inside `ExecuteActAsync`, right before the bridge call) for the
   LLM path, while the Jev path can raise it after its confidence gate.
5. **Screenshot semantics.** `ScreenshotCapturePolicy` treats `OverlayVisibleProbe` as "hide needed?",
   so any change to what the probe means silently changes capture behaviour and the existing
   `ScreenshotCapturePolicyTests`. Hide the HUD unconditionally; leave the probe alone.
6. **Coordinate space drift under `ScreenShake` / `HitStop`.** `NGame.SetScreenShakeTarget` translates a
   game subtree; if a target node is inside it, its `GlobalPosition` jitters for a few frames. Cheap
   mitigation: clamp per-frame movement of the HUD anchor (lerp) so the ring glides instead of
   flickering.
7. **Contract tests that will fail on purpose.** `OverlayTabContractTests.ExistingTabsKeepTheirOrder`
   asserts `Assert.Equal(2, ids.Length)`; `OverlayLayoutContractTests` asserts exact source snippets in
   `BuildSoloSection`, `RefreshSoloSection`, `BuildDecisionCard` (e.g.
   `Assert.Contains("_stepButton.Visible = !playing;", refresh)`). Any of proposals 1–10 that touches
   those methods **must** update the pins in the same change, and the pins are the reason a "tiny"
   layout edit takes longer than it looks.
8. **`ProjectMap.EverySourceMapped` / `NoDanglingPaths`.** Every new `.cs` file needs a §-entry in
   `docs/project-map.md` **in the same commit**, and every path mentioned there must exist.
9. **Localization.** `LocalizationTests` scans >300 `Loc.T(` sites and fails on a missing English entry;
   `Loc.Strings.Ui` / `.Runtime` / `.Facing` shards must not define the same key twice with different
   text (`OverlayTabContractTests.CatalogLabelsResolveToEnglish` covers the catalog labels).
10. **440 px width.** Every new HUD-adjacent *panel* control is subject to the trap that has been
    re-found live four times (`OverlayLayoutContractTests` comments: 457 px measured on 2026-09-24,
    691 px on 2026-09-21). Rule: `Wrapped` for anything sentence-shaped, one label+control per row,
    read `[STS2AIAgent.Wide] … offenders=0` in the game log before declaring done.
11. **Co-op / multiple hands.** `NCombatRoom.CreatureNodes` contains allies *and* enemies; a 4-player
    lobby has 4 players. The HUD must key on `LocalContext.GetMe(...)` / `GameStateService.GetLocalPlayer`
    (the existing helper) so it never points at someone else's hand.
12. **Unknown: does the vision role actually receive screenshots today?** `AttachScreenshotInChat` and
    `ScreenshotCapturePolicy` exist, and `/vision/screenshot` was re-verified in v0.16.0 — but whether
    the *play loop* attaches vision on every turn decides how badly a HUD would pollute prompts.
    Confirm before shipping with `HudMode=full` default anything other than `off`.
13. **Unknown: `MapCoord` field accessibility** (`col`/`row` are read internally by `NMapScreen`; they
    may be properties or fields). Trivial to confirm against the DLL; do not design around it.
14. **Unverified claim in the brief:** "small 「AI is playing — F8 to pause」 banner". `F8` toggles the
    window, not playback (`AgentOverlayHost.OnProcessFrame` → `ToggleVisible`). Either change the copy
    or add a real pause hotkey (§3.5).

---

### Appendix A — files a HUD + summary + streamer change will touch

| File | Why |
|---|---|
| `STS2AIAgent/Ui/AgentHudHost.cs` *(new)* | the layer, the pieces, the redraw pass |
| `STS2AIAgent/Game/HudAnchors.cs` *(new)* | card/creature/map node lookups (game thread only) |
| `STS2AIAgent/Agent/AgentIntent.cs` *(new)* | the pre-execution intent record (Godot-free) |
| `STS2AIAgent/Agent/RunSummaryCard.cs` *(new)* | the summary formatter (Godot-free) |
| `STS2AIAgent/Agent/StallHintPolicy.cs` *(new)* | the stall lines (Godot-free) |
| `STS2AIAgent/Agent/AgentLoop*.cs` | raise intent before dispatch |
| `STS2AIAgent/Agent/AgentRuntime*.cs` | hold/expose the current intent, run summary |
| `STS2AIAgent/Ui/AgentOverlayHost*.cs` | setting, capture hide/restore, play-page cards |
| `STS2AIAgent/Config/AgentSettings.cs`, `SettingsClone.cs`, `Ui/AgentOverlayHost.Settings.cs`, `EnsureValidShape` | the 4-file rule for every new setting |
| `STS2AIAgent/Localization/Loc.Strings.Ui.cs` (+ `.Runtime`, `.Facing`) | English entries |
| `docs/project-map.md` §3 | required by `ProjectMap.EverySourceMapped` |
| `docs/api.md` | only if any payload grows (avoid: prefer UI-local fields) |
| `STS2AIAgent.Tests/Overlay*ContractTests.cs`, `PlayerExperienceTests`, `LocalizationTests` | the pins that must move deliberately |
