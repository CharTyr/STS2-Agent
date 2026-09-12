# Design: agent-facing contract

## 1. Skill field names

Direction of the fix: the skill adapts to the compact view, because the skill's
workflow reads `get_game_state`. The compact names are also what
`GameStateService.BuildAgentViewPayload` emits today
(`GameStateService.cs:3145-3165` selection, `:3239-3260` shop, `:3348-3368`
character select, `:3370-3389` timeline, `:3391-3408` chest).

Edits:

| File:line | Before | After |
| --- | --- | --- |
| `SKILL.md:90` | `can_embark = true` | `embark = true` |
| `SKILL.md:114` | `shop.is_open = true` | `shop.open = true` |
| `references/screen-playbooks.md:11` | `timeline.slots[].state` like `obtained` | `timeline.slots[].actionable` (the compact slot carries `i`, `line`, `actionable`) |
| `references/screen-playbooks.md:22` | `character_select.can_embark` | `character_select.embark` |
| `references/screen-playbooks.md:54` | `min_select`/`max_select`/`selected_count`/`requires_confirmation`/`can_confirm` | `min`/`max`/`selected`/`confirm` plus the real confirm rule |
| `references/screen-playbooks.md:70` | `shop.is_open` | `shop.open` |
| `references/screen-playbooks.md:84` | `chest.has_relic_been_claimed` | `chest.claimed` |
| `SKILL.md:25` | tool list without `wait_for_event` | add `wait_for_event` |
| `mcp_server/README.md` (shop flag mention) | raw name | compact name |

Where the playbook describes the *raw* view on purpose, it must say so; the
default is the compact view.

Screen playbook additions (facts owned by the sibling task, names pinned here so
the two tasks cannot drift):

- `FAKE_MERCHANT` — the Fake Merchant event screen; `open_shop_inventory`
  opens its inventory, `proceed` leaves.
- `PATCH_NOTES` — patch notes; `close_main_menu_submenu` closes it.
- `CARD_INSPECT` / `RELIC_INSPECT` — inspect overlays; `close_cards_view`
  closes them.
- `FEEDBACK` — feedback form; no mod action closes it yet (user-triggered only).

## 2. `get_game_state` fallback

`server.py:481-491` today:

```python
def _agent_state() -> dict[str, Any]:
    state = sts2.get_state()
    agent_view = state.get("agent_view")
    if isinstance(agent_view, dict):
        if "available_actions" not in agent_view and isinstance(agent_view.get("actions"), list):
            return {**agent_view, "available_actions": agent_view["actions"]}
        return agent_view
    return state                      # <-- silent full payload
```

New behavior:

- `agent_view` present ⇒ `{**agent_view, ..., "compact_agent_view": True}`.
- `agent_view` missing ⇒ `{**state, "compact_agent_view": False}`.
- Docstring states both shapes; `get_raw_game_state` remains the way to ask for
  the full payload.

## 3. `wait_until_actionable` key

Every return path of `_wait_until_actionable_impl` (`:510-575`) and the tool
wrapper gains `"actionable": <bool>`:

- immediate-actionable branch ⇒ `matched: False, actionable: True`.
- event/polling branch ⇒ `matched: True|False, actionable: <fresh state check>`.

`matched` keeps its meaning ("an event matched"). The docstring explains that
`actionable` is the portable key shared with the native server
(`NativeMcpServer.cs:486-498` returns `actionable`).

## 4. Legacy `resolve_rewards` tool

Today `ActionToolSpec("resolve_rewards", "option_index", ...)` registers a tool
whose `option_index` is mandatory (`_register_option_index_tool`, `:377-383`),
while `docs/api.md` and `Sts2Client.resolve_rewards(option_index=None)` allow
omitting it and accept `card_index`.

Fix: add a new spec kind `"reward_choice"` and
`_register_reward_choice_tool`, which takes
`option_index: int | None = None, card_index: int | None = None` and calls
`handler(option_index=option_index, card_index=card_index)`.
`Sts2Client.resolve_rewards` gains `card_index: int | None = None` and forwards
it through `execute_action`. `test_legacy_action_coverage.py` keeps passing
(the method still exists on the client).

## Compatibility

- Additive keys only; `matched` and the mandatory-index behavior of other
  `option_index` tools are untouched.
- `get_game_state` still returns a dict with `screen`/`actions` in both shapes.

## Verification

- New/updated tests in `mcp_server/tests/`: compact marker both ways,
  `actionable` on every return path, `resolve_rewards` with and without
  `card_index`, and a skill-field test asserting the compact names appear and the
  raw-only names no longer appear in the skill files.
- `uv run --locked python -m unittest discover -s tests` from `mcp_server`.
- `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release`
  because `McpPlayerSkillTests` and `GameOverContractTests` read the skill files.

## Rollback

Revert the commit; no mod behavior depends on these changes.
