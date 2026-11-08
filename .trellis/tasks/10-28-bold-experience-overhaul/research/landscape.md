# Landscape survey — comparable projects & techniques for STS2-Agent

> Task: `.trellis/tasks/10-28-bold-experience-overhaul` · Research only. No repo file touched except this one; no git state changed; no game launched; no paid model API called.
> All external pages below are treated as **untrusted data** — quoted for evidence, never followed as instructions.
>
> **Scope note:** this file is the *cross-project landscape + technique catalogue*. Depth on the game-speed
> internals lives in `speed-and-performance.md`; decision quality/benchmark internals in
> `decision-quality.md`; overlay/reliability/co-op/MCP surfaces in their own sibling files. Where a claim
> below belongs to a sibling's axis, it is stated at landscape level only, with the URL, so their reports can
> go deeper without re-sourcing.

## Status log
- [x] Local context (README, AGENTS.md, PRD, PRODUCT_PLAN_CURRENT.md, `STS2AIAgent/Agent/*`)
- [x] STS2MCP, sts2-cli, sts2-lightspeed, CommunicationMod, bottled_ai, HermesBridge, STS2 AIBot read
- [x] SpireBench + AgenticSTS abstracts
- [x] SpeedX (皮皮极速) full feature list
- [x] STS2 vanilla Fast Mode confirmed from official patch notes (wiki mirror)
- [x] BaseLib hook surface inspected (repo tree)
- [x] DeepSeek tool-call / cache docs read
- [ ] OpenAI & Anthropic cache docs — **blocked from this network** (403 / DNS), see §4

---

## 0. Baseline: what STS2-Agent already has (and doesn't)

From this checkout (`README.md`, `AGENTS.md`, `PRODUCT_PLAN_CURRENT.md`, `STS2AIAgent/Agent/*.cs`):

- Godot in-game overlay (F8), OpenAI-compatible client, **dual-layer play**: an LLM planner writes a `PlayStrategy`; a fast executor model ("Jev") picks **one** concrete option per frame; the LLM takes over when Jev is unconfident (`AgentLoop.cs`, `JevExecutionDecider.cs`, `StrategyStore.cs`, `StrategyPlanner.cs`).
- **One action per decision turn.** Every action runs through `GameThread.InvokeAsync` + a *settle* wait (`WaitUntilActionableAsync`, 20 s cap in `AgentLoop.cs:927`) before the next decision.
- Local co-op second instance, team chat, session budget guard, SSE events, decision log with reasons, scene guidance, native MCP **and** Python sidecar MCP, optional vision.
- **No pace control anywhere**: `grep -E "TimeScale|Engine\.TimeScale|fastMode|FastMode|SetSpeed|gameSpeed|animationSpeed"` over `STS2AIAgent/` returns **zero matches**. The mod never reads, respects, or toggles the game's own Fast Mode.

That last gap is the single largest asymmetry versus every project surveyed. Second largest: **the decision loop is one-action-per-model-turn**, while the state of the art plays a whole turn per response.

---

## 1. Project comparison table (feature · how · URL)

### 1a. Slay the Spire 2

