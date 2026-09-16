# Contributing

## Branch workflow

- `main` is the protected release branch.
- `dev` is the default integration branch for ongoing development.
- Open pull requests from feature branches into `dev`.
- Open pull requests from `dev` into `main` when preparing a release or promoting tested changes.
- Do not push directly to `main`.

One habit keeps the two branches from drifting apart: after anything lands in `main` -- a
`dev -> main` merge, or a pull request that had to target `main` -- bring it into `dev`.

```bash
git fetch origin
git checkout dev
git merge origin/main
git push origin dev
```

A merge rather than a rebase, so published history is only ever added to. When `dev` has no commits
of its own yet, that merge resolves as a fast-forward and the two branches stay identical.

The invariant worth keeping is that `dev` contains everything `main` has. Skipping it is how `dev`
quietly becomes a fork of an older `main` instead of the integration branch: every merge into `main`
adds a commit `dev` does not have, and a pull request aimed straight at `main` adds all of its own.
It had fallen 196 commits behind before it was brought back on 2026-09-14.

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

1. Merge tested work into `dev`, recording each user-visible change under `## Unreleased` in
   `CHANGELOG.md`.
2. Validate release candidates from `dev`.
3. Open a `dev -> main` pull request.
4. Merge to `main` after final review.
5. Rename `## Unreleased` to the version being released, bump the five version files, and tag and
   publish from `main`.
6. Keep the `build-fingerprint.json` each packaging run writes: it is what the release record cites.

### Why `## Unreleased` is not optional

The same version number has been rebuilt and republished four times (v0.12.3 twice and v0.12.4
twice, all within three days). Every one of them started as a small fix that landed after the tag,
with nowhere in the changelog to record it -- so the choice each time was between spending a version
number on a one-line follow-up and re-cutting the one already published. Re-cutting won, and the
cost is permanent: three builds answer to `0.12.4` and the version string never tells them apart.

A post-tag change with a section to live in is a change that can wait for the next version.

### Telling two builds of one version apart

When a version is republished, `mod_version` is no longer enough to identify a build. Every
packaging run writes a `build-fingerprint.json` beside the artifact (`scripts/package-release.ps1`
and `scripts/package-steam-workshop.ps1`, both through `scripts/lib-build-fingerprint.ps1`) holding
each file's SHA256, the summed byte count Steam reports as `file_size`, and the source commit with a
dirty flag. Copy those numbers into the release record rather than collecting them by hand
afterwards, and quote them when asking a reporter which build they are running.
