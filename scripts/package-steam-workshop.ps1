param(
    [string]$ProjectRoot = "",
    [string]$Configuration = "Release",
    [string]$OutputRoot = "",
    [string]$GodotExe = "",
    [ValidatePattern("^\d+$")]
    [string]$PublishedFileId = "0",
    [ValidateSet("public", "friends", "private", "unlisted")]
    [string]$Visibility = "private",
    [string]$ChangeNote = "Initial Workshop upload"
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

function Get-UniquePath {
    param([string]$BasePath)

    if (-not (Test-Path -LiteralPath $BasePath)) {
        return $BasePath
    }

    $index = 2
    while (Test-Path -LiteralPath "$BasePath-$index") {
        $index += 1
    }

    return "$BasePath-$index"
}

function Assert-EqualValue {
    param(
        [string]$Name,
        [string]$Expected,
        [string]$Actual
    )

    if ($Expected -ne $Actual) {
        throw "$Name mismatch. Expected '$Expected', got '$Actual'."
    }
}

$ProjectRoot = Resolve-ProjectRoot -InputRoot $ProjectRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $ProjectRoot "build/steam-workshop"
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

$manifestPath = Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"
$modIdPath = Join-Path $ProjectRoot "STS2AIAgent/mod_id.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$modId = Get-Content -LiteralPath $modIdPath -Raw | ConvertFrom-Json

Assert-EqualValue -Name "Mod ID" -Expected $manifest.id -Actual $modId.id
Assert-EqualValue -Name "Mod version" -Expected $manifest.version -Actual $modId.version
Assert-EqualValue -Name "Minimum game version" -Expected $manifest.min_game_version -Actual $modId.min_game_version

$buildScript = Join-Path $ProjectRoot "scripts/build-mod.ps1"
$buildArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", $buildScript,
    "-ProjectRoot", $ProjectRoot,
    "-Configuration", $Configuration,
    "-SkipInstall"
)
if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
    $buildArgs += @("-GodotExe", $GodotExe)
}

Write-Host "[steam-workshop] Building Workshop artifacts without installing into the game..."
powershell @buildArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "build-mod.ps1 failed with exit code $LASTEXITCODE"
}

$releaseDirectory = Get-UniquePath -BasePath (Join-Path $OutputRoot "sts2-ai-agent-v$($manifest.version)")
$contentDirectory = Join-Path $releaseDirectory "content"
$stagingDirectory = Join-Path $ProjectRoot "build/mods/$($manifest.id)"
New-Item -ItemType Directory -Force -Path $contentDirectory | Out-Null

$requiredStagedFiles = @(
    "$($manifest.id).dll",
    "$($manifest.id).pck",
    "mod_id.json"
)
foreach ($fileName in $requiredStagedFiles) {
    $sourcePath = Join-Path $stagingDirectory $fileName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required staged file not found: $sourcePath"
    }
}

Copy-Item -LiteralPath (Join-Path $stagingDirectory "$($manifest.id).dll") -Destination (Join-Path $contentDirectory "$($manifest.id).dll") -Force
Copy-Item -LiteralPath (Join-Path $stagingDirectory "$($manifest.id).pck") -Destination (Join-Path $contentDirectory "$($manifest.id).pck") -Force
Copy-Item -LiteralPath (Join-Path $stagingDirectory "mod_id.json") -Destination (Join-Path $contentDirectory "$($manifest.id).json") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "steam-workshop/README.md") -Destination (Join-Path $contentDirectory "README.md") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "LICENSE") -Destination (Join-Path $contentDirectory "LICENSE") -Force

$workshopManifest = Get-Content -LiteralPath (Join-Path $contentDirectory "$($manifest.id).json") -Raw | ConvertFrom-Json
Assert-EqualValue -Name "Workshop manifest ID" -Expected $manifest.id -Actual $workshopManifest.id
Assert-EqualValue -Name "Workshop manifest version" -Expected $manifest.version -Actual $workshopManifest.version

$vdfScript = Join-Path $ProjectRoot "scripts/new-steam-workshop-vdf.ps1"
$vdfPath = Join-Path $releaseDirectory "steam-workshop.vdf"
& $vdfScript -ContentFolder $contentDirectory -PreviewFile (Join-Path $ProjectRoot "steam-workshop/preview.jpg") -OutputPath $vdfPath -PublishedFileId $PublishedFileId -Visibility $Visibility -ChangeNote $ChangeNote

Write-Host "[steam-workshop] Content folder: $contentDirectory"
Write-Host "[steam-workshop] Upload VDF: $vdfPath"
