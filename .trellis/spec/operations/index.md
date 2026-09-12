# Operations and Release Guidelines

Operations scripts are the repository's executable boundary for offline checks, game-connected validation, mod builds, and release packaging. They are invoked from the repository root and have different side-effect levels.

## Guidelines

| Guide | Use it for |
| --- | --- |
| [Validation and release](./validation-and-release.md) | Exact commands, prerequisites, side effects, and release metadata |
| [MCP contracts and tests](../mcp/contracts-and-tests.md) | Python transport and tool behavior validated by the operations checks |

## Pre-Development Checklist

- Classify the change as offline/static, MCP-only, or game-connected before choosing a command. Read the [validation entry point](../../../scripts/run_sts2_validation.py) for the selected subcommand.
- Read [preflight-release.ps1](../../../scripts/preflight-release.ps1) before changing a check. Its output is a static preflight result and its final message points to separate manual validation.
- Check [build-mod.ps1](../../../scripts/build-mod.ps1) before building. Default mode copies artifacts into the game's `mods/` directory; `-SkipInstall` limits the operation to build/staging.
- Check [package-release.ps1](../../../scripts/package-release.ps1) before packaging. It invokes the mod build with `-SkipInstall`, creates a release directory and zip, and validates both artifacts.
- Confirm the game is closed before any build that overwrites a DLL currently loaded by the game.
- Confirm the five release version sources stay synchronized: [`mod_manifest.json`](../../../STS2AIAgent/mod_manifest.json), [`mod_id.json`](../../../STS2AIAgent/mod_id.json), [`Router.cs`](../../../STS2AIAgent/Server/Router.cs), [`pyproject.toml`](../../../mcp_server/pyproject.toml), and the `sts2-ai-agent-mcp` entry in [`uv.lock`](../../../mcp_server/uv.lock). [`check_release_metadata.py`](../../../scripts/check_release_metadata.py) is the authority; `package-release.ps1` refuses to build a package while they disagree.

## Quality Check

- Run commands from the repository root and state whether the selected check is offline or requires a running game.
- Treat `mod-load`, `state-summary`, and `state-invariants` as game-online checks. Use the MCP tool-profile validation with the MCP project's `uv` environment.
- Treat gameplay and lifecycle validation as potentially stateful. Inspect the selected suite before running commands that can start, stop, or mutate a game session.
- Do not describe a static preflight, a compile, or an offline profile check as proof of live gameplay.
- For documentation-only changes, verify Markdown paths and source claims without running builds, installation, packaging, or game validation.
