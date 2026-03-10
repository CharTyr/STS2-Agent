param(
    [string]$ProjectRoot = "C:/Users/chart/Documents/project/sp",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

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
$releaseDoc = Join-Path $ProjectRoot "docs/release-readiness.md"
$requiredDocs = @(
    (Join-Path $ProjectRoot "docs/api.md"),
    (Join-Path $ProjectRoot "docs/roadmap-current.md"),
    (Join-Path $ProjectRoot "docs/phase-4c-shop.md"),
    (Join-Path $ProjectRoot "docs/phase-5-full-chain.md"),
    (Join-Path $ProjectRoot "docs/release-readiness.md")
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
Write-Host "  3. Follow the manual checklist in `"$releaseDoc`""
