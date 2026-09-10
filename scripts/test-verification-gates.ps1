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
    Copy-Item -LiteralPath (Join-Path $repoRoot "AGENTS.md") -Destination $fixture

    Copy-Item -LiteralPath (Join-Path $repoRoot "package.json") -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot "package-lock.json") -Destination $fixture

    $fixtureMcp = Join-Path $fixture "mcp_server"
    New-Item -ItemType Directory -Path $fixtureMcp | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "mcp_server/pyproject.toml") -Destination $fixtureMcp
    Copy-Item -LiteralPath (Join-Path $repoRoot "mcp_server/uv.lock") -Destination $fixtureMcp

    $fixtureAction = Join-Path $fixture "STS2AIAgent/Game"
    New-Item -ItemType Directory -Path $fixtureAction -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "STS2AIAgent/Game/GameActionService.cs") -Destination $fixtureAction

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
    $phantom = "- " + [char]96 + "totally_made_up_action" + [char]96 + " — not real"
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

    # 4. A date-stamped record without its historical marker.
    $datedDoc = Join-Path $fixtureDocs "phase-8-validation-2026-03-11.md"
    $originalDated = Read-Utf8 $datedDoc
    $mutated = ($originalDated -split "\r?\n" | Where-Object { $_ -notmatch 'Historical snapshot|历史快照' }) -join "`n"
    Write-Utf8 $datedDoc $mutated
    Assert-Case -Name "doc-marks gate rejects an unmarked snapshot" -Only "doc-marks"
    Write-Utf8 $datedDoc $originalDated

    # 5. An archived topic page that lost its redirect.
    $redirectDoc = Join-Path $fixtureDocs "sts2-coverage-gaps.md"
    $originalRedirect = Read-Utf8 $redirectDoc
    $mutated = $originalRedirect -replace 'history/sts2-coverage-gaps_2026-03-10.md', 'somewhere-else.md'
    Write-Utf8 $redirectDoc $mutated
    Assert-Case -Name "doc-marks gate rejects a broken archive redirect" -Only "doc-marks"
    Write-Utf8 $redirectDoc $originalRedirect

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
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}

if ($failures -gt 0) {
    Write-Host ""
    Write-Host "verification gate self-test failed: $failures case(s)"
    exit 1
}

Write-Host ""
Write-Host "verification gate self-test passed"
exit 0
