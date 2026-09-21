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

    # A model's own stack count. "Amount" on a power is how many stacks it has and "amount" in
    # BeforeApplied/AfterPowerAmountChanged is the amount being applied to it; both are runtime
    # quantities, but naming the quantity the source names beats printing "?" for it. The match
    # is case-sensitive on purpose: a card body's local "decimal amount = ..." is a different
    # thing and must fall through to the local-assignment lookup below.
    if ($text -cmatch '^(?:base\.)?Amount$') {
        return "Amount"
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
        if ($arg0 -eq "this") {
            return "Remove this power"
        }

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

###############################################################################
# Model class helpers.
#
# Relics and powers have no single entry point the way a card has OnPlay: their
# behaviour lives in whichever combat hooks the class overrides. The hook names
# are read out of the model base classes themselves rather than hard-coded, so a
# hook added to a base class shows up here without anyone editing this script.
###############################################################################

function Get-ModelHookNames {
    $paths = @(
        (Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models/AbstractModel.cs"),
        (Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models/RelicModel.cs"),
        (Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models/PowerModel.cs")
    )

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            continue
        }

        foreach ($match in [regex]::Matches((Get-SourceText -Path $path), 'public\s+virtual\s+(?<ret>[^\n(]+?)\s+(?<name>\w+)\s*\(')) {
            # A property whose initialiser happens to contain a call ("=> Array.Empty<T>()")
            # is not a hook; it is a property that the loose return-type group walked into.
            if ($match.Groups["ret"].Value -match '=>') {
                continue
            }

            $name = $match.Groups["name"].Value
            if (-not $names.Contains($name)) {
                $names.Add($name)
            }
        }
    }

    return $names
}

$script:ModelHookNames = Get-ModelHookNames

function Get-OverriddenHooks {
    param(
        [string]$Text
    )

    $hooks = New-Object System.Collections.Generic.List[string]
    foreach ($match in [regex]::Matches($Text, 'public\s+override\s+(?:async\s+)?(?<ret>[^\n(]+?)\s+(?<name>\w+)\s*\(')) {
        if ($match.Groups["ret"].Value -match '=>') {
            continue
        }

        $name = $match.Groups["name"].Value
        if ($script:ModelHookNames -contains $name -and -not $hooks.Contains($name)) {
            $hooks.Add($name)
        }
    }

    return $hooks
}

function Get-ClassFileTexts {
    param(
        [string]$Directory,
        [string]$Name,
        [int]$Limit = 8
    )

    # The leaf class first, then each base class that has its own file; a base class with no
    # file of its own (MonsterModel, PowerModel) ends the walk, which is also where C#'s
    # inherited members stop being a model's own behaviour.
    $texts = New-Object System.Collections.Generic.List[object]
    $current = $Name

    for ($i = 0; $i -lt $Limit; $i++) {
        if ([string]::IsNullOrWhiteSpace($current)) {
            break
        }

        $path = Join-Path $Directory "$current.cs"
        if (-not (Test-Path $path)) {
            break
        }

        $text = Get-SourceText -Path $path
        $texts.Add([pscustomobject]@{ Name = $current; Text = $text })

        $base = Get-RegexValue -Text $text -Pattern 'public\s+(?:sealed\s+|abstract\s+)?class\s+\w+\s*:\s*(?<value>\w+)'
        if ($base -eq "" -or $base -eq $current) {
            break
        }

        $current = $base
    }

    return , $texts
}

$script:NoOpReturnExpressions = @(
    "Task.CompletedTask",
    "null"
)

function Get-DynamicVarExpressionText {
    param(
        [string]$Text,
        $VarMap
    )

    # "base.DynamicVars.Cards.BaseValue" and "base.DynamicVars["MaxHpLoss"].IntValue" name the
    # same number the Vars column already shows; spelling it out keeps a returned expression
    # readable instead of leaving the reader to resolve a dynamic var by hand.
    return [regex]::Replace(
        $Text,
        '(?:base\.)?DynamicVars(?:\.(?<member>\w+)|\[\s*"(?<index>[^"]+)"\s*\])(?:\.(?:BaseValue|IntValue|PreviewValue))?',
        {
            param($match)
            $name = if ($match.Groups["member"].Value) { $match.Groups["member"].Value } else { $match.Groups["index"].Value }
            if ($VarMap.ContainsKey($name) -and $VarMap[$name] -ne "") {
                return $VarMap[$name]
            }

            return $match.Value
        }
    )
}

function Get-ValueExpressionText {
    param(
        [string]$Expression,
        $VarMap,
        [string]$Body
    )

    $text = Get-DynamicVarExpressionText -Text $Expression.Trim() -VarMap $VarMap
    # A cast on a sub-expression ("amount + (decimal)1") and a decimal suffix ("100m") are
    # both noise in a rendered value; the leading-cast form is handled inside Resolve-AmountText.
    $text = $text -replace '\((?:decimal|int|long|double|float)\)\s*', ''
    $text = $text -replace '(?<=\d)m\b', ''
    if ($text -match '^-?\d+(?:\.\d+)?$') {
        return (Get-IntText -Value $text)
    }

    $resolved = Resolve-AmountText -Expression $text -VarMap $VarMap -Body $Body
    if ($resolved -ne "?") {
        return $resolved
    }

    return $text
}

function Get-ReturnValueTexts {
    param(
        [string]$Body,
        $VarMap
    )

    $values = New-Object System.Collections.Generic.List[string]
    foreach ($match in [regex]::Matches($Body, '(?<![\w.])return\s+(?<value>[^;]+);')) {
        $expression = $match.Groups["value"].Value.Trim()
        if ($expression -eq "" -or $script:NoOpReturnExpressions -contains $expression) {
            continue
        }

        $rendered = Get-ValueExpressionText -Expression $expression -VarMap $VarMap -Body $Body
        if ($rendered -eq "" -or $rendered.Length -gt 80 -or $rendered -match "`n") {
            continue
        }

        if (-not $values.Contains($rendered)) {
            $values.Add($rendered)
        }
    }

    return , $values
}

function Get-BodyClauses {
    param(
        [string]$Body,
        $VarMap
    )

    $clauses = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($Body)) {
        return , $clauses
    }

    foreach ($clause in (Get-EffectClauses -Body $Body -VarMap $VarMap)) {
        $clauses.Add($clause)
    }

    if ($clauses.Count -eq 0) {
        # A value-returning hook (ModifyDamageAdditive, ModifyHandDraw, IsAllowed, ShouldDie,
        # ...) carries its effect in the returned expression rather than in a command call.
        # More than four distinct returns is a branchy body, and summarising it would be a
        # guess, so those stay silent.
        $returns = Get-ReturnValueTexts -Body $Body -VarMap $VarMap
        if ($returns.Count -gt 0 -and $returns.Count -le 4) {
            # "or" rather than "/" because a returned expression may itself be a division.
            $clauses.Add("returns " + ($returns -join " or "))
        }
    }

    if ($clauses.Count -eq 0) {
        # A hook whose whole body is one awaited helper call ("await SummonPet();") has no
        # command to summarise, but the helper's own name is the effect. A body that is a bare
        # call with nothing around it ("Flash();") is presentation, not effect, and is skipped.
        $trimmed = $Body.Trim()
        if ($trimmed -notmatch "`n" -and $trimmed.Length -le 80) {
            $single = $trimmed -replace '^await\s+', '' -replace ';\s*$', ''
            $isAwaited = $trimmed -match '^await\s'
            if ($isAwaited -or $single -match '^-?\d+(?:\.\d+)?m?$|^(?:true|false)$|^(?:base\.)?Amount$|^!?\w+(?:\.\w+)*$') {
                $clauses.Add("returns $(Get-ValueExpressionText -Expression $single -VarMap $VarMap -Body $Body)")
            }
        }
    }

    return , $clauses
}

function Get-HookClauses {
    param(
        [string]$Text,
        [string]$Hook,
        $VarMap
    )

    # A hook usually delegates to a private helper in the same class ("await SummonPet();",
    # "await ModifyStrengthIfNecessary();"); inlining that helper's body is the same step the
    # event summaries take, and it is the difference between "no effect detected" and the
    # amount the helper actually applies.
    $body = Get-MemberBody -Text $Text -MemberName $Hook
    return , (Get-BodyClauses -Body (Add-InlinedHelperBodies -Body $body -FileText $Text) -VarMap $VarMap)
}

###############################################################################
# Monster HP.
#
# MinInitialHp/MaxInitialHp is a plain literal for only a handful of monsters;
# most write it through AscensionHelper.GetValueIfAscension, and a few inherit it
# or name a property of their own. Each of those is a single declaration, so all
# of them are read rather than guessed; a monster whose HP genuinely cannot be
# resolved from source keeps an empty cell.
###############################################################################

function Resolve-MonsterHpValue {
    param(
        [string]$Expression,
        [string]$FileText,
        [string]$MinHpValue = "",
        [int]$Depth = 0
    )

    $text = ""
    if ($null -ne $Expression) {
        $text = $Expression.Trim()
    }

    if ($text -eq "" -or $Depth -gt 3) {
        return ""
    }

    if ($text -match '^\d+$') {
        return $text
    }

    # The roll spans MinInitialHp..MaxInitialHp, so a MaxInitialHp that reads "MinInitialHp"
    # is the same number the Min column already carries.
    if ($text -eq "MinInitialHp") {
        return $MinHpValue
    }

    # AscensionHelper.GetValueIfAscension(level, ascensionValue, fallbackValue) returns the
    # fallback unless the run has that ascension. The table prices a base run, so the
    # non-ascension value is the one kept.
    $ascension = [regex]::Match(
        $text,
        '^AscensionHelper\.GetValueIfAscension\s*\(\s*AscensionLevel\.\w+\s*,\s*(?<ascension>[^,]+),\s*(?<fallback>[^,)]+)\)$'
    )
    if ($ascension.Success) {
        return (Resolve-MonsterHpValue -Expression $ascension.Groups["fallback"].Value -FileText $FileText -MinHpValue $MinHpValue -Depth ($Depth + 1))
    }

    # A named property or constant declared in the same class (TestSubject.FirstFormHp).
    if ($text -match '^\w+$') {
        $declaration = [regex]::Match(
            $FileText,
            '(?m)^\s*(?:public|private|protected|internal)[^\n=;]*?\b' + [regex]::Escape($text) + '\s*=>\s*(?<value>[^;]+);'
        )
        if ($declaration.Success) {
            return (Resolve-MonsterHpValue -Expression $declaration.Groups["value"].Value -FileText $FileText -MinHpValue $MinHpValue -Depth ($Depth + 1))
        }
    }

    return ""
}

function Get-MonsterEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Monsters"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $chain = Get-ClassFileTexts -Directory $dir -Name $name
        $text = $chain[0].Text

        # The declaring class wins over its base, exactly as the C# override does.
        $minExpression = ""
        $maxExpression = ""
        $minText = ""
        $maxText = ""
        foreach ($entry in $chain) {
            if ($minExpression -eq "") {
                $minExpression = Get-RegexValue -Text $entry.Text -Pattern '(?<![A-Za-z])MinInitialHp\s*=>\s*(?<value>[^;]+);'
                if ($minExpression -ne "") {
                    $minText = $entry.Text
                }
            }

            if ($maxExpression -eq "") {
                $maxExpression = Get-RegexValue -Text $entry.Text -Pattern '(?<![A-Za-z])MaxInitialHp\s*=>\s*(?<value>[^;]+);'
                if ($maxExpression -ne "") {
                    $maxText = $entry.Text
                }
            }

            if ($minExpression -ne "" -and $maxExpression -ne "") {
                break
            }
        }

        $minHp = Resolve-MonsterHpValue -Expression $minExpression -FileText $minText
        $maxHp = Resolve-MonsterHpValue -Expression $maxExpression -FileText $maxText -MinHpValue $minHp

        $rows.Add([pscustomobject]@{
            Name = $name
            MinHp = $minHp
            MaxHp = $maxHp
            Moves = Get-MoveSummary -Text $text
            Passive = Get-CommandSummary -MethodBody (Get-MethodBody -Text $text -MethodName "AfterAddedToRoom")
        })
    }

    return $rows
}