| Project | What it is | Techniques / UX worth stealing | URL |
|---|---|---|---|
| **Gennadiyev/STS2MCP** | Mod opens a localhost REST API (`:15526`) + optional Python MCP server. Agents play full runs, SP + co-op; menu and lobby control included. Tested on STS2 v0.103.2 | **Thin mod, fat client**: the mod has no in-process decision loop and no overlay, so the model's thinking time consumes no frame budget — the most plausible mechanical reason users describe it as smooth while an in-process loop stutters. **Full menu/lobby driving**: profile switch, character select with seed, host / Steam-friend / FastMP join, co-op load lobby, game-over dismissal, FTUE popup handling, Timeline visibility. **Bounded context tools**: `/api/v1/wiki?query=` fuzzy search restricted to the profile's *discovered* cards/relics (10 results default) instead of dumping the catalog; `/api/v1/compendium` for durable context with run history capped at 20 files. Published cost: **~7.3–8M tokens per full run** (GPT-5.4 / Claude Sonnet 4.6) | https://github.com/Gennadiyev/STS2MCP · https://github.com/Gennadiyev/STS2MCP/blob/main/README.md |
| **wuhao21/sts2-cli** | Runs the **real STS2 engine headless**: IL-patched `sts2.dll` + Godot stubs + Harmony (localization). Interactive CLI + stdin/stdout JSON protocol; all content unlocked; A0–A10 | **Instant decision points**: no renderer and no animation wait — the loop is just `{cmd, action}` → decision point (`map_select` / `combat_play` / `card_reward` / `rest_site` / `event_choice` / `shop` / `game_over`). **Every run auto-logged as JSONL** (state+action+timestamp, 7-day rotation) and bug reports are expected to attach it ⇒ a free replayable corpus. **`play_full_run.py <n> <character>` as a compat gate** (non-zero exit on crash/stall/timeout). **All unlocked** removes grind-to-test friction | https://github.com/wuhao21/sts2-cli |
| **yizhang-dream/sts2-lightspeed** | Independent C++20 STS2 engine (v0.111.0) for RL: full-run env, MCTS battle search, Python bindings, REINFORCE trainer | **MCTS combat search at ~21 µs/simulation** (2000 sims ≈ 1.5 s/battle; 3.3 runs/s over 12 processes). **Parity corpus**: 65 recorded real-game positions replayed with **zero field diffs required** in CI. **`run determinism <seed>`** fingerprints. Performance work only shipped behind a **bit-identical decision gate**. These are the anti-regression patterns a bold overhaul needs | https://github.com/yizhang-dream/sts2-lightspeed |
| **hiKareeem/ClaudePlaysTheSpire (HermesBridge)** | STS2 mod exposing state over **file-based IPC**: `state.json` / `commands.json` / `result.json` / `trace.log`, plus a shipped agent `SKILL.md` | **Revision-counter handshake** (`Wait-Revision`) so the driver never reads a stale snapshot — a direct answer to polling races. **Monotonic command ids** + typed commands (`PlayCard`, `EndTurn`, `SelectMapNode`, `Purchase`, …). **Per-tick driving rule**: one command at a time, never wrap the driver in a loop (documented as *the* reliable failure mode). Explicitly documents "known refresh lags, state staleness, sub-screen traps". Honest capability claim: the skill only works on frontier models — "smaller models have reliably failed the same way" | https://github.com/hiKareeem/ClaudePlaysTheSpire |
| **ForeverVirus/Slay_The_Spire_2_AIBot** | In-game AI mod with **four modes** and a local knowledge base | **Full Auto / Semi Auto / Assist / QnA**: Semi-Auto executes *natural-language* instructions ("play this turn for me", "pick a reward"); **Assist never plays — it pastes recommendation labels on the actual cards/buttons/screens**; QnA answers with the live board. **Pace config per screen**: `screenActionDelayMs`, `combatActionDelayMs`, `mapActionDelayMs` (+ `pollIntervalMs`, `decisionTimeoutSeconds`). **Layered knowledge base** `core/` + player `custom/` with override-by-id, covering mechanics rules *and* long-form Markdown guides; decision panel + chat dialog + hotkeys | https://github.com/ForeverVirus/Slay_The_Spire_2_AIBot |
| **SpeedX / 皮皮极速** (Workshop 3747557357, Nexus 91) | Pure speed/QoL mod, no gameplay change | **2×–20× global speed** (10–15 min saved per run); **skip banners** (enemy turn / player turn / combat start / floor transition ≈ 5 s per turn); **card flight animations ×3**; **remove hit-stop** (per-hit screen freeze); **auto end turn when nothing is playable** (but waits if the player holds potions); **auto-collect gold and potions after combat while leaving card/relic choices to the player**; **auto-continue** in treasure/event rooms, skips shop; **"time saved" overlay counter**; **per-feature toggles** (F9 panel, optional `ModConfig` integration into the game's settings panel); **multiplayer-safe with mixed speeds, no desync** (beta branch: no matching install needed; stable branch: identical mod sets required) | https://patch.ali213.net/showpatch/304615.html · https://m.18183.com/xiazai/bd_22025.html · https://www.nexusmods.com/slaythespire2/mods/91 |
| 瞬时速度 / 快速动画模式 (further CN speed patches) | Same family of QoL speed patches | Proof that several independent authors patch the same banner/animation pipeline ⇒ the pipeline is well mapped; also that "too slow" is the #1 complaint of this player base | https://dl.3dmgame.com/patch/374542.html · https://mod.3dmgame.com/mod/252764 |
| **Local Multi-Control** (Solo Multiplayer, 2–12 characters) | QoL/co-op mod | Lets one host run **2–12 characters** locally — the "AI fills the whole party" UX becomes possible on top of STS2-Agent's existing dual-instance launcher | https://catalogue.smods.ru/archives/460777 |

### 1b. Slay the Spire 1 (techniques that transfer)

| Project | What it is | Techniques / UX worth stealing | URL |
|---|---|---|---|
| **CommunicationMod** (157★) | Launches an external process; stdin/stdout JSON | **Push on stability, not on a clock**: whenever state is "determined to be stable (no longer changing without external input)" the mod pushes state and *waits* for exactly one command. **Escape hatches for unknown screens**: `KEY <name>`, `CLICK x y`, `WAIT <frames>`, `STATE` — an unrecognized/modded screen degrades to raw input instead of dead-ending (contrast: STS2-Agent halts after 3 consecutive failures). **`CHOOSE <index|name>`** — choice by *name*. **Explicit error shape** `{"error": "...", "ready_for_command": true}`. **10 s `ready` handshake**, process killed on timeout. Caveat on the tin: "Communication Mod has not been tested without fast mode on" | https://github.com/ForgottenArbiter/CommunicationMod |
| **bottled_ai** (xaved88) | Hand-built non-LLM bot; best strategy ≈52% winrate (Watcher) | **Simulate the whole turn, then choose**: graph traversal over play orders + a simulator, scored against **~40 weighted values** ("prefers a turn where it defeats an enemy, but not if we take a bunch of damage"). **Fair-play discipline**: "it can only see what the player does" — no draw-pile peeking. **`presentation_config.py`**: *action pacing is configuration*, decoupled from decision logic — the clean way to ship "instant" without forking the bot. **`run_controller.txt`** as an out-of-band pause switch. **Tests that exercise bot behaviour without the game** | https://github.com/xaved88/bottled_ai |
| **sts_lightspeed** (gamerpuppy) | Headless C++ STS1 for RL/MCTS | Lineage of the fast simulator and `BattleScumSearcher2` (UCB playouts, progressive bias, transposition dedup, balanced root sampling); the STS2 port proves the recipe transfers | https://github.com/gamerpuppy/sts_lightspeed |
| **spirecomm** | Python client library for CommunicationMod | Convention layer on top of the protocol; `CHOOSE`-by-name convenience | https://github.com/ForgottenArbiter/spirecomm |

### 1c. Research harnesses

| Work | Claim | Techniques worth stealing | URL |
|---|---|---|---|
| **SpireBench** (Lowe & Keemink, arXiv 2610.12596, Oct 2026) | 20 LLMs at A20 on the full commercial STS1: frontier models beat heuristic baselines and match the average human, still far behind experts; the gap **widens as the run lengthens** | **Whole-turn batching is a win on both axes**: "We allow the model to return a whole turn's actions as multiple tool calls in one response, and the harness reports the effect of each action back to the model. Against its other variants on the same models and seeds, it plays further and **costs less per floor**." Section headings confirm the rest of the recipe: *Names, not indices*; *Batches and interruptions*; *Corrections and refusals*; *Actions the harness takes by itself*; *Guards*; *One conversation per floor*; *Cost and reasoning*. **Memory hurts**: models "do not improve from the memories they write across runs, which are often wrong and overly cautious" — 17% of stored memories false, caution advice outnumbers risk advice 7:1, "nothing prunes errors". **Risk calibration** is what separates models: weak play hoards HP instead of spending it to grow | https://arxiv.org/abs/2610.12596 · https://arxiv.org/html/2610.12596v1 |
| **AgenticSTS** (Cheng et al., arXiv 2607.02255, Jul 2026) | "A Bounded-Memory Testbed for Long-Horizon LLM Agents" — instantiates memory contracts in **STS2** | **Bounded memory contract**: "every decision is made from a fresh user message assembled by typed retrieval, with no raw cross-decision transcript appended" ⇒ prompt stays bounded at any run length and each memory layer can be ablated. Reports frontier LLMs at **zero wins at A0** across five configurations vs a 16% developer-reported human win rate; their own skill layer moved 3/10 → 6/10 wins (p≈0.37, directional only). Releases 298 tagged trajectories, frozen memory/skill snapshots, prompt records | https://arxiv.org/abs/2607.02255 |
| **Orak** (Park et al. 2026, per SpireBench §2.1) | 12-game benchmark; drives real STS1 via CommunicationMod; LLM plays only combat + card rewards, a rule-based bot does the rest | Hybrid division of labour: deterministic bot for solved sub-problems, LLM only where judgement pays | cited in https://arxiv.org/html/2610.12596v1 |

---

## 2. Techniques worth adopting, ranked by impact

Impact = (player-visible time saved × reliability gained × reach) ÷ implementation risk. Each item names the evidence and the concrete hook into this codebase.

### P0 — the "it feels slow" problem (biggest UX delta, no model cost)

**1. A pace/presentation layer with per-screen delays and an explicit `instant` ceiling.**
Today every action pays a settle wait; nothing in the mod knows the game has a speed setting. bottled_ai isolates pacing in `presentation_config.py`; AIBot ships `screenActionDelayMs` / `combatActionDelayMs` / `mapActionDelayMs`; SpeedX saves 10–15 min/run by attacking banners, card flight and hit-stop. Adopt: a `Pace` profile (`instant` / `fast` / `cinematic`) that (a) sets the mod's own inter-action delay per screen, (b) can *request* the game's Fast Mode while autoplay runs and restore the previous value afterwards (explicit opt-in, visible in the overlay), (c) never inserts delays when the state fingerprint already advanced. Ship it defaulted to `fast`.

**2. Auto-flow for the parts nobody wants to watch.** SpeedX's auto-end-turn / auto-collect / auto-continue are pure UX wins with **zero model cost**, and they are the parts a human notices most. STS2-Agent already has deterministic action handlers for all of them; they are just not composed into a "no decisions needed → keep going" fast path. Add a `flow` policy: when no decision is required (banner, empty reward, chest already claimed, non-choice proceed), advance without a model call and without waiting for the user. Guard it exactly like SpeedX does: *never* take a card/relic/potion choice, and pause auto-end-turn if potions are held.

**3. Whole-turn action batching with per-step effects returned.** SpireBench's headline harness result ("plays further and costs less per floor") is the closest thing to a measured, generalizable efficiency claim in this domain. Concretely: a `play_turn` tool accepting an ordered action list, executed in order with the resulting deltas streamed back, so a 5-card turn is one request instead of five; plus interruption semantics (stop on first illegal/unexpected result, return what happened) — SpireBench's "Batches and interruptions" section is exactly this contract. This also collapses the dominant per-turn latency: 5 settle waits + 5 model round-trips → 1.

**4. Push on stable state instead of polling-and-waiting.** CommunicationMod's whole design is "state is pushed when it stops changing; the mod then waits for one command". STS2-Agent already emits SSE, but the agent loop still polls/settles (`EventPollingCoordinator.cs` 250 ms, `WaitUntilActionableAsync` 20 s). Invert it: an event-driven "actionable, and here is the frame id" signal; action handlers return the frame id they acted on, and stale-frame retries become a non-issue. HermesBridge's `Wait-Revision` handshake is the cheap version of the same idea.

**5. Names, not indices.** SpireBench names this as a deliberate robustness choice; CommunicationMod's `CHOOSE` accepts a name. Indices are the failure class this repo keeps paying for (`invalid_target`, stale frames, "Jev chose an unknown option", the card-grid index contract work in v0.11.0). Adopt name/stable-id selection as the primary API with indices as a compatibility path, and let the LLM say `play_card("Bash", target="Cultist")`.

### P1 — decision quality per token

**6. Read the game's own Fast Mode instead of guessing.** (See §3.) Even without toggling it, exposing `fast_mode: true|false` in `/state` lets the agent, the skill and the docs stop being wrong about how long actions take; and it gives the overlay a one-line hint ("Fast Mode is off — turns take ~2× longer") that costs nothing.

**7. A deterministic combat solver as an optional executor.** sts2-lightspeed shows ~21 µs/sim with a 2000-sim budget (≈1.5 s/battle) and a parity gate that keeps it honest; bottled_ai shows the cheaper version (enumerate play orders + ~40 weights) is enough for a 52% winrate bot. For STS2-Agent the pragmatic version is a *local* turn enumerator (the codebase already has `JevOptionEnumerator` producing concrete legal options!) scored by a small simulator/estimator, used when Jev is unconfident and the LLM is slow. Bonus: the solver can *explain* itself ("this line wins in 3 turns, that one takes 9 damage"), which is far more convincing than a model's prose.

**8. Memory that is bounded, pruned, and verified.** Both STS2 papers point the same way: SpireBench finds cross-run memory makes things *worse* (17% false, 7:1 caution bias, no pruning); AgenticSTS's whole thesis is a bounded contract with typed retrieval and no raw transcript. STS2-Agent's `get_planner_context` / handoff / knowledge surfaces are exactly the risky area. Adopt: (a) hard caps per layer, (b) a *verification* step before writing a "fact" (does the game data agree?), (c) pruning/expiry, (d) prefer facts about the current run over lore about past runs, (e) make each layer individually switchable so it can be ablated.

**9. Prompt shape that actually hits prefix caches.** DeepSeek's cache is **exact full-prefix-unit matching** — "A + B" then "A + C" misses; the system persists both the end-of-input and end-of-output positions as units, plus common-prefix units, and reports `prompt_cache_hit_tokens` / `prompt_cache_miss_tokens` (https://api-docs.deepseek.com/guides/kv_cache). Consequences for this repo: keep the invariant block (rules + skill + card/relic DB slice) **byte-identical and first**, put the volatile state last, never interpolate timestamps/ids/ordering into the prefix, and surface cache-hit tokens in the overlay next to the existing counters. With runs costing ~7–8M tokens (STS2MCP's own numbers), a cache hit rate is the single biggest cost lever available.

