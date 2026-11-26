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

$resolvedDataDir = Resolve-Sts2DataDir -Explicit $DataDir -GameRoot $GameRoot
if ([string]::IsNullOrWhiteSpace($resolvedDataDir) -or -not (Test-Path -LiteralPath (Join-Path $resolvedDataDir "sts2.dll") -PathType Leaf)) {
    throw "Could not find sts2.dll. Pass -DataDir or set STS2_DATA_DIR."
}
$resolvedDataDir = (Resolve-Path -LiteralPath $resolvedDataDir).Path.TrimEnd('\', '/')

if (-not [string]::IsNullOrWhiteSpace($ModDll)) {
    if (-not (Test-Path -LiteralPath $ModDll -PathType Leaf)) {
        throw "Mod assembly not found: $ModDll"
    }
    $resolvedModDll = (Resolve-Path -LiteralPath $ModDll).Path
} else {
    $resolvedModDll = Join-Path $RepoRoot "STS2AIAgent\bin\$Configuration\net9.0\STS2AIAgent.dll"
    if (-not (Test-Path -LiteralPath $resolvedModDll -PathType Leaf)) {
        [Console]::Error.WriteLine("[check-game-abi] Building STS2AIAgent ($Configuration) against $resolvedDataDir ...")
        $buildOut = & dotnet build (Join-Path $RepoRoot "STS2AIAgent\STS2AIAgent.csproj") -c $Configuration "-p:Sts2DataDir=$resolvedDataDir" --nologo -v minimal 2>&1
        if ($LASTEXITCODE -ne 0) {
            $buildOut | ForEach-Object { [Console]::Error.WriteLine($_) }
            exit $LASTEXITCODE
        }
        if (-not $Json) { $buildOut | ForEach-Object { [Console]::Error.WriteLine($_) } }
    }
}

$testDll = Join-Path $RepoRoot "STS2AIAgent.Tests\bin\$Configuration\net9.0\STS2AIAgent.Tests.dll"
$testBuildOut = & dotnet build (Join-Path $RepoRoot "STS2AIAgent.Tests\STS2AIAgent.Tests.csproj") -c $Configuration --nologo -v minimal 2>&1
if ($LASTEXITCODE -ne 0) {
    $testBuildOut | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit $LASTEXITCODE
}

$cliArgs = @($testDll, "--check-abi", $resolvedModDll, $resolvedDataDir)
if ($Json) { $cliArgs += "--json" }

& dotnet @cliArgs
exit $LASTEXITCODE