###############################################################################
# Relics and powers.
###############################################################################

function Get-RelicOwnershipMap {
    $poolDir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.RelicPools"
    $characterDir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Characters"

    # character name -> declared relic pool, and the energy color it declares in
    $characters = New-Object System.Collections.Generic.List[object]
    foreach ($file in Get-ChildItem -Path $characterDir -File | Sort-Object Name) {
        $text = Get-SourceText -Path $file.FullName
        $characters.Add([pscustomobject]@{
            Name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            Pool = Get-RegexValue -Text $text -Pattern 'RelicPool\s*=>[^;]*?ModelDb\.RelicPool<(?<value>\w+)>'
            Energy = Get-RegexValue -Text $text -Pattern 'energyColorName\s*=\s*"(?<value>[^"]+)"'
        })
    }

    # pool name -> owner label. Same rule as the card pools: the energy-color constant decides
    # which of several claimants is the real owner (the random-character shell claims the
    # ironclad pool but declares no energy color of its own).
    $ownerByPool = @{}
    foreach ($file in Get-ChildItem -Path $poolDir -File | Sort-Object Name) {
        $text = Get-SourceText -Path $file.FullName
        $poolName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $energy = Get-RegexValue -Text $text -Pattern 'EnergyColorName\s*=>\s*"(?<value>[^"]+)"'

        $claimants = @($characters | Where-Object { $_.Pool -eq $poolName } | Sort-Object Name)
        $energyOwners = @($claimants | Where-Object { $_.Energy -ne "" -and $_.Energy -eq $energy })

        if ($energyOwners.Count -gt 0) {
            $ownerByPool[$poolName] = ($energyOwners | ForEach-Object { $_.Name }) -join "/"
        }
        elseif ($claimants.Count -eq 1) {
            $ownerByPool[$poolName] = $claimants[0].Name
        }
        else {
            # Shared / Event / Fallback / Deprecated: no character owns the pool, so the pool
            # class name minus its RelicPool suffix is the label.
            $ownerByPool[$poolName] = (ConvertTo-HumanWords ($poolName -replace 'RelicPool$', ''))
        }
    }

    # relic name -> the pools that declare it, in pool-name order so the result is stable.
    # A relic several pools declare is labelled with every owner, joined the way a card pool
    # claimed by several characters is.
    $poolsByRelic = @{}
    foreach ($file in Get-ChildItem -Path $poolDir -File | Sort-Object Name) {
        $text = Get-SourceText -Path $file.FullName
        $poolName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        foreach ($relicName in (Get-RegexMatches -Text $text -Pattern 'ModelDb\.Relic<(?<value>\w+)>\(\)')) {
            if (-not $poolsByRelic.ContainsKey($relicName)) {
                $poolsByRelic[$relicName] = New-Object System.Collections.Generic.List[string]
            }

            if (-not $poolsByRelic[$relicName].Contains($poolName)) {
                $poolsByRelic[$relicName].Add($poolName)
            }
        }
    }

    $owners = @{}
    foreach ($relicName in $poolsByRelic.Keys) {
        $labels = New-Object System.Collections.Generic.List[string]
        foreach ($poolName in $poolsByRelic[$relicName]) {
            $label = $ownerByPool[$poolName]
            if ($label -and -not $labels.Contains($label)) {
                $labels.Add($label)
            }
        }

        $owners[$relicName] = if ($labels.Count -gt 0) { $labels -join "/" } else { "unknown" }
    }

    return $owners
}

