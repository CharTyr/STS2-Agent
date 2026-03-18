param(
    [string]$ProjectRoot = "",
    [string]$Configuration = "Release",
    [string]$OutputRoot = "",
    [string]$GodotExe = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

function Resolve-FullPath {
    param([string]$PathValue)

    return [System.IO.Path]::GetFullPath($PathValue)
}

function Resolve-ProjectRoot {
    param([string]$InputRoot)

    if ([string]::IsNullOrWhiteSpace($InputRoot)) {
        return (Resolve-Path (Join-Path $scriptRoot "..")).Path
    }

    return (Resolve-Path $InputRoot).Path
}

function Get-UniquePath {
    param(
        [string]$BasePath,
        [string]$Extension = ""
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($Extension)) {
        $BasePath
    } else {
        "$BasePath$Extension"
    }

    if (-not (Test-Path $candidate)) {
        return $candidate
    }

    $index = 2
    while ($true) {
        $candidate = if ([string]::IsNullOrWhiteSpace($Extension)) {
            "$BasePath-$index"
        } else {
            "$BasePath-$index$Extension"
        }

        if (-not (Test-Path $candidate)) {
            return $candidate
        }

        $index += 1
    }
}

$ProjectRoot = Resolve-ProjectRoot -InputRoot $ProjectRoot

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $ProjectRoot "build/release"
} else {
    $OutputRoot = Resolve-FullPath -PathValue $OutputRoot
}

$manifestPath = Join-Path $ProjectRoot "STS2AIAgent/mod_manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.version
$releaseBaseName = "sts2-ai-agent-v$version-windows"

$buildScript = Join-Path $ProjectRoot "scripts/build-mod.ps1"
$stagingModDir = Join-Path $ProjectRoot "build/mods/STS2AIAgent"
$releaseDir = Get-UniquePath -BasePath (Join-Path $OutputRoot $releaseBaseName)
$zipPath = Get-UniquePath -BasePath (Join-Path $OutputRoot $releaseBaseName) -Extension ".zip"

$modOutputDir = Join-Path $releaseDir "mod"
$mcpOutputDir = Join-Path $releaseDir "mcp_server"
$scriptOutputDir = Join-Path $releaseDir "scripts"
$docsOutputDir = Join-Path $releaseDir "docs"
$mcpSourceDir = Join-Path $ProjectRoot "mcp_server"

Write-Host "[package-release] Building release mod artifacts..."
$buildArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", $buildScript,
    "-ProjectRoot", $ProjectRoot,
    "-Configuration", $Configuration
)
if (-not [string]::IsNullOrWhiteSpace($GodotExe)) {
    $buildArgs += @("-GodotExe", $GodotExe)
}

powershell @buildArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "build-mod.ps1 failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $modOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $mcpOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $scriptOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $docsOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $mcpOutputDir "src") | Out-Null

Copy-Item -Path (Join-Path $stagingModDir "STS2AIAgent.dll") -Destination (Join-Path $modOutputDir "STS2AIAgent.dll") -Force
Copy-Item -Path (Join-Path $stagingModDir "STS2AIAgent.pck") -Destination (Join-Path $modOutputDir "STS2AIAgent.pck") -Force
Copy-Item -Path (Join-Path $stagingModDir "mod_id.json") -Destination (Join-Path $modOutputDir "mod_id.json") -Force
Copy-Item -Path (Join-Path $stagingModDir "STS2AIAgent.json") -Destination (Join-Path $modOutputDir "STS2AIAgent.json") -Force

Copy-Item -Path (Join-Path $ProjectRoot "README.md") -Destination (Join-Path $releaseDir "README.md") -Force
Copy-Item -Path (Join-Path $mcpSourceDir "README.md") -Destination (Join-Path $mcpOutputDir "README.md") -Force
Copy-Item -Path (Join-Path $mcpSourceDir "pyproject.toml") -Destination (Join-Path $mcpOutputDir "pyproject.toml") -Force
Copy-Item -Path (Join-Path $mcpSourceDir "uv.lock") -Destination (Join-Path $mcpOutputDir "uv.lock") -Force
Copy-Item -Path (Join-Path $mcpSourceDir "data") -Destination (Join-Path $mcpOutputDir "data") -Recurse -Force
Get-ChildItem -Path (Join-Path $mcpSourceDir "src/sts2_mcp") -Recurse -File |
    Where-Object { $_.FullName -notmatch "\\__pycache__\\" } |
    ForEach-Object {
        $relativePath = $_.FullName.Substring($mcpSourceDir.Length + 1)
        $destinationPath = Join-Path $mcpOutputDir $relativePath
        $destinationDir = Split-Path -Parent $destinationPath

        New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
        Copy-Item -Path $_.FullName -Destination $destinationPath -Force
    }

Copy-Item -Path (Join-Path $ProjectRoot "docs/game-knowledge") -Destination (Join-Path $docsOutputDir "game-knowledge") -Recurse -Force
Copy-Item -Path (Join-Path $ProjectRoot "docs/release-readiness.md") -Destination (Join-Path $docsOutputDir "release-readiness.md") -Force

Copy-Item -Path (Join-Path $ProjectRoot "scripts/start-mcp-stdio.ps1") -Destination (Join-Path $scriptOutputDir "start-mcp-stdio.ps1") -Force
Copy-Item -Path (Join-Path $ProjectRoot "scripts/start-mcp-network.ps1") -Destination (Join-Path $scriptOutputDir "start-mcp-network.ps1") -Force

Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath

Write-Host "[package-release] Release directory: $releaseDir"
Write-Host "[package-release] Release zip: $zipPath"
