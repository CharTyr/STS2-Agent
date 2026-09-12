# Thread the reward-drain card choice through the request instead of a static field

Follow-up from `archive/2026-09/09-12-agent-trust-hardening` (child
`09-12-action-trust`), which fixed the silent wrong pick but left the choice in a
process-wide static field. The user picked option A: pass it as a parameter.

## Goal

`GameActionService._pendingCardRewardChoice` is a static field written by
`resolve_rewards` and consumed only if that drain actually reaches a card-reward
selection screen. When it does not, the value survives the request and can be
applied to a later `collect_rewards_and_proceed` — silently skipping a card
reward (`-2`) or picking a card the caller never asked for (`N`). Replace the
field with a value owned by the request so the cross-call leak is impossible by
construction rather than by remembering to reset it.

## Requirements

1. `resolve_rewards` passes its card choice to `DrainRewardFlowAsync` as an
   argument; the drain hands it to `TryResolveCardRewardAsync`.
2. `GameActionService` no longer declares or assigns `_pendingCardRewardChoice`;
   no reward choice survives the action call that created it.
3. `collect_rewards_and_proceed` explicitly asks for the automatic behavior
   (first card), and its call site says so.
4. Within a single drain, a consumed explicit choice still degrades to the
   automatic behavior for any later card-reward screen in the same drain, so the
   observable behavior inside one call is unchanged.
5. Documented semantic change: an explicit choice belongs to the call that carries
   it. Retrying with `resolve_rewards` keeps the choice (the request carries it);
   retrying a `pending` result with `collect_rewards_and_proceed` resolves card
   rewards automatically. This must be stated in `docs/api.md`.
6. Out of scope, recorded as a separate finding: `_cardRewardSkipped` is a second
   instance of the same static-state pattern, but it must outlive the request for
   the `skip_reward_cards` → `collect_rewards_and_proceed` sequence to keep
   working, so parameter threading does not apply to it unchanged. Do not change
   its behavior in this task.

## Acceptance Criteria

- [x] `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` passes: 278 PASS / 0 FAIL.
- [x] Source-contract tests fail if `GameActionService.cs` mentions `_pendingCardRewardChoice` again, if `DrainRewardFlowAsync` stops taking the choice, or if `resolve_rewards` stops forwarding the request's own choice to the drain (added by the reviewer).
- [x] A unit test pins the consume-once behavior (first read returns the explicit index, second returns the automatic choice) plus "two states do not share value".
- [x] A source-contract test pins that `collect_rewards_and_proceed` passes the automatic choice explicitly.
- [x] `resolve_rewards` still rejects an out-of-range explicit index before clicking, picks an exact valid index, keeps the first-card default with no index, and skips with `-1` (branch-by-branch equivalence table in `evidence.md`).
- [x] `_cardRewardSkipped` is byte-identical: all 9 lines unchanged between HEAD and the working tree.
- [x] `docs/api.md` states the retry semantics from requirement 5.
- [x] `python scripts/check_verification_gates.py` passes and `scripts/preflight-release.ps1` exits 0 with 12 OK steps.

## Constraints

- Offline only: no live game session. Claims are code contracts.
- No response field, action name, or error code changes.
- No version bump, tag, or release.

## Notes

- Evidence for the leak and for the second instance: `research/static-choice-leak.md`.
- Verification record: `evidence.md`.
- Recorded follow-up (not this task): `_cardRewardSkipped` keeps its static
  lifetime on purpose, and its timeout exit still leaves it set.