**10. Capability matrix + explicit degradation for OpenAI-compatible gateways.** DeepSeek's own docs show how much varies: `strict` mode is **beta-only** (needs `base_url=.../beta`, `strict: true` per function, `additionalProperties: false`, *all* properties `required`, and a restricted schema subset), and the Chat Completions API **cannot insert tool calls mid-conversation** while the Anthropic-style and Responses APIs can (https://api-docs.deepseek.com/guides/tool_calls). Community fixes cluster around the same four failure modes: malformed/truncated streamed tool arguments, lost `tool_calls` in multi-turn history, backends with no tool parser at all, and thinking models that need a JSON-schema fallback. Adopt: probe each endpoint once (tools / strict / JSON mode / streaming usage / mid-conversation insertion), store a capability row, and route through a fallback ladder — *native tools → strict tools → JSON-mode envelope → single-line DSL* — failing **closed** on partial arguments rather than executing half a call.

### P2 — product surface and safety

**11. Assist mode: recommend without acting.** AIBot's four modes (Full Auto / Semi Auto / Assist / QnA) are the most player-friendly framing found in any STS2 project, and Assist — recommendation labels pinned to the real cards/buttons — is the mode a nervous first-time user actually wants. STS2-Agent has chat advice but no inline recommendation layer; the overlay already draws over the game.

