# Validation and Release

Run the commands below from the repository root. The command determines whether a game or Python environment is required and whether it changes files or game state.

## Offline checks

| Command | What it verifies | Side effects and limits |
| --- | --- | --- |
| `Push-Location mcp_server; uv run --locked python -m unittest discover -s tests -v; Pop-Location` | Python MCP unit tests using the standard-library `unittest` runner | No game required; tests use fakes and patched transport where appropriate |
| `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` | The custom executable C# core test harness | No game required; this is not a live Mod validation |
| `powershell -ExecutionPolicy Bypass -File scripts/test-mcp-tool-profile.ps1` | Offline MCP tool-profile checks | No game required; keep the repository-root working directory |
| `python scripts/check_verification_gates.py` | Twelve offline gates: `lockfile`, `api-doc`, `api-facts`, `api-schema`, `arch-facts`, `doc-links`, `doc-marks`, `docs-tracked`, `packaged-links`, `script-encoding`, `ps1-syntax`, and `sh-syntax` (each is described in the gate table below) | No game, no network, standard library only. Exits 1 with the failing gate named on stderr; select one or more gates with `--only api-doc\|api-facts\|api-schema\|arch-facts\|doc-links\|doc-marks\|docs-tracked\|lockfile\|packaged-links\|ps1-syntax\|script-encoding\|sh-syntax`, or run everything except one with `--skip <gate>`. When the repository root has no `.git` directory, `docs-tracked` prints a skip note (and the two syntax gates skip when `scripts/` holds no script of their kind, or when no interpreter is on `PATH`) instead of failing |
| `python scripts/test-api-schema.py` | Validates the generated OpenAPI model's semantic contracts (source routes/methods, nullable C# wire mappings, typed teammate intent, opaque MCP/dynamic surfaces, and stale-byte rejection) | No game, no network, standard library only; no write outside a temporary file |
| `powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1` | Proves the gates above actually fail on drift, using a throwaway fixture in the temp directory | No game, no network; creates and removes its own fixture only |
| `powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1` | Build, Python compile/import, offline profile, unit-test, version, packaging-source, and release-document checks | Produces static preflight output. Its final “manual validation next” list means live gameplay still needs separate checks; see the closing "Manual validation next" list in [preflight-release.ps1](../../../scripts/preflight-release.ps1) |

The twelve gates are:

| Gate | What it verifies |
| --- | --- |
| `lockfile` | Dependency security floors (`fastmcp`, `fast-uri`) plus manifest/lock agreement for `mcp_server/uv.lock` and `package-lock.json` |
| `api-doc` | Every action the `POST /action` switch accepts appears in the `docs/api.md` action contract block, and the block lists no retired action |
| `api-facts` | Facts `docs/api.md` states that code owns. The documented `mod_version` vs `mod_manifest.json`, the screen enum vs `GameStateService.ResolveNonModalScreen`, the default port vs `HttpServer.DefaultPort`, every payload field against its record, the compact rename table against the `BuildAgent*` methods, and the `/health` keys. Since 2026-09-18 also the three surfaces besides the payload, each bidirectional: **error codes** (with their HTTP status, gathered from `ApiException`, `WriteErrorAsync` and `RestError`), **event types** published on `/events/stream`, and **routes** the router serves against the documented endpoint sections |
| `api-schema` | `docs/openapi.json` byte-matches the standard-library generator `scripts/api_schema.py`: Router route/method ownership, state/action C# wire properties, route-specific request/response wiring, and existing docs-backed shared vocabularies (actions, errors, screens, SSE event types). It does not invent detail for dynamic game-data exports, compact agent-view data, or MCP JSON-RPC envelopes; those surfaces are explicitly modeled as free-form / opaque where their source contracts are open-ended |
| `arch-facts` | The file table in `.trellis/spec/mod/architecture.md` against the files it measures: every listed file exists, its stated line count is within 5% of the real one, the stated file and line totals hold, and **every source file over 1,000 lines appears in the table**. That page went stale once already -- it carried pre-ADR-0001 counts and kept telling readers to add each new action to both action surfaces after that duplication was gone |
| `doc-links` | Every relative Markdown link in a tracked page resolves to a file in the repository, and a `#L<n>` anchor points at a line that file still has -- a line anchor rots as soon as the file is edited, and it rots quietly, because the link still opens. `packaged-links` answers the narrower question of whether the three shipped documents still resolve once they are outside the repo; this covers the other four hundred pages, where a link breaks for the dullest reason there is -- a file moved and the pages pointing at it did not. Skips with a note outside a git work tree |
| `doc-marks` | Date-stamped validation records carry a historical marker, and archived topic pages keep their redirect to `history/` |
| `docs-tracked` | Every Markdown page under `docs/` is tracked by git, so a newly written page cannot fall out of a fresh checkout (which is what CI builds). Skips with a note when the repository root has no `.git` |
| `packaged-links` | Every local link in the three packaged documents resolves inside the release artifact. Both the rewrite table and the shipped-file list are read from the packaging script and the artifact checker rather than copied here, so the gate cannot pass against a stale inventory |
| `script-encoding` | PowerShell scripts containing non-ASCII text carry a UTF-8 BOM |
| `ps1-syntax` | Every `.ps1` under `scripts/` parses without a syntax error, checked with the PowerShell AST parser so each script is read but never executed. Skips with a note when `scripts/` holds no `.ps1` or no PowerShell interpreter is on `PATH` |
| `sh-syntax` | Every `.sh` under `scripts/` parses (`bash -n`), and the offline path-resolver test `scripts/test-lib-sts2-paths.sh` passes. That test is the reason the POSIX resolution logic is checkable without macOS: it points `$HOME` at a fixture library and asserts the whole chain. Skips with a note when no usable bash is found -- Git Bash is preferred over the WSL launcher on Windows, which resolves on `PATH` and then cannot see a `C:/` path |

