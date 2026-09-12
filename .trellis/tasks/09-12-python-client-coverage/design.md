# 设计：动态遍历 + 单次调用守卫

## 1. 死代码删除

`client.py:391-392` 保留 `invite_ai_teammate` 的 `return`，删除 :393-400 整块
（含其后的空行），确保 `choose_timeline_epoch` 方法定义仍然紧跟其后。

## 2. `mcp_server/tests/test_client_actions.py`

数据驱动，避免手写 55 条：

```python
SENTINELS = {"option_index": 11, "card_index": 12, "target_index": 13, "x": 14, "y": 15,
             "tool": "small", "command": "cmd"}

def _per_action_methods(cls):    # 用 inspect.getmembers + AST 判定：方法体内出现 execute_action
    ...
```

对每个方法：

1. `inspect.signature` 绑定哨兵值（`self` 除外）→ 调用；
2. `patch.object(client, "_request", return_value={"ok": True})`，断言
   `mock.assert_called_once_with("POST", "/action", payload=..., is_action=True)`；
3. 断言 `payload["action"] == 方法名`、`payload["client_context"] == {"source":"mcp","tool_name":方法名}`；
4. 断言传入的索引哨兵值出现在**正确**的 payload 键上（`card_index` 不能落到 `option_index`）。

现有范式参考 `tests/test_crystal_sphere_tools.py:79-106`；`DummyClient`/`RecordingClient`
在 `test_waits.py:23`、`test_crystal_sphere_tools.py:11-21`。

## 3. AST 守卫

用 `ast` 解析 `client.py` 源码，对每个类方法统计 `self.execute_action(...)` 调用数：

- 恰为 1 → 通过；
- 0 → 报"方法没有发送动作"；
- ≥2 → 报"方法体有重复/死代码（疑似残留块）"。

该守卫独立于运行时行为，能直接钉住本轮删掉的那类残留。同时断言方法名集合与
`_legacy_action_tools` / `server.py` 的动作名单不冲突（若现有测试已覆盖则复用，不重复）。

## 4. 运行约定

```bash
cd mcp_server
uv run --locked python -m unittest discover -s tests -v
```

仓库路径解析照 `test_legacy_action_coverage.py:20-25`（`Path(__file__).resolve().parents[N]` + marker），
不要依赖 CWD。只用 stdlib。