**12. Escape hatches instead of a circuit breaker as the only answer.** CommunicationMod's `KEY` / `CLICK` / `WAIT` / `STATE` mean an unknown screen never ends the session. STS2-Agent stops autoplay after 3 failures / 5 unsettled turns. Add a documented "raw input" tier (click at normalized coordinates, press the confirm key, wait N frames) that the model can request when `available_actions` is empty on an unknown screen, with the same audit trail.

**13. Replayable JSONL traces + a parity corpus as CI gates.** sts2-cli logs every step for free; sts2-lightspeed gates merges on a 65-position parity corpus and bit-identical decisions. STS2-Agent has a decision log and heavy offline suites but no *replay* gate. A recorded `state → action → state'` corpus would let the "bold overhaul" refactors (pace layer, batching, names-not-indices) be proven non-regressive offline, which is exactly the kind of change that otherwise needs live game sessions.

**14. Keep the per-frame path thin.** STS2MCP and HermesBridge both keep decision-making outside the render loop; STS2-Agent's in-process loop + overlay + vision is the plausible frame-drop source. If P0 items add work, put the executor and any solver in a worker with a frame-id handshake, and make overlay updates event-driven (they already are, via SSE).

**15. Co-op ambition: let the AI fill the party.** With Local Multi-Control supporting 2–12 characters, "invite one AI teammate" is only the first step; the launcher already clones settings and steps `clientId`, which is most of the hard part.

