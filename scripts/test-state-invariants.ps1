param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [int]$TimeoutSec = 5
)

$ErrorActionPreference = "Stop"

function Invoke-JsonEndpoint {
    param(
        [string]$Path
    )

    $response = Invoke-WebRequest -Uri ($BaseUrl.TrimEnd("/") + $Path) -UseBasicParsing -TimeoutSec $TimeoutSec
    return $response.Content | ConvertFrom-Json
}

function Add-MissingActionFailure {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [System.Collections.Generic.HashSet[string]]$ActionSet,
        [string]$ActionName,
        [string]$Reason
    )

    if (-not $ActionSet.Contains($ActionName)) {
        $Failures.Add("missing action '$ActionName': $Reason")
    }
}

function Add-ForbiddenActionFailure {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [System.Collections.Generic.HashSet[string]]$ActionSet,
        [string]$ActionName,
        [string]$Reason
    )

    if ($ActionSet.Contains($ActionName)) {
        $Failures.Add("unexpected action '$ActionName': $Reason")
    }
}

function Test-PlayerSummaries {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [object[]]$Players,
        [string]$Label,
        [int]$ExpectedCount = -1
    )

    if ($ExpectedCount -ge 0 -and $Players.Count -ne $ExpectedCount) {
        $Failures.Add("$Label count should be $ExpectedCount but was $($Players.Count)")
    }

    if ($Players.Count -eq 0) {
        $Failures.Add("$Label should not be empty when the payload exists")
        return
    }

    $localPlayers = @($Players | Where-Object { $_.is_local })
    if ($localPlayers.Count -ne 1) {
        $Failures.Add("$Label should contain exactly one local player entry")
    }

    $playerIds = @($Players | ForEach-Object { [string]$_.player_id } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($playerIds.Count -ne $Players.Count) {
        $Failures.Add("$Label entries must expose non-empty player_id values")
    }
    elseif (($playerIds | Select-Object -Unique).Count -ne $playerIds.Count) {
        $Failures.Add("$Label player_id values must be unique")
    }
}

function Test-IndexedTargetContract {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [object]$Payload,
        [string]$Label,
        [int]$EnemyCount,
        [int]$PlayerCount,
        [bool]$ShouldHaveTargetsWhenUsable
    )

    $scope = [string]$Payload.target_index_space
    $indices = @($Payload.valid_target_indices | Where-Object { $null -ne $_ } | ForEach-Object { [int]$_ })

    if ($Payload.requires_target) {
        if ([string]::IsNullOrWhiteSpace($scope)) {
            $Failures.Add("$Label requires_target=true but target_index_space is missing")
            return
        }

        if (($indices | Select-Object -Unique).Count -ne $indices.Count) {
            $Failures.Add("$Label valid_target_indices must not contain duplicates")
        }

        switch ($scope) {
            "enemies" {
                foreach ($idx in $indices) {
                    if ([int]$idx -lt 0 -or [int]$idx -ge $EnemyCount) {
                        $Failures.Add("$Label valid_target_indices contains an out-of-range combat.enemies[] index")
                        break
                    }
                }
            }
            "players" {
                foreach ($idx in $indices) {
                    if ([int]$idx -lt 0 -or [int]$idx -ge $PlayerCount) {
                        $Failures.Add("$Label valid_target_indices contains an out-of-range combat.players[] index")
                        break
                    }
                }
            }
            default {
                $Failures.Add("$Label target_index_space should be 'enemies' or 'players'")
            }
        }

        if ($ShouldHaveTargetsWhenUsable -and $indices.Count -eq 0) {
            $Failures.Add("$Label requires_target=true but valid_target_indices is empty")
        }
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($scope)) {
            $Failures.Add("$Label requires_target=false but target_index_space is populated")
        }

        if ($indices.Count -gt 0) {
            $Failures.Add("$Label requires_target=false but valid_target_indices is populated")
        }
    }
}

