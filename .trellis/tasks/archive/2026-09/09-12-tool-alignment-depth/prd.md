# 把 native 工具对齐测试从"比名字"加深到"比参数与分支覆盖"

## 背景

`mcp_server/tests/test_native_tool_alignment.py` 目前只比较**工具名**：

- 不比较参数 schema（同名工具的 `option_index`/`card_index`/`target_index` 漂移不会被发现）；
- 不校验 C# `NativeMcpServer.ExecuteToolAsync` 的 `switch` 是否覆盖 Python 暴露的每个动作，
  反过来也不校验 Python 是否暴露了 C# 根本没有的动作（孤儿工具会在运行时才炸）。

## 目标

1. 在现有测试文件里加深断言（不新建重复文件）：工具名集合 + **参数名集合** + **动作分支覆盖**。
2. 冲突/孤儿必须有显式豁免清单，且每条豁免带理由注释（不允许静默放过）。

## 验收标准

- [x] 动作集合双向断言：Python 可调用动作 ⊆ C# `ExecuteAsync` switch 分支（无孤儿），
      反向差异必须落在带注释的豁免名单里。
- [x] 参数名断言：对同名动作，Python 侧（`client.py` 方法签名 / 注册的 inputSchema）
      与 C# 侧（`NativeMcpServer` 的解析或 `docs/api.md` 动作契约表的参数列）一致。
- [x] 断言可否证：临时在 C# switch 里删一个分支或改一个参数名，测试必须变红（记录实际输出）。
- [x] 现有 gate（`python scripts/check_verification_gates.py`）与 MCP 全量测试通过。

## 范围外

- 不改 `docs/api.md` 的动作契约块（55 个动作名由 gate 校验）。
- 不改 C# 或 Python 的生产代码；本任务只加深测试（若测试暴露真缺陷，先报告再决定是否开新任务）。
