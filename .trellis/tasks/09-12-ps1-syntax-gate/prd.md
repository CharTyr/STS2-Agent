# 给每个 PowerShell 脚本加一道 AST 解析 gate

## 背景

`scripts/` 下有 **30 个 `.ps1`**（`git ls-files scripts/*.ps1`），它们是构建、打包、发布、
实机验证与 CI 自测的入口。今天**没有任何检查会解析它们的语法**：

- 6 道 gate 里 `script-encoding` 只看"含非 ASCII 的脚本是否带 UTF-8 BOM"；
- preflight 的 "Compile Python sources" 只 `py_compile` 了 `mcp_server` 的两个 `.py`；
- CI 只跑其中少数几个具体脚本。

后果：某个 `.ps1` 里写错一个括号或漏一个引号，直到有人真的去跑它才会发现——
而那通常发生在"发布前"或"真机验证时"这种最不该出岔子的时刻。

这与上一轮给 16 个未编译 C# 源文件加的语法覆盖是同一类缺口：**缺少"最便宜的静态兜底"**。

## 目标

每个 `.ps1` 至少被解析一次；语法坏掉时 gate 变红并指名文件与行号。

## 验收标准

- [x] `check_verification_gates.py` 新增 gate `ps1-syntax`，进入 `GATES` 注册表，
      出现在 `--only` 候选里，并默认参与全量运行。
- [x] 覆盖 `<repo-root>/scripts/**/*.ps1` 的**全部**文件（当前 30 个），逐个报数（gate 输出里能看到文件数）。
- [x] **只解析、不执行**：使用 `[System.Management.Automation.Language.Parser]::ParseFile`，
      绝不 `Invoke-Expression` / `-File` / dot-source 任何仓库脚本。
- [x] 解析失败时报出 `文件:行:列 + 消息`，并且 gate 退出码为 1。
- [x] 找不到 PowerShell 解释器、或目录里没有 `.ps1` 时**跳过并说明**（与 `docs-tracked` 在无 `.git` 时的跳过形态一致），
      不是静默通过、也不是报错。
- [x] `scripts/test-verification-gates.ps1` 增加自测：往 fixture 的 `scripts/` 放一个语法坏掉的 `.ps1` ⇒ gate 必须拒绝；
      放一个合法 `.ps1` ⇒ gate 必须接受。
- [x] `.trellis/spec/operations/validation-and-release.md` 的 gate 表与 count 从 6 改到 7，`--only` 列表同步。
- [x] 全量 gate、preflight、CI 绿。

## 范围外

- 不解析 `.sh`（没有等价的静态解析能力，且 CI 只跑 Windows）。
- 不做 PSScriptAnalyzer 风格的**规则**检查（未使用变量、别名等）——只做语法。
- 不改任何既有脚本的内容（本任务只加检查能力）。

