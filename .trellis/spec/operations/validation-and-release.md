# Validation and Release

Run the commands below from the repository root (`C:\Users\chart\Documents\project\sp`). The command determines whether a game or Python environment is required and whether it changes files or game state.

## Offline checks

| Command | What it verifies | Side effects and limits |
| --- | --- | --- |
| `Push-Location mcp_server; uv run --locked python -m unittest discover -s tests -v; Pop-Location` | Python MCP unit tests using the standard-library `unittest` runner | No game required; tests use fakes and patched transport where appropriate |
| `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` | The custom executable C# core test harness | No game required; this is not a live Mod validation |
| `powershell -ExecutionPolicy Bypass -File scripts/test-mcp-tool-profile.ps1` | Offline MCP tool-profile checks | No game required; keep the repository-root working directory |
| `python scripts/check_verification_gates.py` | Seven offline gates: `lockfile`, `api-doc`, `api-facts`, `doc-marks`, `docs-tracked`, `script-encoding`, and `ps1-syntax` (each is described in the gate table below) | No game, no network, standard library only. Exits 1 with the failing gate named on stderr; select one or more gates with `--only api-doc\|api-facts\|doc-marks\|docs-tracked\|lockfile\|ps1-syntax\|script-encoding`. When the repository root has no `.git` directory, `docs-tracked` prints a skip note (and `ps1-syntax` skips when `scripts/` holds no `.ps1` or no interpreter is on `PATH`) instead of failing |
| `powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1` | Proves the gates above actually fail on drift, using a throwaway fixture in the temp directory | No game, no network; creates and removes its own fixture only |
| `powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1` | Build, Python compile/import, offline profile, unit-test, version, packaging-source, and release-document checks | Produces static preflight output. Its final “manual validation next” list means live gameplay still needs separate checks; see [preflight-release.ps1](../../../scripts/preflight-release.ps1#L158) |

The seven gates are:

| Gate | What it verifies |
| --- | --- |
| `lockfile` | Dependency security floors (`fastmcp`, `fast-uri`) plus manifest/lock agreement for `mcp_server/uv.lock` and `package-lock.json` |
| `api-doc` | Every action the `POST /action` switch accepts appears in the `docs/api.md` action contract block, and the block lists no retired action |
| `api-facts` | Facts `docs/api.md` states that code owns: the documented `mod_version` vs `mod_manifest.json`, the screen enum vs `GameStateService.ResolveNonModalScreen`, and the documented default port vs `HttpServer.DefaultPort` |
| `doc-marks` | Date-stamped validation records carry a historical marker, and archived topic pages keep their redirect to `history/` |
| `docs-tracked` | Every Markdown page under `docs/` is tracked by git, so a newly written page cannot fall out of a fresh checkout (which is what CI builds). Skips with a note when the repository root has no `.git` |
| `script-encoding` | PowerShell scripts containing non-ASCII text carry a UTF-8 BOM |
| `ps1-syntax` | Every `.ps1` under `scripts/` parses without a syntax error, checked with the PowerShell AST parser so each script is read but never executed. Skips with a note when `scripts/` holds no `.ps1` or no PowerShell interpreter is on `PATH` |

The profile command runs [test-mcp-tool-profile.ps1](../../../scripts/test-mcp-tool-profile.ps1); the C# command targets [STS2AIAgent.Tests.csproj](../../../STS2AIAgent.Tests/STS2AIAgent.Tests.csproj).

The preflight script is the canonical source for the Python test command: it enters `mcp_server/` and runs `uv run --locked python -m unittest discover -s tests -v` ([source](../../../scripts/preflight-release.ps1#L92)).

## Script inventory

These are the offline check entry points, plus the scripts that are deliberately left out of every automated check.

| Entry point | Command | What it checks |
| --- | --- | --- |
| Offline verification gates | `python scripts/check_verification_gates.py` | The seven gates above; `--only <gate>` narrows the run |
| Release metadata | `python scripts/check_release_metadata.py` | The five version sources below still agree |
| Packaging source contract | `python scripts/check_release_package.py --source-root .` | The packaging script still collects the player-facing files (source mode; artifact mode inspects a real release directory or zip and is not an offline check) |
| Budget proxy self-test | `python scripts/sts2-model-budget-proxy-selftest.py` | No-cost offline self-test of the validation budget proxy; asserts the real ledger is untouched and never calls the paid upstream |
| Gate drift self-test | `powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1` | Proves the gates fail on drift, using a throwaway fixture |
| MCP tool profiles | `powershell -ExecutionPolicy Bypass -File scripts/test-mcp-tool-profile.ps1` | `guided` / `layered` / `full` tool registration |
| PowerShell failure propagation | `powershell -ExecutionPolicy Bypass -File scripts/test-native-exit-propagation.ps1` | A failing native command propagates as a script failure |
| Static preflight | `powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1` | Aggregates the offline checks plus build, compile, version, package-source, and release-document checks |

The release path guards the version contract twice: [preflight-release.ps1](../../../scripts/preflight-release.ps1#L96) re-checks the metadata inline, and [package-release.ps1](../../../scripts/package-release.ps1#L82) aborts before building when [check_release_metadata.py](../../../scripts/check_release_metadata.py) reports an inconsistency.

Some scripts are deliberately not wired into any automated check. They are not dead code; they are manual or real-machine entry points:

- [scripts/scan-assembly-strings.ps1](../../../scripts/scan-assembly-strings.ps1#L1) requires a real `sts2.dll` — its `$AssemblyPath` parameter is mandatory ([L2](../../../scripts/scan-assembly-strings.ps1#L2)) — so it only runs on a machine with the game installed. Its invocation is recorded in [docs/reverse-engineering.md](../../../docs/reverse-engineering.md#L297).
- [scripts/generate-sts2-knowledge.ps1](../../../scripts/generate-sts2-knowledge.ps1#L15) regenerates `docs/game-knowledge/*.md` from `extraction/decompiled`, which is gitignored and absent from CI, so it cannot run in a fresh checkout.
- [scripts/sts2-coop-full-run-acceptance.ps1](../../../scripts/sts2-coop-full-run-acceptance.ps1#L28) and [scripts/test-coop-play-together.ps1](../../../scripts/test-coop-play-together.ps1#L1) are two-instance (host + companion) acceptance orchestration: they need a live game, two Mod API ports ([L2](../../../scripts/test-coop-play-together.ps1#L2)), and the model key.
- [scripts/sts2-validation-secrets.ps1](../../../scripts/sts2-validation-secrets.ps1#L3) only supplies that DPAPI-protected key material, and is dot-sourced by the coop acceptance script alone ([L33](../../../scripts/sts2-coop-full-run-acceptance.ps1#L33)).

## Game-connected validation

The shared validation entry point registers these subcommands in [build_parser](../../../scripts/run_sts2_validation.py#L2728):

```powershell
python scripts/run_sts2_validation.py mod-load
python scripts/run_sts2_validation.py state-summary
python scripts/run_sts2_validation.py state-invariants
```

These state and mod checks require the game and Mod API to be online. The Python-dependent profile check uses the MCP project's environment while keeping the script path rooted at the repository:

```powershell
uv run --project mcp_server python scripts/run_sts2_validation.py mcp-tool-profile
```

Other lifecycle, combat, multiplayer, and debug-gating subcommands are also registered by the same parser. Some suites can start or stop processes or mutate a game run; inspect the selected suite before execution and report those prerequisites and effects.

## Build and package behavior

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-mod.ps1 -Configuration Release
powershell -ExecutionPolicy Bypass -File scripts/build-mod.ps1 -Configuration Release -SkipInstall
powershell -ExecutionPolicy Bypass -File scripts/package-release.ps1 -Configuration Release
```

- The default [build script](../../../scripts/build-mod.ps1#L110) builds the C# DLL, packs the PCK, and copies the mod artifacts into the game's `mods/` directory. Close the game before using this mode so a loaded DLL is not locked.
- `-SkipInstall` still builds and stages the DLL/PCK but skips copying into the game directory ([install gate](../../../scripts/build-mod.ps1#L145)). Use it for packaging or a build-only check.
- The [package script](../../../scripts/package-release.ps1#L130) calls `build-mod.ps1 -SkipInstall`, copies the release contents, creates a zip, and validates both the release directory and zip ([artifact checks](../../../scripts/package-release.ps1#L183)).

## Release metadata

[check_release_metadata.py](../../../scripts/check_release_metadata.py) is the source of truth for this contract. It reads all five version sources, requires the version to match `major.minor.patch[-suffix]`, and exits nonzero unless every one of them is identical:

1. [STS2AIAgent/mod_manifest.json](../../../STS2AIAgent/mod_manifest.json) → `"version"` (the value the others are compared against)
2. [STS2AIAgent/mod_id.json](../../../STS2AIAgent/mod_id.json) → `"version"`
3. [STS2AIAgent/Server/Router.cs](../../../STS2AIAgent/Server/Router.cs) → `internal const string ModVersion = "x.y.z";` (a `private` const is also accepted)
4. [mcp_server/pyproject.toml](../../../mcp_server/pyproject.toml) → `project.version`
5. [mcp_server/uv.lock](../../../mcp_server/uv.lock) → the `version` of the `[[package]]` named `sts2-ai-agent-mcp`

[AGENTS.md](../../../AGENTS.md) lists the same five files, [preflight-release.ps1](../../../scripts/preflight-release.ps1#L96) re-checks them inline, and [package-release.ps1](../../../scripts/package-release.ps1#L82) runs the checker before it starts building, so a package cannot be produced from drifted metadata.

Static checks and package inspection do not prove that the Mod loads in the real game. Use the game-connected commands and the manual release checklist only when the task authorizes those side effects.
