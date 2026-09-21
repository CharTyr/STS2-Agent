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

`Sts2ApiError` exposes `status_code`, `code`, `message`, `details`, and `retryable`. The client, `Sts2Client` in [client.py](../../../mcp_server/src/sts2_mcp/client.py), uses synchronous `urllib.request` transport; `iter_events` and `wait_for_event` are synchronous iterators/helpers as well.

Ordinary reads may retry according to the client retry settings. An action is a `POST /action` request and is never automatically replayed. If its response cannot be read, is not valid JSON, or violates the action envelope, the client returns `status: "outcome_unknown"`, marks the response as not stable, and performs at most one `GET /state` reconciliation. `Sts2Client._request` and `_reconcile_action_state_once` in [client.py](../../../mcp_server/src/sts2_mcp/client.py) are the source of truth.

Action responses must be JSON objects with a boolean `ok`. A failed envelope must contain an object `error` with a non-empty string `code`, a string `message`, and a boolean `retryable`; malformed envelopes are `invalid_response`. See `_decode_action_response_envelope` in [client.py](../../../mcp_server/src/sts2_mcp/client.py).

## Typed payload contract

Only two responses have a fixed, mod-declared field set: `/actions/available` and `/decisions`. Their models live in [payloads.py](../../../mcp_server/src/sts2_mcp/payloads.py), which owns validation, defaulting, the extension policy, and `PayloadSchemaError(path, expected, actual)`. Everything else stays a plain dict on purpose — a `/state` snapshot is screen-shaped, a game-data collection is heterogeneous, an SSE frame is whatever was published, and an action result may be a recovery payload; typing those would either infect every consumer or discard information.

The policy, so a new payload does not re-decide it:

- A field the mod always sends is required and type-checked. A wrong type is an error, never a coercion — and `bool` is rejected where a count belongs, because `True` is an `int` in Python.
- A field an older build may omit is optional with a documented default (`requires_*` flags default `False`). Absence is not corruption.
- Unknown fields are retained and re-emitted by `to_wire()`, matching the additive public contract (`additionalProperties: true` in `docs/openapi.json`).

`Sts2Client.get_action_catalog()` and `Sts2Client.get_decision_entries()` are the typed getters; `Sts2Client._parse_payload` converts a `PayloadSchemaError` into `Sts2ApiError(status_code=200, code="invalid_response", retryable=False, details=exc.as_details())`. They are **added beside** `get_available_actions()` / `get_decisions()`, which keep returning the raw JSON values so no existing caller, fake, or tool has to migrate mid-release. A null `/decisions` body is an error rather than an empty list: "no decisions were recorded" and "the response was unusable" are different claims.

