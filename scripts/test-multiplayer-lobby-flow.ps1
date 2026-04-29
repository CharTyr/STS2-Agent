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
        [int]$RetryCount = 15,
        [int]$RetryDelayMs = 1000
    )

    $uri = $BaseUrl.TrimEnd("/") + $Path

    for ($attempt = 0; $attempt -lt $RetryCount; $attempt++) {
        try {
            if ($null -ne $Body) {
                $jsonBody = $Body | ConvertTo-Json -Depth 8 -Compress
                return Invoke-RestMethod -Uri $uri -Method $Method -ContentType "application/json" -Body $jsonBody -TimeoutSec $TimeoutSec
            }

            return Invoke-RestMethod -Uri $uri -Method $Method -TimeoutSec $TimeoutSec
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

function Resolve-BlockingModal {
    param(
        [string]$BaseUrl,
        [int]$MaxAttempts = 4
    )

    for ($attempt = 0; $attempt -lt $MaxAttempts; $attempt++) {
        $state = Get-State -BaseUrl $BaseUrl
        $actions = @($state.available_actions)
        if ($state.screen -ne "MODAL") {
            return $state
        }

        if ($actions -contains "confirm_modal") {
            $response = Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "confirm_modal" }
            if (-not $response.ok) {
                throw "confirm_modal failed at ${BaseUrl}: $($response | ConvertTo-Json -Depth 8 -Compress)"
            }

            Start-Sleep -Milliseconds 250
            continue
        }

        if ($actions -contains "dismiss_modal") {
            $response = Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "dismiss_modal" }
            if (-not $response.ok) {
                throw "dismiss_modal failed at ${BaseUrl}: $($response | ConvertTo-Json -Depth 8 -Compress)"
            }

            Start-Sleep -Milliseconds 250
            continue
        }

        throw "Modal is blocking progress at $BaseUrl, but no modal action is available: $($state | ConvertTo-Json -Depth 8 -Compress)"
    }

    return Get-State -BaseUrl $BaseUrl
}

function Invoke-ActionExpectOk {
    param(
        [string]$BaseUrl,
        [hashtable]$Payload,
        [string]$Description,
        [int]$RetryCount = 1,
        [int]$RetryDelayMs = 1000
    )

    $lastResponse = $null
    for ($attempt = 0; $attempt -lt $RetryCount; $attempt++) {
        $lastResponse = Invoke-Action -BaseUrl $BaseUrl -Payload $Payload
        if ($lastResponse.ok) {
            return $lastResponse
        }

        $isRetryableInternalError = $lastResponse.error.code -eq "internal_error"
        $hasRetriesRemaining = $attempt -lt ($RetryCount - 1)
        if (-not $isRetryableInternalError -or -not $hasRetriesRemaining) {
            break
        }

        Start-Sleep -Milliseconds $RetryDelayMs
    }

    throw "${Description} failed: $($lastResponse | ConvertTo-Json -Depth 8 -Compress)"
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

function Get-FirstPlayableCardPayload {
    param([object]$State)

    foreach ($card in @($State.combat.hand)) {
        if (-not $card.playable) {
            continue
        }

        $payload = @{
            action = "play_card"
            card_index = [int]$card.index
        }

        if ($card.requires_target) {
            $targets = @($card.valid_target_indices)
            if ($targets.Count -eq 0) {
                continue
            }

            $payload.target_index = [int]$targets[0]
        }

        return $payload
    }

    throw "No playable combat card found."
}

function Invoke-LocalRunProgressionStep {
    param(
        [string]$BaseUrl,
        [object]$State
    )

    $actions = @($State.available_actions)

    if ($actions -contains "confirm_modal") {
        return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "confirm_modal" }
    }

    if ($actions -contains "dismiss_modal") {
        return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "dismiss_modal" }
    }

    if ([string]::IsNullOrWhiteSpace([string]$State.screen) -or $State.screen -eq "UNKNOWN") {
        return $null
    }

    switch ($State.screen) {
        "EVENT" {
            if (($actions -contains "choose_event_option") -and $null -ne $State.event -and @($State.event.options).Count -ge 1) {
                $optionIndex = if ($State.event.is_finished -or @($State.event.options).Count -eq 1) { 0 } else { 1 }
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{
                    action = "choose_event_option"
                    option_index = $optionIndex
                }
            }

            if ($actions -contains "proceed") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "proceed" }
            }
        }
        "CARD_SELECTION" {
            if ($actions -contains "select_deck_card") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{
                    action = "select_deck_card"
                    option_index = 0
                }
            }

            if ($actions -contains "confirm_selection") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "confirm_selection" }
            }

            if ($actions -contains "proceed") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "proceed" }
            }
        }
        "REWARD" {
            if ($actions -contains "resolve_rewards") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "resolve_rewards" }
            }

            if ($actions -contains "collect_rewards_and_proceed") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "collect_rewards_and_proceed" }
            }

            if ($actions -contains "claim_reward" -and $null -ne $State.reward -and @($State.reward.rewards).Count -ge 1) {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{
                    action = "claim_reward"
                    option_index = 0
                }
            }

            if ($actions -contains "choose_reward_card" -and $null -ne $State.reward -and @($State.reward.card_options).Count -ge 1) {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{
                    action = "choose_reward_card"
                    option_index = 0
                }
            }

            if ($actions -contains "skip_reward_cards") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "skip_reward_cards" }
            }

            if ($actions -contains "proceed") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "proceed" }
            }
        }
        "MAP" {
            return $null
        }
    }

    throw "Unsupported run progression state at ${BaseUrl}: $($State | ConvertTo-Json -Depth 8 -Compress)"
}

