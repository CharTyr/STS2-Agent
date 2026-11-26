<#
.SYNOPSIS
    Packs the game assemblies and reference metadata needed to develop and build STS2AIAgent on a
    cloud machine without copying the full Slay the Spire 2 installation.

.DESCRIPTION
    `dotnet build STS2AIAgent` needs three assemblies from the game's data directory:
    sts2.dll, GodotSharp.dll and 0Harmony.dll. The rest of an installation -- the 2 GB pck, the
    900 MB executable, the native runtime -- is only needed to run the game.

    By default this script also packs three lightweight reference files from the local game
    installation when present:
      - sts2.xml                API XML docs for IDEs, AI coding agents and decompilers (~1 MB zipped)
      - release_info.json       exact game version, commit, branch and assembly hash (~200 B)
      - sts2.runtimeconfig.json exact bundled .NET runtime metadata (~400 B)
    Pass -Minimal to pack only the three required DLLs.

    The archive carries a manifest.json with per-file SHA-256 checksums plus the game's exact
    GodotSharp / Harmony / .NET runtime versions so scripts/unpack-game-assemblies.sh and
    scripts/setup-cloud-dev.sh --with-godot can verify and match the local game accurately.

    IMPORTANT: The output archive contains proprietary Slay the Spire 2 assemblies. Keep it for
    your own development machines only (the default build/ output is gitignored); never commit it
    or attach it to a public release.

.PARAMETER DataDir
    The directory holding sts2.dll. Defaults to STS2_DATA_DIR, then detection via lib-sts2-paths.ps1.

.PARAMETER GameRoot
    The game installation root. Defaults to STS2_GAME_ROOT, then detection via lib-sts2-paths.ps1.

.PARAMETER OutputDir
    Where the archive is written. Defaults to build/game-assemblies under the repository root.

.PARAMETER Channel
    Optional channel label (for example 'stable' or 'beta') stamped into manifest.json and the
    archive file name when managing multiple game versions side by side.

.PARAMETER Minimal
    Pack only the three required DLLs (skip sts2.xml and runtime metadata).

.PARAMETER IncludeXml
    Kept for backwards compatibility; sts2.xml is already included unless -Minimal is passed.

.PARAMETER Force
    Overwrite an existing archive.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/pack-game-assemblies.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/pack-game-assemblies.ps1 -Channel beta -Force
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/pack-game-assemblies.ps1 -Minimal -Force
#>
[CmdletBinding()]
param(
    [string]$DataDir = "",
    [string]$GameRoot = "",
    [string]$OutputDir = "",
    [string]$Channel = "",
    [switch]$Minimal,
    [switch]$IncludeXml,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot 'lib-sts2-paths.ps1')

$repoRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'build/game-assemblies'
}

function Resolve-PackDataDir {
    param(
        [string]$ExplicitDataDir,
        [string]$ExplicitGameRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitDataDir)) {
        if (Test-Path -LiteralPath $ExplicitDataDir -PathType Container) {
            return (Resolve-Path -LiteralPath $ExplicitDataDir).Path
        }
        return $null
    }

    $candidate = Resolve-Sts2DataDir -Explicit $ExplicitDataDir -GameRoot $ExplicitGameRoot
    if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Container)) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    $root = if (-not [string]::IsNullOrWhiteSpace($ExplicitGameRoot)) {
        $ExplicitGameRoot
    } else {
        Resolve-Sts2GameRoot
    }
    if ($root -and (Test-Path -LiteralPath $root -PathType Container)) {
        foreach ($dir in Get-ChildItem -LiteralPath $root -Directory -Filter 'data_sts2_*' -ErrorAction SilentlyContinue) {
            if (Test-Path -LiteralPath (Join-Path $dir.FullName 'sts2.dll') -PathType Leaf) {
                return $dir.FullName
            }
        }
    }

    return $null
}

