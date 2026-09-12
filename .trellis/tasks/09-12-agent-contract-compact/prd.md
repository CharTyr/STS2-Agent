# Align the agent-facing contract: compact-state skill fields, get_game_state fallback, wait_until_actionable shape

Parent: `09-12-agent-trust-hardening` (goal 3 of 5).

## Goal

The MCP surface promises a compact state and the gameplay skill tells the agent
which fields to read, but five field names in the skill exist only in the raw
state, `get_game_state` silently returns the whole raw payload when
`agent_view` is missing, and `wait_until_actionable` returns a different field
name than the native server. Each item makes an agent read `undefined`.

## Requirements

1. `skills/sts2-mcp-player/` must read compact-state field names, because the
   skill's own workflow uses `get_game_state`. The five mismatches:
   `selection.min_select|max_select|selected_count|requires_confirmation|can_confirm`
   (compact: `min|max|selected|confirm`), `shop.is_open` (compact: `open`),
   `chest.has_relic_been_claimed` (compact: `claimed`),
   `character_select.can_embark` (compact: `embark`),
   `timeline.slots[].state` (compact: only `i|line|actionable`).
2. The skill must also state the real confirm rule for card selection
   (`RequiresConfirmation && CanConfirm` **or** `MinSelect < MaxSelect`), which
   the current text omits.
3. The skill's recommended `allowed_tool_names` must include `wait_for_event`,
   which every profile actually registers.
4. `get_game_state` must declare which shape it returned: add
   `compact_agent_view: true|false` to the tool result, keep returning
   `agent_view` when present, and document the fallback in the docstring.
5. `wait_until_actionable` must expose `actionable` alongside `matched` so the
   Python and native results agree on a machine-readable key.
6. The `full`-profile `resolve_rewards` tool must stop requiring
   `option_index`: the action accepts "no index ⇒ first card", and the
   `card_index` alias must be reachable from the tool surface.

## Acceptance Criteria

- [ ] No compact/raw field-name mismatch remains in `skills/sts2-mcp-player/`; every field the skill names exists in the view the skill tells the agent to read.
- [ ] `mcp_server/README.md` project-name example for the shop flag matches the compact name.
- [ ] `get_game_state` returns `agent_view` plus `compact_agent_view: true` when the mod exposes it, and the raw payload plus `compact_agent_view: false` otherwise.
- [ ] `wait_until_actionable` returns `actionable` on every return path, still returning `matched` for compatibility.
- [ ] `full`-profile `resolve_rewards` accepts a call with no index and a call with `card_index`.
- [ ] New Python unit tests cover all four behaviors; the existing 55 tests still pass.
- [ ] The C# suites still pass (the skill files are read by `McpPlayerSkillTests`).
- [ ] Screen names and actions referenced by the skill stay consistent with the mod: new names `FAKE_MERCHANT`, `PATCH_NOTES`, `CARD_INSPECT`, `RELIC_INSPECT`, `FEEDBACK` are documented as implemented by `09-12-screen-index-contract`, and `close_main_menu_submenu` / `close_cards_view` are described with their widened scope.

## Constraints

- Python style stays snake_case with complete type annotations.
- Tool result keys are additive; `matched` must keep working.
- Offline only.

## Notes

- Evidence: `research/agent-contract-audit.md`.
- This task owns `mcp_server/**` and `skills/**`; the sibling
  `09-12-screen-index-contract` owns `STS2AIAgent/**` and `docs/api.md`.
