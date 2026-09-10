# Evidence: repo hardening - five goals

Session date: 2026-09-10. Source baseline: `main` @ 928d2ba. No game instance was launched; every
acceptance command below is offline.

## Child closure

| # | Child | State | Decisive evidence |
| --- | --- | --- | --- |
| 1 | 09-10-dep-security-closure | closed | `uv.lock` fastmcp 3.4.7 (was 3.1.0), `npm audit` total 0 (was 9), `uv run --locked python -m unittest discover -s tests -v` 49 tests OK, C# harness exit 0 |
| 2 | 09-10-proactive-teammate-chat | closed | 17 new C# cases PASS, mod build 0 warnings / 0 errors, off by default with a read-only proactive path |
| 3 | 09-10-api-doc-contract | closed | api-doc gate reports "55 actions match"; both drift directions rejected by the self-test |
| 4 | 09-10-stale-doc-archive | closed | gap list archived to `history/` with a redirect page; five dated records marked as snapshots |
| 5 | 09-10-verification-gates | closed | `scripts/check_verification_gates.py` + `scripts/test-verification-gates.ps1`; preflight and CI both wired and green |

Each child directory carries its own `evidence.md` with the exact commands and observed output.

## Tree-level acceptance

| Criterion | Result |
| --- | --- |
| Both open issues have a reproducible, evidence-backed resolution in the working tree | met - issues #50 and #51 are closed by lock refreshes; `dep-security-closure/evidence.md` |
| `docs/api.md` names every action the Mod exposes, and a script fails when that stops being true | met - 55/55 by set comparison; negative case proven |
| No document under `docs/` claims a capability is missing when the source implements it | met for the audited set: the gap list is archived and redirected, and the proactive-chat row now reads as implemented |
| Proactive teammate chat exists behind an explicit off-by-default opt-in, respects session budgets and pause semantics, covered by deterministic C# tests | met - 6 messages/session, 75 s interval, gate order fixed, read-only proven by test |
| `scripts/preflight-release.ps1` fails when a lockfile is out of sync with its manifest, when the API document misses an action, or when a stale-document marker is violated | met - the step exists and the self-test proves each failure mode |
| Every claim in the final report is backed by a command run in this session | met - commands and outputs recorded per child |

## Full offline sweep (last run)

```
python scripts/check_verification_gates.py            exit 0
powershell -File scripts/test-verification-gates.ps1  exit 0
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release  0 warnings, 0 errors
powershell -File scripts/preflight-release.ps1        exit 0 (build, py_compile, MCP import,
                                                      tool profile, C# harness, MCP suite,
                                                      version metadata, packaging source
                                                      contract, verification gates, release docs)
```

## Deviations from the original plan

- Five read-only scout sub-agents mapped the repository. One sub-agent was dispatched to implement
  goal 1 but refused to mutate files under its read-only constraint, so the dependency refresh was
  executed in the main session instead. All remaining goals were implemented in the main session.
- `uv sync` (needed to repair the fastmcp 3.4.x partial install) removed `pytest`, `pluggy` and
  `iniconfig` from `mcp_server/.venv`. None is declared in `pyproject.toml` or used by preflight
  and CI, which run `python -m unittest discover`.

## Residual risk and explicit non-claims

- Proactive chat is **not live-validated**. Only deterministic tests and a compile stand behind it.
- The dependency work closes the two reported advisories and clears `npm audit`; it is not a full
  third-party advisory audit.
- The doc audit covered the load-bearing claims identified by the scout, not every line of every
  document.
- No version bump, tag, or release was produced. These changes are main-line work after v0.10.5.

