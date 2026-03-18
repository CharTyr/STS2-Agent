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
    throw "test-enemy-intents-payload.ps1 expects a stable starting screen, but current screen is CARD_SELECTION."
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

Invoke-DebugCommand -Command "fight BYRDONIS_ELITE" | Out-Null
$state = Wait-ForState -Description "enter BYRDONIS combat" -Condition {
    param($CurrentState)

    if (-not $CurrentState.in_combat -or $CurrentState.screen -ne "COMBAT") {
        return $false
    }

    $enemy = $CurrentState.combat.enemies | Where-Object { $_.enemy_id -eq "BYRDONIS" } | Select-Object -First 1
    return $null -ne $enemy
}

$enemy = $state.combat.enemies | Where-Object { $_.enemy_id -eq "BYRDONIS" } | Select-Object -First 1
if ($null -eq $enemy) {
    throw "Expected BYRDONIS enemy in BYRDONIS_ELITE encounter, but received: $($state.combat.enemies | ConvertTo-Json -Depth 6 -Compress)"
}

if ([string]::IsNullOrWhiteSpace([string]$enemy.move_id)) {
    throw "Expected combat enemy payload to expose move_id, but received: $($enemy | ConvertTo-Json -Depth 6 -Compress)"
}

if ([string]$enemy.intent -ne [string]$enemy.move_id) {
    throw "Expected legacy intent field to stay aligned with move_id for backward compatibility, but received: $($enemy | ConvertTo-Json -Depth 6 -Compress)"
}

$intents = @($enemy.intents)
if ($intents.Count -eq 0) {
    throw "Expected BYRDONIS to expose at least one concrete intent payload, but received: $($enemy | ConvertTo-Json -Depth 8 -Compress)"
}

$attackIntent = $intents | Where-Object { @("Attack", "DeathBlow") -contains [string]$_.intent_type } | Select-Object -First 1
if ($null -eq $attackIntent) {
    throw "Expected BYRDONIS to expose an attack intent payload, but received: $($enemy | ConvertTo-Json -Depth 8 -Compress)"
}

if ([string]::IsNullOrWhiteSpace([string]$attackIntent.label)) {
    throw "Expected attack intent label to be populated, but received: $($attackIntent | ConvertTo-Json -Depth 6 -Compress)"
}

if ($null -eq $attackIntent.damage -or $null -eq $attackIntent.hits -or $null -eq $attackIntent.total_damage) {
    throw "Expected attack intent to expose damage, hits, and total_damage, but received: $($attackIntent | ConvertTo-Json -Depth 6 -Compress)"
}

$damage = [int]$attackIntent.damage
$hits = [int]$attackIntent.hits
$totalDamage = [int]$attackIntent.total_damage

if ($damage -le 0) {
    throw "Expected attack intent damage to be positive, but received: $($attackIntent | ConvertTo-Json -Depth 6 -Compress)"
}

if ($hits -lt 1) {
    throw "Expected attack intent hits to be at least 1, but received: $($attackIntent | ConvertTo-Json -Depth 6 -Compress)"
}

if ($totalDamage -ne ($damage * $hits)) {
    throw "Expected attack intent total_damage to equal damage * hits, but received: $($attackIntent | ConvertTo-Json -Depth 6 -Compress)"
}

[pscustomobject]@{
    enemy_id = $enemy.enemy_id
    move_id = $enemy.move_id
    intent_count = $intents.Count
    attack_intent = [pscustomobject]@{
        intent_type = $attackIntent.intent_type
        label = $attackIntent.label
        damage = $damage
        hits = $hits
        total_damage = $totalDamage
    }
} | ConvertTo-Json -Depth 6
