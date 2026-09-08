# MCP Contracts and Tests

## Client transport contract

Follow the existing Python style: snake_case functions and variables, typed
parameters and returns, and explicit optional fields such as `int | None`.
Keep wire keys compatible with the mod. The client's `_request` method makes
the action replay boundary explicit:

```python
last_error: Sts2ApiError | None = None
retry_count = 0 if action_post else self._max_retries
attempts = 1 + retry_count
```

`Sts2ApiError` exposes `status_code`, `code`, `message`, `details`, and `retryable`. The [client definition](../../../mcp_server/src/sts2_mcp/client.py#L46) uses synchronous `urllib.request` transport; `iter_events` and `wait_for_event` are synchronous iterators/helpers as well.

Ordinary reads may retry according to the client retry settings. An action is a `POST /action` request and is never automatically replayed. If its response cannot be read, is not valid JSON, or violates the action envelope, the client returns `status: "outcome_unknown"`, marks the response as not stable, and performs at most one `GET /state` reconciliation. The [request loop](../../../mcp_server/src/sts2_mcp/client.py#L691) and [reconciliation result](../../../mcp_server/src/sts2_mcp/client.py#L788) are the source of truth.

Action responses must be JSON objects with a boolean `ok`. A failed envelope must contain an object `error` with a non-empty string `code`, a string `message`, and a boolean `retryable`; malformed envelopes are `invalid_response`. See the [decoder](../../../mcp_server/src/sts2_mcp/client.py#L958).

## Tool registration contract

The [server profile normalizer](../../../mcp_server/src/sts2_mcp/server.py#L115) defaults to `guided`, maps `planner` and `multi-agent` to `layered`, and maps `legacy` to `full`.

- Base tools are registered for every profile.
- Planner, combat handoff, and knowledge tools are registered for `layered` and `full`.
- Legacy per-action tools are registered only for `full`.
- `run_console_command` is a separate debug tool enabled only when `STS2_ENABLE_DEBUG_ACTIONS` is truthy; it is deliberately excluded from compact `act`.

These gates live in [server registration](../../../mcp_server/src/sts2_mcp/server.py#L564) and [debug/legacy registration](../../../mcp_server/src/sts2_mcp/server.py#L843). Keep the public profile names and the debug boundary stable when changing the tool surface.

Tool functions are ordinary synchronous `def` functions. FastMCP's tool listing is asynchronous, so tests commonly call `asyncio.run(server.get_tool("..."))` and then invoke `tool.fn(...)`. The [wait tests](../../../mcp_server/tests/test_waits.py#L142), [game-data tests](../../../mcp_server/tests/test_game_data_tools.py#L42), and [crystal-sphere tests](../../../mcp_server/tests/test_crystal_sphere_tools.py#L45) demonstrate this pattern.

## Test patterns

Use the standard library `unittest` runner used by the project. Prefer small fakes over a live game:

- A `DummyClient` with queued states verifies wait and tool behavior without HTTP; see [test_waits.py](../../../mcp_server/tests/test_waits.py#L23).
- A `RecordingClient` verifies action arguments and call count; see [test_crystal_sphere_tools.py](../../../mcp_server/tests/test_crystal_sphere_tools.py#L11).
- Patch `_ensure_game_data_index` when testing game-data selection and normalization; see [test_game_data_tools.py](../../../mcp_server/tests/test_game_data_tools.py#L51).
- Patch `request.urlopen` and `time.sleep` to verify transport outcomes. The [replay-safety tests](../../../mcp_server/tests/test_action_replay_safety.py#L108) assert one action POST, no action retry, and one reconciliation GET for an ambiguous result.
- The [native alignment test](../../../mcp_server/tests/test_native_tool_alignment.py#L97) compares the Python guided surface with the C# native MCP surface. Update both sides deliberately when a guided tool changes.

## Change checklist

- Preserve existing response fields and error names for compatibility.
- Test both success and malformed/error envelopes when changing decoding.
- Test action transport loss, unreadable responses, and reconciliation failure when changing action handling.
- Test each affected profile and debug gate when changing registration.
- Keep the test command and its working directory explicit: from the repository root, enter `mcp_server/` and run `uv run --locked python -m unittest discover -s tests -v`.
