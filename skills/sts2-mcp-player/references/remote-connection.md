# Remote Connection (Skill-Local)

This note is intentionally minimal and local to the skill directory.

## Runtime Priority

1. Try `local_server_name` first (recommended: `sts2-ai-agent`).
2. If local is unavailable, retry the same flow on `remote_server_name` (recommended: `sts2-ai-agent-remote`).
3. If aliases are not exposed but STS2 tools are present, call the tools directly.

## Minimal Checks

1. Call `health_check` on the selected server.
2. If healthy and no fresh state is available, start with `decide`, then `act` from its state.
   Reuse the resulting `state`, plan `final_state`, or wait state only when current. Check both
   `plan.stop_reason` and `plan.steps[].status`; if either reports an unknown outcome, obtain fresh
   state before deciding and never replay the uncertain action automatically.
3. Query game metadata only when the current descriptions and known facts leave a decision-critical
   gap; choose a single-item or batch lookup before a broader scene query when it suffices.

## Optional Host Plugin Configuration

For a conservative SubAgent profile use `max_concurrent = 1`, `auto_discover = false`,
`broadcast_iteration_progress = false`, `inject_status_to_main_prompt = false`,
`inject_completed_for_seconds = 120`, and `status_retention_seconds = 900`. Set
`max_mcp_tools_per_iteration = 1` and `share_to_main_chat = false`; one `act` can still carry
multiple combat steps in `then` when the schema supports it. The guided tool allowlist is:

```toml
allowed_tool_names = ["health_check", "get_game_state", "get_raw_game_state", "get_available_actions", "get_decision_log", "get_run_summary", "get_scene_guidance", "diff_state", "get_game_data_item", "get_game_data_items", "get_relevant_game_data", "wait_for_event", "wait_until_actionable", "decide", "act", "get_planner_briefing", "update_play_strategy"]
```

The authoritative profile list is `ESSENTIAL_TOOLS` in `scripts/test-mcp-tool-profile.ps1`.
If the plugin supports the built-in STS2 shortcut, its minimal connection configuration is:

```toml
[sts2]
enabled = true
local_server_name = "sts2-ai-agent"
remote_server_name = "sts2-ai-agent-remote"
```

## Notes

- Do not block on long MCP config debugging inside gameplay turns.
- Keep the same state-first loop regardless of local or remote transport.
