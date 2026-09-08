param(
    [ValidateSet('Prepare', 'ErrorFixture', 'DiscoverModels', 'Execute', 'GameOverSave', 'Disconnect')]
    [string]$Mode = 'Prepare',
    [switch]$AllowLiveGame,
    [string]$RepoRoot = '',
    [string]$HostClientId = '2026090801',
    [string]$CompanionClientId = '2026090802',
    [int]$HostApiPort = 18080,
    [string]$ProxyListen = '127.0.0.1:18090',
    [int]$MaxRequests = 100,
    [int]$MaxTokens = 200000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:STS2_ENABLE_DEBUG_ACTIONS = $null
Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue

function Resolve-RepoRoot {
    param([string]$InputRoot)
    if ($InputRoot) { return (Resolve-Path -LiteralPath $InputRoot).Path }
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

$RepoRoot = Resolve-RepoRoot -InputRoot $RepoRoot
$Evidence = Join-Path $RepoRoot 'build\validation-2026-09-08'
$GameDir = Join-Path $Evidence 'game'
$GameExe = Join-Path $GameDir 'SlayTheSpire2.exe'
$Template = Join-Path $RepoRoot 'scripts\fixtures\coop-full-run-2026-09-08\settings.template.json'
$HostSettings = Join-Path $Evidence 'settings.json'
$CompanionSettings = Join-Path $Evidence 'settings.companion.json'
$Ledger = Join-Path $Evidence 'budget-ledger.json'
$Secrets = Join-Path $RepoRoot 'scripts\sts2-validation-secrets.ps1'
$ProxyPy = Join-Path $RepoRoot 'scripts\sts2-model-budget-proxy.py'
$StartGame = Join-Path $RepoRoot 'scripts\start-game-session.ps1'
$ProtectedSnapshot = Join-Path $Evidence 'protected-save-snapshot.json'
$SteamSaveRoot = Join-Path $env:APPDATA 'SlayTheSpire2\steam\76561198420578597'
$Default1 = Join-Path $env:APPDATA 'SlayTheSpire2\default\1'
$Default1001 = Join-Path $env:APPDATA 'SlayTheSpire2\default\1001'
$AgentSettingsRoot = Join-Path $env:APPDATA 'STS2AIAgent'
$Upstream = 'https://api.gmi-serving.com'
$ProxyBase = 'http://' + $ProxyListen
$DummyKey = 'sts2-budget-proxy-local'
$ExpectedModel = 'MiniMaxAI/MiniMax-M3'
$script:ContinueGameOverIssued = @{}
$script:ContinueGameOverSeconds = @{}
$script:ContinueGameOverClicks = 0

New-Item -ItemType Directory -Force -Path $Evidence | Out-Null
. $Secrets

function Write-JsonFile {
    param([string]$Path, $Object)
    $Object | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Copy-FileNoLink {
    param([string]$From, [string]$To)
    if (-not (Test-Path -LiteralPath $From)) { throw "missing $From" }
    Copy-Item -LiteralPath $From -Destination $To -Force
    $src = Get-Item -LiteralPath $From
    $dst = Get-Item -LiteralPath $To
    if ($dst.LinkType) { throw "refusing linked settings/save: $To link=$($dst.LinkType)" }
    return $dst
}

function Get-ProtectedRoots { @($SteamSaveRoot, $Default1, $Default1001, $AgentSettingsRoot) }

function Save-ProtectedSnapshot {
    $items = @()
    foreach ($root in Get-ProtectedRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $h = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
            $items += [pscustomobject]@{ path = $_.FullName; sha256 = $h.Hash; length = $_.Length; mtime = $_.LastWriteTimeUtc.ToString('o') }
        }
    }
    $obj = [pscustomobject]@{
        captured_at_utc = [DateTime]::UtcNow.ToString('o')
        note = 'read-only snapshot of real Steam/default saves and default agent settings'
        items = $items
    }
    Write-JsonFile -Path $ProtectedSnapshot -Object $obj
    return $items.Count
}

function Test-ProtectedSnapshotUnchanged {
    if (-not (Test-Path -LiteralPath $ProtectedSnapshot)) { throw 'protected snapshot missing; run Prepare first' }
    $snap = Get-Content -LiteralPath $ProtectedSnapshot -Raw | ConvertFrom-Json
    $failures = @()
    foreach ($item in @($snap.items)) {
        if (-not (Test-Path -LiteralPath $item.path)) {
            $failures += ('missing ' + $item.path)
            continue
        }
        $h = Get-FileHash -LiteralPath $item.path -Algorithm SHA256
        if ($h.Hash -ne $item.sha256) { $failures += ('changed ' + $item.path) }
    }
    return $failures
}

function Initialize-IsolatedSettings {
    Copy-FileNoLink -From $Template -To $HostSettings | Out-Null
    Copy-FileNoLink -From $Template -To $CompanionSettings | Out-Null
}

function Get-IsolatedGameInfo {
    if (-not (Test-Path -LiteralPath $GameExe)) {
        return [pscustomobject]@{ present = $false; reason = 'isolated game exe missing' }
    }
    $mods = @(Get-ChildItem -LiteralPath (Join-Path $GameDir 'mods') -Force -ErrorAction SilentlyContinue)
    $exe = Get-Item -LiteralPath $GameExe
    $hash = Get-FileHash -LiteralPath $GameExe -Algorithm SHA256
    return [pscustomobject]@{
        present = $true
        exe = $GameExe
        exe_sha256 = $hash.Hash
        exe_length = $exe.Length
        link_type = [string]$exe.LinkType
        mods_count = $mods.Count
        steam_appid = (Test-Path -LiteralPath (Join-Path $GameDir 'steam_appid.txt'))
        candidate_mod_present = (Test-Path -LiteralPath (Join-Path $GameDir 'mods\STS2AIAgent.dll'))
    }
}

function Start-BudgetProxy {
    param([string]$Fixture = 'none', [string]$UpstreamKey = '', [string]$LedgerPath = '')
    if (-not $LedgerPath) { $LedgerPath = $Ledger }
    $listenHost, $listenPort = $ProxyListen.Split(':')
    $quoted = {
        param([string]$Value)
        if ($Value -match '[\s"]') { return '"' + ($Value.Replace('"','\"')) + '"' }
        return $Value
    }
    $argParts = @(
        (& $quoted $ProxyPy),
        '--listen', (& $quoted $ProxyListen),
        '--upstream', (& $quoted $Upstream),
        '--ledger', (& $quoted $LedgerPath),
        '--max-requests', [string]$MaxRequests,
        '--max-tokens', [string]$MaxTokens,
        '--fixture', $Fixture,
        '--allowed-model', (& $quoted $ExpectedModel),
        '--upstream-timeout', '720'
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'python'
    $psi.Arguments = [string]::Join(' ', $argParts)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $false
    $psi.RedirectStandardOutput = $false
    $psi.CreateNoWindow = $true
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        if ($psi.Environment.ContainsKey($entry.Key)) { $psi.Environment[$entry.Key] = [string]$entry.Value }
        else { $psi.Environment.Add($entry.Key, [string]$entry.Value) }
    }
    if ($psi.Environment.ContainsKey('STS2_ENABLE_DEBUG_ACTIONS')) { $psi.Environment['STS2_ENABLE_DEBUG_ACTIONS'] = '' }
    if ($UpstreamKey) {
        if ($psi.Environment.ContainsKey('STS2_VALIDATION_UPSTREAM_KEY')) { $psi.Environment['STS2_VALIDATION_UPSTREAM_KEY'] = $UpstreamKey }
        else { $psi.Environment.Add('STS2_VALIDATION_UPSTREAM_KEY', $UpstreamKey) }
    } elseif ($psi.Environment.ContainsKey('STS2_VALIDATION_UPSTREAM_KEY')) {
        $psi.Environment.Remove('STS2_VALIDATION_UPSTREAM_KEY')
    }
    $proc = [Diagnostics.Process]::Start($psi)
    $deadline = (Get-Date).AddSeconds(8)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $null = Invoke-RestMethod -Uri ($ProxyBase + '/health') -TimeoutSec 1
            $ready = $true
            break
        } catch {
            if ($proc.HasExited) { throw 'budget proxy exited before health: ' + $proc.ExitCode }
            Start-Sleep -Milliseconds 150
        }
    }
    if (-not $ready) { throw 'budget proxy did not become healthy' }
    return $proc
}

function Stop-BudgetProxy {
    param($Process)
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) { $Process.Kill() }
    } catch {}
}

function Invoke-ErrorFixture {
    $results = @()
    $fixtureLedger = Join-Path $Evidence 'test-ledgers\error-fixture-ledger.json'
    New-Item -ItemType Directory -Force -Path (Split-Path $fixtureLedger) | Out-Null
    foreach ($kind in @('401', '429')) {
        $proc = Start-BudgetProxy -Fixture $kind -LedgerPath $fixtureLedger
        try {
            $code = 0
            try {
                Invoke-RestMethod -Uri ($ProxyBase + '/v1/chat/completions') -Method Post -ContentType 'application/json' -Body '{"model":"fixture","messages":[{"role":"user","content":"ping"}]}' -TimeoutSec 5 | Out-Null
            } catch {
                if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
            }
            $ok = ($code -eq [int]$kind)
            $results += [pscustomobject]@{ fixture = $kind; status = $code; ok = $ok }
            if (-not $ok) { throw "fixture $kind expected HTTP $kind got $code" }
        }
        finally { Stop-BudgetProxy -Process $proc; Start-Sleep -Milliseconds 200 }
    }
    $out = Join-Path $Evidence 'error-fixture-result.json'
    Write-JsonFile -Path $out -Object ([pscustomobject]@{
        at_utc = [DateTime]::UtcNow.ToString('o')
        note = 'No-cost local fixture only. This does not prove MiniMax strategy or connectivity.'
        results = $results
    })
    return $out
}