function Get-RelicEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Relics"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]
    $owners = Get-RelicOwnershipMap

    foreach ($file in $files) {
        $text = Get-SourceText -Path $file.FullName
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)

        # The folder also holds non-relic helpers (VakuuCardSelector); only a RelicModel
        # subclass can ever be handed to a player as a relic_id.
        if ($text -notmatch 'class\s+\w+\s*:\s*RelicModel') {
            continue
        }

        $varMap = Get-DynamicVarValues -Text $text
        $clauses = New-Object System.Collections.Generic.List[string]
        foreach ($hook in (Get-OverriddenHooks -Text $text)) {
            foreach ($clause in (Get-HookClauses -Text $text -Hook $hook -VarMap $varMap)) {
                # Every clause names the hook that produces it: a relic's whole character is
                # the difference between "when you pick it up" and "every combat".
                $entry = "$hook`: $clause"
                if (-not $clauses.Contains($entry)) {
                    $clauses.Add($entry)
                }
            }
        }

        if ($clauses.Count -eq 0) {
            $clauses.Add("no gameplay command in an overridden hook (passive in source)")
        }

        $owner = "unknown"
        if ($owners.ContainsKey($name)) {
            $owner = $owners[$name]
        }

        $rows.Add([pscustomobject]@{
            Name   = $name
            Rarity = Get-RegexValue -Text $text -Pattern '(?<![A-Za-z])Rarity\s*=>\s*RelicRarity\.(?<value>\w+)'
            Owner  = $owner
            Effect = ($clauses -join "; ")
        })
    }

    return $rows
}

