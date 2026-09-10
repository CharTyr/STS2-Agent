# Evidence: stale document archive

Runner: main session. Date: 2026-09-10.

## What was wrong

`docs/sts2-coverage-gaps.md` (2026-03-10) listed CHEST, EVENT, REST, SHOP, POTION,
CHARACTER_SELECT and GAME_OVER as not covered. All of them are implemented now
(`GameStateService.BuildChestPayload`/`BuildEventPayload`/`BuildRestPayload`/`BuildShopPayload`/
`BuildGameOverPayload` and the matching handlers in `GameActionService`), so the document's
"待补齐" list read as a current backlog while describing the opposite of the source.

Dated validation records also had no signal that they are point-in-time snapshots.

## Change

- Archived the gap list to `history/sts2-coverage-gaps_2026-03-10.md` with a historical header that
  states which claims were superseded.
- `docs/sts2-coverage-gaps.md` is now a redirect page pointing at the archived copy, the current
  status page and the API action table — the same pattern `docs/phase-1c-status.md`,
  `docs/phase-4c-shop.md` and `docs/roadmap-current.md` already use.
- `history/README.md` index row added.
- Five date-stamped records gained a "历史快照 / Historical snapshot" marker:
  `2026-08-31-event-option-localization.md`, `2026-08-31-pr53-review-follow-up.md`,
  `phase-6-validation-2026-03-11.md`, `phase-6-validation-2026-03-30-dynamic-card-values.md`,
  `phase-8-validation-2026-03-11.md`.

## Evidence

| Command | Result |
| --- | --- |
| `python scripts/check_verification_gates.py --only doc-marks` | exit 0; lists the five marked snapshots and the `docs/sts2-coverage-gaps.md -> history/sts2-coverage-gaps_2026-03-10.md` redirect |
| `powershell -File scripts/test-verification-gates.ps1` | "doc-marks gate rejects an unmarked snapshot" PASS, "doc-marks gate rejects a broken archive redirect" PASS |
| `git status --short` | `history/sts2-coverage-gaps_2026-03-10.md` added; `docs/sts2-coverage-gaps.md` modified |

## Boundary

Only the documents whose claims were checked against source were touched. The remaining
`docs/` files were classified in the scout report but left alone when no contradiction was found;
this is not a claim that every document in the repository has been re-audited line by line.

