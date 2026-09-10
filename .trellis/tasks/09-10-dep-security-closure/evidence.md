# Evidence: dependency security closure

Runner: main session. Date: 2026-09-10. Environment: Windows, offline-capable except the two package-manager refreshes.

## Issue #50 - fastmcp (CVE-2026-32871, fixed upstream in 3.2.0)

| Command | Result |
| --- | --- |
| `uv lock --upgrade-package fastmcp` (mcp_server) | exit 0. `Updated fastmcp v3.1.0 -> v3.4.7`; also authlib 1.6.9 -> 1.8.0, python-multipart 0.0.22 -> 0.0.32, starlette 0.52.1 -> 1.6.0; added fastmcp-slim 3.4.7, griffelib 2.3.0, joserfc 1.7.5. |
| `uv lock --check` | exit 0, `Resolved 76 packages in 1ms` |
| `uv.lock:341` | `version = "3.4.7"` for name = "fastmcp" (>= 3.2.0 required) |
| `uv run --locked python -m unittest discover -s tests -v` | exit 0, `Ran 49 tests`, `OK` |
| `uv run --locked python ../scripts/check_release_metadata.py` | exit 0, `Release metadata consistent: 0.10.5` |
| `powershell -File scripts/test-mcp-tool-profile.ps1 -RepoRoot .` | exit 0, guided_count 10, layered_count 18, full_count 63, `failures: []` |

### Install defect found and fixed during verification

The first `uv run --locked` after the upgrade failed with
`ImportError: cannot import name 'FastMCP' from 'fastmcp' (unknown location)`.
Cause: fastmcp 3.4.x is a meta distribution; the implementation moved to the
`fastmcp-slim` distribution, and the in-place upgrade left the pre-existing
`fastmcp/` directory from 3.1.0 partly unreplaced (`fastmcp/__init__.py` was
absent although `fastmcp_slim-3.4.7.dist-info/RECORD` lists it).

Fix: `uv sync --locked --reinstall-package fastmcp --reinstall-package fastmcp-slim`
(exit 0). After it: `from fastmcp import FastMCP` resolves to
`fastmcp.server.server.FastMCP`, all 49 tests pass, tool profile unchanged.

Side effect of that sync: it removed `pytest`, `pluggy` and `iniconfig` from
`mcp_server/.venv`. None of them is declared in `pyproject.toml` or used by
preflight/CI, which run `python -m unittest discover`.

## Issue #51 - fast-uri (CVE-2026-13676, fixed < 3.1.6 vulnerable)

| Command | Result |
| --- | --- |
| `npm audit fix` (repo root) | exit 0, `added 2 packages, removed 1 package, changed 15 packages, and audited 138 packages`, `found 0 vulnerabilities` |
| `npm audit --json` | exit 0, `vulnerabilities: {}`, metadata total 0 (was 9: 1 low / 3 moderate / 5 high) |
| `npm ls fast-uri yauzl --all` | `fast-uri@3.1.7` (>= 3.1.6 required), `yauzl@3.2.1 overridden` (override preserved) |
| `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` | exit 0, all lines `PASS`, no failures |

## Files changed

- `mcp_server/uv.lock`
- `package-lock.json`

`package.json` and `mcp_server/pyproject.toml` were NOT modified: every fix
landed inside the already-declared ranges.

## Boundary

Lockfile state, an audit report and offline unit tests are the evidence. No game
instance was started. This closes the two reported advisories; it is not a claim
of a complete third-party audit beyond fastmcp and the npm tree.

