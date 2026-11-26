[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ModDll,
    [string]$GameRoot,
    [string]$DataDir,
    [switch]$Json
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-sts2-paths.ps1")

$resolvedDataDir = Resolve-Sts2DataDir -DataDir $DataDir -GameRoot $GameRoot
if (-not $resolvedDataDir -or -not (Test-Path (Join-Path $resolvedDataDir "sts2.dll"))) {
    throw "Could not find sts2.dll. Pass -DataDir or set STS2_DATA_DIR."
}

$resolvedModDll = if ($ModDll) {
    $ModDll
} else {
    Join-Path $RepoRoot "STS2AIAgent\bin\$Configuration\net9.0\STS2AIAgent.dll"
}

if (-not (Test-Path $resolvedModDll)) {
    Write-Host "[check-game-abi] Building STS2AIAgent ($Configuration) against $resolvedDataDir ..."
    & dotnet build (Join-Path $RepoRoot "STS2AIAgent\STS2AIAgent.csproj") -c $Configuration "-p:Sts2DataDir=$resolvedDataDir" --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$testDll = Join-Path $RepoRoot "STS2AIAgent.Tests\bin\$Configuration\net9.0\STS2AIAgent.Tests.dll"
if (-not (Test-Path $testDll)) {
    & dotnet build (Join-Path $RepoRoot "STS2AIAgent.Tests\STS2AIAgent.Tests.csproj") -c $Configuration --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$cliArgs = @($testDll, "--check-abi", $resolvedModDll, $resolvedDataDir)
if ($Json) { $cliArgs += "--json" }

& dotnet @cliArgs
exit $LASTEXITCODE
