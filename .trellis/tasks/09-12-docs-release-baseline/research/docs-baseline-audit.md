# Audit evidence: docs and release baseline (2026-09-12)

## Version sources

```text
STS2AIAgent/mod_manifest.json:7      "version": "0.11.0"
STS2AIAgent/mod_id.json:6            "version": "0.11.0"
STS2AIAgent/Server/Router.cs:18      internal const string ModVersion = "0.11.0";
mcp_server/pyproject.toml:3          version = "0.11.0"
mcp_server/uv.lock:1196-1197         name = "sts2-ai-agent-mcp" / version = "0.11.0"
```

All five agree. The preflight validates all five
(`scripts/preflight-release.ps1:95-123`); `AGENTS.md` names three
(`:115-119`, `:124`) and calls the constant `private const string`
(actual: `internal const string`).

## Stale claims

```text
PRODUCT_PLAN_CURRENT.md:3-5    "发布代码基准：v0.10.6 @ 2f75e4a"; page has no v0.11.0
PRODUCT_PLAN_CURRENT.md:11-13  "2026-09-10 主线未发布变更"
PRODUCT_PLAN_CURRENT.md:26-32  "2026-09-11 标签后主线未发布变更" (d77982a, 7b02168, proactive-chat fixes, api.md additions)
PRODUCT_PLAN_CURRENT.md:65-68  matrix rows still say #82 / pets / chat-volume fixes are unreleased
docs/api.md:112                "mod_version": "0.10.6" in the /health example
steam-workshop/workshop.json:4 "changeNote": "v0.10.4 timeline unlocks, ..."
```

Reality: tag `v0.11.0` (annotated tag object `35d6b9d` → commit `84631b9`)
contains `f9330ba` (v0.10.7), `d77982a`, `7b02168`; `git log v0.11.0..HEAD` is
only chore/docs archive commits.

## Missing routes

`Router.HandleAsync` serves `/health`, `/state`, `/actions/available`,
`/data/{collection}`, `/events/stream`, `/action`, `/session/control`,
`/companion/control`, `/companion/message`, and `/mcp` (+ `/mcp/`).
`README.md:150-155`, `README.zh-CN.md:150-155`, and `AGENTS.md:103-107` list only
six routes and omit the four above.

## docs/api.md contradictions

```text
docs/api.md:89-96     Action Status table: completed | pending
docs/api.md:1063      ActionResponsePayload: "completed" | "pending" | "failed"
client.py:1037-1045   raises action_failed on status="failed"
docs/api.md:1188      "## POST /mcp" but Router.cs:141 + IsMcpPath (:304-312) accept any method and /mcp/
```

## README asymmetry

```text
README.md:169-181        MCP "Default shape" JSON block + "Built-in tools match in-game autoplay" line
README.zh-CN.md:169-171  jumps straight to the separator; block missing
README.zh-CN.md:198      extra C# test-coverage sentence, absent from README.md:208-212
```

## Preflight vs CI

CI (`.github/workflows/validate.yml`) runs, in order: C# runner (24), MCP unittest
(27), `scripts/test-mcp-tool-profile.ps1` (29), `scripts/check_release_metadata.py`
(32), `scripts/check_verification_gates.py` (34),
`scripts/test-verification-gates.ps1` (36),
`scripts/test-native-exit-propagation.ps1` (38).

`scripts/preflight-release.ps1` covers the C# runner, MCP unittest, release
metadata, verification gates, packaging contract, and mentions
`test-mcp-tool-profile.ps1`, but it does not run
`scripts/test-verification-gates.ps1` or
`scripts/test-native-exit-propagation.ps1`, so a green local preflight is not a
green CI gate run.

## Contradiction

`CONTRIBUTING.md:9` "Do not push directly to `main`" vs `AGENTS.md` Release flow
(`git push origin main`) and the actual release history on `main`.

## Out of scope

- History files under `history/` keep their historical wording.
- No release, tag, or Workshop action is performed by this task.
