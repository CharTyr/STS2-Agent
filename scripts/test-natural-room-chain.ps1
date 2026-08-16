param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [int]$TimeoutSec = 15,
    [int]$RequestRetries = 3,
    [int]$RetryDelayMs = 500,
    [int]$PollAttempts = 160,
    [int]$PollDelayMs = 250,
    [int]$MaxSteps = 160
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

    if ($Payload.action -eq "run_console_command") {
        throw "test-natural-room-chain.ps1 must not use run_console_command."
    }

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

function Resolve-Modals {
    param($State)

    $current = $State
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        if ($current.screen -ne "MODAL") {
            return $current
        }

        $action = if (@($current.available_actions) -contains "confirm_modal") { "confirm_modal" } else { "dismiss_modal" }
        if (-not (@($current.available_actions) -contains $action)) {
            throw "MODAL has no confirm/dismiss action: $($current | ConvertTo-Json -Depth 8 -Compress)"
        }

        $response = Invoke-Action -Payload @{ action = $action }
        if (-not $response.ok) {
            throw "$action failed: $($response | ConvertTo-Json -Depth 8 -Compress)"
        }

        $current = Wait-ForState -Description "leave modal" -Condition {
            param($CurrentState)
            $CurrentState.screen -ne "MODAL"
        }
    }

    throw "Too many stacked modals: $($current | ConvertTo-Json -Depth 8 -Compress)"
}

function Get-FirstPlayableCardPayload {
    param($State)

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

    return $null
}

function Select-MapNode {
    param($State)

    $nodes = @($State.map.available_nodes)
    if ($nodes.Count -eq 0) {
        return $null
    }

    foreach ($preferredType in @("Monster", "Elite", "Shop", "RestSite", "Treasure", "Unknown", "Event")) {
        $match = @($nodes | Where-Object { [string]$_.node_type -like "*$preferredType*" } | Select-Object -First 1)
        if ($match.Count -gt 0) {
            return $match[0]
        }
    }

    return $nodes[0]
}

function Test-IsProgressionAction {
    param([string]$ActionName)

    return @("save_and_quit", "abandon_run", "open_timeline") -notcontains $ActionName
}

function Get-ProgressionActions {
    param($State)

    return @($State.available_actions) | Where-Object { Test-IsProgressionAction -ActionName $_ }
}

function Invoke-RoomStep {
    param($State)

    $actions = @(Get-ProgressionActions -State $State)

    switch ($State.screen) {
        "EVENT" {
            if ($actions -contains "choose_event_option") {
                $optionIndex = if ($State.event.is_finished -or @($State.event.options).Count -eq 1) { 0 } else { 1 }
                return @{ action = "choose_event_option"; option_index = [int]$optionIndex }
            }
            if ($actions -contains "proceed") {
                return @{ action = "proceed" }
            }
        }
        "CARD_SELECTION" {
            if ($actions -contains "select_deck_card") {
                return @{ action = "select_deck_card"; option_index = 0 }
            }
            if ($actions -contains "confirm_selection") {
                return @{ action = "confirm_selection" }
            }
            if ($actions -contains "proceed") {
                return @{ action = "proceed" }
            }
        }
        "COMBAT" {
            if ($actions -contains "play_card") {
                $payload = Get-FirstPlayableCardPayload -State $State
                if ($null -ne $payload) {
                    return $payload
                }
            }
            if ($actions -contains "end_turn") {
                return @{ action = "end_turn" }
            }
        }
        "REWARD" {
            if ($actions -contains "collect_rewards_and_proceed") {
                return @{ action = "collect_rewards_and_proceed" }
            }
            if ($actions -contains "resolve_rewards") {
                return @{ action = "resolve_rewards" }
            }
            if ($actions -contains "skip_reward_cards") {
                return @{ action = "skip_reward_cards" }
            }
            if ($actions -contains "choose_reward_card") {
                return @{ action = "choose_reward_card"; option_index = 0 }
            }
            if ($actions -contains "claim_reward") {
                return @{ action = "claim_reward"; option_index = 0 }
            }
            if ($actions -contains "proceed") {
                return @{ action = "proceed" }
            }
        }
        "SHOP" {
            if ($actions -contains "open_shop_inventory") {
                return @{ action = "open_shop_inventory" }
            }
            if ($actions -contains "close_shop_inventory") {
                return @{ action = "close_shop_inventory" }
            }
            if ($actions -contains "proceed") {
                return @{ action = "proceed" }
            }
        }
        "REST" {
            if ($actions -contains "choose_rest_option") {
                $option = @(
                    $State.rest.options |
                        Where-Object { $_.is_enabled -and -not $_.requires_target } |
                        Select-Object -First 1
                )[0]
                if ($null -eq $option) {
                    $option = @($State.rest.options | Where-Object { $_.is_enabled } | Select-Object -First 1)[0]
                }
                if ($null -eq $option) {
                    throw "REST has choose_rest_option but no enabled option: $($State | ConvertTo-Json -Depth 8 -Compress)"
                }

                $payload = @{ action = "choose_rest_option"; option_index = [int]$option.index }
                if ($option.requires_target) {
                    $targets = @($option.valid_target_indices)
                    if ($targets.Count -eq 0) {
                        throw "Rest option '$($option.option_id)' requires a target but valid_target_indices is empty."
                    }
                    $payload.target_index = [int]$targets[0]
                }
                return $payload
            }
            if ($actions -contains "proceed") {
                return @{ action = "proceed" }
            }
        }
        "CHEST" {
            if ($actions -contains "open_chest") {
                return @{ action = "open_chest" }
            }
            if ($actions -contains "choose_treasure_relic") {
                return @{ action = "choose_treasure_relic"; option_index = 0 }
            }
            if ($actions -contains "proceed") {
                return @{ action = "proceed" }
            }
        }
        "MAP" {
            if ($actions -contains "choose_map_node") {
                $node = Select-MapNode -State $State
                if ($null -eq $node) {
                    throw "MAP has choose_map_node but available_nodes is empty: $($State | ConvertTo-Json -Depth 8 -Compress)"
                }
                return @{ action = "choose_map_node"; option_index = [int]$node.index }
            }
        }
    }

    return $null
}

