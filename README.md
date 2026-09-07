# STS2 AI Agent

<div align="center">

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

**In-Game AI Companion & Autonomous Gameplay Mod for Slay the Spire 2**

[中文说明 (README.zh-CN)](./README.zh-CN.md) • [Product Plan](./PRODUCT_PLAN_CURRENT.md) • [Co-op Delivery Tracker](./COOP_DELIVERY.md) • [API Docs](./docs/api.md) • [MCP Tools Guide](./mcp_server/README.md)

</div>

---

## 🌟 Key Highlights

- 🎮 **In-Game Overlay UI**: Press **F8** at any time to open the configuration and control window directly inside the game—no external browser required.
- 🤖 **Any OpenAI-Compatible Model**: Works seamlessly with official OpenAI, DeepSeek, SiliconFlow, OpenRouter, Ollama, LM Studio, vLLM, and more. Features per-model configurable thinking intensity.
- 🃏 **Autonomous Auto-Play**: Text-only models can complete full runs (combat, card drafting, shops, events, pathing, capstones). Optional vision model support for screenshot context.
- 👥 **Local Co-op AI Teammate**: One-click launch from the main menu spins up an isolated second game instance. You play your character; the AI teammate plays its own character in co-op mode.
- 💬 **Live Team Conversation**: Talk to your AI teammate in natural language from the human window (e.g., "focus the right cultist", "let's take the shop path"). The AI replies and uses recent context in subsequent decisions.
- 🛡️ **Session Budget Guards & Fault Recovery**: Accurate Token accounting with hard cutoff thresholds prevents runaway API bills. Autoplay automatically breaks after 3 consecutive failures with exponential backoff; halts when leaving the run.
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
1. Start *Slay the Spire 2* normally.
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

---

## 🎮 Core Features

### 1. Autonomous Gameplay (Auto-Play)
- **Compact State Engine**: Highly compressed, actionable representation covering cards, energy, intents, relics, HP, and potion slots. Text-only models can clear full runs.
- **Optional Vision Augmentation**: When using a vision-capable model, screenshots are captured on demand to provide rich visual context.
- **Real-Time Counters**: The overlay displays prompt, completion, total tokens, and request counts live.

### 2. Dual-Instance Local Co-op
- **Zero-Collision Isolation**: Propagates `--force-steam off` and increments `clientId` in offline mode. Automatically derives and clones `settings.companion.json` so both instances never write over each other's configurations or save slots.
- **Team Conversation**:
  - Chat directly with the AI teammate during multiplayer runs.
  - Teammate replies using its play model, and recent discussions inform subsequent play decisions.
  - Read-only safety: The chat interface never plays cards for the human or unpauses a paused companion.

### 3. Interactive In-Game Advisor
- Use the **Chat** tab to ask strategic advice.
- Enable "Attach State" to pass full live game context (deck, relics, route) to the model for tactical guidance.

---

## 🛡️ Reliability & Safety Guards

| Mechanism | Description | Player Benefit |
|---|---|---|
| **Session Budget Guard (`SessionBudgetGuard`)** | Accurate accounting across JSON and streaming SSE; hard configurable caps | Immediate cutoff with visual alerts when budget is reached—no runaway bills |
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
Built-in tools match in-game autoplay: `health_check`, `get_game_state`, `get_available_actions`, `act`, `get_game_data_*`, `wait_until_actionable`.

---

## 🧪 Building From Source & Automated Testing

All core logic can be verified **without running the game client**:

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

### Q4: Which models are recommended?
- **Cloud Models**: DeepSeek-V3 / DeepSeek-R1, OpenAI GPT-4o / o3-mini, Claude 3.5 Sonnet.
- **Local Models**: 7B~14B+ models with strong structured JSON tool-call abilities (e.g. Qwen2.5-7B/14B, Llama-3-8B) via Ollama or LM Studio.

---

## 📁 Repository Layout

```text
STS2-Agent/
├── STS2AIAgent/          # C# In-Game Mod (Overlay UI, LLM Client, Decision Loop, Budget Guard)
├── STS2AIAgent.Tests/    # Standalone C# tests (no game client required)
├── mcp_server/           # FastMCP Server implementation and offline game data
├── scripts/              # Build, packaging, startup, and preflight scripts
├── skills/               # State-first gameplay skill specifications
├── docs/                 # Developer reference and API documentation
├── PRODUCT_PLAN_CURRENT.md # Official product plan & remote baseline assessment
└── COOP_DELIVERY.md      # Co-op companion full delivery tracking
```

---

## License

This project is licensed under the [GNU Affero General Public License v3.0 (AGPL-3.0)](./LICENSE).
