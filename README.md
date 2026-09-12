# STS2 AI Agent

<div align="center">

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

**In-Game AI Companion & Autonomous Gameplay Mod for Slay the Spire 2**

[中文说明 (README.zh-CN)](./README.zh-CN.md) • [Current Status](./PRODUCT_PLAN_CURRENT.md) • [Historical Co-op Evidence](./COOP_DELIVERY.md) • [API Docs](./docs/api.md) • [MCP Tools Guide](./mcp_server/README.md)

</div>

---

## 🌟 Key Highlights

- 🎮 **In-Game Overlay UI**: Press **F8** at any time to open the configuration and control window directly inside the game—no external browser required.
- 🤖 **OpenAI-Compatible Endpoints**: Configure a compatible chat-completions endpoint, with per-model thinking settings. Tool calling, vision, streaming, and usage reporting depend on the provider and model; test chat and play separately.
- 🃏 **Autonomous Auto-Play**: Uses text state for combat, card drafting, shops, events, pathing, and capstones, with optional screenshot context. Run completion and strategy quality depend on the model and game conditions; see the current status for validation limits.
- 👥 **Local Co-op AI Teammate**: One-click launch from the main menu spins up an isolated second game instance. You play your character; the AI teammate plays its own character in co-op mode.
- 💬 **Live Team Conversation**: Talk to your AI teammate in natural language from the human window (e.g., "focus the right cultist", "let's take the shop path"). The AI replies and uses recent context in subsequent decisions.
- 🛡️ **Session Budget Guards & Fault Recovery**: Tracks reported token usage and request counts, stops new calls when session limits are reached, and bounds retries. Missing usage is shown as unknown; these guards are not a billing guarantee.
- 🔌 **Developer-Ready**: Built-in local HTTP API (`:8080`). The overlay Connect tab can expose MCP at `/mcp` on the same port for Cursor, Claude, and Codex.

---

## 🚀 3-Minute Quick Start (Players)

The easiest way to play is the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050) page. Subscribe, wait for the download, then Steam → **Play with Mods**. You do not copy any files. That page is a short player guide; this README has extra detail if you need it.

