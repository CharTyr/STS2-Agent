param(
    [string]$ProjectRoot = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
}
else {
    $ProjectRoot = (Resolve-Path $ProjectRoot).Path
}

$sourceRoot = Join-Path $ProjectRoot "extraction/decompiled"
$outputRoot = Join-Path $ProjectRoot "docs/game-knowledge"

function Get-SourceText {
    param(
        [string]$Path
    )

    Get-Content -Path $Path -Raw
}

function Get-RegexValue {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Group = "value"
    )

    $match = [regex]::Match($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if ($match.Success) {
        return $match.Groups[$Group].Value.Trim()
    }

    return ""
}

function Get-RegexMatches {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Group = "value"
    )

    $matches = [regex]::Matches($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $values = New-Object System.Collections.Generic.List[string]

    foreach ($match in $matches) {
        $values.Add($match.Groups[$Group].Value.Trim())
    }

    return $values
}

function Get-MethodBody {
    param(
        [string]$Text,
        [string]$MethodName
    )

    $match = [regex]::Match(
        $Text,
        "(?s)$MethodName\s*\([^\)]*\)\s*\{(?<body>.*?)\n\t\}",
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )

    if ($match.Success) {
        return $match.Groups["body"].Value.Trim()
    }

    return ""
}

function Get-CommandSummary {
    param(
        [string]$MethodBody
    )

    if ([string]::IsNullOrWhiteSpace($MethodBody)) {
        return ""
    }

    $matches = [regex]::Matches($MethodBody, '(?<cmd>\w+Cmd)\.(?<action>\w+)(?:<(?<generic>\w+)>)?', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $tokens = New-Object System.Collections.Generic.List[string]

    foreach ($match in $matches) {
        $cmd = $match.Groups["cmd"].Value.Trim()
        $action = $match.Groups["action"].Value.Trim()
        $generic = $match.Groups["generic"].Value.Trim()
        $token = if ($generic) { "$cmd.$action<$generic>" } else { "$cmd.$action" }
        if (-not $tokens.Contains($token)) {
            $tokens.Add($token)
        }
    }

    return ($tokens -join ", ")
}

function Get-UpgradeSummary {
    param(
        [string]$MethodBody
    )

    if ([string]::IsNullOrWhiteSpace($MethodBody)) {
        return ""
    }

    $tokens = New-Object System.Collections.Generic.List[string]

    foreach ($match in [regex]::Matches($MethodBody, 'UpgradeValueBy\((?<value>[^\)]+)\)')) {
        $value = $match.Groups["value"].Value.Trim()
        if (-not $tokens.Contains("UpgradeValueBy($value)")) {
            $tokens.Add("UpgradeValueBy($value)")
        }
    }

    foreach ($match in [regex]::Matches($MethodBody, 'AddKeyword\(CardKeyword\.(?<value>\w+)\)')) {
        $value = $match.Groups["value"].Value.Trim()
        if (-not $tokens.Contains("AddKeyword($value)")) {
            $tokens.Add("AddKeyword($value)")
        }
    }

    return ($tokens -join ", ")
}

function Get-DynamicVarSummary {
    param(
        [string]$Text
    )

    $tokens = New-Object System.Collections.Generic.List[string]

    foreach ($match in [regex]::Matches($Text, 'new\s+(?<type>\w+Var(?:<\w+>)?)\((?<args>[^\)]*)\)')) {
        $type = $match.Groups["type"].Value.Trim()
        $args = ($match.Groups["args"].Value.Trim() -replace '\s+', ' ')
        $token = "$type($args)"
        if (-not $tokens.Contains($token)) {
            $tokens.Add($token)
        }
    }

    return ($tokens -join ", ")
}

function Get-MoveSummary {
    param(
        [string]$Text
    )

    $matches = [regex]::Matches(
        $Text,
        'MoveState\s+\w+\s*=\s*new MoveState\("(?<name>[^"]+)",\s*[^,]+,\s*new\s+(?<intent>\w+)\((?<args>[^\)]*)\)\)',
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )

    $tokens = New-Object System.Collections.Generic.List[string]
    foreach ($match in $matches) {
        $name = $match.Groups["name"].Value.Trim()
        $intent = $match.Groups["intent"].Value.Trim()
        $args = ($match.Groups["args"].Value.Trim() -replace '\s+', ' ')
        $token = if ($args) { "$name=$intent($args)" } else { "$name=$intent" }
        if (-not $tokens.Contains($token)) {
            $tokens.Add($token)
        }
    }

    return ($tokens -join "; ")
}

function ConvertTo-MarkdownTable {
    param(
        [string[]]$Headers,
        [object[]]$Rows
    )

    $table = New-Object System.Collections.Generic.List[string]
    $table.Add("| " + ($Headers -join " | ") + " |")
    $table.Add("| " + (($Headers | ForEach-Object { "---" }) -join " | ") + " |")

    foreach ($row in $Rows) {
        $cells = foreach ($header in $Headers) {
            $value = $row.$header
            if ($null -eq $value) {
                ""
            } else {
                ($value.ToString() -replace "\|", "\\|")
            }
        }

        $table.Add("| " + ($cells -join " | ") + " |")
    }

    $table -join "`n"
}

function New-MarkdownDocument {
    param(
        [string]$Title,
        [string]$Description,
        [string]$Body
    )

    $generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"
    (
        "# $Title",
        "",
        "> Auto-generated from extraction/decompiled in this repository.  ",
        "> Generated at: $generatedAt",
        "",
        $Description,
        "",
        $Body
    ) -join "`n"
}

function Get-CardEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Cards"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $ctor = [regex]::Match(
            $text,
            ':\s*base\((?<cost>[^,]+),\s*CardType\.(?<type>\w+),\s*CardRarity\.(?<rarity>\w+),\s*TargetType\.(?<target>\w+)\)',
            [System.Text.RegularExpressions.RegexOptions]::Singleline
        )

        $rows.Add([pscustomobject]@{
            Name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            Cost = if ($ctor.Success) { $ctor.Groups["cost"].Value.Trim() } else { "" }
            Type = if ($ctor.Success) { $ctor.Groups["type"].Value.Trim() } else { "" }
            Rarity = if ($ctor.Success) { $ctor.Groups["rarity"].Value.Trim() } else { "" }
            Target = if ($ctor.Success) { $ctor.Groups["target"].Value.Trim() } else { "" }
            Vars = Get-DynamicVarSummary -Text $text
            OnPlay = Get-CommandSummary -MethodBody (Get-MethodBody -Text $text -MethodName "OnPlay")
            OnUpgrade = Get-UpgradeSummary -MethodBody (Get-MethodBody -Text $text -MethodName "OnUpgrade")
        })
    }

    return $rows
}

