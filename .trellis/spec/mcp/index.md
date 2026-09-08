# MCP Server Guidelines

The Python MCP server is a synchronous sidecar around the local STS2 HTTP API. Keep transport behavior, tool registration, and the test contract aligned with the source files linked below.

## Guidelines

| Guide | Use it for |
| --- | --- |
| [Contracts and tests](./contracts-and-tests.md) | HTTP envelopes, action replay safety, tool profiles, and unit-test patterns |
| [Operations and release](../operations/index.md) | Validation entry points, build side effects, and release checks |

## Pre-Development Checklist

- Read the [client transport implementation](../../../mcp_server/src/sts2_mcp/client.py) before changing retries, action handling, event waits, or error fields.
- Read the [server registration and profile gates](../../../mcp_server/src/sts2_mcp/server.py) before adding or moving a tool.
- Check the [native tool alignment test](../../../mcp_server/tests/test_native_tool_alignment.py) when changing the guided surface.
- Decide whether the change affects only offline MCP behavior or also the live game API. Follow [validation-and-release](../operations/validation-and-release.md) for the matching command and side-effect boundary.
- Add or update a standard-library `unittest` case using the patterns in [contracts-and-tests](./contracts-and-tests.md). Do not assume that a tool must become `async` because FastMCP enumerates tools asynchronously.

## Quality Check

- Preserve the default `guided` profile and its compact action surface. The [profile normalizer and gates](../../../mcp_server/src/sts2_mcp/server.py) define `layered`/`planner`/`multi-agent` and `full`/`legacy` aliases.
- Preserve one-shot action semantics: ordinary reads may retry, but an ambiguous `POST /action` returns `outcome_unknown` after at most one state reconciliation. See the [request loop](../../../mcp_server/src/sts2_mcp/client.py#L691).
- Preserve the action envelope schema: `ok` is a boolean; failed responses carry string `error.code` and `error.message`, plus boolean `error.retryable`. See the [action envelope decoder](../../../mcp_server/src/sts2_mcp/client.py#L958).
- Keep network health failures structured. The [healthz endpoint](../../../mcp_server/src/sts2_mcp/network_server.py#L99) retains the STS2 error fields and returns an appropriate failure status.
- Run the focused MCP tests from the repository root by entering `mcp_server/` and using the command in [validation-and-release](../operations/validation-and-release.md). Report commands actually run; documentation-only work does not imply runtime or gameplay validation.
