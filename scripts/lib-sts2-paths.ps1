# Where Slay the Spire 2 lives on Windows, resolved from the caller instead of assumed.
#
# The POSIX scripts already read the same environment variables (scripts/lib-sts2.sh) and
# build-and-env.md documents them; this is the Windows half of that contract. Every caller resolves
# in the same order -- explicit argument, environment variable, detection, conventional default -- so
# an install on another drive or in a second Steam library needs no script edits. Only this file
# knows the conventional path, and it is the last resort rather than the only answer.

$script:Sts2AppId = "2868840"
$script:Sts2DefaultSteamRoot = "C:/Program Files (x86)/Steam"
$script:Sts2DefaultGameRoot = $script:Sts2DefaultSteamRoot + "/steamapps/common/Slay the Spire 2"

function ConvertTo-Sts2NormalPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    # Steam keeps its own path in whichever separator style the value was written with, and some
    # installs store the escaped form. One spelling in, one spelling out: the doubled separators a
    # vdf uses have to collapse before the path can be tested. A leading double separator is the
    # UNC form rather than escaping, so it is held aside while the rest collapses, and a bare drive
    # root keeps its separator, because the root form and the drive-relative form differ.
    $normalized = $Path -replace "/", "\"
    $prefix = ""
    if ($normalized.StartsWith("\\")) {
        $prefix = "\\"
        $normalized = $normalized.Substring(2)
    }
    while ($normalized.Contains("\\")) {
        $normalized = $normalized.Replace("\\", "\")
    }

    $body = $normalized.TrimEnd("\")
    if ($body -match "^[A-Za-z]:$") {
        $body = $body + "\"
    }
    return $prefix + $body
}

function Get-Sts2UniquePaths {
    # Case-insensitive and separator-insensitive: Steam on Windows treats the two spellings of one
    # directory as equal, and the registry hands out several of them for the same install -- while
    # the conventional constants below are written with forward slashes. The key is normalized; the
    # value that comes back keeps the spelling it arrived with.
    param([System.Collections.Generic.List[string]]$Paths)

    $seen = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
    $unique = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in $Paths) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if ($seen.Add((ConvertTo-Sts2NormalPath -Path $candidate))) {
            $unique.Add($candidate)
        }
    }

    return @($unique)
}

function Get-Sts2SteamRoots {
    # Steam itself: the registry key Windows installers write, then the conventional path. A key that
    # is absent or unreadable is normal (no Steam, or a locked hive), so it is skipped rather than
    # fatal -- the caller decides what a missing install means.
    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($key in @(
        "HKCU:\Software\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam"
    )) {
        try {
            $value = (Get-ItemProperty -Path $key -ErrorAction Stop).SteamPath
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $roots.Add((ConvertTo-Sts2NormalPath -Path $value))
            }
        } catch {
        }
    }
    $roots.Add($script:Sts2DefaultSteamRoot)
    return @(Get-Sts2UniquePaths -Paths $roots)
}

function Get-Sts2SteamLibraries {
    # A library root is a directory holding steamapps/. Extra libraries are listed in Steam's own
    # libraryfolders.vdf, so the game can be installed anywhere without this file guessing.
    $libraries = New-Object System.Collections.Generic.List[string]
    foreach ($steamRoot in Get-Sts2SteamRoots) {
        $libraries.Add($steamRoot)
        $vdf = Join-Path $steamRoot "steamapps/libraryfolders.vdf"
        if (-not (Test-Path -LiteralPath $vdf)) {
            continue
        }

        $text = Get-Content -LiteralPath $vdf -Raw
        if ([string]::IsNullOrEmpty($text)) {
            # Steam rewrites this file in place, so a truncated read is possible. A regex built
            # on a null input throws, which would take the whole resolver down -- including the
            # callers that only wanted a different path -- with an error about a regex argument.
            continue
        }

        foreach ($match in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
            $libraries.Add((ConvertTo-Sts2NormalPath -Path $match.Groups[1].Value))
        }
    }
    return @(Get-Sts2UniquePaths -Paths $libraries)
}

function Get-Sts2GameRootCandidates {
    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($library in Get-Sts2SteamLibraries) {
        $candidates.Add((Join-Path $library "steamapps/common/Slay the Spire 2"))
    }
    $candidates.Add($script:Sts2DefaultGameRoot)
    return @(Get-Sts2UniquePaths -Paths $candidates)
}

function Resolve-Sts2GameRoot {
    param([string]$Explicit = "")

    foreach ($candidate in @($Explicit, $env:STS2_GAME_ROOT)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    }

    foreach ($candidate in Get-Sts2GameRootCandidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    # Naming the conventional path is the most useful thing left to say when nothing was found;
    # callers that must not guess check whether it exists and fail with their own message.
    return $script:Sts2DefaultGameRoot
}

function Resolve-Sts2Executable {
    param(
        [string]$Explicit = "",
        [string]$GameRoot = ""
    )

    foreach ($candidate in @($Explicit, $env:STS2_EXE_PATH)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    }

    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        $GameRoot = Resolve-Sts2GameRoot
    }

    foreach ($name in @("SlayTheSpire2.exe", "Slay the Spire 2.exe")) {
        $candidate = Join-Path $GameRoot $name
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return (Join-Path $GameRoot "SlayTheSpire2.exe")
}

function Resolve-Sts2AppManifest {
    param([string]$Explicit = "")

    foreach ($candidate in @($Explicit, $env:STS2_APP_MANIFEST)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            return $candidate
        }
    }

    foreach ($library in Get-Sts2SteamLibraries) {
        $candidate = Join-Path $library ("steamapps/appmanifest_" + $script:Sts2AppId + ".acf")
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return (Join-Path $script:Sts2DefaultSteamRoot ("steamapps/appmanifest_" + $script:Sts2AppId + ".acf"))
}

function Get-Sts2SteamExe {
    param([string]$Explicit = "")

    foreach ($candidate in @($Explicit, $env:STS2_STEAM_EXE)) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        # Normalized before it reaches Start-Process, which resolves a relative path against the
        # host process directory rather than the caller's.
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    foreach ($steamRoot in Get-Sts2SteamRoots) {
        $candidate = Join-Path $steamRoot "steam.exe"
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}