function Get-CharacterEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Characters"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $rows.Add([pscustomobject]@{
            Name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            Gender = Get-RegexValue -Text $text -Pattern 'CharacterGender\.(?<value>\w+)'
            StartingHp = Get-RegexValue -Text $text -Pattern 'StartingHp\s*=>\s*(?<value>\d+)'
            StartingGold = Get-RegexValue -Text $text -Pattern 'StartingGold\s*=>\s*(?<value>\d+)'
            UnlocksAfter = Get-RegexValue -Text $text -Pattern 'UnlocksAfterRunAs\s*=>\s*ModelDb\.Character<(?<value>\w+)>\(\)'
            StartingDeck = (Get-RegexMatches -Text $text -Pattern 'ModelDb\.Card<(?<value>\w+)>\(\)') -join ", "
            StartingRelics = (Get-RegexMatches -Text $text -Pattern 'ModelDb\.Relic<(?<value>\w+)>\(\)') -join ", "
        })
    }

    return $rows
}

function Get-PotionEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Potions"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $rows.Add([pscustomobject]@{
            Name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            Rarity = Get-RegexValue -Text $text -Pattern 'PotionRarity\.(?<value>\w+)'
            Usage = Get-RegexValue -Text $text -Pattern 'PotionUsage\.(?<value>\w+)'
            Target = Get-RegexValue -Text $text -Pattern 'TargetType\.(?<value>\w+)'
            Vars = Get-DynamicVarSummary -Text $text
            OnUse = Get-CommandSummary -MethodBody (Get-MethodBody -Text $text -MethodName "OnUse")
        })
    }

    return $rows
}

function Get-MonsterEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Monsters"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $rows.Add([pscustomobject]@{
            Name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            MinHp = Get-RegexValue -Text $text -Pattern 'MinInitialHp\s*=>\s*(?<value>\d+)'
            MaxHp = Get-RegexValue -Text $text -Pattern 'MaxInitialHp\s*=>\s*(?<value>\d+)'
            Moves = Get-MoveSummary -Text $text
            Passive = Get-CommandSummary -MethodBody (Get-MethodBody -Text $text -MethodName "AfterAddedToRoom")
        })
    }

    return $rows
}

