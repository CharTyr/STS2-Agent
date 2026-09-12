# Implement: dependency security closure

Order matters: the Python lock first (self-contained), then the npm tree, then the offline verification sweep.

## Step 1 - Python lock

- [ ] 1.1 From mcp_server/, run `uv lock --upgrade-package fastmcp` and confirm the diff moves fastmcp to >= 3.2.0.
- [ ] 1.2 Run `uv lock --check` (exit 0) and `uv run --locked python -m unittest discover -s tests -v`.
- [ ] 1.3 Run `uv run --locked python ../scripts/check_release_metadata.py` and confirm the release-version entry in uv.lock is intact.

## Step 2 - npm lock

- [ ] 2.1 Run `npm audit fix` from the repository root (no --force).
- [ ] 2.2 Confirm fast-uri >= 3.1.6 and that the yauzl override still resolves to 3.2.1.
- [ ] 2.3 Run `npm audit --json` and confirm total: 0.
- [ ] 2.4 If advisories remain, resolve them inside the declared semver ranges. Do not escalate to --force.

## Step 3 - Cross-check

- [ ] 3.1 `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj`.
- [ ] 3.2 Record every command and its exit status as task evidence.

## Validation Commands

```powershell
cd mcp_server
uv lock --upgrade-package fastmcp
uv lock --check
uv run --locked python -m unittest discover -s tests -v
uv run --locked python ../scripts/check_release_metadata.py

cd ..
npm audit fix
npm audit --json
npm ls fast-uri --all
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
```

## Review Gates

- No game instance is started at any point.
- No source file outside the two lockfiles is modified.
- A failing MCP test is a stop condition: revert the lock and re-plan.

## Rollback Points

- After step 1: revert mcp_server/uv.lock only.
- After step 2: revert package-lock.json (and package.json only if a range edit was unavoidable and recorded).
