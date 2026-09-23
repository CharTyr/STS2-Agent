param(
    [string]$ExePath = "",
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
. (Join-Path $PSScriptRoot "lib-sts2-paths.ps1")

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
            return $true
        }

        Start-Sleep -Seconds $SleepSeconds
    }

    return $false
}

function Resolve-SteamExe {
    param([string]$ExplicitPath)

    $resolved = Get-Sts2SteamExe -Explicit $ExplicitPath
    if ($resolved) {
        return $resolved
    }

    throw "Steam executable not found. Pass -SteamExe or set STS2_STEAM_EXE."
}

function Get-ExtraArgumentList {
    param([string]$Raw)

    if ([string]::IsNullOrWhiteSpace($Raw)) {
        return @()
    }

    return @($Raw -split "\s+" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-ClientIdFromArguments {
    param([string[]]$Arguments)

    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        if ($Arguments[$i] -eq "--clientId" -and ($i + 1) -lt $Arguments.Count) {
            return [string]$Arguments[$i + 1]
        }
        if ($Arguments[$i] -like "--clientId=*") {
            return [string]($Arguments[$i].Substring("--clientId=".Length))
        }
    }

    return $null
}

function Test-IsolatedSettingsTextReady {
    param([string]$Raw)

    # The agent entry only counts inside the mod_settings object: a stray mods_enabled or
    # mod_list at another level leaves the game reading mod_settings as null.
    $shape = Get-IsolatedModSettingsBody -Raw $Raw
    if ($null -eq $shape) {
        return $false
    }
    $body = $shape.Body
    if ($body -notmatch '"mods_enabled"\s*:\s*true') {
        return $false
    }
    # Scope the flag to the agent's own objects, and check all of them. A player who subscribed on
    # the Workshop while also keeping a dev copy in mods/ has two STS2AIAgent entries, and the game
    # reads the id as disabled if any one of them says so -- observed live on 2026-09-20, where the
    # clone carried mods_directory=true and steam_workshop=false and the game logged
    # "Skipping loading mod STS2AIAgent, it is set to disabled in settings" twice. Checking only the
    # first entry (or flipping only the first) is what made the whole clone useless in that case.
    $entries = @(Get-IsolatedAgentEntryBodies -Raw $body)
    if ($entries.Count -eq 0) {
        return $false
    }
    foreach ($entry in $entries) {
        if ($entry.Body -match '"is_enabled"\s*:\s*false') {
            return $false
        }
    }
    return $true
}

function Get-IsolatedAgentEntryBodies {
    param([string]$Raw)

    $entries = @()
    $searchFrom = 0
    # A Regex instance with Match(input, startat): the static overload that takes an offset also
    # wants a RegexOptions and a TimeSpan, and PowerShell binds the integer as neither.
    $pattern = [regex]::new('"id"\s*:\s*"STS2AIAgent"')
    while ($true) {
        $match = $pattern.Match($Raw, $searchFrom)
        if (-not $match.Success) {
            break
        }

        $open = $Raw.LastIndexOf('{', $match.Index)
        if ($open -ge 0) {
            $depth = 0
            for ($i = $open; $i -lt $Raw.Length; $i++) {
                $ch = $Raw[$i]
                if ($ch -eq '{') { $depth++ }
                elseif ($ch -eq '}') {
                    $depth--
                    if ($depth -eq 0) {
                        $length = $i - $open + 1
                        $entries += [pscustomobject]@{
                            Start = $open
                            Length = $length
                            Body = $Raw.Substring($open, $length)
                        }
                        break
                    }
                }
            }
        }

        $searchFrom = $match.Index + $match.Length
    }

    return $entries
}

function Get-IsolatedModSettingsBody {
    param([string]$Raw)

    $anchor = [regex]::Match($Raw, '"mod_settings"\s*:\s*\{')
    if (-not $anchor.Success) {
        return $null
    }

    $start = $anchor.Index + $anchor.Length - 1
    $depth = 0
    for ($i = $start; $i -lt $Raw.Length; $i++) {
        $ch = $Raw[$i]
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) {
                $length = $i - $start + 1
                return [pscustomobject]@{
                    Start = $start
                    Length = $length
                    Body = $Raw.Substring($start, $length)
                }
            }
        }
    }
    return $null
}

