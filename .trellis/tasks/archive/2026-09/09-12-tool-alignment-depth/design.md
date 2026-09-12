# 设计：双向集合 + 参数名

## 数据来源（全部来自源码文本，无需运行游戏）

| 面 | 位置 | 提取方式 |
|---|---|---|
| C# 动作分支 | `STS2AIAgent/Game/GameActionService.cs` `ExecuteAsync` 的 `"name" =>` 分支 | 正则 |
| C# 原生 MCP 分支 | `STS2AIAgent/Server/NativeMcpServer.cs` 的 switch 分支 | 正则 |
| Python 动作 | `mcp_server/src/sts2_mcp/server.py` 的 `_LEGACY_ACTION_TOOLS` + guided 工具映射 | 正则/AST |
| Python 参数 | `client.py` 逐方法签名（`inspect.signature` 或 AST） | AST |
| 文档契约 | `docs/api.md` 的 `<!-- BEGIN ACTION CONTRACT -->` 块参数列 | 正则在块内解析 |

## 断言

1. `python_actions - csharp_switch_actions == exempt`（exempt 为空或每条带注释）。
2. `csharp_switch_actions - python_actions == exempt_reverse`（明确哪些动作故意不暴露）。
3. 对同名动作：`python_param_names(action) == contract_param_names(action)`
   （忽略 `client_context` 之类内部参数，名单要写在测试里）。
4. 失败时打印差集（不是逐个 assert，便于定位）。

## 可测性注意

- 现有 `test_legacy_action_coverage.py` 可能已覆盖部分集合关系：先读它，扩展而不是复制。
- 路径解析照 `test_legacy_action_coverage.py:20-25` 的 marker 探测，不依赖 CWD。
- 不要在 import 期做重活；把解析放进 `setUpClass`。
