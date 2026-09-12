# Implement: docs and release baseline

1. Read the four sibling tasks' final state (their `prd.md` acceptance boxes and
   the actual code) before writing a single claim.
2. `PRODUCT_PLAN_CURRENT.md`: baseline, §1 unreleased list, §3 matrix, §4 待办.
3. `AGENTS.md`: version files, endpoint table, constant declaration,
   main-push rule.
4. `docs/api.md`: status table, `mod_version`, `/mcp`, new descriptor fields,
   new screens, timeline index contract, widened close actions.
5. `README.md` / `README.zh-CN.md`: mirror the two blocks, add the four routes.
6. `CONTRIBUTING.md`: main-push rule in sync with `AGENTS.md`.
7. `steam-workshop/workshop.json`: current change note text.
8. `scripts/preflight-release.ps1`: cover the CI-only gates.

## Validation

```powershell
python scripts/check_verification_gates.py
python scripts/check_release_package.py --source-root .
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
```

## Review gates

- Every status claim in `PRODUCT_PLAN_CURRENT.md` must cite a tag, a commit, or a
  command result from this session; "done" without a citation is a defect.
- The doc-mark gate must still accept the edited files (dated snapshots keep
  their markers).

## Rollback

Revert the commit; nothing executable depends on the prose.
