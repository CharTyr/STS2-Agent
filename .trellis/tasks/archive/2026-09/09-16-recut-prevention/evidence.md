# Evidence

## Destructive verification (every new contract turned red first, then was restored byte-for-byte)

| # | What was broken | Result |
| --- | --- | --- |
| 1 | `runningAction = RunManager.Instance.ActionExecutor?...` hoisted above the `if (combatState != null && CombatManager.Instance.IsInProgress)` guard -- i.e. the exact shape of the shipped regression | `FAIL CombatGate.QueueReadIsCombatOnly` |
| 2 | A second, unguarded `RunManager.Instance.ActionQueueSet` read added elsewhere in `BuildStatePayload` | `FAIL CombatGate.QueueReadIsCombatOnly` |
| 3 | `lethal_risks` row deleted from the `combat` field table in docs/api.md | gate names the missing field: "internal sealed class CombatPayload serializes fields the docs/api.md '#### `combat` 顶层字段' table does not list: lethal_risks" |
| 4 | `ghost_field` row added to that table | gate names the stale field: "...lists fields internal sealed class CombatPayload no longer serializes: ghost_field" |
| 5 | `snapshot_stabilizing` row deleted from the `reason` table | gate names the missing code: "EvaluateCombatActionGate can answer with reason codes the docs/api.md ... section never mentions: snapshot_stabilizing" |

Cases 1-2 restored with `cmp` proving the file byte-identical; cases 3-5 restored the same way and
the gate returned to green. Cases 3-5 are now permanent cases in
`scripts/test-verification-gates.ps1` (9c / 9d / 9e), which previously had three api-facts cases
covering only the version string, the screen enum and the default port.

## Offline suites, after the round

| Check | Result |
| --- | --- |
| `dotnet run --project STS2AIAgent.Tests -c Release` | **409 PASS / 0 FAIL** (408 before; `CombatGate.QueueReadIsCombatOnly` is the new one) |
| `cd mcp_server && uv run --locked python -m unittest discover -s tests` | **222 tests OK** |
| `python scripts/check_verification_gates.py` | **9 gates green** (api-doc, api-facts, doc-marks, docs-tracked, lockfile, packaged-links, ps1-syntax, script-encoding, sh-syntax) |
| `scripts/test-verification-gates.ps1` | **25 cases pass** (22 before) |
| `python scripts/check_release_metadata.py` | `Release metadata consistent: 0.12.4` |
| `scripts/preflight-release.ps1` | exit 0 |
| `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental` | 0 warnings, 0 errors |

The api-facts gate now reports, in addition to its three existing facts:

```
  - #### `combat` 顶层字段: 7 field(s) match internal sealed class CombatPayload
  - #### `combat.action_readiness`: 23 field(s) match internal sealed class CombatActionReadinessPayload
  - #### `combat.lethal_risks[]`: 10 field(s) match internal sealed class CombatLethalRiskPayload
  - 19 action_readiness reason code(s) from EvaluateCombatActionGate are documented
```

## Health sweep (beyond the round's own scope)

| Check | Result |
| --- | --- |
| `npm audit` | 0 vulnerabilities |
| MCP server builds its tool surface on every profile | guided 11 / layered 19 / full 74 tools |
| Open GitHub issues | 0 |
| Last 8 CI Validate runs | all success |
| `origin/dev` vs `origin/main` | dev ahead by 2 commits, documentation only |
| Workshop build vs released tag | `d0c6fbd` (Workshop third build) is an ancestor of `v0.12.4`, and `d0c6fbd..v0.12.4` touches only `.md` files -- the Workshop, the GitHub release, `main` and `dev` all carry the same code |

## Fingerprint writer, smoke-tested

`Write-BuildFingerprint` was run against a two-file fixture. SHA256 of the 5-byte file came back
`2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824`, which is SHA256("hello"); the
summed byte count, the relative forward-slash paths, the source commit and the dirty flag were all
correct.

## Not covered

- **No live validation.** This round changes documentation, tests and scripts only. No runtime mod
  code was touched, so the published 0.12.4 third build is unaffected and its live evidence stands.
- **Not published.** The work is committed to a local branch; pushing it and opening the
  `-> dev` pull request is the maintainer's call.
