param(
    [string]$ExePath = "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.exe",
    [string]$AppManifestPath = "C:/Program Files (x86)/Steam/steamapps/appmanifest_2868840.acf",
    [string]$AppId = "",
    [int]$Attempts = 15,
    [int]$DelaySeconds = 2,
    [switch]$DeepCheck
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

    $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2
    $content = $response.Content

    return [pscustomobject]@{
        StatusCode = $response.StatusCode
        Content = $content
        Json = $content | ConvertFrom-Json
    }
}

$gameRoot = Split-Path -Path $ExePath -Parent
$appIdFile = Join-Path $gameRoot "steam_appid.txt"
$logPath = Join-Path $env:APPDATA "SlayTheSpire2/logs/godot.log"
$resolvedAppId = Resolve-AppId -ExplicitAppId $AppId -ManifestPath $AppManifestPath
$stateCheck = $null
$actionsCheck = $null

if (-not (Test-Path $appIdFile)) {
    Set-Content -Path $appIdFile -Value $resolvedAppId -Encoding ascii -NoNewline
    Write-Host "[test-mod-load] Created steam_appid.txt with appid $resolvedAppId"
} else {
    $existingAppId = (Get-Content -Path $appIdFile -Raw).Trim()

    if ($existingAppId -ne $resolvedAppId) {
        Write-Warning "[test-mod-load] Existing steam_appid.txt contains '$existingAppId', expected '$resolvedAppId'."
    }
}

$proc = Start-Process -FilePath $ExePath -PassThru
$health = $null

try {
    for ($i = 0; $i -lt $Attempts; $i++) {
        Start-Sleep -Seconds $DelaySeconds

        try {
            $resp = Invoke-WebRequest -Uri "http://127.0.0.1:8080/health" -UseBasicParsing -TimeoutSec 2
            $health = $resp.Content
            break
        } catch {
        }

        if ($proc.HasExited) {
            break
        }
    }

    if ($health -and $DeepCheck) {
        $stateCheck = Invoke-JsonEndpoint -Uri "http://127.0.0.1:8080/state"
        $actionsCheck = Invoke-JsonEndpoint -Uri "http://127.0.0.1:8080/actions/available"
    }
}
finally {
    if (-not $proc.HasExited) {
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