function Test-CardRuntimeMetadata {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [object]$Card,
        [string]$Label
    )

    if ($null -eq $Card) {
        return
    }

    $propertyNames = @($Card.PSObject.Properties.Name)
    foreach ($requiredProperty in @("rules_text", "resolved_rules_text", "dynamic_values")) {
        if ($propertyNames -notcontains $requiredProperty) {
            $Failures.Add("$Label should expose $requiredProperty")
        }
    }

    if (($propertyNames -contains "rules_text") -and $null -ne $Card.rules_text -and $Card.rules_text -isnot [string]) {
        $Failures.Add("$Label rules_text should be a string when populated")
    }

    if (($propertyNames -contains "resolved_rules_text") -and $null -ne $Card.resolved_rules_text -and $Card.resolved_rules_text -isnot [string]) {
        $Failures.Add("$Label resolved_rules_text should be a string when populated")
    }

    if ($propertyNames -notcontains "dynamic_values") {
        return
    }

    $dynamicValues = @($Card.dynamic_values)
    for ($index = 0; $index -lt $dynamicValues.Count; $index++) {
        $dynamicValue = $dynamicValues[$index]
        if ($null -eq $dynamicValue) {
            $Failures.Add("$Label dynamic_values[$index] should not be null")
            continue
        }

        $dynamicPropertyNames = @($dynamicValue.PSObject.Properties.Name)
        foreach ($requiredProperty in @("name", "base_value", "current_value", "enchanted_value", "is_modified", "was_just_upgraded")) {
            if ($dynamicPropertyNames -notcontains $requiredProperty) {
                $Failures.Add("$Label dynamic_values[$index] should expose $requiredProperty")
            }
        }

        if ([string]::IsNullOrWhiteSpace([string]$dynamicValue.name)) {
            $Failures.Add("$Label dynamic_values[$index].name should be populated")
        }

        foreach ($numericProperty in @("base_value", "current_value", "enchanted_value")) {
            $parsedNumber = 0
            if (-not [int]::TryParse([string]$dynamicValue.$numericProperty, [ref]$parsedNumber)) {
                $Failures.Add("$Label dynamic_values[$index].$numericProperty should be an integer")
            }
        }

        foreach ($booleanProperty in @("is_modified", "was_just_upgraded")) {
            if ($dynamicValue.$booleanProperty -isnot [bool]) {
                $Failures.Add("$Label dynamic_values[$index].$booleanProperty should be a boolean")
            }
        }
    }
}

$stateResponse = Invoke-JsonEndpoint -Path "/state"
$actionsResponse = Invoke-JsonEndpoint -Path "/actions/available"

$state = $stateResponse.data
$actionSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($action in @($actionsResponse.data.actions)) {
    if ($null -ne $action -and $null -ne $action.name) {
        [void]$actionSet.Add([string]$action.name)
    }
}

$stateActionSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($actionName in @($state.available_actions)) {
    if (-not [string]::IsNullOrWhiteSpace([string]$actionName)) {
        [void]$stateActionSet.Add([string]$actionName)
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

if ($null -eq $state.session) {
    $failures.Add("state.session should always be populated")
}
else {
    $sessionMode = [string]$state.session.mode
    $sessionPhase = [string]$state.session.phase
    $controlScope = [string]$state.session.control_scope

    if (@("singleplayer", "multiplayer") -notcontains $sessionMode) {
        $failures.Add("state.session.mode should be 'singleplayer' or 'multiplayer'")
    }

    if (@("menu", "character_select", "multiplayer_lobby", "run") -notcontains $sessionPhase) {
        $failures.Add("state.session.phase should be one of menu, character_select, multiplayer_lobby, run")
    }

    if ($controlScope -ne "local_player") {
        $failures.Add("state.session.control_scope should stay 'local_player'")
    }
}

foreach ($actionName in $stateActionSet) {
    if (-not $actionSet.Contains($actionName)) {
        $failures.Add("state.available_actions contains '$actionName' but /actions/available does not")
    }
}

foreach ($actionName in $actionSet) {
    if (-not $stateActionSet.Contains($actionName)) {
        $failures.Add("/actions/available contains '$actionName' but state.available_actions does not")
    }
}

if ($null -ne $state.selection -and @($state.selection.cards).Count -gt 0) {
    Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "select_deck_card" -Reason "selection.cards[] is populated"
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "proceed" -Reason "card selection should not expose proceed while selection.cards[] is populated"

    if ($state.screen -ne "CARD_SELECTION") {
        $failures.Add("selection.cards[] is populated but state.screen is '$($state.screen)' instead of 'CARD_SELECTION'")
    }
}

foreach ($card in @($state.selection.cards)) {
    Test-CardRuntimeMetadata -Failures $failures -Card $card -Label "selection.cards[$($card.index)]"
}

if ($null -ne $state.selection) {
    $minSelect = [int]$state.selection.min_select
    $maxSelect = [int]$state.selection.max_select
    $selectedCount = [int]$state.selection.selected_count
    $requiresConfirmation = [bool]$state.selection.requires_confirmation
    $canConfirm = [bool]$state.selection.can_confirm

    if ($maxSelect -lt $minSelect) {
        $failures.Add("selection.max_select should be >= selection.min_select")
    }

    if ($selectedCount -lt 0) {
        $failures.Add("selection.selected_count should never be negative")
    }

    if ($selectedCount -gt $maxSelect) {
        $failures.Add("selection.selected_count should never exceed selection.max_select")
    }

    if ($canConfirm -and (-not $requiresConfirmation)) {
        $failures.Add("selection.can_confirm should only be true when selection.requires_confirmation is true")
    }

    if ($requiresConfirmation -and $canConfirm -and $selectedCount -lt $minSelect) {
        $failures.Add("selection.can_confirm should stay false until selection.selected_count reaches selection.min_select")
    }

    if ($requiresConfirmation) {
        if ($canConfirm) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "confirm_selection" -Reason "selection.requires_confirmation=true and selection.can_confirm=true"
        }
        else {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "confirm_selection" -Reason "selection.requires_confirmation=true but selection.can_confirm=false"
        }
    }
    else {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "confirm_selection" -Reason "selection does not require manual confirmation"
    }
}
else {
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "confirm_selection" -Reason "selection payload is absent"
}

if ($null -ne $state.reward) {
    Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "collect_rewards_and_proceed" -Reason "reward payload is present"
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "proceed" -Reason "reward flows should use reward-specific actions instead of proceed"
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "discard_potion" -Reason "reward screens should not expose discard_potion"

    if ($state.reward.pending_card_choice) {
        if (@($state.reward.card_options).Count -gt 0) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_reward_card" -Reason "reward.card_options[] is populated"
        }

        if (@($state.reward.alternatives).Count -gt 0) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "skip_reward_cards" -Reason "reward.alternatives[] is populated"
        }
    }
    else {
        if (@($state.reward.rewards | Where-Object { $_.claimable }).Count -gt 0) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "claim_reward" -Reason "reward.rewards[] still contains claimable items"
        }
    }
}

