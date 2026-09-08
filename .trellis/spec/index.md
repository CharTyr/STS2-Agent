# Project Specifications

STS2 AI Agent combines a C# Godot mod, an optional Python MCP server, and build/validation scripts. Choose the applicable layer in this single repository.

| Layer | Scope |
| --- | --- |
| [Mod](mod/index.md) | STS2AIAgent and the executable C# test harness |
| [MCP](mcp/index.md) | Python transport, tool profiles, and tests |
| [Operations](operations/index.md) | Builds, live validation, and releases |
| [Shared guides](guides/index.md) | Cross-layer checks and code reuse |

## Pre-Development Checklist

Read [AGENTS.md](../../AGENTS.md), then the applicable layer index and linked contracts. Source references and named symbols are the evidence for these guidelines. Consult shared guides before changing action/state contracts, tool surfaces, constants, or helpers. Check the [gameplay skill](../../skills/sts2-mcp-player/SKILL.md) when changing gameplay workflows.

## Quality Check

Follow layer checks and [validation guidance](operations/validation-and-release.md). For documentation-only changes, verify local links, index coverage, source claims, and removal of unfilled sections. Confirm discovery with `python ./.trellis/scripts/get_context.py --mode packages`. Runtime changes require the relevant build and gameplay checks.
