# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `mcp_server/tests/test_native_tool_alignment.py` | 扩写（+310/-1）：新增 3 条测试与源码解析辅助函数；原名字比对测试保持原样 |

生产代码零改动；反证自检期间临时改动的 `NativeMcpServer.cs` 已按 SHA256 还原（`59A67FC5…4044`）。

## 新增断言

| 测试 | 覆盖 |
|---|---|
| `test_native_switch_covers_the_python_guided_surface` | 双向分支覆盖：Python guided 工具集 ↔ `ExecuteToolAsync` switch 分支（豁免 `wait_for_event`，带理由）；并断言 `AgentTools.Mcp`（tools/list 广告面）⊆ switch |
| `test_shared_guided_tools_use_identical_argument_names` | 同名工具参数名：Python inputSchema ↔ C# 实际解析的 `arguments` 键（递归合并 `ExecuteActAsync`/`WaitUntilActionableJsonAsync`；`timeout_seconds` 由 `ReadTimeoutSeconds` 方法体推导）；失败时打印差集 |
| `test_documented_action_arguments_match_registered_legacy_tools` | `docs/api.md` 契约块（55）↔ full profile 实注册 legacy 工具（54）双向集合 + 参数名；豁免 `run_console_command`（debug 单独注册）与 `crystal_clear_cell: {tool}`（契约块冻结），且豁免变陈旧会额外报错 |

实测抽取值（非空转）：switch 分支 9 条；`act` → `['action','card_index','option_index','target_index','tool','x','y']`；`wait_until_actionable` → `['timeout_seconds']`。

## 可否证（真红）

1. 删 C# `case "act":` → `FAIL test_native_switch_covers_the_python_guided_surface`
2. `ReadInt(arguments,"option_index")` → `"index"` → `FAIL test_shared_guided_tools_use_identical_argument_names`（打印两列参数名）
3. 移除 `crystal_clear_cell` 豁免 → `FAIL test_documented_action_arguments_match_registered_legacy_tools`

## 门禁

`uv run --locked python -m unittest discover -s tests` → **159 tests OK**（本任务 +3）；
`python scripts/check_verification_gates.py` → 通过（`55 actions match`）。

## 遗留

`docs/api.md` 动作契约块未登记 `crystal_clear_cell` 的可选参数 `tool`（契约块被 gate 冻结，本任务禁改），已落成显式豁免；
建议后续单独开任务补文档或确认可忽略。
