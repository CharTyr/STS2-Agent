# Evidence: docs and release baseline (2026-09-12)

## Changes

| File | What changed |
| --- | --- |
| `PRODUCT_PLAN_CURRENT.md` | baseline is tag `v0.11.0` @ `84631b9`; shipped work moved out of the unreleased list; a new post-tag list covers this task tree with commit ids; the capability matrix marks #82, pets/orbs and the proactive-chat fixes released; §4 keeps only the four genuinely deferred items |
| `docs/api.md` | action status lists `failed`; `mod_version` example is current; `/mcp` documents `/mcp` and `/mcp/`; descriptors document `requires_coordinates` / `requires_tool`; five new screens in the screen table; `choose_timeline_epoch`, `close_main_menu_submenu` and `close_cards_view` sections describe the new contracts |
| `README.md` / `README.zh-CN.md` | four missing routes on both sides; the English-only MCP default-shape block and the Chinese-only test-coverage sentence are now mirrored |
| `scripts/preflight-release.ps1` | runs `test-verification-gates.ps1` and `test-native-exit-propagation.ps1`, so a green preflight now implies a green CI gate run |
| `steam-workshop/workshop.json` | change note no longer advertises v0.10.4 |
| `AGENTS.md` (gitignored local file) | five version files, correct constant declaration, four added routes, release flow now matches `.github/CONTRIBUTING.md` |

## Review round

The reviewer checked every status claim against `git tag --contains` /
`git merge-base --is-ancestor` and corrected two statements that the evidence did
not support:

1. the `choose_timeline_epoch` error contract implied an out-of-range index also
   returns `slot_state` / `is_actionable`; those fields belong to the
   non-actionable-slot response only;
2. the claim that past releases were merged through pull requests is false for
   `v0.10.5`/`v0.10.6`/`v0.11.0` (all direct pushes); the sentence now says only
   that PR-merged records exist, while keeping the PR rule itself.

## Commands actually run

```text
python scripts/check_verification_gates.py                       -> passed (api-doc 55 actions, doc-marks, lockfile, script-encoding)
python scripts/check_release_package.py --source-root .          -> passed
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1 -> exit 0, 12 OK steps incl. the two new gates
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release -> 272 PASS / 0 FAIL (284 PASS in the preflight run, which also runs the self-tests)
cd mcp_server; uv run --locked python -m unittest discover -s tests -> Ran 76 tests, OK
Markdown link check over the edited files                        -> 31 links, 0 broken
```

## Known follow-ups

- `AGENTS.md` is listed in `.gitignore` (the file was deleted from the repository
  in `0827838`), so this edit lives only in the working tree. Making it tracked
  again is a separate repository-policy decision.
- The `docs/api.md` screen table still omits `BUNDLE_SELECTION`,
  `CAPSTONE_SELECTION`, and `TIMELINE`, which were never listed there.
- The Workshop `file_size` / `time_updated` values quoted on the status page were
  not re-verified against the Steam Web API in this session.

## Not verified

Documentation and static preflight only; no live game session was run.
