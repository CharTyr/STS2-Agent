# 设计：离线覆盖 network_server 与 layered 工具

## 1. `tests/test_network_server.py`

| 目标 | 手法 |
|---|---|
| `NetworkServerConfig` 默认值 | 直接构造 + 断言 `host/port/path`，`auth_enabled` 随 token 变化 |
| `_env_flag` | `patch.dict(os.environ, {...}, clear=False)` 真值表 |
| `_normalize_path` | 参数化输入 → 期望输出 |
| `_build_auth_provider` | token 为空 → `None`；非空 → 返回 `StaticTokenVerifier` 实例 |
| `parse_args` | `patch.dict` 设环境变量后 `parse_args([])`；互斥参数用 `assertRaises(SystemExit)` + `.code == 2` |
| `create_network_app` | `patch("sts2_mcp.network_server.Sts2Client")` 注入 fake；断言返回三元组与路由存在；直接调用 `root_endpoint` 断言 `ok=True` |
| healthz 分支 | fake client 让 `get_health()` 返回 ok / 抛异常，断言 200 与 503 两种响应 |

不要真实绑定端口；`run_network_server_async` 不调用。

## 2. `tests/test_layered_tools.py`

复用现有 `DummyClient` 模式（`test_waits.py:23`、`test_agent_contract.py:61`）：

```python
with tempfile.TemporaryDirectory() as tmp:
    os.environ["STS2_AGENT_KNOWLEDGE_DIR"] = tmp      # 必须在 create_server 之前
    server = create_server(tool_profile="layered", client=DummyClient(...))
    fn = server.get_tool("get_planner_context").fn
    payload = asyncio.run(fn())
```

注意用 `patch.dict` 包裹，别把环境变量泄漏到其它测试。

用例：

- `get_planner_context` / `create_planner_handoff`：无 event 的纯分支（state 无 `event.event_id` 时不落盘），
  断言键集与 `instructions` 文案要点；
- `get_combat_context` / `create_combat_handoff`：给 `combat.enemies` 的 state，断言 `combat_key`/去重排序；
- `complete_combat_handoff` / `complete_event_handoff`：断言 `DummyClient.get_state` **未被调用**
  （它们不该碰 sts2）——这是行为契约，不是实现细节；
- `append_combat_knowledge` / `append_event_knowledge`：断言写入落在 tmp 目录内的期望相对路径。

## 3. 运行约定

只用 stdlib `unittest`/`unittest.mock`、`tempfile`；不新增依赖（`--locked` 会失败）；
文件放入 `mcp_server/tests/`，名字 `test_*.py`，无 `__init__.py`。
