# Evidence: action trust (2026-09-12)

## Implementation

New pure policy classes (compiled into the offline test assembly):

| File | Decision it owns |
| --- | --- |
| `STS2AIAgent/Game/RewardChoicePolicy.cs` | pending card-reward choice ⇒ Auto / Skip / Pick, and whether an explicit index is valid |
| `STS2AIAgent/Game/MenuTransitionPolicy.cs` | menu/embark/character-select settledness, and the "blocked by modal X" message |
| `STS2AIAgent/Game/BackgroundTaskOutcome.cs` | finished-but-failed background task ⇒ reason string |
| `STS2AIAgent/Game/CardPlayCounterPolicy.cs` | whether a `play_card` must roll its optimistic counters back |

Call sites changed in `STS2AIAgent/Game/GameActionService.cs`:

```text
  build reward choice + reject an out-of-range explicit index before clicking
  consume the choice through RewardChoicePolicy (no FirstOrDefault fallback)
  remove the three "an open modal == the transition happened" branches
  remove_card_at_shop: surface an already-failed purchase instead of pending
  choose_bundle / confirm_bundle: snapshot failure => 503 state_unavailable
  play_card: roll back the optimistic counters when the card never left the hand
```

`GameStateService.cs`, `mcp_server/`, `docs/`, and `skills/` were not touched.

## Review round

The reviewer sub-agent fixed four issues in the first pass:

1. the request-time reward check could reject a legal `option_index: 0` while the
   card-reward option list was still empty (now it rejects early only when the
   list is authoritative and defers otherwise);
2. `open_character_select` still reported `completed` with a modal open (the
   prompt now takes `modalOpen`);
3. `MenuTransitionPolicy.IsCharacterSelectSettled` was an identity function whose
   test merely restated it (now `visible && !modalOpen`);
4. the request-time reward rejection and the shop purchase-failure wiring had no
   coverage (two contract tests added).

## Commands actually run (repository root unless noted)

```text
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release   -> 257 PASS / 0 FAIL, exit 0
dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release --no-incremental     -> 0 warnings / 0 errors
cd mcp_server; uv run --locked python -m unittest discover -s tests         -> Ran 55 tests, OK
python scripts/check_verification_gates.py                                  -> passed: api-doc, doc-marks, lockfile, script-encoding
powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1      -> "Static preflight complete.", exit 0
```

Preflight transcript: `build/preflight-out.txt` (257 PASS, 0 FAIL, MCP 55 OK,
profile validation OK, packaging contract OK).

## Test inventory added by this task

`STS2AIAgent.Tests/RewardChoicePolicyTests.cs`,
`MenuTransitionPolicyTests.cs`, `BackgroundTaskOutcomeTests.cs`,
`CardPlayCounterPolicyTests.cs`, `GameActionTrustContractTests.cs` — 25 test
methods, all registered in `TestRunner.AllTests`.

## Known follow-ups

- A pending card-reward choice can survive a `resolve_rewards` whose drain never
  reached a card-reward screen; a later `collect_rewards_and_proceed` can then
  consume it, and an out-of-range value is reported with
  `action: "resolve_rewards"`. Clearing it would break the legitimate
  "resolve_rewards returned pending, retry" flow, so the lifetime needs a product
  decision.
- `card_index: -1` on `resolve_rewards` now returns 409 `invalid_target` where it
  previously silently took the first card. This follows the design (the alias
  means "pick"), but it is a strict-semantics change for that alias.

## Not verified

No live game session was run. Every statement above is an offline code-contract
result; modal names in the pending message come from the Godot type name.
