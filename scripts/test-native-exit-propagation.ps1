$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib-checked-native.ps1')

$caught = $false
try {
    Invoke-CheckedNative -FilePath 'powershell' -Arguments @('-NoProfile', '-Command', 'exit 23')
}
catch {
    if ($_.Exception.Message -notlike '*exit code 23*') { throw }
    $caught = $true
}
if (-not $caught) { throw 'A failing native command incorrectly passed the preflight wrapper.' }
Invoke-CheckedNative -FilePath 'powershell' -Arguments @('-NoProfile', '-Command', 'exit 0')

# Exercise the actual preflight entry point in a separate Windows PowerShell process.
# Supply reference filenames for preflight and intercept dotnet before it reads them.
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('sts2-preflight-failure-' + [guid]::NewGuid().ToString('N'))
$fakeDotnet = Join-Path $fixtureRoot 'dotnet.cmd'
$gameRoot = Join-Path $fixtureRoot 'selected game'
$dataDir = Join-Path $gameRoot 'data_sts2_windows_x86_64'
$referenceFiles = @('sts2.dll', 'GodotSharp.dll', '0Harmony.dll') | ForEach-Object { Join-Path $dataDir $_ }
$originalPath = $env:PATH
$originalDataDir = $env:STS2_DATA_DIR
$originalExpectedData = $env:STS2_PREFLIGHT_TEST_DATA
try {
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    foreach ($file in $referenceFiles) { Set-Content -LiteralPath $file -Value 'offline fixture' -Encoding Ascii }
    $env:STS2_DATA_DIR = $null
    $env:STS2_PREFLIGHT_TEST_DATA = (Resolve-Path -LiteralPath $dataDir).Path
    Set-Content -LiteralPath $fakeDotnet -Value @(
        '@if not "%~5"=="-p:Sts2DataDir=%STS2_PREFLIGHT_TEST_DATA%" exit /b 24'
        '@exit /b 23'
    ) -Encoding Ascii
    $env:PATH = $fixtureRoot + [IO.Path]::PathSeparator + $originalPath
    $preflight = Join-Path $PSScriptRoot 'preflight-release.ps1'
    # Windows PowerShell turns redirected native stderr into error records.
    # Collect the expected failure instead of terminating before checking its exit code.
    $ErrorActionPreference = 'Continue'
    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $preflight -GameRoot $gameRoot 2>&1
        $preflightExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = 'Stop' }
    $combinedOutput = $output | Out-String
    if ($preflightExitCode -eq 0) { throw 'The preflight entry point swallowed a failed build.' }
    if ($combinedOutput -notlike '*exit code 23*') { throw "Preflight failed for an unexpected reason: $combinedOutput" }
    if ($combinedOutput -like '*OK - Build mod*' -or $combinedOutput -like '*Static preflight complete*') {
        throw 'Preflight reported success after a failed build.'
    }
}
finally {
    $env:PATH = $originalPath
    $env:STS2_DATA_DIR = $originalDataDir
    $env:STS2_PREFLIGHT_TEST_DATA = $originalExpectedData
    foreach ($path in ($referenceFiles + @($fakeDotnet, $dataDir, $gameRoot, $fixtureRoot))) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
}
Write-Host 'PASS: native failures stop the preflight process and successful commands remain successful.'
