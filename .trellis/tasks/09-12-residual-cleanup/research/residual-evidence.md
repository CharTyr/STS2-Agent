# 证据

## 1. docs 跟踪漂移（已证实）

- `.gitignore:15` = `docs/`；`.gitignore:17` = `AGENTS.md`（后者确为本地文件）。
- `git status --porcelain --ignored docs`：
  ```
  !! docs/model-compatibility-matrix.md
  !! docs/phase-6-validation-2026-03-30-dynamic-card-values.md
  ```
- 磁盘 `docs/**/*.md` = 33 个；`git ls-files docs` = 44 个（含非 md）。
- 本地 preflight 的 doc-marks 输出确实包含 `docs/phase-6-validation-2026-03-30-dynamic-card-values.md marked as a snapshot`，
  而该文件未入库 ⇒ CI 的全新检出不会检查它。
- `rg --no-ignore -F 'model-compatibility-matrix'` 无命中：该文档目前只靠目录列举被发现。

## 2. spec 过期（已证实）

- `.trellis/spec/operations/validation-and-release.md:12` 描述只有四个 gate（且缺 api-facts），
  `--only lockfile|api-doc|doc-marks` 候选串过期（实际五选）。
- 同文件 "Release metadata" 段写"三个文件"，而 `AGENTS.md:120` 与 `check_release_metadata.py` 已是五个。

## 3. 孤儿自测（已证实）

- `scripts/sts2-model-budget-proxy-selftest.py`：全仓唯一严格零引用脚本（多轮 rg 检索无引用者）。
- 本机实测：`exit=0 elapsed=4.19s`，输出含 `{"name":"real_ledger_untouched","ok":true}`、
  `{"name":"http_limit","ok":true}` 等检查项 ⇒ 离线、零成本、可进 CI。
- 其宿主 `sts2-model-budget-proxy.py` 只被 `sts2-coop-full-run-acceptance.ps1:34`（真机人工编排）引用。

## 4. 打包缺版本守卫（已证实）

- `rg -n 'preflight|check_release_metadata|check_verification_gates' scripts/package-release.ps1` 无命中；
  `scripts/preflight-release.ps1` 与 `package-release.ps1` 互不引用。
- `check_release_metadata.py` 校验五个文件一致（mod_manifest / mod_id / Router / pyproject / uv.lock），
  但只在 CI（`validate.yml:30-32`）与本地 preflight（`:96`）里跑。

## 刻意不做

- 不删 `mcp_server/data/eng/`、不删脚本：属产品决策。
- 不给 CI 加 mod 主工程编译：CI 无 `sts2.dll` / `GodotSharp.dll`。
