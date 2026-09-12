# Final integration review (2026-09-12)

## Children, all delivered and archived

| # | Child | Commit | Offline proof |
| - | --- | --- | --- |
| 1 | `09-12-action-trust` | `72c96fd` | 25 new C# tests; five defect classes closed |
| 2 | `09-12-bounded-game-waits` | `12c35b3` | 8 new C# tests incl. a mutation-proven source contract |
| 3 | `09-12-agent-contract-compact` | `33b137e` | 21 new MCP tests; skill field contract |
| 4 | `09-12-screen-index-contract` | `5457e0d` | 7 new C# tests; 26-entry screen mapping table |
| 5 | `09-12-docs-release-baseline` | `3f55a3f` | traceable claims; two reviewer corrections |

All five child directories are under `.trellis/tasks/archive/2026-09/`.

## Cross-child verification (run on the final tree)

```text
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release  -> 272 PASS / 0 FAIL  (baseline was 232; +40)
cd mcp_server; uv run --locked python -m unittest discover -s tests        -> Ran 76 tests, OK  (baseline was 55; +21)
python scripts/check_verification_gates.py                                 -> passed (api-doc 55 actions, doc-marks, lockfile, script-encoding)
python scripts/check_release_package.py --source-root .                    -> passed
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1     -> exit 0, 12 OK steps, "Static preflight complete."
```

## Cross-layer consistency checks

- The five new screen strings match character-for-character in the mod
  (`GameStateService.cs:6742-6746`), the gameplay skill, and `docs/api.md`.
- `requires_coordinates` / `requires_tool` exist in the descriptor
  (`GameStateService.cs:8042-8044`, set at `:524` / `:531`) and are documented in
  `docs/api.md:954-955`.
- The skill now names only compact fields for the view it tells the agent to
  read, enforced by an MCP test, while `docs/api.md` stays a raw-view document.
- `09-12-bounded-game-waits` recorded its new contract in
  `.trellis/spec/mod/game-actions.md` §"Waiting and frame safety".

## Requirements that carried a boundary (not overstated)

- Offline only: no live game session was run. Every behavior claim is a code
  contract plus offline tests; the affected screens were not driven in-game.
- No version bump, no new tag, no Workshop upload. Verified:
  `git diff 608c583..HEAD -- mod_manifest.json mod_id.json Router.cs pyproject.toml uv.lock`
  is empty and `git tag` still ends at `v0.11.0`.

## Deliberate behavior changes worth a release note

- `NPatchNotesScreen` no longer reports `screen = "MAIN_MENU"`; it is
  `PATCH_NOTES` and `close_main_menu_submenu` closes it.
- `resolve_rewards` with an explicit out-of-range index now fails instead of
  taking the first card, and `card_index: -1` is an error rather than "first card".
- `continue_run` / `embark` / `open_character_select` report `pending` while a
  modal blocks them, instead of `completed`.
- `/actions/available` descriptors gained two fields (always present, default
  `false`), and `get_game_state` gained `compact_agent_view`.

## Follow-ups handed back (not part of this tree)

- Decide whether `v0.10.7` should get a backfilled tag.
- Native-speaker review of the English label table.
- `AGENTS.md` is gitignored; the edit lives only in the working tree.
- A stale `_pendingCardRewardChoice` can outlive a `resolve_rewards` whose drain
  never reached a card-reward screen; the lifetime needs a product decision.
