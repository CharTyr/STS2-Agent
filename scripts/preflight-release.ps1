param(
    [string]$ProjectRoot = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

function Resolve-ProjectRoot {
    param([string]$InputRoot)

    if ([string]::IsNullOrWhiteSpace($InputRoot)) {
        return (Resolve-Path (Join-Path $scriptRoot "..")).Path
    }

    return (Resolve-Path $InputRoot).Path
}

$ProjectRoot = Resolve-ProjectRoot -InputRoot $ProjectRoot

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "[preflight] $Name"
    & $Action
    Write-Host "[preflight] OK - $Name"
}

$modProject = Join-Path $ProjectRoot "STS2AIAgent/STS2AIAgent.csproj"
$mcpRoot = Join-Path $ProjectRoot "mcp_server"
$clientPy = Join-Path $mcpRoot "src/sts2_mcp/client.py"
$serverPy = Join-Path $mcpRoot "src/sts2_mcp/server.py"
$buildScript = Join-Path $ProjectRoot "scripts/build-mod.ps1"
$testScript = Join-Path $ProjectRoot "scripts/test-mod-load.ps1"
$stateInvariantScript = Join-Path $ProjectRoot "scripts/test-state-invariants.ps1"
$mcpToolProfileScript = Join-Path $ProjectRoot "scripts/test-mcp-tool-profile.ps1"
$releaseDoc = Join-Path $ProjectRoot "docs/release-readiness.md"
$requiredDocs = @(
    (Join-Path $ProjectRoot "docs/api.md"),
    (Join-Path $ProjectRoot "docs/roadmap-current.md"),
    (Join-Path $ProjectRoot "docs/phase-4c-shop.md"),
    (Join-Path $ProjectRoot "docs/phase-5-full-chain.md"),
    (Join-Path $ProjectRoot "docs/phase-6-validation-template.md"),
    (Join-Path $ProjectRoot "docs/release-readiness.md"),
    (Join-Path $ProjectRoot "docs/mechanic-coverage-matrix.md")
)

Invoke-Step -Name "Build mod project ($Configuration)" -Action {
    dotnet build $modProject -c $Configuration | Out-Host
}

Invoke-Step -Name "Compile Python sources" -Action {
    python -m py_compile $clientPy $serverPy
}

Invoke-Step -Name "Import MCP server package" -Action {
    Push-Location $mcpRoot
    try {
        uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')" | Out-Host
    }
    finally {
        Pop-Location
    }
}

Invoke-Step -Name "Validate MCP tool profiles" -Action {
    powershell -ExecutionPolicy Bypass -File $mcpToolProfileScript -RepoRoot $ProjectRoot | Out-Host
}

Invoke-Step -Name "Check release documents" -Action {
    $missing = $requiredDocs | Where-Object { -not (Test-Path $_) }

    if ($missing.Count -gt 0) {
        throw "Missing release docs: $($missing -join ', ')"
    }

    foreach ($doc in $requiredDocs) {
        Write-Host "  - $doc"
    }
}

Write-Host ""
Write-Host "[preflight] Static preflight complete."
Write-Host "[preflight] Manual validation next:"
Write-Host "  1. powershell -ExecutionPolicy Bypass -File `"$buildScript`" -Configuration $Configuration"
Write-Host "  2. powershell -ExecutionPolicy Bypass -File `"$testScript`" -DeepCheck"
Write-Host "  3. powershell -ExecutionPolicy Bypass -File `"$stateInvariantScript`""
Write-Host "  4. powershell -ExecutionPolicy Bypass -File `"$mcpToolProfileScript`" -RepoRoot `"$ProjectRoot`""
Write-Host "  5. Follow the manual checklist in `"$releaseDoc`""
