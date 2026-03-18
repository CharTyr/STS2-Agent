param(
    [string]$ProjectRoot = "",
    [switch]$KeepGameRunning
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$scriptRoot = Join-Path $ProjectRoot "scripts"
$env:UV_CACHE_DIR = Join-Path $ProjectRoot ".uv-cache"
$results = [System.Collections.Generic.List[object]]::new()
$failed = $false
$failureMessage = $null

function Stop-GameIfRunning {
    $existing = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-Process -Id $existing.Id -Force
        Start-Sleep -Seconds 2
    }
}

function Invoke-ApiJson {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null
    )

    $uri = "http://127.0.0.1:8080" + $Path

    try {
        if ($null -eq $Body) {
            $response = Invoke-WebRequest -Method $Method -Uri $uri -UseBasicParsing -TimeoutSec 10
        }
        else {
            $response = Invoke-WebRequest -Method $Method -Uri $uri -UseBasicParsing -TimeoutSec 10 -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 8 -Compress)
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
        [scriptblock]$Condition,
        [int]$PollAttempts = 120,
        [int]$PollDelayMs = 250
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

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    $startedAt = Get-Date
    Write-Host "==> $Name"

    try {
        & $Action
        $duration = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
        [void]$results.Add([pscustomobject]@{
                name = $Name
                status = "passed"
                duration_seconds = $duration
            })
    }
    catch {
        $duration = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
        [void]$results.Add([pscustomobject]@{
                name = $Name
                status = "failed"
                duration_seconds = $duration
                error = $_.Exception.Message
            })

        throw
    }
}

function Start-DebugSession {
    param([string]$StepName)

    Invoke-RepoScript -Name $StepName -FileName "start-game-session.ps1" -Arguments @("-EnableDebugActions")
}

function Ensure-ActiveRunMainMenu {
    Invoke-Step -Name "ensure active-run MAIN_MENU" -Action {
        $state = Wait-ForState -Description "stable startup state" -Condition {
            param($CurrentState)
            $CurrentState.screen -ne "UNKNOWN" -and (
                $CurrentState.screen -ne "MAIN_MENU" -or @($CurrentState.available_actions).Count -gt 0
            )
        } -PollAttempts 160 -PollDelayMs 250

        if ($state.screen -eq "MAIN_MENU" -and @($state.available_actions) -contains "continue_run") {
            return
        }

        if ($state.screen -eq "MAIN_MENU" -and @($state.available_actions) -contains "open_character_select") {
            $openCharacterSelect = Invoke-Action -Payload @{ action = "open_character_select" }
            if (-not $openCharacterSelect.ok) {
                throw "open_character_select failed while bootstrapping active run: $($openCharacterSelect | ConvertTo-Json -Depth 8 -Compress)"
            }

            $characterSelectState = Wait-ForState -Description "CHARACTER_SELECT while bootstrapping active run" -Condition {
                param($CurrentState)
                $CurrentState.screen -eq "CHARACTER_SELECT" -and $null -ne $CurrentState.character_select
            }

            $characters = @($characterSelectState.character_select.characters | Where-Object { -not $_.is_locked })
            if ($characters.Count -eq 0) {
                throw "Expected at least one unlocked character while bootstrapping active run."
            }

            $selectedCharacter = $characters[0]
            $selectCharacter = Invoke-Action -Payload @{
                action = "select_character"
                option_index = [int]$selectedCharacter.index
            }

            if (-not $selectCharacter.ok) {
                throw "select_character failed while bootstrapping active run: $($selectCharacter | ConvertTo-Json -Depth 8 -Compress)"
            }

            [void](Wait-ForState -Description "embarkable CHARACTER_SELECT" -Condition {
                    param($CurrentState)
                    $CurrentState.screen -eq "CHARACTER_SELECT" -and $null -ne $CurrentState.character_select -and [bool]$CurrentState.character_select.can_embark
                })

            $embark = Invoke-Action -Payload @{ action = "embark" }
            if (-not $embark.ok) {
                throw "embark failed while bootstrapping active run: $($embark | ConvertTo-Json -Depth 8 -Compress)"
            }

            $runState = Wait-ForState -Description "leave CHARACTER_SELECT while bootstrapping active run" -Condition {
                param($CurrentState)
                $CurrentState.screen -ne "CHARACTER_SELECT"
            }

            while ($runState.screen -eq "MODAL") {
                $modalAction = if (@($runState.available_actions) -contains "confirm_modal") { "confirm_modal" } else { "dismiss_modal" }
                $modalResponse = Invoke-Action -Payload @{ action = $modalAction }
                if (-not $modalResponse.ok) {
                    throw "$modalAction failed while bootstrapping active run: $($modalResponse | ConvertTo-Json -Depth 8 -Compress)"
                }

                $runState = Wait-ForState -Description "leave embark modal while bootstrapping active run" -Condition {
                    param($CurrentState)
                    $CurrentState.screen -ne "MODAL"
                }
            }

            if ($runState.screen -eq "MAIN_MENU") {
                throw "Embark returned to MAIN_MENU instead of entering a run while bootstrapping active run."
            }

            Stop-GameIfRunning
            Start-DebugSession -StepName "restart debug session after creating active run"
            [void](Wait-ForState -Description "active-run MAIN_MENU after bootstrap restart" -Condition {
                    param($CurrentState)
                    $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions) -contains "continue_run"
                } -PollAttempts 160 -PollDelayMs 250)

            return
        }

        if ($state.screen -ne "MAIN_MENU") {
            Stop-GameIfRunning
            Start-DebugSession -StepName "restart debug session to surface active-run MAIN_MENU"
            [void](Wait-ForState -Description "active-run MAIN_MENU after restart" -Condition {
                    param($CurrentState)
                    $CurrentState.screen -eq "MAIN_MENU" -and @($CurrentState.available_actions) -contains "continue_run"
                } -PollAttempts 160 -PollDelayMs 250)

            return
        }

        throw "Unable to reach active-run MAIN_MENU from state: $($state | ConvertTo-Json -Depth 8 -Compress)"
    }
}

