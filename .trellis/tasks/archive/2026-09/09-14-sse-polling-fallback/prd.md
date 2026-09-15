# 3. Failed event streams yield promptly to state polling

## Goal
When /events/stream is unavailable while /state works, wait_until_actionable must promptly poll within its remaining deadline and expose structured event_stream_error/source=polling. Avoid tight reconnection loops. Healthy SSE and direct wait_for_event deadline semantics remain bounded.

## Evidence
client.py:201-226 catches connection_error and retries without backoff to deadline. server.py:724-749 passes the entire wait window then polls only if time remains. Existing test_agent_contract FailingEventClient immediately raises and does not reproduce real transport behavior.

## Acceptance
Real-client failed-SSE/working-state deterministic regression, bounded connect attempts, structured failure; healthy SSE tests; uv run --locked python -m unittest discover -s tests -v in mcp_server
Record actual checks and live-environment limits.

## Scope
mcp_server/src/sts2_mcp/client.py and server.py; tests/test_waits.py and test_agent_contract.py
No release, provider calls or player-save mutation required.

## Authorization
Latest instruction (2026-09-15): organize tasks and stop. This is a planned task, not authorized for continued execution in this session. On resumption use xai/grok-4.6 xhigh subagents unless the user changes that preference.