function Invoke-DiscoverModels {
    $secure = Import-Sts2ValidationSecureKey
    $plain = Convert-Sts2SecureStringToPlain -Secure $secure
    $proc = $null
    $catalogPath = Join-Path $Evidence 'model-catalog-redacted.json'
    try {
        $proc = Start-BudgetProxy -Fixture 'none' -UpstreamKey $plain
        $raw = Invoke-RestMethod -Uri ($ProxyBase + '/v1/models') -TimeoutSec 30
        $ids = @()
        if ($raw.data) {
            foreach ($m in @($raw.data)) {
                $ids += [pscustomobject]@{ id = [string]$m.id; owned_by = [string]$m.owned_by }
            }
        }
        $match = @($ids | Where-Object { $_.id -eq $ExpectedModel -or $_.id -like '*MiniMax-M3*' -or $_.id -like '*MiniMax*M3*' })
        $redacted = [pscustomobject]@{
            at_utc = [DateTime]::UtcNow.ToString('o')
            upstream_host = 'api.gmi-serving.com'
            requested_via = $ProxyBase + '/v1/models'
            model_count = $ids.Count
            expected_id = $ExpectedModel
            expected_id_present = (@($match).Count -gt 0)
            minimax_matches = $match
            all_ids = @($ids.id)
            note = 'IDs only. No API keys. This is catalog discovery, not a strategy test.'
        }
        Write-JsonFile -Path $catalogPath -Object $redacted
        return $redacted
    }
    finally {
        Stop-BudgetProxy -Process $proc
        $plain = $null
        [GC]::Collect()
    }
}

function Write-PrepareEvidence {
    $game = Get-IsolatedGameInfo
    $settingsReady = (Test-Path -LiteralPath $HostSettings) -and (Test-Path -LiteralPath $CompanionSettings)
    $probe = [pscustomobject]@{
        at_utc = [DateTime]::UtcNow.ToString('o')
        baseline_sha = 'bb26a21e915ba63c0408c90aacb0c6013f1add7f'
        branch = (git -C $RepoRoot rev-parse --abbrev-ref HEAD)
        head_sha = (git -C $RepoRoot rev-parse HEAD)
        game_running = [bool](Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue)
        isolated_game = $game
        host_client_id = $HostClientId
        companion_client_id = $CompanionClientId
        host_api_port = $HostApiPort
        proxy = $ProxyBase
        settings_ready = $settingsReady
        debug_env_cleared = [string]::IsNullOrEmpty($env:STS2_ENABLE_DEBUG_ACTIONS)
        protected_snapshot = $ProtectedSnapshot
        candidate_build = 'not_installed_waiting_for_test_slot'
        real_model_strategy_not_proven = $true
    }
    Write-JsonFile -Path (Join-Path $Evidence 'environment-probe.json') -Object $probe
    $method = [pscustomobject]@{
        flow = @('configure isolated settings','invite_ai_teammate from MAIN_MENU','companion joins lobby','host HTTP acts as human','companion MiniMax via budget proxy','rewards/map','native GAME_OVER continue_game_over','both isolated saves save_status=verified')
        host_player = 'HTTP /state /action on host port; do not start host autoplay'
        companion_player = 'in-game autoplay through local budget proxy to MiniMax'
        not_full_run = @('debug-win','hp hacks','heuristic Drive-Once on companion')
        launch = @{
            exe = $GameExe
            extra_arguments = "--windowed --force-steam off --clientId $HostClientId"
            api_port = $HostApiPort
            settings_path = $HostSettings
            enable_debug_actions = $false
            via_steam = $false
        }
        saves = @{
            engine_user_root_still = (Join-Path $env:APPDATA 'SlayTheSpire2')
            isolation = 'new offline clientIds, not Steam profile switch'
            host_dir = (Join-Path $env:APPDATA "SlayTheSpire2\default\$HostClientId")
            companion_dir = (Join-Path $env:APPDATA "SlayTheSpire2\default\$CompanionClientId")
            do_not_touch = @( $SteamSaveRoot, $Default1, $Default1001, $AgentSettingsRoot )
        }
        budget = @{
            max_requests = $MaxRequests
            max_tokens = $MaxTokens
            token_cap_requires_usage = $true
            missing_usage_marked_unknown = $true
        }
    }
    Write-JsonFile -Path (Join-Path $Evidence 'method.json') -Object $method
}

function Invoke-Prepare {
    if (-not (Test-Path -LiteralPath $GameExe)) { throw "isolated game missing: $GameExe" }
    $game = Get-IsolatedGameInfo
    if ($game.mods_count -ne 0) { throw 'isolated mods/ is not empty; extra mods must stay out until candidate install' }
    if ($game.link_type) { throw 'isolated exe is a link; copy required' }
    Initialize-IsolatedSettings
    if (-not (Test-Path -LiteralPath $ProtectedSnapshot)) { Save-ProtectedSnapshot | Out-Null }
    Write-PrepareEvidence
    Write-Output 'PREPARE_OK'
}

function Get-CandidatePaths {
    $mods = Join-Path $GameDir 'mods'
    return [pscustomobject]@{
        Mods = $mods
        Dll = Join-Path $mods 'STS2AIAgent.dll'
        Pck = Join-Path $mods 'STS2AIAgent.pck'
        ModId = Join-Path $mods 'mod_id.json'
        NestedDir = Join-Path $mods 'STS2AIAgent'
        NestedDll = Join-Path $mods 'STS2AIAgent\STS2AIAgent.dll'
        NestedPck = Join-Path $mods 'STS2AIAgent\STS2AIAgent.pck'
    }
}

