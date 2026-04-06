# STS2 AI Agent

https://github.com/user-attachments/assets/89353468-a299-4315-9516-e520bcbfbd4b

中文版说明请见 [README.zh-CN.md](./README.zh-CN.md).

`STS2 AI Agent` is a Slay the Spire 2 mod + MCP server bundle:

- `STS2AIAgent`: exposes game state and actions through a local HTTP API
- `mcp_server`: wraps that local API as an MCP server for AI clients

## What's New In `v0.5.4`

This release adds:

- run-time Ascension data in `RunPayload` via `run.ascension` and `run.ascension_effects[]`
- compact agent-view Ascension visibility through `agent_view.run.ascension`
- a fix for reward-screen potion discard exposure: `discard_potion` is no longer exposed on reward / card-reward screens
- updated regression checks covering both Ascension payloads and reward-screen potion discard gating

Detailed MCP tool documentation lives in [mcp_server/README.md](./mcp_server/README.md). If you want an agent workflow on top of it, start with [skills/sts2-mcp-player/SKILL.md](./skills/sts2-mcp-player/SKILL.md).

## Quick Start

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

### 2. Start The Game And Confirm The Mod Is Loaded

Launch the game normally so the mod can load with it.

Then open:

```text
http://127.0.0.1:8080/health
```

If the endpoint responds, the mod is running.

### 3. Start The MCP Server

Prepare the environment first:

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

This is the recommended default. Most desktop AI clients prefer `stdio` MCP integration.

### 4. Connect Your MCP Client

If your client supports command-based MCP startup, point its working directory at `mcp_server/` and use:

```text
uv run sts2-mcp-server
```

If your client works better over HTTP, start the network server instead.

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\start-mcp-network.ps1"
```

macOS / Linux:

```bash
./scripts/start-mcp-network.sh
```

Default MCP endpoint:

```text
http://127.0.0.1:8765/mcp
```

## What The Project Can Do

The current `main` branch provides a playable MCP integration for STS2, including:

- reading live game state
- listing currently legal actions
- driving combat, rewards, shops, map routing, events, rest sites, and chests
- reducing polling through SSE events
- exposing MCP over `stdio` or HTTP
- serving bundled game metadata for cards, relics, monsters, potions, and events
- supporting layered planner / combat agent handoff flows

See [mcp_server/README.md](./mcp_server/README.md) for the detailed tool surface.

## FAQ

### `http://127.0.0.1:8080/health` Does Not Open

Check these first:

1. The game is actually running
2. `STS2AIAgent.dll`, `STS2AIAgent.pck`, and `mod_id.json` are all inside the game's `mods/` directory
3. The files were not duplicated or renamed by the OS
4. You copied them into the Steam game directory, not the repository directory

### The MCP Server Starts But Cannot Read Game State

That usually means `mcp_server` is running, but the in-game mod is not connected. Confirm:

1. The game is running
2. `http://127.0.0.1:8080/health` is reachable
3. The MCP server is still pointing at `http://127.0.0.1:8080`

### Should I Enable Debug Actions?

Usually no.

Developer-only actions such as `run_console_command` are disabled by default and should stay disabled in normal use and releases.

## Building From Source

If you are building from source instead of using a release package:

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File ".\scripts\build-mod.ps1" -Configuration Release
```

macOS / Linux:

```bash
./scripts/build-mod.sh --configuration Release
```

More complete environment, path-discovery, and validation notes are in [build-and-env.md](./build-and-env.md).

## Repository Layout

- `STS2AIAgent/`: game mod source
- `mcp_server/`: MCP server source
- `scripts/`: startup, build, and validation scripts
- `docs/`: supporting documentation
- `skills/`: companion skills

## License

This project is licensed under the GNU Affero General Public License v3.0 only (AGPL-3.0-only).
