# 执行清单

1. 读：
   - `scripts/check_verification_gates.py`：文件头注释（说明每条 gate 的口径）、
     `GATES` 字典与 `main()`（约 630-670）、`check_docs_tracked`（580-615，跳过形态的范例）、
     `list_tracked_docs`（560-577，`subprocess.run` 的既有用法）；
   - `scripts/test-verification-gates.ps1`：`Invoke-Gate` / `Assert-Case` / fixture 构造（1-100 行）与末尾的 case 列表；
   - `.trellis/spec/operations/validation-and-release.md:10-40`（gate 表与 count）。
2. 实现 `check_ps1_syntax` 并注册进 `GATES`。
3. 加两个自测 case。
4. 更新 spec 的 gate 表/count/--only。
5. 验证（全部要原始输出）：
   ```powershell
   python scripts/check_verification_gates.py                     # 7 道全绿
   python scripts/check_verification_gates.py --only ps1-syntax    # 单跑
   powershell -ExecutionPolicy Bypass -File scripts/test-verification-gates.ps1
   powershell -ExecutionPolicy Bypass -File scripts/preflight-release.ps1
   ```
   手动确认：把 `scripts/build-mod.ps1` 复制一份改成坏语法放进 `scripts/`（**临时**）⇒ 全量 gate 变红并报出该文件；
   删掉后恢复绿。（或直接依赖自测 case，但至少手动做一次真实的"仓库内坏脚本"验证。）
6. 不提交、不推送；报告改动文件、红/绿证据、以及任何意外发现。

注意：`scripts/*.ps1` 里含非 ASCII 的必须保持 UTF-8 BOM（`script-encoding` gate）——
你**只新增自测 case 与 gate 代码**，若 `test-verification-gates.ps1` 已经是 BOM 的，保持它。

