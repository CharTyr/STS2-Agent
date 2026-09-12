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

function Invoke-Gate([string]$Fixture, [string]$Only) {
    $arguments = @("--repo-root", $Fixture)
    if ($Only) {
        $arguments += @("--only", $Only)
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

function Assert-Case([string]$Name, [string]$Only, [scriptblock]$Mutate) {
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

    Write-Host "PASS  $Name"
    Write-Host "      $($message.Trim())"
}

try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture "scripts") | Out-Null
    $fixtureScript = Join-Path $fixture "scripts/check_verification_gates.py"
    Copy-Item -LiteralPath $gateScript -Destination $fixtureScript
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

    $fixtureAction = Join-Path $fixture "STS2AIAgent/Game"
    New-Item -ItemType Directory -Path $fixtureAction -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Game/GameActionService.cs") -Destination $fixtureAction
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Game/GameStateService.cs") -Destination $fixtureAction

    $fixtureServerSource = Join-Path $fixture "STS2AIAgent/Server"
    New-Item -ItemType Directory -Path $fixtureServerSource -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Server/HttpServer.cs") -Destination $fixtureServerSource

    $sourceDocs = Join-Path $repoRoot "docs"
    $fixtureDocs = Join-Path $fixture "docs"
    Get-ChildItem -Path $sourceDocs -Recurse -File -Filter *.md | ForEach-Object {
        $relative = $_.FullName.Substring($sourceDocs.Length).TrimStart([char]92, [char]47)
        $destination = Join-Path $fixtureDocs $relative
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination
    }

    $baseline = Invoke-Gate -Fixture $fixture -Only $null
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
    Assert-Case -Name "api-doc drift rejects undocumented action" -Only "api-doc"
    Write-Utf8 $apiDoc $original

    # 2. A documented action the code does not accept.
    # Build the backticks with [char]96: PowerShell would treat a literal backtick as an escape.
    # Keep this fixture ASCII: Windows PowerShell 5.1 reads a BOM-less script with the machine's
    # ANSI code page, where the UTF-8 bytes of a dash decode to a smart quote that ends the string.
    $phantom = "- " + [char]96 + "totally_made_up_action" + [char]96 + " - not real"
    $mutated = $original.Replace("<!-- END ACTION CONTRACT -->", $phantom + [char]10 + "<!-- END ACTION CONTRACT -->")
    if ($mutated -eq $original) { throw "fixture setup failed: the action contract end marker was not found in docs/api.md" }
    Write-Utf8 $apiDoc $mutated
    Assert-Case -Name "api-doc drift rejects phantom action" -Only "api-doc"
    Write-Utf8 $apiDoc $original

    # 3. A lockfile below the security floor.
    $uvLock = Join-Path $fixtureMcp "uv.lock"
    $originalLock = Read-Utf8 $uvLock
    $mutated = $originalLock -replace '(?m)^(name = "fastmcp"\r?\nversion = ")[^"]+(")', '${1}3.1.0${2}'
    if ($mutated -eq $originalLock) { throw "fixture setup failed: could not rewrite the fastmcp version in uv.lock" }
    Write-Utf8 $uvLock $mutated
    Assert-Case -Name "lockfile gate rejects a version below the security floor" -Only "lockfile"
    Write-Utf8 $uvLock $originalLock

    # 3b. A manifest that demands more than the lock resolves (the lock is stale, not unsafe).
    $pyproject = Join-Path $fixtureMcp "pyproject.toml"
    $originalPyproject = Read-Utf8 $pyproject
    $mutated = $originalPyproject -replace 'fastmcp>=3\.1\.0,<4\.0\.0', 'fastmcp>=9.0.0,<10.0.0'
    if ($mutated -eq $originalPyproject) { throw "fixture setup failed: could not rewrite the fastmcp range in pyproject.toml" }
    Write-Utf8 $pyproject $mutated
    Assert-Case -Name "lockfile gate rejects a stale uv.lock against its manifest" -Only "lockfile"
    Write-Utf8 $pyproject $originalPyproject

    # 3c. An npm manifest that demands a version the lock cannot satisfy.
    $npmManifest = Join-Path $fixture "package.json"
    $originalNpmManifest = Read-Utf8 $npmManifest
    $mutated = $originalNpmManifest -replace '"@sammysnake/fast-context-mcp": "\^1\.2\.0"', '"@sammysnake/fast-context-mcp": "^99.0.0"'
    if ($mutated -eq $originalNpmManifest) { throw "fixture setup failed: could not rewrite the npm dependency range" }
    Write-Utf8 $npmManifest $mutated
    Assert-Case -Name "lockfile gate rejects a stale package-lock against its manifest" -Only "lockfile"
    Write-Utf8 $npmManifest $originalNpmManifest

    # 4. A date-stamped record without its historical marker.
    $datedDoc = Join-Path $fixtureDocs "phase-8-validation-2026-03-11.md"
    $originalDated = Read-Utf8 $datedDoc
    $mutated = ($originalDated -split "\r?\n" | Where-Object { $_ -notmatch 'Historical snapshot|历史快照' }) -join "`n"
    Write-Utf8 $datedDoc $mutated
    Assert-Case -Name "doc-marks gate rejects an unmarked snapshot" -Only "doc-marks"
    Write-Utf8 $datedDoc $originalDated

    $matrixDoc = Join-Path $fixtureDocs "mechanic-coverage-matrix.md"
    $originalMatrix = Read-Utf8 $matrixDoc
    $mutated = ($originalMatrix -split "\r?\n" | Where-Object { $_ -notmatch 'Historical snapshot|历史快照' }) -join "`n"
    if ($mutated -eq $originalMatrix) { throw "fixture setup failed: the matrix header holds no marker to remove" }
    Write-Utf8 $matrixDoc $mutated
    Assert-Case -Name "doc-marks gate rejects an unmarked date-less snapshot" -Only "doc-marks"
    Write-Utf8 $matrixDoc $originalMatrix

   # 5. An archived topic page that lost its redirect.
    $redirectDoc = Join-Path $fixtureDocs "sts2-coverage-gaps.md"
    $originalRedirect = Read-Utf8 $redirectDoc
    $mutated = $originalRedirect -replace 'history/sts2-coverage-gaps_2026-03-10.md', 'somewhere-else.md'
    Write-Utf8 $redirectDoc $mutated
    Assert-Case -Name "doc-marks gate rejects a broken archive redirect" -Only "doc-marks"
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
    Assert-Case -Name "script-encoding gate rejects non-ASCII without a BOM" -Only "script-encoding"
    Remove-Item -LiteralPath $encodingScript -Force

    # 7. The mod version documented in docs/api.md drifting away from the manifest.
    $factsDoc = Join-Path $fixtureDocs "api.md"
    $originalFactsDoc = Read-Utf8 $factsDoc
    $mutated = $originalFactsDoc -replace '"mod_version": "[^"]+"', '"mod_version": "0.0.1"'
    if ($mutated -eq $originalFactsDoc) { throw "fixture setup failed: docs/api.md has no mod_version value to rewrite" }
    Write-Utf8 $factsDoc $mutated
    Assert-Case -Name "api-facts gate rejects a stale documented mod_version" -Only "api-facts"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 8. A screen the code can emit but the docs enum no longer lists.
    $mutated = ($originalFactsDoc -split "\r?\n" | Where-Object { $_ -notmatch ('^\| ' + [char]96 + 'CARDS_VIEW' + [char]96) }) -join [char]10
    if ($mutated -eq $originalFactsDoc) { throw "fixture setup failed: docs/api.md has no CARDS_VIEW screen row" }
    Write-Utf8 $factsDoc $mutated
    Assert-Case -Name "api-facts gate rejects a screen missing from the docs enum" -Only "api-facts"
    Write-Utf8 $factsDoc $originalFactsDoc

    # 9. The documented default port drifting away from HttpServer.DefaultPort.
    $httpServer = Join-Path $fixture "STS2AIAgent/Server/HttpServer.cs"
    $originalHttpServer = Read-Utf8 $httpServer
    $mutated = $originalHttpServer -replace 'const int DefaultPort = \d+', 'const int DefaultPort = 9999'
    if ($mutated -eq $originalHttpServer) { throw "fixture setup failed: HttpServer.cs has no DefaultPort constant to rewrite" }
    Write-Utf8 $httpServer $mutated
    Assert-Case -Name "api-facts gate rejects a default port the docs do not state" -Only "api-facts"
    Write-Utf8 $httpServer $originalHttpServer

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
    Assert-Case -Name "docs-tracked gate rejects a docs file git does not track" -Only "docs-tracked"
    Remove-Item -LiteralPath $untrackedDoc -Force

    $restored = Invoke-Gate -Fixture $fixture -Only $null
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
