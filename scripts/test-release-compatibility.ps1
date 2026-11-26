param(
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
    [Parameter(Mandatory = $true)][string]$MinimumGameRoot,
    [Parameter(Mandatory = $true)][string]$TargetGameRoot,
    [Parameter(Mandatory = $true)][string]$TargetGameVersion,
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib-sts2-paths.ps1")

function Get-ReleaseGame {
    param([string]$Root, [string]$ExpectedVersion)

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $info = Get-Sts2ReleaseInfo -GameRoot $resolvedRoot
    $version = ([string]$info.version).TrimStart('v')
    if ($version -ne $ExpectedVersion) {
        throw "Expected game $ExpectedVersion at '$resolvedRoot', found '$version'."
    }
    $exe = Get-Sts2ExecutableInRoot -GameRoot $resolvedRoot
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "No game executable at '$exe'."
    }
    return [pscustomobject]@{ root = $resolvedRoot; exe = $exe; version = $version; commit = $info.commit }
}

function Get-FreeLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return $listener.LocalEndpoint.Port
    }
    finally {
        $listener.Stop()
    }
}

function Assert-ModDllHash {
    param([string]$Path, [string]$ExpectedHash)
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $ExpectedHash) {
        throw "Release DLL changed: '$Path' expected $ExpectedHash, found $actual."
    }
}

function Remove-ReleaseScratchDirectory {
    param([string]$Path, [string]$ExpectedParent)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $parent = (Resolve-Path -LiteralPath $ExpectedParent).Path
    if ((Split-Path -Parent $resolved).TrimEnd('\') -ne $parent.TrimEnd('\')) {
        Write-Warning "Skipped cleanup outside the expected directory: '$resolved'."
        return
    }
    # Windows can keep game DLLs locked briefly after the owned process exits.
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if (-not (Test-Path -LiteralPath $resolved)) { return }
            Start-Sleep -Milliseconds 400
        }
    }
    Write-Warning "The test directory could not be removed: '$resolved'."
}

