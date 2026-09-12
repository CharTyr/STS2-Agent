# 收尾最后几处残留

## 背景

上一轮收尾后仍留下四处**已被证实、且不需要产品决策**的小问题：

1. **`docs/` 的跟踪漂移**：`.gitignore:15` 忽略 `docs/`，而 31 个 docs/*.md 是被 force-add 进库的。
   工作树上有 33 个 .md，**两个未入库**（`git status --ignored docs` 显示 `!!`）：
   `docs/model-compatibility-matrix.md`（2026-09-08 的现行说明）与
   `docs/phase-6-validation-2026-03-30-dynamic-card-values.md`（带历史快照标记的验证记录）。
   后果：`git add .` 不会带上新文档；本地 preflight 的 doc-marks 会检查
   `phase-6-…-dynamic-card-values.md`（实测输出里有它），而 CI 的全新检出看不到该文件 ——
   **同一道门禁在本地与 CI 的检查集合不同**。
2. **spec 门禁清单过期**：`.trellis/spec/operations/validation-and-release.md:12` 只列
   lockfile / api-doc / doc-marks，缺上一轮新增的 `api-facts` 与既有的 `script-encoding`；
   `--only` 候选串同样过期。同文件的"Release metadata"段仍写"三个文件"，而现在是五个。
3. **孤儿自测从未运行**：`scripts/sts2-model-budget-proxy-selftest.py` 是全仓唯一的严格零引用脚本，
   但它正是 `sts2-model-budget-proxy.py`（双开全流程验收的成本守门组件）的**零成本离线自测**：
   实测离线、4.2 秒、exit 0，检查项含 `real_ledger_untouched`。它从未被 preflight 或 CI 调用。
4. **打包缺版本守卫**：`scripts/package-release.ps1` 不调用 `check_release_metadata.py`，也不调用
   `preflight-release.ps1`。也就是说，只要没先手动跑 preflight，就可能用五处不一致的版本号打出发布包。

## 目标

1. 让 `docs/` 成为受控目录：取消 ignore、把两个未入库文档入库，并加一道门禁
   "磁盘上的 docs/**/*.md 必须都被 git 跟踪"（本地发现"新文档悄悄没入库"，CI 侧自动退化为恒真）。
2. 更新 operations spec：门禁表补 `api-facts` / `script-encoding`，刷新 `--only` 列表，
   把版本同步改成"五个文件"，并补一节脚本清单（离线检查入口 + 刻意不接线的脚本与原因）。
3. 把 `sts2-model-budget-proxy-selftest.py` 接进 preflight 与 CI，使它从孤儿变成被覆盖的资产；
   若删除它，门禁必须变红（不能静默消失）。
4. `package-release.ps1` 在开始构建前先做版本一致性守卫，失败即中止并给出可读原因。

## 验收标准

- [x] `git status --ignored docs` 不再显示被忽略的 docs 文件；两个文档已入库。
- [x] 新门禁对"磁盘上存在但未被 git 跟踪的 docs/*.md"变红，对正常状态变绿；`--only` 可单独选它；
      在非 git 环境（无 `.git`）下跳过而不是崩溃（给出说明行）。
- [x] `scripts/test-verification-gates.ps1` 为新门禁补一条自测用例，整体仍全 PASS。
- [x] spec 的门禁表、`--only` 列表、版本文件数量与当前实现一致；脚本清单准确（每条给 file:line）。
- [x] preflight 输出里出现运行孤儿自测的步骤且 PASS；`.github/workflows/validate.yml` 同样跑它。
- [x] `package-release.ps1` 在版本不一致时**先失败**（可离线演示：临时改一处版本号 → 打包脚本立即报错退出，不产出 zip；还原后恢复）。
- [x] 全量门禁通过：C# 单测、MCP 单测、`check_verification_gates.py`、`check_release_package.py`、preflight 350+ PASS / 0 FAIL。

## 范围外

- **不删** `mcp_server/data/eng/`、**不删**任何脚本（删除属产品决策，用户未拍板）。
- 不改 `docs/api.md` 的动作契约块与其它已受门禁保护的契约。
- 不给 CI 加 mod 主工程编译（CI 无 `sts2.dll` / `GodotSharp.dll`；本机 preflight 已覆盖该步）。
