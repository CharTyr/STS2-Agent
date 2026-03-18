param(
    [string]$RepoRoot = "",
    [Alias("Host")]
    [string]$BindHost = "127.0.0.1",
    [int]$Port = 8765,
    [string]$Path = "/mcp",
    [string]$ApiBaseUrl = "http://127.0.0.1:8080"
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

function Resolve-RepoRoot {
    param([string]$InputRoot)

    if ([string]::IsNullOrWhiteSpace($InputRoot)) {
        return (Resolve-Path (Join-Path $scriptRoot "..")).Path
    }

    return (Resolve-Path $InputRoot).Path
}

$resolvedRepoRoot = Resolve-RepoRoot -InputRoot $RepoRoot
$mcpRoot = Join-Path $resolvedRepoRoot "mcp_server"

if (-not (Test-Path $mcpRoot)) {
    throw "mcp_server directory not found: $mcpRoot"
}

if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    throw "uv is not installed or not available in PATH."
}

Push-Location $mcpRoot
try {
    Write-Host "[start-mcp-network] Syncing dependencies..."
    uv sync | Out-Host

    Write-Host "[start-mcp-network] Starting MCP server on http://$BindHost`:$Port$Path"
    uv run sts2-network-mcp-server --host $BindHost --port $Port --path $Path --api-base-url $ApiBaseUrl
}
finally {
    Pop-Location
}