function Get-AssemblyVersionTag {
    param([string]$DllPath)

    if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) {
        return $null
    }
    try {
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($DllPath)
        if (-not [string]::IsNullOrWhiteSpace($info.ProductVersion)) {
            return ($info.ProductVersion -split '\+', 2)[0]
        }
        if (-not [string]::IsNullOrWhiteSpace($info.FileVersion)) {
            return $info.FileVersion
        }
    } catch {
    }
    return $null
}

$resolvedDataDir = Resolve-PackDataDir -ExplicitDataDir $DataDir -ExplicitGameRoot $GameRoot
if (-not $resolvedDataDir) {
    Write-Error @'
Could not find the game data directory (the one holding sts2.dll).
Pass -DataDir PATH, -GameRoot PATH, set STS2_DATA_DIR / STS2_GAME_ROOT, or install the game through Steam.
'@
    exit 1
}

$required = @('sts2.dll', 'GodotSharp.dll', '0Harmony.dll')
$missing = @()
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedDataDir $name) -PathType Leaf)) {
        $missing += $name
    }
}
if ($missing.Count -gt 0) {
    Write-Error ("The data directory is missing: " + ($missing -join ', ') + "`n  " + $resolvedDataDir)
    exit 1
}

$gameRootGuess = if (-not [string]::IsNullOrWhiteSpace($GameRoot) -and (Test-Path -LiteralPath $GameRoot -PathType Container)) {
    (Resolve-Path -LiteralPath $GameRoot).Path
} else {
    Split-Path -Parent $resolvedDataDir
}

$releaseInfoPath = Join-Path $gameRootGuess 'release_info.json'
$releaseInfoObj = $null
$version = 'unknown'
$gameCommit = $null
$gameBranch = $null
$mainAssemblyHash = $null
if (Test-Path -LiteralPath $releaseInfoPath -PathType Leaf) {
    try {
        $releaseInfoObj = Get-Content -LiteralPath $releaseInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($releaseInfoObj.version) { $version = [string]$releaseInfoObj.version }
        if ($releaseInfoObj.commit) { $gameCommit = [string]$releaseInfoObj.commit }
        if ($releaseInfoObj.branch) { $gameBranch = [string]$releaseInfoObj.branch }
        if ($null -ne $releaseInfoObj.main_assembly_hash) { $mainAssemblyHash = $releaseInfoObj.main_assembly_hash }
    } catch {
        Write-Warning "release_info.json could not be read; the archive name will say 'unknown'."
    }
}

$runtimeConfigPath = Join-Path $resolvedDataDir 'sts2.runtimeconfig.json'
$runtimeFrameworkVersion = $null
if (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf) {
    try {
        $rt = Get-Content -LiteralPath $runtimeConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($rt.runtimeOptions.includedFrameworks -and $rt.runtimeOptions.includedFrameworks.Count -gt 0) {
            $runtimeFrameworkVersion = [string]$rt.runtimeOptions.includedFrameworks[0].version
        }
    } catch {
    }
}

$godotVersion = Get-AssemblyVersionTag -DllPath (Join-Path $resolvedDataDir 'GodotSharp.dll')
$harmonyVersion = Get-AssemblyVersionTag -DllPath (Join-Path $resolvedDataDir '0Harmony.dll')

$platform = Split-Path -Leaf $resolvedDataDir
if ($platform -like 'data_sts2_*') { $platform = $platform.Substring('data_sts2_'.Length) }
if (-not $platform) { $platform = 'unknown' }