foreach ($card in @($state.reward.card_options)) {
    Test-CardRuntimeMetadata -Failures $failures -Card $card -Label "reward.card_options[$($card.index)]"
}

if ($null -ne $state.map -and @($state.map.available_nodes).Count -gt 0) {
    Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_map_node" -Reason "map.available_nodes[] is populated"
}
elseif ($null -ne $state.map) {
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_map_node" -Reason "map.available_nodes[] is empty"
}

if ($null -ne $state.chest) {
    if (-not $state.chest.is_opened) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "open_chest" -Reason "chest is present and not yet opened"
    }

    if ((@($state.chest.relic_options).Count -gt 0) -and (-not $state.chest.has_relic_been_claimed)) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_treasure_relic" -Reason "chest.relic_options[] is populated"
    }

    if (($actionSet.Contains("proceed")) -and (-not $state.chest.has_relic_been_claimed)) {
        $failures.Add("chest.has_relic_been_claimed should be true before proceed is exposed")
    }

    if ($state.chest.has_relic_been_claimed) {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_treasure_relic" -Reason "chest relic has already been claimed"
    }
}

if ($null -ne $state.event) {
    if (@($state.event.options | Where-Object { -not $_.is_locked }).Count -gt 0) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_event_option" -Reason "event has unlocked options"
    }

    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "proceed" -Reason "event flows should use choose_event_option, including finished synthetic proceed"

    $proceedOptions = @($state.event.options | Where-Object { $_.is_proceed })
    if ($state.event.is_finished) {
        if (@($state.event.options).Count -ne 1) {
            $failures.Add("finished events should only expose one synthetic proceed option")
        }

        if ($proceedOptions.Count -ne 1) {
            $failures.Add("finished events should expose exactly one synthetic proceed option")
        }
    }
    elseif ($proceedOptions.Count -gt 0) {
        $failures.Add("unfinished events should not expose synthetic proceed options")
    }
}

if ($null -ne $state.rest -and @($state.rest.options | Where-Object { $_.is_enabled }).Count -gt 0) {
    Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_rest_option" -Reason "rest.options[] has enabled entries"
}

