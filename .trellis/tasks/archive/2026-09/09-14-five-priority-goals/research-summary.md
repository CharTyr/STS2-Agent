# Exploration and selection

Five read-only scouts ran with xai/grok-4.6 / xhigh and no inherited history. Main read the architecture, workflow, layer specs and current product baseline directly. Semantic search was attempted by the scouts; some calls returned resource_exhausted and exact searches were used as fallback.

## Selected in priority order

1. Runtime replaces the budget guard on SaveSettings/ReloadSettings while AutoPlayRecovery holds its old reference. Keep identity and totals stable while updating limits.
2. SessionBudgetGuard.Observe invents one request for zero-request immediate actions. Count actual model requests.
3. Sts2Client.wait_for_event retries connection_error without backoff until the complete wait deadline, preventing server polling fallback. Exercise the real client transport in tests.
4. Invite/continue co-op actions are advertised under weaker conditions than the executor's shared structural launch policy. Share the availability gate while preserving external takeover without model setup.
5. POSIX build installs only DLL/PCK and always creates the destination; Windows already stages mod_id.json and supports SkipInstall. Align artifact and safe staging behavior.

## Deferred, not claimed as verified defects

- Router heartbeat starts a new channel wait after each idle timeout; the scout did not reproduce the suggested stall. Channel implementation behavior needs confirmation.
- HttpServer's canceled Task.Run scheduling could skip cleanup during shutdown; occurrence and listener-close behavior need reproduction.
- Workshop staging uses a nested local directory; the game loader's supported layouts were not checked, so changing it to flat based only on README examples is not justified.
- Workshop discovery only considers conventional Steam roots; this is a separate broader installation discovery task.

## Baseline evidence

- C# core executable: exit 0, all emitted cases PASS.
- Python standard-library unittest: 198 tests, OK.
- check_verification_gates.py: all nine gates passed, including 19 shell scripts and 29 resolver checks.
- No live game or paid model request was made. The initially clean dev HEAD was e13f5ec; implementation branch is codex/five-priority-reliability.