The profile command runs [test-mcp-tool-profile.ps1](../../../scripts/test-mcp-tool-profile.ps1); the C# command targets [STS2AIAgent.Tests.csproj](../../../STS2AIAgent.Tests/STS2AIAgent.Tests.csproj).

The preflight script is the canonical source for the Python test command: it enters `mcp_server/` and runs `uv run --locked python -m unittest discover -s tests -v` (the "MCP unit tests" step in [preflight-release.ps1](../../../scripts/preflight-release.ps1)).

## Script inventory

These are the offline check entry points, plus the scripts that are deliberately left out of every automated check.

| Entry point | Command | What it checks |
| --- | --- | --- |
| Offline verification gates | `python scripts/check_verification_gates.py` | The twelve gates above; `--only <gate>` narrows the run |
| Release metadata | `python scripts/check_release_metadata.py` | The five version sources below still agree |
| Packaging source contract | `python scripts/check_release_package.py --source-root .` | The packaging script still collects the player-facing files (source mode; artifact mode inspects a real release directory or zip and is not an offline check) |
| Budget proxy self-test | `python scripts/sts2-model-budget-proxy-selftest.py` | No-cost offline self-test of the validation budget proxy; asserts the real ledger is untouched and never calls the paid upstream |
| API schema semantics | `python scripts/test-api-schema.py` | Source route/method ownership, nullable wire-type mappings, typed teammate intent, explicit dynamic/opaque boundaries, and stale-byte refusal for `docs/openapi.json` |
| Gate drift self-test | `powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1` | Proves the gates fail on drift, using a throwaway fixture |
| MCP tool profiles | `powershell -ExecutionPolicy Bypass -File scripts/test-mcp-tool-profile.ps1` | `guided` / `layered` / `full` tool registration |
| PowerShell failure propagation | `powershell -ExecutionPolicy Bypass -File scripts/test-native-exit-propagation.ps1` | A failing native command propagates as a script failure |
| Static preflight | `powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1` | Aggregates the offline checks plus build, compile, version, package-source, and release-document checks |

The scripts that need the installed game share one resolver per platform, and both resolve in the same order: an explicit argument wins, then the environment variable, then detection, and only then the conventional location. On Windows that is [lib-sts2-paths.ps1](../../../scripts/lib-sts2-paths.ps1) -- `STS2_GAME_ROOT` / `STS2_EXE_PATH` / `STS2_APP_MANIFEST` / `STS2_STEAM_EXE`, detection through the registry and `libraryfolders.vdf`, last resort `C:/Program Files (x86)/Steam`. On macOS and Linux it is [lib-sts2-paths.sh](../../../scripts/lib-sts2-paths.sh) -- the same three of those names, detection through the three conventional Steam roots plus each installation's own `libraryfolders.vdf`, so a game in a second library is found there too.

Two things keep that honest. The layout knowledge lives in exactly one file per platform, which [test_posix_script_portability.py](../../../mcp_server/tests/test_posix_script_portability.py) enforces for the POSIX side the way its sibling enforces it for PowerShell. And the POSIX resolver is exercised offline by [test-lib-sts2-paths.sh](../../../scripts/test-lib-sts2-paths.sh), which runs inside the `sh-syntax` gate: it points `$HOME` at a fixture and asserts precedence, the vdf reader, the .app bundle layout and a second library, with no game, no Steam and no network. What still needs a real machine -- that the game starts, that the PCK packs, that a running process is recognised -- stays on the platform acceptance checklists rather than being claimed here.
The release path guards the version contract twice: [preflight-release.ps1](../../../scripts/preflight-release.ps1) runs the same metadata checker CI runs, and [package-release.ps1](../../../scripts/package-release.ps1) aborts before building (`Assert-ReleaseMetadataConsistent`) when [check_release_metadata.py](../../../scripts/check_release_metadata.py) reports an inconsistency.