if ($null -ne $state.shop) {
    if ($state.shop.can_open) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "open_shop_inventory" -Reason "shop.can_open=true"
    }
    else {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "open_shop_inventory" -Reason "shop.can_open=false"
    }

    if ($state.shop.can_close) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "close_shop_inventory" -Reason "shop.can_close=true"
    }
    else {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "close_shop_inventory" -Reason "shop.can_close=false"
    }

    if ($state.shop.is_open) {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "proceed" -Reason "open shop inventory should not expose proceed"

        if (@($state.shop.cards | Where-Object { $_.is_stocked -and $_.enough_gold }).Count -gt 0) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_card" -Reason "shop.is_open=true and shop.cards[] has purchasable entries"
        }
        else {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_card" -Reason "shop.is_open=true but no shop.cards[] entries are purchasable"
        }

        if (@($state.shop.relics | Where-Object { $_.is_stocked -and $_.enough_gold }).Count -gt 0) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_relic" -Reason "shop.is_open=true and shop.relics[] has purchasable entries"
        }
        else {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_relic" -Reason "shop.is_open=true but no shop.relics[] entries are purchasable"
        }

        if (@($state.shop.potions | Where-Object { $_.is_stocked -and $_.enough_gold }).Count -gt 0) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_potion" -Reason "shop.is_open=true and shop.potions[] has purchasable entries"
        }
        else {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_potion" -Reason "shop.is_open=true but no shop.potions[] entries are purchasable"
        }

        if ($null -ne $state.shop.card_removal -and $state.shop.card_removal.available -and $state.shop.card_removal.enough_gold -and (-not $state.shop.card_removal.used)) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "remove_card_at_shop" -Reason "shop.is_open=true and shop.card_removal is available and affordable"
        }
        else {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "remove_card_at_shop" -Reason "shop.is_open=true but shop.card_removal is not currently purchasable"
        }
    }
    else {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_card" -Reason "shop inventory is closed"
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_relic" -Reason "shop inventory is closed"
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "buy_potion" -Reason "shop inventory is closed"
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "remove_card_at_shop" -Reason "shop inventory is closed"
    }
}

foreach ($card in @($state.shop.cards)) {
    Test-CardRuntimeMetadata -Failures $failures -Card $card -Label "shop.cards[$($card.index)]"
}

if ($null -ne $state.character_select) {
    if ($state.session.phase -ne "character_select") {
        $failures.Add("state.character_select is populated but state.session.phase is '$($state.session.phase)' instead of 'character_select'")
    }

    $expectedCharacterSelectMode = if ([bool]$state.character_select.is_multiplayer) { "multiplayer" } else { "singleplayer" }
    if ($state.session.mode -ne $expectedCharacterSelectMode) {
        $failures.Add("state.session.mode should match character_select.is_multiplayer")
    }

    if (@($state.character_select.characters | Where-Object { -not $_.is_locked }).Count -gt 0) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "select_character" -Reason "character_select has unlocked choices"
    }

    if ($state.character_select.can_embark) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "embark" -Reason "character_select.can_embark=true"
    }

    if ($state.character_select.can_unready) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "unready" -Reason "character_select.can_unready=true"
    }
    else {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "unready" -Reason "character_select.can_unready=false"
    }

    if ($state.character_select.can_increase_ascension) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "increase_ascension" -Reason "character_select.can_increase_ascension=true"
    }
    else {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "increase_ascension" -Reason "character_select.can_increase_ascension=false"
    }

    if ($state.character_select.can_decrease_ascension) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "decrease_ascension" -Reason "character_select.can_decrease_ascension=true"
    }
    else {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "decrease_ascension" -Reason "character_select.can_decrease_ascension=false"
    }

    if ([int]$state.character_select.max_players -gt 0 -and [int]$state.character_select.player_count -gt [int]$state.character_select.max_players) {
        $failures.Add("character_select.player_count should not exceed character_select.max_players")
    }

    Test-PlayerSummaries -Failures $failures -Players @($state.character_select.players) -Label "character_select.players" -ExpectedCount ([int]$state.character_select.player_count)

    $localCharacterPlayer = @($state.character_select.players | Where-Object { $_.is_local } | Select-Object -First 1)
    if ($localCharacterPlayer.Count -eq 1 -and [bool]$state.character_select.local_ready -ne [bool]$localCharacterPlayer[0].is_ready) {
        $failures.Add("character_select.local_ready should match the local player roster entry")
    }
}

