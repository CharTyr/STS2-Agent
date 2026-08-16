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
        throw "Expected action '$ActionName' to be available, but state was: $($State | ConvertTo-Json -Depth 6 -Compress)"
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
        throw "Debug command '$Command' failed: $($response | ConvertTo-Json -Depth 6 -Compress)"
    }

    return $response
}

[void](Invoke-ApiJson -Method "GET" -Path "/health")

$state = Get-State

if ($state.screen -eq "CARD_SELECTION") {
    throw "test-deferred-potion-flow.ps1 expects a stable starting screen, but current screen is CARD_SELECTION."
}

if ($state.screen -eq "MAIN_MENU") {
    Assert-ActionAvailable -State $state -ActionName "continue_run"
    $continueResponse = Invoke-Action -Payload @{ action = "continue_run" }
    if (-not $continueResponse.ok) {
        throw "continue_run failed: $($continueResponse | ConvertTo-Json -Depth 6 -Compress)"
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
        throw "collect_rewards_and_proceed failed: $($rewardResponse | ConvertTo-Json -Depth 6 -Compress)"
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

Invoke-DebugCommand -Command "card STRIKE_DEFECT discard" | Out-Null
Invoke-DebugCommand -Command "card DEFEND_DEFECT discard" | Out-Null
Invoke-DebugCommand -Command "potion LIQUID_MEMORIES" | Out-Null

$state = Wait-ForState -Description "LIQUID_MEMORIES usable with use_potion available" -Condition {
    param($CurrentState)
    $CurrentState.in_combat -and
    $CurrentState.screen -eq "COMBAT" -and
    (@($CurrentState.available_actions) -contains "use_potion") -and
    (@($CurrentState.run.potions | Where-Object { $_.occupied -and $_.potion_id -eq "LIQUID_MEMORIES" -and $_.can_use }).Count -gt 0)
}
$liquidMemoriesPotion = @($state.run.potions | Where-Object { $_.occupied -and $_.potion_id -eq "LIQUID_MEMORIES" } | Select-Object -First 1)
if ($null -eq $liquidMemoriesPotion) {
    throw "Failed to inject LIQUID_MEMORIES potion into the current run state."
}

$usePotionResponse = Invoke-Action -Payload @{
    action = "use_potion"
    option_index = [int]$liquidMemoriesPotion.index
}

if (-not $usePotionResponse.ok) {
    throw "use_potion failed: $($usePotionResponse | ConvertTo-Json -Depth 6 -Compress)"
}

if ($usePotionResponse.data.status -ne "pending" -or [bool]$usePotionResponse.data.stable) {
    throw "Expected LIQUID_MEMORIES to return pending while awaiting selection, but received: $($usePotionResponse | ConvertTo-Json -Depth 6 -Compress)"
}

$selectionState = $usePotionResponse.data.state
if ($selectionState.screen -ne "CARD_SELECTION") {
    throw "Expected use_potion state.screen=CARD_SELECTION, but received: $($usePotionResponse | ConvertTo-Json -Depth 6 -Compress)"
}

if (-not (@($selectionState.available_actions) -contains "select_deck_card")) {
    throw "Expected select_deck_card to be available during LIQUID_MEMORIES selection, but received: $($usePotionResponse | ConvertTo-Json -Depth 6 -Compress)"
}

$selectionCards = @($selectionState.selection.cards)
if ($selectionCards.Count -lt 2) {
    throw "Expected LIQUID_MEMORIES to expose at least two discard options, but received: $($usePotionResponse | ConvertTo-Json -Depth 6 -Compress)"
}

$selectedCard = $selectionCards[0]
$selectResponse = Invoke-Action -Payload @{
    action = "select_deck_card"
    option_index = 0
}

if (-not $selectResponse.ok) {
    throw "select_deck_card failed after LIQUID_MEMORIES: $($selectResponse | ConvertTo-Json -Depth 6 -Compress)"
}

$finalState = if ($selectResponse.data.state.in_combat -and $selectResponse.data.state.screen -eq "COMBAT") {
    $selectResponse.data.state
}
else {
    Wait-ForState -Description "resolve LIQUID_MEMORIES selection back to COMBAT" -Condition {
        param($CurrentState)
        $CurrentState.in_combat -and $CurrentState.screen -eq "COMBAT"
    }
}

$zeroCostMatches = @($finalState.combat.hand | Where-Object {
    $_.card_id -eq $selectedCard.card_id -and [int]$_.energy_cost -eq 0
})

if ($zeroCostMatches.Count -eq 0) {
    throw "Expected selected card '$($selectedCard.card_id)' to return to hand at 0 cost, but final state was: $($finalState | ConvertTo-Json -Depth 6 -Compress)"
}

[pscustomobject]@{
    screen = $finalState.screen
    selected_card_id = $selectedCard.card_id
    selected_card_zero_cost = $true
    initial_status = $usePotionResponse.data.status
    initial_screen = $selectionState.screen
    selection_count = $selectionCards.Count
} | ConvertTo-Json -Depth 5
