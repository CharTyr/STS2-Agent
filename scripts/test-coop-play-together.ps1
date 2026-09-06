param(
    [int]$HostApiPort = 8080,
    [int]$CompanionApiPort = 8081,
    [int]$Minutes = 25,
    [switch]$KeepGamesRunning
)

$ErrorActionPreference = "Stop"
$hostBase = "http://127.0.0.1:$HostApiPort"
$log = Join-Path $PSScriptRoot "..\build\coop-play-full.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
"" | Set-Content -LiteralPath $log -Encoding UTF8

function Write-Log([string]$Message) {
    $line = "{0} {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    Add-Content -LiteralPath $log -Value $line -Encoding UTF8
    Write-Host $line
}

function Invoke-Json {
    param(
        [string]$BaseUrl,
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [int]$TimeoutSec = 20
    )
    $uri = $BaseUrl.TrimEnd("/") + $Path
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 8 -Compress
            return Invoke-RestMethod -Uri $uri -Method $Method -ContentType "application/json" -Body $json -TimeoutSec $TimeoutSec
        }
        return Invoke-RestMethod -Uri $uri -Method $Method -TimeoutSec $TimeoutSec
    }
    catch {
        $raw = $null
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $raw = $_.ErrorDetails.Message }
        if ($raw) {
            try { return $raw | ConvertFrom-Json } catch { }
        }
        throw
    }
}

function Get-State([string]$BaseUrl) {
    return (Invoke-Json -BaseUrl $BaseUrl -Method GET -Path "/state").data
}

function Get-Health([string]$BaseUrl) {
    return Invoke-Json -BaseUrl $BaseUrl -Method GET -Path "/health"
}

function Invoke-Act([string]$BaseUrl, [hashtable]$Payload, [int]$TimeoutSec = 30) {
    return Invoke-Json -BaseUrl $BaseUrl -Method POST -Path "/action" -Body $Payload -TimeoutSec $TimeoutSec
}

function Get-Occupancy($state) {
    $ids = @()
    if ($state.multiplayer -and $state.multiplayer.connected_player_ids) {
        $ids = @($state.multiplayer.connected_player_ids)
    }
    return $ids.Count
}

function Summarize($state, [string]$who) {
    $acts = @($state.available_actions) -join ","
    $ids = @()
    if ($state.multiplayer -and $state.multiplayer.connected_player_ids) {
        $ids = @($state.multiplayer.connected_player_ids)
    }
    $max = $state.character_select.max_players
    $count = $state.character_select.player_count
    return ("{0} screen={1} acts={2} ids={3} max={4} count={5}" -f $who, $state.screen, $acts, ($ids -join ","), $max, $count)
}

function Drive-Combat([string]$BaseUrl, $state) {
    $actions = @($state.available_actions)
    if ($actions -contains "play_card" -and $state.combat -and $state.combat.hand) {
        foreach ($card in @($state.combat.hand)) {
            if (-not $card.playable) { continue }
            $payload = @{ action = "play_card"; card_index = [int]$card.index }
            if ($card.requires_target) {
                $targets = @($card.valid_target_indices)
                if ($targets.Count -eq 0) { continue }
                $payload.target_index = [int]$targets[0]
            }
            return Invoke-Act $BaseUrl $payload
        }
    }
    if ($actions -contains "end_turn") { return Invoke-Act $BaseUrl @{ action = "end_turn" } }
    return $null
}

