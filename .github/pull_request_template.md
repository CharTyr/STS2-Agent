## Summary

- What changed in this PR?
- Why is this change needed?

## Validation

- [ ] `dotnet build "STS2AIAgent/STS2AIAgent.csproj"`
- [ ] `powershell -ExecutionPolicy Bypass -File "scripts/build-mod.ps1"`
- [ ] `powershell -ExecutionPolicy Bypass -File "scripts/test-mod-load.ps1"`
- [ ] `cd "mcp_server"` then `uv run sts2-mcp-server`

List any additional validation you ran:

```text
<commands and results>
```

## Prerequisites

- [ ] No machine-specific paths were added to tracked files
- [ ] Required local tools / env vars are documented
- [ ] User-facing behavior or API changes are documented

## Notes

- Screenshots, logs, compatibility notes, or follow-up work
