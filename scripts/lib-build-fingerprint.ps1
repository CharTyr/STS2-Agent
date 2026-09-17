# Build fingerprints for packaged artifacts.
#
# Why this exists: this project has republished the same version number four times (v0.12.3 twice
# and v0.12.4 twice). Three builds answer to "0.12.4" and the version string never tells them
# apart, so every release record has had to say "use the size or the hash" and then collect those
# numbers by hand after the fact -- from Get-FileHash run in an ad-hoc shell, matched against a
# commit reconstructed from the git log. That is the step that has to be right when a player
# reports a bug against "0.12.4", and it was the step with no tooling.
#
# Write-BuildFingerprint records it at the moment the artifact is built, next to the artifact:
# every packaged file with its byte count and SHA256, the summed byte count the Steam Workshop
# reports as file_size, and the commit the tree was built from (with a dirty flag, because a build
# from an edited working tree is not that commit). A release record is then a copy of this file
# instead of a reconstruction.

function Get-BuildFingerprintCommit {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $result = [ordered]@{
        commit = $null
        dirty  = $null
    }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        return $result
    }

    # A packaging run must not fail because git is unhappy; an unknown commit is recorded as null
    # rather than guessed, which is the honest answer and keeps the artifact reproducible anyway.
    $commit = & $git.Source -C $RepositoryRoot rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        return $result
    }

    $result.commit = $commit.Trim()
    $status = & $git.Source -C $RepositoryRoot status --porcelain 2>$null
    if ($LASTEXITCODE -eq 0) {
        $result.dirty = -not [string]::IsNullOrWhiteSpace(($status | Out-String).Trim())
    }

    return $result
}

function Write-BuildFingerprint {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$ArtifactKind,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ContentRoot,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [string]$Label = "build-fingerprint"
    )

    if (-not (Test-Path -LiteralPath $ContentRoot -PathType Container)) {
        throw "Cannot fingerprint a content root that does not exist: $ContentRoot"
    }

    $contentFull = (Resolve-Path -LiteralPath $ContentRoot).Path
    $files = @(Get-ChildItem -LiteralPath $contentFull -Recurse -File | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Cannot fingerprint an empty content root: $ContentRoot"
    }

    $entries = @()
    $totalBytes = 0
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($contentFull.Length).TrimStart([char]92, [char]47)
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $totalBytes += $file.Length
        $entries += [ordered]@{
            path   = ($relative -replace [regex]::Escape([string][char]92), "/")
            bytes  = $file.Length
            sha256 = $hash
        }
    }

    $source = Get-BuildFingerprintCommit -RepositoryRoot $RepositoryRoot
    $fingerprint = [ordered]@{
        artifact_kind      = $ArtifactKind
        version            = $Version
        generated_utc      = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        source_commit      = $source.commit
        source_tree_dirty  = $source.dirty
        content_root       = $contentFull
        # The Steam Workshop reports file_size as the summed bytes of the content folder, so the
        # release records compare against exactly this number.
        total_bytes        = $totalBytes
        file_count         = $entries.Count
        files              = $entries
    }

    $json = $fingerprint | ConvertTo-Json -Depth 6
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, $utf8NoBom)

    Write-Host "[$Label] Fingerprint: $OutputPath"
    Write-Host "[$Label] version $Version, $($entries.Count) file(s), $totalBytes byte(s) total"
    if ($source.commit) {
        $dirtySuffix = if ($source.dirty) { " (working tree dirty -- this build is NOT that commit)" } else { "" }
        Write-Host "[$Label] source commit $($source.commit)$dirtySuffix"
    }
    else {
        Write-Host "[$Label] source commit unknown (git unavailable)"
    }
    foreach ($entry in $entries) {
        Write-Host ("[$Label]   {0}  {1} bytes  {2}" -f $entry.path, $entry.bytes, $entry.sha256)
    }

    return $fingerprint
}
