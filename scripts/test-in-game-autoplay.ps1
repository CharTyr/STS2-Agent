param(
    [int]$ApiPort = 18097,
    [double]$TimeoutSeconds = 90.0,
    [switch]$Headless,
    [string]$LiveBaseUrl = "",
    [string]$LiveModel = "",
    [string]$LiveApiKeyEnv = "",
    [int]$MaxSessionRequests = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$probeScript = Join-Path $PSScriptRoot "in_game_autoplay_probe.py"
$pythonArgs = @(
    $probeScript,
    "--api-port", "$ApiPort",
    "--timeout", "$TimeoutSeconds",
    "--max-session-requests", "$MaxSessionRequests"
)

if ($Headless) {
    $pythonArgs += "--headless"
}

if (-not [string]::IsNullOrWhiteSpace($LiveBaseUrl)) {
    $pythonArgs += @("--live-base-url", $LiveBaseUrl)
}

if (-not [string]::IsNullOrWhiteSpace($LiveModel)) {
    $pythonArgs += @("--live-model", $LiveModel)
}

if (-not [string]::IsNullOrWhiteSpace($LiveApiKeyEnv)) {
    $pythonArgs += @("--live-api-key-env", $LiveApiKeyEnv)
}

& python @pythonArgs
exit $LASTEXITCODE
