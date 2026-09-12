# Evidence: preflight and CI verification gates

Runner: main session. Date: 2026-09-10. Offline only.

## Change

- New `scripts/check_verification_gates.py` — standard-library only, three gates:
  - `lockfile`: dependency security floors (`fastmcp >= 3.2.0`, `fast-uri >= 3.1.6`), every direct
    `pyproject.toml` dependency present in `uv.lock`, and every `package.json` override (including
    nested ones) satisfied by `package-lock.json` with the pinned `yauzl 3.2.1` preserved.
  - `api-doc`: set equality between the action switch in `GameActionService.cs` and the marked
    action block in `docs/api.md`.
  - `doc-marks`: date-stamped `docs/**/*.md` records must carry a historical marker, and archived
    topic pages must keep their `history/` redirect.
- New `scripts/test-verification-gates.ps1` — builds a throwaway fixture in the temp directory and
  asserts each gate rejects a deliberately drifted input.
- `scripts/preflight-release.ps1`: new step "Run dependency, API-doc, and doc-snapshot gates"
  between the packaging source contract and the release-documents check.
- `.github/workflows/validate.yml`: two new steps, "Verification gates (deps, API doc, doc
  snapshots)" and "Verification gate self-test".

## Evidence

| Command | Result |
| --- | --- |
| `python scripts/check_verification_gates.py` | exit 0; api-doc 55 actions match, doc-marks 6 findings, lockfile fastmcp 3.4.7 / fast-uri 3.1.7 / yauzl 3.2.1 |
| `powershell -File scripts/test-verification-gates.ps1` | exit 0; baseline, restored, and six drift cases all behave as required |
| clean checkout (`git worktree add --detach` of the committed HEAD, then run there) | `check_verification_gates.py` exit 0 with the 55-action match and the fastmcp / fast-uri / yauzl floors; the self-test exit 0; `dotnet run` exit 0 including the new ProactiveChat.Session cases; `uv run --locked python -m unittest discover -s tests -v` 49 tests OK; `check_release_package.py --source-root` passed |
| `powershell -File scripts/preflight-release.ps1` | exit 0 end to end, including `[preflight] OK - Run dependency, API-doc, and doc-snapshot gates` |

Each drift case proves a specific failure mode the goal named: a missing action, a phantom action,
a lockfile below the floor, an unmarked dated snapshot, an unmarked date-less snapshot, and a broken
archive redirect.

The clean-checkout run is the evidence that matters for CI: a working tree cannot prove it, because
`AGENTS.md` is gitignored and therefore absent from a fresh clone. The gate originally used that file
as its repository-root sentinel and would have failed in CI; it now keys off tracked files
(`STS2AIAgent/mod_manifest.json`, `mcp_server/pyproject.toml`), and the self-test fixture copies a
tracked file instead.

## Boundary

These gates are static checks. They cannot detect a behavioural regression in the mod, and the
lockfile gate proves the declared floors, not a complete third-party advisory audit.
