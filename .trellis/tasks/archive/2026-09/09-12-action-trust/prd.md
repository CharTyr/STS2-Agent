# Action trust: no silent wrong picks, no fake success, no swallowed failures

Parent: `09-12-agent-trust-hardening` (goal 1 of 5).

## Goal

An action response must mean what it says. Today three classes of defect let the
mod report `completed` for work that did not happen, pick a card the caller did
not ask for, or hide a failed purchase from the caller.

## Requirements

1. **No silent wrong pick.** `resolve_rewards` with an explicit out-of-range
   `option_index` (or the `card_index` alias) must fail with an error the caller
   can act on. It must never substitute the first card and report `completed`.
   The documented "no index given ⇒ first card" behavior stays.
2. **No fake success from a modal.** `continue_run`, `embark`, and
   `open_character_select` must not treat "some modal is open" as proof that
   the menu transition or the character-select open happened. A blocking modal
   is an unsettled transition, and the response must say so (`pending`), so the
   caller can read the modal actions from the fresh state.
3. **No swallowed purchase failure.** `remove_card_at_shop` keeps its
   non-blocking behavior for the success path (the game blocks on deck
   selection), but a purchase that already failed must surface as an error
   instead of a bare `pending`.
4. **No empty-state success.** `choose_bundle` / `confirm_bundle` must not
   return `completed` with a default empty state object when the state snapshot
   could not be built.
5. **Counters match reality.** A `play_card` that never leaves the hand must not
   leave the mod's mid-turn card counters incremented.

## Acceptance Criteria

- [x] `resolve_rewards` with an out-of-range index returns HTTP 409 `invalid_target` and does not click anything. (request-time check when the option list is authoritative, consume-time check otherwise; both throw before any click)
- [x] `resolve_rewards` with a valid index still picks that exact card; with no index it still picks the first; with `-1` it still skips.
- [x] A modal opened during `continue_run` / `embark` / `open_character_select` yields `status="pending"`, `stable=false`, and a message naming the blocking modal.
- [x] A failed `remove_card_at_shop` purchase returns 409 with the failure text instead of `pending`; the success path still returns without waiting for deck selection.
- [x] Bundle actions never return `completed` together with a fabricated empty state (snapshot failure ⇒ 503 `state_unavailable`).
- [x] A `play_card` that ends with the card still in hand rolls the mid-turn counters back.
- [x] Every decision above lives in an offline-testable pure policy class with unit tests for the failure branches.
- [x] Full offline sweep green: C# 257 PASS / 0 FAIL, MCP 55 OK, verification gates pass, preflight exit 0 (recorded in `evidence.md`).

## Constraints

- Offline only: no live game session. The claims above are about code
  contracts, not observed gameplay.
- Response shapes stay additive.

## Notes

- Evidence for each defect is in `research/action-trust-audit.md`.
- Verification record: `evidence.md`.
- Known follow-ups (not blockers, recorded in `evidence.md`): a pending card
  reward choice can outlive a `resolve_rewards` whose drain never reached a card
  reward screen, and `card_index: -1` now returns 409 instead of silently taking
  the first card.
