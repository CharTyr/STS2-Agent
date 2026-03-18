# Remote Connection (Skill-Local)

This note is intentionally minimal and local to the skill directory.

## Runtime Priority

1. Try `local_server_name` first (recommended: `sts2-ai-agent`).
2. If local is unavailable, retry the same flow on `remote_server_name` (recommended: `sts2-ai-agent-remote`).
3. If aliases are not exposed but STS2 tools are present, call the tools directly.

## Minimal Checks

1. Call `health_check` on the selected server.
2. If healthy, continue with `get_game_state -> get_available_actions -> act`.
3. For card/monster/potion/shop/event metadata, prioritize:
   `get_relevant_game_data`, `get_game_data_item`, `get_game_data_items`.

## Notes

- Do not block on long MCP config debugging inside gameplay turns.
- Keep the same state-first loop regardless of local or remote transport.
