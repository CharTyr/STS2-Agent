# 执行清单

1. 读：`scripts/test-main-menu-active-run.ps1`（全文）、`scripts/run_sts2_validation.py`
   （`settle_game_over`/`settle_main_menu`/`collect_rewards_if_needed`/`suite_new_run_lifecycle`/
   `suite_main_menu_active_run`）、`scripts/test-multiplayer-lobby-flow.ps1:630-745`、
   `STS2AIAgent.Tests/TimelineIndexContractTests.cs`、`STS2AIAgent/Game/GameStateService.cs:6795-6866`
   （屏幕全集）与 `:4768-4800`（timeline payload）。
2. 按 design 改四处。
3. 可否证自检：
   - `python -c "import py_compile; py_compile.compile('scripts/run_sts2_validation.py', doraise=True)"`；
   - `python scripts/check_verification_gates.py --only script-encoding`；
   - PowerShell 语法：`[System.Management.Automation.Language.Parser]::ParseFile('<脚本>', [ref]$null, [ref]$errors)` 报 0 error；
   - timeline 索引逻辑用假 payload（`slots=[{index:0,is_actionable:false},{index:1,is_actionable:true}]`）验证取到 1。
4. 验证：
   ```powershell
   python scripts/check_verification_gates.py
   ```
5. 不提交、不推送；报告改动文件、语法/编码证据与"只能实机验证"的清单。

注意：本轮不需要新增 C# 测试，也不要改 `STS2AIAgent.Tests/TestRunner.cs`。