function Invoke-RepoScript {
    param(
        [string]$Name,
        [string]$FileName,
        [string[]]$Arguments = @()
    )

    $path = Join-Path $scriptRoot $FileName
    Invoke-Step -Name $Name -Action {
        & powershell -ExecutionPolicy Bypass -File $path @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Script failed with exit code ${LASTEXITCODE}: $FileName"
        }
    }
}

try {
    Invoke-Step -Name "stop running game before install" -Action {
        Stop-GameIfRunning
    }
    Invoke-RepoScript -Name "build mod" -FileName "build-mod.ps1"
    Invoke-RepoScript -Name "mod load deep check" -FileName "test-mod-load.ps1" -Arguments @("-DeepCheck")
    Invoke-RepoScript -Name "debug console gating (disabled)" -FileName "test-debug-console-gating.ps1"
    Invoke-RepoScript -Name "debug console gating (enabled)" -FileName "test-debug-console-gating.ps1" -Arguments @("-EnableDebugActions")
    Invoke-RepoScript -Name "mcp tool profile" -FileName "test-mcp-tool-profile.ps1"

    Start-DebugSession -StepName "start debug session for main-menu lifecycle"
    Ensure-ActiveRunMainMenu
    Invoke-RepoScript -Name "main-menu active-run lifecycle" -FileName "test-main-menu-active-run.ps1"
    Invoke-RepoScript -Name "state invariants after main-menu lifecycle" -FileName "test-state-invariants.ps1"

    Start-DebugSession -StepName "start debug session for combat confirm flow"
    Ensure-ActiveRunMainMenu
    Invoke-RepoScript -Name "combat hand confirm flow" -FileName "test-combat-hand-confirm-flow.ps1"
    Invoke-RepoScript -Name "state invariants after combat confirm flow" -FileName "test-state-invariants.ps1"

    Start-DebugSession -StepName "start debug session for deferred potion flow"
    Ensure-ActiveRunMainMenu
    Invoke-RepoScript -Name "deferred potion flow" -FileName "test-deferred-potion-flow.ps1"
    Invoke-RepoScript -Name "state invariants after deferred potion flow" -FileName "test-state-invariants.ps1"

    Start-DebugSession -StepName "start debug session for target index contracts"
    Ensure-ActiveRunMainMenu
    Invoke-RepoScript -Name "target index contracts" -FileName "test-target-index-contract.ps1"
    Invoke-RepoScript -Name "state invariants after target index contracts" -FileName "test-state-invariants.ps1"

    Start-DebugSession -StepName "start debug session for enemy intents payload"
    Ensure-ActiveRunMainMenu
    Invoke-RepoScript -Name "enemy intents payload" -FileName "test-enemy-intents-payload.ps1"
    Invoke-RepoScript -Name "state invariants after enemy intents payload" -FileName "test-state-invariants.ps1"

    Invoke-RepoScript -Name "multiplayer lobby flow" -FileName "test-multiplayer-lobby-flow.ps1"

    Start-DebugSession -StepName "start debug session for new-run lifecycle"
    Ensure-ActiveRunMainMenu
    Invoke-RepoScript -Name "new-run lifecycle" -FileName "test-new-run-lifecycle.ps1"
    Invoke-RepoScript -Name "state invariants after new-run lifecycle" -FileName "test-state-invariants.ps1"
}
catch {
    $failed = $true
    $failureMessage = $_.Exception.Message
}
finally {
    if (-not $KeepGameRunning) {
        Stop-GameIfRunning
    }

    [pscustomobject]@{
        project_root = $ProjectRoot
        keep_game_running = [bool]$KeepGameRunning
        total_steps = $results.Count
        failed = $failed
        failure_message = $failureMessage
        steps = $results
    } | ConvertTo-Json -Depth 6
}

if ($failed) {
    throw $failureMessage
}