This mod is still in development. Some things may be unfinished or break. Please send suggestions and issues here: [GitHub Issues](https://github.com/CharTyr/STS2-Agent/issues).

### Step 1: Install The Mod

**Option A: Steam Workshop (recommended)**

1. Subscribe on the [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050) and wait for the download.
2. Start the game from Steam with **Play with Mods**. You do not copy any files.
3. If the game warns that the code is untrusted: accept, quit fully, enable **STS2 AI Agent** in Mods, then restart.

**Option B: GitHub zip**

1. Download `sts2-ai-agent-v*-windows.zip` from [GitHub Releases](https://github.com/CharTyr/STS2-Agent/releases) and unzip it.
2. Copy **only** these three files from the zip **`mod/`** folder into the game `mods/` folder (create it if needed):
   ```text
   STS2AIAgent.dll
   STS2AIAgent.pck
   mod_id.json
   ```
3. Final layout example:
   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\STS2AIAgent.dll
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\STS2AIAgent.pck
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\mod_id.json
   ```
   Do not copy the whole zip or `mcp_server/` into `mods/`. `mcp_server/` is optional for developers.

**Do not double-install:** if you already subscribed on Workshop and also copied GitHub files, delete the manual copies from `mods/` and keep the Workshop item. Two copies can load twice.

### Step 2: Launch The Game & Open The Overlay
1. Start *Slay the Spire 2* from Steam with **Play with Mods**.
2. Press **`F8`** (configurable) or click the grey **`AI`** tab on the right edge of the screen to open the Agent window.

### Step 3: Configure Your LLM Endpoint
1. The overlay opens automatically on first launch. Later, press **`F8`** or click **`AI`**. A default URL plus model name is **not** treated as ready.
2. Open **Settings**:
   1. **Add Endpoint** (name, Base URL; API Key may be empty for Ollama / LM Studio)
   2. **Add Model** and bind it to that endpoint
   3. Choose **Chat Model** and **Play Model** (empty play model uses chat)
   4. Click **Test Connection** (sends a request to your configured service). Chat success is not play success.
   5. Click **Save Settings**. Unsaved edits are saved when you leave the tab so they are not dropped silently.
3. Thinking intensity, vision, and session budgets are under **Show advanced options**.
4. Invite a teammate only after the play model shows connectivity success.

### Step 4: Play!

#### Option A: Auto-Play
- Start a standard single-player run.
- Switch to the **Play** tab and click **Start Auto-Play** (or **Step Once**).
- The model evaluates live state and dispatches cards, rewards, and route decisions autonomously.

#### Option B: Co-op With An AI Teammate (Recommended!)
- Go to the game's **Main Menu**.
- Switch to the **AI Teammate** tab and click **Invite AI Teammate**.
- A second game window will launch automatically and join the co-op lobby. You play your character; the AI controls its character!
- The main window shows whether the teammate is connected, waiting on you/the game/the model, or why it stopped and what to click next. **Pause teammate** gives immediate feedback; already submitted actions still finish.
- Use team chat to coordinate in plain English or Chinese.
- By default the teammate takes the preselected character and readies up by itself. To pick its character yourself (in the teammate window, or through the companion API with `select_character` then `embark`), set `companionAutoSelectCharacter` to `false` in the mod's `settings.json`; the teammate then waits on the character screen without a timeout.

---

## 🎮 Core Features

### 1. Autonomous Gameplay (Auto-Play)
- **Compact State Engine**: Actionable text state covering cards, energy, intents, relics, HP, and potion slots. Vision is optional; offline state and action tests do not prove full-run completion.
- **Optional Vision Augmentation**: When using a vision-capable model, screenshots are captured on demand to provide rich visual context.
- **Real-Time Counters**: The overlay displays prompt, completion, total tokens, and request counts live.

### 2. Dual-Instance Local Co-op
- **Zero-Collision Isolation**: Propagates `--force-steam off` and increments `clientId` in offline mode. Automatically derives and clones `settings.companion.json` so both instances never write over each other's configurations or save slots.
- **Team Conversation**:
  - Chat directly with the AI teammate during multiplayer runs.
  - Teammate replies using its play model, and recent discussions inform subsequent play decisions.
  - Read-only safety: The chat interface never plays cards for the human or unpauses a paused companion.
  - Conversation is player-initiated by default. An optional, off-by-default proactive chat lets the teammate speak one short line when a fight starts and ends, with a selectable tone (轻松搭档 / 沉稳参谋 / 简短简报). It never plays for you and stays under the session budget.

### 3. Interactive In-Game Advisor
- Use the **Chat** tab to ask strategic advice.
- Enable "Attach State" to pass full live game context (deck, relics, route) to the model for tactical guidance.

---

## 🛡️ Reliability & Safety Guards

| Mechanism | Description | Player Benefit |
|---|---|---|
| **Session Budget Guard (`SessionBudgetGuard`)** | Tracks provider-reported JSON/SSE usage and requests; checks configured caps before new calls | Stops new calls at the limit and shows next steps; in-flight calls can add usage, and missing usage remains unknown |
| **Autoplay Circuit Breaker (`AutoPlayRecovery`)** | Automatic halt after 3 consecutive failures with 2s/4s exponential backoff | Prevents spin loops on unrecognized game dialogs or invalid choices |
| **Immediate Config Error Exit** | Halts immediately upon receiving HTTP 401, 403, or 404 responses | Stops wasted token calls when API keys expire or are mistyped |
| **Run Boundary Protection (`CurrentRunBoundary`)** | Scoped strictly to the active run's unique `runId` | Exiting a run, surrendering, or returning to lobby immediately stops autoplay |
| **Process & Settings Isolation (`CoopLaunchPolicy`)** | Dedicated companion settings file and safe `clientId` stepping | Complete segregation between main and companion instances |

---

## 🛠️ Advanced Users & Developers Guide

### System Architecture

```text
┌───────────────────────────────────────────────────────────┐
│                    Slay the Spire 2                       │
│  ┌─────────────────────────────────────────────────────┐  │
│  │             STS2AIAgent (C# Mod)                    │  │
│  │  - Godot In-Game Overlay UI (F8)                    │  │
│  │  - OpenAI-compatible Client & Budget Guard          │  │
│  │  - Autoplay Decision Loop & Recovery Controller     │  │
│  │  - GameThread Action / State Synchronizer           │  │
│  │  - Local Dual-Instance Process Launcher             │  │
│  └───────────────────────┬─────────────────────────────┘  │
└──────────────────────────┼────────────────────────────────┘
                           │ Local HTTP API (:8080)
                           │ Optional MCP (:8080/mcp)
                           ▼
        External Agents (Cursor / Claude Desktop / Codex)
```

### Local HTTP API

The mod runs an embedded HTTP server on `http://127.0.0.1:8080` (with dynamic fallback on port contention):

- `GET /health`: Health check, returns `api_port`, `instance_role`, `mcp_enabled`, and process PID.
- `GET /state`: Full raw game state JSON.
- `GET /actions/available`: Currently available legal actions and schema.
- `GET /events/stream`: Real-time SSE stream for game events.
- `POST /action`: Dispatch an action (e.g., `play_card`, `choose_map_node`, `proceed`).
- `GET /data/{collection}`: Export a bundled game metadata collection (`cards`, `relics`, `monsters`, `potions`, `events`, `powers`, `characters`).
- `POST /session/control`: Start or pause autoplay for this instance (`{"running": true|false}`).
- `POST /companion/control` / `POST /companion/message`: Control or message the AI teammate instance (local dual-instance only).
- `POST /mcp`: Optional MCP (Streamable HTTP). Off by default; enable it on the overlay Connect tab.

### Which MCP entry to use

| If you want to… | Use | Needs | How to confirm |
| --- | --- | --- | --- |
| Play with an AI teammate | In-game overlay, MCP off | Mod only | F8 / AI tab opens **AI Teammate** |
| Drive the game from Cursor / Claude / Codex | **Native MCP** on the Connect tab | Mod only | Copy the **actual** URL shown (port may not be 8080) |
| stdio, layered/full, or compatibility | Optional Python `mcp_server/` | Python + uv | From the release root, run `scripts/test-mcp-tool-profile.ps1` |

External clients: F8 → **Connect** → enable **Open MCP service** → copy the URL or JSON. It shares the HTTP API port and listens on `127.0.0.1` only. Do not hard-code `8080` or `8765`.

The Python sidecar is not required for players and is not the recommended entry.

Default shape (replace the port with the one on the Connect tab):
   ```json
   {
     "mcpServers": {
       "sts2-ai-agent": {
         "type": "http",
         "url": "http://127.0.0.1:8080/mcp"
       }
     }
   }
   ```
Built-in MCP tools add `health_check` on top of the surface in-game Auto-Play uses: `get_game_state`, `get_available_actions`, `act`, `get_game_data_*`, `wait_until_actionable`.

In-game Auto-Play already follows the bundled play contract. If you drive the game from an **external** agent over MCP (Cursor, Claude, Codex, or the optional Python sidecar), also load the companion skill [`sts2-mcp-player`](./skills/sts2-mcp-player/SKILL.md). Connecting tools without that skill can click legal actions; the skill is what matches in-game play quality.

---

## 🧪 Building From Source & Automated Testing

Standalone core and MCP contract tests run **without the game client**. Godot UI, real-game actions, multiplayer, saves, and installation channels require separate live validation:

The GitHub zip includes only the optional sidecar launch and profile-check
scripts under `scripts/`. The build, preflight, and live-game commands below
require a source checkout.

### Build Mod

> ⚠️ **Close the game first** so the DLL file is not locked by the OS.

```powershell
# Windows
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release

# Linux / macOS
./scripts/build-mod.sh --configuration Release
```

### Run Tests

- **C# Core Unit Tests**:
  ```powershell
  dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
  ```
  Covers the session budget guard, autoplay recovery backoff, run-boundary interception, offline dual-instance isolation, HTTP port fallback, and native save switching, among others.
- **Python MCP Contract Tests**:
  ```powershell
  cd mcp_server
  uv run python -m unittest discover -s tests -v
  ```
- **Full Release Preflight Check**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File ".\scripts\preflight-release.ps1"
  ```

---

## ❓ FAQ

### Q1: Pressing F8 does not open the overlay.
1. Workshop: start with **Play with Mods**. GitHub: copy the three files from the zip `mod/` folder into the game `mods/` folder. Do not keep both Workshop and a manual copy.
2. Confirm files were copied into the Steam game installation directory, not the repository directory.
3. Look for the grey **AI** tab on the right screen edge and click it directly.

### Q2: Autoplay stopped unexpectedly. How do I resume?
1. Read the **AI Teammate** status line and the suggested next click. Config errors do not retry forever; fix settings, test, then **Resume play** without restarting the whole game.
2. If tokens show **unknown**, the provider omitted usage; that is not a zero spend. Request caps still work.
3. Use **Export diagnostics**. The copy excludes API keys, Authorization headers, and session tokens, and does not include chat bodies by default.

### Q3: Dual-instance companion window fails to start.
1. Local co-op runs through the internal multiplayer test lobby.
2. The launcher sets `--force-steam off` and steps `clientId` automatically. Check if third-party antivirus software blocked launching the child process.
3. If ports are in use, the mod automatically selects an available fallback port.

---

## 📁 Repository Layout

```text
STS2-Agent/
├── STS2AIAgent/          # C# In-Game Mod (Overlay UI, LLM Client, Decision Loop, Budget Guard)
├── STS2AIAgent.Tests/    # Standalone C# tests (no game client required)
├── mcp_server/           # FastMCP Server implementation
├── scripts/              # Build, packaging, startup, and preflight scripts
├── skills/               # State-first gameplay skill specifications
├── docs/                 # Developer reference and API documentation
├── PRODUCT_PLAN_CURRENT.md # Current product status and evidence boundary
└── COOP_DELIVERY.md      # Historical co-op evidence index
```

---

## License

This project is licensed under the [GNU Affero General Public License v3.0 (AGPL-3.0)](./LICENSE).
