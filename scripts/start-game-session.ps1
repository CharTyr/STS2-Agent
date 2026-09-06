param(
    [string]$ExePath = "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.exe",
    [int]$Attempts = 180,
    [int]$DelaySeconds = 1,
    [switch]$EnableDebugActions,
    [int]$ApiPort = 8080,
    [switch]$KeepExistingProcesses,
    [switch]$Headless,
    [switch]$ViaSteam,
    [string]$SteamExe = "",
    [string]$SteamAppId = "2868840",
    [string]$ExtraArguments = ""
)

$ErrorActionPreference = "Stop"

function Wait-ForHealth {
    param(
        [int]$MaxAttempts,
        [int]$SleepSeconds,
        [System.Diagnostics.Process]$Process,
        [string]$BaseUrl
    )

    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        if (($i % 5) -eq 0) {
            Write-Host "[start-game-session] waiting for /health on $BaseUrl (attempt $($i + 1)/$MaxAttempts)"
        }

        Start-Sleep -Seconds $SleepSeconds

        try {
            $null = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd("/") + "/health") -TimeoutSec 2
            return
        } catch {
        }

        if ($Process -and $Process.HasExited -and -not (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue)) {
            throw "Game process exited before /health became ready."
        }
    }

    try {
        $null = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd("/") + "/health") -TimeoutSec 2
        return
    } catch {
    }

    throw "Timed out waiting for /health."
}

function Wait-ForStateReady {
    param(
        [int]$MaxAttempts,
        [int]$SleepSeconds,
        [System.Diagnostics.Process]$Process,
        [string]$BaseUrl
    )

    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        if (($i % 5) -eq 0) {
            Write-Host "[start-game-session] waiting for /state on $BaseUrl (attempt $($i + 1)/$MaxAttempts)"
        }

        Start-Sleep -Seconds $SleepSeconds

        try {
            $payload = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd("/") + "/state") -TimeoutSec 2
            if ($null -ne $payload.data -and -not [string]::IsNullOrWhiteSpace([string]$payload.data.screen)) {
                return
            }
        } catch {
        }

        if ($Process -and $Process.HasExited -and -not (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue)) {
            throw "Game process exited before /state became ready."
        }
    }

    try {
        $payload = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd("/") + "/state") -TimeoutSec 2
        if ($null -ne $payload.data -and -not [string]::IsNullOrWhiteSpace([string]$payload.data.screen)) {
            return
        }
    } catch {
    }

    throw "Timed out waiting for /state."
}

function Wait-ForPortRelease {
    param(
        [int]$MaxAttempts,
        [int]$SleepSeconds,
        [int]$Port
    )

    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        try {
            $listenerActive = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop).Count -gt 0
        } catch {
            $listenerActive = $false
        }

        if (-not $listenerActive) {
            return
        }

        Start-Sleep -Seconds $SleepSeconds
    }
}

$baseUrl = "http://127.0.0.1:$ApiPort"
$launchDir = Split-Path -Parent $ExePath

if (-not $KeepExistingProcesses) {
    $existing = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-Process -Id $existing.Id -Force
        Start-Sleep -Seconds 2
        Wait-ForPortRelease -MaxAttempts 10 -SleepSeconds 1 -Port $ApiPort
    }
}

function Resolve-SteamExe {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and (Test-Path -LiteralPath $ExplicitPath)) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Steam\steam.exe",
        "$env:ProgramFiles\Steam\steam.exe",
        "C:\Program Files (x86)\Steam\steam.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Steam executable not found. Pass -SteamExe."
}

function Get-ExtraArgumentList {
    param([string]$Raw)

    if ([string]::IsNullOrWhiteSpace($Raw)) {
        return @()
    }

    return @($Raw -split "\s+" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$previousDebugValue = [Environment]::GetEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS", "Process")
$previousPortValue = [Environment]::GetEnvironmentVariable("STS2_API_PORT", "Process")
$proc = $null

try {
    [Environment]::SetEnvironmentVariable("STS2_API_PORT", [string]$ApiPort, "Process")
    if ($EnableDebugActions) {
        [Environment]::SetEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS", "1", "Process")
    }
    else {
        [Environment]::SetEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS", $null, "Process")
    }

    $extraArgs = Get-ExtraArgumentList -Raw $ExtraArguments
    if ($ViaSteam) {
        $steamExe = Resolve-SteamExe -ExplicitPath $SteamExe
        $steamArgs = @("-applaunch", $SteamAppId) + $extraArgs
        Write-Host "[start-game-session] launching via Steam: $steamExe $($steamArgs -join ' ')"
        $null = Start-Process -FilePath $steamExe -ArgumentList $steamArgs
        $deadline = (Get-Date).AddSeconds([Math]::Max(30, $Attempts * $DelaySeconds))
        while ((Get-Date) -lt $deadline) {
            $proc = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
            if ($proc) {
                break
            }
            Start-Sleep -Seconds $DelaySeconds
        }
        if (-not $proc) {
            throw "Steam launch did not start SlayTheSpire2.exe."
        }
        Write-Host "[start-game-session] Steam started SlayTheSpire2 PID $($proc.Id)"
    }
    else {
        $argumentList = @()
        if ($Headless) {
            $argumentList += "--headless"
        }
        $argumentList += $extraArgs

        $startParams = @{
            FilePath = $ExePath
            WorkingDirectory = $launchDir
            PassThru = $true
        }
        if ($argumentList.Count -gt 0) {
            $startParams.ArgumentList = $argumentList
            Write-Host "[start-game-session] arguments: $($argumentList -join ' ')"
        }

        $proc = Start-Process @startParams
        if ($proc.HasExited) {
            $relaunched = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
            if ($relaunched) {
                Write-Host "[start-game-session] original PID exited; tracking relaunched PID $($relaunched.Id)"
                $proc = $relaunched
            }
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable("STS2_API_PORT", $previousPortValue, "Process")
    [Environment]::SetEnvironmentVariable("STS2_ENABLE_DEBUG_ACTIONS", $previousDebugValue, "Process")
}

Wait-ForHealth -MaxAttempts $Attempts -SleepSeconds $DelaySeconds -Process $proc -BaseUrl $baseUrl
Wait-ForStateReady -MaxAttempts $Attempts -SleepSeconds 1 -Process $proc -BaseUrl $baseUrl

[pscustomobject]@{
    pid = $proc.Id
    debug_actions_enabled = [bool]$EnableDebugActions
    api_port = $ApiPort
    base_url = $baseUrl
    health = "ready"
} | ConvertTo-Json -Compress
