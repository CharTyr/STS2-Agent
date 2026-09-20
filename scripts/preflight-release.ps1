param(
    [string]$ProjectRoot = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "lib-checked-native.ps1")

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
$payloadsPy = Join-Path $mcpRoot "src/sts2_mcp/payloads.py"
# Modules whose absence the packaging or import contract would otherwise only reveal at runtime.
$requiredMcpModules = @($clientPy, $serverPy, $payloadsPy)
$buildScript = Join-Path $ProjectRoot "scripts/build-mod.ps1"
$testScript = Join-Path $ProjectRoot "scripts/test-mod-load.ps1"
$stateInvariantScript = Join-Path $ProjectRoot "scripts/test-state-invariants.ps1"
$mcpToolProfileScript = Join-Path $ProjectRoot "scripts/test-mcp-tool-profile.ps1"
$multiplayerFlowScript = Join-Path $ProjectRoot "scripts/test-multiplayer-lobby-flow.ps1"
$packageChecker = Join-Path $ProjectRoot "scripts/check_release_package.py"
$releaseMetadataChecker = Join-Path $ProjectRoot "scripts/check_release_metadata.py"
$verificationGates = Join-Path $ProjectRoot "scripts/check_verification_gates.py"
$verificationGateSelfTest = Join-Path $ProjectRoot "scripts/test-verification-gates.ps1"
$apiSchemaTest = Join-Path $ProjectRoot "scripts/test-api-schema.py"
$decisionBenchmark = Join-Path $ProjectRoot "scripts/decision_benchmark.py"
$decisionBenchmarkTest = Join-Path $ProjectRoot "scripts/test-decision-benchmark.py"
$nativeExitPropagationTest = Join-Path $ProjectRoot "scripts/test-native-exit-propagation.ps1"
$budgetProxySelfTest = Join-Path $ProjectRoot "scripts/sts2-model-budget-proxy-selftest.py"
$changelogPath = Join-Path $ProjectRoot "CHANGELOG.md"
$releaseDoc = Join-Path $ProjectRoot "docs/release-readiness.md"
$modManifestPath = Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"
$modIdManifestPath = Join-Path $ProjectRoot "STS2AIAgent/mod_id.json"
$routerPath = Join-Path $ProjectRoot "STS2AIAgent/Server/Router.cs"
$mcpProjectPath = Join-Path $mcpRoot "pyproject.toml"
$mcpLockPath = Join-Path $mcpRoot "uv.lock"
$requiredDocs = @(
    $changelogPath,
    (Join-Path $ProjectRoot "docs/api.md"),
    (Join-Path $ProjectRoot "docs/roadmap-current.md"),
    (Join-Path $ProjectRoot "docs/phase-4c-shop.md"),
    (Join-Path $ProjectRoot "docs/phase-5-full-chain.md"),
    (Join-Path $ProjectRoot "docs/phase-6-validation-template.md"),
    (Join-Path $ProjectRoot "docs/release-readiness.md"),
    (Join-Path $ProjectRoot "docs/mechanic-coverage-matrix.md")
)

Invoke-Step -Name "Build mod project ($Configuration)" -Action {
    Invoke-CheckedNative -FilePath "dotnet" -Arguments @("build", $modProject, "-c", $Configuration)
}

Invoke-Step -Name "Compile Python sources" -Action {
    # Every module, not a remembered pair: naming only client.py and server.py meant a syntax error
    # in any other module (payloads.py, envelope.py, state_views.py, ...) reached this gate only if
    # some test happened to import it. The list below stays, because the whole-package import step
    # after this one would otherwise report a missing module as a confusing ImportError.
    $missingModules = @($requiredMcpModules | Where-Object { -not (Test-Path $_) })
    if ($missingModules.Count -gt 0) {
        throw "Missing MCP module(s): $($missingModules -join ', ')"
    }

    $mcpModules = @(
        Get-ChildItem -Path (Join-Path $mcpRoot "src/sts2_mcp") -Filter "*.py" -File |
            ForEach-Object { $_.FullName }
    )
    if ($mcpModules.Count -lt 6) {
        throw "Expected at least 6 MCP modules to compile, found $($mcpModules.Count); the walk is wrong."
    }

    Invoke-CheckedNative -FilePath "python" -Arguments (@("-m", "py_compile") + $mcpModules)
}

