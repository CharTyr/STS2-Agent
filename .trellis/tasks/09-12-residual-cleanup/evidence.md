# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `.gitignore` | 删掉 `docs/` 一行（同段其它条目保留）——docs 从此是受控目录 |
| `docs/model-compatibility-matrix.md`、`docs/phase-6-validation-2026-03-30-dynamic-card-values.md` | 入库（此前被 ignore 而游离在版本控制外） |
| `scripts/check_verification_gates.py` | 新增第 6 道 gate `docs-tracked`（`git ls-files -z -- docs` 求集合；无 `.git` 时跳过并说明） |
| `scripts/test-verification-gates.ps1` | 新增 3 条自测（拒绝未跟踪 / 接受全跟踪 / 无 .git 时跳过）+ `Invoke-Git`、`Remove-Fixture` 辅助 |
| `.trellis/spec/operations/validation-and-release.md` | 门禁表补齐 6 道 gate、刷新 `--only` 列表、版本同步改为五文件、新增 `## Script inventory`（含刻意未接线脚本与原因），并修 5 处漂移的行锚点 |
| `.trellis/spec/operations/index.md` | "三个版本来源" → 五个，并指向 `check_release_metadata.py` 与打包守卫 |
| `AGENTS.md`（gitignored 本地文件） | scripts 段与发布前检查段补上新的离线自测入口 |
| `scripts/preflight-release.ps1` | 新增步骤 "Check the model budget proxy (no-cost self-test)" |
| `.github/workflows/validate.yml` | 新增同命令步骤（steps 12 → 仍在 12 步内的新条目） |
| `scripts/check_release_package.py` | `SOURCE_FILES` 纳入该自测脚本（`SOURCE_FILES` 语义 = 源树必须有、不必发货；注释已写明） |
| `scripts/package-release.ps1` | 构建前新增 `Assert-ReleaseMetadataConsistent` 守卫（+19 行，纯新增） |
| `scripts/sts2-model-budget-proxy-selftest.py` | **修掉 harness 的偶发失败**：`MockUpstream.protocol_version = "HTTP/1.1"` + `_drain_body()` |

## 验收

| 标准 | 证据 |
|---|---|
| docs 不再被忽略、两文档入库 | `git status --ignored docs` 无 `!!`；两文件为 `A`；gate 报 `all 33 Markdown pages under docs/ are tracked by git` |
| 新 gate 可否证 + 无 `.git` 时跳过 | 自测三条全 PASS：`rejects a docs file git does not track`（真仓红输出见下）、`accepts a fully tracked docs tree`、`skips a tree without .git`；`--only` 列表含 `docs-tracked` |
| 自测脚本整体全 PASS | `verification gate self-test passed`（18 条） |
| spec 与实现一致 | 程序化比对：gate 表六个名字与 `--only` 候选串 == argparse choices；五版本文件与 `check_release_metadata.py` 实际校验范围一致；spec 内全部 `../` 链接与 `#L` 锚点逐条解析通过 |
| preflight 跑孤儿自测 | 新步骤 `[preflight] OK - Check the model budget proxy (no-cost self-test)`；整体 `PASS=353 FAIL=0`，OK 步骤 12 → **13** |
| CI 同样跑它 | `validate.yml` 含该步骤；用 `uv run --no-project --with pyyaml` 校验 YAML 合法 |
| 打包版本守卫可否证 | 临时把 `pyproject.toml` 改成 0.11.1 → 打包脚本在 `Building release mod artifacts` **之前** exit 1，zip 数 24→24、无 `*0.11.1*` 产物；还原后 checker 恢复一致 |
| 全量门禁 | C# **335 PASS / 0 FAIL**；MCP **167 OK**；gates 6 道全绿；`check_release_package.py --source-root .` 通过；preflight 353/0 |

## 额外修复：孤儿自测本身是 flaky（必须修，否则等于给 CI 加偶发红）

W3 接线时发现该自测约 5–8% 偶发失败（`http_allowlist_and_count` / `sse_usage` 报
`chat status 502`，代理侧 stderr 为 `upstream failure type=ConnectionAbortedError`）。

根因（已用受控实验定位，不是猜）：

```
baseline（原样）              : posts=40  failures=2   [(20,502), (34,502)]
仅改 keep-alive（不读 body）  : posts=150 failures=2   [(44,502), (93,502)]
keep-alive + 读干请求体       : posts=150 failures=0
keep-alive + 读干请求体       : posts=400 failures=0
完整自测 15 次                : 15/15 exit 0
```

`MockUpstream` 用的是 `BaseHTTPRequestHandler` 默认的 HTTP/1.0：响应后立刻关连接，
而请求体从未被读取；Windows 回环在这种"带未读数据的关闭"上发 RST，
代理的 `urllib` 读到 `ConnectionAbortedError`，落到 502 兜底分支。
改成 HTTP/1.1 + `_drain_body()` 后每个请求都保持成帧，竞态消失。

**没有**改生产代理的转发语义（尤其没有给付费 POST 加自动重试）——修复只在测试 harness 内。

## 只能实机 / 未证明

- CI 侧实际运行（无 push，GitHub Actions 未触发）：命令逐字相同、本地等价验证通过，但 runner 未跑。
- Script inventory 中"刻意未接线"的脚本（`scan-assembly-strings.ps1`、`generate-sts2-knowledge.ps1`、
  双开验收两脚本、`sts2-validation-secrets.ps1`）需要 `sts2.dll` / 在线游戏 / DPAPI 密钥，本机未执行。

## 范围外（未做，属用户决策）

不删 `mcp_server/data/eng/`、不删任何脚本、不给 CI 加 mod 主工程编译。