if ($null -ne $state.multiplayer_lobby) {
    if ($state.session.mode -ne "multiplayer") {
        $failures.Add("multiplayer_lobby payload is populated but state.session.mode is not 'multiplayer'")
    }

    if ($state.session.phase -ne "multiplayer_lobby") {
        $failures.Add("multiplayer_lobby payload is populated but state.session.phase is '$($state.session.phase)' instead of 'multiplayer_lobby'")
    }

    if ($state.screen -ne "MULTIPLAYER_LOBBY") {
        $failures.Add("multiplayer_lobby payload is populated but state.screen is '$($state.screen)' instead of 'MULTIPLAYER_LOBBY'")
    }

    if ([string]::IsNullOrWhiteSpace([string]$state.multiplayer_lobby.net_game_type)) {
        $failures.Add("multiplayer_lobby.net_game_type should be populated")
    }

    if ([int]$state.multiplayer_lobby.join_port -le 0) {
        $failures.Add("multiplayer_lobby.join_port should be a positive integer")
    }

    if (@($state.multiplayer_lobby.characters).Count -eq 0) {
        $failures.Add("multiplayer_lobby.characters should not be empty")
    }

    if ($state.multiplayer_lobby.has_lobby) {
        if ([int]$state.multiplayer_lobby.player_count -ne @($state.multiplayer_lobby.players).Count) {
            $failures.Add("multiplayer_lobby.player_count should match multiplayer_lobby.players.Count when has_lobby=true")
        }

        if ([int]$state.multiplayer_lobby.max_players -gt 0 -and [int]$state.multiplayer_lobby.player_count -gt [int]$state.multiplayer_lobby.max_players) {
            $failures.Add("multiplayer_lobby.player_count should not exceed multiplayer_lobby.max_players")
        }

        if (@($state.multiplayer_lobby.players).Count -gt 0) {
            Test-PlayerSummaries -Failures $failures -Players @($state.multiplayer_lobby.players) -Label "multiplayer_lobby.players" -ExpectedCount ([int]$state.multiplayer_lobby.player_count)
        }

        $localLobbyPlayer = @($state.multiplayer_lobby.players | Where-Object { $_.is_local } | Select-Object -First 1)
        if ($localLobbyPlayer.Count -eq 1 -and [bool]$state.multiplayer_lobby.local_ready -ne [bool]$localLobbyPlayer[0].is_ready) {
            $failures.Add("multiplayer_lobby.local_ready should match the local multiplayer_lobby.players entry")
        }

        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "select_character" -Reason "multiplayer_lobby has active character options"

        if ($state.multiplayer_lobby.can_ready) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "ready_multiplayer_lobby" -Reason "multiplayer_lobby.can_ready=true"
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "unready" -Reason "multiplayer_lobby.can_ready=true should suppress unready"
        }
        elseif ($state.multiplayer_lobby.can_unready) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "unready" -Reason "multiplayer_lobby.can_unready=true"
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "ready_multiplayer_lobby" -Reason "multiplayer_lobby.can_unready=true should suppress ready_multiplayer_lobby"
        }
        else {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "ready_multiplayer_lobby" -Reason "multiplayer_lobby cannot ready right now"
        }

        if ($state.multiplayer_lobby.can_disconnect) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "disconnect_multiplayer_lobby" -Reason "multiplayer_lobby.can_disconnect=true"
        }
        else {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "disconnect_multiplayer_lobby" -Reason "multiplayer_lobby.can_disconnect=false"
        }

        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "host_multiplayer_lobby" -Reason "multiplayer_lobby.has_lobby=true should suppress host action"
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "join_multiplayer_lobby" -Reason "multiplayer_lobby.has_lobby=true should suppress join action"
    }
    else {
        if ([int]$state.multiplayer_lobby.player_count -ne 0) {
            $failures.Add("multiplayer_lobby.player_count should be 0 when has_lobby=false")
        }

        if (@($state.multiplayer_lobby.players).Count -ne 0) {
            $failures.Add("multiplayer_lobby.players should be empty when has_lobby=false")
        }

        if ($state.multiplayer_lobby.can_host) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "host_multiplayer_lobby" -Reason "multiplayer_lobby.can_host=true"
        }

        if ($state.multiplayer_lobby.can_join) {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "join_multiplayer_lobby" -Reason "multiplayer_lobby.can_join=true"
        }

        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "ready_multiplayer_lobby" -Reason "multiplayer_lobby.has_lobby=false"
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "disconnect_multiplayer_lobby" -Reason "multiplayer_lobby.has_lobby=false"
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "unready" -Reason "multiplayer_lobby.has_lobby=false"
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "select_character" -Reason "multiplayer_lobby.has_lobby=false"
    }
}
else {
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "host_multiplayer_lobby" -Reason "multiplayer_lobby payload is absent"
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "join_multiplayer_lobby" -Reason "multiplayer_lobby payload is absent"
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "ready_multiplayer_lobby" -Reason "multiplayer_lobby payload is absent"
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "disconnect_multiplayer_lobby" -Reason "multiplayer_lobby payload is absent"
}

