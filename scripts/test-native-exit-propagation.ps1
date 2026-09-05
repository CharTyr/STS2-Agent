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
# Shadow only its first build command, so no installed game or dependencies are needed.
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('sts2-preflight-failure-' + [guid]::NewGuid().ToString('N'))
$fakeDotnet = Join-Path $fixtureRoot 'dotnet.cmd'
$originalPath = $env:PATH
try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    Set-Content -LiteralPath $fakeDotnet -Value '@exit /b 23' -Encoding Ascii
    $env:PATH = $fixtureRoot + [IO.Path]::PathSeparator + $originalPath
    $preflight = Join-Path $PSScriptRoot 'preflight-release.ps1'
    # Windows PowerShell turns redirected native stderr into error records.
    # Collect the expected failure instead of terminating before checking its exit code.
    $ErrorActionPreference = 'Continue'
    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $preflight 2>&1
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
    if (Test-Path -LiteralPath $fakeDotnet) { Remove-Item -LiteralPath $fakeDotnet }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot }
}
Write-Host 'PASS: native failures stop the preflight process and successful commands remain successful.'