function Test-ReleaseGame {
    param($Game, [string]$Channel, [string]$Sandbox, [string]$DllHash)

    $destination = Join-Path $Sandbox ("game-" + $Channel)
    if ($destination.StartsWith($Game.root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "The temporary game directory would be inside the source installation: '$destination'."
    }
    & robocopy $Game.root $destination /E /XD mods /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Host
    if ($LASTEXITCODE -ge 8) {
        throw "Copying game $($Game.version) into the sandbox failed (robocopy $LASTEXITCODE)."
    }

    $mods = Join-Path $destination "mods/STS2AIAgent"
    New-Item -ItemType Directory -Path $mods | Out-Null
    foreach ($file in $artifactFiles) {
        Copy-Item -LiteralPath $file -Destination $mods
    }
    $copiedDll = Join-Path $mods "STS2AIAgent.dll"
    Assert-ModDllHash -Path $copiedDll -ExpectedHash $DllHash

    $exe = Join-Path $destination (Split-Path -Leaf $Game.exe)
    $port = Get-FreeLoopbackPort
    $clientId = [DateTime]::UtcNow.Ticks
    $settingsPath = Join-Path $Sandbox "agent-$clientId.json"
    $userRoot = Join-Path $env:APPDATA "SlayTheSpire2"
    $profileParent = Join-Path $userRoot "default"
    $profile = Join-Path $profileParent $clientId
    if (Test-Path -LiteralPath $profile) {
        throw "The test client ID is already in use: $clientId."
    }
    [IO.File]::WriteAllText($settingsPath, '{"mcpEnabled":true}')
    $previousSettings = $env:STS2_AGENT_SETTINGS_PATH
    $previousUserRoot = $env:STS2_SLAY_USER_ROOT
    try {
        $env:STS2_AGENT_SETTINGS_PATH = $settingsPath
        $env:STS2_SLAY_USER_ROOT = $userRoot
        Write-Host "[release-compatibility] $Channel game $($Game.version), port $port, test client $clientId, DLL $DllHash"
        $launcher = Join-Path $PSScriptRoot "start-game-session.ps1"
        $session = & $launcher -ExePath $exe -KeepExistingProcesses -Headless -EnableDebugActions -ApiPort $port -Attempts 30 `
            -ExtraArguments "--force-steam off --clientId $clientId --log-file release-compatibility.log" | ConvertFrom-Json
        $process = Get-Process -Id $session.pid -ErrorAction Stop
        if ($process.Path -ne $exe) {
            throw "Validation process $($session.pid) was not launched from '$exe'."
        }

        $validationScript = Join-Path $PSScriptRoot "run_sts2_validation.py"
        $result = & python $validationScript patch-check --release-smoke --base-url $session.base_url `
            --expected-game-version $Game.version --expected-mod-version $manifest.version `
            --expected-process-id $session.pid
        if ($LASTEXITCODE -ne 0) {
            throw "Release smoke failed on game $($Game.version) with exit code $LASTEXITCODE."
        }
        Assert-ModDllHash -Path $copiedDll -ExpectedHash $DllHash
        return [pscustomobject]@{
            channel = $Channel
            game_version = $Game.version
            game_commit = $Game.commit
            dll_sha256 = $DllHash
            checks = ($result | Out-String | ConvertFrom-Json)
        }
    }
    catch {
        $log = Join-Path $destination "release-compatibility.log"
        if (Test-Path -LiteralPath $log) {
            Select-String -LiteralPath $log -Pattern 'Steam initialization skipped|\[STS2AIAgent.*(Failed:|Compatibility:)' |
                Select-Object -Last 12 | ForEach-Object { Write-Host $_.Line }
        }
        throw
    }
    finally {
        $env:STS2_AGENT_SETTINGS_PATH = $previousSettings
        $env:STS2_SLAY_USER_ROOT = $previousUserRoot
        # The launcher can fail before returning a PID. Match the private executable path,
        # never all processes with the game's name.
        foreach ($instance in @(Get-Process -ErrorAction SilentlyContinue | Where-Object Path -eq $exe)) {
            Stop-Process -Id $instance.Id -ErrorAction SilentlyContinue
            Wait-Process -Id $instance.Id -Timeout 10 -ErrorAction SilentlyContinue
        }
        Remove-ReleaseScratchDirectory -Path $profile -ExpectedParent $profileParent
    }
}

$artifactRoot = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$manifestPath = Join-Path $artifactRoot "mod_id.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    $manifestPath = Join-Path $artifactRoot "STS2AIAgent.json"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.id -ne "STS2AIAgent" -or -not $manifest.version -or -not $manifest.min_game_version) {
    throw "Artifact manifest is missing the mod ID, mod version, or minimum game version."
}
$dll = Join-Path $artifactRoot "STS2AIAgent.dll"
$artifactFiles = @($dll, (Join-Path $artifactRoot "STS2AIAgent.pck"), $manifestPath)
foreach ($path in $artifactFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release artifact is missing '$path'."
    }
}
$minimumVersion = ([string]$manifest.min_game_version).TrimStart('v')
$targetVersion = $TargetGameVersion.TrimStart('v')
if ([version]$targetVersion -lt [version]$minimumVersion) {
    throw "Target game $targetVersion is older than the manifest minimum $minimumVersion."
}
$minimum = Get-ReleaseGame -Root $MinimumGameRoot -ExpectedVersion $minimumVersion
$target = Get-ReleaseGame -Root $TargetGameRoot -ExpectedVersion $targetVersion
$dllHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash

$tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path
$sandbox = (New-Item -ItemType Directory -Path (Join-Path $tempRoot ("sts2-release-" + [Guid]::NewGuid().ToString("N")))).FullName
try {
    $checks = @(
        Test-ReleaseGame -Game $minimum -Channel "stable" -Sandbox $sandbox -DllHash $dllHash
        Test-ReleaseGame -Game $target -Channel "public-beta" -Sandbox $sandbox -DllHash $dllHash
    )
    Assert-ModDllHash -Path $dll -ExpectedHash $dllHash
    $report = [ordered]@{ mod_version = $manifest.version; dll_sha256 = $dllHash; games = $checks }
    $json = $report | ConvertTo-Json -Depth 20
    if ($ReportPath) {
        [IO.File]::WriteAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath), $json)
    }
    Write-Output $json
}
finally {
    # Only remove the unique directory created above, after its owned processes stop.
    Remove-ReleaseScratchDirectory -Path $sandbox -ExpectedParent $tempRoot
}
