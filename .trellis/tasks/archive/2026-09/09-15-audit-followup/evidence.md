# Grok change-set audit (2026-09-15)

The previous round shipped through grok-4.6 sub-agents. This round audited their claims
against the working tree instead of trusting the status files, then fixed what did not hold.

## Method

- Five read-only grok-4.6 agents (xhigh) over disjoint surfaces: MCP layer, build/release
  scripts, C# correctness and the request ledger, docs consistency, and the POSIX seed logic.
  Two delivered reports; three died on upstream 429 and the main agent audited those surfaces.
- Every claim below was re-read in the source by the main agent before it was acted on, and the
  fixes were re-verified by running the suites and, where the claim was about behaviour, the game.

## Confirmed defects, fixed this round

1. A dual-launch loser classified on the owner's outcome. `TryBeginDualLaunch` writes
   `DualLaunchOutcome` after claiming the gate, and a failed claim is not ordered after that
   write, so the loser could read `Idle` or the previous attempt's `Succeeded`. The launch
   entries now return `null` for a caller that owns no attempt (`AgentRuntime.cs`
   `TryLaunchDualInstanceAsync` / `TryContinueDualInstanceAsync`), and both executors answer
   `pending` without touching the outcome (`GameActionService.cs`). Reproduced live: two
   simultaneous invites both return 200 `pending` with a stale `Succeeded` still in the field.
2. A second `continue_ai_teammate` while a launch was in flight fell through to the menu and save
   probes and answered 409 `invalid_action` with "No saved multiplayer run to continue." It now
   short-circuits to `pending` while `DualLaunching` is true. Verified live: 200 `pending`.
3. `DualLaunchOutcome.cs` and the `DualLaunchOutcomeTests` summary still described the
   gate-contention branch as the writer of `InProgress`; the owner writes it now. Comments and
   pinned declarations updated to the real contract.
4. The new `test_waits.py` regression test could only fail by hanging: it pinned the client clock
   to a fake that advances on sleep, so a retry loop that never sleeps would spin until the CI
   job timed out. The fake transport now fails after five reopens, and a second test pins the
   other half of the contract (an idle read timeout stays inside `wait_for_event`).
5. `state-invariants` demanded `play_card` whenever the local player's turn was active and the
   hand had playable cards. That is only the first link of the executor's readiness chain: a
   snapshot taken while a played card resolves satisfies it with an action table that has not
   been rebuilt. The gate now follows `combat.action_readiness.can_use_combat_actions`, with
   `player_action_phase` as the legacy fallback (`scripts/run_sts2_validation.py`).
6. The POSIX session script seeded the isolated profile under
   `${XDG_DATA_HOME:-$HOME/.local/share}/SlayTheSpire2` on every non-macOS system. On Windows
   the game reads `%APPDATA%\SlayTheSpire2`, so under Git Bash and MSYS the seeder wrote a file
   the game never opens and still reported success. `sts2_slay_user_root` now resolves
   `STS2_SLAY_USER_ROOT`, then `%APPDATA%` on MINGW/MSYS/CYGWIN (translated through `cygpath`
   with a manual fallback), then the platform default, and warns when the root does not exist.
   `start-game-session.ps1` gained the same environment override behind its `-UserRoot`
   parameter.

## Verification

- `dotnet build STS2AIAgent -c Release`: 0 warnings, 0 errors.
- `dotnet run --project STS2AIAgent.Tests`: 405 PASS, 0 FAIL, exit 0.
- `uv run --locked python -m unittest discover -s tests`: 220 tests OK.
- `python scripts/check_verification_gates.py`: 9/9 gates pass.
- `scripts/preflight-release.ps1`: 13 checks OK, exit 0.
- Seed harnesses (outside the repo, under %TEMP%): PowerShell 11 scenarios, POSIX 16 checks,
  both `HARNESS_RESULT all-pass`. The PowerShell override was exercised separately: the
  environment root receives the seed and an explicit `-UserRoot` still wins.

## Live evidence (isolated clientId 2026091001, API 18080, game v0.111.0, mod 0.12.3)

| Check | Result |
| --- | --- |
| `/health` at idle | carries `dual_launch_outcome: null`; every pre-existing field intact |
| First `invite_ai_teammate` | 200 `pending`, `stable: false`, returned in 259 ms |
| Immediately after the invite | `invite_ai_teammate` and `continue_ai_teammate` gone from `available_actions` |
| `/health` while launching | `dual_launch_outcome: InProgress`, fresh `dual_status` text |
| Second concurrent `continue_ai_teammate` | 200 `pending` (was a misleading 409) |
| Two simultaneous `invite_ai_teammate` | both 200 `pending`; nothing reported `completed` |
| Launch settles | `dual_launch_outcome: Succeeded`, companion PID 49492 on 18081, `auto_play: false` |
| `state-invariants` on both instances | host `failure_count: 0`, companion `failure_count: 0` |
| Steam profile | untouched: `settings.save` mtime 2026-09-12, sha256 C360D60B840994FD34FAA60C1F61AA25449D902A4122BA5958F022A25D23BF3E |

## Limits and open items

- The invite loser path was hit with two simultaneous calls from one process; the ordinary
  second call arrives after the screen has left the main menu and is refused structurally, which
  is correct but does not exercise the gate.
- `state-invariants` ran live on MAIN_MENU and CHARACTER_SELECT. The readiness change only
  narrows what is demanded, and the stricter rule it replaces already passed a live fight with
  `failure_count: 0`.
- The docs-consistency audit agent died on 429; the main agent checked the `CHANGELOG.md`,
  `docs/api.md` and `docs/live-validation-checklist.md` claims it would have covered.
- Nothing is committed, pushed, or released.
