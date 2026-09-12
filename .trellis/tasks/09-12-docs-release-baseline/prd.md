# Sync the state page, release baseline, and doc gates

Parent: `09-12-agent-trust-hardening` (goal 5 of 5).

## Goal

The repository's "current state" page still describes the v0.10.6 baseline and
lists work as unreleased that shipped in v0.10.7 / v0.11.0; the release checklist
in `AGENTS.md` names three version files while the preflight checks five; the
READMEs and `AGENTS.md` are missing four routes the server actually serves; and
the API doc contradicts itself about the `failed` action status.

## Requirements

1. `PRODUCT_PLAN_CURRENT.md` reflects the v0.11.0 tag: baseline, capability
   matrix, and the "post-tag unreleased" list. Work that shipped moves out of the
   unreleased section; work from this task tree is listed as post-tag unreleased
   with the real evidence state (offline-only where that is the truth).
2. `AGENTS.md` version management lists all five synchronized files
   (`mod_manifest.json`, `mod_id.json`, `Router.cs`, `pyproject.toml`,
   `uv.lock`) and describes the constant as it is (`internal const string`).
3. `AGENTS.md`, `README.md`, and `README.zh-CN.md` route lists include
   `POST /session/control`, `POST /companion/control`,
   `POST /companion/message`, and `GET /data/{collection}`.
4. `docs/api.md`: the action-status table lists `failed`; the `mod_version`
   example matches the current version; `/mcp` documents that the route accepts
   `/mcp` and `/mcp/`; the new `ActionDescriptor` fields, the five new screen
   names, the timeline index contract, and the widened `close_cards_view` /
   `close_main_menu_submenu` scope from `09-12-screen-index-contract` are
   documented.
5. README symmetry: the English-only "Default shape" block and the
   Chinese-only test-coverage sentence are mirrored in the other language.
6. `scripts/preflight-release.ps1` runs the same gate set CI runs (add the
   missing `test-verification-gates.ps1` / `test-native-exit-propagation.ps1`
   if they are not already covered), so a green preflight implies a green CI gate
   run.
7. `CONTRIBUTING.md` and `AGENTS.md` stop contradicting each other about
   pushing to `main`; pick the rule the repositories' history follows and make
   both say it.
8. `steam-workshop/workshop.json` `changeNote` no longer advertises v0.10.4.

## Acceptance Criteria

- [ ] `PRODUCT_PLAN_CURRENT.md` contains no v0.10.6-as-current claim and no "unreleased" entry whose commit is an ancestor of `v0.11.0`.
- [ ] `AGENTS.md` lists five version files and the correct constant declaration.
- [ ] All three route lists match the routes in `Router.HandleAsync`.
- [ ] `docs/api.md` documents `failed`, the current `mod_version`, the new descriptor fields, the new screens, and the widened close actions.
- [ ] Both READMEs contain the same two mirrored blocks.
- [ ] `preflight-release.ps1` still exits 0 and now covers the CI-only gates.
- [ ] `python scripts/check_verification_gates.py` and the doc-mark gate still pass.
- [ ] `python scripts/check_release_package.py --source-root .` still passes.

## Constraints

- Documentation-only except the preflight script; no behavior change.
- Historical documents under `history/` are not rewritten.
- `docs/api.md` is owned here; `mcp_server/README.md` is owned by
  `09-12-agent-contract-compact`.