$channelToken = ''
if (-not [string]::IsNullOrWhiteSpace($Channel)) {
    $sanitized = ($Channel.Trim().ToLowerInvariant() -replace '[^a-z0-9._-]', '-')
    if (-not [string]::IsNullOrWhiteSpace($sanitized)) {
        $channelToken = '-' + $sanitized
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$archiveName = "sts2-assemblies-$version$channelToken-$platform.zip"
$archivePath = Join-Path $OutputDir $archiveName
if ((Test-Path -LiteralPath $archivePath) -and -not $Force) {
    Write-Error "Archive already exists: $archivePath`nPass -Force to replace it."
    exit 1
}

$packXml = (-not $Minimal) -or $IncludeXml
$packMetadata = (-not $Minimal)

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('sts2-assemblies-' + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $entries = @()
    $plan = New-Object System.Collections.Generic.List[object]

    foreach ($name in $required) {
        $plan.Add([pscustomobject]@{
            name   = $name
            source = (Join-Path $resolvedDataDir $name)
            role   = 'assembly'
        })
    }

    if ($packXml) {
        $xmlSource = Join-Path $resolvedDataDir 'sts2.xml'
        if (Test-Path -LiteralPath $xmlSource -PathType Leaf) {
            $plan.Add([pscustomobject]@{
                name   = 'sts2.xml'
                source = $xmlSource
                role   = 'xml_doc'
            })
        }
    }

    if ($packMetadata) {
        if (Test-Path -LiteralPath $releaseInfoPath -PathType Leaf) {
            $plan.Add([pscustomobject]@{
                name   = 'release_info.json'
                source = $releaseInfoPath
                role   = 'metadata'
            })
        }
        if (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf) {
            $plan.Add([pscustomobject]@{
                name   = 'sts2.runtimeconfig.json'
                source = $runtimeConfigPath
                role   = 'metadata'
            })
        }
    }

    foreach ($item in $plan) {
        $target = Join-Path $staging $item.name
        Copy-Item -LiteralPath $item.source -Destination $target -Force
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        $entries += [ordered]@{
            name   = $item.name
            role   = $item.role
            bytes  = (Get-Item -LiteralPath $target).Length
            sha256 = $hash
        }
    }

    $manifest = [ordered]@{
        schemaVersion           = 2
        generatedAt             = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        gameVersion             = $version
        gameCommit              = $gameCommit
        gameBranch              = $gameBranch
        mainAssemblyHash        = $mainAssemblyHash
        channel                 = if ([string]::IsNullOrWhiteSpace($Channel)) { $null } else { $Channel.Trim() }
        platform                = $platform
        godotVersion            = $godotVersion
        harmonyVersion          = $harmonyVersion
        runtimeFrameworkVersion = $runtimeFrameworkVersion
        sourceDataDir           = $resolvedDataDir
        # What the archive is for, so a later reader does not mistake it for a runnable game.
        purpose                 = 'layer 1 of docs/cloud-dev.md: reference assemblies and metadata STS2AIAgent compiles against'
        files                   = $entries
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $staging 'manifest.json'), $manifestJson + "`n", (New-Object System.Text.UTF8Encoding $false))

    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archivePath -Force
} finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 1)
$totalBytes = 0
foreach ($entry in $entries) { $totalBytes += $entry.bytes }
$rawMb = [math]::Round($totalBytes / 1MB, 1)

Write-Host "[pack] game version:    $version ($platform)$(if ($gameCommit) { " commit=$gameCommit" } else { '' })"
if ($godotVersion -or $harmonyVersion -or $runtimeFrameworkVersion) {
    Write-Host "[pack] engine metadata: GodotSharp=$godotVersion Harmony=$harmonyVersion .NET=$runtimeFrameworkVersion"
}
Write-Host "[pack] source:          $resolvedDataDir"
Write-Host "[pack] archive:         $archivePath ($sizeMb MB, raw $rawMb MB)"
Write-Host "[pack] files:"
foreach ($entry in $entries) {
    $displaySize = if ($entry.bytes -ge 1048576) {
        "{0,6:N1} MB" -f ($entry.bytes / 1MB)
    } else {
        "{0,6:N1} KB" -f ($entry.bytes / 1KB)
    }
    Write-Host ("         {0,-24} {1}  {2,-9}  {3}" -f $entry.name, $displaySize, $entry.role, $entry.sha256.Substring(0, 16))
}
Write-Host ''
Write-Host '[pack] on the cloud machine:'
Write-Host "         bash scripts/unpack-game-assemblies.sh `"$archiveName`""
if ($Channel -eq 'stable' -or $Channel -eq 'min') {
    Write-Host "         # or register as secondary compatibility baseline (STS2_MIN_DATA_DIR):"
    Write-Host "         bash scripts/unpack-game-assemblies.sh `"$archiveName`" --as-min"
}
