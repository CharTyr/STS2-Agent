param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [int]$TimeoutSec = 5,
    [int]$PollAttempts = 60,
    [int]$PollDelayMs = 200
)

$ErrorActionPreference = "Stop"

function Invoke-ApiJson {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null
    )

    $uri = $BaseUrl.TrimEnd("/") + $Path

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

        throw
    }
}

function Get-State {
    return (Invoke-ApiJson -Method "GET" -Path "/state").data
}

function Invoke-Action {
    param(
        [hashtable]$Payload
    )

    return Invoke-ApiJson -Method "POST" -Path "/action" -Body $Payload
}

function Wait-ForState {
    param(
        [string]$Description,
        [scriptblock]$Condition
    )

    for ($attempt = 0; $attempt -lt $PollAttempts; $attempt++) {
        $state = Get-State
        if (& $Condition $state) {
            return $state
        }

        Start-Sleep -Milliseconds $PollDelayMs
    }

    throw "Timed out waiting for state: $Description"
}

function Assert-ActionAvailable {
    param(
        $State,
        [string]$ActionName
    )

    if (-not (@($State.available_actions) -contains $ActionName)) {
        throw "Expected action '$ActionName' to be available, but state was: $($State | ConvertTo-Json -Depth 8 -Compress)"
    }
}

function Invoke-DebugCommand {
    param(
        [string]$Command
    )

    $response = Invoke-Action -Payload @{
        action = "run_console_command"
        command = $Command
    }

    if (-not $response.ok) {
        throw "Debug command '$Command' failed: $($response | ConvertTo-Json -Depth 8 -Compress)"
    }

    return $response
}

[void](Invoke-ApiJson -Method "GET" -Path "/health")

$state = Get-State
if ($state.screen -eq "CARD_SELECTION") {
    throw "test-combat-hand-confirm-flow.ps1 expects a stable starting screen, but current screen is CARD_SELECTION."
}

if ($state.screen -eq "MAIN_MENU") {
    Assert-ActionAvailable -State $state -ActionName "continue_run"
    $continueResponse = Invoke-Action -Payload @{ action = "continue_run" }
    if (-not $continueResponse.ok) {
        throw "continue_run failed: $($continueResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $state = Wait-ForState -Description "leave MAIN_MENU" -Condition {
        param($CurrentState)
        $CurrentState.screen -ne "MAIN_MENU"
    }
}

while ($state.screen -eq "REWARD") {
    Assert-ActionAvailable -State $state -ActionName "collect_rewards_and_proceed"
    $rewardResponse = Invoke-Action -Payload @{ action = "collect_rewards_and_proceed" }
    if (-not $rewardResponse.ok) {
        throw "collect_rewards_and_proceed failed: $($rewardResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $state = Wait-ForState -Description "leave REWARD" -Condition {
        param($CurrentState)
        $CurrentState.screen -ne "REWARD"
    }
}

if (-not $state.in_combat) {
    Invoke-DebugCommand -Command "room Monster" | Out-Null
    $state = Wait-ForState -Description "enter COMBAT" -Condition {
        param($CurrentState)
        $CurrentState.in_combat -and $CurrentState.screen -eq "COMBAT"
    }
}

Invoke-DebugCommand -Command "card CLAW hand" | Out-Null
Invoke-DebugCommand -Command "card PURITY hand" | Out-Null

$state = Get-State
$purityCard = @($state.combat.hand | Where-Object { $_.card_id -eq "PURITY" } | Select-Object -First 1)
if ($null -eq $purityCard) {
    throw "Failed to inject PURITY into the current combat hand."
}

$playResponse = Invoke-Action -Payload @{
    action = "play_card"
    card_index = [int]$purityCard.index
}

if (-not $playResponse.ok) {
    throw "play_card(PURITY) failed: $($playResponse | ConvertTo-Json -Depth 8 -Compress)"
}

if ($playResponse.data.status -ne "pending" -or [bool]$playResponse.data.stable) {
    throw "Expected PURITY play_card to return pending while awaiting manual selection, but received: $($playResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$selectionState = $playResponse.data.state
if ($selectionState.screen -ne "CARD_SELECTION") {
    throw "Expected PURITY selection to report screen=CARD_SELECTION, but received: $($playResponse | ConvertTo-Json -Depth 8 -Compress)"
}

Assert-ActionAvailable -State $selectionState -ActionName "select_deck_card"
Assert-ActionAvailable -State $selectionState -ActionName "confirm_selection"

if (-not [bool]$selectionState.selection.requires_confirmation) {
    throw "Expected PURITY selection.requires_confirmation=true, but received: $($playResponse | ConvertTo-Json -Depth 8 -Compress)"
}

if (-not [bool]$selectionState.selection.can_confirm) {
    throw "Expected PURITY selection.can_confirm=true before confirmation, but received: $($playResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$selectionCards = @($selectionState.selection.cards)
$targetCard = @($selectionCards | Where-Object { $_.card_id -eq "CLAW" } | Select-Object -First 1)
if ($null -eq $targetCard) {
    $targetCard = @($selectionCards | Select-Object -First 1)
}

if ($null -eq $targetCard) {
    throw "Expected PURITY selection to expose at least one selectable card, but received: $($playResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$selectResponse = Invoke-Action -Payload @{
    action = "select_deck_card"
    option_index = [int]$targetCard.index
}

if (-not $selectResponse.ok) {
    throw "select_deck_card failed during PURITY flow: $($selectResponse | ConvertTo-Json -Depth 8 -Compress)"
}

if ($selectResponse.data.status -ne "pending" -or [bool]$selectResponse.data.stable) {
    throw "Expected PURITY select_deck_card to stay pending until confirmation, but received: $($selectResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$afterSelectState = $selectResponse.data.state
if ($afterSelectState.screen -ne "CARD_SELECTION") {
    throw "Expected PURITY flow to remain in CARD_SELECTION after selecting a card, but received: $($selectResponse | ConvertTo-Json -Depth 8 -Compress)"
}

Assert-ActionAvailable -State $afterSelectState -ActionName "confirm_selection"
if ([int]$afterSelectState.selection.selected_count -lt 1) {
    throw "Expected PURITY selection.selected_count to increase after choosing a card, but received: $($selectResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$confirmResponse = Invoke-Action -Payload @{ action = "confirm_selection" }
if (-not $confirmResponse.ok) {
    throw "confirm_selection failed during PURITY flow: $($confirmResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$finalState = if ($confirmResponse.data.state.in_combat -and $confirmResponse.data.state.screen -eq "COMBAT") {
    $confirmResponse.data.state
}
else {
    Wait-ForState -Description "resolve PURITY selection back to COMBAT" -Condition {
        param($CurrentState)
        $CurrentState.in_combat -and $CurrentState.screen -eq "COMBAT"
    }
}

if (@($finalState.combat.hand | Where-Object { $_.card_id -eq $targetCard.card_id }).Count -gt 0) {
    throw "Expected selected card '$($targetCard.card_id)' to be exhausted by PURITY, but final state was: $($finalState | ConvertTo-Json -Depth 8 -Compress)"
}

[pscustomobject]@{
    action = "PURITY"
    selected_card_id = $targetCard.card_id
    initial_status = $playResponse.data.status
    post_select_status = $selectResponse.data.status
    confirm_status = $confirmResponse.data.status
    final_screen = $finalState.screen
    final_hand_count = @($finalState.combat.hand).Count
} | ConvertTo-Json -Depth 5