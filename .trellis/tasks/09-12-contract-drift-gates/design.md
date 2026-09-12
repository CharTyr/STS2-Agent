# 设计：api-facts 门禁与 README 绑定

## 1. `scripts/check_verification_gates.py`

新增 `check_api_facts(repo_root)` 并注册进 `GATES` 字典（名字 `api-facts`），沿用既有的
`read_text` / `GateError` 风格与"每步 print 明细"的输出约定。

```python
VERSION_PATTERN = re.compile(r'"mod_version"\s*:\s*"([^"]+)"')
SCREEN_ROW = re.compile(r"^\|\s*\`([A-Z][A-Z0-9_]*)\`", re.MULTILINE)   # 以文档真实表格形态为准
SCREEN_LITERAL = re.compile(r'=>\s*"([A-Z][A-Z0-9_]*)"')
SCREEN_EARLY_RETURN = re.compile(r'return\s+"([A-Z][A-Z0-9_]*)"')
PORT_PATTERN = re.compile(r'"port"\s*:\s*(\d+)')
```

判定：

1. `api.md` 的 `mod_version` == `STS2AIAgent/mod_manifest.json` 的 `version`（不等即 GateError，
   错误信息里同时给出两个值）。
2. 从 `STS2AIAgent/Game/GameStateService.cs` 的 `ResolveNonModalScreen` 方法体里提取
   `=> "NAME"` 与 `return "NAME"` 两类屏幕名（后者覆盖早退分支），要求
   `代码集合 - 文档集合 == 空`；并给出反向差集作为提示（文档多列不算失败，除非实现时确认要严格）。
   **注意**：提取必须限定在 `ResolveNonModalScreen` 方法体内（用方法体切片），不要把整个文件里的
   字符串都收进来。
3. 端口：以 `HttpServer` 的默认值为事实源（实现时确认真实常量位置），与 `docs/api.md` 里
   明确声明"默认端口"的那一处比对；若文档没有可解析的默认端口，则退回"文档出现的端口集合必须含
   代码默认值"。

每条都要能被"改坏即红"验证，并写进 `scripts/test-verification-gates.ps1`。

## 2. `mcp_server/README.md` 工具清单

- 先修内容：把缺失的 14 项补进"当前工具"节（保持既有排版与描述风格，描述取自
  `server.py` 的 `ActionToolSpec` 文案或 `docs/api.md` 的动作描述）。
- 再加绑定：在 `mcp_server/tests/test_legacy_action_coverage.py` 追加一条测试，
  解析 README 里的工具名与 `server.py` 的 `_LEGACY_ACTION_TOOLS` 做**双向集合**断言
  （缺一即红，多一即红）。

## 3. CI 与门禁自测

- `.github/workflows/validate.yml`：在 verification gates 步骤之后加一步
  `python scripts/check_release_package.py --source-root .`（工作目录与既有步骤一致）。
- `scripts/test-verification-gates.ps1`：照既有 fixture 模式（临时复制 + 破坏 + 断言 exit≠0）
  为新门禁加用例；**该脚本是 .ps1 且含非 ASCII 时必须带 UTF-8 BOM**（script-encoding 门禁会检查）。

## 风险

- 屏幕名提取是文本级的，方法体切片必须准确；实现时先打印提取结果确认条数合理，再断言集合关系。
- 若 `docs/api.md` 的屏幕枚举表格形态与上面的正则不符，以实现时读到的真实形态为准（不要硬套示例）。