function Repair-IsolatedSettingsText {
    param([string]$Raw)

    $agentJson = '{ "id": "STS2AIAgent", "is_enabled": true, "source": "mods_directory" }'
    $modSettingsJson = @"
{
    "mods_enabled": true,
    "mod_list": [
      $agentJson
    ]
  }
"@

    $patched = $Raw
    if ($patched -match '"mod_settings"\s*:\s*null') {
        return [regex]::Replace($patched, '"mod_settings"\s*:\s*null', ('"mod_settings": ' + $modSettingsJson.Trim()), 1)
    }

    $shape = Get-IsolatedModSettingsBody -Raw $patched
    if ($null -eq $shape) {
        # Nothing to repair inside; the caller re-checks and writes a complete fallback if needed.
        return $patched
    }

    $fixed = [regex]::Replace($shape.Body, '"mods_enabled"\s*:\s*false', '"mods_enabled": true', 1)

    if ($fixed -notmatch '"id"\s*:\s*"STS2AIAgent"') {
        if ($fixed -match '"mod_list"\s*:\s*\[\s*\]') {
            # An empty list must not grow a trailing comma, so this shape inserts the entry alone.
            $fixed = [regex]::Replace($fixed, '("mod_list"\s*:\s*\[\s*)\]', ('$1' + $agentJson + ']'), 1)
        }
        elseif ($fixed -match '"mod_list"\s*:\s*\[') {
            $fixed = [regex]::Replace($fixed, '("mod_list"\s*:\s*\[)', ('$1' + $agentJson + ', '), 1)
        }
        elseif ($fixed -match '^\{\s*\}') {
            $fixed = '{ "mods_enabled": true, "mod_list": [' + $agentJson + '] }'
        }
        else {
            $fixed = [regex]::Replace($fixed, '^(\{\s*)', ('$1"mod_list": [' + $agentJson + '], '), 1)
        }
    }

    if ($fixed -notmatch '"mods_enabled"') {
        if ($fixed -match '^\{\s*\}') {
            $fixed = '{ "mods_enabled": true, "mod_list": [' + $agentJson + '] }'
        }
        else {
            $fixed = [regex]::Replace($fixed, '^(\{\s*)', '$1"mods_enabled": true, ', 1)
        }
    }

    # Flip every agent entry, not just the first. A player who subscribed on the Workshop and also
    # kept a dev copy in mods/ has two STS2AIAgent entries; enabling one of them leaves the game
    # reading the id as disabled (observed live 2026-09-20). Rewriting back-to-front keeps the
    # offsets of the entries still to come valid.
    $entries = @(Get-IsolatedAgentEntryBodies -Raw $fixed)
    for ($index = $entries.Count - 1; $index -ge 0; $index--) {
        $entry = $entries[$index]
        $entryFixed = if ($entry.Body -match '"is_enabled"\s*:\s*(true|false)') {
            [regex]::Replace($entry.Body, '("is_enabled"\s*:\s*)false', '${1}true', 1)
        } else {
            [regex]::Replace($entry.Body, '^(\{\s*)', '$1"is_enabled": true, ', 1)
        }
        $fixed = $fixed.Substring(0, $entry.Start) + $entryFixed + $fixed.Substring($entry.Start + $entry.Length)
    }

    return $patched.Substring(0, $shape.Start) + $fixed + $patched.Substring($shape.Start + $shape.Length)
}

function Get-IsolatedFallbackSettingsJson {
    return @"
{
  "schema_version": 8,
  "mod_settings": {
    "mods_enabled": true,
    "mod_list": [
      { "id": "STS2AIAgent", "is_enabled": true, "source": "mods_directory" }
    ]
  },
  "seen_ea_disclaimer": true,
  "skip_intro_logo": true,
  "fullscreen": false,
  "limit_fps_in_background": false,
  "vsync": "disabled",
  "window_size": { "X": 1280, "Y": 720 },
  "window_position": { "X": -1, "Y": -1 },
  "language": "en",
  "aspect_ratio": "sixteen_by_nine",
  "fps_limit": 60,
  "msaa": 2,
  "resize_windows": true,
  "target_display": 0,
  "volume_ambience": 0.5,
  "volume_bgm": 0.5,
  "volume_master": 0.5,
  "volume_sfx": 0.5,
  "controller_mapping_type": "default",
  "controller_mapping": {},
  "keyboard_mapping": {},
  "keyboard_only_mapping": {}
}
"@
}

