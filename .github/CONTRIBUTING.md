# Contributing

## Branch workflow

- `main` is the protected release branch.
- `dev` is the default integration branch for ongoing development.
- Open pull requests from feature branches into `dev`.
- Open pull requests from `dev` into `main` when preparing a release or promoting tested changes.
- Do not push directly to `main`.

Recommended branch naming:

- `codex/<topic>`
- `feat/<topic>`
- `fix/<topic>`
- `chore/<topic>`

## Validation expectations

For mod changes:

- Run `dotnet build "STS2AIAgent/STS2AIAgent.csproj"`
- Run `powershell -ExecutionPolicy Bypass -File "scripts/build-mod.ps1"`
- Run `powershell -ExecutionPolicy Bypass -File "scripts/test-mod-load.ps1"`

For MCP server changes:

- Run `cd "mcp_server"` then `uv sync`
- Run `uv run sts2-mcp-server`
- Verify the server can reach `/health` or `/state` from a running mod

## Release flow

1. Merge tested work into `dev`.
2. Validate release candidates from `dev`.
3. Open a `dev -> main` pull request.
4. Merge to `main` after final review.
5. Tag and publish the release from `main`.
