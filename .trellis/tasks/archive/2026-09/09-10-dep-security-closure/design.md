# Design: dependency security closure

## Boundary

Two package managers, two lockfiles, no application source change.

| Manager | Manifest | Lock | Vulnerable pin | Fix |
| --- | --- | --- | --- | --- |
| uv | mcp_server/pyproject.toml | mcp_server/uv.lock | fastmcp 3.1.0 | re-resolve inside the existing range |
| npm | package.json | package-lock.json | fast-uri 3.1.0 (plus 8 more) | npm audit fix inside semver ranges |

## Why re-resolve instead of hand-editing

Both lockfiles carry integrity hashes. Editing a version string by hand leaves a wrong hash and produces a lock that fails on install. The supported tool (uv lock --upgrade-package fastmcp, npm audit fix) rewrites version, resolved URL, and integrity together, which is the only shape a verifier can trust.

## Tradeoffs

- Re-resolving fastmcp also moves its transitive Python dependencies. The MCP unit suite and the guided tool-profile check are the guard against a behaviour change; both run offline.
- npm audit fix touches nine packages, not only fast-uri. Fixing only fast-uri would leave the audit red and re-open the same report on the next scan, so the wider fix is the one that actually closes the goal.
- npm audit fix --force is rejected: it may install semver-major versions the manifest never declared.

## Rollback

Both files are tracked, so rollback is `git checkout -- <lockfile>`. The pre-existing yauzl override must survive the refresh; if it disappears, the lock is wrong and must be regenerated rather than patched.

## Compatibility

package.json and pyproject.toml are not rewritten unless a required fix falls outside the declared range. If that happens this design changes: the range edit must be recorded explicitly rather than folded into a lock refresh.
