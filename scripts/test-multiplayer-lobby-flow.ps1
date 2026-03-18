param(
    [string]$ProjectRoot = "",
    [int]$HostApiPort = 8080,
    [int]$ClientApiPort = 8081,
    [switch]$KeepGamesRunning
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$scriptRoot = Join-Path $ProjectRoot "scripts"
$hostBaseUrl = "http://127.0.0.1:$HostApiPort"
$clientBaseUrl = "http://127.0.0.1:$ClientApiPort"

function Stop-Games {
    $existing = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-Process -Id $existing.Id -Force
        Start-Sleep -Seconds 2
    }
}

function Invoke-ApiJson {
    param(
        [string]$BaseUrl,
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [int]$TimeoutSec = 10,
        [int]$RetryCount = 5,
        [int]$RetryDelayMs = 500
    )

    $uri = $BaseUrl.TrimEnd("/") + $Path

    for ($attempt = 0; $attempt -lt $RetryCount; $attempt++) {
        try {
            if ($null -eq $Body) {
                $response = Invoke-WebRequest -Method $Method -Uri $uri -UseBasicParsing -TimeoutSec $TimeoutSec
            }
            else {
                $response = Invoke-WebRequest -Method $Method -Uri $uri -UseBasicParsing -TimeoutSec $TimeoutSec -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 8 -Compress)
            }

            return $response.Content | ConvertFrom-Json
        }
        catch {
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

            $isLastAttempt = $attempt -ge ($RetryCount - 1)
            if ($isLastAttempt) {
                throw
            }

            Start-Sleep -Milliseconds $RetryDelayMs
        }
    }
}

function Get-State {
    param([string]$BaseUrl)
    return (Invoke-ApiJson -BaseUrl $BaseUrl -Method "GET" -Path "/state").data
}

function Invoke-Action {
    param(
        [string]$BaseUrl,
        [hashtable]$Payload
    )

    return Invoke-ApiJson -BaseUrl $BaseUrl -Method "POST" -Path "/action" -Body $Payload
}

function Wait-ForState {
    param(
        [string]$BaseUrl,
        [string]$Description,
        [scriptblock]$Condition,
        [int]$PollAttempts = 180,
        [int]$PollDelayMs = 250
    )

    for ($attempt = 0; $attempt -lt $PollAttempts; $attempt++) {
        $state = Get-State -BaseUrl $BaseUrl
        if (& $Condition $state) {
            return $state
        }

        Start-Sleep -Milliseconds $PollDelayMs
    }

    throw "Timed out waiting for state at ${BaseUrl}: $Description"
}

function Assert-ActionAvailable {
    param(
        $State,
        [string]$ActionName,
        [string]$BaseUrl
    )

    if (-not (@($State.available_actions) -contains $ActionName)) {
        throw "Expected action '$ActionName' to be available at $BaseUrl, but state was: $($State | ConvertTo-Json -Depth 8 -Compress)"
    }
}

function Invoke-StateInvariantScript {
    param([string]$BaseUrl)

    $scriptPath = Join-Path $scriptRoot "test-state-invariants.ps1"
    & powershell -ExecutionPolicy Bypass -File $scriptPath -BaseUrl $BaseUrl
    if ($LASTEXITCODE -ne 0) {
        throw "test-state-invariants.ps1 failed for $BaseUrl"
    }
}

