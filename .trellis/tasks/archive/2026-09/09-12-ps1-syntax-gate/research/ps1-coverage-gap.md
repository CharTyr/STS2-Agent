# 证据：PowerShell 语法今天无人检查

## 现状核对

| 检查 | 覆盖范围 |
|---|---|
| gate `script-encoding` | 仅"含非 ASCII 的 `.ps1` 是否带 UTF-8 BOM"，**不解析语法** |
| preflight "Compile Python sources" | `python -m py_compile <client.py> <server.py>`——两个 `.py`，不含 `.ps1` |
| CI `validate.yml` | 只调用具体几个脚本（profile / gates 自测 / 退出码传播），不解析其余 |
| C# 测试 | `CompanionStartupTests` 等只把 `.ps1` 当文本读（`AgentSourceFixture.Read`），不解析 |

`git ls-files scripts/*.ps1` = **30 个**（含 `build-mod.ps1`、`package-release.ps1`、
`preflight-release.ps1`、两个 gate 自测、十余个实机验证脚本、平台镜像等）。
仓库里没有任何 `.ps1` 在 `scripts/` 之外。

## 为什么这不是理论风险

这些脚本是发布链路的一部分：`package-release.ps1` 负责产出玩家 zip，
`preflight-release.ps1` 是"发布前总检"，`sts2-coop-full-run-acceptance.ps1` 是双实例联机验收。
它们大多**只在真机/发布时**才被跑到——正是最不能出岔子的时刻。语法错误在真机上表现为
"脚本启动即失败"，而没有 gate 会在提交时提醒。

## 与既有工作的一致性

- 上一轮给 16 个 CI 无法编译的 C# 源文件加了 Roslyn 解析兜底（`SourceCoverageTests`）；
- 本任务把同一种"最便宜的静态兜底"补到 `.ps1` 上。
- 两次都遵守同一原则：**只解析、不执行**。

## 只能 CI/环境相关确认

- `pwsh`（PowerShell 7）与 `powershell`（5.1）在语法解析上对我仓库里用的语法形态一致；
  本机与 CI 都装了 `pwsh`（CI 日志显示 runner 用 `C:\Program Files\PowerShell\7\pwsh.exe`）。
- "两者都没装"时的跳过路径无法在本机验证（本机两个都有），只能由代码审查确认。