function Ensure-InRun {
    $state = Resolve-Modals -State (Get-State)

    if ($state.screen -eq "MAIN_MENU" -and @($state.available_actions) -contains "continue_run") {
        $response = Invoke-Action -Payload @{ action = "continue_run" }
        if (-not $response.ok) {
            throw "continue_run failed: $($response | ConvertTo-Json -Depth 8 -Compress)"
        }
        return Resolve-Modals -State (Wait-ForState -Description "leave MAIN_MENU" -Condition {
                param($CurrentState)
                $CurrentState.screen -ne "MAIN_MENU"
            })
    }

    if ($state.screen -eq "MAIN_MENU" -and @($state.available_actions) -contains "open_character_select") {
        $openResponse = Invoke-Action -Payload @{ action = "open_character_select" }
        if (-not $openResponse.ok) {
            throw "open_character_select failed: $($openResponse | ConvertTo-Json -Depth 8 -Compress)"
        }

        return Complete-CharacterSelectAndEmbark -State $openResponse.data.state
    }

    if ($state.screen -eq "CHARACTER_SELECT") {
        return Complete-CharacterSelectAndEmbark -State $state
    }

    if ($state.screen -eq "MAIN_MENU") {
        throw "Unable to enter a run from MAIN_MENU: $($state | ConvertTo-Json -Depth 8 -Compress)"
    }

    return $state
}

function Complete-CharacterSelectAndEmbark {
    param($State)

    $characterSelectState = Resolve-Modals -State $State
    if ($characterSelectState.screen -ne "CHARACTER_SELECT" -or $null -eq $characterSelectState.character_select) {
        $characterSelectState = Wait-ForState -Description "CHARACTER_SELECT" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "CHARACTER_SELECT" -and $null -ne $CurrentState.character_select
        }
    }

    $characters = @($characterSelectState.character_select.characters | Where-Object { -not $_.is_locked })
    if ($characters.Count -eq 0) {
        throw "Expected at least one unlocked character: $($characterSelectState | ConvertTo-Json -Depth 8 -Compress)"
    }

    $selected = $characters[0]
    $selectResponse = Invoke-Action -Payload @{ action = "select_character"; option_index = [int]$selected.index }
    if (-not $selectResponse.ok) {
        throw "select_character failed: $($selectResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    [void](Wait-ForState -Description "character select can embark" -Condition {
            param($CurrentState)
            $CurrentState.screen -eq "CHARACTER_SELECT" -and [bool]$CurrentState.character_select.can_embark
        })

    $embarkResponse = Invoke-Action -Payload @{ action = "embark" }
    if (-not $embarkResponse.ok) {
        throw "embark failed: $($embarkResponse | ConvertTo-Json -Depth 8 -Compress)"
    }

    $runState = Wait-ForState -Description "leave CHARACTER_SELECT into a run" -Condition {
        param($CurrentState)
        $CurrentState.screen -ne "CHARACTER_SELECT"
    }
    return Resolve-Modals -State $runState
}

