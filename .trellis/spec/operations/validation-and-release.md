# Validation and Release

Run the commands below from the repository root (`C:\Users\chart\Documents\project\sp`). The command determines whether a game or Python environment is required and whether it changes files or game state.

## Offline checks

| Command | What it verifies | Side effects and limits |
| --- | --- | --- |
| `Push-Location mcp_server; uv run --locked python -m unittest discover -s tests -v; Pop-Location` | Python MCP unit tests using the standard-library `unittest` runner | No game required; tests use fakes and patched transport where appropriate |
| `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj` | The custom executable C# core test harness | No game required; this is not a live Mod validation |
| `powershell -ExecutionPolicy Bypass -File scripts/test-mcp-tool-profile.ps1` | Offline MCP tool-profile checks | No game required; keep the repository-root working directory |
| `python scripts/check_verification_gates.py` | Dependency security floors, manifest/lock agreement, the `docs/api.md` action contract, and doc-snapshot markers | No game, no network, standard library only. Exits 1 with the failing gate named on stderr; select one gate with `--only lockfile\|api-doc\|doc-marks` |
| `powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1` | Proves the gates above actually fail on drift, using a throwaway fixture in the temp directory | No game, no network; creates and removes its own fixture only |
| `powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1` | Build, Python compile/import, offline profile, unit-test, version, packaging-source, and release-document checks | Produces static preflight output. Its final “manual validation next” list means live gameplay still needs separate checks; see [preflight-release.ps1](../../../scripts/preflight-release.ps1#L142) |

The profile command runs [test-mcp-tool-profile.ps1](../../../scripts/test-mcp-tool-profile.ps1); the C# command targets [STS2AIAgent.Tests.csproj](../../../STS2AIAgent.Tests/STS2AIAgent.Tests.csproj).

The preflight script is the canonical source for the Python test command: it enters `mcp_server/` and runs `uv run --locked python -m unittest discover -s tests -v` ([source](../../../scripts/preflight-release.ps1#L87)).

## Game-connected validation

The shared validation entry point registers these subcommands in [build_parser](../../../scripts/run_sts2_validation.py#L2598):

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
- The [package script](../../../scripts/package-release.ps1#L110) calls `build-mod.ps1 -SkipInstall`, copies the release contents, creates a zip, and validates both the release directory and zip ([artifact checks](../../../scripts/package-release.ps1#L164)).

## Release metadata

Keep the release version synchronized in [`STS2AIAgent/mod_manifest.json`](../../../STS2AIAgent/mod_manifest.json), [`STS2AIAgent/Server/Router.cs`](../../../STS2AIAgent/Server/Router.cs), and [`mcp_server/pyproject.toml`](../../../mcp_server/pyproject.toml), as required by [`AGENTS.md`](../../../AGENTS.md). The preflight script also checks `mod_id.json` and the MCP lockfile, so a release can fail even when those three primary values match.

Static checks and package inspection do not prove that the Mod loads in the real game. Use the game-connected commands and the manual release checklist only when the task authorizes those side effects.