Invoke-Step -Name "Import MCP server package" -Action {
    Push-Location $mcpRoot
    try {
        Invoke-CheckedNative -FilePath "uv" -Arguments @("run", "--locked", "python", "-c", "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')")
    }
    finally {
        Pop-Location
    }
}

Invoke-Step -Name "Validate MCP tool profiles" -Action {
    Invoke-CheckedNative -FilePath "powershell" -Arguments @("-ExecutionPolicy", "Bypass", "-File", $mcpToolProfileScript, "-RepoRoot", $ProjectRoot)
}

Invoke-Step -Name "Core unit tests" -Action {
    Invoke-CheckedNative -FilePath "dotnet" -Arguments @("run", "--project", (Join-Path $ProjectRoot "STS2AIAgent.Tests/STS2AIAgent.Tests.csproj"), "-c", $Configuration)
}

Invoke-Step -Name "MCP unit tests" -Action {
    Push-Location $mcpRoot
    try { Invoke-CheckedNative -FilePath "uv" -Arguments @("run", "--locked", "python", "-m", "unittest", "discover", "-s", "tests", "-v") }
    finally { Pop-Location }
}

Invoke-Step -Name "Check the model budget proxy (no-cost self-test)" -Action {
    Invoke-CheckedNative -FilePath "python" -Arguments @($budgetProxySelfTest)
}

Invoke-Step -Name "Validate release version metadata" -Action {
    # Run the same checker CI runs instead of a second implementation of it. The inline copy this
    # replaced had drifted: it never validated the version format (the checker requires
    # x.y.z[-suffix]), and it read the first "version =" line of pyproject rather than the
    # project table, so a malformed version could pass preflight and fail in CI.
    Invoke-CheckedNative -FilePath "python" -Arguments @($releaseMetadataChecker)
}

Invoke-Step -Name "Check release packaging source contract" -Action {
    Invoke-CheckedNative -FilePath "python" -Arguments @($packageChecker, "--source-root", $ProjectRoot)
}

Invoke-Step -Name "Run dependency, API-doc, schema, and doc-snapshot gates" -Action {
    Invoke-CheckedNative -FilePath "python" -Arguments @($verificationGates, "--repo-root", $ProjectRoot)
}

Invoke-Step -Name "Test generated API schema semantics" -Action {
    Invoke-CheckedNative -FilePath "python" -Arguments @($apiSchemaTest)
}

Invoke-Step -Name "Validate offline decision benchmark" -Action {
    Invoke-CheckedNative -FilePath "python" -Arguments @($decisionBenchmark)
    Invoke-CheckedNative -FilePath "python" -Arguments @($decisionBenchmarkTest)
}

Invoke-Step -Name "Self-test the verification gates" -Action {
    Invoke-CheckedNative -FilePath "powershell" -Arguments @("-ExecutionPolicy", "Bypass", "-File", $verificationGateSelfTest)
}

Invoke-Step -Name "Check Windows PowerShell failure propagation" -Action {
    Invoke-CheckedNative -FilePath "powershell" -Arguments @("-ExecutionPolicy", "Bypass", "-File", $nativeExitPropagationTest)
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
Write-Host "  5. powershell -ExecutionPolicy Bypass -File `"$multiplayerFlowScript`""
Write-Host "  6. Follow the manual checklist in `"$releaseDoc`""
Write-Host "  7. After packaging, inspect the real release directory or zip: python `"$packageChecker`" --artifact <release-dir-or-zip>"
