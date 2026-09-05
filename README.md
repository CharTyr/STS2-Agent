# STS2 AI Agent

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

中文版说明请见 [README.zh-CN.md](./README.zh-CN.md).

`STS2 AI Agent` is a Slay the Spire 2 mod with an in-game AI overlay. After you install the mod, you can configure model endpoints, chat, set per-model thinking intensity, let the model play, optionally attach vision, and launch a local second instance for co-op with the model.

The local HTTP API and MCP server remain available for developers and external clients.

- `STS2AIAgent`: in-game overlay + local HTTP API (`http://127.0.0.1:8080` by default)
- `mcp_server`: optional MCP wrapper around that API

Detailed MCP tool documentation lives in [mcp_server/README.md](./mcp_server/README.md). The in-game play loop follows the same state-first rules as [skills/sts2-mcp-player/SKILL.md](./skills/sts2-mcp-player/SKILL.md).

## Quick Start (Players)

### 1. Install The Mod

After downloading and extracting the release package, copy these files into your game's `mods/` directory:

```text
STS2AIAgent.dll
STS2AIAgent.pck
mod_id.json
```

The default Steam install path is usually:

```text
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2
```

Your final layout should look like this:

```text
Slay the Spire 2/
  mods/
    STS2AIAgent.dll
    STS2AIAgent.pck
    mod_id.json
```

### 2. Open The In-Game Agent Window

Launch the game normally. Press **F8** (configurable) or the **AI** tab on the right edge.

In the overlay:

1. **Settings**: add one or more OpenAI-compatible endpoints (OpenAI, DeepSeek, SiliconFlow, OpenRouter, Ollama, LM Studio, …), add models, then pick the conversation model. Optionally pick a play model and a vision model. Each model has its own thinking intensity (Off / Low / Medium / High).
2. **Chat**: talk to the model. Enable “attach current state” or “attach screenshot” as needed.
3. **Play**: start auto-play or step once. This uses compact live state and tools, the same contract as MCP. Vision is optional and not required to finish a run.
4. **AI teammate**: invite an AI companion from the main menu. You control your character while the model joins and plays in a second game window. Inviting saves the current model settings; repeated clicks do not launch another companion process.
5. **Connect**: start the optional HTTP MCP server with one click and copy the API / MCP URLs for Cursor, Claude, Codex, or a custom client.

Settings default to `%AppData%/STS2AIAgent/settings.json`. When launching a local companion via the in-game UI, the launcher automatically isolates configuration by deriving and provisioning a companion-specific file (`settings.companion.json` seeded from the main instance) so concurrent settings writes never collide. You can also explicitly assign an isolated configuration file path to any instance by setting the `STS2_AGENT_SETTINGS_PATH` environment variable to an absolute path (or `STS2_COMPANION_SETTINGS_PATH` for the launcher companion override).

Vision is optional extra context. Auto-play works with text-only models: compact `agent_view`, `get_game_state` / `get_available_actions` / `get_game_data_*` / `wait_until_actionable` / `act`. If you assign a vision-capable play model or a vision sidecar, screenshots are attached as supporting context only.

Local dual-instance currently uses the game debug `multiplayer test` lobby. Steam may block a second process. The launcher does not kill your current game.

After inviting a companion, use **AI teammate → Team conversation** in the human window to discuss targets, routes, or the teammate's choices. Replies use the companion's play model, and recent conversation informs future play decisions. Chat is read-only: it never plays the human's cards or resumes a paused companion. Messages wait for an in-progress action to finish and incur additional model requests.

### 3. Optional: Confirm The HTTP API

The overlay does not need a browser. Developers can still open:

```text
http://127.0.0.1:8080/health
```

`/health` reports `api_port`, `instance_role`, and `process_id`. If 8080 and nearby ports are unavailable, the mod tries dynamically allocated loopback ports. Use the actual address shown in the overlay. An explicit `STS2_API_PORT` stays fixed and fails clearly if unavailable.

## Optional: MCP Server (Developers)

MCP is not required for the in-game agent. The overlay **Connect** tab can start HTTP MCP (`http://127.0.0.1:8765/mcp`) if `uv` and the release `mcp_server` folder are present. You can also start it from these scripts:

1. Install `Python 3.11+`
2. Install `uv`

Install `uv` on Windows:

```powershell
powershell -ExecutionPolicy Bypass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

On macOS:

```bash
brew install uv
```

Then start the default `stdio` MCP server.

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-stdio.ps1"
```

macOS / Linux:

```bash
./scripts/start-mcp-stdio.sh
```

If your client works better over HTTP:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
```

Default MCP endpoint: `http://127.0.0.1:8765/mcp`

## What The Project Can Do

- In-game overlay: multi-endpoint / multi-model config, chat, per-model thinking intensity, auto-play, optional screenshot vision, local dual-instance co-op, one-click MCP for external agents
- Reading live game state (compact `agent_view` now includes multiplayer lobby summaries)
- Listing currently legal actions
- Driving combat, rewards, shops, map routing, events, rest sites, chests, capstone selection, and bundle selection
- Optional MCP over `stdio` or HTTP for external agents
- Live game metadata for cards, relics, monsters, potions, and events via the Mod API

See [mcp_server/README.md](./mcp_server/README.md) for the MCP tool surface.

## FAQ

### I installed the mod but there is no window

Press **F8** or click the **AI** tab on the right edge. Confirm `STS2AIAgent.dll`, `STS2AIAgent.pck`, and `mod_id.json` are in the Steam game `mods/` directory.

### `http://127.0.0.1:8080/health` Does Not Open

Check these first:

1. The game is actually running
2. The three mod files are inside the game's `mods/` directory
3. Check the actual API address in the overlay; port conflicts or Windows reserved ranges may require a different port
4. You copied them into the Steam game directory, not the repository directory

### The MCP Server Starts But Cannot Read Game State

That usually means `mcp_server` is running, but the in-game mod is not connected. Confirm `/health` on the actual `api_port`.

### Should I Enable Debug Actions?

Usually no. Dual-instance from the overlay opens the debug multiplayer test scene internally and does not require you to expose `run_console_command` to MCP.

## Building From Source

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release
```

macOS / Linux:

```bash
./scripts/build-mod.sh --configuration Release
```

Core unit tests (no game required):

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
```

More complete environment, path-discovery, and validation notes are in [build-and-env.md](./build-and-env.md).

## Repository Layout

- `STS2AIAgent/`: game mod source (overlay, LLM client, agent loop, HTTP API)
- `STS2AIAgent.Tests/`: unit tests for settings, LLM JSON, and the agent loop
- `mcp_server/`: optional MCP server source
- `scripts/`: startup, build, and validation scripts
- `docs/`: supporting documentation
- `skills/`: companion skills for MCP clients

## License

This project is licensed under the GNU Affero General Public License v3.0 only (AGPL-3.0-only).