function Get-FileHashRecord([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $item = Get-Item -LiteralPath $Path
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    return [pscustomobject]@{ path = $item.FullName; sha256 = $hash.Hash; length = $item.Length }
}

function Write-IsolatedProgressSaves {
    $json = @'
{
  "schema_version": 8,
  "mod_settings": {
    "mods_enabled": true,
    "mod_list": [
      { "id": "STS2AIAgent", "is_enabled": true, "source": "mods_directory" }
    ]
  },
  "seen_ea_disclaimer": true,
  "skip_intro_logo": true,
  "fullscreen": false,
  "limit_fps_in_background": false,
  "vsync": "disabled",
  "window_size": { "X": 1280, "Y": 720 }
}
'@
    foreach ($id in @($HostClientId, $CompanionClientId)) {
        $dir = Join-Path $env:APPDATA ("SlayTheSpire2\default\" + $id)
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Set-Content -LiteralPath (Join-Path $dir 'settings.save') -Value $json -Encoding utf8
    }
}

function Set-VerifiedRoleTests {
    $settings = Get-Content -LiteralPath $HostSettings -Raw | ConvertFrom-Json
    $fp = Get-Sts2RoleFingerprint -BaseUrl $settings.endpoints[0].baseUrl -Model $settings.models[0].model -ApiKey $settings.endpoints[0].apiKey
    $now = [DateTime]::UtcNow.ToString('o')
    $mk = {
        param($role)
        [pscustomobject]@{
            role = $role
            status = 'verified'
            capabilityStatus = 'unverified'
            endpointId = $settings.endpoints[0].id
            endpointName = $settings.endpoints[0].name
            modelId = $settings.models[0].id
            modelName = $settings.models[0].model
            fingerprint = $fp
            testedAt = $now
            nextStep = '连通成功。工具/视觉能力仍为未验证。'
        }
    }
    $settings | Add-Member -NotePropertyName roleTests -NotePropertyValue @(& $mk conversation; & $mk play) -Force
    Write-JsonFile -Path $HostSettings -Object $settings
    Copy-FileNoLink -From $HostSettings -To $CompanionSettings | Out-Null
}

function Invoke-Json {
    param([string]$BaseUrl, [string]$Method, [string]$Path, $Body = $null, [int]$TimeoutSec = 30)
    $uri = $BaseUrl.TrimEnd('/') + $Path
    if ($null -ne $Body) {
        $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 8 -Compress }
        return Invoke-RestMethod -Uri $uri -Method $Method -ContentType 'application/json' -Body $json -TimeoutSec $TimeoutSec
    }
    return Invoke-RestMethod -Uri $uri -Method $Method -TimeoutSec $TimeoutSec
}

function Get-State([string]$BaseUrl) { return (Invoke-Json -BaseUrl $BaseUrl -Method GET -Path '/state').data }
function Get-Health([string]$BaseUrl) { return Invoke-Json -BaseUrl $BaseUrl -Method GET -Path '/health' }
function Invoke-Act([string]$BaseUrl, $Payload, [int]$TimeoutSec = 40) { return Invoke-Json -BaseUrl $BaseUrl -Method POST -Path '/action' -Body $Payload -TimeoutSec $TimeoutSec }
function Invoke-SessionControl([string]$BaseUrl, [bool]$Running) {
    return Invoke-Json -BaseUrl $BaseUrl -Method POST -Path '/session/control' -Body @{ running = $Running } -TimeoutSec 45
}

function Get-Occupancy($state) {
    $ids = @()
    if ($state.multiplayer -and $state.multiplayer.connected_player_ids) { $ids = @($state.multiplayer.connected_player_ids) }
    return $ids.Count
}

function Summarize($state, [string]$who) {
    $acts = @($state.available_actions) -join ','
    return ('{0} screen={1} acts={2}' -f $who, $state.screen, $acts)
}

function Drive-Combat([string]$BaseUrl, $state) {
    $ready = $true
    if ($state.combat -and $state.combat.action_readiness) {
        $ready = [bool]$state.combat.action_readiness.can_use_combat_actions
    }
    if (-not $ready) { return $null }
    $actions = @($state.available_actions)
    if ($actions -contains 'play_card' -and $state.combat -and $state.combat.hand) {
        foreach ($card in @($state.combat.hand)) {
            if (-not $card.playable) { continue }
            $payload = @{ action = 'play_card'; card_index = [int]$card.index }
            if ($card.requires_target) {
                $targets = @($card.valid_target_indices)
                if ($targets.Count -eq 0) { continue }
                $payload.target_index = [int]$targets[0]
            }
            return Invoke-Act $BaseUrl $payload
        }
    }
    if ($actions -contains 'end_turn') { return Invoke-Act $BaseUrl @{ action = 'end_turn' } }
    return $null
}

function Drive-Once([string]$BaseUrl, $state) {
    $actions = @($state.available_actions)
    if ($actions -contains 'confirm_modal') { return Invoke-Act $BaseUrl @{ action = 'confirm_modal' } }
    if ($actions -contains 'dismiss_modal') { return Invoke-Act $BaseUrl @{ action = 'dismiss_modal' } }
    if ($actions -contains 'confirm_timeline_overlay') { return Invoke-Act $BaseUrl @{ action = 'confirm_timeline_overlay' } }
    if ($state.in_combat -and (($actions -contains 'play_card') -or ($actions -contains 'end_turn'))) {
        $combatAct = Drive-Combat $BaseUrl $state
        if ($null -ne $combatAct) { return $combatAct }
    }
    $occupied = Get-Occupancy $state
    switch ($state.screen) {
        'MAIN_MENU' { return $null }
        'CHARACTER_SELECT' {
            if ($actions -contains 'embark' -and $occupied -ge 2) { return Invoke-Act $BaseUrl @{ action = 'embark' } }
            if ($actions -contains 'ready_multiplayer_lobby') { return Invoke-Act $BaseUrl @{ action = 'ready_multiplayer_lobby' } }
            if ($actions -contains 'select_character') { return Invoke-Act $BaseUrl @{ action = 'select_character'; option_index = 0 } }
        }
        'MULTIPLAYER_LOBBY' {
            if ($actions -contains 'select_character') { return Invoke-Act $BaseUrl @{ action = 'select_character'; option_index = 0 } }
            if ($actions -contains 'ready_multiplayer_lobby') { return Invoke-Act $BaseUrl @{ action = 'ready_multiplayer_lobby' } }
        }
        'BUNDLE_SELECTION' {
            if ($actions -contains 'confirm_bundle') { return Invoke-Act $BaseUrl @{ action = 'confirm_bundle' } }
            if ($actions -contains 'choose_bundle') { return Invoke-Act $BaseUrl @{ action = 'choose_bundle'; option_index = 0 } }
        }
        'EVENT' {
            if ($actions -contains 'choose_event_option') { return Invoke-Act $BaseUrl @{ action = 'choose_event_option'; option_index = 0 } }
            if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } }
        }
        'CARD_SELECTION' {
            if ($actions -contains 'confirm_selection') { return Invoke-Act $BaseUrl @{ action = 'confirm_selection' } }
            if ($actions -contains 'select_deck_card') {
                $cards = @()
                if ($state.selection -and $state.selection.cards) { $cards = @($state.selection.cards) }
                $fresh = $cards | Where-Object { -not $_.selected } | Select-Object -First 1
                $idx = 0
                if ($fresh) { $idx = [int]$fresh.index }
                return Invoke-Act $BaseUrl @{ action = 'select_deck_card'; option_index = $idx }
            }
        }
        'CAPSTONE_SELECTION' {
            if ($actions -contains 'choose_capstone_option') { return Invoke-Act $BaseUrl @{ action = 'choose_capstone_option'; option_index = 0 } }
        }
        'REWARD' {
            if ($actions -contains 'collect_rewards_and_proceed') { return Invoke-Act $BaseUrl @{ action = 'collect_rewards_and_proceed' } }
            if ($actions -contains 'resolve_rewards') { return Invoke-Act $BaseUrl @{ action = 'resolve_rewards' } }
            if ($actions -contains 'claim_reward') { return Invoke-Act $BaseUrl @{ action = 'claim_reward'; option_index = 0 } }
            if ($actions -contains 'choose_reward_card') { return Invoke-Act $BaseUrl @{ action = 'choose_reward_card'; option_index = 0 } }
            if ($actions -contains 'skip_reward_cards') { return Invoke-Act $BaseUrl @{ action = 'skip_reward_cards' } }
            if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } }
        }
        'MAP' {
            if ($actions -contains 'choose_map_node') {
                $nodes = @()
                if ($state.map -and $state.map.available_nodes) { $nodes = @($state.map.available_nodes) }
                $follow = $nodes | Where-Object { $_.vote_count -gt 0 -and -not $_.has_local_vote } | Select-Object -First 1
                if ($follow) { return Invoke-Act $BaseUrl @{ action = 'choose_map_node'; option_index = [int]$follow.index } }
                $monster = $nodes | Where-Object { -not $_.has_local_vote -and $_.node_type -eq 'Monster' } | Select-Object -First 1
                if ($monster) { return Invoke-Act $BaseUrl @{ action = 'choose_map_node'; option_index = [int]$monster.index } }
                $fresh = $nodes | Where-Object { -not $_.has_local_vote } | Select-Object -First 1
                if ($fresh) { return Invoke-Act $BaseUrl @{ action = 'choose_map_node'; option_index = [int]$fresh.index } }
            }
        }
        'COMBAT' {
            $combatAct = Drive-Combat $BaseUrl $state
            if ($null -ne $combatAct) { return $combatAct }
        }
        'REST' {
            if ($actions -contains 'choose_rest_option') { return Invoke-Act $BaseUrl @{ action = 'choose_rest_option'; option_index = 0 } }
            if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } }
        }
        'SHOP' {
            if ($actions -contains 'close_shop_inventory') { return Invoke-Act $BaseUrl @{ action = 'close_shop_inventory' } }
            if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } }
        }
        'CHEST' {
            if ($actions -contains 'open_chest') { return Invoke-Act $BaseUrl @{ action = 'open_chest' } }
            if ($actions -contains 'choose_treasure_relic') { return Invoke-Act $BaseUrl @{ action = 'choose_treasure_relic'; option_index = 0 } }
            if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } }
        }
        'TREASURE' {
            if ($actions -contains 'choose_treasure_relic') { return Invoke-Act $BaseUrl @{ action = 'choose_treasure_relic'; option_index = 0 } }
            if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } }
        }
        'CRYSTAL_SPHERE' { if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } } }
        'GAME_OVER' {
            if ($actions -contains 'dismiss_game_over_wait') { return Invoke-Act $BaseUrl @{ action = 'dismiss_game_over_wait' } }
            if ($actions -contains 'continue_game_over') {
                if ($script:ContinueGameOverIssued -and $script:ContinueGameOverIssued[$BaseUrl]) { return $null }
                if (-not $script:ContinueGameOverIssued) { $script:ContinueGameOverIssued = @{} }
                $script:ContinueGameOverIssued[$BaseUrl] = $true
                $script:ContinueGameOverClicks++
                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                try {
                    $resp = Invoke-Act $BaseUrl @{ action = 'continue_game_over' } -TimeoutSec 90
                    $script:ContinueGameOverSeconds[$BaseUrl] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
                    Write-RunLog ('continue_game_over once url={0} seconds={1} ok={2}' -f $BaseUrl, $script:ContinueGameOverSeconds[$BaseUrl], $resp.ok)
                    return $resp
                } catch {
                    $script:ContinueGameOverIssued[$BaseUrl] = $false
                    $script:ContinueGameOverClicks = [Math]::Max(0, $script:ContinueGameOverClicks - 1)
                    throw
                }
            }
            if ($actions -contains 'confirm_unlock') { return Invoke-Act $BaseUrl @{ action = 'confirm_unlock' } }
            if ($actions -contains 'return_to_main_menu') { return Invoke-Act $BaseUrl @{ action = 'return_to_main_menu' } }
        }
        'UNLOCK' { if ($actions -contains 'confirm_unlock') { return Invoke-Act $BaseUrl @{ action = 'confirm_unlock' } } }
    }
    if ($actions -contains 'proceed') { return Invoke-Act $BaseUrl @{ action = 'proceed' } }
    return $null
}

