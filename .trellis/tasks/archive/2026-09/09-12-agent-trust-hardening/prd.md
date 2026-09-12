# Agent trust hardening: 5 goals from the 2026-09-12 audit

## Goal

Turn the 2026-09-12 read-only audit (6 parallel scouts over the mod, the MCP
server, the docs, the tests, and the gameplay skill) into five independently
verifiable deliverables, executed in importance order.

The user asked for the five most important goals to be picked freely and then
finished one by one without further approval. Everything stays on `main`; no
release, tag, or Workshop upload is part of this task.

## Source requirement set

- User request (2026-09-12): "全面理解当前项目，并自由探索5个最重要的目标，并依重要性逐个全部完成，不需要再经过我的审批。子代理全部以 deepseek/deepseek-flash 模型、xhigh 推理强度进行派发。"
- Audit input: six scout reports (screens/actions, action robustness, MCP
  audit, test/CI health, doc/release consistency, game data/skill).
- Repo rules: `AGENTS.md`, `.trellis/spec/**`, `.trellis/workflow.md`.

## Task map (importance order)

| # | Child task | Why it ranks here | Deliverable |
| - | --- | --- | --- |
| 1 | `09-12-action-trust` | A wrong card gets picked silently, a blocked menu reports success, and a failed purchase is swallowed. These corrupt play decisions. | No silent wrong index, no fake success, no swallowed failure |
| 2 | `09-12-bounded-game-waits` | Nine game-side `await`s have no deadline, so an HTTP action can hang forever. | Every awaited game task is bounded; failure is reported |
| 3 | `09-12-agent-contract-compact` | The gameplay skill tells the agent to read raw-state field names from the compact view; five are undefined there. | Skill and MCP surface match the compact contract |
| 4 | `09-12-screen-index-contract` | Timeline epoch indexes are exposed in one space and consumed in another; two screens have no usable action. | Index spaces agree; FAKE_MERCHANT and patch-notes screens are not dead ends |
| 5 | `09-12-docs-release-baseline` | The state page still describes v0.10.6 and calls shipped work unreleased; the release checklist misses two of five version files. | Docs match the v0.11.0 baseline and the executable gates |

## Cross-child acceptance criteria

- [ ] Every child has `prd.md` (+ `design.md` / `implement.md` for the complex ones) before `task.py start`.
- [ ] Behavior changes land with offline tests that fail if the old behavior returns.
- [ ] `dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release` passes (no regressions).
- [ ] `uv run --locked python -m unittest discover -s tests` passes from `mcp_server`.
- [ ] `python scripts/check_verification_gates.py` and `powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1` pass.
- [ ] Docs touched by a change are updated in the same child (skill, `docs/api.md`, spec).
- [ ] No release tag, Workshop upload, or version bump is performed by these children.
- [ ] Parent task reviews the union of the children at the end and records the result.

## Constraints

- Offline only: no live game session, no provider calls. Live behavior claims
  must stay labeled as unverified.
- Keep the existing response shapes additive; never remove a field a client
  may already read.
- One writer per file at a time: the children are executed sequentially, not
  in parallel, because they all touch `GameActionService.cs`.

## Notes

- Each child archives independently; this parent owns the requirement set, the
  map above, the cross-child acceptance criteria, and the final review.
- Live-game verification of the fixed screens remains out of scope and is
  recorded as such.
