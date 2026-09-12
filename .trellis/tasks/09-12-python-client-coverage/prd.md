# 删掉 client.py 里的死代码，并给 55 个 per-action 方法加审计测试

## 背景

`mcp_server/src/sts2_mcp/client.py` 的 `invite_ai_teammate`（:384-391）在 `return` 之后
还挂着一整块**不可达代码**（:393-400）：

```python
def invite_ai_teammate(self) -> dict[str, Any]:
    return self.execute_action("invite_ai_teammate", ...)

    return self.execute_action(                 # ← 永远不可达
        "choose_timeline_epoch",
        option_index=option_index,              # ← option_index 在此作用域不存在
        ...
    )

def choose_timeline_epoch(self, option_index: int) -> dict[str, Any]:  # 正确的方法在下面
```

一旦有人误删上面那行 `return` 或调整缩进，就会立刻 `NameError`；现在它只是把
`invite_ai_teammate` 的真实实现藏在一块"看起来像第二个方法"的噪声里。

更重要的是：这 55 个 per-action 便捷方法**没有任何逐方法测试**，
`test_native_tool_alignment.py` 也只比工具名不比参数。参数塞错键（`card_index` 塞进
`option_index`）、`client_context` 写错、方法名与 action 名不一致、一次调用发两个 POST
都不会被现有测试抓到。

## 目标

1. 删除 :393-400 的死代码，保持 `invite_ai_teammate` 行为不变。
2. 新增 `mcp_server/tests/test_client_actions.py`：逐方法验证
   `POST /action` 的 `action` 名、索引参数映射与 `client_context`。
3. 加一条 AST 守卫：每个 per-action 方法体内**恰好一次** `execute_action` 调用
   （这条会在修复前因不可达的第二块而……——注意：AST 统计的是调用次数，死代码也会被计入，
   所以它正好能把这类残留钉住）。

## 验收标准

- [x] `client.py` 中不再有不可达的 `execute_action` 块；`git diff` 只删不加。
- [x] 新测试遍历全部 55 个方法（方法清单从类定义里动态取，不手写死列表），
      对每个方法断言一次 `POST /action`、正确 `action` 名、正确 payload 键、
      正确 `client_context`、`is_action=True`。
- [x] 参数映射覆盖 6 种形态：无参(38)、`option_index`(15)、`play_card(card_index,target_index)`、
      `resolve_rewards(card_index,option_index)`、`choose_rest_option/use_potion(option_index,target_index)`、
      `crystal_set_tool(tool)`/`crystal_clear_cell(x,y,tool)`/`run_console_command(command)`。
- [x] 测试只用 stdlib `unittest` + `unittest.mock`，不新增依赖，不联网。
- [x] `cd mcp_server; uv run --locked python -m unittest discover -s tests` 全绿且计数增加。

## 范围外

- 不改 `execute_action` 本身（重试/对账已有 `test_action_replay_safety.py` 覆盖）。
- 不改 `server.py` 的工具注册与 profile 结构。
- 不校验 mod 端是否接受这些 action（属实机验证）。
