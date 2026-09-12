# 执行清单

1. 读 `scripts/check_verification_gates.py`（重点 `GATES` 注册与三个既有 gate 的写法）、
   `scripts/test-verification-gates.ps1`、`docs/api.md:1-160`（版本、屏幕枚举、端口）、
   `STS2AIAgent/Game/GameStateService.cs` 的 `ResolveNonModalScreen`、
   `mcp_server/README.md:1-140`、`mcp_server/src/sts2_mcp/server.py` 的 `_LEGACY_ACTION_TOOLS`、
   `.github/workflows/validate.yml`。
2. 实现 `check_api_facts`，先只输出提取结果（dry run 打印条数），确认无误后再改成断言。
3. 逐条可否证自检：改坏版本号 / 删一个屏幕名 / 改端口 → 三次分别看到红 → 还原。
4. 修 `mcp_server/README.md` 的工具清单，加双向集合测试。
5. CI 加步骤；`test-verification-gates.ps1` 加用例（注意 BOM）。
6. 验证：
   ```powershell
   python scripts/check_verification_gates.py
   powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1
   cd mcp_server; uv run --locked python -m unittest discover -s tests
   python scripts/check_release_package.py --source-root .
   ```
7. 不提交、不推送；报告改动文件、每条门禁的提取结果与红/绿证据、以及"CI 上未实跑"的说明。
