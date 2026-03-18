param(
    [string]$Configuration = "Debug",
    [string]$ProjectRoot = "",
    [string]$GameRoot = "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2",
    [string]$GodotExe = ""
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

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = $env:GODOT_BIN
}

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    throw "Godot executable not found. Pass -GodotExe or set the GODOT_BIN environment variable."
}

if (-not (Test-Path $GodotExe)) {
    throw "Godot executable not found: $GodotExe"
}

$modName = "STS2AIAgent"
$modProject = Join-Path $ProjectRoot "STS2AIAgent/STS2AIAgent.csproj"
$buildOutputDir = Join-Path $ProjectRoot "STS2AIAgent/bin/$Configuration/net9.0"
$stagingDir = Join-Path $ProjectRoot "build/mods/$modName"
$modsDir = Join-Path $GameRoot "mods"
$manifestSource = Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"
$dllSource = Join-Path $buildOutputDir "$modName.dll"
$pckOutput = Join-Path $stagingDir "$modName.pck"
$dllTarget = Join-Path $stagingDir "$modName.dll"
$builderProjectDir = Join-Path $ProjectRoot "tools/pck_builder"
$builderScript = Join-Path $builderProjectDir "build_pck.gd"

Write-Host "[build-mod] Building C# mod project..."
dotnet build $modProject -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $dllSource)) {
    throw "Built DLL not found: $dllSource"
}

New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null
Copy-Item -Force $dllSource $dllTarget

if (-not (Test-Path $manifestSource)) {
    throw "Manifest not found: $manifestSource"
}

Write-Host "[build-mod] Packing mod_manifest.json into PCK..."
& $GodotExe --headless --path $builderProjectDir --script $builderScript -- $manifestSource $pckOutput | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Godot PCK build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $pckOutput)) {
    throw "PCK output not found: $pckOutput"
}

Write-Host "[build-mod] Preparing game mods directory..."
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
Copy-Item -Force $dllTarget (Join-Path $modsDir "$modName.dll")
Copy-Item -Force $pckOutput (Join-Path $modsDir "$modName.pck")

Write-Host "[build-mod] Done."
Write-Host "[build-mod] Installed files:"
Write-Host "  $(Join-Path $modsDir "$modName.dll")"
Write-Host "  $(Join-Path $modsDir "$modName.pck")"
