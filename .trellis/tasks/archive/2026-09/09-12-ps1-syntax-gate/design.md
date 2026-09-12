# 设计

## gate 实现（`scripts/check_verification_gates.py`）

新增函数 `check_ps1_syntax(repo_root: Path) -> list[str]`，签名与其它 gate 一致（返回 notes，失败抛 `GateError`）。

```python
PS1_GLOB = "*.ps1"

def check_ps1_syntax(repo_root: Path) -> list[str]:
    scripts_dir = repo_root / "scripts"
    paths = sorted(p for p in scripts_dir.rglob(PS1_GLOB) if p.is_file())
    if not paths:
        return [f"no PowerShell scripts under {scripts_dir.name}/ to parse"]
    interpreter = shutil.which("pwsh") or shutil.which("powershell")
    if interpreter is None:
        return ["no PowerShell interpreter on PATH; skipping the .ps1 syntax check"]
    ...  # 一次子进程解析全部文件，收 JSON 结果，逐条报错
```

要点：

- **解释器选择**：优先 `pwsh`（CI 的 `windows-latest` 与本地都有），回落到 `powershell`（Windows PowerShell 5.1）。
  两个都装了 `Microsoft.PowerShell.Utility` 的 `Parser` 类型；用哪个都能解析同一份语法。
- **一次调用解析全部文件**（不要每个文件起一个进程）：把文件列表经 stdin 或参数传给一段
  `-Command` 脚本，脚本对每个文件 `ParseFile`，把 `{file, errors:[{line, column, message}]}` 以 JSON 打到 stdout。
  实现时用 `-NoProfile -NonInteractive`，避免污染与交互等待。
- **不要执行**：只用 `[System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)`。
  `ParseFile` 是纯解析：有语法错的文件也不会被执行。
- **子进程失败的处理**：解释器启动失败/非零退出而拿不到 JSON ⇒ 抛 `GateError`（这是 gate 自身坏了，
  不能当成"脚本没问题"）。
- **输出形态**：与其它 gate 一致，逐条 `notes.append(...)`；成功时给一行汇总（例如
  `30 PowerShell scripts parse cleanly`）。gate 名注册进 `GATES` 字典，`--only` 自动带上
  （`choices=sorted(GATES)`）。

## 自测（`scripts/test-verification-gates.ps1`）

现有 `Assert-Case` 会：建 fixture → 变异 → 用 `--only <gate>` 跑 → 断言非零退出。新增两个 case：

1. 坏语法：把 `if (` 这类不闭合的文本写进 `$fixture/scripts/broken-probe.ps1` ⇒ 期望 gate 拒绝
   （`Assert-Case -Name "..." -Only "ps1-syntax" -Mutate { ... }`）。
2. 好语法：写一个合法 `.ps1` ⇒ 用一个"期望通过"的断言形态（现有脚本里若有 `Assert-Accepts` 之类就用它，
   没有就按同一模式加一个小 helper，或直接把 case 写成"gate 退出码为 0"）。
   注意 fixture 的 `scripts/` 里本来就有 `check_verification_gates.py`（唯一的文件），
   所以"无 `.ps1` ⇒ 跳过"这条路径也可以顺带钉住。

fixture 结构见 `scripts/test-verification-gates.ps1:85-101`。**不要**改动既有的 case 语义。

## 文档

`.trellis/spec/operations/validation-and-release.md`：
- "Six offline gates: ..." → seven，并把 `ps1-syntax` 加进括注；
- "The six gates are:" 表加一行；
- `--only` 候选串同步（当前写作 `--only api-doc|api-facts|doc-marks|docs-tracked|lockfile|script-encoding`）。
若 `.trellis/spec/operations/index.md` 也提到 gate 数量，一并同步。

## 陷阱

- `check_verification_gates.py` 保持**只用标准库**且不联网；调用 `pwsh` 与它已有的
  `subprocess.run(["git", ...])`（`docs-tracked`）是同一类做法，不违反约束。
- gate 必须能在 **fixture** 上跑（自测用 `--repo-root <fixture>`）：fixture 的 `scripts/` 只有那个 `.py`，
  所以"没有 `.ps1` ⇒ 跳过"这条分支必须真的存在，否则其它 case 会误红。
- 路径要用 `repo_root` 推导，不要用 `Path.cwd()`（自测从别处调用）。
- gate 输出不要打印整份脚本内容。
- `.ps1` 数量会增长：不要写死 30，动态枚举。

