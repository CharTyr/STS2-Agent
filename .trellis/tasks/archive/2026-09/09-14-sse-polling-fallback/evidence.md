# Goal 3 evidence — SSE failure yields to polling

Date: 2026-09-15

## Change
`Sts2Client.iter_events` marks idle stream timeouts with details.kind=read_timeout. `wait_for_event` still retries those until the deadline. Other connection_error values (refused stream, HTTPError) raise immediately so `wait_until_actionable` can poll /state, set source=polling, and keep event_stream_error.

## Files
- mcp_server/src/sts2_mcp/client.py
- mcp_server/tests/test_waits.py

## Commands
From mcp_server: `uv run --locked python -m unittest discover -s tests -v`

## Results
199 tests OK. New test_wait_until_actionable_real_client_polls_when_event_stream_refused uses real Sts2Client.wait_for_event with urlopen raising URLError(ConnectionRefusedError). Existing deadline/reconnect test still opens the stream once.

## Limits
Offline unittest only. No live game SSE.

