# Dependency security closure for uv.lock and package-lock.json

## Goal

Close both open dependency reports (#50, #51) by moving the pinned vulnerable packages to fixed releases, so a fresh clone resolves no known-vulnerable dependency and the lockfiles stay reproducible.

## Requirements

- R1 `mcp_server/uv.lock` must resolve `fastmcp` at >= 3.2.0. Issue #50 reports CVE-2026-32871 (unencoded OpenAPI path parameters leading to authenticated SSRF) and states 3.2.0 fixes it. `pyproject.toml` already allows `>=3.1.0,<4.0.0`; only the lock is stale.
- R2 `package-lock.json` must resolve `fast-uri` at >= 3.1.6. Issue #51 reports CVE-2026-13676 (IDN hostname canonicalisation) for `<3.1.6`, and the version the issue suggests (3.1.3) is itself still inside a vulnerable advisory range.
- R3 `npm audit` must report zero known vulnerabilities for the tree, so the remaining advisories on the same dependency chain are not left half-fixed.
- R4 No source patch from either issue body is applied. The vulnerable code lives in third-party packages, not in tracked project files.
- R5 The refresh must not change project behaviour or break the pinned `yauzl` override, the guided MCP tool surface, or the C# core tests.

## Constraints

- Do not launch the game; every acceptance command must work offline.
- Do not use `npm audit fix --force`. Stay inside the semver ranges already declared in `package.json`.
- `node_modules/` is gitignored; only `package.json` and `package-lock.json` are tracked and may change.

## Acceptance Criteria

- [ ] `uv lock --check` (from `mcp_server`) exits 0 and `uv.lock` records `fastmcp` >= 3.2.0.
- [ ] `uv run --locked python -m unittest discover -s tests -v` passes.
- [ ] `uv run --locked python ../scripts/check_release_metadata.py` passes; the lock still carries the release version entry.
- [ ] `npm audit --json` reports `total: 0` vulnerabilities.
- [ ] `fast-uri` resolves at >= 3.1.6 and `yauzl` stays at the 3.2.1 override.
- [ ] `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` passes.
- [ ] Each claim above is backed by a command actually run in this session.

## Evidence Boundary

Lockfile state, an audit report, and unit-test runs are offline evidence. They do not prove in-game behaviour, and they are not a full third-party advisory audit beyond fastmcp and the npm tree.
