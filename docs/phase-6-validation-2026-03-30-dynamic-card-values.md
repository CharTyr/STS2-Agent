# Dynamic Card Values Validation Record

> Historical snapshot: this file records a point-in-time validation run, not current state. Current status: [PRODUCT_PLAN_CURRENT.md](../PRODUCT_PLAN_CURRENT.md).

- Validation date: `2026-03-30`
- Validator: Codex
- Git commit: `ffeb0e7`
- Game version: `v0.99.1`
- Mod version: `0.5.2`
- Protocol version: `2026-03-11-v1`
- Validation mode: live local game session with debug actions enabled

## Goal

Validate that card payloads now expose runtime-resolved rule text and structured dynamic values in a real combat session, not only in static card data.

## Static / Script Checks

- `powershell -ExecutionPolicy Bypass -File "scripts/test-state-invariants.ps1"` passed
- `python -m py_compile "scripts/run_sts2_validation.py"` passed
- `python "scripts/run_sts2_validation.py" state-invariants --base-url "http://127.0.0.1:8080"` passed

Observed summary:

```json
{"screen":"COMBAT","checked_actions":4,"failure_count":0,"warning_count":0,"failures":[],"warnings":[]}
```

## Live Combat Probe

Test card: `BODY_SLAM` (`全身撞击`)

Setup sequence:

1. Launch the game with the mod enabled.
2. Confirm `/health` returns `status=ready`.
3. Resume a live run and enter combat.
4. Inject `BODY_SLAM` into hand with the debug console.
5. Increase player block and read `/state`.

Observed payload sample at `12` block:

```json
{
  "card_id": "BODY_SLAM",
  "resolved_rules_text": "造成你当前格挡值的伤害。 （造成12点伤害）",
  "dynamic_values": [
    {
      "name": "CalculatedDamage",
      "current_value": 12,
      "is_modified": true
    }
  ]
}
```

Observed payload sample after increasing block to `17`:

```json
{
  "card_id": "BODY_SLAM",
  "resolved_rules_text": "造成你当前格挡值的伤害。 （造成17点伤害）",
  "dynamic_values": [
    {
      "name": "CalculatedDamage",
      "current_value": 17,
      "is_modified": true
    }
  ]
}
```

## Result

- `resolved_rules_text` updated with the live combat value.
- `dynamic_values[].current_value` updated with the same live combat value.
- The value changed from `12` to `17` after the combat state changed, confirming that the payload is runtime-resolved rather than static.
- The invariant checks accepted the new fields on `combat.hand[]`, `run.deck[]`, `selection.cards[]`, `reward.card_options[]`, and `shop.cards[]`.

## Conclusion

The runtime card-value payload is working in a real game session. This closes the original protocol gap where agents could only infer dynamic card state from unresolved text.