function Write-RunLog([string]$Message) {
    $line = '{0} {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message
    Add-Content -LiteralPath $script:FullRunLog -Value $line -Encoding UTF8
    Write-Host $line
}

function Get-LedgerSnapshot {
    if (-not (Test-Path -LiteralPath $script:Ledger)) { return $null }
    return Get-Content -LiteralPath $script:Ledger -Raw | ConvertFrom-Json
}

function Invoke-ConnectivityProbe {
    $body = '{"model":"MiniMaxAI/MiniMax-M3","messages":[{"role":"user","content":"ping"}],"max_tokens":8,"stream":false}'
    $resp = Invoke-RestMethod -Uri ($ProxyBase + '/v1/chat/completions') -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 60
    if (-not $resp) { throw 'connectivity probe returned empty response' }
    return $true
}

function Stop-TrackedGames([int[]]$Pids) {
    foreach ($procId in ($Pids | Select-Object -Unique)) {
        if (-not $procId) { continue }
        try {
            $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
            if ($proc -and $proc.ProcessName -eq 'SlayTheSpire2') { Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue }
        } catch {}
    }
}

function Invoke-Execute {
    if (-not $AllowLiveGame) {
        throw 'Execute refused: pass -AllowLiveGame after the candidate is installed only in the isolated mods folder. Do not start the live Steam game.'
    }
    if (-not (Test-Path -LiteralPath $GameExe)) { throw "isolated game missing: $GameExe" }
    $paths = Get-CandidatePaths
    if (-not ((Test-Path -LiteralPath $paths.Dll) -and (Test-Path -LiteralPath $paths.Pck))) {
        throw 'Execute is prepared but candidate DLL/PCK hashes are not installed in isolated mods/ yet.'
    }
    $running = @(Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw ('Refuse to start isolated acceptance while SlayTheSpire2 is already running: ' + (($running | ForEach-Object { $_.Id }) -join ','))
    }
    $script:FullRunLog = Join-Path $Evidence 'full-run.log'
    $script:ContinueGameOverIssued = @{}
    $script:ContinueGameOverSeconds = @{}
    $script:ContinueGameOverClicks = 0
    Set-Content -LiteralPath $script:FullRunLog -Value ('=== START {0} ===' -f (Get-Date -Format o)) -Encoding UTF8
    Initialize-IsolatedSettings
    if (-not (Test-Path -LiteralPath $ProtectedSnapshot)) { Save-ProtectedSnapshot | Out-Null }
    Write-IsolatedProgressSaves
    $env:STS2_ENABLE_DEBUG_ACTIONS = $null
    Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue
    $env:STS2_AGENT_SETTINGS_PATH = $HostSettings
    $env:STS2_COMPANION_SETTINGS_PATH = $CompanionSettings
    $candidate = [pscustomobject]@{
        at_utc = [DateTime]::UtcNow.ToString('o')
        baseline_sha = (git -C $RepoRoot rev-parse HEAD)
        branch = (git -C $RepoRoot rev-parse --abbrev-ref HEAD)
        dll = Get-FileHashRecord $paths.Dll
        pck = Get-FileHashRecord $paths.Pck
        mod_id = Get-FileHashRecord $paths.ModId
        nested_dll = Get-FileHashRecord $paths.NestedDll
        game_exe = Get-FileHashRecord $GameExe
        installed_only_in_isolated_mods = $true
    }
    Write-JsonFile -Path (Join-Path $Evidence 'candidate-build.json') -Object $candidate
    $proxy = $null
    $hostPid = $null
    $companionPid = $null
    $companionPort = $HostApiPort + 1
    $hostBase = 'http://127.0.0.1:' + $HostApiPort
    $companionBase = 'http://127.0.0.1:' + $companionPort
    $result = [ordered]@{
        started_at_utc = [DateTime]::UtcNow.ToString('o')
        outcome = 'incomplete'
        host_player = 'HTTP /state /action; host autoplay not started'
        companion_player = 'in-game autoplay through budget proxy'
        pause_probe = 'not_run'
        disconnect_probe = 'deferred_until_native_stop'
        budget = $null
        screens = @()
        flags = @{}
        failures = @()
        protected_save_failures = @()
    }
    try {
        $secure = Import-Sts2ValidationSecureKey
        $plain = Convert-Sts2SecureStringToPlain -Secure $secure
        try {
            $proxy = Start-BudgetProxy -Fixture 'none' -UpstreamKey $plain
        } finally {
            $plain = $null
            [GC]::Collect()
        }
        Write-RunLog 'budget proxy healthy; running connectivity probe'
        Invoke-ConnectivityProbe | Out-Null
        Set-VerifiedRoleTests
        Write-RunLog 'role tests stamped after live probe'
        Write-RunLog 'launching isolated host'
        $launchLines = @(& $StartGame -ExePath $GameExe -ApiPort $HostApiPort -KeepExistingProcesses -Attempts 360 -DelaySeconds 1 -ExtraArguments ("--windowed --force-steam off --clientId " + $HostClientId))
        $launchJson = $launchLines | Where-Object { $_ -match '^\s*\{' } | Select-Object -Last 1
        if (-not $launchJson) { throw ('host launch did not return JSON: ' + ($launchLines -join ' | ')) }
        $launch = $launchJson | ConvertFrom-Json
        $hostPid = [int]$launch.pid
        Write-RunLog ('host pid={0} health={1}' -f $hostPid, $launch.health)
        $inviteDeadline = (Get-Date).AddSeconds(180)
        $hostState = $null
        $lastHostSummary = ''
        while ((Get-Date) -lt $inviteDeadline) {
            try {
                $hostState = Get-State $hostBase
                $summary = Summarize $hostState 'host'
                if ($summary -ne $lastHostSummary) { Write-RunLog $summary; $lastHostSummary = $summary }
                $acts = @($hostState.available_actions)
                if ($acts -contains 'abandon_run') {
                    Write-RunLog 'abandoning leftover isolated run'
                    Invoke-Act $hostBase @{ action = 'abandon_run' } | Out-Null
                    Start-Sleep -Seconds 1
                    continue
                }
                if ($acts -contains 'confirm_modal') { Invoke-Act $hostBase @{ action = 'confirm_modal' } | Out-Null; Start-Sleep -Milliseconds 400; continue }
                if ($acts -contains 'dismiss_modal') { Invoke-Act $hostBase @{ action = 'dismiss_modal' } | Out-Null; Start-Sleep -Milliseconds 400; continue }
                if ($hostState.screen -eq 'MAIN_MENU' -and $acts -contains 'invite_ai_teammate') { break }
            } catch {
                Write-RunLog ('wait main menu: ' + $_.Exception.Message)
            }
            Start-Sleep -Seconds 1
        }
        if ($null -eq $hostState -or $hostState.screen -ne 'MAIN_MENU' -or @($hostState.available_actions) -notcontains 'invite_ai_teammate') {
            throw ('host is not ready to invite: ' + (Summarize $hostState 'host'))
        }
        Write-RunLog 'inviting AI teammate'
        $invite = Invoke-Act $hostBase @{ action = 'invite_ai_teammate' } -TimeoutSec 180
        Write-RunLog ('invite ok={0} msg={1}' -f $invite.ok, $invite.data.message)
        if (-not $invite.ok) { throw ('invite failed: ' + ($invite | ConvertTo-Json -Compress -Depth 6)) }
        if ($invite.data.message -match 'API (\d+)') {
            $companionPort = [int]$Matches[1]
            $companionBase = 'http://127.0.0.1:' + $companionPort
        }
        $companionReady = $false
        for ($i = 0; $i -lt 120; $i++) {
            try {
                $ch = Get-Health $companionBase
                if ($ch.ok -and $ch.data.instance_role -eq 'companion') {
                    $companionPid = [int]$ch.data.process_id
                    Write-RunLog ('companion health port={0} pid={1} play={2}' -f $ch.data.api_port, $companionPid, $ch.data.play_phase)
                    $companionReady = $true
                    break
                }
            } catch {}
            Start-Sleep -Seconds 1
        }
        if (-not $companionReady) { throw "companion API did not come up on $companionBase" }
        $flags = @{
            hostReachedMap = $false
            hostPlayedCombat = $false
            companionOnClimb = $false
            companionCombatSeen = $false
            combatFinished = 0
            rewardsSeen = $false
            returnedToMapAfterCombat = $false
            gameOver = $false
            hostSaveVerified = $false
            companionSaveVerified = $false
            returnedToMenu = $false
            companionStoppedOnMenu = $false
        }
        $screens = New-Object 'System.Collections.Generic.HashSet[string]'
        $deadline = (Get-Date).AddMinutes(180)
        $pauseDone = $false
        $hostWasCombat = $false
        $stopReason = 'timeout'
        while ((Get-Date) -lt $deadline) {
            $ledgerSnap = Get-LedgerSnapshot
            if ($ledgerSnap -and $ledgerSnap.stopped) {
                $stopReason = 'budget_stopped:' + [string]$ledgerSnap.stop_reason
                Write-RunLog $stopReason
                break
            }
            $hostState = Get-State $hostBase
            [void]$screens.Add('h:' + [string]$hostState.screen)
            Write-RunLog (Summarize $hostState 'host')
            $cstate = $null
            $chealth = $null
            try {
                $cstate = Get-State $companionBase
                $chealth = Get-Health $companionBase
                [void]$screens.Add('c:' + [string]$cstate.screen)
                Write-RunLog ((Summarize $cstate 'companion') + ' play=' + [string]$chealth.data.play_phase + ' req=' + [string]$chealth.data.session_requests)
                if ([string]$cstate.screen -in @('MAP','COMBAT','MAP_WAIT','REST','SHOP','EVENT','REWARD','CHEST')) { $flags.companionOnClimb = $true }
                if ([string]$cstate.screen -eq 'COMBAT' -or $cstate.in_combat) { $flags.companionCombatSeen = $true }
            } catch {
                Write-RunLog ('companion state error: ' + $_.Exception.Message)
            }
            if ($hostState.screen -in @('MAP','COMBAT','MAP_WAIT')) { $flags.hostReachedMap = $true }
            if ($hostState.screen -eq 'REWARD') { $flags.rewardsSeen = $true }
            if ($hostState.screen -eq 'GAME_OVER') { $flags.gameOver = $true }
            if ($hostWasCombat -and $hostState.screen -in @('REWARD','MAP','EVENT','REST','SHOP','CHEST','GAME_OVER') -and -not $hostState.in_combat) { $flags.combatFinished++ }
            if ($flags.combatFinished -ge 1 -and $hostState.screen -eq 'MAP' -and -not $hostState.in_combat) { $flags.returnedToMapAfterCombat = $true }
            $hostWasCombat = ($hostState.screen -eq 'COMBAT' -or $hostState.in_combat)
            if ($hostState.game_over -and $hostState.game_over.save_verified) { $flags.hostSaveVerified = $true }
            if ($cstate -and $cstate.game_over -and $cstate.game_over.save_verified) { $flags.companionSaveVerified = $true }
            if ($hostState.screen -eq 'MAIN_MENU' -and $flags.gameOver) {
                $flags.returnedToMenu = $true
                if ($chealth -and -not $chealth.data.play_running) { $flags.companionStoppedOnMenu = $true }
                $stopReason = 'native_end'
                break
            }
            if (-not $pauseDone -and $flags.hostPlayedCombat -and $flags.companionCombatSeen -and $chealth -and [int]$chealth.data.session_requests -ge 1) {
                Write-RunLog 'pause probe: stopping companion autoplay'
                $before = Get-LedgerSnapshot
                try {
                    $pauseResp = Invoke-SessionControl $companionBase $false
                    Write-RunLog ('pause phase={0} running={1}' -f $pauseResp.data.phase, $pauseResp.data.play_running)
                    Start-Sleep -Seconds 20
                    $after = Get-LedgerSnapshot
                    $delta = 0
                    if ($before -and $after) { $delta = [int]$after.requests - [int]$before.requests }
                    $pausedHealth = Get-Health $companionBase
                    $pauseOk = ([string]$pausedHealth.data.play_phase -eq 'paused' -or -not $pausedHealth.data.play_running) -and $delta -le 1
                    $result.pause_probe = [pscustomobject]@{ ok = $pauseOk; phase = [string]$pausedHealth.data.play_phase; request_delta = $delta }
                    Write-RunLog ('pause probe ok={0} delta={1}' -f $pauseOk, $delta)
                    Invoke-SessionControl $companionBase $true | Out-Null
                } catch {
                    Write-RunLog ('pause probe error: ' + $_.Exception.Message)
                    $pausedHealth = $null
                    try { $pausedHealth = Get-Health $companionBase } catch {}
                    $alreadyStopped = $pausedHealth -and -not $pausedHealth.data.play_running
                    $result.pause_probe = [pscustomobject]@{ ok = [bool]$alreadyStopped; error = $_.Exception.Message; phase = $(if ($pausedHealth) { [string]$pausedHealth.data.play_phase } else { 'unknown' }) }
                    try { Invoke-SessionControl $companionBase $true | Out-Null } catch { Write-RunLog ('pause resume error: ' + $_.Exception.Message) }
                }
                $pauseDone = $true
            }
            if ($pauseDone -and $chealth -and -not $chealth.data.play_running -and [string]$chealth.data.stop_kind -ne 'run_end' -and [string]$cstate.screen -ne 'MAIN_MENU') {
                Write-RunLog ('companion paused after probe; resuming phase={0}' -f [string]$chealth.data.play_phase)
                try { Invoke-SessionControl $companionBase $true | Out-Null } catch { Write-RunLog ('resume error: ' + $_.Exception.Message) }
            }
            $acted = $false
            try {
                $resp = Drive-Once $hostBase $hostState
                if ($null -ne $resp) {
                    $acted = $true
                    Write-RunLog ('host action ok={0} action={1} msg={2}' -f $resp.ok, $resp.data.action, $resp.data.message)
                    if ($resp.ok -and $resp.data.action -in @('play_card','end_turn')) { $flags.hostPlayedCombat = $true }
                }
            } catch {
                Write-RunLog ('host action error: ' + $_.Exception.Message)
            }
            if (-not $acted) { Start-Sleep -Seconds 1 } else { Start-Sleep -Milliseconds 350 }
        }
        $result.flags = $flags
        $result.screens = @($screens | Sort-Object)
        $result.budget = Get-LedgerSnapshot
        $result.protected_save_failures = @(Test-ProtectedSnapshotUnchanged)
        $failures = @()
        if (-not $companionReady) { $failures += 'companion never ready' }
        if (-not $flags.companionOnClimb) { $failures += 'companion did not reach MAP/COMBAT' }
        if (-not $flags.hostReachedMap) { $failures += 'host did not reach MAP/COMBAT' }
        if (-not $flags.hostPlayedCombat) { $failures += 'host did not play_card/end_turn' }
        if (-not $flags.companionCombatSeen) { $failures += 'companion combat never observed' }
        if ($flags.combatFinished -lt 1) { $failures += 'did not finish a combat' }
        if (-not $flags.rewardsSeen -and -not $flags.returnedToMapAfterCombat) { $failures += 'no post-combat reward or return to map' }
        if ($result.pause_probe -eq 'not_run') { $failures += 'pause probe not run' }
        elseif ($result.pause_probe.ok -ne $true) { $failures += 'pause probe failed' }
        if ($result.protected_save_failures.Count -gt 0) { $failures += 'protected saves changed' }
        if ($stopReason -eq 'native_end') {
            if (-not $flags.hostSaveVerified) { $failures += 'host save not verified' }
            if (-not $flags.companionSaveVerified) { $failures += 'companion save not verified' }
            if (-not $flags.companionStoppedOnMenu) { $failures += 'companion did not stop after return to menu' }
        }
        $result.failures = $failures
        $result.stop_reason = $stopReason
        $result.ended_at_utc = [DateTime]::UtcNow.ToString('o')
        if ($failures.Count -eq 0 -and $stopReason -eq 'native_end') { $result.outcome = 'full_run_ok' }
        elseif ($failures.Count -eq 0) { $result.outcome = 'progress_ok_incomplete' }
        else { $result.outcome = 'failed' }
        if ($flags.gameOver -and $hostPid) {
            Write-RunLog 'disconnect probe after game-over path: stopping companion pid only'
            if ($companionPid) { Stop-TrackedGames @($companionPid) }
            Start-Sleep -Seconds 2
            try {
                $null = Get-Health $companionBase
                $result.disconnect_probe = 'companion_still_reachable'
            } catch {
                $result.disconnect_probe = 'companion_unreachable_after_stop'
            }
        }
        Write-JsonFile -Path (Join-Path $Evidence 'full-run-result.json') -Object $result
        Write-RunLog ('RESULT outcome={0} stop={1} failures={2}' -f $result.outcome, $stopReason, ($failures -join '; '))
        if ($result.outcome -eq 'failed') { throw ('FULL_RUN_FAIL ' + ($failures -join '; ')) }
        Write-Output ('FULL_RUN_' + $result.outcome.ToUpperInvariant() + ' ' + $stopReason)
    }
    finally {
        Stop-TrackedGames @($hostPid, $companionPid)
        Stop-BudgetProxy -Process $proxy
        Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue
    }
}

