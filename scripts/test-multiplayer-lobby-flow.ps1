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
$script:KnownTestPids = New-Object 'System.Collections.Generic.HashSet[int]'

function Get-TestGameProcesses {
    param([int[]]$ApiPorts)

    $matches = @()
    foreach ($proc in Get-CimInstance Win32_Process | Where-Object { $_.Name -eq "SlayTheSpire2.exe" }) {
        $command = [string]$proc.CommandLine
        $matchedPort = $null
        foreach ($port in $ApiPorts) {
            if ($command -match ("STS2_API_PORT[= ]{0}" -f $port) -or $command -match ("--api-port[= ]{0}" -f $port) -or $command -match ("127\.0\.0\.1:{0}" -f $port)) {
                $matchedPort = $port
                break
            }
        }

        if ($null -ne $matchedPort) {
            $matches += [pscustomobject]@{
                ProcessId = [int]$proc.ProcessId
                Port = $matchedPort
                CommandLine = $command
            }
        }
    }

    return $matches
}

function Stop-Games {
    $ports = @($HostApiPort, $ClientApiPort)
    $known = @(Get-TestGameProcesses -ApiPorts $ports)
    $ids = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($proc in $known) { [void]$ids.Add([int]$proc.ProcessId) }
    foreach ($testPid in @($script:KnownTestPids)) {
        if (Get-Process -Id $testPid -ErrorAction SilentlyContinue) { [void]$ids.Add([int]$testPid) }
    }
    if ($ids.Count -eq 0) {
        Write-Host "==> no proven test games on ports $($ports -join ',') ; leaving other SlayTheSpire2 processes alone"
        return
    }

    foreach ($testPid in $ids) {
        Write-Host "==> stopping test game pid=$testPid"
        Stop-Process -Id $testPid -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
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

function Save-TimeoutStateDump {
    param(
        [string]$TimedOutBaseUrl,
        [string]$Description,
        $LastState = $null,
        [string]$ErrorMessage = ""
    )

    $dumpDir = [Environment]::GetEnvironmentVariable("STS2_STATE_DUMP_DIR", "Process")
    if ([string]::IsNullOrWhiteSpace($dumpDir)) {
        return
    }

    try {
        New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null
        [pscustomobject]@{
            timestamp = (Get-Date).ToString("o")
            timed_out_url = $TimedOutBaseUrl
            description = $Description
            error = $ErrorMessage
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $dumpDir "timeout-meta.json") -Encoding UTF8

        if ($null -ne $LastState) {
            $LastState | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $dumpDir "last-polled-state.json") -Encoding UTF8
        }

        foreach ($name in @("host", "client")) {
            $url = if ($name -eq "host") { $hostBaseUrl } else { $clientBaseUrl }
            try {
                $payload = Invoke-RestMethod -Uri ($url.TrimEnd("/") + "/state") -TimeoutSec 8
                $payload | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $dumpDir "$name-state.json") -Encoding UTF8
                $data = $payload.data
                [pscustomobject]@{
                    url = $url
                    screen = $data.screen
                    in_combat = $data.in_combat
                    turn = $data.turn
                    run_id = $data.run_id
                    available_actions = @($data.available_actions)
                    map_current = $data.map.current_node
                    map_local_vote = $data.map.local_vote
                    map_available = @($data.map.available_nodes)
                    combat_enemy_count = @($data.combat.enemies).Count
                    combat_hand_count = @($data.combat.hand).Count
                    play_card_available = @($data.available_actions) -contains "play_card"
                    multiplayer = $data.multiplayer
                } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $dumpDir "$name-summary.json") -Encoding UTF8
            }
            catch {
                $_.Exception.ToString() | Set-Content -LiteralPath (Join-Path $dumpDir "$name-state-error.txt") -Encoding UTF8
            }
        }

        Write-Host "==> timeout state dump written to $dumpDir"
    }
    catch {
        Write-Host "==> timeout state dump failed: $($_.Exception.Message)"
    }
}

function Test-ShouldConfirmBlockingModal {
    param($State)

    if ($null -eq $State -or [string]$State.screen -ne "MODAL") {
        return $false
    }

    $typeName = [string]$State.modal.type_name
    if ($typeName -match "NErrorPopup" -or $typeName -match "ErrorPopup") {
        return $false
    }

    $actions = @($State.available_actions)
    if ($actions -notcontains "confirm_modal" -or $actions -contains "dismiss_modal") {
        return $false
    }

    if ([bool]$State.modal.can_dismiss) {
        return $false
    }

    return $typeName -match "Ftue" -or [bool]$State.in_combat
}