Some scripts are deliberately not wired into any automated check. They are not dead code; they are manual or real-machine entry points:

- [scripts/scan-assembly-strings.ps1](../../../scripts/scan-assembly-strings.ps1) requires a real `sts2.dll` — its `$AssemblyPath` parameter is mandatory — so it only runs on a machine with the game installed. Its invocation is recorded under "临时替代方案" in [docs/reverse-engineering.md](../../../docs/reverse-engineering.md).
- [scripts/generate-sts2-knowledge.ps1](../../../scripts/generate-sts2-knowledge.ps1) regenerates `docs/game-knowledge/*.md` from `extraction/decompiled`, which is gitignored and absent from CI, so it cannot run in a fresh checkout.
- [scripts/sts2-coop-full-run-acceptance.ps1](../../../scripts/sts2-coop-full-run-acceptance.ps1) and [scripts/test-coop-play-together.ps1](../../../scripts/test-coop-play-together.ps1) are two-instance (host + companion) acceptance orchestration: they need a live game, two Mod API ports (`-HostApiPort` / `-CompanionApiPort`), and the model key. Which Steam profile counts as "do not touch" is detected from the machine or given with `-SteamAccountId`; no account id is baked into the script.
- [scripts/sts2-validation-secrets.ps1](../../../scripts/sts2-validation-secrets.ps1) only supplies that DPAPI-protected key material, and is dot-sourced by the coop acceptance script alone (its `$Secrets` path).

Three defects came out of the same sweep and are fixed with the resolver: `build-mod.ps1` used to let `New-Item -Force` absorb a wrong install path by creating an empty tree (the `Test-Path -LiteralPath $GameRoot` check in [build-mod.ps1](../../../scripts/build-mod.ps1)), a truncated `libraryfolders.vdf` used to throw inside the resolver rather than fall through, and `test-debug-console-gating.ps1` takes an `-ApiPort` that it now hands to the game it launches through `STS2_API_PORT`, the variable the mod actually reads (`IsExplicitPortConfigured` in [HttpServer.cs](../../../STS2AIAgent/Server/HttpServer.cs)).

The isolated-profile seeder has its own root, one level below those game paths: `start-game-session` writes `SlayTheSpire2/default/<clientId>/settings.save`, and on Windows the game only reads it from `%APPDATA%\SlayTheSpire2`. PowerShell already defaulted there; the POSIX script answered with the XDG location on every non-macOS system, so under Git Bash or MSYS it seeded a directory the game never opens while still reporting a successful seed. It now resolves `STS2_SLAY_USER_ROOT`, then `%APPDATA%` when `uname` reports MINGW/MSYS/CYGWIN, then the platform default, and warns when the resolved root does not exist.

## Game-connected validation

The shared validation entry point registers these subcommands in `build_parser` in [run_sts2_validation.py](../../../scripts/run_sts2_validation.py):

```powershell
python scripts/run_sts2_validation.py mod-load
python scripts/run_sts2_validation.py state-summary
python scripts/run_sts2_validation.py state-invariants
```

`patch-check` composes the four checks a game-version bump has to survive into one command, because "remember four commands in the right order" is how a patch regression ships:

```powershell
python scripts/run_sts2_validation.py patch-check
```

It requires `reflected_members_missing` to be `0` (otherwise the build is unsupported and the run stops before the expensive steps), then runs the deep mod load, the ADR 0001 state/descriptor invariants, and a replay of the recorded action surface. The replay compares the current screen against the newest `build/validation-*/action-surface-baseline.jsonl` (or `--baseline <path>`), and **only fails on a flag that disagrees for an action present on both sides** — a baseline sample was taken in some other run state, so an action set that differs between saves is reported as `only_in_state` / `only_in_baseline` rather than a failure. A screen the baseline never sampled is reported with `baseline_screen_present: false` and is not a pass; likewise a checkout with no baseline file at all. That baseline lives under gitignored `build/`, so a fresh checkout skips the replay step and says so.

These state and mod checks require the game and Mod API to be online. The Python-dependent profile check uses the MCP project's environment while keeping the script path rooted at the repository:

```powershell
uv run --project mcp_server python scripts/run_sts2_validation.py mcp-tool-profile
```

Other lifecycle, combat, multiplayer, and debug-gating subcommands are also registered by the same parser. Some suites can start or stop processes or mutate a game run; inspect the selected suite before execution and report those prerequisites and effects.