function Invoke-McpOriginLive([string]$BaseUrl) {
    $init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"validation","version":"1"}}}'
    $url = $BaseUrl.TrimEnd('/') + '/mcp'
    $probe = {
        param($Origin)
        $headers = @{ 'Content-Type' = 'application/json' }
        if ($Origin) { $headers['Origin'] = $Origin }
        try {
            $resp = Invoke-WebRequest -Uri $url -Method POST -Headers $headers -Body $init -TimeoutSec 10
            return [pscustomobject]@{ origin = $Origin; status = [int]$resp.StatusCode; cors = [string]$resp.Headers['Access-Control-Allow-Origin']; body = [string]$resp.Content; wildcard = ([string]$resp.Headers['Access-Control-Allow-Origin'] -eq '*') }
        } catch {
            $code = 0
            $body = ''
            $cors = ''
            $exResp = $_.Exception.Response
            if ($exResp) {
                $code = [int]$exResp.StatusCode
                try { $cors = [string]$exResp.Headers['Access-Control-Allow-Origin'] } catch {}
                try {
                    $stream = $exResp.GetResponseStream()
                    if ($stream) { $body = [IO.StreamReader]::new($stream).ReadToEnd() }
                } catch {}
            }
            return [pscustomobject]@{ origin = $Origin; status = $code; cors = $cors; body = $body; wildcard = ($cors -eq '*'); error = $_.Exception.Message }
        }
    }
    $evil = & $probe 'https://evil.example'
    $same = & $probe ($BaseUrl.TrimEnd('/'))
    $native = & $probe $null
    $ok = ($evil.status -eq 403) -and (-not $evil.wildcard) -and ($same.status -eq 200) -and ($same.cors -eq ($BaseUrl.TrimEnd('/'))) -and (-not $same.wildcard) -and ($native.status -eq 200) -and (-not $native.wildcard)
    return [pscustomobject]@{ ok = $ok; evil = $evil; same_origin = $same; native = $native }
}

