# Audit evidence: agent-facing contract (2026-09-12)

## 1. Compact/raw field-name mismatches in the gameplay skill

The skill tells the agent to use `get_game_state` (compact `agent_view`), but
five field names it names exist only in the raw payload.

| Skill text | Compact name | Raw name | Compact builder | Raw builder |
| --- | --- | --- | --- | --- |
| `selection.min_select/max_select/selected_count/requires_confirmation/can_confirm` (`references/screen-playbooks.md:54`) | `min`/`max`/`selected`/`confirm` (no `requires_confirmation`) | same names as the skill | `GameStateService.cs:3145-3165` | `:4265-4277` |
| `shop.is_open` (`SKILL.md:114`, `screen-playbooks.md:70`, `mcp_server/README.md`) | `open` | `is_open` | `:3239-3260` | `:4578` |
| `chest.has_relic_been_claimed` (`screen-playbooks.md:84`) | `claimed` (+ `opened`) | `has_relic_been_claimed` | `:3391-3408` | `:4662-4677` |
| `character_select.can_embark` (`SKILL.md:90`, `screen-playbooks.md:22`) | `embark` | `can_embark` | `:3348-3368` | `:4308` |
| `timeline.slots[].state` (`screen-playbooks.md:11`) | slot has only `i`/`line`/`actionable`; the state text is folded into `line` | slot has `state` | `:3370-3389` | `:7187-7199` |

Also missing: `wait_for_event` in the skill's `allowed_tool_names`
(`SKILL.md:25`) although every profile registers it
(`server.py:756-757`, `mcp_server/README.md:17`); and the confirm rule the
recipe needs, which is `RequiresConfirmation && CanConfirm` **or**
`MinSelect < MaxSelect` (`GameStateService.cs:920-937`) — the raw
`can_confirm` alone is not the rule the skill implies.

## 2. `get_game_state` silently returns the full payload

```text
server.py:481-491
  def _agent_state() -> dict[str, Any]:
      state = sts2.get_state()
      agent_view = state.get("agent_view")
      if isinstance(agent_view, dict):
          ...
          return agent_view
      return state
```

Docstring says "Read the compact agent-facing game state snapshot."
(`server.py:581`). The native tool has only the compact path
(`NativeMcpServer.cs:456-457`), so the two surfaces disagree and a fallback can
blow up the caller's context with no signal.

## 3. `wait_until_actionable` key divergence

- Python (`server.py:510-575`): `matched`, `event`, `state`, `actions`,
  `timeout_seconds`, `source`; the immediate branch returns
  `matched=False, source="state"`.
- Native (`NativeMcpServer.cs:486-498`): `actionable`, `timeout_seconds`,
  `state`, `actions`.

## 4. Legacy `resolve_rewards` tool required `option_index`

```text
server.py:62   ActionToolSpec("resolve_rewards", "option_index", "... use option_index -1 to skip card rewards.")
server.py:377-383  _register_option_index_tool -> def tool(option_index: int)   # mandatory
client.py:440  def resolve_rewards(self, option_index: int | None = None)
docs/api.md:986  "may carry option_index, or card_index"
GameActionService.cs:1694,1708-1710  card_index accepted as a backwards-compatible alias
```

So the full-profile tool cannot express what the action and the client both allow.

## 5. Consistency checks that already hold

- Action-name sets: mod switch (55) = `docs/api.md` action contract (55);
  `_LEGACY_ACTION_TOOLS` = 54 = 55 minus the debug-gated
  `run_console_command` (`server.py:58-114`).
- Guided surface: 9 native tools vs 10 Python tools, the extra one being
  `wait_for_event` by design (`test_native_tool_alignment.py:111-126`).
- Crystal Sphere tool arguments (`server.py:404-420`) match the skill.

## Out of scope here

- The mod-side index and screen fixes live in `09-12-screen-index-contract`.
- No live-game verification was performed.
