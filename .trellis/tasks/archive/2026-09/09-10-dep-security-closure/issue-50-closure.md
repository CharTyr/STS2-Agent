Resolved in the main-line worktree (commit `cd55fe1`, 2026-09-10).

`mcp_server/uv.lock` now pins **fastmcp 3.4.7**, above the 3.2.0 fixed version this report names. `pyproject.toml` already allowed `>=3.1.0,<4.0.0`, so no manifest change was needed.

Evidence, all run offline:
- `uv lock --check` — exit 0
- `uv run --locked python -m unittest discover -s tests -v` — `Ran 49 tests`, `OK`
- `powershell -File scripts/test-mcp-tool-profile.ps1` — guided 10 / layered 18 / full 63, `failures: []`

One wrinkle worth recording: fastmcp 3.4.x is a meta distribution and moved its implementation into `fastmcp-slim`. The in-place upgrade left a partial `fastmcp/` directory, and `from fastmcp import FastMCP` failed until `uv sync --locked --reinstall-package fastmcp --reinstall-package fastmcp-slim` was run. That procedure is now written down in `.trellis/spec/mcp/contracts-and-tests.md`.

The source patch in the report body was not applied: the vulnerable code lives in the dependency, not in this repository.

A regression floor for this advisory is now enforced by `scripts/check_verification_gates.py` (fails below fastmcp 3.2.0), which runs in preflight and in CI.