function Invoke-Disconnect {
    if (-not $AllowLiveGame) { throw 'Disconnect refused: pass -AllowLiveGame. Do not start the live Steam game.' }
    $paths = Get-CandidatePaths
    if (-not ((Test-Path -LiteralPath $paths.Dll) -and (Test-Path -LiteralPath $paths.Pck))) { throw 'candidate missing in isolated mods' }
    $running = @(Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) { throw ('Refuse disconnect probe while SlayTheSpire2 is running: ' + (($running | ForEach-Object { $_.Id }) -join ',')) }
    $script:FullRunLog = Join-Path $Evidence 'disconnect-run.log'
    Set-Content -LiteralPath $script:FullRunLog -Value ('=== DISCONNECT START {0} ===' -f (Get-Date -Format o)) -Encoding UTF8
    Initialize-IsolatedSettings
    Set-VerifiedRoleTests
    if (-not (Test-Path -LiteralPath $ProtectedSnapshot)) { Save-ProtectedSnapshot | Out-Null }
    Write-IsolatedProgressSaves
    $env:STS2_ENABLE_DEBUG_ACTIONS = $null
    Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue
    $env:STS2_AGENT_SETTINGS_PATH = $HostSettings
    $env:STS2_COMPANION_SETTINGS_PATH = $CompanionSettings
    $proxy = $null
    $hostPid = $null
    $companionPid = $null
    $hostBase = 'http://127.0.0.1:' + $HostApiPort
    $companionPort = $HostApiPort + 1
    $companionBase = 'http://127.0.0.1:' + $companionPort
    $result = [ordered]@{ started_at_utc = [DateTime]::UtcNow.ToString('o'); outcome = 'failed'; used_upstream = $false; ledger_requests_before = $null; ledger_requests_after = $null }
    try {
        $before = Get-LedgerSnapshot
        $result.ledger_requests_before = if ($before) { [int]$before.requests } else { 0 }
        $proxy = Start-BudgetProxy -Fixture 'none'
        Write-RunLog 'proxy started without upstream key; stopped ledger should 429 without forwarding'
        $launchLines = @(& $StartGame -ExePath $GameExe -ApiPort $HostApiPort -KeepExistingProcesses -Attempts 360 -DelaySeconds 1 -ExtraArguments ('--windowed --force-steam off --clientId ' + $HostClientId))
        $launchJson = $launchLines | Where-Object { $_ -match '^\s*\{' } | Select-Object -Last 1
        if (-not $launchJson) { throw ('host launch did not return JSON') }
        $launch = $launchJson | ConvertFrom-Json
        $hostPid = [int]$launch.pid
        Write-RunLog ('host pid={0}' -f $hostPid)
        $inviteDeadline = (Get-Date).AddSeconds(180)
        $hostState = $null
        while ((Get-Date) -lt $inviteDeadline) {
            try {
                $hostState = Get-State $hostBase
                $acts = @($hostState.available_actions)
                if ($acts -contains 'confirm_modal') { Invoke-Act $hostBase @{ action = 'confirm_modal' } | Out-Null; Start-Sleep -Milliseconds 400; continue }
                if ($acts -contains 'dismiss_modal') { Invoke-Act $hostBase @{ action = 'dismiss_modal' } | Out-Null; Start-Sleep -Milliseconds 400; continue }
                if ($hostState.screen -eq 'MAIN_MENU' -and $acts -contains 'invite_ai_teammate') { break }
            } catch { Write-RunLog ('wait: ' + $_.Exception.Message) }
            Start-Sleep -Seconds 1
        }
        if ($null -eq $hostState -or @($hostState.available_actions) -notcontains 'invite_ai_teammate') { throw 'host not ready to invite' }
        $invite = Invoke-Act $hostBase @{ action = 'invite_ai_teammate' } -TimeoutSec 180
        Write-RunLog ('invite ok={0} msg={1}' -f $invite.ok, $invite.data.message)
        if (-not $invite.ok) { throw 'invite failed' }
        if ($invite.data.message -match 'API (\d+)') { $companionPort = [int]$Matches[1]; $companionBase = 'http://127.0.0.1:' + $companionPort }
        $companionReady = $false
        for ($i = 0; $i -lt 120; $i++) {
            try {
                $ch = Get-Health $companionBase
                if ($ch.ok -and $ch.data.instance_role -eq 'companion') {
                    $companionPid = [int]$ch.data.process_id
                    $companionReady = $true
                    Write-RunLog ('companion pid={0} port={1}' -f $companionPid, $ch.data.api_port)
                    break
                }
            } catch {}
            Start-Sleep -Seconds 1
        }
        if (-not $companionReady) { throw 'companion never ready' }
        $origin = Invoke-McpOriginLive $hostBase
        $result.origin_live = $origin
        Write-RunLog ('origin live ok={0} evil={1} same={2} native={3}' -f $origin.ok, $origin.evil.status, $origin.same_origin.status, $origin.native.status)
        $hostHealthBefore = Get-Health $hostBase
        $result.host_before_kill = [pscustomobject]@{ companion_process_alive = $hostHealthBefore.data.companion_process_alive; companion_process_exited = $hostHealthBefore.data.companion_process_exited; dual_status = [string]$hostHealthBefore.data.dual_status }
        Write-RunLog 'killing companion process'
        Stop-TrackedGames @($companionPid)
        $exited = $false
        $hostHealthAfter = $null
        for ($i = 0; $i -lt 20; $i++) {
            Start-Sleep -Seconds 1
            $hostHealthAfter = Get-Health $hostBase
            if ($hostHealthAfter.data.companion_process_exited -eq $true) { $exited = $true; break }
        }
        $companionUnreachable = $false
        try { Get-Health $companionBase | Out-Null } catch { $companionUnreachable = $true }
        $after = Get-LedgerSnapshot
        $result.ledger_requests_after = if ($after) { [int]$after.requests } else { 0 }
        $result.host_after_kill = [pscustomobject]@{ companion_process_alive = $hostHealthAfter.data.companion_process_alive; companion_process_exited = $hostHealthAfter.data.companion_process_exited; dual_status = [string]$hostHealthAfter.data.dual_status }
        $result.companion_unreachable = $companionUnreachable
        $result.companion_exited_flag = $exited
        $result.protected_save_failures = @(Test-ProtectedSnapshotUnchanged)
        $failures = @()
        if (-not $companionUnreachable) { $failures += 'companion still reachable' }
        if (-not $exited) { $failures += 'host health did not report companion_process_exited' }
        if ($hostHealthAfter.data.companion_process_alive -eq $true) { $failures += 'host still reports companion alive' }
        if ($result.ledger_requests_after -ne $result.ledger_requests_before) { $failures += 'ledger requests changed; disconnect probe must not spend MiniMax' }
        if (-not $origin.ok) { $failures += 'origin live policy failed' }
        if ($result.protected_save_failures.Count -gt 0) { $failures += 'protected saves changed' }
        $result.failures = $failures
        $result.ended_at_utc = [DateTime]::UtcNow.ToString('o')
        $result.outcome = $(if ($failures.Count -eq 0) { 'disconnect_ok' } else { 'failed' })
        Write-JsonFile -Path (Join-Path $Evidence 'disconnect-result.json') -Object $result
        Write-RunLog ('RESULT outcome={0} failures={1}' -f $result.outcome, ($failures -join '; '))
        if ($result.outcome -eq 'failed') { throw ('DISCONNECT_FAIL ' + ($failures -join '; ')) }
        Write-Output ('DISCONNECT_OK origin=' + $origin.ok)
    }
    finally {
        Stop-TrackedGames @($hostPid, $companionPid)
        Stop-BudgetProxy -Process $proxy
        Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue
    }
}

