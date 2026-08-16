param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [int]$TimeoutSec = 15,
    [int]$RequestRetries = 3,
    [int]$RetryDelayMs = 500,
    [int]$PollAttempts = 120,
    [int]$PollDelayMs = 250
)

$ErrorActionPreference = "Stop"

function Invoke-ApiJson {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null
    )

    $uri = $BaseUrl.TrimEnd("/") + $Path

    for ($attempt = 0; $attempt -lt $RequestRetries; $attempt++) {
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

            $isRetriable = $_.Exception -is [System.Net.WebException] -and (
                $_.Exception.Status -eq [System.Net.WebExceptionStatus]::Timeout -or
                $_.Exception.Status -eq [System.Net.WebExceptionStatus]::ConnectFailure
            )

            if ($isRetriable -and $attempt -lt ($RequestRetries - 1)) {
                Start-Sleep -Milliseconds $RetryDelayMs
                continue
            }

            throw
        }
    }
}

function Get-State {
    return (Invoke-ApiJson -Method "GET" -Path "/state").data
}

function Invoke-Action {
    param([hashtable]$Payload)
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
    param([string]$Command)
    $response = Invoke-Action -Payload @{ action = "run_console_command"; command = $Command }
    if (-not $response.ok) {
        throw "Debug command '$Command' failed: $($response | ConvertTo-Json -Depth 8 -Compress)"
    }

    return $response
}

[void](Invoke-ApiJson -Method "GET" -Path "/health")

$state = Wait-ForState -Description "active-run MAIN_MENU" -Condition {
    param($CurrentState)
    $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions) -contains "abandon_run"
}

$abandonResponse = Invoke-Action -Payload @{ action = "abandon_run" }
if (-not $abandonResponse.ok) {
    throw "abandon_run failed: $($abandonResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$modalState = $abandonResponse.data.state
Assert-ActionAvailable -State $modalState -ActionName "confirm_modal"

$confirmAbandonResponse = Invoke-Action -Payload @{ action = "confirm_modal" }
if (-not $confirmAbandonResponse.ok) {
    throw "confirm_modal failed for abandon_run: $($confirmAbandonResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$menuWithoutRun = Wait-ForState -Description "MAIN_MENU without active run" -Condition {
    param($CurrentState)
    $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions) -contains "open_character_select"
}

$openCharacterSelectResponse = Invoke-Action -Payload @{ action = "open_character_select" }
if (-not $openCharacterSelectResponse.ok) {
    throw "open_character_select failed: $($openCharacterSelectResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$characterSelectState = $openCharacterSelectResponse.data.state
$modalAttempts = 0
while ($characterSelectState.screen -eq "MODAL") {
    $modalAttempts += 1
    if ($modalAttempts -gt 8) {
        throw "Too many modals after open_character_select: $($characterSelectState | ConvertTo-Json -Depth 8 -Compress)"
    }

    $modalAction = if (@($characterSelectState.available_actions) -contains "confirm_modal") { "confirm_modal" } else { "dismiss_modal" }
    if (-not (@($characterSelectState.available_actions) -contains $modalAction)) {
        throw "MODAL after open_character_select has no confirm/dismiss action: $($characterSelectState | ConvertTo-Json -Depth 8 -Compress)"
    }

    $modalResponse = Invoke-Action -Payload @{ action = $modalAction }
    if (-not $modalResponse.ok) {
        throw "$modalAction failed after open_character_select: $($modalResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $characterSelectState = $modalResponse.data.state
    if ($characterSelectState.screen -eq "MODAL") {
        $characterSelectState = Wait-ForState -Description "leave open_character_select modal or reach CHARACTER_SELECT" -Condition {
            param($CurrentState)
            $CurrentState.screen -ne "MODAL" -or (@($CurrentState.available_actions) -contains "confirm_modal") -or (@($CurrentState.available_actions) -contains "dismiss_modal")
        }
    }
}

if ($characterSelectState.screen -ne "CHARACTER_SELECT" -or $null -eq $characterSelectState.character_select) {
    $characterSelectState = Wait-ForState -Description "CHARACTER_SELECT" -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "CHARACTER_SELECT" -and $null -ne $CurrentState.character_select
    }
}

$characters = @($characterSelectState.character_select.characters | Where-Object { -not $_.is_locked })
if ($characters.Count -eq 0) {
    throw "Expected at least one unlocked character, but state was: $($characterSelectState | ConvertTo-Json -Depth 8 -Compress)"
}

$selectedCharacter = $characters[0]
$selectCharacterResponse = Invoke-Action -Payload @{ action = "select_character"; option_index = [int]$selectedCharacter.index }
if (-not $selectCharacterResponse.ok) {
    throw "select_character failed: $($selectCharacterResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$embarkableState = Wait-ForState -Description "character select can embark" -Condition {
    param($CurrentState)
    $CurrentState.screen -eq "CHARACTER_SELECT" -and $null -ne $CurrentState.character_select -and [bool]$CurrentState.character_select.can_embark
}

$embarkResponse = Invoke-Action -Payload @{ action = "embark" }
if (-not $embarkResponse.ok) {
    throw "embark failed: $($embarkResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$runState = Wait-ForState -Description "leave CHARACTER_SELECT into a run" -Condition {
    param($CurrentState)
    $CurrentState.screen -ne "CHARACTER_SELECT"
}

while ($runState.screen -eq "MODAL") {
    $action = if (@($runState.available_actions) -contains "confirm_modal") { "confirm_modal" } else { "dismiss_modal" }
    $modalResponse = Invoke-Action -Payload @{ action = $action }
    if (-not $modalResponse.ok) {
        throw "$action failed after embark: $($modalResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $runState = Wait-ForState -Description "leave embark modal" -Condition {
        param($CurrentState)
        $CurrentState.screen -ne "MODAL"
    }
}

Invoke-DebugCommand -Command "die" | Out-Null
$gameOverState = Wait-ForState -Description "GAME_OVER" -Condition {
    param($CurrentState)
    $CurrentState.screen -eq "GAME_OVER" -and $null -ne $CurrentState.game_over -and [bool]$CurrentState.game_over.can_return_to_main_menu
}

$returnResponse = Invoke-Action -Payload @{ action = "return_to_main_menu" }
if (-not $returnResponse.ok) {
    throw "return_to_main_menu failed: $($returnResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$finalMenuState = Wait-ForState -Description "MAIN_MENU after game over" -Condition {
    param($CurrentState)
    $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions) -contains "open_character_select"
}

[pscustomobject]@{
    selected_character_id = $selectedCharacter.character_id
    embark_destination = $runState.screen
    game_over_actions = @($gameOverState.available_actions)
    final_menu_actions = @($finalMenuState.available_actions)
} | ConvertTo-Json -Depth 6
