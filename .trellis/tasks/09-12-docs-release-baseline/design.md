# Design: docs and release baseline

## Ordering

This task runs last: it documents the changes from
`09-12-action-trust`, `09-12-bounded-game-waits`,
`09-12-agent-contract-compact`, and `09-12-screen-index-contract`, so their
screen names, descriptor fields, and statuses must already be on disk.

## 1. PRODUCT_PLAN_CURRENT.md

Structure to preserve (it is the repository's single current-state page, rule set
at the bottom of the file, and `preflight` requires the path to exist):

- header: baseline = tag `v0.11.0` (commit `84631b9`) and the previous release;
- §1 current baseline: keep the v0.11.0 localization release entry, move the
  entries currently marked "主线未发布" that shipped in `v0.10.7`/`v0.11.0` into
  released wording with their real evidence, then add a new
  "标签后主线未发布变更" list covering this task tree;
- §3 capability matrix: mark `#82`, the pets/orb payload, and the proactive-chat
  fixes as released with the tag they shipped in; keep the "only verified on an
  isolated copy" boundary language;
- §4 待办: drop completed P-items, keep the genuinely deferred ones
  (real-upstream long-stall end-to-end, real-browser Origin, native-speaker
  review of the English table, `v0.10.7` tag backfill decision);
- §5 maintenance rules: unchanged.

Every claim must be traceable: a tag, a commit, a file under `history/`, or a
command run in this session.

## 2. AGENTS.md

- §版本号管理: five files, in the order the preflight checks them, and
  `internal const string ModVersion`.
- §API 端点: add the four missing routes with their purpose.
- §Release 流程 / CONTRIBUTING.md: state one rule. The repository's own history
  pushes release commits directly to `main`, and `AGENTS.md` documents that
  flow; `CONTRIBUTING.md:9` says the opposite. Follow the history: allow the
  maintainer's direct release push, and say so in both files.

## 3. docs/api.md

- Action status table: `completed` | `pending` | `failed`, matching
  `client.py:1037-1045` which raises `action_failed` on `failed`.
- `mod_version` example: current version, and note that it mirrors
  `Router.ModVersion`.
- `/mcp`: document that the router matches `/mcp` and `/mcp/` for the MCP
  JSON-RPC endpoint, and note the intended method (`POST`).
- New from the sibling tasks: descriptor fields `requires_coordinates` /
  `requires_tool`; screens `FAKE_MERCHANT`, `PATCH_NOTES`, `CARD_INSPECT`,
  `RELIC_INSPECT`, `FEEDBACK`; `choose_timeline_epoch` takes
  `timeline.slots[].index` and rejects non-actionable slots; `close_cards_view`
  also closes the inspect overlays; `close_main_menu_submenu` also closes the
  patch-notes screen.

## 4. READMEs

Mirror the two one-sided blocks: `README.md:169-181` (MCP default shape +
built-in tools line) into `README.zh-CN.md`, and
`README.zh-CN.md:198` (C# test coverage sentence) into `README.md`. Add the
four routes to both route lists.

## 5. Scripts

`scripts/preflight-release.ps1`: compare its check list with
`.github/workflows/validate.yml` and add what CI runs but preflight does not
(`scripts/test-verification-gates.ps1`, `scripts/test-native-exit-propagation.ps1`,
and confirm `scripts/test-mcp-tool-profile.ps1` is executed, not only named).
Keep the script's existing style and its final "manual steps" message.

## Verification

```powershell
python scripts/check_verification_gates.py
python scripts/check_release_package.py --source-root .
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
```

plus a link check over the edited Markdown files (the repository has no link
checker script; do it with a small read-only pass, as the previous docs task
did).

## Rollback

Documentation-only; revert the commit.