if ($null -ne $state.timeline) {
    if ($state.timeline.can_choose_epoch) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "choose_timeline_epoch" -Reason "timeline.can_choose_epoch=true"
    }

    if ($state.timeline.can_confirm_overlay) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "confirm_timeline_overlay" -Reason "timeline.can_confirm_overlay=true"
    }

    if ($state.timeline.back_enabled) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "close_main_menu_submenu" -Reason "timeline.back_enabled=true"
    }
}

if ($null -ne $state.modal) {
    if ($state.modal.can_confirm) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "confirm_modal" -Reason "modal.can_confirm=true"
    }

    if ($state.modal.can_dismiss) {
        Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "dismiss_modal" -Reason "modal.can_dismiss=true"
    }
}

if ($null -ne $state.game_over -and $state.game_over.can_return_to_main_menu) {
    Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "return_to_main_menu" -Reason "game_over.can_return_to_main_menu=true"
}

if ($state.in_combat -and $null -ne $state.combat) {
    $combatSelectionActive = ($state.screen -eq "CARD_SELECTION") -and ($null -ne $state.selection)
    $combatEnemyCount = @($state.combat.enemies).Count
    $combatPlayerCount = @($state.combat.players).Count

    foreach ($enemy in @($state.combat.enemies)) {
        if ($null -eq $enemy) {
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$enemy.intent) -and
            -not [string]::IsNullOrWhiteSpace([string]$enemy.move_id) -and
            [string]$enemy.intent -ne [string]$enemy.move_id) {
            $failures.Add("combat.enemies[$($enemy.index)] intent should stay aligned with move_id for backward compatibility")
        }

        if ($null -eq $enemy.intents) {
            $failures.Add("combat.enemies[$($enemy.index)] should expose intents[]")
            continue
        }

        foreach ($intent in @($enemy.intents)) {
            if ($null -eq $intent) {
                continue
            }

            $intentType = [string]$intent.intent_type
            if ([string]::IsNullOrWhiteSpace($intentType)) {
                $failures.Add("combat.enemies[$($enemy.index)].intents[] entries must expose intent_type")
                continue
            }

            if (@("Attack", "DeathBlow") -contains $intentType) {
                if ($null -eq $intent.damage -or $null -eq $intent.hits -or $null -eq $intent.total_damage) {
                    $failures.Add("combat.enemies[$($enemy.index)].intents[] attack payloads must expose damage, hits, and total_damage")
                    continue
                }

                $damageValue = [int]$intent.damage
                $hitsValue = [int]$intent.hits
                $totalDamageValue = [int]$intent.total_damage

                if ($damageValue -lt 0) {
                    $failures.Add("combat.enemies[$($enemy.index)].intents[] attack damage must not be negative")
                }

                if ($hitsValue -lt 1) {
                    $failures.Add("combat.enemies[$($enemy.index)].intents[] attack hits must be at least 1")
                }

                if ($totalDamageValue -ne ($damageValue * $hitsValue)) {
                    $failures.Add("combat.enemies[$($enemy.index)].intents[] attack total_damage must equal damage * hits")
                }
            }

            if ($intentType -eq "StatusCard") {
                if ($null -eq $intent.status_card_count -or [int]$intent.status_card_count -le 0) {
                    $failures.Add("combat.enemies[$($enemy.index)].intents[] StatusCard payloads must expose a positive status_card_count")
                }
            }
        }
    }

    if (@($state.combat.players).Count -gt 0) {
        Test-PlayerSummaries -Failures $failures -Players @($state.combat.players) -Label "combat.players"
    }

    if (@($state.combat.hand | Where-Object { $_.playable }).Count -gt 0) {
        if ($combatSelectionActive) {
            Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "play_card" -Reason "combat card-selection overlay should suspend play_card"
        }
        else {
            Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "play_card" -Reason "combat.hand[] has playable cards"
        }
    }
    elseif (-not $combatSelectionActive) {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "play_card" -Reason "combat.hand[] has no playable cards"
    }

    if ($combatSelectionActive) {
        Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "end_turn" -Reason "combat card-selection overlay should suspend end_turn"
    }

    if ($null -ne $state.combat.player) {
        $orbCount = @($state.combat.player.orbs).Count
        $orbCapacity = [int]$state.combat.player.orb_capacity
        $emptyOrbSlots = [int]$state.combat.player.empty_orb_slots

        if ($orbCapacity -lt 0) {
            $failures.Add("combat.player.orb_capacity should never be negative")
        }

        if ($orbCount -gt $orbCapacity) {
            $failures.Add("combat.player.orbs[] count exceeds combat.player.orb_capacity")
        }

        if ($emptyOrbSlots -ne ($orbCapacity - $orbCount)) {
            $failures.Add("combat.player.empty_orb_slots does not match orb_capacity - orbs.Count")
        }

        $expectedSlotIndex = 0
        foreach ($orb in @($state.combat.player.orbs)) {
            if ($orb.slot_index -ne $expectedSlotIndex) {
                $failures.Add("combat.player.orbs[] slot_index values must stay contiguous and zero-based")
                break
            }

            if ([string]::IsNullOrWhiteSpace([string]$orb.orb_id)) {
                $failures.Add("combat.player.orbs[] entries must expose orb_id")
                break
            }

            $expectedSlotIndex++
        }
    }

    $localCombatPlayer = @($state.combat.players | Where-Object { $_.is_local } | Select-Object -First 1)
    $localCombatPlayerIndex = if ($localCombatPlayer.Count -eq 1) { [int]$localCombatPlayer[0].slot_index } else { -1 }

    foreach ($card in @($state.combat.hand)) {
        if ($null -eq $card) {
            continue
        }

        Test-CardRuntimeMetadata -Failures $failures -Card $card -Label "combat.hand[$($card.index)]"

        Test-IndexedTargetContract `
            -Failures $failures `
            -Payload $card `
            -Label "combat.hand[$($card.index)]" `
            -EnemyCount $combatEnemyCount `
            -PlayerCount $combatPlayerCount `
            -ShouldHaveTargetsWhenUsable ([bool]$card.playable)

        if ($card.target_type -eq "AnyEnemy") {
            if (-not $card.requires_target) {
                $failures.Add("combat.hand[$($card.index)] should require target_index when target_type=AnyEnemy")
            }
        }

        if ($card.target_type -eq "AnyAlly") {
            if (-not $card.requires_target) {
                $failures.Add("combat.hand[$($card.index)] should require target_index when target_type=AnyAlly")
            }

            if ($card.target_index_space -ne "players") {
                $failures.Add("combat.hand[$($card.index)] should target combat.players[] when target_type=AnyAlly")
            }

            $cardTargetIndices = @($card.valid_target_indices | Where-Object { $null -ne $_ } | ForEach-Object { [int]$_ })
            if ($localCombatPlayerIndex -ge 0 -and @($cardTargetIndices | Where-Object { $_ -eq $localCombatPlayerIndex }).Count -gt 0) {
                $failures.Add("combat.hand[$($card.index)] AnyAlly targets should not include the local player slot")
            }
        }
    }
}

