# Validation evidence

Baseline: `00aad146b8bafb27a64aabb806f823f412251998` on local dev.

- Red: 9 of the initial 10 added tests fail against unchanged production sources; normal action control passes.
- Final: 26 new C# tests, 615 C# PASS / 0 FAIL; 315 Python tests OK.
- Release build: 0 warnings / 0 errors.
- Full preflight: exit 0, all 16 steps and all 12 verification gates pass.
- Final source hashes match `build/agent-turn-integrity-2026-09-21/tested-source-hashes.json`.
- Logs and summary: `build/agent-turn-integrity-2026-09-21/`.
- Report: `docs/agent-turn-integrity-2026-09-21.md`.

No game launch, mod deployment, real provider calls, remote push or release. Model/game behavior was exercised with controlled fakes; real Task/SemaphoreSlim/cancellation exercise the queue and receipt ordering. Game-dependent runtime wiring is source-checked and compiled against the installed game assemblies. The original cstest.log and v.log are not staged.

Work commit: `763eabddb10fdb52ff941df6715014bc468a4f8e`.