---

## 3. STS2 speed / fast-mode facts with sources

1. **STS2 ships a vanilla `Fast Mode` tickbox.** Patch **V0.95.0** (released 2026-02-19), *UI & UX → General*: "**Combat Speed setting is now Fast Mode and is a tickbox**". A "Combat Speed" settings option (with a hovertip) existed before, added around **V0.50.0**: "Added hovertip for Combat Speed settings option".
   https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2:V0.95.0_-_Pre-release_Version · https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2:V0.50.0_-_Pre-release_Version
2. **Fast Mode is a live, maintained code path in current STS2, not a legacy flag.** V0.95.0 bug fixes: "The robots in Punch-Off event no longer punch each other at Benny-Hill-esque speeds when you're in Fast Mode"; the Chomper page notes a VFX overlap "especially when Fast Mode is enabled".
   https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2:Chomper
3. **STS1 precedent is the same shape**: Fast Mode was a Settings checkbox introduced in Weekly Patch 11 ("Simply checkbox 'Fast Mode' in the Settings screen"), and patches kept tuning per-card timings for it ("X resolves 0.3 s faster in Fast Mode"). CommunicationMod explicitly says it was never tested without fast mode ⇒ fast mode is the de-facto baseline for bot play.
   https://slaythespire.wiki.gg/wiki/Weekly_Patch_11_-_A_Library_of_Features
