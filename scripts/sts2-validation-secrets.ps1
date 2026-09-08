Set-StrictMode -Version Latest

function Get-Sts2ValidationKeyPath {
    param([string]$FileName = '2026-09-08-model-key.clixml')
    return (Join-Path $env:LOCALAPPDATA (Join-Path 'STS2AgentValidation' $FileName))
}

function Import-Sts2ValidationSecureKey {
    param([string]$Path = (Get-Sts2ValidationKeyPath))
    if (-not (Test-Path -LiteralPath $Path)) {
        throw ('Validation key file is missing: ' + $Path)
    }
    $loaded = Import-Clixml -LiteralPath $Path
    if ($loaded -isnot [SecureString]) {
        throw 'Validation key file did not contain a SecureString.'
    }
    return $loaded
}

function Convert-Sts2SecureStringToPlain {
    param([Parameter(Mandatory=$true)][SecureString]$Secure)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Get-Sts2RoleFingerprint {
    param([string]$BaseUrl, [string]$Model, [string]$ApiKey)
    $nl = [char]10
    $raw = $BaseUrl + $nl + $Model + $nl + $ApiKey
    $bytes = [Text.Encoding]::UTF8.GetBytes($raw)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return ([BitConverter]::ToString($hash).Replace('-', '').Substring(0, 16))
}