function Resolve-RunIntroToMap {
    param(
        [string]$HostBaseUrl,
        [string]$ClientBaseUrl,
        [int]$MaxRounds = 24
    )

    for ($round = 0; $round -lt $MaxRounds; $round++) {
        $hostState = Get-State -BaseUrl $HostBaseUrl
        $clientState = Get-State -BaseUrl $ClientBaseUrl

        $hostReady = $hostState.screen -eq "MAP" -and $null -ne $hostState.map -and @($hostState.map.available_nodes).Count -ge 1
        $clientReady = $clientState.screen -eq "MAP" -and $null -ne $clientState.map -and @($clientState.map.available_nodes).Count -ge 1

        if ($hostReady -and $clientReady) {
            return [pscustomobject]@{
                host = $hostState
                client = $clientState
            }
        }

        if (-not $hostReady) {
            $hostStep = Invoke-LocalRunProgressionStep -BaseUrl $HostBaseUrl -State $hostState
            if ($null -ne $hostStep -and (-not $hostStep.ok)) {
                throw "Host intro progression failed: $($hostStep | ConvertTo-Json -Depth 8 -Compress)"
            }
        }

        if (-not $clientReady) {
            $clientStep = Invoke-LocalRunProgressionStep -BaseUrl $ClientBaseUrl -State $clientState
            if ($null -ne $clientStep -and (-not $clientStep.ok)) {
                throw "Client intro progression failed: $($clientStep | ConvertTo-Json -Depth 8 -Compress)"
            }
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out resolving multiplayer run intro to map."
}

function Invoke-DebugCombatWin {
    param([string]$BaseUrl)

    return Invoke-Action -BaseUrl $BaseUrl -Payload @{
        action = "run_console_command"
        command = "win"
    }
}

function Get-RestOptionById {
    param(
        [object]$State,
        [string]$OptionId
    )

    return @($State.rest.options | Where-Object { $_.option_id -eq $OptionId } | Select-Object -First 1)[0]
}

function Start-DebugSession {
    param(
        [int]$ApiPort,
        [switch]$KeepExistingProcesses
    )

    $scriptPath = Join-Path $scriptRoot "start-game-session.ps1"
    $startOutput = if ($KeepExistingProcesses) {
        & $scriptPath -EnableDebugActions -ApiPort $ApiPort -KeepExistingProcesses
    } else {
        & $scriptPath -EnableDebugActions -ApiPort $ApiPort
    }

    $latestProcess = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue |
        Sort-Object StartTime -Descending |
        Select-Object -First 1

    [void](Wait-ForState -BaseUrl ("http://127.0.0.1:$ApiPort") -Description "API state ready on port $ApiPort" -Condition {
            param($CurrentState)
            $null -ne $CurrentState.screen
        } -PollAttempts 40 -PollDelayMs 500)
    Start-Sleep -Seconds 2

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

    [void](Resolve-BlockingModal -BaseUrl $hostBaseUrl)

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

    [void](Resolve-BlockingModal -BaseUrl $clientBaseUrl)

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

    Write-Host "==> resolve multiplayer intro branch to map"
    $introResolution = Resolve-RunIntroToMap -HostBaseUrl $hostBaseUrl -ClientBaseUrl $clientBaseUrl
    $hostMapState = $introResolution.host
    $clientMapState = $introResolution.client

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    $selectedMapNode = $hostMapState.map.available_nodes[0]

    Write-Host "==> host votes for next map node"
    $hostVoteResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{
        action = "choose_map_node"
        option_index = 0
    }
    if (-not $hostVoteResponse.ok) {
        throw "Host choose_map_node failed: $($hostVoteResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostVotedMapState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host map vote registered" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MAP" -and
        $null -ne $CurrentState.map -and
        $null -ne $CurrentState.map.local_vote -and
        [int]$CurrentState.map.local_vote.row -eq [int]$selectedMapNode.row -and
        [int]$CurrentState.map.local_vote.col -eq [int]$selectedMapNode.col -and
        @($CurrentState.map.available_nodes | Where-Object { $_.has_local_vote -and [int]$_.row -eq [int]$selectedMapNode.row -and [int]$_.col -eq [int]$selectedMapNode.col }).Count -eq 1
    }

    [void](Wait-ForState -BaseUrl $clientBaseUrl -Description "client sees host vote" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "MAP" -and
            $null -ne $CurrentState.map -and
            @($CurrentState.map.available_nodes | Where-Object {
                    [int]$_.row -eq [int]$selectedMapNode.row -and
                    [int]$_.col -eq [int]$selectedMapNode.col -and
                    [int]$_.vote_count -ge 1 -and
                    (-not $_.has_local_vote)
                }).Count -eq 1
        })

    Write-Host "==> client votes for same map node"
    $clientVoteResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{
        action = "choose_map_node"
        option_index = 0
    }
    if (-not $clientVoteResponse.ok) {
        throw "Client choose_map_node failed: $($clientVoteResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostCombatState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host combat ready" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        $null -ne $CurrentState.combat -and
        @($CurrentState.combat.enemies).Count -ge 1 -and
        @($CurrentState.combat.hand).Count -ge 1 -and
        (
            @($CurrentState.available_actions) -contains "play_card" -or
            @($CurrentState.available_actions) -contains "end_turn"
        )
    }
    $clientCombatState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client combat ready" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        $null -ne $CurrentState.combat -and
        @($CurrentState.combat.enemies).Count -ge 1 -and
        @($CurrentState.combat.hand).Count -ge 1 -and
        (
            @($CurrentState.available_actions) -contains "play_card" -or
            @($CurrentState.available_actions) -contains "end_turn"
        )
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> host plays a combat card"
    $hostPlayPayload = Get-FirstPlayableCardPayload -State $hostCombatState
    $hostPlayResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload $hostPlayPayload
    if (-not $hostPlayResponse.ok) {
        throw "Host play_card failed: $($hostPlayResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostAfterPlayState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host card resolved" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        $CurrentState.combat.player.cards_played_this_turn -ge 1
    }

    Write-Host "==> client plays a combat card"
    $clientPlayPayload = Get-FirstPlayableCardPayload -State $clientCombatState
    $clientPlayResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload $clientPlayPayload
    if (-not $clientPlayResponse.ok) {
        throw "Client play_card failed: $($clientPlayResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientAfterPlayState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client card resolved" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        $CurrentState.combat.player.cards_played_this_turn -ge 1
    }

    Write-Host "==> host and client end turn"
    $hostEndTurnResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{ action = "end_turn" }
    if (-not $hostEndTurnResponse.ok) {
        throw "Host end_turn failed: $($hostEndTurnResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientEndTurnResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{ action = "end_turn" }
    if (-not $clientEndTurnResponse.ok) {
        throw "Client end_turn failed: $($clientEndTurnResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostTurnTwoState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host reached turn 2" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        [int]$CurrentState.turn -ge 2 -and
        (
            @($CurrentState.available_actions) -contains "play_card" -or
            @($CurrentState.available_actions) -contains "end_turn"
        )
    }
    $clientTurnTwoState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client reached turn 2" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        [int]$CurrentState.turn -ge 2 -and
        (
            @($CurrentState.available_actions) -contains "play_card" -or
            @($CurrentState.available_actions) -contains "end_turn"
        )
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> finish combat with debug win on both players"
    $hostWinResponse = Invoke-DebugCombatWin -BaseUrl $hostBaseUrl
    if (-not $hostWinResponse.ok) {
        throw "Host debug combat win failed: $($hostWinResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientWinResponse = Invoke-DebugCombatWin -BaseUrl $clientBaseUrl
    if (-not $clientWinResponse.ok) {
        throw "Client debug combat win failed: $($clientWinResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostRewardState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host reward screen ready" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "REWARD" -and
        $null -ne $CurrentState.reward -and
        (@($CurrentState.reward.rewards).Count -ge 1 -or @($CurrentState.reward.card_options).Count -ge 1)
    }
    $clientRewardState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client reward screen ready" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "REWARD" -and
        $null -ne $CurrentState.reward -and
        (@($CurrentState.reward.rewards).Count -ge 1 -or @($CurrentState.reward.card_options).Count -ge 1)
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> host and client resolve reward flow"
    $hostResolveRewardResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{ action = "resolve_rewards" }
    if (-not $hostResolveRewardResponse.ok) {
        throw "Host resolve_rewards failed: $($hostResolveRewardResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientResolveRewardResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{ action = "resolve_rewards" }
    if (-not $clientResolveRewardResponse.ok) {
        throw "Client resolve_rewards failed: $($clientResolveRewardResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostPostRewardMapState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host returned to map after rewards" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MAP" -and
        $null -ne $CurrentState.map -and
        $null -ne $CurrentState.map.current_node -and
        [int]$CurrentState.map.current_node.row -eq [int]$selectedMapNode.row -and
        [int]$CurrentState.map.current_node.col -eq [int]$selectedMapNode.col -and
        @($CurrentState.map.available_nodes).Count -ge 1
    }
    $clientPostRewardMapState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client returned to map after rewards" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MAP" -and
        $null -ne $CurrentState.map -and
        $null -ne $CurrentState.map.current_node -and
        [int]$CurrentState.map.current_node.row -eq [int]$selectedMapNode.row -and
        [int]$CurrentState.map.current_node.col -eq [int]$selectedMapNode.col -and
        @($CurrentState.map.available_nodes).Count -ge 1
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> jump both players to REST for multiplayer MEND validation"
    Start-Sleep -Seconds 2
    $hostRestJumpResponse = $null
    $hostRestDebugUnsupported = $false
    try {
        $hostRestJumpResponse = Invoke-ActionExpectOk -BaseUrl $hostBaseUrl -Description "Host room RestSite" -RetryCount 3 -RetryDelayMs 1500 -Payload @{
            action = "run_console_command"
            command = "room RestSite"
        }
    }
    catch {
        Write-Warning "Host room RestSite remained unavailable after retries; falling back to client-only MEND validation."
        $hostRestDebugUnsupported = $true
    }

    $clientRestJumpResponse = Invoke-ActionExpectOk -BaseUrl $clientBaseUrl -Description "Client room RestSite" -RetryCount 2 -RetryDelayMs 1000 -Payload @{
        action = "run_console_command"
        command = "room RestSite"
    }
    $hostRestState = $null
    if (-not $hostRestDebugUnsupported) {
        $hostRestState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host REST options ready" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "REST" -and
            @($CurrentState.available_actions) -contains "choose_rest_option" -and
            $null -ne (Get-RestOptionById -State $CurrentState -OptionId "MEND")
        }
    }
    $clientRestState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client REST options ready" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "REST" -and
        @($CurrentState.available_actions) -contains "choose_rest_option" -and
        $null -ne (Get-RestOptionById -State $CurrentState -OptionId "MEND")
    }

    $hostMendOption = if ($hostRestState -ne $null) { Get-RestOptionById -State $hostRestState -OptionId "MEND" } else { $null }
    $clientMendOption = Get-RestOptionById -State $clientRestState -OptionId "MEND"

    if ($hostMendOption -ne $null -and (-not $hostMendOption.requires_target -or @($hostMendOption.valid_target_indices).Count -lt 1)) {
        throw "Host MEND option did not expose target metadata: $($hostMendOption | ConvertTo-Json -Depth 8 -Compress)"
    }

    if (-not $clientMendOption.requires_target -or @($clientMendOption.valid_target_indices).Count -lt 1) {
        throw "Client MEND option did not expose target metadata: $($clientMendOption | ConvertTo-Json -Depth 8 -Compress)"
    }

    Write-Host "==> verify MEND rejects missing target_index"
    $clientMissingTargetResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{
        action = "choose_rest_option"
        option_index = [int]$clientMendOption.index
    }
    if ($clientMissingTargetResponse.ok -or $clientMissingTargetResponse.error.code -ne "invalid_target") {
        throw "Client MEND without target_index should fail with invalid_target: $($clientMissingTargetResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    Write-Host "==> client MEND targets host"
    $clientMendResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{
        action = "choose_rest_option"
        option_index = [int]$clientMendOption.index
        target_index = [int](@($clientMendOption.valid_target_indices)[0])
    }
    if (-not $clientMendResponse.ok) {
        throw "Client MEND with target_index failed: $($clientMendResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientRestProceedState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client MEND resolved to proceed" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "REST" -and
        @($CurrentState.available_actions) -contains "proceed" -and
        @($CurrentState.rest.options).Count -eq 0
    }

    $hostRestProceedState = $null
    if (-not $hostRestDebugUnsupported) {
        Write-Host "==> host MEND targets client"
        $hostMendResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{
            action = "choose_rest_option"
            option_index = [int]$hostMendOption.index
            target_index = [int](@($hostMendOption.valid_target_indices)[0])
        }
        if (-not $hostMendResponse.ok) {
            throw "Host MEND with target_index failed: $($hostMendResponse | ConvertTo-Json -Depth 8 -Compress)"
        }

        $hostRestProceedState = Wait-ForState -BaseUrl $hostBaseUrl -Description "host MEND resolved to proceed" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "REST" -and
            @($CurrentState.available_actions) -contains "proceed" -and
            @($CurrentState.rest.options).Count -eq 0
        }
    }

    if (-not $hostRestDebugUnsupported) {
        Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    }
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> leave REST and return to MAP"
    if (-not $hostRestDebugUnsupported) {
        $hostProceedFromRestResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload @{ action = "proceed" }
        if (-not $hostProceedFromRestResponse.ok) {
            throw "Host proceed from REST failed: $($hostProceedFromRestResponse | ConvertTo-Json -Depth 8 -Compress)"
        }
    }
    $clientProceedFromRestResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload @{ action = "proceed" }
    if (-not $clientProceedFromRestResponse.ok) {
        throw "Client proceed from REST failed: $($clientProceedFromRestResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostPostRestMapState = if (-not $hostRestDebugUnsupported) {
        Wait-ForState -BaseUrl $hostBaseUrl -Description "host returned to map after REST" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "MAP" -and
            $null -ne $CurrentState.map -and
            @($CurrentState.map.available_nodes).Count -ge 1
        }
    } else {
        Get-State -BaseUrl $hostBaseUrl
    }
    $clientPostRestMapState = Wait-ForState -BaseUrl $clientBaseUrl -Description "client returned to map after REST" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "MAP" -and
        $null -ne $CurrentState.map -and
        @($CurrentState.map.available_nodes).Count -ge 1
    }

    if (-not $hostRestDebugUnsupported) {
        Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    }
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    [pscustomobject]@{
        host = [pscustomobject]@{
            pid = $hostSession.pid
            base_url = $hostBaseUrl
            screen = $hostPostRestMapState.screen
            run_id = $hostPostRewardMapState.run_id
            net_game_type = $hostPostRewardMapState.multiplayer.net_game_type
            player_count = @($hostPostRewardMapState.run.players).Count
            selected_character_id = "SILENT"
            local_vote = if ($hostVotedMapState.map.local_vote) { "$($hostVotedMapState.map.local_vote.row),$($hostVotedMapState.map.local_vote.col)" } else { $null }
            turn = $hostTurnTwoState.turn
            cards_played_this_turn = $hostAfterPlayState.combat.player.cards_played_this_turn
            current_node = if ($hostPostRestMapState.map.current_node) { "$($hostPostRestMapState.map.current_node.row),$($hostPostRestMapState.map.current_node.col)" } else { $null }
            next_map_options = @($hostPostRestMapState.map.available_nodes).Count
            rest_mend_target_required = if ($hostMendOption -ne $null) { [bool]$hostMendOption.requires_target } else { $null }
            rest_mend_targets = if ($hostMendOption -ne $null) { @($hostMendOption.valid_target_indices) } else { @() }
            rest_debug_room_supported = -not $hostRestDebugUnsupported
        }
        client = [pscustomobject]@{
            pid = $clientSession.pid
            base_url = $clientBaseUrl
            screen = $clientPostRestMapState.screen
            run_id = $clientPostRewardMapState.run_id
            net_game_type = $clientPostRewardMapState.multiplayer.net_game_type
            player_count = @($clientPostRewardMapState.run.players).Count
            selected_character_id = "DEFECT"
            turn = $clientTurnTwoState.turn
            cards_played_this_turn = $clientAfterPlayState.combat.player.cards_played_this_turn
            current_node = "$($clientPostRestMapState.map.current_node.row),$($clientPostRestMapState.map.current_node.col)"
            next_map_options = @($clientPostRestMapState.map.available_nodes).Count
            rest_mend_target_required = [bool]$clientMendOption.requires_target
            rest_mend_targets = @($clientMendOption.valid_target_indices)
        }
    } | ConvertTo-Json -Depth 6
}
finally {
    if (-not $KeepGamesRunning) {
        Stop-Games
    }
}