function Drive-Once([string]$BaseUrl, $state) {
    $actions = @($state.available_actions)
    if ($actions -contains "confirm_modal") { return Invoke-Act $BaseUrl @{ action = "confirm_modal" } }
    if ($actions -contains "dismiss_modal") { return Invoke-Act $BaseUrl @{ action = "dismiss_modal" } }
    if ($state.in_combat -and (($actions -contains "play_card") -or ($actions -contains "end_turn"))) {
        $combatAct = Drive-Combat $BaseUrl $state
        if ($null -ne $combatAct) { return $combatAct }
    }
    $occupied = Get-Occupancy $state
    switch ($state.screen) {
        "MAIN_MENU" { return $null }
        "CHARACTER_SELECT" {
            if ($actions -contains "embark" -and $occupied -ge 2) { return Invoke-Act $BaseUrl @{ action = "embark" } }
            if ($actions -contains "ready_multiplayer_lobby") { return Invoke-Act $BaseUrl @{ action = "ready_multiplayer_lobby" } }
            if ($actions -contains "select_character") { return Invoke-Act $BaseUrl @{ action = "select_character"; option_index = 0 } }
        }
        "MULTIPLAYER_LOBBY" {
            if ($actions -contains "select_character") { return Invoke-Act $BaseUrl @{ action = "select_character"; option_index = 0 } }
            if ($actions -contains "ready_multiplayer_lobby") { return Invoke-Act $BaseUrl @{ action = "ready_multiplayer_lobby" } }
        }
        "BUNDLE_SELECTION" {
            if ($actions -contains "confirm_bundle") { return Invoke-Act $BaseUrl @{ action = "confirm_bundle" } }
            if ($actions -contains "choose_bundle") { return Invoke-Act $BaseUrl @{ action = "choose_bundle"; option_index = 0 } }
        }
        "EVENT" {
            if ($actions -contains "choose_event_option") { return Invoke-Act $BaseUrl @{ action = "choose_event_option"; option_index = 0 } }
            if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
        }
        "CARD_SELECTION" {
            if ($actions -contains "confirm_selection") { return Invoke-Act $BaseUrl @{ action = "confirm_selection" } }
            if ($actions -contains "select_deck_card") { return Invoke-Act $BaseUrl @{ action = "select_deck_card"; option_index = 0 } }
        }
        "CAPSTONE_SELECTION" {
            if ($actions -contains "choose_capstone_option") { return Invoke-Act $BaseUrl @{ action = "choose_capstone_option"; option_index = 0 } }
        }
        "REWARD" {
            if ($actions -contains "collect_rewards_and_proceed") { return Invoke-Act $BaseUrl @{ action = "collect_rewards_and_proceed" } }
            if ($actions -contains "resolve_rewards") { return Invoke-Act $BaseUrl @{ action = "resolve_rewards" } }
            if ($actions -contains "claim_reward") { return Invoke-Act $BaseUrl @{ action = "claim_reward"; option_index = 0 } }
            if ($actions -contains "choose_reward_card") { return Invoke-Act $BaseUrl @{ action = "choose_reward_card"; option_index = 0 } }
            if ($actions -contains "skip_reward_cards") { return Invoke-Act $BaseUrl @{ action = "skip_reward_cards" } }
            if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
        }
        "MAP" {
            if ($actions -contains "choose_map_node") {
                $nodes = @($state.map.available_nodes)
                $follow = $nodes | Where-Object { $_.vote_count -gt 0 -and -not $_.has_local_vote } | Select-Object -First 1
                if ($follow) { return Invoke-Act $BaseUrl @{ action = "choose_map_node"; option_index = [int]$follow.index } }
                $monster = $nodes | Where-Object { -not $_.has_local_vote -and $_.node_type -eq "Monster" } | Select-Object -First 1
                if ($monster) { return Invoke-Act $BaseUrl @{ action = "choose_map_node"; option_index = [int]$monster.index } }
                $fresh = $nodes | Where-Object { -not $_.has_local_vote } | Select-Object -First 1
                if ($fresh) { return Invoke-Act $BaseUrl @{ action = "choose_map_node"; option_index = [int]$fresh.index } }
            }
        }
        "COMBAT" {
            $combatAct = Drive-Combat $BaseUrl $state
            if ($null -ne $combatAct) { return $combatAct }
            if ($actions -contains "choose_map_node") {
                $nodes = @($state.map.available_nodes)
                $follow = $nodes | Where-Object { $_.vote_count -gt 0 -and -not $_.has_local_vote } | Select-Object -First 1
                if ($follow) { return Invoke-Act $BaseUrl @{ action = "choose_map_node"; option_index = [int]$follow.index } }
                $fresh = $nodes | Where-Object { -not $_.has_local_vote } | Select-Object -First 1
                if ($fresh) { return Invoke-Act $BaseUrl @{ action = "choose_map_node"; option_index = [int]$fresh.index } }
            }
        }
        "REST" {
            if ($actions -contains "choose_rest_option") { return Invoke-Act $BaseUrl @{ action = "choose_rest_option"; option_index = 0 } }
            if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
        }
        "SHOP" {
            if ($actions -contains "close_shop_inventory") { return Invoke-Act $BaseUrl @{ action = "close_shop_inventory" } }
            if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
            if ($actions -contains "open_shop_inventory") { return Invoke-Act $BaseUrl @{ action = "open_shop_inventory" } }
        }
        "CHEST" {
            if ($actions -contains "open_chest") { return Invoke-Act $BaseUrl @{ action = "open_chest" } }
            if ($actions -contains "choose_treasure_relic") { return Invoke-Act $BaseUrl @{ action = "choose_treasure_relic"; option_index = 0 } }
            if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
        }
        "TREASURE" {
            if ($actions -contains "choose_treasure_relic") { return Invoke-Act $BaseUrl @{ action = "choose_treasure_relic"; option_index = 0 } }
            if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
        }
        "CRYSTAL_SPHERE" {
            if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
        }
        "GAME_OVER" {
            if ($actions -contains "continue_game_over") { return Invoke-Act $BaseUrl @{ action = "continue_game_over" } }
            if ($actions -contains "confirm_unlock") { return Invoke-Act $BaseUrl @{ action = "confirm_unlock" } }
            if ($actions -contains "return_to_main_menu") { return Invoke-Act $BaseUrl @{ action = "return_to_main_menu" } }
        }
        "UNLOCK" {
            if ($actions -contains "confirm_unlock") { return Invoke-Act $BaseUrl @{ action = "confirm_unlock" } }
        }
    }
    if ($actions -contains "proceed") { return Invoke-Act $BaseUrl @{ action = "proceed" } }
    return $null
}

