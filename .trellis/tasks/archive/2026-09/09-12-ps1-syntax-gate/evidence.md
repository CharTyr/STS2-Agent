# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `scripts/check_verification_gates.py` | 新增 gate `ps1-syntax`：`PS1_GLOB`/`PS1_PARSE_PROBE` 常量、`check_ps1_syntax()`、注册进 `GATES`、文件头说明、`import shutil`（仅标准库、不联网） |
| `scripts/test-verification-gates.ps1` | 新增 3 个自测 case（跳过无 `.ps1` / 接受合法 / 拒绝坏语法）；UTF-8 BOM 保持不变 |
| `.trellis/spec/operations/validation-and-release.md` | "Six → Seven"、`--only` 候选串加 `ps1-syntax`、gate 表加一行 |
| `scripts/serve-sts2-network-mcp.ps1` | **修复新 gate 抓到的真实语法错误**（见下） |

`.trellis/spec/operations/index.md` 未改：grep 确认它没有出现 gate 数量。

## 新 gate 立刻抓到一个真实的已提交 bug

`scripts/serve-sts2-network-mcp.ps1:58`：

    Write-Host "Starting STS2 network MCP server on http://$BindHost:$Port$Path"

PowerShell 把 `$BindHost:` 解析成作用域/盘符式变量引用 ⇒ **解析直接失败**，
即这个脚本在提交后从来就跑不起来。不是误报：同目录 `start-mcp-network.ps1:39` 在对应位置写的是
转义冒号形式（``$BindHost`:$Port``），说明作者知道这个坑、只是漏了一处。
且 `mcp_server/REMOTE.md:16` 把这个脚本作为可执行命令写进文档——命令"启动即失败"。

修法（与兄弟脚本对齐，一行）：给 `$BindHost` 后的冒号加转义。
修后 `Parser::ParseFile` 报 0 error，文件仍是纯 ASCII（不需要 BOM，`script-encoding` 绿）。

## 验收

| 标准 | 证据 |
|---|---|
| gate 进 `GATES`、出现在 `--only`、默认参与全量 | `--help` 的 `--only` 含 `ps1-syntax`；全量跑输出 `verification gates passed: api-doc, api-facts, doc-marks, docs-tracked, lockfile, ps1-syntax, script-encoding` |
| 动态覆盖全部 `.ps1`、逐个报数 | 临时树 29 个合法脚本 → `29 PowerShell scripts parse cleanly`（未写死数量） |
| 只解析不执行 | 探针只调 `[System.Management.Automation.Language.Parser]::ParseFile`，无 `Invoke-Expression`/`-File`/dot-source |
| 失败报 文件:行:列 + 消息、退出码 1 | `scripts/serve-sts2-network-mcp.ps1:58:56: Variable reference is not valid...`（修复前） |
| 无 `.ps1` / 无解释器时跳过并说明 | 自测 `PASS ps1-syntax gate skips a scripts/ that holds no .ps1`；解释器分支：强制 `PATH` 只留 PowerShell 5.1 时同样 `29 ... parse cleanly` |
| 自测：坏语法拒绝 / 合法接受 | `PASS ... rejects a script with a syntax error` / `PASS ... accepts a well-formed script`（自测共 21 个 case 全过） |
| spec gate 表与 count 6→7 | 见交付表 |
| 全量门禁 | 7 道 gate 全绿；preflight **372 PASS / 0 FAIL**；C# 351 PASS / 0 FAIL；MCP 165 OK |

**手动真实仓库验证**：把 `build-mod.ps1` 复制成坏语法放进 `scripts/`（临时）→
gate 同时报出该临时文件与那个既有真实错误 → 删除临时文件后只剩后者（即当时仍红，直到主代理修掉那一行）。

## 只能 CI/环境验证

- "无 PowerShell 解释器 ⇒ 跳过"分支：本机 `pwsh` 与 `powershell` 都在，无法本地制造两者皆无，
  只能靠代码审查确认（`shutil.which("pwsh") or shutil.which("powershell")` 为 `None` 时返回跳过说明）。
- CI `windows-latest` 上实际命中哪个解释器由 runner PATH 决定；两条路径的兼容性都已本地验证。

## 价值说明

这道 gate 的设计目的就是"提交时发现语法错误"。它在**首次运行**就抓到一个已提交且文档化的真实故障脚本，
本身就是它值得存在的证据。