function Initialize-IsolatedClientSettings {
    param(
        [string]$ClientId,
        [string]$UserRoot = ""
    )

    if ([string]::IsNullOrWhiteSpace($ClientId)) {
        return
    }

    # The game parses the client id as a number: a value it cannot parse makes it fall back to
    # client 1, so the launcher would seed `default\<id>` while the game read `default\1` -- the mod
    # then loads with whatever that other profile says, or does not load at all. Observed live on
    # 2026-09-20 with `--clientId 20260920v14`: the seeding wrote default\20260920v14 and the game
    # logged "Profile-scoped data path initialized: user://default/1/modded/profile1". Failing here
    # is the difference between a wrong answer and an obvious one.
    if ($ClientId -notmatch '^\d+$') {
        throw "clientId '$ClientId' is not a number. The game falls back to client 1 for a value it cannot parse, which silently validates a different profile. Use digits only, for example --clientId 2026092014."
    }

    if ([string]::IsNullOrWhiteSpace($UserRoot)) {
        # Parameter, then environment, then the platform default -- the same order the game-path
        # resolver uses. A machine that keeps the save data elsewhere points STS2_SLAY_USER_ROOT
        # at it instead of editing this script.
        $UserRoot = if ([string]::IsNullOrWhiteSpace($env:STS2_SLAY_USER_ROOT)) {
            Join-Path $env:APPDATA "SlayTheSpire2"
        } else {
            $env:STS2_SLAY_USER_ROOT
        }
    }

    $dir = Join-Path $UserRoot ("default\" + $ClientId)
    $settingsPath = Join-Path $dir "settings.save"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $encoding = New-Object System.Text.UTF8Encoding $false

    if ((Test-Path -LiteralPath $settingsPath) -and ((Get-Item -LiteralPath $settingsPath).Length -gt 0)) {
        $raw = [System.IO.File]::ReadAllText($settingsPath)
        if (Test-IsolatedSettingsTextReady $raw) {
            return
        }
        $repaired = Repair-IsolatedSettingsText $raw
        if (-not (Test-IsolatedSettingsTextReady $repaired)) {
            # The repair could not make the file usable (unexpected shape). Writing a complete
            # fallback is the difference between "mods load" and "the game silently ignores mods".
            [System.IO.File]::WriteAllText($settingsPath, (Get-IsolatedFallbackSettingsJson).Trim() + [Environment]::NewLine, $encoding)
            Write-Host "[start-game-session] could not repair isolated settings; wrote a complete fallback for clientId $ClientId"
            return
        }
        [System.IO.File]::WriteAllText($settingsPath, $repaired, $encoding)
        Write-Host "[start-game-session] patched isolated settings for clientId $ClientId"
        return
    }

    $steamRoot = Join-Path $UserRoot "steam"
    if (Test-Path -LiteralPath $steamRoot) {
        $template = Get-ChildItem -LiteralPath $steamRoot -Filter "settings.save" -Recurse -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $template) {
            Copy-Item -LiteralPath $template.FullName -Destination $settingsPath -Force
            $raw = [System.IO.File]::ReadAllText($settingsPath)
            $repaired = Repair-IsolatedSettingsText $raw
            if (-not (Test-IsolatedSettingsTextReady $repaired)) {
                $repaired = (Get-IsolatedFallbackSettingsJson).Trim() + [Environment]::NewLine
                Write-Host "[start-game-session] cloned template could not be repaired; wrote a complete fallback for clientId $ClientId"
            }
            else {
                Write-Host "[start-game-session] seeded isolated settings for clientId $ClientId from steam template"
            }
            [System.IO.File]::WriteAllText($settingsPath, $repaired, $encoding)
            return
        }
    }

    [System.IO.File]::WriteAllText($settingsPath, (Get-IsolatedFallbackSettingsJson).Trim() + [Environment]::NewLine, $encoding)
    Write-Host "[start-game-session] seeded isolated settings for clientId $ClientId"
}

if ($MyInvocation.InvocationName -ne '.') {
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Resolve-Sts2Executable
}

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Slay the Spire 2 executable not found at '$ExePath'. Pass -ExePath or set STS2_EXE_PATH."
}

$baseUrl = "http://127.0.0.1:$ApiPort"
$launchDir = Split-Path -Parent $ExePath

if (-not $KeepExistingProcesses) {
    $existing = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-Process -Id $existing.Id -Force
        Start-Sleep -Seconds 2
        # The mod refuses to fall back to another port when STS2_API_PORT is set, so launching
        # while the old listener lingers would start a game with no API (or let /health answer
        # from the old instance). Fail like the POSIX launcher does.
        if (-not (Wait-ForPortRelease -MaxAttempts 10 -SleepSeconds 1 -Port $ApiPort)) {
            throw "Timed out waiting for port $ApiPort to be released before starting a new game session."
        }
    }
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
    Initialize-IsolatedClientSettings -ClientId (Get-ClientIdFromArguments -Arguments $extraArgs)
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

        $settingsPath = [Environment]::GetEnvironmentVariable("STS2_AGENT_SETTINGS_PATH", "Process")
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $ExePath
        $startInfo.WorkingDirectory = $launchDir
        $startInfo.UseShellExecute = $false
        if ($argumentList.Count -gt 0) {
            $quoted = @()
            foreach ($arg in $argumentList) {
                if ($arg -match '\s') { $quoted += '"' + $arg + '"' } else { $quoted += $arg }
            }
            $startInfo.Arguments = [string]::Join(' ', $quoted)
        }
        foreach ($entry in [Environment]::GetEnvironmentVariables("Process").GetEnumerator()) {
            $startInfo.Environment[$entry.Key] = [string]$entry.Value
        }
        $startInfo.Environment["STS2_API_PORT"] = [string]$ApiPort
        if ($EnableDebugActions) { $startInfo.Environment["STS2_ENABLE_DEBUG_ACTIONS"] = "1" }
        if (-not [string]::IsNullOrWhiteSpace($settingsPath)) {
            $startInfo.Environment["STS2_AGENT_SETTINGS_PATH"] = $settingsPath
            Write-Host "[start-game-session] STS2_AGENT_SETTINGS_PATH=$settingsPath"
        }
        $proc = [System.Diagnostics.Process]::Start($startInfo)
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
}
