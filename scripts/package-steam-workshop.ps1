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

function Assert-RequiredFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required Workshop listing file not found: $Path"
    }
}

function Assert-WorkshopPreviewImage {
    param(
        [string]$Path,
        [int]$MaxBytes = 1048576
    )

    Assert-RequiredFile -Path $Path
    $length = (Get-Item -LiteralPath $Path).Length
    if ($length -ge $MaxBytes) {
        throw "Workshop preview image exceeds 1 MB: $Path ($length bytes)."
    }
}

function Write-Utf8NoBomFile {
    param(
        [string]$Path,
        [string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
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

$workshopSourceDir = Join-Path $ProjectRoot "steam-workshop"
$playerReadmePath = Join-Path $workshopSourceDir "content-readme.md"
$workshopConfigPath = Join-Path $workshopSourceDir "workshop.json"
$englishDescriptionPath = Join-Path $workshopSourceDir "description.en.txt"
$chineseDescriptionPath = Join-Path $workshopSourceDir "description.zh-CN.txt"
$previewJpgPath = Join-Path $workshopSourceDir "preview.jpg"
$imagePngPath = Join-Path $workshopSourceDir "image.png"
Assert-RequiredFile -Path $playerReadmePath
Assert-RequiredFile -Path $workshopConfigPath
Assert-RequiredFile -Path $englishDescriptionPath
Assert-RequiredFile -Path $chineseDescriptionPath
Assert-WorkshopPreviewImage -Path $previewJpgPath
Assert-WorkshopPreviewImage -Path $imagePngPath

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
Copy-Item -LiteralPath $playerReadmePath -Destination (Join-Path $contentDirectory "README.md") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "LICENSE") -Destination (Join-Path $contentDirectory "LICENSE") -Force

$workshopManifest = Get-Content -LiteralPath (Join-Path $contentDirectory "$($manifest.id).json") -Raw | ConvertFrom-Json
Assert-EqualValue -Name "Workshop manifest ID" -Expected $manifest.id -Actual $workshopManifest.id
Assert-EqualValue -Name "Workshop manifest version" -Expected $manifest.version -Actual $workshopManifest.version

$vdfScript = Join-Path $ProjectRoot "scripts/new-steam-workshop-vdf.ps1"
$vdfPath = Join-Path $releaseDirectory "steam-workshop.vdf"
& $vdfScript -ContentFolder $contentDirectory -PreviewFile $previewJpgPath -OutputPath $vdfPath -PublishedFileId $PublishedFileId -Visibility $Visibility -ChangeNote $ChangeNote

Copy-Item -LiteralPath $imagePngPath -Destination (Join-Path $releaseDirectory "image.png") -Force

$previewsSourceDir = Join-Path $workshopSourceDir "previews"
if (Test-Path -LiteralPath $previewsSourceDir -PathType Container) {
    $previewFiles = Get-ChildItem -LiteralPath $previewsSourceDir -File
    if ($previewFiles.Count -gt 0) {
        $previewsOutputDir = Join-Path $releaseDirectory "previews"
        New-Item -ItemType Directory -Force -Path $previewsOutputDir | Out-Null
        foreach ($previewFile in $previewFiles) {
            Assert-WorkshopPreviewImage -Path $previewFile.FullName
            Copy-Item -LiteralPath $previewFile.FullName -Destination (Join-Path $previewsOutputDir $previewFile.Name) -Force
        }
    }
}

$workshopConfig = Get-Content -LiteralPath $workshopConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$englishDescription = (Get-Content -LiteralPath $englishDescriptionPath -Raw -Encoding UTF8).Trim()
$visibilityForUploader = if ($Visibility -eq "friends") { "friends_only" } else { $Visibility }
$workshopConfig | Add-Member -NotePropertyName "description" -NotePropertyValue $englishDescription -Force
$workshopConfig.visibility = $visibilityForUploader
$workshopConfig.changeNote = $ChangeNote
Write-Utf8NoBomFile -Path (Join-Path $releaseDirectory "workshop.json") -Content ($workshopConfig | ConvertTo-Json -Depth 8)

Write-Host "[steam-workshop] Content folder: $contentDirectory"
Write-Host "[steam-workshop] Upload VDF: $vdfPath"
Write-Host "[steam-workshop] ModUploader workspace: $releaseDirectory"
Write-Host "[steam-workshop] After upload, paste Simplified Chinese listing from: $chineseDescriptionPath"