if (@($state.run.potions | Where-Object { $_.can_use }).Count -gt 0) {
    Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "use_potion" -Reason "run.potions[] has usable entries"
}
else {
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "use_potion" -Reason "run.potions[] has no usable entries"
}

if (@($state.run.potions | Where-Object { $_.can_discard }).Count -gt 0) {
    Add-MissingActionFailure -Failures $failures -ActionSet $actionSet -ActionName "discard_potion" -Reason "run.potions[] has discardable entries"
}
else {
    Add-ForbiddenActionFailure -Failures $failures -ActionSet $actionSet -ActionName "discard_potion" -Reason "run.potions[] has no discardable entries"
}

foreach ($potion in @($state.run.potions)) {
    if ($null -eq $potion -or -not $potion.occupied) {
        continue
    }

    if ($potion.target_type -eq "TargetedNoCreature" -and $potion.requires_target) {
        $failures.Add("potion '$($potion.potion_id)' should not require target_index when target_type=TargetedNoCreature")
    }

    if ($potion.target_type -eq "AnyEnemy" -and (-not $potion.requires_target)) {
        $failures.Add("potion '$($potion.potion_id)' should require target_index when target_type=AnyEnemy")
    }

    if ($potion.requires_target) {
        $combatEnemyCount = if ($null -ne $state.combat) { @($state.combat.enemies).Count } else { 0 }
        $combatPlayerCount = if ($null -ne $state.combat) { @($state.combat.players).Count } else { 0 }
        Test-IndexedTargetContract `
            -Failures $failures `
            -Payload $potion `
            -Label "run.potions[$($potion.index)]" `
            -EnemyCount $combatEnemyCount `
            -PlayerCount $combatPlayerCount `
            -ShouldHaveTargetsWhenUsable ([bool]$potion.can_use)
    }
    else {
        $potionTargetIndices = @($potion.valid_target_indices | Where-Object { $null -ne $_ } | ForEach-Object { [int]$_ })
        if (-not [string]::IsNullOrWhiteSpace([string]$potion.target_index_space)) {
            $failures.Add("potion '$($potion.potion_id)' should leave target_index_space empty when requires_target=false")
        }

        if ($potionTargetIndices.Count -gt 0) {
            $failures.Add("potion '$($potion.potion_id)' should leave valid_target_indices empty when requires_target=false")
        }
    }

    if ($potion.target_type -eq "AnyEnemy" -and $potion.target_index_space -ne "enemies") {
        $failures.Add("potion '$($potion.potion_id)' should target combat.enemies[] when target_type=AnyEnemy")
    }

    if ($potion.target_type -eq "AnyPlayer" -and $potion.requires_target -and $potion.target_index_space -ne "players") {
        $failures.Add("potion '$($potion.potion_id)' should target combat.players[] when target_type=AnyPlayer and requires_target=true")
    }
}