function Get-EventEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Events"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $rows.Add([pscustomobject]@{
            Name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            BaseType = Get-RegexValue -Text $text -Pattern 'public\s+(?:sealed\s+)?class\s+\w+\s*:\s*(?<value>\w+)'
        })
    }

    return $rows
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$cards = Get-CardEntries
$characters = Get-CharacterEntries
$potions = Get-PotionEntries
$monsters = Get-MonsterEntries
$events = Get-EventEntries

$summaryBody = @"
## Coverage

- Characters: $($characters.Count)
- Cards: $($cards.Count)
- Monsters: $($monsters.Count)
- Potions: $($potions.Count)
- Events: $($events.Count)

## Usage

- Prefer these indexes when MCP returns `card_id`, `enemy_id`, `event_id`, or `potion_id`.
- Read `docs/game-knowledge/agent-reference.md` first, then inspect the specific index file.
- Refresh this knowledge base after game updates by running `powershell -ExecutionPolicy Bypass -File "scripts/generate-sts2-knowledge.ps1"`.
"@

Set-Content -Path (Join-Path $outputRoot "README.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "STS2 Game Knowledge Base" `
    -Description "Local AI-facing indexes generated from the current repository's decompiled STS2 data." `
    -Body $summaryBody)

$charactersBody = ConvertTo-MarkdownTable -Headers @("Name", "Gender", "StartingHp", "StartingGold", "UnlocksAfter", "StartingRelics", "StartingDeck") -Rows $characters
Set-Content -Path (Join-Path $outputRoot "characters.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Character Index" `
    -Description "Quick mapping for character internal names, starting state, and opening deck/relics." `
    -Body $charactersBody)

$cardsBody = ConvertTo-MarkdownTable -Headers @("Name", "Cost", "Type", "Rarity", "Target") -Rows $cards
Set-Content -Path (Join-Path $outputRoot "cards.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Card Index" `
    -Description "Base metadata for card internal names. Use this when MCP returns unfamiliar `card_id` values." `
    -Body $cardsBody)

$cardBehaviorBody = ConvertTo-MarkdownTable -Headers @("Name", "Vars", "OnPlay", "OnUpgrade") -Rows $cards
Set-Content -Path (Join-Path $outputRoot "card-behaviors.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Card Behavior Index" `
    -Description "Behavior-oriented summaries extracted from card source. Command names are intentionally kept close to code for tool-friendly lookup." `
    -Body $cardBehaviorBody)

$monstersBody = ConvertTo-MarkdownTable -Headers @("Name", "MinHp", "MaxHp") -Rows $monsters
Set-Content -Path (Join-Path $outputRoot "monsters.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Monster Index" `
    -Description "Initial HP range lookup for monster internal names seen in `enemy_id`." `
    -Body $monstersBody)

$monsterBehaviorBody = ConvertTo-MarkdownTable -Headers @("Name", "Moves", "Passive") -Rows $monsters
Set-Content -Path (Join-Path $outputRoot "monster-behaviors.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Monster Behavior Index" `
    -Description "Move-state and passive-command summaries extracted from monster source." `
    -Body $monsterBehaviorBody)

$potionsBody = ConvertTo-MarkdownTable -Headers @("Name", "Rarity", "Usage", "Target") -Rows $potions
Set-Content -Path (Join-Path $outputRoot "potions.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Potion Index" `
    -Description "Potion rarity, usage timing, and targeting metadata for future potion support." `
    -Body $potionsBody)

$potionBehaviorBody = ConvertTo-MarkdownTable -Headers @("Name", "Vars", "OnUse") -Rows $potions
Set-Content -Path (Join-Path $outputRoot "potion-behaviors.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Potion Behavior Index" `
    -Description "Behavior summaries extracted from potion source. Useful when adding potion support or planning item usage." `
    -Body $potionBehaviorBody)

$eventsBody = ConvertTo-MarkdownTable -Headers @("Name", "BaseType") -Rows $events
Set-Content -Path (Join-Path $outputRoot "events.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Event Index" `
    -Description "Event model lookup by internal name and base type for coarse classification." `
    -Body $eventsBody)

Write-Host "[generate-sts2-knowledge] Generated knowledge base in $outputRoot"