function Wait-UntilProgressable {
    param(
        [string]$Description = "progression actions"
    )

    $state = Wait-ForState -Description $Description -Condition {
        param($CurrentState)
        $CurrentState.screen -eq "GAME_OVER" -or
        $CurrentState.screen -eq "MODAL" -or
        @(Get-ProgressionActions -State $CurrentState).Count -gt 0
    }
    return Resolve-Modals -State $state
}

[void](Invoke-ApiJson -Method "GET" -Path "/health")

$state = Ensure-InRun
$visited = [System.Collections.Generic.List[string]]::new()
$actionsTaken = [System.Collections.Generic.List[string]]::new()
$choseMapNode = $false
$chosenNodeType = $null
$destinationScreen = $null
$destinationAction = $null

for ($step = 0; $step -lt $MaxSteps; $step++) {
    $state = Wait-UntilProgressable
    if (-not $visited.Contains([string]$state.screen)) {
        $visited.Add([string]$state.screen)
    }

    if ($state.screen -eq "GAME_OVER" -and -not $choseMapNode) {
        throw "Reached GAME_OVER before choosing a map node. Visited: $($visited -join ' -> ')"
    }

    if ($choseMapNode) {
        if ($state.screen -eq "MAP") {
            $state = Wait-ForState -Description "leave MAP after choosing a node" -Condition {
                param($CurrentState)
                $CurrentState.screen -ne "MAP"
            }
            $state = Resolve-Modals -State $state
            continue
        }

        $destinationScreen = [string]$state.screen
        $payload = Invoke-RoomStep -State $state
        if ($null -eq $payload) {
            throw "No progression action after map destination $($state.screen): $($state | ConvertTo-Json -Depth 8 -Compress)"
        }

        $response = Invoke-Action -Payload $payload
        if (-not $response.ok) {
            throw "Destination action '$($payload.action)' failed: $($response | ConvertTo-Json -Depth 8 -Compress)"
        }

        $destinationAction = [string]$payload.action
        $actionsTaken.Add($destinationAction)
        break
    }

    $payload = Invoke-RoomStep -State $state
    if ($null -eq $payload) {
        Start-Sleep -Milliseconds $PollDelayMs
        continue
    }

    if ($payload.action -eq "choose_map_node") {
        $node = Select-MapNode -State $state
        $chosenNodeType = [string]$node.node_type
        $choseMapNode = $true
    }

    $response = Invoke-Action -Payload $payload
    if (-not $response.ok) {
        throw "Action '$($payload.action)' failed: $($response | ConvertTo-Json -Depth 8 -Compress)"
    }

    $actionsTaken.Add([string]$payload.action)

    if ($choseMapNode) {
        $state = Wait-ForState -Description "leave MAP after choosing a node" -Condition {
            param($CurrentState)
            $CurrentState.screen -ne "MAP"
        }
        $state = Resolve-Modals -State $state
    }
}

if (-not $choseMapNode) {
    throw "Did not reach a choosable MAP node within $MaxSteps steps. Visited: $($visited -join ' -> ')"
}

if ([string]::IsNullOrWhiteSpace($destinationScreen) -or [string]::IsNullOrWhiteSpace($destinationAction)) {
    $state = Resolve-Modals -State (Get-State)
    throw "Chose map node '$chosenNodeType' but did not take a destination-room action. Current screen=$($state.screen). Visited: $($visited -join ' -> ')"
}

$roomScreens = @("COMBAT", "SHOP", "REST", "CHEST", "REWARD", "EVENT")
if (-not ($roomScreens -contains $destinationScreen)) {
    throw "Map destination '$destinationScreen' is not a known room screen. Visited: $($visited -join ' -> ')"
}

[pscustomobject]@{
    visited_screens = @($visited)
    actions_taken = @($actionsTaken)
    chosen_node_type = $chosenNodeType
    destination_screen = $destinationScreen
    destination_action = $destinationAction
    used_debug_commands = $false
} | ConvertTo-Json -Depth 6