function Get-ProgressSaveInfo([string]$ClientId) {
    $path = Join-Path $env:APPDATA ("SlayTheSpire2\default\" + $ClientId + "\modded\profile1\saves\progress.save")
    if (-not (Test-Path -LiteralPath $path)) {
        return [pscustomobject]@{ client_id = $ClientId; path = $path; exists = $false }
    }
    $item = Get-Item -LiteralPath $path
    $raw = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $losses = $null
    if ($raw.character_stats) {
        $stats = @($raw.character_stats)
        if ($stats.Count -gt 0) { $losses = $stats[0].total_losses }
    }
    return [pscustomobject]@{
        client_id = $ClientId
        path = $path
        exists = $true
        mtime_utc = $item.LastWriteTimeUtc.ToString('o')
        mtime_ticks = $item.LastWriteTimeUtc.Ticks
        length = $item.Length
        floors_climbed = $raw.floors_climbed
        current_score = $raw.current_score
        total_losses = $losses
    }
}

function Install-IsolatedCandidate {
    $staging = Join-Path $RepoRoot 'build\mods\STS2AIAgent'
    $paths = Get-CandidatePaths
    New-Item -ItemType Directory -Force -Path $paths.Mods | Out-Null
    foreach ($name in @('STS2AIAgent.dll', 'STS2AIAgent.pck', 'mod_id.json')) {
        $from = Join-Path $staging $name
        if (-not (Test-Path -LiteralPath $from)) { throw "staging missing $from" }
        Copy-Item -LiteralPath $from -Destination (Join-Path $paths.Mods $name) -Force
    }
}

function Invoke-DriveBoth([string]$HostBase, [string]$CompanionBase) {
    $hostState = Get-State $HostBase
    Write-RunLog (Summarize $hostState 'host')
    $cstate = $null
    try {
        $cstate = Get-State $CompanionBase
        Write-RunLog (Summarize $cstate 'companion')
    } catch {
        Write-RunLog ('companion state error: ' + $_.Exception.Message)
    }
    $acted = $false
    try {
        $resp = Drive-Once $HostBase $hostState
        if ($null -ne $resp) {
            $acted = $true
            Write-RunLog ('host action ok={0} action={1} msg={2}' -f $resp.ok, $resp.data.action, $resp.data.message)
        }
    } catch {
        Write-RunLog ('host action error: ' + $_.Exception.Message)
    }
    if ($null -ne $cstate) {
        try {
            $cresp = Drive-Once $CompanionBase $cstate
            if ($null -ne $cresp) {
                $acted = $true
                Write-RunLog ('companion action ok={0} action={1} msg={2}' -f $cresp.ok, $cresp.data.action, $cresp.data.message)
            }
        } catch {
            Write-RunLog ('companion action error: ' + $_.Exception.Message)
        }
    }
    return [pscustomobject]@{ host = $hostState; companion = $cstate; acted = $acted }
}

function Test-OnClimb($state) {
    if ($null -eq $state) { return $false }
    $screen = [string]$state.screen
    return $screen -in @('MAP', 'COMBAT', 'MAP_WAIT', 'EVENT', 'REWARD', 'REST', 'SHOP', 'CHEST') -or [bool]$state.in_combat
}

function Invoke-GameOverSave {
    if (-not $AllowLiveGame) {
        throw 'GameOverSave refused: pass -AllowLiveGame after the candidate is installed only in the isolated mods folder. Do not start the live Steam game.'
    }
    if (-not (Test-Path -LiteralPath $GameExe)) { throw "isolated game missing: $GameExe" }
    $running = @(Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw ('Refuse to start isolated acceptance while SlayTheSpire2 is already running: ' + (($running | ForEach-Object { $_.Id }) -join ','))
    }
    Install-IsolatedCandidate
    $paths = Get-CandidatePaths
    if (-not ((Test-Path -LiteralPath $paths.Dll) -and (Test-Path -LiteralPath $paths.Pck))) {
        throw 'GameOverSave is prepared but candidate DLL/PCK hashes are not installed in isolated mods/ yet.'
    }
    $script:FullRunLog = Join-Path $Evidence 'gameover-save-run.log'
    $script:ContinueGameOverIssued = @{}
    $script:ContinueGameOverSeconds = @{}
    $script:ContinueGameOverClicks = 0
    Set-Content -LiteralPath $script:FullRunLog -Value ('=== GAME_OVER SAVE START {0} ===' -f (Get-Date -Format o)) -Encoding UTF8
    Initialize-IsolatedSettings
    if (-not (Test-Path -LiteralPath $ProtectedSnapshot)) { Save-ProtectedSnapshot | Out-Null }
    Write-IsolatedProgressSaves
    $env:STS2_ENABLE_DEBUG_ACTIONS = '1'
    $env:STS2_AGENT_SETTINGS_PATH = $HostSettings
    $env:STS2_COMPANION_SETTINGS_PATH = $CompanionSettings
    $candidate = [pscustomobject]@{
        at_utc = [DateTime]::UtcNow.ToString('o')
        baseline_sha = (git -C $RepoRoot rev-parse HEAD)
        branch = (git -C $RepoRoot rev-parse --abbrev-ref HEAD)
        dll = Get-FileHashRecord $paths.Dll
        pck = Get-FileHashRecord $paths.Pck
        mod_id = Get-FileHashRecord $paths.ModId
        game_exe = Get-FileHashRecord $GameExe
        installed_only_in_isolated_mods = $true
        mode = 'GameOverSave'
    }
    Write-JsonFile -Path (Join-Path $Evidence 'gameover-save-candidate.json') -Object $candidate
    $proxy = $null
    $hostPid = $null
    $companionPid = $null
    $companionPort = $HostApiPort + 1
    $hostBase = 'http://127.0.0.1:' + $HostApiPort
    $companionBase = 'http://127.0.0.1:' + $companionPort
    $result = [ordered]@{
        started_at_utc = [DateTime]::UtcNow.ToString('o')
        outcome = 'incomplete'
        mode = 'GameOverSave'
        flow = @('isolated dual launch', 'invite companion', 'HTTP lobby/embark', 'console fight', 'console die', 'continue_game_over once/90s', 'return_to_main_menu', 'progress.save mtime + save_verified')
        host_continue_seconds = $null
        companion_continue_seconds = $null
        continue_clicks = 0
        forced_return_suspected = $false
        host_progress_before = $null
        companion_progress_before = $null
        host_progress_after = $null
        companion_progress_after = $null
        flags = @{}
        failures = @()
        protected_save_failures = @()
    }
    try {
        $secure = Import-Sts2ValidationSecureKey
        $plain = Convert-Sts2SecureStringToPlain -Secure $secure
        try {
            $proxy = Start-BudgetProxy -Fixture 'none' -UpstreamKey $plain
        } finally {
            $plain = $null
            [GC]::Collect()
        }
        Write-RunLog 'budget proxy healthy; running connectivity probe'
        Invoke-ConnectivityProbe | Out-Null
        Set-VerifiedRoleTests
        Write-RunLog 'launching isolated host with debug actions'
        $launchLines = @(& $StartGame -ExePath $GameExe -ApiPort $HostApiPort -EnableDebugActions -KeepExistingProcesses -Attempts 360 -DelaySeconds 1 -ExtraArguments ("--windowed --force-steam off --clientId " + $HostClientId))
        $launchJson = $launchLines | Where-Object { $_ -match '^\s*\{' } | Select-Object -Last 1
        if (-not $launchJson) { throw ('host launch did not return JSON: ' + ($launchLines -join ' | ')) }
        $launch = $launchJson | ConvertFrom-Json
        $hostPid = [int]$launch.pid
        Write-RunLog ('host pid={0} health={1} debug={2}' -f $hostPid, $launch.health, $launch.debug_actions_enabled)
        $inviteDeadline = (Get-Date).AddSeconds(180)
        $hostState = $null
        $lastHostSummary = ''
        while ((Get-Date) -lt $inviteDeadline) {
            try {
                $hostState = Get-State $hostBase
                $summary = Summarize $hostState 'host'
                if ($summary -ne $lastHostSummary) { Write-RunLog $summary; $lastHostSummary = $summary }
                $acts = @($hostState.available_actions)
                if ($acts -contains 'abandon_run') {
                    Write-RunLog 'abandoning leftover isolated run'
                    Invoke-Act $hostBase @{ action = 'abandon_run' } | Out-Null
                    Start-Sleep -Seconds 1
                    continue
                }
                if ($acts -contains 'confirm_modal') { Invoke-Act $hostBase @{ action = 'confirm_modal' } | Out-Null; Start-Sleep -Milliseconds 400; continue }
                if ($acts -contains 'dismiss_modal') { Invoke-Act $hostBase @{ action = 'dismiss_modal' } | Out-Null; Start-Sleep -Milliseconds 400; continue }
                if ($hostState.screen -eq 'MAIN_MENU' -and $acts -contains 'invite_ai_teammate') { break }
            } catch {
                Write-RunLog ('wait main menu: ' + $_.Exception.Message)
            }
            Start-Sleep -Seconds 1
        }
        if ($null -eq $hostState -or $hostState.screen -ne 'MAIN_MENU' -or @($hostState.available_actions) -notcontains 'invite_ai_teammate') {
            throw ('host is not ready to invite: ' + (Summarize $hostState 'host'))
        }
        Write-RunLog 'inviting AI teammate'
        $invite = Invoke-Act $hostBase @{ action = 'invite_ai_teammate' } -TimeoutSec 180
        Write-RunLog ('invite ok={0} msg={1}' -f $invite.ok, $invite.data.message)
        if (-not $invite.ok) { throw ('invite failed: ' + ($invite | ConvertTo-Json -Compress -Depth 6)) }
        if ($invite.data.message -match 'API (\d+)') {
            $companionPort = [int]$Matches[1]
            $companionBase = 'http://127.0.0.1:' + $companionPort
        }
        $companionReady = $false
        for ($i = 0; $i -lt 120; $i++) {
            try {
                $ch = Get-Health $companionBase
                if ($ch.ok -and $ch.data.instance_role -eq 'companion') {
                    $companionPid = [int]$ch.data.process_id
                    Write-RunLog ('companion health port={0} pid={1} play={2}' -f $ch.data.api_port, $companionPid, $ch.data.play_phase)
                    $companionReady = $true
                    break
                }
            } catch {}
            Start-Sleep -Seconds 1
        }
        if (-not $companionReady) { throw "companion API did not come up on $companionBase" }
        Write-RunLog 'pausing companion autoplay; host and companion will be driven over HTTP'
        try { Invoke-SessionControl $companionBase $false | Out-Null } catch { Write-RunLog ('pause companion: ' + $_.Exception.Message) }
        $flags = @{
            companionReady = $true
            reachedClimb = $false
            fought = $false
            died = $false
            gameOver = $false
            hostSaveVerified = $false
            companionSaveVerified = $false
            returnedToMenu = $false
        }
        $climbDeadline = (Get-Date).AddMinutes(10)
        while ((Get-Date) -lt $climbDeadline) {
            $pair = Invoke-DriveBoth $hostBase $companionBase
            if ((Test-OnClimb $pair.host) -and (Test-OnClimb $pair.companion)) {
                $flags.reachedClimb = $true
                break
            }
            if (-not $pair.acted) { Start-Sleep -Seconds 1 } else { Start-Sleep -Milliseconds 350 }
        }
        if (-not $flags.reachedClimb) { throw 'did not reach MAP/COMBAT on both instances' }
        $hostState = Get-State $hostBase
        if ([string]$hostState.screen -ne 'COMBAT' -and -not [bool]$hostState.in_combat) {
            Write-RunLog 'issuing fight MAWLER_NORMAL'
            $fight = Invoke-Act $hostBase @{ action = 'run_console_command'; command = 'fight MAWLER_NORMAL' } -TimeoutSec 30
            Write-RunLog ('fight ok={0} msg={1}' -f $fight.ok, $fight.data.message)
            $combatDeadline = (Get-Date).AddSeconds(45)
            while ((Get-Date) -lt $combatDeadline) {
                $hostState = Get-State $hostBase
                if ([string]$hostState.screen -eq 'COMBAT' -or [bool]$hostState.in_combat) { break }
                Start-Sleep -Milliseconds 500
            }
        }
        $hostState = Get-State $hostBase
        if ([string]$hostState.screen -eq 'COMBAT' -or [bool]$hostState.in_combat) { $flags.fought = $true }
        if (-not $flags.fought) { throw ('fight did not enter COMBAT: ' + (Summarize $hostState 'host')) }
        Write-RunLog 'issuing die on companion then host'
        try { Invoke-Act $companionBase @{ action = 'run_console_command'; command = 'die' } -TimeoutSec 30 | Out-Null } catch { Write-RunLog ('companion die: ' + $_.Exception.Message) }
        Start-Sleep -Milliseconds 400
        $die = Invoke-Act $hostBase @{ action = 'run_console_command'; command = 'die' } -TimeoutSec 30
        Write-RunLog ('host die ok={0} msg={1}' -f $die.ok, $die.data.message)
        $dieDeadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $dieDeadline) {
            $hostState = Get-State $hostBase
            $cstate = $null
            try { $cstate = Get-State $companionBase } catch {}
            if ([string]$hostState.screen -eq 'GAME_OVER') { $flags.gameOver = $true; $flags.died = $true; break }
            Start-Sleep -Milliseconds 500
        }
        if (-not $flags.gameOver) { throw ('did not reach GAME_OVER: ' + (Summarize $hostState 'host')) }
        $result.host_progress_before = Get-ProgressSaveInfo $HostClientId
        $result.companion_progress_before = Get-ProgressSaveInfo $CompanionClientId
        Write-RunLog ('progress before host ticks={0} companion ticks={1}' -f $result.host_progress_before.mtime_ticks, $result.companion_progress_before.mtime_ticks)
        $continueDeadline = (Get-Date).AddMinutes(4)
        $hostContinueStarted = $null
        $companionContinueStarted = $null
        while ((Get-Date) -lt $continueDeadline) {
            $pair = Invoke-DriveBoth $hostBase $companionBase
            if ($pair.host -and $pair.host.game_over -and $pair.host.game_over.save_verified) { $flags.hostSaveVerified = $true }
            if ($pair.companion -and $pair.companion.game_over -and $pair.companion.game_over.save_verified) { $flags.companionSaveVerified = $true }
            if ($script:ContinueGameOverIssued[$hostBase] -and -not $hostContinueStarted) { $hostContinueStarted = Get-Date }
            if ($script:ContinueGameOverIssued[$companionBase] -and -not $companionContinueStarted) { $companionContinueStarted = Get-Date }
            $hostMenu = $pair.host -and [string]$pair.host.screen -eq 'MAIN_MENU'
            $compMenu = $pair.companion -and [string]$pair.companion.screen -eq 'MAIN_MENU'
            if ($hostMenu -and $compMenu) { $flags.returnedToMenu = $true; break }
            if (-not $pair.acted) { Start-Sleep -Seconds 1 } else { Start-Sleep -Milliseconds 350 }
        }
        if ($script:ContinueGameOverSeconds.ContainsKey($hostBase)) { $result.host_continue_seconds = $script:ContinueGameOverSeconds[$hostBase] }
        if ($script:ContinueGameOverSeconds.ContainsKey($companionBase)) { $result.companion_continue_seconds = $script:ContinueGameOverSeconds[$companionBase] }
        $result.continue_clicks = $script:ContinueGameOverClicks
        $result.host_progress_after = Get-ProgressSaveInfo $HostClientId
        $result.companion_progress_after = Get-ProgressSaveInfo $CompanionClientId
        $hostSaved = $result.host_progress_after.exists -and $result.host_progress_before.exists -and ($result.host_progress_after.mtime_ticks -gt $result.host_progress_before.mtime_ticks)
        $compSaved = $result.companion_progress_after.exists -and $result.companion_progress_before.exists -and ($result.companion_progress_after.mtime_ticks -gt $result.companion_progress_before.mtime_ticks)
        $result.forced_return_suspected = ($result.host_continue_seconds -ge 14 -and $result.host_continue_seconds -le 17 -and -not $hostSaved)
        $result.protected_save_failures = @(Test-ProtectedSnapshotUnchanged)
        $failures = @()
        if (-not $flags.reachedClimb) { $failures += 'did not reach climb' }
        if (-not $flags.fought) { $failures += 'fight did not enter COMBAT' }
        if (-not $flags.gameOver) { $failures += 'GAME_OVER not reached' }
        if ($result.continue_clicks -gt 2) { $failures += ('continue clicked too many times: ' + $result.continue_clicks) }
        if (-not $flags.hostSaveVerified) { $failures += 'host save not verified' }
        if (-not $flags.companionSaveVerified) { $failures += 'companion save not verified' }
        if (-not $hostSaved) { $failures += 'host progress.save mtime did not update' }
        if (-not $compSaved) { $failures += 'companion progress.save mtime did not update' }
        if (-not $flags.returnedToMenu) { $failures += 'did not return to MAIN_MENU on both instances' }
        if ($result.forced_return_suspected) { $failures += 'continue_game_over looks like the old 15s Enable skip' }
        if ($result.protected_save_failures.Count -gt 0) { $failures += 'protected saves changed' }
        $result.flags = $flags
        $result.failures = $failures
        $result.ended_at_utc = [DateTime]::UtcNow.ToString('o')
        $result.outcome = $(if ($failures.Count -eq 0) { 'gameover_save_ok' } else { 'failed' })
        Write-JsonFile -Path (Join-Path $Evidence 'gameover-save-result.json') -Object $result
        Write-RunLog ('RESULT outcome={0} failures={1}' -f $result.outcome, ($failures -join '; '))
        if ($result.outcome -eq 'failed') { throw ('GAMEOVER_SAVE_FAIL ' + ($failures -join '; ')) }
        Write-Output ('GAMEOVER_SAVE_OK host_mtime=' + $result.host_progress_after.mtime_utc)
    }
    finally {
        Stop-TrackedGames @($hostPid, $companionPid)
        Stop-BudgetProxy -Process $proxy
        Remove-Item Env:STS2_ENABLE_DEBUG_ACTIONS -ErrorAction SilentlyContinue
    }
}

switch ($Mode) {
    'Prepare' { Invoke-Prepare }
    'ErrorFixture' { Invoke-Prepare; $p = Invoke-ErrorFixture; Write-Output ('ERROR_FIXTURE_OK ' + $p) }
    'DiscoverModels' { Invoke-Prepare; $c = Invoke-DiscoverModels; Write-Output ('DISCOVER_OK count=' + $c.model_count + ' expected_present=' + $c.expected_id_present) }
    'Execute' { Invoke-Execute }
    'GameOverSave' { Invoke-GameOverSave }
    'Disconnect' { Invoke-Disconnect }
}

