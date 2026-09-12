# 执行清单（四个 worker，写范围互不重叠）

## W1 — docs 跟踪一致性

写范围：`.gitignore`、`docs/model-compatibility-matrix.md`、`docs/phase-6-validation-2026-03-30-dynamic-card-values.md`、
`scripts/check_verification_gates.py`、`scripts/test-verification-gates.ps1`。

1. 删掉 `.gitignore` 里的 `docs/` 一行（保留同段的其它条目），确认删除后 `git status` 只多出那两个文档。
2. 用 `git add` 把两个文档入库（现在不再需要 `-f`）。
3. 在 `check_verification_gates.py` 新增 gate（名字自定，建议 `docs-tracked`）：
   - 枚举 `docs/**/*.md`，逐个用 `git ls-files --error-unmatch <path>`（或一次 `git ls-files -z -- docs` 求集合）判断是否被跟踪；
   - 未跟踪的文件必须在错误信息里逐个列出，并提示"docs/ 已不再被忽略，请 git add"；
   - 当仓库根下没有 `.git`（例如 source tarball）时，**跳过并打印一条说明**，不算失败；
   - 加进 `GATES` 字典与文件头 docstring 的 gate 列表。
4. 在 `test-verification-gates.ps1` 补一条用例：fixture 里放一个 docs/*.md 但**不**跟踪它 → gate 变红；
   恢复后变绿。注意该脚本含非 ASCII，**必须保持 UTF-8 BOM**（script-encoding gate 会检查），
   fixture 需要能被 `git` 识别——若 fixture 不是 git 仓库，gate 会走"跳过"分支，
   所以这条用例要么给 fixture 初始化一个临时 git 仓库（`git init` + `git add` 已跟踪文件），
   要么把用例写成"在真实仓库上验证"的形式；以实现时的最简可靠方案为准，并在报告里说明选择。

## W2 — operations spec 与 AGENTS 刷新

写范围：`.trellis/spec/operations/validation-and-release.md`、`AGENTS.md`（后者是 **gitignored 本地文件**，
改了只留在工作区，**绝对不要 `git add -f`**）。

1. 门禁表：把 `check_verification_gates.py` 那行的描述补齐为当前五个 gate
   （lockfile / api-doc / api-facts / doc-marks / script-encoding），`--only` 候选串同步刷新。
   实现前先跑一次 `python scripts/check_verification_gates.py` 与 `--help` 确认实际名字。
2. "Release metadata" 段：三处 → **五个文件**，并指向 `scripts/check_release_metadata.py`
   的实际校验范围（mod_manifest.json / mod_id.json / Router.cs / pyproject.toml / uv.lock）。
3. 新增一小节"Script inventory"（放在 Offline checks 之后）：
   - 列出**离线可跑的检查入口**（各一条命令 + 一句用途）；
   - 列出**刻意未接线的脚本**并给出原因（`scan-assembly-strings.ps1` 由 `docs/reverse-engineering.md` 记录；
     `generate-sts2-knowledge.ps1` 是其产物 docs/game-knowledge/*.md 的再生器；
     `sts2-coop-full-run-acceptance.ps1` / `test-coop-play-together.ps1` 是需真机的人工验收编排；
     `sts2-validation-secrets.ps1` 只服务前者）。
     每条都要给 `file:line` 证据，并注明"这些不是死代码，是人工/真机入口"。
4. `AGENTS.md`：把新增的离线自测入口（W3 接线的那个）写进 scripts 说明段落，保持既有排版。

## W3 — 把孤儿自测接进 preflight 与 CI

写范围：`scripts/preflight-release.ps1`、`.github/workflows/validate.yml`、
`scripts/check_release_package.py`（仅在确认语义合适时）。

1. 先确认 `python scripts/sts2-model-budget-proxy-selftest.py` 离线、快速、exit 0（本机实测 4.2s）。
2. `preflight-release.ps1`：照既有的 `Invoke-Step -Name "..." -Action { ... }` 风格新增一步
   （建议名 "Check the model budget proxy (no-cost self-test)"），放在 Python 相关步骤之后。
   **保持脚本的 UTF-8 BOM**。
3. `.github/workflows/validate.yml`：新增一步运行同一命令（与既有步骤的 shell/工作目录风格一致）。
4. `check_release_package.py`：读 `SOURCE_FILES` 的语义（源契约 = 必须存在于源码树）。
   **只有在确认它不要求该文件进入发布产物时**才把自测脚本加进去；若两者语义耦合，则不改该文件，
   并在报告里说明原因。无论哪种选择，都要保证"删掉这个自测脚本会让某道门禁变红"。

## W4 — 打包前的版本守卫

写范围：`scripts/package-release.ps1`。

1. 在脚本开始构建之前，调用 `python scripts/check_release_metadata.py`（或等价的内联校验），
   失败即中止并给出可读原因（例如"版本不一致，先跑 preflight-release.ps1"）。
2. 不改脚本其它行为（产物结构、zip 名称、`check_release_package.py --artifact` 调用都不动）。
3. 可否证演示：临时把 `mcp_server/pyproject.toml` 的 version 改一位 → 运行打包脚本应立即失败且**不产出 zip**；
   还原后再跑一次确认恢复（若完整打包需要关闭游戏或耗时过长，可用 `-WhatIf`/等价手段或只跑到守卫步骤，
   在报告里说明演示方式）。

## 共同要求

- 不要 `git add/commit/push`（主代理统一提交）。
- 门禁真跑贴输出；断言可否证（改坏→红→还原）。
- 报告：改动文件绝对路径、验收证据、只能实机/未证明的项。
