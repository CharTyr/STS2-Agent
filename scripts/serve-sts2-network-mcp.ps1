param(
    [string]$SpRepoRoot = "",
    [string]$BindHost = "0.0.0.0",
    [int]$Port = 8765,
    [ValidateSet("streamable-http", "http", "sse")]
    [string]$Transport = "streamable-http",
    [string]$Path = "/mcp",
    [ValidateSet("guided", "full", "legacy")]
    [string]$ToolProfile = "guided",
    [string]$ApiBaseUrl = "http://127.0.0.1:8080",
    [string]$BearerToken = $env:STS2_NETWORK_BEARER_TOKEN,
    [switch]$StatelessHttp,
    [switch]$JsonResponse
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($SpRepoRoot)) {
    $SpRepoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
} else {
    $SpRepoRoot = (Resolve-Path $SpRepoRoot).Path
}

$pythonExe = Join-Path $SpRepoRoot "mcp_server/.venv/Scripts/python.exe"
if (-not (Test-Path $pythonExe)) {
    throw "Python virtualenv not found: $pythonExe"
}

$srcPath = Join-Path $SpRepoRoot "mcp_server/src"
$env:PYTHONPATH = if ([string]::IsNullOrWhiteSpace($env:PYTHONPATH)) {
    $srcPath
} else {
    "$srcPath;$env:PYTHONPATH"
}
$env:STS2_API_BASE_URL = $ApiBaseUrl

$arguments = @(
    "-m", "sts2_mcp.network_server",
    "--host", $BindHost,
    "--port", $Port.ToString(),
    "--transport", $Transport,
    "--path", $Path,
    "--tool-profile", $ToolProfile,
    "--api-base-url", $ApiBaseUrl
)

if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
    $arguments += @("--bearer-token", $BearerToken)
}
if ($StatelessHttp.IsPresent) {
    $arguments += "--stateless-http"
}
if ($JsonResponse.IsPresent) {
    $arguments += "--json-response"
}

Write-Host "Starting STS2 network MCP server on http://$BindHost:$Port$Path"
Write-Host "Transport: $Transport | Tool profile: $ToolProfile | Auth: $([bool](-not [string]::IsNullOrWhiteSpace($BearerToken)))"

& $pythonExe @arguments
exit $LASTEXITCODE
