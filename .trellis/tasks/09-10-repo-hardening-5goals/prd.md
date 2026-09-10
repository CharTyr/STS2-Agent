# Repo hardening: five highest-value goals

## Context

Session started from `trellis-start`. Repo state at session start (`main` @ 928d2ba, clean
worktree): v0.10.5 released, 180 C# core tests and the Python MCP suite green, no active
Trellis task, two open GitHub issues (#50, #51) and a set of documents whose claims no longer
match the source.

Constraints from the user: do not launch the game (the user is playing). Every deliverable in
this task tree must therefore be verifiable offline. Evidence must be produced by commands that
do not need a running game instance.

## Goals (in completion order)

1. `09-10-dep-security-closure` — Dependency security closure for `uv.lock` and
   `package-lock.json`, closing issues #50 and #51.
2. `09-10-proactive-teammate-chat` — Proactive teammate chat with an opt-in and a selectable
   conversation tone (the largest documented product gap that does not need a live game).
3. `09-10-api-doc-contract` — Make `docs/api.md` describe the actions the code actually
   exposes, with an automated guard against future drift.
4. `09-10-stale-doc-archive` — Archive or rewrite documents that contradict current source
   (starting with the 2026-03-10 gap list).
5. `09-10-verification-gates` — Wire dependency, lockfile, and document-contract gates into
   preflight and CI so goals 1, 3, and 4 cannot silently regress.

## Scope

In scope: dependency manifests and lockfiles, C# agent/UI/config code for the proactive-chat
capability, Markdown documents under `docs/` and the repository root status page, and the
offline verification scripts (`scripts/`, `.github/workflows/`).

Out of scope for this task tree: launching the game or the Mod, live gameplay validation,
Steam Workshop uploads, and publishing a new tagged release. The repository stays
release-ready, but shipping a tag is a separate decision that needs live validation the
current environment cannot produce.

## Acceptance criteria (tree level)

- Both open issues have a reproducible, evidence-backed resolution in the working tree.
- `docs/api.md` names every action the Mod exposes, and a script fails when that stops being
  true.
- No document under `docs/` claims a capability is missing when the source implements it.
- Proactive teammate chat exists behind an explicit off-by-default opt-in, respects session
  budgets and pause semantics, and is covered by deterministic C# tests.
- `scripts/preflight-release.ps1` fails when a lockfile is out of sync with its manifest, when
  the API document misses an action, or when a stale-document marker is violated.
- Every claim in the final report is backed by a command run in this session.

## Evidence boundary

Offline tests, static preflight, and package inspection are the strongest evidence this
environment can produce. They do not prove in-game behaviour. Any live-game behaviour touched by
goal 2 stays explicitly unverified until someone runs the game.