`state-invariants` demands an action exactly where the executor would accept it. Its combat branch requires `play_card` only while `combat.action_readiness.can_use_combat_actions` is true, which is the executor's whole readiness chain, not merely `player_action_phase`: a snapshot taken while a played card is still resolving reports the local player's turn with playable cards in hand, so gating on the turn predicate alone reports a missing action that was never expected. Payloads without the readiness field fall back to `player_action_phase`, and payloads with neither keep the old demand.

### Console travel names are not screen names

A suite that moves the run with `run_console_command` uses the game's internal room names, which are not the `screen` values the mod reports. A rest site is `room RestSite`, not `room Rest` (`room Rest` answers `Room 'REST' not found`); a treasure room is `room Treasure`. Getting this wrong looks like a mod failure and is not one.

Re-issuing a travel command while already standing in the resulting room answers 409. That is idempotence rather than a contract failure, so check the current screen before travelling. The notes are kept next to the helper in [run_sts2_validation.py](../../../scripts/run_sts2_validation.py) (`RUN_SETTLE_SECONDS` block) and for agents in [debug-and-validation.md](../../../skills/sts2-mcp-player/references/debug-and-validation.md).

## Build and package behavior

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-mod.ps1 -Configuration Release
powershell -ExecutionPolicy Bypass -File scripts/build-mod.ps1 -Configuration Release -SkipInstall
powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1 -Configuration Release
```

- The default [build script](../../../scripts/build-mod.ps1) builds the C# DLL, packs the PCK, and copies the mod artifacts into the game's `mods/` directory. Close the game before using this mode so a loaded DLL is not locked. The install location is resolved rather than assumed — argument, then `STS2_GAME_ROOT`, then detection through Steam's own library list — and a location that does not exist is refused up front (the `Test-Path -LiteralPath $GameRoot` check), so a typo can no longer create an empty directory tree.
- `-SkipInstall` still builds and stages the DLL/PCK but skips copying into the game directory (the `if (-not $SkipInstall)` block). Use it for packaging or a build-only check, and note that it is the one mode that does not need the game to be installed.
- The [package script](../../../scripts/package-release.ps1) calls `build-mod.ps1 -SkipInstall`, copies the release contents, creates a zip, and validates both the release directory and zip (`Invoke-ArtifactCheck`).
- Both packaging scripts write a `build-fingerprint.json` beside the artifact through [lib-build-fingerprint.ps1](../../../scripts/lib-build-fingerprint.ps1): every packaged file with its byte count and SHA256, the summed byte count Steam reports as `file_size`, and the source commit with a dirty flag. This exists because `mod_version` stops identifying a build the moment a version is republished, and this project has republished four times (v0.12.3 twice, v0.12.4 twice) -- three builds answer to `0.12.4` and only size or hash tells them apart. Those numbers used to be collected by hand after the upload; now they are produced by the run that made the artifact. The cross-version index is [history/build-fingerprints.md](../../../history/build-fingerprints.md).
- Record post-tag work under `## Unreleased` in [CHANGELOG.md](../../../CHANGELOG.md). Every one of those four re-cuts began as a fix that landed after a tag with nowhere in the changelog to go, so re-cutting the published version beat spending a new number on it. A change that has a section to live in is a change that can wait for the next release.

## Release metadata

[check_release_metadata.py](../../../scripts/check_release_metadata.py) is the source of truth for this contract. It reads all five version sources, requires the version to match `major.minor.patch[-suffix]`, and exits nonzero unless every one of them is identical:

1. [STS2AIAgent/mod_manifest.json](../../../STS2AIAgent/mod_manifest.json) → `"version"` (the value the others are compared against)
2. [STS2AIAgent/mod_id.json](../../../STS2AIAgent/mod_id.json) → `"version"`
3. [STS2AIAgent/Server/Router.cs](../../../STS2AIAgent/Server/Router.cs) → `internal const string ModVersion = "x.y.z";` (a `private` const is also accepted)
4. [mcp_server/pyproject.toml](../../../mcp_server/pyproject.toml) → `project.version`
5. [mcp_server/uv.lock](../../../mcp_server/uv.lock) → the `version` of the `[[package]]` named `sts2-ai-agent-mcp`

`AGENTS.md` (a local working file, deliberately untracked, so it is not a link: nothing a fresh checkout contains would resolve) lists the same five files, [preflight-release.ps1](../../../scripts/preflight-release.ps1) runs the same checker CI runs, and [package-release.ps1](../../../scripts/package-release.ps1) runs it (`Assert-ReleaseMetadataConsistent`) before it starts building, so a package cannot be produced from drifted metadata.

Static checks and package inspection do not prove that the Mod loads in the real game. Use the game-connected commands and the manual release checklist only when the task authorizes those side effects.
