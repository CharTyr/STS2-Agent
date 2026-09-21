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

function Get-IntText {
    param(
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $text = $Value.Trim()
    if ($text.EndsWith("m")) {
        $text = $text.Substring(0, $text.Length - 1)
    }

    $parsed = 0.0
    if ([double]::TryParse($text, [ref]$parsed)) {
        if ($parsed -eq [math]::Floor($parsed)) {
            return ([int]$parsed).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        }

        return $parsed.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $text
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

###############################################################################
# Derived summaries.
#
# Everything below is derived from the decompiled sources only: numbers come
# from the CanonicalVars declarations of the model, amounts from the expressions
# actually passed to the command calls. An amount that cannot be resolved
# statically is rendered as "?" instead of a guess, and a call whose meaning is
# not mapped here falls back to a readable form of its own call name.
###############################################################################

$script:CosmeticCallPrefixes = @(
    "CreatureCmd.TriggerAnim",
    "CardCmd.Preview",
    "VfxCmd.",
    "SfxCmd.",
    "HoverTipFactory.",
    "NDebugAudioManager.",
    "PreloadManager.",
    "SceneHelper.",
    "SaveManager.",
    "TaskHelper."
)

$script:PileDisplayNames = @{
    "Draw"    = "draw pile"
    "Hand"    = "hand"
    "Discard" = "discard pile"
    "Exhaust" = "exhaust pile"
    "Deck"    = "deck"
    "Play"    = "play area"
    "None"    = "pile"
}

$script:RiskOrder = @{
    "none-detected"   = 0
    "costly"          = 1
    "harmful"         = 2
    "lethal-possible" = 3
}

function ConvertTo-Slug {
    param(
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    # Mirrors StringHelper.Slugify in the decompiled source, which is what
    # ModelDb.GetEntry uses to turn a model class name into its id entry.
    $snake = [regex]::Replace($Text.Trim(), '([A-Za-z0-9]|\G(?!^))([A-Z])', '$1_$2')
    $spaced = [regex]::Replace($snake.ToUpperInvariant(), '\s+', '_')
    return [regex]::Replace($spaced, '[^A-Z0-9_]', '')
}

function ConvertTo-HumanWords {
    param(
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return ""
    }

    $text = $Name -creplace '([a-z0-9])([A-Z])', '$1 $2'
    $text = $text -creplace '([A-Z]+)([A-Z][a-z])', '$1 $2'
    $text = $text -replace '_', ' '
    $text = $text -replace '\bHp\b', 'HP'
    $text = $text -replace '\bAi\b', 'AI'
    return ($text -replace '\s+', ' ').Trim()
}

function Get-PowerDisplayName {
    param(
        [string]$Name
    )

    $trimmed = $Name
    if ($trimmed.EndsWith("Power") -and $trimmed.Length -gt "Power".Length) {
        $trimmed = $trimmed.Substring(0, $trimmed.Length - "Power".Length)
    }

    return (ConvertTo-HumanWords $trimmed)
}

function ConvertTo-FallbackPhrase {
    param(
        [string]$Action,
        [string]$Generic
    )

    $words = ConvertTo-HumanWords $Action
    if (-not [string]::IsNullOrWhiteSpace($Generic)) {
        $words = "$words $(ConvertTo-HumanWords ($Generic -replace '\.', ' '))"
    }

    if ($words.Length -eq 0) {
        return ""
    }

    return $words.Substring(0, 1).ToLowerInvariant() + $words.Substring(1)
}

function Test-CosmeticCall {
    param(
        [string]$Command,
        [string]$Action
    )

    $full = "$Command.$Action"
    foreach ($prefix in $script:CosmeticCallPrefixes) {
        if ($full.StartsWith($prefix)) {
            return $true
        }
    }

    return $false
}

function Split-TopLevelArguments {
    param(
        [string]$Text
    )

    $values = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($Text)) {
        return $values
    }

    $depth = 0
    $inString = $false
    $current = New-Object System.Text.StringBuilder

    foreach ($ch in $Text.ToCharArray()) {
        if ($inString) {
            [void]$current.Append($ch)
            if ($ch -eq '"') {
                $inString = $false
            }
            continue
        }

        if ($ch -eq '"') {
            $inString = $true
            [void]$current.Append($ch)
            continue
        }

        if ($ch -eq '(') {
            $depth++
            [void]$current.Append($ch)
            continue
        }

        if ($ch -eq ')') {
            $depth--
            [void]$current.Append($ch)
            continue
        }

        if ($ch -eq ',' -and $depth -eq 0) {
            $values.Add($current.ToString().Trim())
            [void]$current.Clear()
            continue
        }

        [void]$current.Append($ch)
    }

    $tail = $current.ToString().Trim()
    if ($tail -ne "" -or $values.Count -gt 0) {
        $values.Add($tail)
    }

    # The unary comma keeps a one-argument call from collapsing to a bare string.
    return , $values
}

function Get-CallArgument {
    param(
        $ArgumentList,
        [int]$Index
    )

    if ($null -eq $ArgumentList) {
        return ""
    }

    if ($Index -lt 0 -or $Index -ge $ArgumentList.Count) {
        return ""
    }

    return $ArgumentList[$Index]
}

function Get-CallStatement {
    param(
        [string]$Text,
        [int]$Start
    )

    $open = $Text.IndexOf('(', $Start)
    if ($open -lt 0) {
        return $null
    }

    $depth = 0
    $close = -1
    for ($i = $open; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($ch -eq '(') {
            $depth++
        }
        elseif ($ch -eq ')') {
            $depth--
            if ($depth -eq 0) {
                $close = $i
                break
            }
        }
    }

    if ($close -lt 0) {
        return $null
    }

    # The chain stops at the end of the statement, at the next sibling argument
    # ("new EventOption(...).ThatDoesDamage(x), new EventOption(...)"), or at the
    # enclosing call, so one option never inherits a neighbour's fluent modifier.
    $depth = 0
    $stop = $Text.Length
    for ($i = $close + 1; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($ch -eq '(') {
            $depth++
        }
        elseif ($ch -eq ')') {
            if ($depth -eq 0) {
                $stop = $i
                break
            }

            $depth--
        }
        elseif (($ch -eq ';' -or $ch -eq ',') -and $depth -le 0) {
            $stop = $i
            break
        }
    }

    return [pscustomobject]@{
        Arguments = $Text.Substring($open + 1, $close - $open - 1)
        Chain     = $Text.Substring($close + 1, $stop - $close - 1)
        Start     = $Start
        End       = $stop
    }
}

function Split-TopLevelBinary {
    param(
        [string]$Text,
        [char]$Operator
    )

    $depth = 0
    $inString = $false

    for ($i = $Text.Length - 1; $i -gt 0; $i--) {
        $ch = $Text[$i]
        if ($ch -eq '"') {
            $inString = -not $inString
            continue
        }

        if ($inString) {
            continue
        }

        if ($ch -eq ')') {
            $depth++
            continue
        }

        if ($ch -eq '(') {
            $depth--
            continue
        }

        if ($depth -ne 0 -or $ch -ne $Operator) {
            continue
        }

        $previous = $Text[$i - 1]
        if ($previous -eq '+' -or $previous -eq '-' -or $previous -eq '*' -or $previous -eq '/' -or $previous -eq '=' -or $previous -eq '(') {
            continue
        }

        return @($Text.Substring(0, $i), $Text.Substring($i + 1))
    }

    return $null
}

function Get-DynamicVarValues {
    param(
        [string]$Text
    )

    $values = @{}

    foreach ($match in [regex]::Matches($Text, 'new\s+(?<type>\w+Var)(?:<(?<generic>[\w\.]+)>)?\s*\((?<args>[^\)]*)\)')) {
        $varType = $match.Groups["type"].Value
        $generic = $match.Groups["generic"].Value
        $rawArgs = $match.Groups["args"].Value
        $quoted = Get-RegexValue -Text $rawArgs -Pattern '"(?<value>[^"]*)"'
        $numeric = Get-RegexValue -Text ($rawArgs -replace '"[^"]*"', '""') -Pattern '(?<![\w\.])(?<value>\d+(?:\.\d+)?)m?'

        $name = switch ($varType) {
            "PowerVar" { if ($quoted) { $quoted } else { $generic } }
            "DamageVar" { if ($quoted) { $quoted } else { "Damage" } }
            "BlockVar" { if ($quoted) { $quoted } else { "Block" } }
            "CardsVar" { if ($quoted) { $quoted } else { "Cards" } }
            "EnergyVar" { if ($quoted) { $quoted } else { "Energy" } }
            "SummonVar" { if ($quoted) { $quoted } else { "Summon" } }
            "StarsVar" { if ($quoted) { $quoted } else { "Stars" } }
            "ForgeVar" { if ($quoted) { $quoted } else { "Forge" } }
            "HpLossVar" { if ($quoted) { $quoted } else { "HpLoss" } }
            "GoldVar" { if ($quoted) { $quoted } else { "Gold" } }
            "HealVar" { if ($quoted) { $quoted } else { "Heal" } }
            "MaxHpVar" { if ($quoted) { $quoted } else { "MaxHp" } }
            "OstyDamageVar" { if ($quoted) { $quoted } else { "OstyDamage" } }
            "RepeatVar" { if ($quoted) { $quoted } else { "Repeat" } }
            "ExtraDamageVar" { if ($quoted) { $quoted } else { "ExtraDamage" } }
            "CalculationBaseVar" { "CalculationBase" }
            "CalculationExtraVar" { "CalculationExtra" }
            "CalculatedDamageVar" { if ($quoted) { $quoted } else { "CalculatedDamage" } }
            "CalculatedBlockVar" { if ($quoted) { $quoted } else { "CalculatedBlock" } }
            default { $quoted }
        }

        if ([string]::IsNullOrWhiteSpace($name)) {
            continue
        }

        if (-not $values.ContainsKey($name)) {
            $values[$name] = (Get-IntText -Value $numeric)
        }

        # PowerVar<T> is also read through the short form of the power name
        # (base.DynamicVars.Dexterity for PowerVar<DexterityPower>), and some
        # cards index it by the long form, so both spellings resolve.
        if ($varType -eq "PowerVar" -and -not $quoted -and $generic.EndsWith("Power")) {
            $alias = $generic.Substring(0, $generic.Length - "Power".Length)
            if ($alias -ne "" -and -not $values.ContainsKey($alias)) {
                $values[$alias] = (Get-IntText -Value $numeric)
            }
        }
    }

    return $values
}

function Resolve-AmountText {
    param(
        [string]$Expression,
        $VarMap,
        [string]$Body,
        [int]$Depth = 0
    )

    $text = ""
    if ($null -ne $Expression) {
        $text = $Expression.Trim()
    }

    if ($text -eq "" -or $Depth -gt 4) {
        return "?"
    }

    $text = $text -replace '^\(\s*(?:decimal|int|long|double|float)\s*\)\s*', ''
    if ($text -eq "") {
        return "?"
    }

    if ($text -match 'ResolveEnergyXValue\s*\(\s*\)') {
        return "X (energy spent)"
    }

    if ($text -match 'ResolveStarXValue\s*\(\s*\)') {
        return "X (stars spent)"
    }

    if ($text -match '^-\s*(?<inner>.+)$') {
        $inner = Resolve-AmountText -Expression $Matches["inner"] -VarMap $VarMap -Body $Body -Depth ($Depth + 1)
        if ($inner -ne "?") {
            return "-$inner"
        }

        return "?"
    }

    $literal = [regex]::Match($text, '^(?<value>\d+(?:\.\d+)?)m?$')
    if ($literal.Success) {
        return (Get-IntText -Value $literal.Groups["value"].Value)
    }

    foreach ($operator in @('+', '-')) {
        $operands = Split-TopLevelBinary -Text $text -Operator $operator
        if ($null -ne $operands) {
            $left = Resolve-AmountText -Expression $operands[0] -VarMap $VarMap -Body $Body -Depth ($Depth + 1)
            $right = Resolve-AmountText -Expression $operands[1] -VarMap $VarMap -Body $Body -Depth ($Depth + 1)
            $leftValue = 0.0
            $rightValue = 0.0
            if ([double]::TryParse($left, [ref]$leftValue) -and [double]::TryParse($right, [ref]$rightValue)) {
                $result = if ($operator -eq '+') { $leftValue + $rightValue } else { $leftValue - $rightValue }
                return (Get-IntText -Value ([string]$result))
            }

            return "?"
        }
    }

    foreach ($pattern in @(
        '^(?:base\.)?DynamicVars\.(?<name>\w+)(?:\.(?:BaseValue|IntValue|PreviewValue))?$',
        '^(?:base\.)?DynamicVars\[\s*"(?<name>[^"]+)"\s*\](?:\.(?:BaseValue|IntValue|PreviewValue))?$'
    )) {
        $varMatch = [regex]::Match($text, $pattern)
        if ($varMatch.Success) {
            $name = $varMatch.Groups["name"].Value
            if ($VarMap.ContainsKey($name) -and $VarMap[$name] -ne "") {
                return $VarMap[$name]
            }

            return "?"
        }
    }

    if ($text -match 'Calculate\s*\(') {
        return "?"
    }

    if ($text -match '^(?<name>[a-z]\w*)$') {
        $name = $Matches["name"]
        $assignment = [regex]::Match(
            $Body,
            '(?m)^\s*(?:int|decimal|double|float|long|uint|var)\s+' + [regex]::Escape($name) + '\s*=\s*(?<init>[^;]+);'
        )

        if ($assignment.Success) {
            return (Resolve-AmountText -Expression $assignment.Groups["init"].Value -VarMap $VarMap -Body $Body -Depth ($Depth + 1))
        }

        return "?"
    }

    return "?"
}

function Get-ConditionalRanges {
    param(
        [string]$Body,
        [bool]$Negated
    )

    $ranges = New-Object System.Collections.Generic.List[object]
    $pattern = if ($Negated) {
        'if\s*\(\s*!\s*(?:base\.)?IsUpgraded\s*\)'
    }
    else {
        'if\s*\(\s*(?:base\.)?IsUpgraded\s*\)'
    }

    foreach ($match in [regex]::Matches($Body, $pattern)) {
        $i = $match.Index + $match.Length
        while ($i -lt $Body.Length -and [char]::IsWhiteSpace($Body[$i])) {
            $i++
        }

        if ($i -lt $Body.Length -and $Body[$i] -eq '{') {
            $depth = 0
            $j = $i
            for (; $j -lt $Body.Length; $j++) {
                if ($Body[$j] -eq '{') {
                    $depth++
                }
                elseif ($Body[$j] -eq '}') {
                    $depth--
                    if ($depth -eq 0) {
                        break
                    }
                }
            }

            $ranges.Add([pscustomobject]@{ Start = $i; End = $j })
        }
        else {
            $j = $Body.IndexOf(';', $i)
            if ($j -lt 0) {
                $j = $Body.Length - 1
            }

            $ranges.Add([pscustomobject]@{ Start = $i; End = $j })
        }
    }

    return , $ranges
}

function Test-IndexInRanges {
    param(
        [int]$Index,
        $Ranges
    )

    foreach ($range in $Ranges) {
        if ($Index -ge $range.Start -and $Index -le $range.End) {
            return $true
        }
    }

    return $false
}

function Get-PileDisplayName {
    param(
        [string]$Expression
    )

    $match = [regex]::Match($Expression, 'PileType\.(?<value>\w+)')
    if ($match.Success) {
        $key = $match.Groups["value"].Value
        if ($script:PileDisplayNames.ContainsKey($key)) {
            return $script:PileDisplayNames[$key]
        }

        return (ConvertTo-HumanWords $key).ToLowerInvariant()
    }

    return "pile"
}

function Get-SelectorCount {
    param(
        [string]$Text,
        $VarMap,
        [string]$Body
    )

    $match = [regex]::Match($Text, 'CardSelectorPrefs\s*\((?<args>[^\)]*)\)')
    if (-not $match.Success) {
        return "1"
    }

    $parts = Split-TopLevelArguments -Text $match.Groups["args"].Value
    if ($parts.Count -lt 2) {
        return "1"
    }

    return (Resolve-AmountText -Expression $parts[$parts.Count - 1] -VarMap $VarMap -Body $Body)
}

function Get-CardCountPhrase {
    param(
        [string]$Count,
        [string]$Verb
    )

    if ($Count -eq "1") {
        return "$Verb 1 card"
    }

    return "$Verb $Count cards"
}

function Get-LoopBound {
    param(
        [string]$Body,
        [int]$Index
    )

    if ($Index -le 0) {
        return ""
    }

    $matches = [regex]::Matches($Body.Substring(0, $Index), 'for\s*\([^)]*?<\s*(?<bound>[^;)]+);')
    if ($matches.Count -eq 0) {
        return ""
    }

    return $matches[$matches.Count - 1].Groups["bound"].Value.Trim()
}

function Get-CallPhrase {
    param(
        [string]$Command,
        [string]$Action,
        [string]$Generic,
        $CallStatement,
        $VarMap,
        [string]$Body
    )

    $token = "$Command.$Action"
    $callArgs = Split-TopLevelArguments -Text $CallStatement.Arguments
    $chain = $CallStatement.Chain
    $arg0 = Get-CallArgument -ArgumentList $callArgs -Index 0
    $arg1 = Get-CallArgument -ArgumentList $callArgs -Index 1
    $arg2 = Get-CallArgument -ArgumentList $callArgs -Index 2

    if ($token -eq "DamageCmd.Attack") {
        $damage = Resolve-AmountText -Expression $arg0 -VarMap $VarMap -Body $Body
        $phrase = if ($arg0 -match 'OstyDamage') { "Your Osty deals $damage damage" } else { "Deal $damage damage" }
        $hitExpression = Get-RegexValue -Text $chain -Pattern 'WithHitCount\((?<value>[^\)]*)\)'
        if ($hitExpression) {
            $hits = Resolve-AmountText -Expression $hitExpression -VarMap $VarMap -Body $Body
            if ($hits -ne "1") {
                $phrase = "$phrase $hits times"
            }
        }

        if ($chain -match 'TargetingAllOpponents') {
            $phrase = "$phrase to ALL enemies"
        }
        elseif ($chain -match 'TargetingRandomOpponents') {
            $phrase = "$phrase to a random enemy"
        }

        return $phrase
    }

    if ($token -eq "CreatureCmd.Damage") {
        $amount = Resolve-AmountText -Expression $arg2 -VarMap $VarMap -Body $Body
        if ($arg1 -match 'Owner\.Creature' -or $arg1 -match 'Owner\.Osty') {
            return "Lose $amount HP"
        }

        return "Deal $amount damage to the target"
    }

    if ($token -eq "CreatureCmd.GainBlock") {
        $amount = Resolve-AmountText -Expression $arg1 -VarMap $VarMap -Body $Body
        if ($arg0 -match 'Owner\.Creature') {
            return "Gain $amount Block"
        }

        if ($arg0 -match 'cardPlay\.Target') {
            return "Give the target $amount Block"
        }

        return "Give $amount Block"
    }

    if ($token -eq "PowerCmd.Apply") {
        $powerName = Get-PowerDisplayName -Name $Generic
        $targetIndex = 0
        $amountIndex = 1
        if ([string]::IsNullOrWhiteSpace($Generic)) {
            $targetIndex = 1
            $amountIndex = 2
        }

        $target = Get-CallArgument -ArgumentList $callArgs -Index $targetIndex
        $amount = Resolve-AmountText -Expression (Get-CallArgument -ArgumentList $callArgs -Index $amountIndex) -VarMap $VarMap -Body $Body

        if ([string]::IsNullOrWhiteSpace($powerName)) {
            return "Apply a power to the target"
        }

        if ($target -match 'Owner\.(?:Creature|Osty)' -or $target -eq 'base.Owner' -or $target -match 'Owner\.Player') {
            return "Gain $amount $powerName"
        }

        return "Apply $amount $powerName to the target"
    }

    if ($token -eq "PowerCmd.Remove") {
        $powerName = Get-PowerDisplayName -Name $Generic
        if ([string]::IsNullOrWhiteSpace($powerName)) {
            return "Remove a power from the target"
        }

        return "Remove $powerName from the target"
    }

    if ($token -eq "PowerCmd.ModifyAmount") {
        return "Increase a power already on the target"
    }

    if ($token -eq "CardPileCmd.Draw") {
        $count = "1"
        if ($callArgs.Count -ge 3) {
            $count = Resolve-AmountText -Expression $arg1 -VarMap $VarMap -Body $Body
        }

        return (Get-CardCountPhrase -Count $count -Verb "Draw")
    }

    if ($token -eq "PlayerCmd.GainEnergy") {
        return "Gain $(Resolve-AmountText -Expression $arg0 -VarMap $VarMap -Body $Body) Energy"
    }

    if ($token -eq "PlayerCmd.GainStars") {
        return "Gain $(Resolve-AmountText -Expression $arg0 -VarMap $VarMap -Body $Body) Stars"
    }

    if ($token -eq "PlayerCmd.GainGold") {
        return "Gain $(Resolve-AmountText -Expression $arg0 -VarMap $VarMap -Body $Body) Gold"
    }

    if ($token -eq "PlayerCmd.LoseGold") {
        return "Lose $(Resolve-AmountText -Expression $arg0 -VarMap $VarMap -Body $Body) Gold"
    }

    if ($token -eq "PlayerCmd.EndTurn") {
        return "End your turn"
    }

    if ($token -eq "PlayerCmd.MimicRestSiteHeal") {
        return "Heal as if you had rested"
    }

    if ($token -eq "CreatureCmd.Heal") {
        return "Heal $(Resolve-AmountText -Expression $arg1 -VarMap $VarMap -Body $Body) HP"
    }

    if ($token -eq "CreatureCmd.GainMaxHp") {
        return "Gain $(Resolve-AmountText -Expression $arg1 -VarMap $VarMap -Body $Body) Max HP"
    }

    if ($token -eq "CreatureCmd.LoseMaxHp") {
        $amountExpression = $arg1
        if ($callArgs.Count -ge 3) {
            $amountExpression = $arg2
        }

        return "Lose $(Resolve-AmountText -Expression $amountExpression -VarMap $VarMap -Body $Body) Max HP"
    }

    if ($token -eq "CreatureCmd.Kill") {
        if ($arg0 -match 'Owner\.Osty') {
            return "Kill your own Osty"
        }

        if ($arg0 -match 'Owner\.Creature') {
            return "Kill yourself"
        }

        return "Kill the target"
    }

    if ($token -eq "CreatureCmd.Stun") {
        return "Stun the target"
    }

    if ($token -eq "CreatureCmd.LoseBlock") {
        return "Lose all Block"
    }

    if ($token -eq "OstyCmd.Summon") {
        return "Summon $(Resolve-AmountText -Expression $arg2 -VarMap $VarMap -Body $Body)"
    }

    if ($token -eq "ForgeCmd.Forge") {
        return "Forge $(Resolve-AmountText -Expression $arg0 -VarMap $VarMap -Body $Body)"
    }

    if ($token -eq "OrbCmd.Channel") {
        $orb = ConvertTo-HumanWords ($Generic -replace 'Orb$', '')
        if ([string]::IsNullOrWhiteSpace($orb)) {
            return "Channel an orb"
        }

        return "Channel a $orb orb"
    }

    if ($token -eq "OrbCmd.EvokeNext") {
        return "Evoke your next orb"
    }

    if ($token -eq "OrbCmd.AddSlots") {
        $amountExpression = $arg1
        if ($callArgs.Count -lt 2) {
            $amountExpression = $arg0
        }

        return "Add $(Resolve-AmountText -Expression $amountExpression -VarMap $VarMap -Body $Body) orb slot(s)"
    }

    if ($token -eq "OrbCmd.RemoveSlots") {
        $amountExpression = $arg1
        if ($callArgs.Count -lt 2) {
            $amountExpression = $arg0
        }

        return "Remove $(Resolve-AmountText -Expression $amountExpression -VarMap $VarMap -Body $Body) orb slot(s)"
    }

    if ($token -eq "OrbCmd.Passive") {
        return "Trigger an orb passive"
    }

    if ($token -eq "PotionCmd.TryToProcure") {
        return "Obtain a random potion"
    }

    if ($token -eq "PotionCmd.Discard") {
        return "Discard a potion"
    }

    if ($token -eq "CardCmd.Exhaust") {
        return "Exhaust a card"
    }

    if ($token -eq "CardCmd.Discard") {
        return "Discard a card"
    }

    if ($token -eq "CardCmd.DiscardAndDraw") {
        return "Discard your hand and draw that many cards"
    }

    if ($token -eq "CardCmd.Upgrade") {
        return "Upgrade a card"
    }

    if ($token -eq "CardCmd.Downgrade") {
        return "Downgrade a card"
    }

    if ($token -eq "CardCmd.Transform") {
        return "Transform a card"
    }

    if ($token -eq "CardCmd.TransformTo") {
        if ([string]::IsNullOrWhiteSpace($Generic)) {
            return "Transform a card"
        }

        return "Transform a card into $(ConvertTo-HumanWords $Generic)"
    }

    if ($token -eq "CardCmd.TransformToRandom") {
        return "Transform a card into a random card"
    }

    if ($token -eq "CardCmd.AutoPlay") {
        return "Auto-play a card"
    }

    if ($token -eq "CardCmd.Enchant") {
        if ([string]::IsNullOrWhiteSpace($Generic)) {
            return "Enchant a card"
        }

        return "Enchant a card with $(ConvertTo-HumanWords $Generic)"
    }

    if ($token -eq "CardCmd.ApplyKeyword") {
        $keyword = Get-RegexValue -Text $CallStatement.Arguments -Pattern 'CardKeyword\.(?<value>\w+)'
        if ([string]::IsNullOrWhiteSpace($keyword)) {
            return "Give a card a keyword"
        }

        return "Give a card $(ConvertTo-HumanWords $keyword)"
    }

    if ($token -eq "CardPileCmd.Add") {
        return "Put a card into your $(Get-PileDisplayName -Expression $arg1)"
    }

    if ($token -eq "CardPileCmd.AddGeneratedCardToCombat" -or $token -eq "CardPileCmd.AddGeneratedCardsToCombat") {
        return "Add a generated card to your $(Get-PileDisplayName -Expression $arg1)"
    }

    if ($token -eq "CardPileCmd.AddCurseToDeck") {
        if ([string]::IsNullOrWhiteSpace($Generic)) {
            return "Add a curse to your deck"
        }

        return "Add the curse $(ConvertTo-HumanWords $Generic) to your deck"
    }

    if ($token -eq "CardPileCmd.AddCursesToDeck") {
        return "Add curses to your deck"
    }

    if ($token -eq "CardPileCmd.RemoveFromDeck") {
        return "Remove a card from your deck"
    }

    if ($token -eq "CardPileCmd.Shuffle" -or $token -eq "CardPileCmd.ShuffleIfNecessary") {
        return "Shuffle your draw pile"
    }

    if ($token -eq "CardPileCmd.AutoPlayFromDrawPile") {
        return "Auto-play a card from your draw pile"
    }

    if ($token -eq "CardSelectCmd.FromHandForDiscard") {
        $count = Get-SelectorCount -Text $CallStatement.Arguments -VarMap $VarMap -Body $Body
        if ($count -eq "1") {
            return "Discard 1 card from your hand"
        }

        return "Discard $count cards from your hand"
    }

    if ($token -eq "CardSelectCmd.FromHandForUpgrade") {
        return "Upgrade a card in your hand"
    }

    if ($token -eq "CardSelectCmd.FromDeckForRemoval") {
        return "Remove a card from your deck"
    }

    if ($token -eq "CardSelectCmd.FromDeckForUpgrade") {
        return "Upgrade a card in your deck"
    }

    if ($token -eq "CardSelectCmd.FromDeckForTransformation") {
        return "Transform a card in your deck"
    }

    if ($token -eq "CardSelectCmd.FromDeckForEnchantment") {
        return "Enchant a card in your deck"
    }

    if ($token -eq "CardSelectCmd.FromSimpleGridForRewards" -or $token -eq "CardSelectCmd.FromChooseACardScreen") {
        return "Choose a card from an offered set"
    }

    if ($token -eq "CardSelectCmd.FromSimpleGrid") {
        return "Choose a card from a pile"
    }

    if ($token -eq "CardSelectCmd.FromHand") {
        return "Choose a card in your hand"
    }

    if ($token -eq "CardSelectCmd.FromDeckGeneric") {
        return "Choose a card in your deck"
    }

    if ($Action -eq "CreateInHand" -or $Action -eq "CreateInDrawPile" -or $Action -eq "CreateInDiscard") {
        $cardName = ConvertTo-HumanWords $Command
        $pileName = "hand"
        if ($Action -eq "CreateInDrawPile") {
            $pileName = "draw pile"
        }
        elseif ($Action -eq "CreateInDiscard") {
            $pileName = "discard pile"
        }

        $count = "1"
        if ($callArgs.Count -ge 2 -and $arg1 -notmatch 'CombatState') {
            $count = Resolve-AmountText -Expression $arg1 -VarMap $VarMap -Body $Body
        }
        else {
            $loopBound = Get-LoopBound -Body $Body -Index $CallStatement.Start
            if ($loopBound) {
                $count = Resolve-AmountText -Expression $loopBound -VarMap $VarMap -Body $Body
            }
        }

        if ($count -eq "1") {
            return "Add 1 $cardName to your $pileName"
        }

        return "Add $count ${cardName}s to your $pileName"
    }

    if ($token -eq "RelicCmd.Obtain") {
        $relicName = $Generic
        if ([string]::IsNullOrWhiteSpace($relicName)) {
            $relicName = Get-RegexValue -Text $CallStatement.Arguments -Pattern 'ModelDb\.Relic<(?<value>\w+)>'
        }

        if ([string]::IsNullOrWhiteSpace($relicName)) {
            return "Obtain a relic"
        }

        return "Obtain the relic $(ConvertTo-HumanWords $relicName)"
    }

    if ($token -eq "RelicCmd.Remove") {
        return "Lose a relic"
    }

    if ($token -eq "RewardsCmd.OfferCustom") {
        return "Offer extra rewards"
    }

    if ($token -eq "CardFactory.CreateForReward") {
        return "Offer a card reward"
    }

    if ($token -eq "RelicFactory.PullNextRelicFromFront") {
        return "Obtain a random relic"
    }

    if ($token -eq "CardCmd.Preview") {
        return ""
    }

    $fallback = ConvertTo-FallbackPhrase -Action $Action -Generic $Generic
    if ([string]::IsNullOrWhiteSpace($fallback)) {
        return ""
    }

    return $fallback
}

function Get-EffectClauses {
    param(
        [string]$Body,
        $VarMap
    )

    $clauses = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($Body)) {
        return , $clauses
    }

    $upgradeRanges = Get-ConditionalRanges -Body $Body -Negated $false
    $notUpgradeRanges = Get-ConditionalRanges -Body $Body -Negated $true
    $covered = New-Object System.Collections.Generic.List[object]

    # Commands come from the *Cmd/*Factory helper classes; the card classes have
    # their own static CreateInHand/CreateInDrawPile helpers, which are also
    # gameplay effects and are matched here by name.
    $callPattern = '([A-Z]\w+)\.(?<action>CreateInHand|CreateInDrawPile|CreateInDiscard)\s*\(|(?<cmd>\w+Cmd)\.(?<action>\w+)(?:<(?<generic>[\w\.]+)>)?\s*\(|(?<cmd>\w+Factory)\.(?<action>\w+)(?:<(?<generic>[\w\.]+)>)?\s*\('

    foreach ($match in [regex]::Matches($Body, $callPattern)) {
        $command = $match.Groups["cmd"].Value
        $action = $match.Groups["action"].Value
        $generic = $match.Groups["generic"].Value
        if ([string]::IsNullOrWhiteSpace($command)) {
            $command = $match.Value.Substring(0, $match.Value.IndexOf('.'))
        }

        if (Test-CosmeticCall -Command $command -Action $action) {
            continue
        }

        $statement = Get-CallStatement -Text $Body -Start $match.Index
        if ($null -eq $statement) {
            continue
        }

        # A call nested inside another summarised call (an argument expression)
        # is part of that call's clause, not a clause of its own.
        $nested = $false
        foreach ($span in $covered) {
            if ($statement.Start -ge $span.Start -and $statement.End -le $span.End) {
                $nested = $true
                break
            }
        }

        if ($nested) {
            continue
        }

        $covered.Add([pscustomobject]@{ Start = $statement.Start; End = $statement.End })

        $phrase = Get-CallPhrase -Command $command -Action $action -Generic $generic -CallStatement $statement -VarMap $VarMap -Body $Body
        if ([string]::IsNullOrWhiteSpace($phrase)) {
            continue
        }

        $prefix = ""
        if (Test-IndexInRanges -Index $match.Index -Ranges $upgradeRanges) {
            $prefix = "if upgraded: "
        }
        elseif (Test-IndexInRanges -Index $match.Index -Ranges $notUpgradeRanges) {
            $prefix = "if not upgraded: "
        }

        $clause = "$prefix$phrase"
        if (-not $clauses.Contains($clause)) {
            $clauses.Add($clause)
        }
    }

    return , $clauses
}

function Get-KeywordPhrase {
    param(
        [string]$Text
    )

    $body = Get-RegexValue -Text $Text -Pattern 'CanonicalKeywords\s*=>\s*(?<value>.*?);'
    $keywords = New-Object System.Collections.Generic.List[string]

    foreach ($match in [regex]::Matches($body, 'CardKeyword\.(?<value>\w+)')) {
        $word = ConvertTo-HumanWords $match.Groups["value"].Value
        if (-not $keywords.Contains($word)) {
            $keywords.Add($word)
        }
    }

    if ($keywords.Count -eq 0) {
        return ""
    }

    return "keywords: " + ($keywords -join ", ")
}

function Get-CardEffectSummary {
    param(
        [string]$Text
    )

    $varMap = Get-DynamicVarValues -Text $Text
    $body = Get-MethodBody -Text $Text -MethodName "OnPlay"
    $clauses = New-Object System.Collections.Generic.List[string]

    if ([string]::IsNullOrWhiteSpace($body)) {
        $clauses.Add("no OnPlay effect in source")
    }
    else {
        foreach ($clause in (Get-EffectClauses -Body $body -VarMap $varMap)) {
            $clauses.Add($clause)
        }

        if ($clauses.Count -eq 0) {
            $clauses.Add("no gameplay effect detected in OnPlay")
        }
    }

    $keywordPhrase = Get-KeywordPhrase -Text $Text
    if ($keywordPhrase) {
        $clauses.Add($keywordPhrase)
    }

    return ($clauses -join "; ")
}

function Get-CardOwnershipMap {
    $poolDir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.CardPools"
    $characterDir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Characters"

    # character name -> declared card pool, and the energy color it declares in
    $characters = New-Object System.Collections.Generic.List[object]
    foreach ($file in Get-ChildItem -Path $characterDir -File | Sort-Object Name) {
        $text = Get-SourceText -Path $file.FullName
        $characters.Add([pscustomobject]@{
            Name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            Pool = Get-RegexValue -Text $text -Pattern 'CardPool\s*=>[^;]*?ModelDb\.CardPool<(?<value>\w+)>'
            Energy = Get-RegexValue -Text $text -Pattern 'energyColorName\s*=\s*"(?<value>[^"]+)"'
        })
    }

    # card name -> owning pool, in pool-name order so the result is stable
    $poolByCard = @{}
    foreach ($file in Get-ChildItem -Path $poolDir -File | Sort-Object Name) {
        $text = Get-SourceText -Path $file.FullName
        $poolName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        foreach ($cardName in (Get-RegexMatches -Text $text -Pattern 'ModelDb\.Card<(?<value>\w+)>\(\)')) {
            if (-not $poolByCard.ContainsKey($cardName)) {
                $poolByCard[$cardName] = $poolName
            }
        }
    }

    # pool name -> owner. A pool is character-owned when a character declares that
    # pool as its card pool; the energy-color constant decides which of several
    # claimants is the real owner (the random-character shell claims the
    # ironclad pool but declares no energy color of its own).
    $ownerByPool = @{}
    foreach ($file in Get-ChildItem -Path $poolDir -File | Sort-Object Name) {
        $text = Get-SourceText -Path $file.FullName
        $poolName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $energy = Get-RegexValue -Text $text -Pattern 'EnergyColorName\s*=>\s*"(?<value>[^"]+)"'
        $poolTitle = Get-RegexValue -Text $text -Pattern 'Title\s*=>\s*"(?<value>[^"]+)"'

        $claimants = @($characters | Where-Object { $_.Pool -eq $poolName } | Sort-Object Name)
        $energyOwners = @($claimants | Where-Object { $_.Energy -ne "" -and $_.Energy -eq $energy })

        if ($energyOwners.Count -gt 0) {
            $ownerByPool[$poolName] = ($energyOwners | ForEach-Object { $_.Name }) -join "/"
        }
        elseif ($claimants.Count -eq 1) {
            $ownerByPool[$poolName] = $claimants[0].Name
        }
        elseif ($poolTitle) {
            $ownerByPool[$poolName] = $poolTitle
        }
        else {
            $ownerByPool[$poolName] = "unknown"
        }
    }

    $owners = @{}
    foreach ($cardName in $poolByCard.Keys) {
        $owners[$cardName] = $ownerByPool[$poolByCard[$cardName]]
    }

    return $owners
}

function Get-MemberBody {
    param(
        [string]$Text,
        [string]$MemberName
    )

    foreach ($match in [regex]::Matches($Text, '(?<![A-Za-z0-9_])' + [regex]::Escape($MemberName) + '\s*\(')) {
        $depth = 0
        $i = $match.Index
        $closed = -1
        for (; $i -lt $Text.Length; $i++) {
            if ($Text[$i] -eq '(') {
                $depth++
            }
            elseif ($Text[$i] -eq ')') {
                $depth--
                if ($depth -eq 0) {
                    $closed = $i
                    break
                }
            }
        }

        if ($closed -lt 0) {
            continue
        }

        $j = $closed + 1
        while ($j -lt $Text.Length -and [char]::IsWhiteSpace($Text[$j])) {
            $j++
        }

        if ($j -lt $Text.Length -and $Text[$j] -eq '{') {
            $depth = 0
            $end = -1
            for ($k = $j; $k -lt $Text.Length; $k++) {
                if ($Text[$k] -eq '{') {
                    $depth++
                }
                elseif ($Text[$k] -eq '}') {
                    $depth--
                    if ($depth -eq 0) {
                        $end = $k
                        break
                    }
                }
            }

            if ($end -gt $j) {
                return $Text.Substring($j + 1, $end - $j - 1)
            }

            continue
        }

        # expression-bodied member: "=> someCall();"
        if ($j + 1 -lt $Text.Length -and $Text[$j] -eq '=' -and $Text[$j + 1] -eq '>') {
            $end = $Text.IndexOf(';', $j)
            if ($end -gt $j) {
                return $Text.Substring($j + 2, $end - $j - 2)
            }
        }
    }

    return ""
}

function Add-InlinedHelperBodies {
    param(
        [string]$Body,
        [string]$FileText,
        [int]$Depth = 0
    )

    if ([string]::IsNullOrWhiteSpace($Body) -or $Depth -gt 1) {
        return $Body
    }

    $result = $Body
    $known = @(
        "ResolveEnergyXValue", "ResolveStarXValue", "CalculateVars", "WillKillPlayer",
        "AssertMutable", "L10NLookup", "SetEventState", "SetEventFinished", "Log"
    )

    foreach ($match in [regex]::Matches($Body, '(?<![A-Za-z0-9_\.])(?<name>\w+)\s*\(\s*\)')) {
        $name = $match.Groups["name"].Value
        if ($known -contains $name) {
            continue
        }

        $helperBody = Get-MemberBody -Text $FileText -MemberName $name
        if ([string]::IsNullOrWhiteSpace($helperBody)) {
            continue
        }

        $result = "$result`n$helperBody"
    }

    return $result
}

function Get-MethodDisplayName {
    param(
        [string]$Argument
    )

    $text = $Argument.Trim()
    if ($text -eq "" -or $text -eq "null") {
        return "none"
    }

    if ($text -match '^[A-Za-z_]\w*$') {
        return $text
    }

    if ($text -match '=>') {
        return "(inline)"
    }

    return "(inline)"
}

function Get-EventOptionKey {
    param(
        [string]$Argument,
        [string]$EventName,
        [string]$PageName = "INITIAL"
    )

    $text = $Argument.Trim()
    if ($text -eq "") {
        return "unknown"
    }

    if ($text -match '^"(?<value>[^"]*)"$') {
        return (Shorten-OptionKey -Key $Matches["value"] -EventName $EventName)
    }

    if ($text -match '^\$"(?<value>[^"]*)"$') {
        $key = $Matches["value"] -replace '\{[^}]*\}', '*'
        return (Shorten-OptionKey -Key $key -EventName $EventName)
    }

    if ($text -match 'InitialOptionKey\s*\(\s*"(?<value>[^"]*)"\s*\)') {
        return "INITIAL.options.$($Matches["value"])"
    }

    if ($text -match 'OptionKey\s*\(') {
        return "$PageName.options.relic-option"
    }

    return "unknown"
}

function Shorten-OptionKey {
    param(
        [string]$Key,
        [string]$EventName
    )

    $slug = ConvertTo-Slug -Text $EventName
    $prefix = "$slug.pages."
    if ($Key.StartsWith($prefix)) {
        return $Key.Substring($prefix.Length)
    }

    return $Key
}

function Get-EventOptionRows {
    param(
        [string]$EventName,
        [string]$Text
    )

    $rows = New-Object System.Collections.Generic.List[object]
    $varMap = Get-DynamicVarValues -Text $Text
    $seen = New-Object System.Collections.Generic.List[string]

    $addRow = {
        param($OptionKey, $Handler, $Effect, $Cost, $Risk, $Continuation)

        $identity = "$OptionKey|$Handler"
        if ($seen.Contains($identity)) {
            return
        }

        $seen.Add($identity)
        $rows.Add([pscustomobject]@{
            Option       = $OptionKey
            Handler      = $Handler
            Effect       = $Effect
            Cost         = $Cost
            Risk         = $Risk
            Continuation = $Continuation
        })
    }

    foreach ($match in [regex]::Matches($Text, 'new\s+EventOption\s*\(')) {
        $statement = Get-CallStatement -Text $Text -Start $match.Index
        if ($null -eq $statement) {
            continue
        }

        $callArgs = Split-TopLevelArguments -Text $statement.Arguments
        $handlerArgument = (Get-CallArgument -ArgumentList $callArgs -Index 1).Trim()
        $keyArgument = Get-CallArgument -ArgumentList $callArgs -Index 2
        $optionKey = Get-EventOptionKey -Argument $keyArgument -EventName $EventName

        $disableOnChosen = "true"
        $isProceed = "false"
        foreach ($extra in ($callArgs | Select-Object -Skip 3)) {
            if ($extra -match 'disableOnChosen\s*:\s*(?<value>\w+)') {
                $disableOnChosen = $Matches["value"].ToLowerInvariant()
            }
            if ($extra -match 'isProceed\s*:\s*(?<value>\w+)') {
                $isProceed = $Matches["value"].ToLowerInvariant()
            }
        }

        if ($handlerArgument -eq "null") {
            & $addRow $optionKey "none" "option is locked (no handler)" "n/a" "locked" "cannot be chosen"
            continue
        }

        $handlerName = Get-MethodDisplayName -Argument $handlerArgument
        $body = Get-HandlerBody -Text $Text -HandlerArgument $handlerArgument
        $body = Add-InlinedHelperBodies -Body $body -FileText $Text
        $body = "$body`n$($statement.Chain)"

        $evidence = Get-EventOptionEvidence -Body $body -VarMap $varMap -Chain $statement.Chain
        $continuation = Get-EventContinuation -Body $body -OptionKey $optionKey -IsProceed $isProceed -DisableOnChosen $disableOnChosen

        & $addRow $optionKey $handlerName $evidence.Effect $evidence.Cost $evidence.Risk $continuation
    }

    # Ancient events build their relic options through RelicOption<T>, whose
    # handler is defined in AncientEventModel (obtain the relic, then finish).
    foreach ($match in [regex]::Matches($Text, 'RelicOption\s*(?:<(?<generic>\w+)>)?\s*\(')) {
        $generic = $match.Groups["generic"].Value
        if ([string]::IsNullOrWhiteSpace($generic)) {
            continue
        }

        $statement = Get-CallStatement -Text $Text -Start $match.Index
        $callArgs = Split-TopLevelArguments -Text $statement.Arguments
        $page = Get-CallArgument -ArgumentList $callArgs -Index 0
        if ($page -match '^"(?<value>[^"]*)"$') {
            $page = $Matches["value"]
        }
        else {
            $page = "INITIAL"
        }

        $optionKey = "$page.options.$(ConvertTo-Slug -Text $generic)"
        & $addRow `
            $optionKey `
            "RelicOption<$generic>" `
            "Obtain the relic $(ConvertTo-HumanWords $generic); the source finishes the event after it" `
            "none detected" `
            "none-detected" `
            "ends event"
    }

    return , $rows
}

function Get-HandlerBody {
    param(
        [string]$Text,
        [string]$HandlerArgument
    )

    if ($HandlerArgument -match '^[A-Za-z_]\w*$') {
        return (Get-MemberBody -Text $Text -MemberName $HandlerArgument)
    }

    if ($HandlerArgument -match '=>\s*(?<call>[A-Za-z_]\w*)\s*\(') {
        return (Get-MemberBody -Text $Text -MemberName $Matches["call"])
    }

    if ($HandlerArgument -match '=>\s*\{(?<body>.*)\}\s*$') {
        return $Matches["body"]
    }

    return $HandlerArgument
}

function Get-EventOptionEvidence {
    param(
        [string]$Body,
        $VarMap,
        [string]$Chain
    )

    $costs = New-Object System.Collections.Generic.List[string]
    $grants = New-Object System.Collections.Generic.List[string]
    $harmful = $false
    $costly = $false

    foreach ($clause in (Get-EffectClauses -Body $Body -VarMap $VarMap)) {
        if ($clause -match '^Lose .*HP$') {
            $costs.Add($clause)
            $harmful = $true
            continue
        }

        if ($clause -match '^Lose .*Max HP$') {
            $costs.Add($clause)
            $harmful = $true
            continue
        }

        if ($clause -match '^Lose .*Gold$') {
            $costs.Add($clause)
            $costly = $true
            continue
        }

        if ($clause -match 'Add the curse' -or $clause -match 'Add curses') {
            $costs.Add($clause)
            $harmful = $true
            continue
        }

        if ($clause -match '^Remove a card from your deck$' -or $clause -match '^Downgrade a card$') {
            $costs.Add($clause)
            $costly = $true
            continue
        }

        if ($clause -match '^Lose a relic$' -or $clause -match '^Discard a potion$') {
            $costs.Add($clause)
            $harmful = $true
            continue
        }

        $grants.Add($clause)
    }

    # The damage an option can deal to the player is declared on the option itself.
    foreach ($damageMatch in [regex]::Matches($Chain, 'ThatDoesDamage\((?<value>[^\)]*)\)')) {
        $amount = Resolve-AmountText -Expression $damageMatch.Groups["value"].Value -VarMap $VarMap -Body $Body
        $entry = "event is marked lethal at $amount current HP"
        if (-not $costs.Contains($entry)) {
            $costs.Add($entry)
        }
    }

    $risk = "none-detected"
    $lethalMarked = $Chain -match 'ThatDoesDamage\(' -or $Chain -match 'ThatWillKillPlayerIf\('
    if ($lethalMarked) {
        $risk = "lethal-possible"
    }
    elseif ($harmful) {
        $risk = "harmful"
    }
    elseif ($costly) {
        $risk = "costly"
    }

    $effectText = if ($grants.Count -gt 0) { $grants -join "; " } else { "none detected" }
    $costText = if ($costs.Count -gt 0) { $costs -join "; " } else { "none detected" }

    return [pscustomobject]@{
        Effect = $effectText
        Cost   = $costText
        Risk   = $risk
    }
}

function Get-EventContinuation {
    param(
        [string]$Body,
        [string]$OptionKey,
        [string]$IsProceed,
        [string]$DisableOnChosen
    )

    if ($IsProceed -eq "true") {
        return "proceed (ends event)"
    }

    $lastSegment = $OptionKey.Split('.')[-1]
    $repeats = $false
    if ($lastSegment -ne "unknown" -and $lastSegment -notmatch '\*') {
        $repeats = $Body.Contains(".options.$lastSegment")
    }

    if ($repeats) {
        if ($DisableOnChosen -eq "false") {
            return "repeats this option (repeatable)"
        }

        return "repeats this option (pages)"
    }

    if ($Body -match 'SetEventFinished\s*\(') {
        return "ends event"
    }

    if ($Body -match 'SetEventState\s*\(') {
        return "next page"
    }

    return "unknown (handler not resolvable)"
}

function Get-EventEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Events"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $indexRows = New-Object System.Collections.Generic.List[object]
    $optionRows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $baseType = Get-RegexValue -Text $text -Pattern 'public\s+(?:sealed\s+)?class\s+\w+\s*:\s*(?<value>\w+)'

        $layout = "Default"
        if ($text -match 'LayoutType\s*=>\s*EventLayoutType\.(?<value>\w+)') {
            $layout = $Matches["value"]
        }
        elseif ($baseType -eq "AncientEventModel") {
            $layout = "Ancient"
        }

        $encounter = Get-RegexValue -Text $text -Pattern 'CanonicalEncounter\s*=>\s*ModelDb\.Encounter<(?<value>\w+)>'
        if ([string]::IsNullOrWhiteSpace($encounter)) {
            $encounter = "-"
        }

        $options = Get-EventOptionRows -EventName $name -Text $text
        foreach ($option in $options) {
            $optionRows.Add([pscustomobject]@{
                Event        = $name
                BaseType     = $baseType
                Option       = $option.Option
                Handler      = $option.Handler
                Effect       = $option.Effect
                Cost         = $option.Cost
                Risk         = $option.Risk
                Continuation = $option.Continuation
            })
        }

        $highestRisk = "none-detected"
        $unknownCount = 0
        foreach ($option in $options) {
            if ($option.Risk -eq "unknown") {
                $unknownCount++
                continue
            }

            if ($option.Risk -eq "locked") {
                continue
            }

            if ($script:RiskOrder[$option.Risk] -gt $script:RiskOrder[$highestRisk]) {
                $highestRisk = $option.Risk
            }
        }

        if ($options.Count -eq 0) {
            $highestRisk = "unknown"
        }
        elseif ($unknownCount -gt 0 -and $highestRisk -eq "none-detected") {
            $highestRisk = "unknown"
        }

        if ($unknownCount -gt 0) {
            $highestRisk = "$highestRisk ($unknownCount unknown)"
        }

        $indexRows.Add([pscustomobject]@{
            Name        = $name
            BaseType    = $baseType
            Layout      = $layout
            Encounter   = $encounter
            Options     = $options.Count
            HighestRisk = $highestRisk
        })
    }

    return [pscustomobject]@{
        Index   = $indexRows
        Options = $optionRows
    }
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
    $owners = Get-CardOwnershipMap

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $ctor = [regex]::Match(
            $text,
            ':\s*base\((?<cost>[^,]+),\s*CardType\.(?<type>\w+),\s*CardRarity\.(?<rarity>\w+),\s*TargetType\.(?<target>\w+)\)',
            [System.Text.RegularExpressions.RegexOptions]::Singleline
        )

        $owner = "unknown"
        if ($owners.ContainsKey($name)) {
            $owner = $owners[$name]
        }

        $rows.Add([pscustomobject]@{
            Name = $name
            Cost = if ($ctor.Success) { $ctor.Groups["cost"].Value.Trim() } else { "" }
            Type = if ($ctor.Success) { $ctor.Groups["type"].Value.Trim() } else { "" }
            Rarity = if ($ctor.Success) { $ctor.Groups["rarity"].Value.Trim() } else { "" }
            Target = if ($ctor.Success) { $ctor.Groups["target"].Value.Trim() } else { "" }
            Owner = $owner
            Effect = Get-CardEffectSummary -Text $text
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
- Events: $($events.Index.Count)
- Event options with a risk tag: $($events.Options.Count)

## Usage

- Prefer these indexes when MCP returns `card_id`, `enemy_id`, `event_id`, or `potion_id`.
- Read `docs/game-knowledge/agent-reference.md` first, then inspect the specific index file.
- Use `card-behaviors.md`, `monster-behaviors.md`, and `potion-behaviors.md` when metadata alone is too thin for action choice.
- Refresh this knowledge base after game updates by running `powershell -ExecutionPolicy Bypass -File "scripts/generate-sts2-knowledge.ps1"`.
- How these indexes are put together, and what belongs in each of them: [knowledge-plan.md](./knowledge-plan.md).
- Where a risk or effect tag says "none detected" or "?", that is an absence of evidence rather than a guarantee; live state is still the authority.
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

$cardsBody = ConvertTo-MarkdownTable -Headers @("Name", "Cost", "Type", "Rarity", "Target", "Owner", "Effect") -Rows $cards
Set-Content -Path (Join-Path $outputRoot "cards.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Card Index" `
    -Description "Base metadata plus character ownership and a one-line readable effect for each card internal name. Use this when MCP returns unfamiliar ``card_id`` values. `Owner` is the character whose card pool declares the card; cards in a pool no character owns (colorless, curse, status, token, event, quest) show that pool's title instead. Numbers inside `Effect` are the base (unupgraded) values taken from the card's dynamic vars; `?` means the amount is only computed at play time and is deliberately left unresolved rather than guessed." `
    -Body $cardsBody)

$cardBehaviorBody = ConvertTo-MarkdownTable -Headers @("Name", "Vars", "OnPlay", "OnUpgrade", "Owner", "Effect") -Rows $cards
Set-Content -Path (Join-Path $outputRoot "card-behaviors.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Card Behavior Index" `
    -Description "Behavior-oriented summaries extracted from card source. `Vars`, `OnPlay`, and `OnUpgrade` stay close to the code for tool-friendly lookup; `Effect` is the same readable summary used by cards.md, with `?` marking an amount that is only computed at play time." `
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

$eventsIndexBody = ConvertTo-MarkdownTable -Headers @("Name", "BaseType", "Layout", "Encounter", "Options", "HighestRisk") -Rows $events.Index
$eventsOptionBody = ConvertTo-MarkdownTable -Headers @("Event", "Option", "Handler", "Effect", "Cost", "Risk", "Continuation") -Rows $events.Options
$eventsBody = @"
## Event Index

``Layout`` is the event layout the source declares (``Combat`` events start a fight; ``Ancient`` is the
AncientEventModel default). ``Options`` counts the distinct options the source builds for the event,
and ``HighestRisk`` is the strongest risk tag among them.

$eventsIndexBody

## Option Risk Details

Every distinct option the event builds, in source order. ``Option`` is the option's localization key
without the ``<EVENT>.pages.`` prefix; options an Ancient creates through ``RelicOption<T>`` are
labelled after that helper instead of a literal key.

``Risk`` is graded from the handler body and the option's own markers:

- ``lethal-possible`` - the source marks the option with ``ThatDoesDamage``/``ThatWillKillPlayerIf``,
  so the game itself can treat the choice as fatal.
- ``harmful`` - the handler damages the player, burns Max HP, adds a curse, or takes a relic/potion.
- ``costly`` - the handler only spends Gold or removes/downgrades a card.
- ``none-detected`` - no harmful command was found in the handler body. This is an absence of
  evidence, not a guarantee.
- ``locked`` - the option has no handler and cannot be chosen.
- ``unknown`` - the handler could not be read from the source.

``Cost`` and ``Effect`` only list what the handler body shows; ``none detected`` means nothing of
that kind was found, ``?`` means the amount is computed at runtime. ``Continuation`` records whether
choosing the option ends the event, leads to another page, or offers the same option again.

$eventsOptionBody
"@

Set-Content -Path (Join-Path $outputRoot "events.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Event Index" `
    -Description "Event lookup by internal name and base type, plus per-option consequence and risk grading derived from the event sources." `
    -Body $eventsBody)

Write-Host "[generate-sts2-knowledge] Generated knowledge base in $outputRoot"