Tests: [test_client_payloads.py](../../../mcp_server/tests/test_client_payloads.py) covers accepted and malformed payloads, the defaulting and extension policy, the transport seam (including that an error envelope still surfaces as the mod's own `Sts2ApiError`), and the legacy dict getters.

## Tool registration contract

`_normalize_tool_profile` in [server.py](../../../mcp_server/src/sts2_mcp/server.py) defaults to `guided`, maps `planner` and `multi-agent` to `layered`, and maps `legacy` to `full`.

- Base tools are registered for every profile.
- Planner, combat handoff, and knowledge tools are registered for `layered` and `full`.
- Legacy per-action tools are registered only for `full`.
- `run_console_command` is a separate debug tool enabled only when `STS2_ENABLE_DEBUG_ACTIONS` is truthy; it is deliberately excluded from compact `act`.

These gates live in `create_server` in [server.py](../../../mcp_server/src/sts2_mcp/server.py), which calls `_register_legacy_action_tools` for `full` and registers the debug tool only when `_debug_tools_enabled()`. Keep the public profile names and the debug boundary stable when changing the tool surface.

Tool functions are ordinary synchronous `def` functions. FastMCP's tool listing is asynchronous, so tests commonly call `asyncio.run(server.get_tool("..."))` and then invoke `tool.fn(...)`. `WaitBehaviorTests` in [test_waits.py](../../../mcp_server/tests/test_waits.py), `GameDataToolsTests` in [test_game_data_tools.py](../../../mcp_server/tests/test_game_data_tools.py) and `CrystalSphereToolTests` in [test_crystal_sphere_tools.py](../../../mcp_server/tests/test_crystal_sphere_tools.py) demonstrate this pattern.

## Test patterns

Use the standard library `unittest` runner used by the project. Prefer small fakes over a live game:

- A `DummyClient` with queued states verifies wait and tool behavior without HTTP; see `DummyClient` in [test_waits.py](../../../mcp_server/tests/test_waits.py).
- A `RecordingClient` verifies action arguments and call count; see `RecordingClient` in [test_crystal_sphere_tools.py](../../../mcp_server/tests/test_crystal_sphere_tools.py).
- Patch `_ensure_game_data_index` when testing game-data selection and normalization; see `GameDataToolsTests` in [test_game_data_tools.py](../../../mcp_server/tests/test_game_data_tools.py).
- Patch `request.urlopen` and `time.sleep` to verify transport outcomes. The replay-safety tests in [test_action_replay_safety.py](../../../mcp_server/tests/test_action_replay_safety.py) (for example `test_action_transport_failures_post_once_and_reconcile`) assert one action POST, no action retry, and one reconciliation GET for an ambiguous result.
- `NativeToolAlignmentTests` in [test_native_tool_alignment.py](../../../mcp_server/tests/test_native_tool_alignment.py) compares the Python guided surface with the C# native MCP surface. Update both sides deliberately when a guided tool changes.
- [test_client_payloads.py](../../../mcp_server/tests/test_client_payloads.py) is the typed-model pattern: pure parser tests plus one transport test per getter, patching `request.urlopen` with a minimal `JsonResponse` double.

## Dependency refresh checklist

`fastmcp` is a meta distribution: since 3.4.x the implementation ships in the `fastmcp-slim`
distribution declared as `fastmcp-slim[client,server]==<version>`, while the `fastmcp` wheel only
contributes an `__init__.py` and the dependency edges. An in-place upgrade in an existing venv can
therefore leave a `site-packages/fastmcp/` directory that still contains the previous release's
subpackages but no `__init__.py`, and `from fastmcp import FastMCP` then fails with
`cannot import name 'FastMCP' from 'fastmcp' (unknown location)`.

After `uv lock --upgrade-package`, force a clean reinstall of both distributions:

```powershell
Push-Location mcp_server
uv lock --upgrade-package fastmcp
uv sync --locked --reinstall-package fastmcp --reinstall-package fastmcp-slim
uv run --locked python -m unittest discover -s tests -v
Pop-Location
```

Note that `uv sync` prunes anything the lock does not declare, so an ad-hoc `uv pip install pytest`
does not survive it. The project's runner is `python -m unittest`, so that is not a supported need.

The security floors themselves are enforced by `python scripts/check_verification_gates.py`; raise a
floor in that script only together with the lock that satisfies it.


## Change checklist

- Preserve existing response fields and error names for compatibility.
- Test both success and malformed/error envelopes when changing decoding.
- Test action transport loss, unreadable responses, and reconciliation failure when changing action handling.
- Test each affected profile and debug gate when changing registration.
- Keep the test command and its working directory explicit: from the repository root, enter `mcp_server/` and run `uv run --locked python -m unittest discover -s tests -v`.

## State diff and event stream regressions

Register a tool wrapper under its public name and import its implementation under a different local name. Exercise the registered `tool.fn` in every applicable profile; testing only a same-named pure helper will miss wrapper recursion.

The C# and Python state diff implementations share the same output contract. Clamp the entry limit to 1..200. Report truncation only after finding an additional omitted change, or when the depth limit prevents a complete comparison. An empty result proves equality only when `truncated` is false. Keep object, array-length, depth and string markers distinct. Quote non-identifier property names with JSON brackets and deterministic ASCII escapes so literal dots, brackets, Unicode and backslashes cannot collide with generated paths. Preserve native numeric output without converting integers through double; equivalent decimal spellings compare equal.

Event-stream connect timeouts and broken reads must surface as structured `Sts2ApiError` values so `wait_until_actionable` can fall back to state polling. Only an idle read timeout on an opened stream stays within `wait_for_event`. Cap both connection and read timeouts by the remaining overall deadline. Back off an immediate EOF reconnect without extending that deadline. Read failures in an HTTP error body must not replace the HTTP error with a bare exception.

Use a fake clock that advances during simulated I/O or sleep. Advancing time every time `monotonic` is read makes a harmless extra deadline check change test behavior.