$failures = @()
$companionBase = "http://127.0.0.1:$CompanionApiPort"

try {
    $health = Get-Health $hostBase
    Write-Log ("host health version={0} role={1} port={2}" -f $health.data.mod_version, $health.data.instance_role, $health.data.api_port)
    $hostState = Get-State $hostBase
    Write-Log (Summarize $hostState "host")
    if (@($hostState.available_actions) -contains "abandon_run") {
        Write-Log "abandoning leftover run for a clean climb"
        $abandon = Invoke-Act $hostBase @{ action = "abandon_run" }
        Write-Log ("abandon ok={0} msg={1}" -f $abandon.ok, $abandon.data.message)
        Start-Sleep -Seconds 1
        for ($m = 0; $m -lt 6; $m++) {
            $hostState = Get-State $hostBase
            $acts = @($hostState.available_actions)
            if ($acts -contains "confirm_modal") { Invoke-Act $hostBase @{ action = "confirm_modal" } | Out-Null; Start-Sleep -Milliseconds 400; continue }
            if ($acts -contains "dismiss_modal") { Invoke-Act $hostBase @{ action = "dismiss_modal" } | Out-Null; Start-Sleep -Milliseconds 400; continue }
            break
        }
        $hostState = Get-State $hostBase
        Write-Log (Summarize $hostState "host")
    }

    if ($hostState.screen -eq "MAIN_MENU" -and @($hostState.available_actions) -contains "invite_ai_teammate") {
        Write-Log "inviting teammate"
        $invite = Invoke-Act $hostBase @{ action = "invite_ai_teammate" } -TimeoutSec 180
        Write-Log ("invite ok={0} msg={1}" -f $invite.ok, $invite.data.message)
        if (-not $invite.ok) { throw "invite failed: $($invite | ConvertTo-Json -Compress -Depth 6)" }
        if ($invite.data.message -match "API (\d+)") {
            $CompanionApiPort = [int]$Matches[1]
            $companionBase = "http://127.0.0.1:$CompanionApiPort"
        }
    }

    $companionReady = $false
    for ($i = 0; $i -lt 90; $i++) {
        try {
            $ch = Get-Health $companionBase
            if ($ch.ok -and $ch.data.instance_role -eq "companion") {
                Write-Log ("companion health version={0} port={1} pid={2}" -f $ch.data.mod_version, $ch.data.api_port, $ch.data.process_id)
                $companionReady = $true
                break
            }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $companionReady) { throw "companion API did not come up on $companionBase" }

    $hostReachedMap = $false
    $hostPlayedCombat = $false
    $companionOnClimb = $false
    $companionPlayedCombat = $false
    $combatFinished = 0
    $rewardsSeen = $false
    $returnedToMapAfterCombat = $false
    $gameOver = $false
    $hostWasCombat = $false
    $screens = New-Object "System.Collections.Generic.HashSet[string]"
    $deadline = (Get-Date).AddMinutes($Minutes)
    $lastCompanionScreen = ""

    while ((Get-Date) -lt $deadline) {
        $hostState = Get-State $hostBase
        [void]$screens.Add("h:" + [string]$hostState.screen)
        Write-Log (Summarize $hostState "host")
        $cstate = $null
        try {
            $cstate = Get-State $companionBase
            $lastCompanionScreen = [string]$cstate.screen
            [void]$screens.Add("c:" + $lastCompanionScreen)
            Write-Log (Summarize $cstate "companion")
            if ($lastCompanionScreen -in @("MAP", "COMBAT", "MAP_WAIT", "REST", "SHOP", "EVENT", "REWARD", "CHEST")) { $companionOnClimb = $true }
        } catch {
            Write-Log ("companion state error: {0}" -f $_.Exception.Message)
        }

        if ($hostState.screen -in @("MAP", "COMBAT", "MAP_WAIT")) { $hostReachedMap = $true }
        if ($hostState.screen -eq "REWARD") { $rewardsSeen = $true }
        if ($hostState.screen -eq "GAME_OVER") { $gameOver = $true }
        if ($hostWasCombat -and $hostState.screen -in @("REWARD", "MAP", "EVENT", "REST", "SHOP", "CHEST", "GAME_OVER") -and -not $hostState.in_combat) {
            $combatFinished++
            Write-Log ("combat finished count={0} next={1}" -f $combatFinished, $hostState.screen)
        }
        if ($combatFinished -ge 1 -and $hostState.screen -eq "MAP" -and -not $hostState.in_combat) { $returnedToMapAfterCombat = $true }
        $hostWasCombat = ($hostState.screen -eq "COMBAT" -or $hostState.in_combat)

        if ($gameOver -and $hostPlayedCombat -and $companionPlayedCombat) { break }

        $acted = $false
        try {
            $resp = Drive-Once $hostBase $hostState
            if ($null -ne $resp) {
                $acted = $true
                Write-Log ("host action ok={0} action={1} msg={2}" -f $resp.ok, $resp.data.action, $resp.data.message)
                if ($resp.ok -and $resp.data.action -in @("play_card", "end_turn")) { $hostPlayedCombat = $true }
            }
        } catch {
            Write-Log ("host action error: {0}" -f $_.Exception.Message)
        }

        if ($null -ne $cstate) {
            try {
                $cresp = Drive-Once $companionBase $cstate
                if ($null -ne $cresp) {
                    $acted = $true
                    Write-Log ("companion action ok={0} action={1} msg={2}" -f $cresp.ok, $cresp.data.action, $cresp.data.message)
                    if ($cresp.ok -and $cresp.data.action -in @("play_card", "end_turn")) { $companionPlayedCombat = $true }
                    if (-not $cresp.ok) { Write-Log ("companion action error body={0}" -f ($cresp | ConvertTo-Json -Compress -Depth 4)) }
                }
            } catch {
                Write-Log ("companion action error: {0}" -f $_.Exception.Message)
            }
        }

        if (-not $acted) { Start-Sleep -Seconds 1 } else { Start-Sleep -Milliseconds 350 }
    }

    $hostFinal = Get-State $hostBase
    $compFinal = Get-State $companionBase
    $screenList = ($screens | Sort-Object) -join ","
    Write-Log ("FINAL " + (Summarize $hostFinal "host"))
    Write-Log ("FINAL " + (Summarize $compFinal "companion"))
    Write-Log ("flags hostMap={0} hostCombatAct={1} companionClimb={2} companionCombatAct={3} combatFinished={4} rewards={5} mapAfterCombat={6} gameOver={7} screens={8}" -f $hostReachedMap, $hostPlayedCombat, $companionOnClimb, $companionPlayedCombat, $combatFinished, $rewardsSeen, $returnedToMapAfterCombat, $gameOver, $screenList)

    if (-not $companionReady) { $failures += "companion never ready" }
    if (-not $companionOnClimb) { $failures += "companion did not reach MAP/COMBAT (screen=$lastCompanionScreen)" }
    if (-not $hostReachedMap) { $failures += "host did not reach MAP/COMBAT" }
    if (-not $hostPlayedCombat) { $failures += "host did not play_card/end_turn" }
    if (-not $companionPlayedCombat) { $failures += "companion did not play_card/end_turn" }
    if ($combatFinished -lt 1) { $failures += "did not finish a combat" }
    if (-not $rewardsSeen -and -not $returnedToMapAfterCombat) { $failures += "no post-combat reward or return to map" }

    if ($failures.Count -eq 0) {
        if ($gameOver) { Write-Log "LIVE_COOP_FULL_RUN_OK" }
        elseif ($returnedToMapAfterCombat -or $rewardsSeen) { Write-Log "LIVE_COOP_CLIMB_PROGRESS_OK" }
        Write-Log "LIVE_COOP_PLAY_TOGETHER_OK"
        if (-not $KeepGamesRunning) {
            Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force
        }
        exit 0
    }

    Write-Log ("LIVE_COOP_PLAY_TOGETHER_FAIL " + ($failures -join "; "))
    if (-not $KeepGamesRunning) {
        Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    exit 1
}
catch {
    Write-Log ("LIVE_COOP_PLAY_TOGETHER_FAIL " + $_.Exception.Message)
    if (-not $KeepGamesRunning) {
        Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    throw
}
