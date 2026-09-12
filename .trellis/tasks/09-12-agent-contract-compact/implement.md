# Implement: agent-facing contract

1. `mcp_server/src/sts2_mcp/server.py`: `_agent_state` marker, `_wait_until_actionable_impl`
   `actionable` key, new `reward_choice` tool kind + registration.
2. `mcp_server/src/sts2_mcp/client.py`: `resolve_rewards(option_index=None, card_index=None)`.
3. `skills/sts2-mcp-player/SKILL.md` and `references/screen-playbooks.md`: the
   field-name table from `design.md`, the confirm rule, `wait_for_event`, and the
   new screen notes.
4. `mcp_server/README.md`: compact shop flag name; keep the guided tool list in sync.
5. Tests: extend `test_game_data_tools.py`-style DummyClient patterns in a new
   `mcp_server/tests/test_agent_contract.py`; update `test_waits.py` for the new key.

## Validation

```bash
cd mcp_server && uv run --locked python -m unittest discover -s tests
```

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release
```

## Review gates

- Every field named in the skill must be greppable in the compact payload builder;
  the new test enforces it.
- Confirm `get_raw_game_state` is still the only tool that returns the full payload.

## Rollback

Single commit; docs-only impact outside the two small Python behaviors.