function Start-DebugSession {
    param(
        [int]$ApiPort,
        [switch]$KeepExistingProcesses
    )

    $scriptPath = Join-Path $scriptRoot "start-game-session.ps1"
    $arguments = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $scriptPath,
        "-EnableDebugActions",
        "-ApiPort", [string]$ApiPort
    )

    if ($KeepExistingProcesses) {
        $arguments += "-KeepExistingProcesses"
    }

    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ("sts2-start-session-{0}-{1}.stdout.log" -f $ApiPort, [guid]::NewGuid().ToString("N"))
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ("sts2-start-session-{0}-{1}.stderr.log" -f $ApiPort, [guid]::NewGuid().ToString("N"))

    try {
        $process = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        if ($process.ExitCode -ne 0) {
            $stdout = if (Test-Path $stdoutPath) { (Get-Content -Path $stdoutPath -Raw).Trim() } else { "" }
            $stderr = if (Test-Path $stderrPath) { (Get-Content -Path $stderrPath -Raw).Trim() } else { "" }
            $details = @($stdout, $stderr) | Where-Object { $_ }
            if (@($details).Count -gt 0) {
                throw "start-game-session.ps1 failed for port $ApiPort. Output: $($details -join [Environment]::NewLine)"
            }

            throw "start-game-session.ps1 failed for port $ApiPort"
        }
    }
    finally {
        Remove-Item -Path $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
    }

    $latestProcess = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending |
        Select-Object -First 1

    [void](Wait-ForState -BaseUrl ("http://127.0.0.1:$ApiPort") -Description "API state ready on port $ApiPort" -Condition {
            param($CurrentState)
            $null -ne $CurrentState.screen
        } -PollAttempts 40 -PollDelayMs 500)

    return [pscustomobject]@{
        pid = $latestProcess?.Id
        debug_actions_enabled = $true
        api_port = $ApiPort
        base_url = "http://127.0.0.1:$ApiPort"
        health = "ready"
    }
}