function Get-PowerIsPositive {
    param(
        $Chain
    )

    foreach ($entry in $Chain) {
        $value = Get-RegexValue -Text $entry.Text -Pattern 'IsPositive\s*=>\s*(?<value>true|false)'
        if ($value -ne "") {
            return $value
        }
    }

    return "true"
}

function Get-PowerEntries {
    $dir = Join-Path $sourceRoot "MegaCrit.Sts2.Core.Models.Powers"
    $files = Get-ChildItem -Path $dir -File | Sort-Object Name
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $chain = Get-ClassFileTexts -Directory $dir -Name $name
        $text = $chain[0].Text

        # The three Temporary*Power bases are abstract: no creature can ever carry them as a
        # power_id, though their concrete children inherit every hook below.
        if ($text -match 'public\s+abstract\s+class') {
            continue
        }

        $isPositive = (Get-PowerIsPositive -Chain $chain) -eq "true"
        $varMap = Get-DynamicVarValues -Text $text

        # PowerModel.Amount is this power's own stack count, and the "amount" a power receives
        # in BeforeApplied/AfterPowerAmountChanged is the amount being applied to it; the two
        # spellings name the same runtime quantity. ITemporaryPower.Sign is +1 for a positive
        # temporary power and -1 otherwise, so "Sign * amount" and "-Sign * base.Amount" reduce
        # to a sign the source declares.
        $appliedSign = if ($isPositive) { "" } else { "-" }
        $expiredSign = if ($isPositive) { "-" } else { "" }

        $stackType = ""
        $type = ""
        foreach ($entry in $chain) {
            if ($stackType -eq "") {
                $stackType = Get-RegexValue -Text $entry.Text -Pattern '(?<![A-Za-z])StackType\s*=>\s*PowerStackType\.(?<value>\w+)'
                if ($stackType -eq "") {
                    # Two powers pick their stack type from runtime state inside a getter
                    # (MonologuePower from a dynamic var, ShrinkPower from a negative amount).
                    # Both branches are reported rather than one being chosen.
                    $stackBlock = Get-RegexValue -Text $entry.Text -Pattern '(?s)override\s+PowerStackType\s+StackType\s*\{(?<value>.*?)\n\t\}'
                    $stackValues = New-Object System.Collections.Generic.List[string]
                    foreach ($stackMatch in [regex]::Matches($stackBlock, 'PowerStackType\.(?<value>\w+)')) {
                        $stackValue = $stackMatch.Groups["value"].Value
                        if (-not $stackValues.Contains($stackValue)) {
                            $stackValues.Add($stackValue)
                        }
                    }

                    if ($stackValues.Count -gt 0) {
                        $stackType = $stackValues -join " / "
                    }
                }
            }

            if ($type -eq "") {
                $type = Get-RegexValue -Text $entry.Text -Pattern '(?<![A-Za-z])Type\s*=>\s*PowerType\.(?<value>\w+)'
                if ($type -eq "") {
                    # The temporary-power bases pick the type from IsPositive inside a getter.
                    $conditional = Get-RegexValue -Text $entry.Text -Pattern '(?s)override\s+PowerType\s+Type\s*\{(?<value>.*?)\n\t\}'
                    if ($conditional -match 'IsPositive') {
                        $type = if ($isPositive) { "Buff" } else { "Debuff" }
                    }
                }
            }

            if ($type -ne "" -and $stackType -ne "") {
                break
            }
        }

        $hooks = New-Object System.Collections.Generic.List[string]
        $clauses = New-Object System.Collections.Generic.List[string]
        foreach ($entry in $chain) {
            foreach ($hook in (Get-OverriddenHooks -Text $entry.Text)) {
                if (-not $hooks.Contains($hook)) {
                    $hooks.Add($hook)
                }

                $body = Get-MemberBody -Text $entry.Text -MemberName $hook
                if ([string]::IsNullOrWhiteSpace($body)) {
                    continue
                }

                $body = Add-InlinedHelperBodies -Body $body -FileText $entry.Text
                $body = $body -replace '\(decimal\)\s*Sign\s*\*\s*amount', "$($appliedSign)Amount"
                $body = $body -replace '-Sign\s*\*\s*base\.Amount', "$($expiredSign)Amount"

                # A hook a base class declares is labelled with that class, so an inherited
                # temporary-power behaviour is never mistaken for the power's own code.
                $label = if ($entry.Name -eq $chain[0].Name) { $hook } else { "$($entry.Name).$hook" }
                foreach ($clause in (Get-BodyClauses -Body $body -VarMap $varMap)) {
                    if ($clause -eq "") {
                        continue
                    }

                    $line = "$label`: $clause"
                    if (-not $clauses.Contains($line)) {
                        $clauses.Add($line)
                    }
                }
            }
        }

        if ($clauses.Count -eq 0) {
            $clauses.Add("no hook effect detected in source")
        }

        $rows.Add([pscustomobject]@{
            Name      = $name
            Type      = $type
            StackType = $stackType
            Hook      = if ($hooks.Count -gt 0) { $hooks -join ", " } else { "passive" }
            Effect    = ($clauses -join "; ")
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
$relics = Get-RelicEntries
$powers = Get-PowerEntries

$summaryBody = @"
## Coverage

- Characters: $($characters.Count)
- Cards: $($cards.Count)
- Monsters: $($monsters.Count)
- Potions: $($potions.Count)
- Events: $($events.Index.Count)
- Event options with a risk tag: $($events.Options.Count)
- Relics: $($relics.Count)
- Powers: $($powers.Count)

## Usage

- Prefer these indexes when MCP returns ``card_id``, ``enemy_id``, ``event_id``, ``potion_id``, ``relic_id``, or ``power_id``.
- Read ``docs/game-knowledge/agent-reference.md`` first, then inspect the specific index file.
- Use ``card-behaviors.md``, ``monster-behaviors.md``, and ``potion-behaviors.md`` when metadata alone is too thin for action choice.
- Use ``relics.md`` to price a relic offer and ``powers.md`` to find out what a ``power_id`` plus a stack count actually does.
- Refresh this knowledge base after game updates by running ``powershell -ExecutionPolicy Bypass -File "scripts/generate-sts2-knowledge.ps1"``.
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
    -Description "Initial HP range lookup for monster internal names seen in ``enemy_id``. The range is the one the fight rolls on a base (non-ascension) run, the same convention ``cards.md`` uses for unupgraded numbers: where the source writes ``MinInitialHp``/``MaxInitialHp`` through ``AscensionHelper.GetValueIfAscension``, the non-ascension value is the one shown, and the ``ToughEnemies`` ascension value is higher for nearly every monster that declares one. A monster that inherits its HP from a base class shows the inherited range. A blank cell means no declaration in that class chain yields a number; it is left empty rather than guessed. Live ``min_hp``/``max_hp`` from the game state remains the authority for the fight in front of you." `
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

$relicsBody = ConvertTo-MarkdownTable -Headers @("Name", "Rarity", "Owner", "Effect") -Rows $relics
Set-Content -Path (Join-Path $outputRoot "relics.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Relic Index" `
    -Description "Rarity, owner, and readable effect for every relic, keyed by the internal name behind ``relic_id`` (the class name; the id the game reports is its slugified upper-case form, e.g. ``BurningBlood`` -> ``BURNING_BLOOD``). Use it when deciding whether to take a relic from a chest, buy one in a shop, or accept one from an event. ``Owner`` is the relic pool that declares the relic: a character name means that character's pool, ``Shared`` is the pool every character draws from, and ``Event``/``Fallback``/``Deprecated`` are the pools no character owns. ``Effect`` names the hook that produces each clause, because for a relic the difference between ``AfterObtained`` (once, on pickup) and ``AfterSideTurnStart`` (every turn) is the whole decision; a hook a base class declares is prefixed with that class name. Numbers come from the relic's dynamic vars; ``?`` marks an amount only computed at run time, and ``Amount`` is the relic's own counter where it has one." `
    -Body $relicsBody)

$powersBody = ConvertTo-MarkdownTable -Headers @("Name", "Type", "StackType", "Hook", "Effect") -Rows $powers
Set-Content -Path (Join-Path $outputRoot "powers.md") -Encoding UTF8 -Value (New-MarkdownDocument `
    -Title "Power Index" `
    -Description "What a ``power_id`` plus a stack count actually does. ``Type`` is ``PowerType`` (Buff/Debuff) and ``StackType`` is ``PowerStackType``: ``Counter`` means the stacks accumulate and the amount is meaningful, ``Single`` means the power is present or not, ``None`` means it does not stack at all, and two values separated by a slash mean the source picks between them at run time. ``Hook`` lists the combat hooks the class overrides, which is when it triggers; ``Effect`` says what each of those hooks does, prefixed with the hook name, with a hook inherited from a base class prefixed with that class instead. ``Amount`` inside an effect is the power's own stack count (``PowerModel.Amount``), not a fixed number, and ``?`` marks an amount that is only computed at run time. A power whose source declares no command in any hook shows ``no hook effect detected in source`` rather than an invented description." `
    -Body $powersBody)

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
