# STS2 Remote MCP

This wrapper lets a remote host reach the local Windows STS2 game through Tailscale without changing the game mod itself.

## Topology

- Windows game machine: run the game, the `STS2AIAgent` mod, and the network wrapper from this repo.
- Remote MaiBot host: connect to the wrapper over Tailscale with a normal HTTP MCP entry.
- SubAgent: keep one `play_sts2` skill and let `mcp_server_candidates` prefer local first and remote second.

## Windows Start Command

Use the PowerShell launcher inside `sp/scripts`:

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/serve-sts2-network-mcp.ps1" `
  -SpRepoRoot "<repo-root>" `
  -BindHost "0.0.0.0" `
  -Port 8765 `
  -Transport "streamable-http" `
  -ToolProfile "guided" `
  -BearerToken "replace-with-a-long-random-token"
```

The wrapper exposes:

- `GET /healthz`
- `GET|POST|DELETE /mcp` when using `streamable-http`

## Direct Python Entry

You can also run the server directly:

```powershell
cd "<repo-root>/mcp_server"
$env:PYTHONPATH = "<repo-root>/mcp_server/src"
python -m sts2_mcp.network_server --host 0.0.0.0 --port 8765 --transport streamable-http --path /mcp --tool-profile guided --bearer-token "replace-with-a-long-random-token"
```

## Health Check

Local machine:

```powershell
Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8765/healthz"
```

Remote Tailscale host:

```powershell
Invoke-WebRequest -UseBasicParsing "http://WINDOWS_TAILSCALE_IP:8765/healthz"
```

If the wrapper is up but the STS2 mod is not reachable, `/healthz` returns `503` with the inner error payload.

## MCPBridge Example

Configure the remote entry on the MaiBot host with the standard name:

```json
{
  "mcpServers": {
    "sts2-ai-agent-remote": {
      "enabled": true,
      "transport": "streamable_http",
      "url": "http://WINDOWS_TAILSCALE_IP:8765/mcp",
      "headers": {
        "Authorization": "Bearer replace-with-a-long-random-token"
      }
    }
  }
}
```

## SubAgent Pairing

Keep the SubAgent shortcut config simple:

```toml
[sts2]
enabled = true
local_server_name = "sts2-ai-agent"
remote_server_name = "sts2-ai-agent-remote"
```

That keeps one STS2 skill and lets the plugin prefer local MCP when present, then fall back to Tailscale remote.