function Save-ErrorPopupAndFail {
    param(
        [string]$BaseUrl,
        $State,
        [string]$Reason = "NErrorPopup"
    )

    $dumpDir = [Environment]::GetEnvironmentVariable("STS2_STATE_DUMP_DIR", "Process")
    if (-not [string]::IsNullOrWhiteSpace($dumpDir)) {
        New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null
        $stamp = Get-Date -Format "HHmmss"
        $State | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $dumpDir ("error-popup-{0}-{1}.json" -f ($BaseUrl -replace "[^0-9]", ""), $stamp)) -Encoding UTF8
        [pscustomobject]@{
            url = $BaseUrl
            reason = $Reason
            screen = $State.screen
            modal = $State.modal
            run_id = $State.run_id
            turn = $State.turn
            in_combat = $State.in_combat
            available_actions = @($State.available_actions)
        } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $dumpDir ("error-popup-{0}-{1}-summary.json" -f ($BaseUrl -replace "[^0-9]", ""), $stamp)) -Encoding UTF8
    }

    throw ("Captured {0} on {1} type={2}; not auto-confirming." -f $Reason, $BaseUrl, [string]$State.modal.type_name)
}

function Clear-BlockingFtueModals {
    param([string[]]$BaseUrls)

    foreach ($url in $BaseUrls) {
        if ([string]::IsNullOrWhiteSpace($url)) {
            continue
        }

        try {
            $payload = Invoke-ApiJson -BaseUrl $url -Method "GET" -Path "/state" -TimeoutSec 2 -RetryCount 1 -RetryDelayMs 0
            $state = $payload.data
            if (-not (Test-ShouldConfirmBlockingModal -State $state)) {
                continue
            }

            $response = Invoke-ApiJson -BaseUrl $url -Method "POST" -Path "/action" -Body @{ action = "confirm_modal" } -TimeoutSec 5 -RetryCount 1 -RetryDelayMs 0
            if ($response.ok) {
                Write-Host "==> confirmed blocking modal on $url type=$($state.modal.type_name)"
            }
        }
        catch {
        }
    }
}

function Advance-CombatTurnIfNeeded {
    param(
        [string[]]$BaseUrls,
        [int]$MinTurn
    )

    if ($MinTurn -le 0) {
        return
    }

    foreach ($url in $BaseUrls) {
        if ([string]::IsNullOrWhiteSpace($url)) {
            continue
        }

        try {
            $payload = Invoke-ApiJson -BaseUrl $url -Method "GET" -Path "/state" -TimeoutSec 2 -RetryCount 1 -RetryDelayMs 0
            $state = $payload.data
            if (-not [bool]$state.in_combat) {
                continue
            }

            $turn = 0
            [void][int]::TryParse([string]$state.turn, [ref]$turn)
            if ($turn -ge $MinTurn) {
                continue
            }

            if (@($state.available_actions) -contains "end_turn") {
                $response = Invoke-ApiJson -BaseUrl $url -Method "POST" -Path "/action" -Body @{ action = "end_turn" } -TimeoutSec 5 -RetryCount 1 -RetryDelayMs 0
                if ($response.ok) {
                    Write-Host "==> end_turn retry on $url turn=$turn"
                }
            }
        }
        catch {
        }
    }
}

function Wait-ForState {
    param(
        [string]$BaseUrl,
        [string]$Description,
        [scriptblock]$Condition,
        [int]$PollAttempts = 180,
        [int]$PollDelayMs = 250,
        [int]$EndTurnBelowTurn = 0
    )

    $state = $null
    for ($attempt = 0; $attempt -lt $PollAttempts; $attempt++) {
        try {
            $state = Get-State -BaseUrl $BaseUrl
        }
        catch {
            Save-TimeoutStateDump -TimedOutBaseUrl $BaseUrl -Description $Description -LastState $state -ErrorMessage $_.Exception.Message
            throw
        }

        $modalType = [string]$state.modal.type_name
        if ([string]$state.screen -eq "MODAL" -and ($modalType -match "NErrorPopup" -or $modalType -match "ErrorPopup")) {
            Save-ErrorPopupAndFail -BaseUrl $BaseUrl -State $state
        }

        if (& $Condition $state) {
            return $state
        }

        Clear-BlockingFtueModals -BaseUrls @($hostBaseUrl, $clientBaseUrl)
        if ([string]$state.screen -eq "CARD_SELECTION") {
            try { [void](Invoke-LocalRunProgressionStep -BaseUrl $BaseUrl -State $state) } catch { }
        }
        Advance-CombatTurnIfNeeded -BaseUrls @($hostBaseUrl, $clientBaseUrl) -MinTurn $EndTurnBelowTurn
        Start-Sleep -Milliseconds $PollDelayMs
    }

    Save-TimeoutStateDump -TimedOutBaseUrl $BaseUrl -Description $Description -LastState $state
    throw "Timed out waiting for state at ${BaseUrl}: $Description"
}