4. **Speed is moddable far past Fast Mode.** SpeedX: 2×–20× global speed, banner skipping, card-flight ×3, hit-stop removal, auto-end-turn/auto-collect/auto-continue, per-feature toggles (F9; optional ModConfig panel), and **multiplayer-desync-safe with mixed speeds/installs** (stable branch: identical mods required).
   https://patch.ali213.net/showpatch/304615.html
5. **The animation layer can also be bypassed entirely.** sts2-cli drives the real engine headless via IL patch + Godot stubs; sts2-lightspeed reimplements the rules in C++ with a parity gate. So "no-animation" does not require defeating the animation system — it can be routed around.
   https://github.com/wuhao21/sts2-cli · https://github.com/yizhang-dream/sts2-lightspeed
6. **Modding stack**: **BaseLib** (Alchyr, 460★, C#) is the de-facto content/hook library — HermesBridge lists it as a hard dependency and calls it "the StS2 mod loader". Its tree contains a typed hook set (`BaseLibHooks`, `IAfterCardPlayed`, `IHealAmountModifier`, `IMaxHandSizeModifier`, `IHealthBarForecastSource`, …) and UI patches (`Patches/UI/CustomAnimationPatch.cs`, `MerchantCharacterAnimPatch.cs`) — i.e. **hooks exist for content and UI, but there is no speed/timescale hook**: the banner/hit-stop work in SpeedX is Harmony patching of the animation pipeline, not a supported hook. STS2's own patch notes also reference a `ModHelper` for pool injection.
   https://github.com/Alchyr/BaseLib-StS2

**Net answer to "does STS2 expose fast mode / game speed?"** — Yes, at the *user-settings* level: one boolean Fast Mode tickbox (no numeric slider). There is **no known public numeric timescale API**; going beyond Fast Mode means Harmony-patching animations/banners (what SpeedX does) or running headless (what sts2-cli does). Whether a mod can read/write that Fast Mode flag programmatically is **unverified** (§4).

---

## 4. Unknowns (must be checked before promising anything)

1. **Is Fast Mode readable/writable from a mod?** Patch notes prove the setting exists; nothing found shows its backing object (Godot `ProjectSettings` vs a save-file/config field). Inspecting `sts2.dll` for the settings model is the next step — this decides whether "auto Fast Mode" is a 20-line change or a Harmony patch.
2. **Exact Harmony targets for banners / hit-stop / card flight.** SpeedX is closed-source here (binary distribution only); no public source of its patch targets was found. Would need decompilation of the animation driver or the SpeedX DLL.
3. **Multiplayer reconciliation.** SpeedX claims desync-safe mixed speeds. Whether an *agent* acting faster than the human's client is safe (turn ownership, map votes, co-op RNG) is untested — STS2-Agent's co-op path is the highest-risk area for a speed layer.
4. **The "no frame drops" claim about STS2MCP is second-hand.** It was given as user feedback in the brief, not found as a quotable source. The thin-mod-vs-in-process-loop explanation is an *inference*, and should be treated as a hypothesis to measure (frame time with overlay open + loop running), not a fact.
5. **OpenAI and Anthropic prompt-cache specifics could not be verified from this machine** — `platform.openai.com`, `openai.com/index/*` and `developers.openai.com` return 403, `docs.anthropic.com` redirects to `platform.claude.com` which does not resolve here. The DeepSeek rules in §2.9 are sourced; the OpenAI/Anthropic thresholds, discount rates and TTLs must be re-read before coding against them.
6. **Whether batching is safe on this game's screens.** SpireBench's win is on STS1 through a different harness. STS2 has interrupt screens (modal, FTUE, sub-menus) mid-flow; the batch contract needs a "stop and report" rule and live verification per screen type.
7. **How Jev is meant to be used at scale.** `JevOptionEnumerator` caps options (`MaxOptions`) and drops unmatched hints; whether option caps bind in multi-enemy fights with big hands is unmeasured, and it directly limits a solver/batching design.
8. **AgenticSTS's memory implementation details** (what "typed retrieval" concretely stores) are only in the abstract; the paper's HTML/PDF was not read end-to-end.
9. **Steam Workshop / Reddit sentiment** for these mods could not be read (steamcommunity.com and reddit fetches fail from this network), so player complaints are inferred from the CN patch-download sites and the mod feature lists rather than from discussion threads.