if ($null -ne $state.run) {
    if ([string]::IsNullOrWhiteSpace([string]$state.run.character_id)) {
        $failures.Add("run.character_id should always be populated when run payload exists")
    }

    if ([string]::IsNullOrWhiteSpace([string]$state.run.character_name)) {
        $failures.Add("run.character_name should always be populated when run payload exists")
    }

    if ($state.run.ascension -isnot [int] -and $state.run.ascension -isnot [long]) {
        $failures.Add("run.ascension should be an integer")
    }
    elseif ([int]$state.run.ascension -lt 0) {
        $failures.Add("run.ascension should never be negative")
    }

    $ascensionEffects = @($state.run.ascension_effects)
    if ($null -eq $state.run.ascension_effects) {
        $failures.Add("run.ascension_effects should always be populated")
    }
    elseif ([int]$state.run.ascension -ne $ascensionEffects.Count) {
        $failures.Add("run.ascension_effects count should match run.ascension")
    }

    for ($i = 0; $i -lt $ascensionEffects.Count; $i++) {
        $effect = $ascensionEffects[$i]
        if ($null -eq $effect) {
            $failures.Add("run.ascension_effects[$i] should not be null")
            continue
        }

        if ([string]::IsNullOrWhiteSpace([string]$effect.id)) {
            $failures.Add("run.ascension_effects[$i].id should be populated")
        }

        if ([string]::IsNullOrWhiteSpace([string]$effect.name)) {
            $failures.Add("run.ascension_effects[$i].name should be populated")
        }

        if ([string]::IsNullOrWhiteSpace([string]$effect.description)) {
            $failures.Add("run.ascension_effects[$i].description should be populated")
        }
    }

    if ([int]$state.run.base_orb_slots -lt 0) {
        $failures.Add("run.base_orb_slots should never be negative")
    }

    if (@($state.run.players).Count -gt 0) {
        Test-PlayerSummaries -Failures $failures -Players @($state.run.players) -Label "run.players"
    }

    foreach ($card in @($state.run.deck)) {
        Test-CardRuntimeMetadata -Failures $failures -Card $card -Label "run.deck[$($card.index)]"
    }
}

if ($null -ne $state.multiplayer) {
    if ($state.session.mode -ne "multiplayer") {
        $failures.Add("multiplayer payload is populated but state.session.mode is not 'multiplayer'")
    }

    if (-not $state.multiplayer.is_multiplayer) {
        $failures.Add("multiplayer payload should only be present when is_multiplayer=true")
    }

    if ([string]::IsNullOrWhiteSpace([string]$state.multiplayer.net_game_type)) {
        $failures.Add("multiplayer.net_game_type should be populated")
    }

    if ([int]$state.multiplayer.player_count -lt @($state.multiplayer.connected_player_ids).Count) {
        $failures.Add("multiplayer.connected_player_ids cannot exceed multiplayer.player_count")
    }

    if ([string]::IsNullOrWhiteSpace([string]$state.multiplayer.local_player_id)) {
        $failures.Add("multiplayer.local_player_id should be populated")
    }
}
elseif ($state.session.mode -ne "singleplayer" -and $state.session.phase -ne "multiplayer_lobby") {
    $failures.Add("state.session.mode should only stay 'multiplayer' without multiplayer payload during the multiplayer_lobby phase")
}

$summary = [pscustomobject]@{
    screen = $state.screen
    checked_actions = @($actionsResponse.data.actions).Count
    failure_count = $failures.Count
    warning_count = $warnings.Count
    failures = @($failures)
    warnings = @($warnings)
}

if ($failures.Count -gt 0) {
    $summary | ConvertTo-Json -Depth 5
    exit 1
}

$summary | ConvertTo-Json -Depth 5
