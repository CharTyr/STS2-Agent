param(
    [Parameter(Mandatory = $true)]
    [string]$ContentFolder,
    [string]$PreviewFile = "",
    [string]$OutputPath = "",
    [ValidatePattern("^\d+$")]
    [string]$PublishedFileId = "0",
    [ValidateSet("public", "friends", "private", "unlisted")]
    [string]$Visibility = "private",
    [string]$ChangeNote = "Initial Workshop upload"
)

$ErrorActionPreference = "Stop"

function Get-FullPath {
    param([string]$PathValue)

    return [System.IO.Path]::GetFullPath($PathValue)
}

function ConvertTo-VdfValue {
    param([string]$Value)

    return $Value.Replace([string][char]13, "").Replace([string][char]10, "\n").Replace('"', '\"')
}

$ContentFolder = Get-FullPath -PathValue $ContentFolder
if (-not (Test-Path -LiteralPath $ContentFolder -PathType Container)) {
    throw "Workshop content folder not found: $ContentFolder"
}

if ([string]::IsNullOrWhiteSpace($PreviewFile)) {
    $PreviewFile = Join-Path $PSScriptRoot "../steam-workshop/preview.jpg"
}
$PreviewFile = Get-FullPath -PathValue $PreviewFile
if (-not (Test-Path -LiteralPath $PreviewFile -PathType Leaf)) {
    throw "Workshop preview image not found: $PreviewFile"
}
$previewLength = (Get-Item -LiteralPath $PreviewFile).Length
if ($previewLength -ge 1048576) {
    throw "Workshop preview image exceeds 1 MB: $PreviewFile ($previewLength bytes)."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $ContentFolder "../steam-workshop.vdf"
}
$OutputPath = Get-FullPath -PathValue $OutputPath

$visibilityValues = @{
    public = 0
    friends = 1
    private = 2
    unlisted = 3
}

$descriptionPath = Join-Path $PSScriptRoot "../steam-workshop/description.en.txt"
$description = Get-Content -LiteralPath $descriptionPath -Raw -Encoding UTF8
$title = "STS2 AI Agent"

$vdf = @"
"workshopitem"
{
    "appid" "2868840"
    "publishedfileid" "$(ConvertTo-VdfValue $PublishedFileId)"
    "contentfolder" "$(ConvertTo-VdfValue $ContentFolder)"
    "previewfile" "$(ConvertTo-VdfValue $PreviewFile)"
    "visibility" "$(ConvertTo-VdfValue ([string]$visibilityValues[$Visibility]))"
    "title" "$(ConvertTo-VdfValue $title)"
    "description" "$(ConvertTo-VdfValue $description)"
    "changenote" "$(ConvertTo-VdfValue $ChangeNote)"
}
"@

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
Set-Content -LiteralPath $OutputPath -Value $vdf -Encoding UTF8

Write-Host "[steam-workshop] SteamCMD VDF: $OutputPath"
