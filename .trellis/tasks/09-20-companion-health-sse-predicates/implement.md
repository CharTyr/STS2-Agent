# Implementation plan

## Execution order

### Task 1 — Companion liveness

- [ ] Add failing C# cases for degraded acceptance and malformed/unknown status rejection at the `CompanionHealth.IsExpectedProcess` seam.
- [ ] Implement exact ready/degraded status policy without weakening identity fields.
- [ ] Add/update POSIX helper regression coverage and change ready-only check.
- [ ] Run focused C# test filter if supported, otherwise full C# harness; run shell helper test/gate.

### Task 2 — Role-correct health

- [ ] Add offline source/contract tests for host-only vs companion-null fields and common field presence.
- [ ] Implement role-aware values while retaining all keys.
- [ ] Update `docs/api.md`, validation checklist observation, api-facts expectations if needed, and Unreleased changelog.
- [ ] Run focused health contract tests and api-facts.

### Task 3 — Demand-driven polling

- [ ] Add a game-independent lifecycle test seam and red tests for zero/one/two/leave/resume/Stop/concurrent-first behavior.
- [ ] Implement one shared demand-driven loop with clean Stop semantics.
- [ ] Pin `session_started`/`stream_ready` ordering.
- [ ] Run focused event lifecycle tests.

### Task 4 — Slow subscriber

- [ ] Add deterministic bounded-capacity tests for order, non-full behavior, overflow closure/removal, global event IDs, and isolation between subscribers.
- [ ] Change channel full mode to explicit nonblocking failure semantics.
- [ ] Audit Python EOF/reconnect/fallback; add/modify tests and code only if necessary.
- [ ] Update SSE docs and changelog.
- [ ] Run focused C# event tests and Python `test_waits.py`.

### Task 5 — Predicate relocation

- [ ] Compute predicate member set and predicate-only helper closure.
- [ ] Move declarations verbatim in source order into partial file(s).
- [ ] Verify nonblank moved logic lines/method text are unchanged.
- [ ] Update source scanners/coverage, architecture counts/description, and lower budgets.
- [ ] Run C# source contracts plus arch-facts.

### Documentation and full verification

- [ ] Ensure external changes Tasks 1–4 are documented as offline verified/live validation pending; do not alter historical evidence.
- [ ] Run `dotnet run --project STS2AIAgent.Tests\STS2AIAgent.Tests.csproj`.
- [ ] From `mcp_server`, run `uv run --locked python -m unittest discover -s tests -v`.
- [ ] Run `python scripts\check_verification_gates.py`.
- [ ] Run `powershell -ExecutionPolicy Bypass -File scripts\preflight-release.ps1`.
- [ ] Inspect diffs, test failures and `git status`; preserve user-owned `cstest.log` and `v.log`.
- [ ] Run Trellis quality check and produce the requested Task 1–5 report.

## Risky files / rollback points

- `STS2AIAgent/Server/GameEventService.cs`: highest concurrency risk; keep Tasks 3 and 4 logically separable in review.
- `STS2AIAgent/Game/GameStateService.cs`: relocation only; any semantic diff blocks completion.
- `scripts/lib-sts2.sh`: embedded Python/heredoc quoting; validate with bash syntax and offline tests.
- `docs/api.md`: api-facts extracts exact health keys and SSE types.

## Validation commands

```powershell
dotnet run --project STS2AIAgent.Tests\STS2AIAgent.Tests.csproj
Push-Location mcp_server
uv run --locked python -m unittest discover -s tests -v
Pop-Location
python scripts\check_verification_gates.py
powershell -ExecutionPolicy Bypass -File scripts\preflight-release.ps1
```

Focused commands will be selected from the custom C# runner capabilities and relevant Python unittest modules after source audit.
