$ErrorActionPreference = 'Stop'

# Offline contract for the isolated-profile seeding in start-game-session.ps1.
#
# The launcher's job is to hand the game a settings.save where the agent mod is enabled. It used
# to enable only the *first* entry that names STS2AIAgent, which is wrong for every player who has
# both a Workshop subscription and a dev copy in mods/: the clone then carried
# mods_directory=true and steam_workshop=false, and the game read the id as disabled and logged
# "Skipping loading mod STS2AIAgent, it is set to disabled in settings" twice. Observed live on
# 2026-09-20 -- the game started and the mod was not in the list. This test would have caught it
# without a game: the second entry is exactly what the old code left alone.
. (Join-Path $PSScriptRoot 'start-game-session.ps1')

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

# Entry objects, not `"id"`-anchored matches: a repair may insert `is_enabled` *before* the id, so
# a regex that starts at the id silently reports the entry as flagless when it is not.
function Get-EntryObjects {
    param([string]$Raw, [string]$ModId)
    $pattern = [regex]::new('"id"\s*:\s*"' + [regex]::Escape($ModId) + '"')
    $found = @()
    $from = 0
    while ($true) {
        $match = $pattern.Match($Raw, $from)
        if (-not $match.Success) { break }
        $open = $Raw.LastIndexOf('{', $match.Index)
        $depth = 0
        for ($i = $open; $i -lt $Raw.Length; $i++) {
            $ch = $Raw[$i]
            if ($ch -eq '{') { $depth++ }
            elseif ($ch -eq '}') {
                $depth--
                if ($depth -eq 0) {
                    $found += $Raw.Substring($open, $i - $open + 1)
                    break
                }
            }
        }
        $from = $match.Index + $match.Length
    }
    return $found
}

$bothSourcesClone = @'
{
  "schema_version": 8,
  "mod_settings": {
    "mods_enabled": true,
    "mod_list": [
      { "id": "STS2AIAgent", "is_enabled": true, "source": "mods_directory" },
      { "id": "DamageMeter", "is_enabled": false, "source": "mods_directory" },
      { "id": "STS2AIAgent", "is_enabled": false, "source": "steam_workshop" }
    ]
  }
}
'@

Assert-True (-not (Test-IsolatedSettingsTextReady $bothSourcesClone)) `
    'A clone whose Workshop copy of the agent is disabled must not count as ready; the game reads it as disabled.'

$repaired = Repair-IsolatedSettingsText $bothSourcesClone
Assert-True (Test-IsolatedSettingsTextReady $repaired) 'The repaired clone must be ready.'

$agentEntries = @(Get-EntryObjects -Raw $repaired -ModId 'STS2AIAgent')
Assert-True ($agentEntries.Count -eq 2) "Expected 2 agent entries after repair, found $($agentEntries.Count)."
foreach ($entry in $agentEntries) {
    Assert-True ($entry -match '"is_enabled"\s*:\s*true') "Every STS2AIAgent entry must end up enabled; found $entry."
}
Assert-True ((@($agentEntries | Where-Object { $_ -match 'steam_workshop' })).Count -eq 1) `
    'The Workshop entry must still be the Workshop entry after the repair.'
Assert-True ((@($agentEntries | Where-Object { $_ -match 'mods_directory' })).Count -eq 1) `
    'The dev copy must still be the dev copy after the repair.'

# Sibling mods keep their own flags: a fixed character window over the file used to reach the next
# entry and turn a mod the operator had deliberately disabled back on.
$siblings = @(Get-EntryObjects -Raw $repaired -ModId 'DamageMeter')
Assert-True ($siblings.Count -eq 1) 'The sibling mod must survive the repair.'
Assert-True ($siblings[0] -match '"is_enabled"\s*:\s*false') "A disabled sibling mod must stay disabled; found $($siblings[0])."

# Entries without an explicit flag gain one rather than being left ambiguous.
$missingFlag = $bothSourcesClone.Replace(
    '{ "id": "STS2AIAgent", "is_enabled": false, "source": "steam_workshop" }',
    '{ "id": "STS2AIAgent", "source": "steam_workshop" }')
Assert-True ($missingFlag -match '"id": "STS2AIAgent", "source": "steam_workshop"') 'Fixture setup: the flagless entry was not produced.'
$repairedMissing = Repair-IsolatedSettingsText $missingFlag
$missingEntries = @(Get-EntryObjects -Raw $repairedMissing -ModId 'STS2AIAgent')
Assert-True ($missingEntries.Count -eq 2) 'The flagless clone must keep both agent entries.'
foreach ($entry in $missingEntries) {
    Assert-True ($entry -match '"is_enabled"\s*:\s*true') "A flagless entry must be given an explicit flag; found $entry."
}

# A repair of an already-correct clone changes nothing observable.
Assert-True (Test-IsolatedSettingsTextReady (Repair-IsolatedSettingsText $repaired)) 'A repair must be idempotent.'

# A null mod_settings block is what a fresh profile looks like.
$nullModSettings = '{ "schema_version": 8, "mod_settings": null }'
$repairedNull = Repair-IsolatedSettingsText $nullModSettings
Assert-True (Test-IsolatedSettingsTextReady $repairedNull) 'A null mod_settings block must be replaced with an enabled agent entry.'
Assert-True ((@(Get-EntryObjects -Raw $repairedNull -ModId 'STS2AIAgent')).Count -eq 1) `
    'A null mod_settings block must end up with exactly one agent entry.'

Write-Host 'PASS: isolated-profile seeding enables every STS2AIAgent entry and leaves sibling mods alone.'
