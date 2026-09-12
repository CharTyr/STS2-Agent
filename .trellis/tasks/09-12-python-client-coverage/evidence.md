# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `mcp_server/src/sts2_mcp/client.py` | 删除 `invite_ai_teammate` 之后（:393-400）不可达的 `execute_action("choose_timeline_epoch", option_index=option_index, ...)` 块；`0 增 9 删`，无其它重构 |
| `mcp_server/tests/test_client_actions.py`（新） | 6 条用例 + AST 守卫 |

## 验收

| 标准 | 证据 |
|---|---|
| 死代码消失、只删不加 | `git diff --numstat` → `0 9`；`return` 后紧跟 `choose_timeline_epoch` 定义 |
| 55 个方法动态遍历 | 方法清单由 `ast` 解析得出（非手写），并断言恰等于 `{_LEGACY_ACTION_TOOLS 的 54 个}` ∪ `{run_console_command}` |
| 参数映射 6 种形态 | 实测分布：无参 33、`option_index` 15、`play_card(card_index,target_index)`、`resolve_rewards(option_index,card_index)`、`choose_rest_option/use_potion(option_index,target_index)`、`crystal_set_tool(tool)`/`crystal_clear_cell(x,y,tool)`/`run_console_command(command)` |
| 每方法一次 POST + 正确 action/键/client_context | `_request.assert_called_once_with("POST","/action",payload=...,is_action=True)`；哨兵值互不相同，错键必被抓 |
| 仅 stdlib、无新依赖 | 只 import `ast`/`unittest`/`unittest.mock`/`pathlib`；`pyproject.toml`/`uv.lock` 未改 |
| 可否证 | 三处变异全部被抓：`play_card` 键改错（运行时红）、`choose_map_node` 的 `tool_name` 改错（AST+运行时红）、把不可达块插回（AST 守卫 3 条全红，运行时仍绿 → 正是需要 AST 守卫的原因） |

## 门禁

`cd mcp_server; uv run --locked python -m unittest discover -s tests` → **159 tests OK**（本任务 +6）。

## 范围外

mod 端是否接受这些 action 未验证（属实机验证）。
