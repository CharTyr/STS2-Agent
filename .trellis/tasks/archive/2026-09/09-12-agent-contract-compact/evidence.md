# Evidence: agent-facing contract (2026-09-12)

## Implementation

| Behavior | Where |
| --- | --- |
| `compact_agent_view` marker on every `get_game_state` return path | `mcp_server/src/sts2_mcp/server.py` `_agent_state` |
| `actionable` on every `wait_until_actionable` return path | `server.py` `_wait_until_actionable_impl` |
| `reward_choice` legacy tool kind + client `card_index` passthrough | `server.py` spec/dispatch, `client.py` `resolve_rewards` |
| Compact field names in the gameplay skill | `skills/sts2-mcp-player/SKILL.md`, `references/screen-playbooks.md`, `skills/sts2-mcp-player/README.md` |

Compact names were re-verified against their builders in
`GameStateService.BuildAgentViewPayload`: `selection.min|max|selected|confirm`,
`shop.open`, `character_select.embark`, `timeline.slots[].i|line|actionable`,
`chest.claimed`.

## Review round

The reviewer added the two raw-only field names the first pass missed
(`is_locked`, `will_kill_player` in the event recipe, plus `index` where the
compact option key is `i`), added them to the regression guard list, and covered
the SSE-failure polling path plus the marker-echo behavior with four more tests.

It confirmed the shared play-contract markers are intact and unique
(`SKILL.md:48`/`:131`), which is what `McpPlayerSkillTests` and the embedded mod
resource depend on.

## Commands actually run

```text
cd mcp_server; uv run --locked python -m unittest discover -s tests   -> Ran 76 tests, OK
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release -> 265 PASS / 0 FAIL
python scripts/check_verification_gates.py                            -> passed
```

## Known follow-ups

- The five new screen names are documented here but only become reachable once
  `09-12-screen-index-contract` implements them in `ResolveNonModalScreen` with
  the same strings.
- `PlayPrompt.BuildPlaySystem()` lists `wait_until_actionable` but not
  `wait_for_event`; that is correct, because `wait_for_event` is the Python
  sidecar's own tool and the native alignment test pins exactly that difference.

## Not verified

No live game session; the contract claims are offline behavior of the sidecar and
the state builder.
