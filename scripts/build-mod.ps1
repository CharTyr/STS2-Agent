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

function Get-FirstExistingPath {
    param([string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $matches = @(Get-ChildItem -Path $candidate -File -ErrorAction SilentlyContinue | Sort-Object FullName)
        if ($matches.Count -gt 0) {
            $consoleMatch = @($matches | Where-Object { $_.Name -like "*console*" } | Select-Object -First 1)
            if ($consoleMatch.Count -gt 0) {
                return $consoleMatch[0].FullName
            }

            return $matches[0].FullName
        }
    }

    return $null
}

function Resolve-GodotExe {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return $ExplicitPath
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GODOT_BIN)) {
        return $env:GODOT_BIN
    }

    $command = Get-Command "Godot*" -CommandType Application -ErrorAction SilentlyContinue |
        Sort-Object @{ Expression = { if ($_.Name -like "*console*") { 0 } else { 1 } } }, Name |
        Select-Object -First 1

    if ($command) {
        if (-not [string]::IsNullOrWhiteSpace($command.Source)) {
            return $command.Source
        }

        if (-not [string]::IsNullOrWhiteSpace($command.Path)) {
            return $command.Path
        }

        if (-not [string]::IsNullOrWhiteSpace($command.Definition)) {
            return $command.Definition
        }
    }

    return Get-FirstExistingPath -Candidates @(
        (Join-Path $env:LOCALAPPDATA "Microsoft/WinGet/Links/Godot*.exe"),
        (Join-Path $env:LOCALAPPDATA "Microsoft/WinGet/Packages/GodotEngine.GodotEngine_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot*.exe"),
        (Join-Path ${env:ProgramFiles} "Godot*/Godot*.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Godot*/Godot*.exe")
    )
}

$ProjectRoot = Resolve-ProjectRoot -InputRoot $ProjectRoot

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = Resolve-GodotExe -ExplicitPath $GodotExe
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
$modIdManifestSource = Join-Path $ProjectRoot "STS2AIAgent/mod_id.json"
$dllSource = Join-Path $buildOutputDir "$modName.dll"
$pckOutput = Join-Path $stagingDir "$modName.pck"
$dllTarget = Join-Path $stagingDir "$modName.dll"
$modIdManifestTarget = Join-Path $stagingDir "mod_id.json"
$modIdManifestTargetForName = Join-Path $stagingDir "$modName.json"
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
if (-not (Test-Path $modIdManifestSource)) {
    throw "Mod ID manifest not found: $modIdManifestSource"
}

Write-Host "[build-mod] Packing mod_manifest.json into PCK..."
& $GodotExe --headless --path $builderProjectDir --script $builderScript -- $manifestSource $pckOutput | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Godot PCK build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $pckOutput)) {
    throw "PCK output not found: $pckOutput"
}
Copy-Item -Force $modIdManifestSource $modIdManifestTarget
Copy-Item -Force $modIdManifestSource $modIdManifestTargetForName

Write-Host "[build-mod] Preparing game mods directory..."
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
Copy-Item -Force $dllTarget (Join-Path $modsDir "$modName.dll")
Copy-Item -Force $pckOutput (Join-Path $modsDir "$modName.pck")
Copy-Item -Force $modIdManifestTarget (Join-Path $modsDir "mod_id.json")
Copy-Item -Force $modIdManifestTargetForName (Join-Path $modsDir "$modName.json")

Write-Host "[build-mod] Done."
Write-Host "[build-mod] Using Godot: $GodotExe"
Write-Host "[build-mod] Installed files:"
Write-Host "  $(Join-Path $modsDir "$modName.dll")"
Write-Host "  $(Join-Path $modsDir "$modName.pck")"
