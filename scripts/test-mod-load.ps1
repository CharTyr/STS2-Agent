param(
    [string]$ExePath = "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.exe",
    [string]$AppManifestPath = "C:/Program Files (x86)/Steam/steamapps/appmanifest_2868840.acf",
    [string]$AppId = "",
    [int]$Attempts = 180,
    [int]$DelaySeconds = 1,
    [switch]$DeepCheck,
    [switch]$EnableDebugActions,
    [int]$ApiPort = 8080
)

$ErrorActionPreference = "Stop"

function Resolve-AppId {
    param(
        [string]$ExplicitAppId,
        [string]$ManifestPath
    )

    if ($ExplicitAppId) {
        return $ExplicitAppId
    }

    if (-not (Test-Path $ManifestPath)) {
        throw "Steam app manifest not found: $ManifestPath"
    }

    $manifest = Get-Content -Path $ManifestPath -Raw
    $match = [regex]::Match($manifest, '"appid"\s+"(?<appid>\d+)"')

    if (-not $match.Success) {
        throw "Unable to resolve appid from manifest: $ManifestPath"
    }

    return $match.Groups["appid"].Value
}

function Get-FailureHint {
    param(
        [string]$LogPath
    )

    if (-not (Test-Path $LogPath)) {
        return $null
    }

    $logTail = (Get-Content -Path $LogPath -Tail 200) -join "`n"

    if ($logTail -match "user has not yet seen the mods warning") {
        return "The game exited after showing the first-time mod loading consent. Run the script one more time."
    }

    return $null
}

function Invoke-JsonEndpoint {
    param(
        [string]$Uri
    )

    return [pscustomobject]@{
        StatusCode = 200
        Json = Invoke-RestMethod -Uri $Uri -TimeoutSec 2
    }
}

$gameRoot = Split-Path -Path $ExePath -Parent
$appIdFile = Join-Path $gameRoot "steam_appid.txt"
$logPath = Join-Path $env:APPDATA "SlayTheSpire2/logs/godot.log"
$resolvedAppId = Resolve-AppId -ExplicitAppId $AppId -ManifestPath $AppManifestPath
$stateCheck = $null
$actionsCheck = $null
$proc = $null
$baseUrl = "http://127.0.0.1:$ApiPort"
$startSessionScript = Join-Path $PSScriptRoot "start-game-session.ps1"

if (-not (Test-Path $appIdFile)) {
    Set-Content -Path $appIdFile -Value $resolvedAppId -Encoding ascii -NoNewline
    Write-Host "[test-mod-load] Created steam_appid.txt with appid $resolvedAppId"
} else {
    $existingAppId = (Get-Content -Path $appIdFile -Raw).Trim()

    if ($existingAppId -ne $resolvedAppId) {
        Write-Warning "[test-mod-load] Existing steam_appid.txt contains '$existingAppId', expected '$resolvedAppId'."
    }
}

$health = $null

try {
    $startArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $startSessionScript,
        "-ExePath", $ExePath,
        "-Attempts", $Attempts,
        "-DelaySeconds", $DelaySeconds,
        "-ApiPort", $ApiPort
    )

    if ($EnableDebugActions) {
        $startArgs += "-EnableDebugActions"
    }

    $sessionOutput = @(powershell @startArgs)
    $sessionJson = $sessionOutput | Select-Object -Last 1
    $session = $sessionJson | ConvertFrom-Json
    $proc = Get-Process -Id $session.pid -ErrorAction Stop
    $health = $session.health

    if ($DeepCheck) {
        $stateCheck = Invoke-JsonEndpoint -Uri ($baseUrl + "/state")
        $actionsCheck = Invoke-JsonEndpoint -Uri ($baseUrl + "/actions/available")
    }
}
finally {
    if ($null -ne $proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}

if ($health) {
    if ($DeepCheck) {
        [pscustomobject]@{
            health_ok = $true
            state_ok = $stateCheck -ne $null -and $stateCheck.StatusCode -eq 200
            actions_ok = $actionsCheck -ne $null -and $actionsCheck.StatusCode -eq 200
            screen = $stateCheck.Json.data.screen
            available_action_count = @($actionsCheck.Json.data.actions).Count
        } | ConvertTo-Json -Compress
    } else {
        $health
    }
} else {
    $hint = Get-FailureHint -LogPath $logPath

    if ($hint) {
        Write-Warning "[test-mod-load] $hint"
    }

    "NO_HEALTH_RESPONSE"
}
