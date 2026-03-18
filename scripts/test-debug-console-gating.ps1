param(
    [string]$ExePath = "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.exe",
    [int]$Attempts = 40,
    [int]$DelaySeconds = 2,
    [string]$Command = "help",
    [string]$ProjectRoot = "",
    [switch]$EnableDebugActions
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

function Wait-ForHealth {
    param(
        [int]$MaxAttempts,
        [int]$SleepSeconds,
        [System.Diagnostics.Process]$Process
    )

    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        Start-Sleep -Seconds $SleepSeconds

        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:8080/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return
            }
        } catch {
        }

        if ($Process.HasExited) {
            throw "Game process exited before /health became ready."
        }
    }

    throw "Timed out waiting for /health."
}

function Invoke-ActionJson {
    param(
        [string]$ActionName,
        [string]$ConsoleCommand
    )

    $body = @{
        action = $ActionName
        command = $ConsoleCommand
    } | ConvertTo-Json

    try {
        $response = Invoke-WebRequest -Method Post -Uri "http://127.0.0.1:8080/action" -ContentType "application/json" -Body $body -UseBasicParsing -TimeoutSec 5
        return $response.Content | ConvertFrom-Json
    } catch {
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            return $_.ErrorDetails.Message | ConvertFrom-Json
        }

        if ($_.Exception.Response -and $_.Exception.Response.GetResponseStream()) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $content = $reader.ReadToEnd()
            if ($content) {
                return $content | ConvertFrom-Json
            }
        }

        throw
    }
}

function Invoke-McpDebugSmoke {
    param(
        [string]$RepoRoot,
        [string]$ConsoleCommand
    )

    $mcpRoot = Join-Path $RepoRoot "mcp_server"
    $previousCommandEnv = $env:STS2_DEBUG_TEST_COMMAND
    $env:STS2_DEBUG_TEST_COMMAND = $ConsoleCommand

    try {
        Push-Location $mcpRoot

        try {
            $pythonScript = @'
import asyncio
import json
import os

from sts2_mcp.client import Sts2Client
from sts2_mcp.server import create_server


class CapturingClient(Sts2Client):
    def __init__(self) -> None:
        super().__init__(base_url="http://127.0.0.1:8080")
        self.last_request = None

    def _request(self, method, path, payload=None, *, is_action=False):
        self.last_request = {
            "method": method,
            "path": path,
            "payload": payload,
            "is_action": is_action,
        }
        return {"ok": True}


async def main() -> None:
    server = create_server()
    tools = [tool.name for tool in await server.list_tools()]
    client = CapturingClient()
    client_error = None

    try:
        client.run_console_command(os.environ.get("STS2_DEBUG_TEST_COMMAND", "help"))
    except Exception as exc:
        client_error = {
            "type": type(exc).__name__,
            "message": str(exc),
        }

    print(json.dumps({
        "tool_registered": "run_console_command" in tools,
        "tool_count": len(tools),
        "client_error": client_error,
        "client_request": client.last_request,
    }, ensure_ascii=False))


asyncio.run(main())
'@

            return ($pythonScript | uv run python - | ConvertFrom-Json)
        }
        finally {
            Pop-Location
        }
    }
    finally {
        if ($null -eq $previousCommandEnv) {
            Remove-Item Env:STS2_DEBUG_TEST_COMMAND -ErrorAction SilentlyContinue
        } else {
            $env:STS2_DEBUG_TEST_COMMAND = $previousCommandEnv
        }
    }
}

$previousEnv = $env:STS2_ENABLE_DEBUG_ACTIONS

if ($EnableDebugActions) {
    [System.Environment]::SetEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS", "1", "Process")
    $env:STS2_ENABLE_DEBUG_ACTIONS = "1"
} else {
    [System.Environment]::SetEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS", $null, "Process")
    Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue
}

$existing = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Process -Id $existing.Id -Force
    Start-Sleep -Seconds 2
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $ExePath
$startInfo.UseShellExecute = $false

if ($EnableDebugActions) {
    $startInfo.EnvironmentVariables["STS2_ENABLE_DEBUG_ACTIONS"] = "1"
} else {
    $startInfo.EnvironmentVariables.Remove("STS2_ENABLE_DEBUG_ACTIONS")
}

$proc = [System.Diagnostics.Process]::Start($startInfo)

try {
    $mcpResult = Invoke-McpDebugSmoke -RepoRoot $ProjectRoot -ConsoleCommand $Command

    if ($EnableDebugActions) {
        if (-not $mcpResult.tool_registered) {
            throw "Expected MCP debug tool to be registered when STS2_ENABLE_DEBUG_ACTIONS=1."
        }
    } else {
        if ($mcpResult.tool_registered) {
            throw "Expected MCP debug tool to stay hidden while STS2_ENABLE_DEBUG_ACTIONS is disabled."
        }
    }

    if ($null -ne $mcpResult.client_error) {
        throw "Expected MCP client run_console_command wiring to succeed, but received: $($mcpResult.client_error | ConvertTo-Json -Depth 5 -Compress)"
    }

    if ($null -eq $mcpResult.client_request -or
        $mcpResult.client_request.payload.action -ne "run_console_command" -or
        $mcpResult.client_request.payload.command -ne $Command) {
        throw "Expected MCP client payload to contain action=run_console_command and the requested command, but received: $($mcpResult | ConvertTo-Json -Depth 6 -Compress)"
    }

    Wait-ForHealth -MaxAttempts $Attempts -SleepSeconds $DelaySeconds -Process $proc
    $result = Invoke-ActionJson -ActionName "run_console_command" -ConsoleCommand $Command

    if ($EnableDebugActions) {
        if (-not $result.ok -or $result.data.status -ne "completed") {
            throw "Expected debug command to succeed, but received: $($result | ConvertTo-Json -Depth 6 -Compress)"
        }
    } else {
        if ($result.ok -or $result.error.code -ne "invalid_action") {
            throw "Expected invalid_action while debug actions are disabled, but received: $($result | ConvertTo-Json -Depth 6 -Compress)"
        }
    }

    [pscustomobject]@{
        debug_actions_enabled = [bool]$EnableDebugActions
        ok = $result.ok
        status = $result.data.status
        error_code = $result.error.code
        message = if ($result.ok) { $result.data.message } else { $result.error.message }
        mcp_tool_registered = [bool]$mcpResult.tool_registered
        mcp_client_payload_ok = $null -eq $mcpResult.client_error
    } | ConvertTo-Json -Compress
}
finally {
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }

    [System.Environment]::SetEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS", $previousEnv, "Process")

    if ($null -eq $previousEnv) {
        Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue
    } else {
        $env:STS2_ENABLE_DEBUG_ACTIONS = $previousEnv
    }
}
