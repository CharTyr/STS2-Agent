Resolved in the main-line worktree (commit `cd55fe1`, 2026-09-10).

`package-lock.json` now resolves **fast-uri 3.1.7**. Note that the version suggested in this report (3.1.3) is itself still inside a vulnerable advisory range, so the fix went further than the suggestion; 3.1.6 is the first version outside every listed range and 3.1.7 is what the resolver selected.

Evidence, all run offline:
- `npm audit fix` — `changed 15 packages, and audited 138 packages`, `found 0 vulnerabilities`
- `npm audit --json` — `vulnerabilities: {}`, metadata total 0 (was 9: 1 low / 3 moderate / 5 high)
- `npm ls fast-uri yauzl --all` — `fast-uri@3.1.7`, `yauzl@3.2.1 overridden` (the existing override survived the refresh)
- `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` — exit 0

The refresh fixed the whole audited tree rather than only `fast-uri`, because leaving eight other advisories in the same dependency chain would keep the audit red and re-open this report on the next scan. No `--force` was used and `package.json` was not modified: every fix landed inside the declared semver ranges.

A regression floor for this advisory is now enforced by `scripts/check_verification_gates.py` (fails below fast-uri 3.1.6), which runs in preflight and in CI.

