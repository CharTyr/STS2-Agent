param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [int]$TimeoutSec = 5,
    [int]$PollAttempts = 80,
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

[void](Invoke-ApiJson -Method "GET" -Path "/health")

$state = Wait-ForState -Description "active-run MAIN_MENU" -Condition {
    param($CurrentState)
    $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions) -contains "continue_run"
}

Assert-ActionAvailable -State $state -ActionName "abandon_run"

# The timeline button follows the profile's timeline progress, not the run save alone
# (NMainMenu.UpdateTimelineButtonBehavior has a branch that enables it while a save exists, when no
# epoch has been discovered yet). Live run 2026-09-12: with an active run save and many complete
# epochs, open_timeline was available and worked, so asserting either way would be wrong. Record it
# as a diagnostic; the interactive sequence itself lives in run_sts2_validation.py's lifecycle suite.
$openTimelineAvailable = @($state.available_actions) -contains "open_timeline"

$abandonResponse = Invoke-Action -Payload @{ action = "abandon_run" }
if (-not $abandonResponse.ok) {
    throw "abandon_run failed: $($abandonResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$modalState = $abandonResponse.data.state
if ($modalState.screen -ne "MODAL") {
    throw "Expected abandon_run to open MODAL, but received: $($abandonResponse | ConvertTo-Json -Depth 8 -Compress)"
}

Assert-ActionAvailable -State $modalState -ActionName "confirm_modal"
Assert-ActionAvailable -State $modalState -ActionName "dismiss_modal"

$dismissResponse = Invoke-Action -Payload @{ action = "dismiss_modal" }
if (-not $dismissResponse.ok) {
    throw "dismiss_modal failed: $($dismissResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$menuState = Wait-ForState -Description "return to MAIN_MENU after dismiss_modal" -Condition {
    param($CurrentState)
    $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions) -contains "continue_run"
}

# The timeline interaction sequence (open_timeline / choose_timeline_epoch / confirm_timeline_overlay /
# close_main_menu_submenu) lives in run_sts2_validation.py suite_new_run_lifecycle, which walks it from
# a menu with no run save. Walking it here would contradict the disabled open_timeline above, so the
# active-run suite stops at the menu and hands off to continue_run.

$continueResponse = Invoke-Action -Payload @{ action = "continue_run" }
if (-not $continueResponse.ok) {
    throw "continue_run failed: $($continueResponse | ConvertTo-Json -Depth 8 -Compress)"
}

$runState = Wait-ForState -Description "leave MAIN_MENU via continue_run" -Condition {
    param($CurrentState)
    $CurrentState.screen -ne "MAIN_MENU"
}

[pscustomobject]@{
    initial_menu_actions = @($state.available_actions)
    open_timeline_available_with_active_run = $openTimelineAvailable
    menu_after_dismiss_actions = @($menuState.available_actions)
    continue_run_destination = $runState.screen
    final_available_actions = @($runState.available_actions)
} | ConvertTo-Json -Depth 6