function Resolve-BlockingModal {
    param(
        [string]$BaseUrl,
        [int]$MaxAttempts = 12
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

        $code = [string]$lastResponse.error.code
        $isRetryable = $code -eq "internal_error" -or $code -eq "invalid_action" -or $code -eq "state_unavailable"
        $hasRetriesRemaining = $attempt -lt ($RetryCount - 1)
        if (-not $isRetryable -or -not $hasRetriesRemaining) {
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
    param(
        [string]$BaseUrl,
        [int]$RetryCount = 4
    )

    $scriptPath = Join-Path $scriptRoot "test-state-invariants.ps1"
    $lastExit = 1
    for ($attempt = 0; $attempt -lt $RetryCount; $attempt++) {
        Clear-BlockingFtueModals -BaseUrls @($hostBaseUrl, $clientBaseUrl)
        & powershell -ExecutionPolicy Bypass -File $scriptPath -BaseUrl $BaseUrl
        $lastExit = $LASTEXITCODE
        if ($lastExit -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 400
    }

    throw "test-state-invariants.ps1 failed for $BaseUrl"
}

function Wait-ForCardPlayResolved {
    param(
        [string]$BaseUrl,
        [string]$Description
    )

    return Wait-ForState -BaseUrl $BaseUrl -Description $Description -Condition {
        param($CurrentState)
        if ($CurrentState.screen -eq "CARD_SELECTION" -or $CurrentState.screen -eq "MODAL") {
            return $false
        }

        return $CurrentState.screen -eq "COMBAT" -and
            $CurrentState.in_combat -and
            $CurrentState.combat.player.cards_played_this_turn -ge 1
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

function Test-CombatHasPlayableCard {
    param([object]$State)

    if ($null -eq $State.combat) {
        return $false
    }

    foreach ($card in @($State.combat.hand)) {
        if (-not $card.playable) {
            continue
        }

        if ($card.requires_target -and @($card.valid_target_indices).Count -eq 0) {
            continue
        }

        return $true
    }

    return $false
}

function Wait-ForCombatPlayable {
    param(
        [string]$BaseUrl,
        [string]$Description
    )

    return Wait-ForState -BaseUrl $BaseUrl -Description $Description -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        $null -ne $CurrentState.combat -and
        @($CurrentState.combat.enemies).Count -ge 1 -and
        @($CurrentState.available_actions) -contains "play_card" -and
        (Test-CombatHasPlayableCard -State $CurrentState)
    }
}

function Invoke-NaturalCombatUntilReward {
    param(
        [string]$HostBaseUrl,
        [string]$ClientBaseUrl,
        [int]$TimeoutSeconds = 180
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $hostState = Get-State -BaseUrl $HostBaseUrl
        $clientState = Get-State -BaseUrl $ClientBaseUrl
        foreach ($pair in @(
                @{ url = $HostBaseUrl; state = $hostState },
                @{ url = $ClientBaseUrl; state = $clientState }
            )) {
            $modalType = [string]$pair.state.modal.type_name
            if ([string]$pair.state.screen -eq "MODAL" -and ($modalType -match "NErrorPopup" -or $modalType -match "ErrorPopup")) {
                Save-ErrorPopupAndFail -BaseUrl $pair.url -State $pair.state
            }
        }

        if ([string]$hostState.screen -eq "REWARD" -and [string]$clientState.screen -eq "REWARD" -and
            [string]$hostState.run_id -eq [string]$clientState.run_id -and
            -not [string]::IsNullOrWhiteSpace([string]$hostState.run_id) -and
            [string]$hostState.run_id -ne "run_unknown") {
            Write-Host ("==> natural reward host turn={0} client turn={1}" -f $hostState.turn, $clientState.turn)
            return @{
                Host = $hostState
                Client = $clientState
            }
        }

        Clear-BlockingFtueModals -BaseUrls @($HostBaseUrl, $ClientBaseUrl)

        foreach ($pair in @(
                @{ url = $HostBaseUrl; state = $hostState; name = "host" },
                @{ url = $ClientBaseUrl; state = $clientState; name = "client" }
            )) {
            $state = $pair.state
            if ([string]$state.screen -ne "COMBAT" -or -not [bool]$state.in_combat) {
                continue
            }

            $actions = @($state.available_actions)
            $ready = $false
            try { $ready = [bool]$state.combat.action_readiness.can_use_combat_actions } catch { $ready = $true }
            if (-not $ready) {
                continue
            }

            if (($actions -contains "play_card") -and (Test-CombatHasPlayableCard -State $state)) {
                try {
                    $payload = Get-FirstPlayableCardPayload -State $state
                    $response = Invoke-Action -BaseUrl $pair.url -Payload $payload
                    if ($response.ok) {
                        Write-Host ("==> {0} play_card turn={1}" -f $pair.name, $state.turn)
                    }
                } catch {
                }
                continue
            }

            if ($actions -contains "end_turn") {
                try {
                    $response = Invoke-Action -BaseUrl $pair.url -Payload @{ action = "end_turn" }
                    if ($response.ok) {
                        Write-Host ("==> {0} end_turn turn={1}" -f $pair.name, $state.turn)
                    }
                } catch {
                }
            }
        }

        Start-Sleep -Milliseconds 350
    }

    Save-TimeoutStateDump -TimedOutBaseUrl $HostBaseUrl -Description "natural combat until both REWARD" -LastState (Get-State -BaseUrl $HostBaseUrl)
    throw "Timed out waiting for both sides to reach natural REWARD without debug-win"
}

function Wait-ForCombatAction {
    param(
        [string]$BaseUrl,
        [string]$ActionName,
        [string]$Description
    )

    return Wait-ForState -BaseUrl $BaseUrl -Description $Description -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "COMBAT" -and
        $CurrentState.in_combat -and
        $CurrentState.combat.action_readiness.can_use_combat_actions -eq $true -and
        @($CurrentState.available_actions) -contains $ActionName
    }
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
        "BUNDLE_SELECTION" {
            if ($actions -contains "confirm_bundle") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{ action = "confirm_bundle" }
            }

            if ($actions -contains "choose_bundle") {
                return Invoke-Action -BaseUrl $BaseUrl -Payload @{
                    action = "choose_bundle"
                    option_index = 0
                }
            }
        }
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

    $baseUrl = "http://127.0.0.1:$ApiPort"
    [void](Wait-ForState -BaseUrl $baseUrl -Description "MAIN_MENU or startup modal on port $ApiPort" -Condition {
            param($CurrentState)
            $actions = @($CurrentState.available_actions)
            (
                $CurrentState.screen -eq "MAIN_MENU" -and $actions.Count -gt 0
            ) -or (
                $CurrentState.screen -eq "MODAL" -and (
                    $actions -contains "confirm_modal" -or $actions -contains "dismiss_modal"
                )
            )
        } -PollAttempts 120 -PollDelayMs 500)
    [void](Resolve-BlockingModal -BaseUrl $baseUrl)
    [void](Wait-ForState -BaseUrl $baseUrl -Description "MAIN_MENU ready for debug commands on port $ApiPort" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions).Count -gt 0
        } -PollAttempts 80 -PollDelayMs 250)

    if ($latestProcess?.Id) { [void]$script:KnownTestPids.Add([int]$latestProcess.Id) }
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

    $hostCombatState = Wait-ForCombatPlayable -BaseUrl $hostBaseUrl -Description "host combat ready"
    $clientCombatState = Wait-ForCombatPlayable -BaseUrl $clientBaseUrl -Description "client combat ready"

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> host plays a combat card"
    $hostCombatState = Wait-ForCombatPlayable -BaseUrl $hostBaseUrl -Description "host can play a card"
    $hostPlayPayload = Get-FirstPlayableCardPayload -State $hostCombatState
    $hostPlayResponse = Invoke-Action -BaseUrl $hostBaseUrl -Payload $hostPlayPayload
    if (-not $hostPlayResponse.ok) {
        throw "Host play_card failed: $($hostPlayResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $hostAfterPlayState = Wait-ForCardPlayResolved -BaseUrl $hostBaseUrl -Description "host card resolved"

    Write-Host "==> client plays a combat card"
    $clientCombatState = Wait-ForCombatPlayable -BaseUrl $clientBaseUrl -Description "client can play a card after host play"
    $clientPlayPayload = Get-FirstPlayableCardPayload -State $clientCombatState
    $clientPlayResponse = Invoke-Action -BaseUrl $clientBaseUrl -Payload $clientPlayPayload
    if (-not $clientPlayResponse.ok) {
        throw "Client play_card failed: $($clientPlayResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $clientAfterPlayState = Wait-ForCardPlayResolved -BaseUrl $clientBaseUrl -Description "client card resolved"

    Write-Host "==> host and client end turn"
    [void](Wait-ForCombatAction -BaseUrl $hostBaseUrl -ActionName "end_turn" -Description "host can end turn")
    $hostEndTurnResponse = Invoke-ActionExpectOk -BaseUrl $hostBaseUrl -Description "Host end_turn" -RetryCount 4 -RetryDelayMs 400 -Payload @{ action = "end_turn" }

    [void](Wait-ForCombatAction -BaseUrl $clientBaseUrl -ActionName "end_turn" -Description "client can end turn")
    $clientEndTurnResponse = Invoke-ActionExpectOk -BaseUrl $clientBaseUrl -Description "Client end_turn" -RetryCount 4 -RetryDelayMs 400 -Payload @{ action = "end_turn" }

    $deadline = (Get-Date).AddMinutes(2)
    $hostTurnTwoState = $null
    $clientTurnTwoState = $null
    do {
        $hostTurnTwoState = Get-State -BaseUrl $hostBaseUrl
        $clientTurnTwoState = Get-State -BaseUrl $clientBaseUrl
        foreach ($pair in @(
                @{ url = $hostBaseUrl; state = $hostTurnTwoState },
                @{ url = $clientBaseUrl; state = $clientTurnTwoState }
            )) {
            $modalType = [string]$pair.state.modal.type_name
            if ([string]$pair.state.screen -eq "MODAL" -and ($modalType -match "NErrorPopup" -or $modalType -match "ErrorPopup")) {
                Save-ErrorPopupAndFail -BaseUrl $pair.url -State $pair.state
            }
        }

        $bothCombatTurnTwo = (
            [string]$hostTurnTwoState.screen -eq "COMBAT" -and [bool]$hostTurnTwoState.in_combat -and [int]$hostTurnTwoState.turn -ge 2 -and
            [string]$clientTurnTwoState.screen -eq "COMBAT" -and [bool]$clientTurnTwoState.in_combat -and [int]$clientTurnTwoState.turn -ge 2
        )
        $bothNaturalReward = (
            [string]$hostTurnTwoState.screen -eq "REWARD" -and [string]$clientTurnTwoState.screen -eq "REWARD" -and
            [string]$hostTurnTwoState.run_id -eq [string]$clientTurnTwoState.run_id -and
            -not [string]::IsNullOrWhiteSpace([string]$hostTurnTwoState.run_id) -and
            [string]$hostTurnTwoState.run_id -ne "run_unknown"
        )
        if ($bothCombatTurnTwo -or $bothNaturalReward) {
            break
        }

        Clear-BlockingFtueModals -BaseUrls @($hostBaseUrl, $clientBaseUrl)
        Advance-CombatTurnIfNeeded -BaseUrls @($hostBaseUrl, $clientBaseUrl) -MinTurn 2
        Start-Sleep -Milliseconds 250
        $hostTurnTwoState = $null
    } while ((Get-Date) -lt $deadline)

    if ($null -eq $hostTurnTwoState) {
        $hostTurnTwoState = Get-State -BaseUrl $hostBaseUrl
        $clientTurnTwoState = Get-State -BaseUrl $clientBaseUrl
        Save-TimeoutStateDump -TimedOutBaseUrl $hostBaseUrl -Description "both sides COMBAT turn>=2 or both natural REWARD" -LastState $hostTurnTwoState
        throw "Timed out waiting for both sides COMBAT turn>=2 or both natural REWARD"
    }

    $dumpDir = [Environment]::GetEnvironmentVariable("STS2_STATE_DUMP_DIR", "Process")
    if (-not [string]::IsNullOrWhiteSpace($dumpDir)) {
        New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null
        $hostTurnTwoState | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $dumpDir "host-turn2-or-reward.json") -Encoding UTF8
        $clientTurnTwoState | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $dumpDir "client-turn2-or-reward.json") -Encoding UTF8
    }
    Write-Host ("==> end-turn accepted host screen={0} turn={1} client screen={2} turn={3}" -f $hostTurnTwoState.screen, $hostTurnTwoState.turn, $clientTurnTwoState.screen, $clientTurnTwoState.turn)

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    $hostRewardState = $null
    $clientRewardState = $null
    if ([string]$hostTurnTwoState.screen -eq "REWARD" -and [string]$clientTurnTwoState.screen -eq "REWARD") {
        Write-Host "==> combat ended naturally after end_turn; skipping later debug fixture"
        $hostRewardState = $hostTurnTwoState
        $clientRewardState = $clientTurnTwoState
    }
    else {
        Write-Host "==> continue natural combat until both REWARD; debug-win is not used"
        $natural = Invoke-NaturalCombatUntilReward -HostBaseUrl $hostBaseUrl -ClientBaseUrl $clientBaseUrl
        $hostRewardState = $natural.Host
        $clientRewardState = $natural.Client
    }

    if ($null -eq $hostRewardState) {
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
    }

    Invoke-StateInvariantScript -BaseUrl $hostBaseUrl
    Invoke-StateInvariantScript -BaseUrl $clientBaseUrl

    Write-Host "==> host and client resolve reward flow"
    $hostResolveRewardResponse = Invoke-ActionExpectOk -BaseUrl $hostBaseUrl -Description "Host resolve_rewards" -RetryCount 6 -RetryDelayMs 500 -Payload @{ action = "resolve_rewards" }
    $clientResolveRewardResponse = Invoke-ActionExpectOk -BaseUrl $clientBaseUrl -Description "Client resolve_rewards" -RetryCount 6 -RetryDelayMs 500 -Payload @{ action = "resolve_rewards" }

    $deadline = (Get-Date).AddMinutes(2)
    $hostPostRewardMapState = $null
    $clientPostRewardMapState = $null
    do {
        $hostPostRewardMapState = Get-State -BaseUrl $hostBaseUrl
        $clientPostRewardMapState = Get-State -BaseUrl $clientBaseUrl
        foreach ($pair in @(
                @{ url = $hostBaseUrl; state = $hostPostRewardMapState },
                @{ url = $clientBaseUrl; state = $clientPostRewardMapState }
            )) {
            $modalType = [string]$pair.state.modal.type_name
            if ([string]$pair.state.screen -eq "MODAL" -and ($modalType -match "NErrorPopup" -or $modalType -match "ErrorPopup")) {
                Save-ErrorPopupAndFail -BaseUrl $pair.url -State $pair.state
            }
        }

        $bothOnMap = (
            [string]$hostPostRewardMapState.screen -eq "MAP" -and [string]$clientPostRewardMapState.screen -eq "MAP" -and
            @($hostPostRewardMapState.available_actions) -contains "choose_map_node" -and
            @($clientPostRewardMapState.available_actions) -contains "choose_map_node"
        )
        if ($bothOnMap) { break }

        Clear-BlockingFtueModals -BaseUrls @($hostBaseUrl, $clientBaseUrl)
        foreach ($url in @($hostBaseUrl, $clientBaseUrl)) {
            try {
                $state = if ($url -eq $hostBaseUrl) { $hostPostRewardMapState } else { $clientPostRewardMapState }
                if ([string]$state.screen -in @("REWARD", "MODAL", "CARD_SELECTION")) {
                    [void](Invoke-LocalRunProgressionStep -BaseUrl $url -State $state)
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 400
        $hostPostRewardMapState = $null
    } while ((Get-Date) -lt $deadline)

    if ($null -eq $hostPostRewardMapState) {
        $hostPostRewardMapState = Get-State -BaseUrl $hostBaseUrl
        $clientPostRewardMapState = Get-State -BaseUrl $clientBaseUrl
        Save-TimeoutStateDump -TimedOutBaseUrl $hostBaseUrl -Description "both returned to map after rewards" -LastState $hostPostRewardMapState
        throw "Timed out waiting for both sides to return to MAP after rewards"
    }
    Write-Host ("==> post-reward map host={0} client={1}" -f $hostPostRewardMapState.screen, $clientPostRewardMapState.screen)

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