try {
    Write-Host "==> stop existing games"
    Stop-Games

    Write-Host "==> start host debug session"
    $hostSession = Start-DebugSession -ApiPort $HostApiPort
    Write-Host "==> host open multiplayer test"
    $hostOpenResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{
        action = "run_console_command"
        command = "multiplayer test"
    }

    if (-not $hostOpenResponse.ok) {
        throw "Host failed to open multiplayer test scene: $($hostOpenResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostOpenState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host MULTIPLAYER_LOBBY without active lobby" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MULTIPLAYER_LOBBY" -and
        $null -ne $CurrentState.multiplayer_lobby -and
        (-not $CurrentState.multiplayer_lobby.has_lobby)
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Assert-ActionAvailable -State $hostOpenState -ActionName "host_multiplayer_lobby" -BaseUrl $hostBaseUrl
    Assert-ActionAvailable -State $hostOpenState -ActionName "join_multiplayer_lobby" -BaseUrl $hostBaseUrl

    Write-Host "==> host create lobby"
    $hostStartResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{ action = "host_multiplayer_lobby" }
    if (-not $hostStartResponse.ok) {
        throw "host_multiplayer_lobby failed: $($hostStartResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostLobbyState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host lobby ready" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MULTIPLAYER_LOBBY" -and
        $null -ne $CurrentState.multiplayer_lobby -and
        $CurrentState.multiplayer_lobby.has_lobby -and
        $CurrentState.multiplayer_lobby.is_host -and
        [int]$CurrentState.multiplayer_lobby.player_count -eq 1
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl

    Write-Host "==> host select SILENT"
    $hostSelectResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{
        action = "select_character"
        option_index = 1
    }

    if (-not $hostSelectResponse.ok) {
        throw "Host select_character failed: $($hostSelectResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    [void](Wait-ForState -BaseUrl $hostBaseUrl -Description "host selected SILENT" -Condition {
            param($CurrentState)
            $CurrentState.multiplayer_lobby.selected_character_id -eq "SILENT"
        })

    Write-Host "==> start client debug session"
    $clientSession = Start-DebugSession -ApiPort $ClientApiPort -KeepExistingProcesses
    Write-Host "==> client open multiplayer test"
    $clientOpenResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{
        action = "run_console_command"
        command = "multiplayer test"
    }

    if (-not $clientOpenResponse.ok) {
        throw "Client failed to open multiplayer test scene: $($clientOpenResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientOpenState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client MULTIPLAYER_LOBBY without active lobby" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MULTIPLAYER_LOBBY" -and
        $null -ne $CurrentState.multiplayer_lobby -and
        (-not $CurrentState.multiplayer_lobby.has_lobby)
    }

    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl
    Assert-ActionAvailable -State $clientOpenState -ActionName "join_multiplayer_lobby" -BaseUrl $clientBaseUrl

    Write-Host "==> client join lobby"
    $clientJoinResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{ action = "join_multiplayer_lobby" }
    if (-not $clientJoinResponse.ok) {
        throw "join_multiplayer_lobby failed: $($clientJoinResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientLobbyState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client joined lobby" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MULTIPLAYER_LOBBY" -and
        $null -ne $CurrentState.multiplayer_lobby -and
        $CurrentState.multiplayer_lobby.has_lobby -and
        $CurrentState.multiplayer_lobby.is_client -and
        [int]$CurrentState.multiplayer_lobby.player_count -eq 2
    }

    $hostTwoPlayerLobbyState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host sees second player" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MULTIPLAYER_LOBBY" -and
        $null -ne $CurrentState.multiplayer_lobby -and
        [int]$CurrentState.multiplayer_lobby.player_count -eq 2
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> client select DEFECT"
    $clientSelectResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{
        action = "select_character"
        option_index = 4
    }

    if (-not $clientSelectResponse.ok) {
        throw "Client select_character failed: $($clientSelectResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    [void](Wait-ForState -BaseUrl $clientBaseUrl -Description "client selected DEFECT" -Condition {
            param($CurrentState)
            $CurrentState.multiplayer_lobby.selected_character_id -eq "DEFECT"
        })
    [void](Wait-ForState -BaseUrl $hostBaseUrl -Description "host roster reflects DEFECT client" -Condition {
            param($CurrentState)
            @($CurrentState.multiplayer_lobby.players | Where-Object { (-not $_.is_local) -and $_.character_id -eq "DEFECT" }).Count -eq 1
        })

    Write-Host "==> client ready"
    $clientReadyResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{ action = "ready_multiplayer_lobby" }
    if (-not $clientReadyResponse.ok) {
        throw "Client ready_multiplayer_lobby failed: $($clientReadyResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    [void](Wait-ForState -BaseUrl $clientBaseUrl -Description "client local_ready=true in lobby" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "MULTIPLAYER_LOBBY" -and
            $CurrentState.multiplayer_lobby.local_ready
        })
    [void](Wait-ForState -BaseUrl $hostBaseUrl -Description "host sees remote ready state" -Condition {
            param($CurrentState)
            @($CurrentState.multiplayer_lobby.players | Where-Object { (-not $_.is_local) -and $_.is_ready }).Count -eq 1
        })

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> host ready and begin run"
    $hostReadyResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{ action = "ready_multiplayer_lobby" }
    if (-not $hostReadyResponse.ok) {
        throw "Host ready_multiplayer_lobby failed: $($hostReadyResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostRunState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host leaves MULTIPLAYER_LOBBY and enters multiplayer run" -Condition {
        param($CurrentState)
        $CurrentState.screen -ne "MULTIPLAYER_LOBBY" -and
        $null -ne $CurrentState.run -and
        @($CurrentState.run.players).Count -eq 2 -and
        $null -ne $CurrentState.multiplayer -and
        $CurrentState.multiplayer.is_multiplayer
    }

    $clientRunState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client leaves MULTIPLAYER_LOBBY and enters multiplayer run" -Condition {
        param($CurrentState)
        $CurrentState.screen -ne "MULTIPLAYER_LOBBY" -and
        $null -ne $CurrentState.run -and
        @($CurrentState.run.players).Count -eq 2 -and
        $null -ne $CurrentState.multiplayer -and
        $CurrentState.multiplayer.is_multiplayer
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    [pscustomobject]@{
        host = [pscustomobject]@{
            pid = $hostSession.pid
            base_url = $hostBaseUrl
            screen = $hostRunState.screen
            run_id = $hostRunState.run_id
            net_game_type = $hostRunState.multiplayer.net_game_type
            player_count = @($hostRunState.run.players).Count
            selected_character_id = "SILENT"
        }
        client = [pscustomobject]@{
            pid = $clientSession.pid
            base_url = $clientBaseUrl
            screen = $clientRunState.screen
            run_id = $clientRunState.run_id
            net_game_type = $clientRunState.multiplayer.net_game_type
            player_count = @($clientRunState.run.players).Count
            selected_character_id = "DEFECT"
        }
    } | ConvertTo-Json -Depth 6
}
finally {
    if (-not $KeepGamesRunning) {
        Stop-Games
    }
}
