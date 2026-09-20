# Proves the verification gates in scripts/check_verification_gates.py actually fail on drift.
# Builds a throwaway fixture, mutates one input per case, and asserts the matching gate rejects it.
# Offline only: no game, no network.

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$gateScript = Join-Path $repoRoot "scripts/check_verification_gates.py"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Invoke-Gate([string]$Fixture, [string]$Only, [string]$Skip) {
    $arguments = @("--repo-root", $Fixture)
    if ($Only) {
        $arguments += @("--only", $Only)
    }
    if ($Skip) {
        $arguments += @("--skip", $Skip)
    }

    # A failing gate writes to stderr; that is the expected path here, not a script error.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & python $gateScript @arguments 2>&1 | Out-String
    }
    finally {
        $ErrorActionPreference = $previous
    }

    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Invoke-Git([string]$Path, [string[]]$Arguments) {
    # git writes progress and line-ending warnings to stderr; that is not a script error.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & git -C $Path @Arguments 2>&1 | Out-String
    }
    finally {
        $ErrorActionPreference = $previous
    }

    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Remove-Fixture([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    }
    catch {
        # git marks its object files read-only on Windows; clear the attribute and retry once.
        Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Attributes = "Normal" }
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    }
}

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("sts2-verification-gates-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$failures = 0

# $Expect is the point of this function, not decoration. Until 2026-09-17 a case passed on a
# non-zero exit alone, so a gate that died for an unrelated reason -- a missing fixture file, an
# import error, an encoding traceback -- read as "rejected the drifted input". That happened:
# after GameStateService was split into three files, three api-facts cases reported PASS while
# the gate was really failing with "missing required file". A case now names what it expects to
# be told, and saying nothing is itself a failure.
function Assert-Case([string]$Name, [string]$Only, [string]$Expect) {
    if ([string]::IsNullOrWhiteSpace($Expect)) {
        Write-Host "FAIL  $Name (the case declares no expected message)"
        $script:failures++
        return
    }

    $result = Invoke-Gate -Fixture $fixture -Only $Only
    if ($result.ExitCode -eq 0) {
        Write-Host "FAIL  $Name (gate accepted a drifted input)"
        $script:failures++
        return
    }

    $message = ($result.Output -split "\r?\n" | Where-Object { $_ -match "verification gates failed" } | Select-Object -First 1)
    if (-not $message) {
        $message = $result.Output.Trim()
    }

    # Compared with all whitespace removed, against the whole output rather than one line.
    # PowerShell wraps a native command's stderr at the console width, and it wraps mid-word: the
    # name this case exists to see can arrive as "totally_" on one line and "made_up_action" on the
    # next. Dropping whitespace is the same trick the C# source contracts use for the same reason.
    $haystack = (($result.Output -join "") -replace "[\s]", "")
    $needle = ($Expect -replace "[\s]", "")
    if ($haystack -notmatch [regex]::Escape($needle)) {
        Write-Host "FAIL  $Name (rejected, but not for the expected reason)"
        Write-Host "      expected to contain: $Expect"
        Write-Host "      got: $($message.Trim())"
        $script:failures++
        return
    }

    Write-Host "PASS  $Name"
    Write-Host "      $($message.Trim())"
}

try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture "scripts") | Out-Null
    $fixtureScript = Join-Path $fixture "scripts/check_verification_gates.py"
    $fixtureScripts = Join-Path $fixture "scripts"
    Copy-Item -LiteralPath $gateScript -Destination $fixtureScript
    # api-schema is a deep stdlib module: the dispatcher imports it by file path, and its committed
    # output lives under docs/. Mirror both so the fixture proves source changes make the generated
    # contract go stale instead of only proving a missing module fails.
    Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/api_schema.py") -Destination $fixtureScripts
    # packaged-links reads the packaging script, the release checker, and the packaged documents,
    # so the fixture mirrors them for the baseline (all-gates) run to stay green.
    Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/package-release.ps1") -Destination $fixtureScripts
    Copy-Item -LiteralPath (Join-Path $repoRoot "scripts/check_release_package.py") -Destination $fixtureScripts
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.zh-CN.md") -Destination $fixture
    # Mirror the root sentinels the gate looks for, using tracked files only: AGENTS.md is
    # gitignored, so a fresh checkout (and therefore CI) does not contain it.
    $fixtureAgent = Join-Path $fixture "STS2AIAgent"
    New-Item -ItemType Directory -Path $fixtureAgent -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/mod_manifest.json") -Destination $fixtureAgent

    Copy-Item -LiteralPath (Join-Path $repoRoot "package.json") -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot "package-lock.json") -Destination $fixture

    $fixtureMcp = Join-Path $fixture "mcp_server"
    New-Item -ItemType Directory -Path $fixtureMcp | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "mcp_server/pyproject.toml") -Destination $fixtureMcp
    Copy-Item -LiteralPath (Join-Path $repoRoot "mcp_server/uv.lock") -Destination $fixtureMcp
    Copy-Item -LiteralPath (Join-Path $repoRoot "mcp_server/README.md") -Destination $fixtureMcp

    $fixtureAction = Join-Path $fixture "STS2AIAgent/Game"
    New-Item -ItemType Directory -Path $fixtureAction -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Game/GameActionService.cs") -Destination $fixtureAction
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Game/GameStateService.cs") -Destination $fixtureAction
    # GameStateService is a partial class across several files: the payload declarations, the compact
    # agent_view, and -- since 2026-09-20 -- one file per screen for the raw /state builders. The
    # whole-tree mirror below brings the per-screen files in; these are the two the older api-facts
    # checks name by path.
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Game/GameStateService.AgentView.cs") -Destination $fixtureAction
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Game/GameStateService.Payloads.cs") -Destination $fixtureAction

    $fixtureServerSource = Join-Path $fixture "STS2AIAgent/Server"
    New-Item -ItemType Directory -Path $fixtureServerSource -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Server/HttpServer.cs") -Destination $fixtureServerSource
    # api-facts also reads BuildHealthData, so the router has to be in the fixture too.
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Server/Router.cs") -Destination $fixtureServerSource

    # arch-facts measures the whole mod, not a handful of files: it counts every source file and
    # then insists that each one over 1,000 lines appears in the architecture page's table. A
    # fixture holding five files would make it measure a mod that does not exist, so the tree is
    # mirrored whole. It is text and it is small.
    $sourceMod = Join-Path $repoRoot "STS2AIAgent"
    Get-ChildItem -Path $sourceMod -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch "[\/](bin|obj)[\/]" } |
        ForEach-Object {
            $relative = $_.FullName.Substring($sourceMod.Length).TrimStart([char]92, [char]47)
            $destination = Join-Path $fixtureAgent $relative
            $parent = Split-Path -Parent $destination
            if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }

    $fixtureArchDir = Join-Path $fixture ".trellis/spec/mod"
    New-Item -ItemType Directory -Path $fixtureArchDir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot ".trellis/spec/mod/architecture.md") -Destination $fixtureArchDir

    $sourceDocs = Join-Path $repoRoot "docs"
    $fixtureDocs = Join-Path $fixture "docs"
    # Every file, not only the Markdown: doc-links resolves links to screenshots and to the root
    # status page, and a fixture holding the prose but not what it points at would make the gate
    # report dangling links that are perfectly fine in the real tree. docs/ is 13 MB.
    Copy-Item -LiteralPath (Join-Path $repoRoot "PRODUCT_PLAN_CURRENT.md") -Destination $fixture
    Get-ChildItem -Path $sourceDocs -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($sourceDocs.Length).TrimStart([char]92, [char]47)
        $destination = Join-Path $fixtureDocs $relative
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination
    }

    $baseline = Invoke-Gate -Fixture $fixture -Only $null -Skip "doc-links"
    if ($baseline.ExitCode -ne 0) {
        Write-Host "FAIL  baseline (an unmodified fixture must pass)"
        Write-Host $baseline.Output
        $failures++
    }
    else {
        Write-Host "PASS  baseline"
    }

    # 1. A code action missing from docs/api.md.
    $apiDoc = Join-Path $fixtureDocs "api.md"
    $original = Read-Utf8 $apiDoc
    $mutated = ($original -split "\r?\n" | Where-Object { $_ -notmatch '^- `choose_bundle`' }) -join "`n"
    Write-Utf8 $apiDoc $mutated
    Assert-Case -Name "api-doc drift rejects undocumented action" -Only "api-doc" -Expect "does not document these actions"
    Write-Utf8 $apiDoc $original

    # 2. A documented action the code does not accept.
    # Build the backticks with [char]96: PowerShell would treat a literal backtick as an escape.
    # Keep this fixture ASCII: Windows PowerShell 5.1 reads a BOM-less script with the machine's
    # ANSI code page, where the UTF-8 bytes of a dash decode to a smart quote that ends the string.
    $phantom = "- " + [char]96 + "totally_made_up_action" + [char]96 + " - not real"
    $mutated = $original.Replace("<!-- END ACTION CONTRACT -->", $phantom + [char]10 + "<!-- END ACTION CONTRACT -->")
    if ($mutated -eq $original) { throw "fixture setup failed: the action contract end marker was not found in docs/api.md" }
    Write-Utf8 $apiDoc $mutated
    Assert-Case -Name "api-doc drift rejects phantom action" -Only "api-doc" -Expect "totally_made_up_action"
    Write-Utf8 $apiDoc $original

    # 3. A lockfile below the security floor.
    $uvLock = Join-Path $fixtureMcp "uv.lock"
    $originalLock = Read-Utf8 $uvLock
    $mutated = $originalLock -replace '(?m)^(name = "fastmcp"\r?\nversion = ")[^"]+(")', '${1}3.1.0${2}'
    if ($mutated -eq $originalLock) { throw "fixture setup failed: could not rewrite the fastmcp version in uv.lock" }
    Write-Utf8 $uvLock $mutated
    Assert-Case -Name "lockfile gate rejects a version below the security floor" -Only "lockfile" -Expect "below the safe floor"
    Write-Utf8 $uvLock $originalLock

    # 3b. A manifest that demands more than the lock resolves (the lock is stale, not unsafe).
    $pyproject = Join-Path $fixtureMcp "pyproject.toml"
    $originalPyproject = Read-Utf8 $pyproject
    $mutated = $originalPyproject -replace 'fastmcp>=3\.1\.0,<4\.0\.0', 'fastmcp>=9.0.0,<10.0.0'
    if ($mutated -eq $originalPyproject) { throw "fixture setup failed: could not rewrite the fastmcp range in pyproject.toml" }
    Write-Utf8 $pyproject $mutated
    Assert-Case -Name "lockfile gate rejects a stale uv.lock against its manifest" -Only "lockfile" -Expect "uv.lock is out of sync"
    Write-Utf8 $pyproject $originalPyproject

    # 3c. An npm manifest that demands a version the lock cannot satisfy.
    $npmManifest = Join-Path $fixture "package.json"
    $originalNpmManifest = Read-Utf8 $npmManifest
    $mutated = $originalNpmManifest -replace '"@sammysnake/fast-context-mcp": "\^1\.2\.0"', '"@sammysnake/fast-context-mcp": "^99.0.0"'
    if ($mutated -eq $originalNpmManifest) { throw "fixture setup failed: could not rewrite the npm dependency range" }
    Write-Utf8 $npmManifest $mutated
    Assert-Case -Name "lockfile gate rejects a stale package-lock against its manifest" -Only "lockfile" -Expect "package-lock.json is out of sync"
    Write-Utf8 $npmManifest $originalNpmManifest

    # 4. A date-stamped record without its historical marker.
    $datedDoc = Join-Path $fixtureDocs "phase-8-validation-2026-03-11.md"
    $originalDated = Read-Utf8 $datedDoc
    $mutated = ($originalDated -split "\r?\n" | Where-Object { $_ -notmatch 'Historical snapshot|历史快照' }) -join "`n"
    Write-Utf8 $datedDoc $mutated
    Assert-Case -Name "doc-marks gate rejects an unmarked snapshot" -Only "doc-marks" -Expect "phase-8-validation-2026-03-11.md"
    Write-Utf8 $datedDoc $originalDated

    $matrixDoc = Join-Path $fixtureDocs "mechanic-coverage-matrix.md"
    $originalMatrix = Read-Utf8 $matrixDoc
    $mutated = ($originalMatrix -split "\r?\n" | Where-Object { $_ -notmatch 'Historical snapshot|历史快照' }) -join "`n"
    if ($mutated -eq $originalMatrix) { throw "fixture setup failed: the matrix header holds no marker to remove" }
    Write-Utf8 $matrixDoc $mutated
    Assert-Case -Name "doc-marks gate rejects an unmarked date-less snapshot" -Only "doc-marks" -Expect "mechanic-coverage-matrix.md"
    Write-Utf8 $matrixDoc $originalMatrix

   # 5. An archived topic page that lost its redirect.
    $redirectDoc = Join-Path $fixtureDocs "sts2-coverage-gaps.md"
    $originalRedirect = Read-Utf8 $redirectDoc
    $mutated = $originalRedirect -replace 'history/sts2-coverage-gaps_2026-03-10.md', 'somewhere-else.md'
    Write-Utf8 $redirectDoc $mutated
    Assert-Case -Name "doc-marks gate rejects a broken archive redirect" -Only "doc-marks" -Expect "must redirect to history/"
    Write-Utf8 $redirectDoc $originalRedirect

    # 6. A PowerShell script with non-ASCII text saved without a UTF-8 BOM. Windows PowerShell 5.1
    # would read such a file with the machine's ANSI code page, so the same bytes decode differently
    # per locale and a stray quote character can break the whole script.
    $snapshotMarker = [string][char]0x5386 + [char]0x53F2 + [char]0x5FEB + [char]0x7167
    $encodingScript = Join-Path (Join-Path $fixture "scripts") "fixture-non-ascii.ps1"
    $fixtureBody = "Write-Host '" + $snapshotMarker + "'" + [char]10
    [System.IO.File]::WriteAllText($encodingScript, $fixtureBody, (New-Object System.Text.UTF8Encoding($true)))
    $withBom = Invoke-Gate -Fixture $fixture -Only "script-encoding"
    if ($withBom.ExitCode -ne 0) {
        Write-Host "FAIL  script-encoding gate rejects a non-ASCII script that has a BOM"
        Write-Host $withBom.Output
        $failures++
    }
    else {
        Write-Host "PASS  script-encoding gate accepts a non-ASCII script with a BOM"
    }

    Write-Utf8 $encodingScript $fixtureBody
    Assert-Case -Name "script-encoding gate rejects non-ASCII without a BOM" -Only "script-encoding" -Expect "fixture-non-ascii.ps1"
    Remove-Item -LiteralPath $encodingScript -Force

    # 6b. No .ps1 to parse at all. The fixture scripts/ holds the gate copy plus the packaged-links
    # inputs at this point, so stash the copied packaging script to pin the branch the other cases
    # depend on: a tree with no PowerShell script has to skip and explain, not fail and not pass
    # silently. Without it every case above would turn red.
    $packagingScript = Join-Path $fixture "scripts/package-release.ps1"
    $stashedPackagingScript = Join-Path $fixture "package-release.ps1.stash"
    Move-Item -LiteralPath $packagingScript -Destination $stashedPackagingScript
    $ps1Empty = Invoke-Gate -Fixture $fixture -Only "ps1-syntax"
    Move-Item -LiteralPath $stashedPackagingScript -Destination $packagingScript
    if ($ps1Empty.ExitCode -ne 0 -or $ps1Empty.Output -notmatch "no PowerShell scripts under scripts/ to parse") {
        Write-Host "FAIL  ps1-syntax gate skips a scripts/ that holds no .ps1"
        Write-Host $ps1Empty.Output
        $script:failures++
    }
    else {
        Write-Host "PASS  ps1-syntax gate skips a scripts/ that holds no .ps1"
    }

    # 6c. A well-formed .ps1 the parser must accept. The gate reads the AST, so an accepted script
    # is reported by name and count rather than run.
    $validPs1 = Join-Path (Join-Path $fixture "scripts") "fixture-valid-probe.ps1"
    Write-Utf8 $validPs1 ("Write-Host 'fixture probe'" + [char]10)
    $ps1Valid = Invoke-Gate -Fixture $fixture -Only "ps1-syntax"
    if ($ps1Valid.ExitCode -ne 0 -or $ps1Valid.Output -notmatch "PowerShell scripts parse cleanly") {
        Write-Host "FAIL  ps1-syntax gate accepts a well-formed script"
        Write-Host $ps1Valid.Output
        $script:failures++
    }
    else {
        Write-Host "PASS  ps1-syntax gate accepts a well-formed script"
    }
    Remove-Item -LiteralPath $validPs1 -Force

    # 6d. An unbalanced script. A stray brace or quote aborts a script on launch, and these scripts
    # are build, packaging, and real-machine entry points that nothing else parses, so this is the
    # failure the gate exists to catch.
    $brokenPs1 = Join-Path (Join-Path $fixture "scripts") "fixture-broken-probe.ps1"
    Write-Utf8 $brokenPs1 ("if (" + [char]10)
    Assert-Case -Name "ps1-syntax gate rejects a script with a syntax error" -Only "ps1-syntax" -Expect "fixture-broken-probe.ps1"
    Remove-Item -LiteralPath $brokenPs1 -Force

    # 7. The mod version documented in docs/api.md drifting away from the manifest.
    $factsDoc = Join-Path $fixtureDocs "api.md"
    $originalFactsDoc = Read-Utf8 $factsDoc
    $mutated = $originalFactsDoc -replace '"mod_version": "[^"]+"', '"mod_version": "0.0.1"'
    if ($mutated -eq $originalFactsDoc) { throw "fixture setup failed: docs/api.md has no mod_version value to rewrite" }
    Write-Utf8 $factsDoc $mutated
    Assert-Case -Name "api-facts gate rejects a stale documented mod_version" -Only "api-facts" -Expect "states mod_version 0.0.1"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 8. A screen the code can emit but the docs enum no longer lists.
    $mutated = ($originalFactsDoc -split "\r?\n" | Where-Object { $_ -notmatch ('^\| ' + [char]96 + 'CARDS_VIEW' + [char]96) }) -join [char]10
    if ($mutated -eq $originalFactsDoc) { throw "fixture setup failed: docs/api.md has no CARDS_VIEW screen row" }
    Write-Utf8 $factsDoc $mutated
    Assert-Case -Name "api-facts gate rejects a screen missing from the docs enum" -Only "api-facts" -Expect "can emit screens missing from"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 9. The documented default port drifting away from HttpServer.DefaultPort.
    $httpServer = Join-Path $fixture "STS2AIAgent/Server/HttpServer.cs"
    $originalHttpServer = Read-Utf8 $httpServer
    $mutated = $originalHttpServer -replace 'const int DefaultPort = \d+', 'const int DefaultPort = 9999'
    if ($mutated -eq $originalHttpServer) { throw "fixture setup failed: HttpServer.cs has no DefaultPort constant to rewrite" }
    Write-Utf8 $httpServer $mutated
    Assert-Case -Name "api-facts gate rejects a default port the docs do not state" -Only "api-facts" -Expect "states default port 8080"
    Write-Utf8 $httpServer $originalHttpServer

    # 9c. The /state combat payload records drifting away from the docs/api.md tables that
    # describe them. Every field here reaches agents through the compact agent_view, and the
    # action contract only covers action names, so this is the drift nothing else would notice.
    $mutated = ($originalFactsDoc -split "?
" | Where-Object { $_ -notmatch ('^\| ' + [char]96 + 'lethal_risks' + [char]96 + ' \|') }) -join [char]10
    if ($mutated -eq $originalFactsDoc) { throw "fixture setup failed: docs/api.md has no lethal_risks field row" }
    Write-Utf8 $factsDoc $mutated
    Assert-Case -Name "api-facts gate rejects a combat payload field the docs stop listing" -Only "api-facts" -Expect "CombatPayload serializes fields"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 9d. The other direction: a documented field the record never serializes, which is what a
    # client would branch on and never receive.
    $playerRow = ($originalFactsDoc -split "?
" | Where-Object { $_ -match ('^\| ' + [char]96 + 'player' + [char]96 + ' \| object \|') } | Select-Object -First 1)
    if (-not $playerRow) { throw "fixture setup failed: docs/api.md has no combat player field row" }
    $ghostRow = '| ' + [char]96 + 'ghost_field' + [char]96 + ' | object | fixture |'
    $mutated = $originalFactsDoc.Replace($playerRow, $playerRow + [char]10 + $ghostRow)
    Write-Utf8 $factsDoc $mutated
    Assert-Case -Name "api-facts gate rejects a documented field the payload never serializes" -Only "api-facts" -Expect "table lists fields"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 9e. A reason code EvaluateCombatActionGate can answer with that the docs never mention.
    # Each code tells an agent something different about whether to wait, so an undocumented one
    # reads as an unknown failure rather than "keep polling".
    $mutated = ($originalFactsDoc -split "?
" | Where-Object { $_ -notmatch ('^\| ' + [char]96 + 'snapshot_stabilizing' + [char]96 + ' \|') }) -join [char]10
    if ($mutated -eq $originalFactsDoc) { throw "fixture setup failed: docs/api.md has no snapshot_stabilizing reason row" }
    Write-Utf8 $factsDoc $mutated
    Assert-Case -Name "api-facts gate rejects an undocumented action_readiness reason" -Only "api-facts" -Expect "reason codes the docs/api.md"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 9f. A payload field that no table owns and that docs/api.md never names. The per-table cases
    # above only cover records that have a table; this is the coarse net under them, and it is the
    # one that was missing while 91 fields -- whole screens, including character select, the
    # multiplayer lobby and game over -- shipped undocumented.
    $stateService = Join-Path $fixture "STS2AIAgent/Game/GameStateService.Payloads.cs"
    $originalStateService = Read-Utf8 $stateService
    $nl = if ($originalStateService.Contains([char]13 + [char]10)) { [char]13 + [char]10 } else { [char]10 }
    $orbAnchor = "internal sealed class CombatOrbPayload"
    if ($originalStateService.IndexOf($orbAnchor) -lt 0) { throw "fixture setup failed: GameStateService.Payloads.cs has no CombatOrbPayload record" }
    $strayRecord = "internal sealed class FixtureStrayPayload" + $nl + "{" + $nl + "    public int fixture_undocumented_field { get; init; }" + $nl + "}" + $nl + $nl + $orbAnchor
    $mutated = $originalStateService.Replace($orbAnchor, $strayRecord)
    if ($mutated -eq $originalStateService) { throw "fixture setup failed: could not inject a stray payload record" }
    Write-Utf8 $stateService $mutated
    Assert-Case -Name "api-facts gate rejects a payload field docs/api.md never names" -Only "api-facts" -Expect "fixture_undocumented_field"
    Write-Utf8 $stateService $originalStateService

    # 9m. A member the checks ask about, moved to another file of the same partial class. Reading a
    # single path made api-facts report that EvaluateCombatActionGate had been deleted the moment the
    # raw builders were split by screen: the member was fine, the reader was pointed at one file of a
    # partial. Deleting that file is the same failure from the other side, and the family reader has
    # to see it.
    $combatPartial = Join-Path $fixture "STS2AIAgent/Game/GameStateService.Combat.cs"
    if (-not (Test-Path $combatPartial)) { throw "fixture setup failed: the fixture has no GameStateService.Combat.cs partial" }
    $originalCombatPartial = Read-Utf8 $combatPartial
    Remove-Item -LiteralPath $combatPartial -Force
    Assert-Case -Name "api-facts gate reads every file of the GameStateService partial" -Only "api-facts" -Expect "no longer declares"
    Write-Utf8 $combatPartial $originalCombatPartial

    # 9g. The compact rename table claiming a rename the agent view does not perform. The compact
    # view is what MCP get_game_state returns by default, so a wrong compact key sends a client to a
    # property that is simply not there -- it reads as an empty state rather than as an error.
    $tick = [char]96
    $renameRow = '| ' + $tick + 'character_select' + $tick + ' | ' + $tick + 'can_embark' + $tick +
        ' / ' + $tick + 'selected_character_id' + $tick + ' | ' + $tick + 'embark' + $tick +
        ' / ' + $tick + 'selected' + $tick + ' |'
    if ($originalFactsDoc.IndexOf($renameRow) -lt 0) { throw "fixture setup failed: docs/api.md has no character_select compact rename row" }
    $brokenRow = $renameRow.Replace($tick + 'embark' + $tick + ' / ', $tick + 'disembark' + $tick + ' / ')
    if ($brokenRow -eq $renameRow) { throw "fixture setup failed: could not rewrite the compact rename row" }
    Write-Utf8 $factsDoc $originalFactsDoc.Replace($renameRow, $brokenRow)
    Assert-Case -Name "api-facts gate rejects a compact rename the agent view does not perform" -Only "api-facts" -Expect "compact rename table claims"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 9h. A GET /health key that only appears in the example JSON. /health is the first call any
    # client makes and the one a person checks when something is wrong, so a key nobody documents is
    # a key nobody reads.
    $tick = [char]96
    $serviceRow = ($originalFactsDoc -split ([char]10) | Where-Object { $_ -match ('^\| ' + $tick + 'service' + $tick + ' \|') } | Select-Object -First 1)
    if (-not $serviceRow) { throw "fixture setup failed: docs/api.md has no service row in the /health table" }
    Write-Utf8 $factsDoc $originalFactsDoc.Replace($serviceRow + [char]10, "")
    Assert-Case -Name "api-facts gate rejects an undocumented GET /health key" -Only "api-facts" -Expect "GET /health answers with keys"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 9j-l. The three contract surfaces besides the payload: error codes, event types and routes.
    # docs/api.md tells a client what it may call, what it will read, what it must handle and what
    # it can wait for. Only the payload half was checked until 2026-09-18, and three error codes had
    # already slipped out of the table -- a 500, a 405 and a 413 an agent can receive and cannot
    # look up.
    $errorRow = ($originalFactsDoc -split ([char]10) | Where-Object { $_ -match ('^\| ' + [char]96 + 'payload_too_large' + [char]96 + ' \|') } | Select-Object -First 1)
    if (-not $errorRow) { throw "fixture setup failed: docs/api.md has no payload_too_large error row" }
    Write-Utf8 $factsDoc $originalFactsDoc.Replace($errorRow + [char]10, "")
    Assert-Case -Name "api-facts gate rejects an error code the docs stop listing" -Only "api-facts" -Expect "payload_too_large"
    Write-Utf8 $factsDoc $originalFactsDoc

    # The status is what a client branches on before it ever reads the code, so a row with the
    # right name and the wrong number is worse than a missing row.
    $statusRow = ($originalFactsDoc -split ([char]10) | Where-Object { $_ -match ('^\| ' + [char]96 + 'invalid_target' + [char]96 + ' \| 409 ') } | Select-Object -First 1)
    if (-not $statusRow) { throw "fixture setup failed: docs/api.md has no invalid_target 409 row" }
    Write-Utf8 $factsDoc $originalFactsDoc.Replace($statusRow, $statusRow.Replace("| 409 ", "| 400 "))
    Assert-Case -Name "api-facts gate rejects a documented error status the code contradicts" -Only "api-facts" -Expect "wrong HTTP status"
    Write-Utf8 $factsDoc $originalFactsDoc

    $eventRow = ($originalFactsDoc -split ([char]10) | Where-Object { $_ -match ('^\| ' + [char]96 + 'combat_turn_changed' + [char]96 + ' \|') } | Select-Object -First 1)
    if (-not $eventRow) { throw "fixture setup failed: docs/api.md has no combat_turn_changed event row" }
    Write-Utf8 $factsDoc $originalFactsDoc.Replace($eventRow + [char]10, "")
    Assert-Case -Name "api-facts gate rejects an event type the docs stop listing" -Only "api-facts" -Expect "combat_turn_changed"
    Write-Utf8 $factsDoc $originalFactsDoc

    # An event name that stops being spelled as a literal beside its publish call. `stream_ready`
    # is sent through PublishSnapshot, which is the third spelling the extraction has to recognise:
    # if it stops matching, the gate must say the documented type is never published rather than
    # quietly checking one fewer event.
    $eventServicePath = Join-Path $fixture "STS2AIAgent/Server/GameEventService.cs"
    $originalEventService = Read-Utf8 $eventServicePath
    $readyLiteral = 'PublishSnapshot("stream_ready", current)'
    if ($originalEventService.IndexOf($readyLiteral) -lt 0) { throw "fixture setup failed: GameEventService.cs has no $readyLiteral call" }
    Write-Utf8 $eventServicePath $originalEventService.Replace($readyLiteral, 'PublishSnapshot(eventNameForSnapshot, current)')
    Assert-Case -Name "api-facts gate rejects a snapshot event name the code stops spelling" -Only "api-facts" -Expect "stream_ready"
    Write-Utf8 $eventServicePath $originalEventService

    # An event whose name comes from a constant. The literal pattern cannot see it, so the gate
    # carries an explicit list; dropping the constant's use has to be reported as stale gate
    # bookkeeping rather than silently reducing what the gate checks.
    $constantUse = 'EventChurnPolicy.EventType'
    if ($originalEventService.IndexOf($constantUse) -lt 0) { throw "fixture setup failed: GameEventService.cs never uses $constantUse" }
    Write-Utf8 $eventServicePath $originalEventService.Replace($constantUse, 'ChurnEventType')
    Assert-Case -Name "api-facts gate reports a constant-named event its list no longer matches" -Only "api-facts" -Expect "debug_churn"
    Write-Utf8 $eventServicePath $originalEventService

    # A documented endpoint the router does not serve. The other direction -- a served path with no
    # section -- is covered by mutating the router itself below.
    $dataHeading = '## ' + [char]96 + 'GET /data/{collection}' + [char]96
    if ($originalFactsDoc.IndexOf($dataHeading) -lt 0) { throw "fixture setup failed: docs/api.md has no /data/{collection} heading" }
    $phantomHeading = '## ' + [char]96 + 'POST /fixture/phantom' + [char]96 + [char]10 + [char]10 + 'fixture' + [char]10 + [char]10 + '---' + [char]10 + [char]10 + $dataHeading
    Write-Utf8 $factsDoc $originalFactsDoc.Replace($dataHeading, $phantomHeading)
    Assert-Case -Name "api-facts gate rejects a documented endpoint the router does not serve" -Only "api-facts" -Expect "/fixture/phantom"
    Write-Utf8 $factsDoc $originalFactsDoc

    $routerPath = Join-Path $fixture "STS2AIAgent/Server/Router.cs"
    $originalRouter = Read-Utf8 $routerPath
    $healthRoute = 'request.Url?.AbsolutePath == "/health")'
    if ($originalRouter.IndexOf($healthRoute) -lt 0) { throw "fixture setup failed: Router.cs has no /health dispatch" }
    Write-Utf8 $routerPath $originalRouter.Replace($healthRoute, 'request.Url?.AbsolutePath == "/fixture/undocumented")')
    Assert-Case -Name "api-facts gate rejects a served path with no documented section" -Only "api-facts" -Expect "/fixture/undocumented"
    Write-Utf8 $routerPath $originalRouter

    # 9h. The machine-readable contract is generated, not another hand-maintained route list. A
    # source-owned response key changing without regeneration must fail with "out of date"; merely
    # deleting docs/openapi.json would only prove that the module exists.
    $healthAnchor = 'service = ServiceName,'
    if ($originalRouter.IndexOf($healthAnchor) -lt 0) { throw "fixture setup failed: Router.cs has no BuildHealthData service key" }
    $schemaDriftRouter = $originalRouter.Replace($healthAnchor, $healthAnchor + [char]10 + '            schema_fixture_key = true,')
    Write-Utf8 $routerPath $schemaDriftRouter
    Assert-Case -Name "api-schema gate rejects CSharp source drift without regeneration" -Only "api-schema" -Expect "docs/openapi.json is out of date"
    Write-Utf8 $routerPath $originalRouter

    # 9i. The gate's own output on a console that is not UTF-8. Gate messages quote the Chinese
    # section headings of docs/api.md, and the Windows CI runner's stdout is cp1252: printing one
    # raised UnicodeEncodeError, so a gate that PASSED still exited 1, and a gate that failed would
    # have had its real message replaced by an encoding traceback. This case runs the whole suite
    # the way CI does and requires it to survive.
    $previousIoEncoding = $env:PYTHONIOENCODING
    $env:PYTHONIOENCODING = "cp1252"
    try {
        $cp1252Result = Invoke-Gate -Fixture $fixture -Only "api-facts"
    }
    finally {
        if ($null -eq $previousIoEncoding) { Remove-Item Env:PYTHONIOENCODING -ErrorAction SilentlyContinue }
        else { $env:PYTHONIOENCODING = $previousIoEncoding }
    }
    if ($cp1252Result.ExitCode -ne 0 -or $cp1252Result.Output -match "UnicodeEncodeError") {
        Write-Host "FAIL  gate output survives a non-UTF-8 console"
        Write-Host "      $($cp1252Result.Output.Trim())"
        $failures++
    }
    else {
        Write-Host "PASS  gate output survives a non-UTF-8 console"
    }

    # 11a-c. The architecture page's measurements. This page went stale once already: it carried
    # pre-ADR-0001 line counts for a month and, worse, kept telling readers to add every new action
    # to *both* action surfaces after that duplication was gone. Numbers nobody checks are numbers
    # that drift, and a page that is confidently wrong about the shape of the code is worse than no
    # page at all.
    $archDoc = Join-Path $fixture ".trellis/spec/mod/architecture.md"
    $originalArch = Read-Utf8 $archDoc
    $archNl = if ($originalArch.Contains([char]13 + [char]10)) { [char]13 + [char]10 } else { [char]10 }

    $archRow = ($originalArch -split "`r?`n" | Where-Object { $_ -match "^\| \[GameStateService\.cs\]" } | Select-Object -First 1)
    if (-not $archRow) { throw "fixture setup failed: architecture.md has no GameStateService.cs row" }
    $staleRow = $archRow -replace "\| [\d,]+ \|", "| 8,559 |"
    if ($staleRow -eq $archRow) { throw "fixture setup failed: could not rewrite the line count" }
    Write-Utf8 $archDoc $originalArch.Replace($archRow, $staleRow)
    Assert-Case -Name "arch-facts gate rejects a stale line count" -Only "arch-facts" -Expect "is 8,559 lines; it is"
    Write-Utf8 $archDoc $originalArch

    # A big file the table simply does not mention. This is the failure that matters most: the table
    # is how someone finds out where the mod's weight is, so a monolith missing from it is one
    # nobody is watching.
    $roomsRow = ($originalArch -split "`r?`n" | Where-Object { $_ -match "^\| \[GameActionService\.Rooms\.cs\]" } | Select-Object -First 1)
    if (-not $roomsRow) { throw "fixture setup failed: architecture.md has no GameActionService.Rooms.cs row" }
    Write-Utf8 $archDoc $originalArch.Replace($roomsRow + $archNl, "")
    Assert-Case -Name "arch-facts gate rejects a large file the table omits" -Only "arch-facts" -Expect "GameActionService.Rooms.cs"
    Write-Utf8 $archDoc $originalArch

    # Renaming the section is how a check like this gets turned off by accident rather than on
    # purpose, so the gate refuses to pass when it cannot find what it reads.
    Write-Utf8 $archDoc $originalArch.Replace("## Code shape and its known debts", "## Code shape")
    Assert-Case -Name "arch-facts gate rejects a renamed section" -Only "arch-facts" -Expect "turns the check off"
    Write-Utf8 $archDoc $originalArch

    # 9b. A packaged README linking to a file the release does not ship, at a target no rewrite
    # rule covers. This is the shape #105 shipped: the link was new, the rewrite table did not
    # know it, and the artifact builder copied the file through untouched. packaged-links was the
    # only gate with no destructive case, so nothing proved it could still catch that.
    $readmePath = Join-Path $fixture "README.md"
    $originalReadme = Read-Utf8 $readmePath
    $slash = [char]47
    $leak = "[fixture leak](." + $slash + "docs" + $slash + "roadmap-current.md)"
    Write-Utf8 $readmePath ($originalReadme.TrimEnd() + [char]10 + [char]10 + $leak + [char]10)
    $leakResult = Invoke-Gate -Fixture $fixture -Only "packaged-links"
    if ($leakResult.ExitCode -eq 0) {
        Write-Host "FAIL  packaged-links gate rejects a link the release does not ship (gate accepted a drifted input)"
        Write-Host $leakResult.Output
        $script:failures++
    }
    elseif ($leakResult.Output -notmatch "roadmap-current") {
        Write-Host "FAIL  packaged-links gate names the unshipped target"
        Write-Host $leakResult.Output
        $script:failures++
    }
    else {
        Write-Host "PASS  packaged-links gate rejects a link the release does not ship"
        $leakMessage = ($leakResult.Output -split "\r?\n" | Where-Object { $_ -match "verification gates failed" } | Select-Object -First 1)
        Write-Host ("      " + $leakMessage.Trim())
    }
    Write-Utf8 $readmePath $originalReadme

    # 10. A docs/*.md that is on disk but that git does not track. docs/ used to be gitignored,
    # so a new page could pass the local doc-marks gate and still be absent from every fresh
    # checkout -- which is exactly what CI builds from. The gate compares against the git index,
    # so outside a work tree it has to skip with a note instead of failing: a source tarball has
    # no .git and no index.
    $noRepo = Invoke-Gate -Fixture $fixture -Only "docs-tracked"
    if ($noRepo.ExitCode -ne 0 -or $noRepo.Output -notmatch "skipping the docs/ tracking check") {
        Write-Host "FAIL  docs-tracked gate skips a tree without .git"
        Write-Host $noRepo.Output
        $script:failures++
    }
    else {
        Write-Host "PASS  docs-tracked gate skips a tree without .git"
    }

    # The fixture is not a repository, so give it one and track everything in it. That is the
    # state a developer is in after the docs/ ignore rule is gone and the pages are added.
    $gitInit = Invoke-Git -Path $fixture -Arguments @("init", "--quiet")
    if ($gitInit.ExitCode -ne 0) { throw "fixture setup failed: git init`n$($gitInit.Output)" }
    $gitAdd = Invoke-Git -Path $fixture -Arguments @("add", "-A")
    if ($gitAdd.ExitCode -ne 0) { throw "fixture setup failed: git add -A`n$($gitAdd.Output)" }

    $trackedRepo = Invoke-Gate -Fixture $fixture -Only "docs-tracked"
    if ($trackedRepo.ExitCode -ne 0) {
        Write-Host "FAIL  docs-tracked gate rejects a fully tracked docs tree"
        Write-Host $trackedRepo.Output
        $script:failures++
    }
    else {
        Write-Host "PASS  docs-tracked gate accepts a fully tracked docs tree"
    }

    $untrackedDoc = Join-Path $fixtureDocs "fixture-untracked-page.md"
    Write-Utf8 $untrackedDoc "# Fixture page`n"
    Assert-Case -Name "docs-tracked gate rejects a docs file git does not track" -Only "docs-tracked" -Expect "fixture-untracked-page.md"
    Remove-Item -LiteralPath $untrackedDoc -Force

    # 12. A relative Markdown link pointing at a file that is not there. This is the dullest way a
    # repository decays -- someone moves a file and the pages that pointed at it keep pointing --
    # and until 2026-09-17 nothing checked it outside the three packaged documents. A specification
    # that sends a reader to a 404 stops being trusted and then stops being read.
    #
    # It gets its own tree. doc-links asks a question about a whole repository, and the fixture
    # above is deliberately a handful of files: every link in README.md that points at LICENSE or
    # the skills directory dangles there and is perfectly fine in the real checkout. Running it
    # against a partial tree would mean a gate that reports problems it invented, so the two
    # whole-suite runs skip it and this tree is built to hold exactly the links it declares.
    $linkFixture = Join-Path ([System.IO.Path]::GetTempPath()) ("sts2-doc-links-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    try {
        New-Item -ItemType Directory -Path $linkFixture | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $linkFixture "docs") | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $linkFixture "scripts") | Out-Null
        Copy-Item -LiteralPath $gateScript -Destination (Join-Path $linkFixture "scripts/check_verification_gates.py")
        Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $linkFixture
        # The gate script refuses a directory that does not look like the repository root.
        New-Item -ItemType Directory -Path (Join-Path $linkFixture "STS2AIAgent") | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $linkFixture "mcp_server") | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/mod_manifest.json") -Destination (Join-Path $linkFixture "STS2AIAgent")
        Copy-Item -LiteralPath (Join-Path $repoRoot "mcp_server/pyproject.toml") -Destination (Join-Path $linkFixture "mcp_server")
        # README.md came from the real repo and links all over it, so replace it with one that
        # points only at what this tree holds.
        Write-Utf8 (Join-Path $linkFixture "README.md") ("# Fixture" + [char]10 + [char]10 + "[guide](./docs/guide.md)" + [char]10)
        Write-Utf8 (Join-Path $linkFixture "docs/guide.md") ("# Guide" + [char]10 + [char]10 + "[back](../README.md)" + [char]10)
        $linkInit = Invoke-Git -Path $linkFixture -Arguments @("init", "--quiet")
        if ($linkInit.ExitCode -ne 0) { throw "fixture setup failed: git init for doc-links" }
        $linkAdd = Invoke-Git -Path $linkFixture -Arguments @("add", "-A")
        if ($linkAdd.ExitCode -ne 0) { throw "fixture setup failed: git add -A for doc-links" }

        $linkClean = Invoke-Gate -Fixture $linkFixture -Only "doc-links"
        if ($linkClean.ExitCode -ne 0) {
            Write-Host "FAIL  doc-links gate accepts a tree whose links all resolve"
            Write-Host $linkClean.Output
            $script:failures++
        }
        else {
            Write-Host "PASS  doc-links gate accepts a tree whose links all resolve"
        }

        # Links to targets that are on disk but that git does not track. This is not hypothetical:
        # the gate first shipped asking the filesystem, passed locally and failed on CI, because
        # AGENTS.md and extraction/decompiled/ are both gitignored and both present on a developer's
        # machine. A link only works for the reader who clones, so tracked is the question.
        $untrackedTarget = Join-Path $linkFixture "docs/fixture-untracked-target.md"
        Write-Utf8 $untrackedTarget ("# Present but untracked" + [char]10)
        $untrackedDirectory = Join-Path $linkFixture "docs/fixture-untracked-directory"
        New-Item -ItemType Directory -Path $untrackedDirectory | Out-Null
        Write-Utf8 (Join-Path $untrackedDirectory "README.md") ("# Present but untracked" + [char]10)
        $ignoreFile = Join-Path $linkFixture ".gitignore"
        Write-Utf8 $ignoreFile ("docs/fixture-untracked-target.md" + [char]10 + "docs/fixture-untracked-directory/" + [char]10)
        Write-Utf8 (Join-Path $linkFixture "docs/guide.md") ("# Guide" + [char]10 + [char]10 + "[back](../README.md)" + [char]10 + [char]10 + "[present but untracked](./fixture-untracked-target.md)" + [char]10 + [char]10 + "[directory present but untracked](./fixture-untracked-directory/)" + [char]10)
        $linkAddUntracked = Invoke-Git -Path $linkFixture -Arguments @("add", "-A")
        if ($linkAddUntracked.ExitCode -ne 0) { throw "fixture setup failed: git add -A before the untracked-target case" }
        $linkUntracked = Invoke-Gate -Fixture $linkFixture -Only "doc-links"
        if ($linkUntracked.ExitCode -eq 0) {
            Write-Host "FAIL  doc-links gate rejects a link to a file git does not track (gate accepted a drifted input)"
            $script:failures++
        }
        elseif (((($linkUntracked.Output -join "") -replace "[\s]", "")) -notmatch "fixture-untracked-target.md") {
            Write-Host "FAIL  doc-links gate names the untracked target"
            Write-Host $linkUntracked.Output
            $script:failures++
        }
        elseif (((($linkUntracked.Output -join "") -replace "[\s]", "")) -notmatch "fixture-untracked-directory/") {
            Write-Host "FAIL  doc-links gate names the untracked directory"
            Write-Host $linkUntracked.Output
            $script:failures++
        }
        else {
            Write-Host "PASS  doc-links gate rejects a link to a file git does not track"
        }
        Remove-Item -LiteralPath $untrackedTarget -Force
        Remove-Item -LiteralPath $untrackedDirectory -Recurse -Force
        Remove-Item -LiteralPath $ignoreFile -Force

        # A #L anchor past the end of the file it points at. This is the quiet half of link rot:
        # the link still opens, it just lands somewhere else, so nothing looks wrong. Six anchors
        # into client.py and server.py went stale the moment those modules were split, and the
        # only reason anyone noticed was that someone went looking.
        Write-Utf8 (Join-Path $linkFixture "docs/guide.md") ("# Guide" + [char]10 + [char]10 + "[back](../README.md)" + [char]10 + [char]10 + "[past the end](../README.md#L99999)" + [char]10)
        $linkAddAnchor = Invoke-Git -Path $linkFixture -Arguments @("add", "-A")
        if ($linkAddAnchor.ExitCode -ne 0) { throw "fixture setup failed: git add -A before the line-anchor case" }
        $linkAnchor = Invoke-Gate -Fixture $linkFixture -Only "doc-links"
        if ($linkAnchor.ExitCode -eq 0) {
            Write-Host "FAIL  doc-links gate rejects a line anchor past the end of the file (gate accepted a drifted input)"
            $script:failures++
        }
        elseif (((($linkAnchor.Output -join "") -replace "[\s]", "")) -notmatch "L99999") {
            Write-Host "FAIL  doc-links gate names the dangling line anchor"
            Write-Host $linkAnchor.Output
            $script:failures++
        }
        else {
            Write-Host "PASS  doc-links gate rejects a line anchor past the end of the file"
        }

        # A #L anchor inside the file is no safer, only quieter: it lands on whatever line 1 is
        # today. When the specs' 41 anchors were replaced, one named build_parser and pointed into
        # another function, and three test links landed on blank lines -- all of them in range.
        Write-Utf8 (Join-Path $linkFixture "docs/guide.md") ("# Guide" + [char]10 + [char]10 + "[back](../README.md)" + [char]10 + [char]10 + "[in range](../README.md#L1)" + [char]10)
        $linkAddInRange = Invoke-Git -Path $linkFixture -Arguments @("add", "-A")
        if ($linkAddInRange.ExitCode -ne 0) { throw "fixture setup failed: git add -A before the in-range line-anchor case" }
        $linkInRange = Invoke-Gate -Fixture $linkFixture -Only "doc-links"
        if ($linkInRange.ExitCode -eq 0) {
            Write-Host "FAIL  doc-links gate rejects a line anchor inside the file (gate accepted a line anchor)"
            $script:failures++
        }
        elseif (((($linkInRange.Output -join "") -replace "[\s]", "")) -notmatch "README\.md#L1") {
            Write-Host "FAIL  doc-links gate names the in-range line anchor"
            Write-Host $linkInRange.Output
            $script:failures++
        }
        else {
            Write-Host "PASS  doc-links gate rejects a line anchor inside the file"
        }

        Write-Utf8 (Join-Path $linkFixture "docs/guide.md") ("# Guide" + [char]10 + [char]10 + "[back](../README.md)" + [char]10 + [char]10 + "[moved](./fixture-no-such-page.md)" + [char]10)
        $linkAdd2 = Invoke-Git -Path $linkFixture -Arguments @("add", "-A")
        if ($linkAdd2.ExitCode -ne 0) { throw "fixture setup failed: git add -A after the doc-links mutation" }
        $linkBroken = Invoke-Gate -Fixture $linkFixture -Only "doc-links"
        if ($linkBroken.ExitCode -eq 0) {
            Write-Host "FAIL  doc-links gate rejects a link to a file that is not there (gate accepted a drifted input)"
            $script:failures++
        }
        elseif ((($linkBroken.Output -join "") -replace "[\s]", "") -notmatch "fixture-no-such-page.md") {
            Write-Host "FAIL  doc-links gate names the dangling target"
            Write-Host $linkBroken.Output
            $script:failures++
        }
        else {
            Write-Host "PASS  doc-links gate rejects a link to a file that is not there"
            $linkMessage = ($linkBroken.Output -split "?
" | Where-Object { $_ -match "verification gates failed" } | Select-Object -First 1)
            Write-Host ("      " + $linkMessage.Trim())
        }
    }
    finally {
        Remove-Fixture -Path $linkFixture
    }

    $restored = Invoke-Gate -Fixture $fixture -Only $null -Skip "doc-links"
    if ($restored.ExitCode -ne 0) {
        Write-Host "FAIL  restored fixture (every mutation must be reverted)"
        Write-Host $restored.Output
        $failures++
    }
    else {
        Write-Host "PASS  restored"
    }
}
finally {
    Remove-Fixture $fixture
}

if ($failures -gt 0) {
    Write-Host ""
    Write-Host "verification gate self-test failed: $failures case(s)"
    exit 1
}

Write-Host ""
Write-Host "verification gate self-test passed"
exit 0
