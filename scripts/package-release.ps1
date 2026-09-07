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
$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = $manifest.version
$releaseBaseName = "sts2-ai-agent-v$version-windows"

$buildScript = Join-Path $ProjectRoot "scripts/build-mod.ps1"
$stagingModDir = Join-Path $ProjectRoot "build/mods/STS2AIAgent"
$releaseDir = Get-UniquePath -BasePath (Join-Path $OutputRoot $releaseBaseName)
$zipPath = Get-UniquePath -BasePath (Join-Path $OutputRoot $releaseBaseName) -Extension ".zip"

$modOutputDir = Join-Path $releaseDir "mod"
$mcpOutputDir = Join-Path $releaseDir "mcp_server"
$scriptOutputDir = Join-Path $releaseDir "scripts"
$mcpSourceDir = Join-Path $ProjectRoot "mcp_server"
$packageChecker = Join-Path $ProjectRoot "scripts/check_release_package.py"

function Rewrite-PackagedReadmeLinks {
    param([string]$Path)

    $text = [System.IO.File]::ReadAllText($Path)
    $replacements = @{
        "(./PRODUCT_PLAN_CURRENT.md)" = "(https://github.com/CharTyr/STS2-Agent/blob/main/PRODUCT_PLAN_CURRENT.md)"
        "(./COOP_DELIVERY.md)" = "(https://github.com/CharTyr/STS2-Agent/blob/main/COOP_DELIVERY.md)"
        "(./docs/api.md)" = "(https://github.com/CharTyr/STS2-Agent/blob/main/docs/api.md)"
        "(../skills/sts2-mcp-player/SKILL.md)" = "(https://github.com/CharTyr/STS2-Agent/blob/main/skills/sts2-mcp-player/SKILL.md)"
        "(../docs/release-readiness.md)" = "(https://github.com/CharTyr/STS2-Agent/blob/main/docs/release-readiness.md)"
    }
    foreach ($pair in $replacements.GetEnumerator()) {
        $text = $text.Replace($pair.Key, $pair.Value)
    }
    [System.IO.File]::WriteAllText($Path, $text)
}

function Invoke-ArtifactCheck {
    param([string]$ArtifactPath)

    & python $packageChecker --artifact $ArtifactPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Release artifact check failed for '$ArtifactPath' with exit code $LASTEXITCODE."
    }
}

Write-Host "[package-release] Building release mod artifacts..."
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

powershell @buildArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "build-mod.ps1 failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $modOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $mcpOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $scriptOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $mcpOutputDir "src") | Out-Null

Copy-Item -Path (Join-Path $stagingModDir "STS2AIAgent.dll") -Destination (Join-Path $modOutputDir "STS2AIAgent.dll") -Force
Copy-Item -Path (Join-Path $stagingModDir "STS2AIAgent.pck") -Destination (Join-Path $modOutputDir "STS2AIAgent.pck") -Force
Copy-Item -Path (Join-Path $stagingModDir "mod_id.json") -Destination (Join-Path $modOutputDir "mod_id.json") -Force

Copy-Item -Path (Join-Path $ProjectRoot "README.md") -Destination (Join-Path $releaseDir "README.md") -Force
Copy-Item -Path (Join-Path $ProjectRoot "README.zh-CN.md") -Destination (Join-Path $releaseDir "README.zh-CN.md") -Force
Copy-Item -Path (Join-Path $ProjectRoot "LICENSE") -Destination (Join-Path $releaseDir "LICENSE") -Force
Copy-Item -Path (Join-Path $ProjectRoot "CHANGELOG.md") -Destination (Join-Path $releaseDir "CHANGELOG.md") -Force
Rewrite-PackagedReadmeLinks -Path (Join-Path $releaseDir "README.md")
Rewrite-PackagedReadmeLinks -Path (Join-Path $releaseDir "README.zh-CN.md")
Copy-Item -Path (Join-Path $mcpSourceDir "README.md") -Destination (Join-Path $mcpOutputDir "README.md") -Force
Rewrite-PackagedReadmeLinks -Path (Join-Path $mcpOutputDir "README.md")
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

Copy-Item -Path (Join-Path $ProjectRoot "scripts/start-mcp-stdio.ps1") -Destination (Join-Path $scriptOutputDir "start-mcp-stdio.ps1") -Force
Copy-Item -Path (Join-Path $ProjectRoot "scripts/start-mcp-network.ps1") -Destination (Join-Path $scriptOutputDir "start-mcp-network.ps1") -Force
Copy-Item -Path (Join-Path $ProjectRoot "scripts/test-mcp-tool-profile.ps1") -Destination (Join-Path $scriptOutputDir "test-mcp-tool-profile.ps1") -Force

Write-Host "[package-release] Checking release directory artifact..."
Invoke-ArtifactCheck -ArtifactPath $releaseDir
Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath
Write-Host "[package-release] Checking release zip artifact..."
Invoke-ArtifactCheck -ArtifactPath $zipPath

Write-Host "[package-release] Release directory: $releaseDir"
Write-Host "[package-release] Release zip: $zipPath"
