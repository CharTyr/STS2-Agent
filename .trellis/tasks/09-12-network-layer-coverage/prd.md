# 覆盖 network_server 配置与 8 个 layered 知识工具

## 背景

`mcp_server/tests/` 目前 7 个文件（`test_action_replay_safety`、`test_agent_contract`、
`test_crystal_sphere_tools`、`test_game_data_tools`、`test_legacy_action_coverage`、
`test_native_tool_alignment`、`test_waits`），
`rg -n 'network_server|handoff|knowledge' mcp_server/tests` **无命中**：

- `network_server.py`（:21-235）：`NetworkServerConfig`、`_env_flag`、`_normalize_path`、
  `_build_auth_provider`、`create_network_app`、`parse_args`、`run_network_server_async` 全无测试；
- `server.py` 的 layered 8 个工具（:625-724）也无测试。

这些代码决定"能不能连上、要不要鉴权、路径对不对"，是最容易被改坏又最不容易被发现的一层。

## 目标

1. 新增 `mcp_server/tests/test_network_server.py`：覆盖纯逻辑与参数解析（含互斥参数的报错路径）。
2. 新增 `mcp_server/tests/test_layered_tools.py`：覆盖 8 个 layered 工具的报文结构，
   并验证"不触碰 sts2 的两个工具"（`complete_combat_handoff`/`complete_event_handoff`）确实不调 `get_state`。
3. 全程离线：不连真实 `127.0.0.1:8080`，不绑真实端口，不写仓库 `agent_knowledge/`。

## 验收标准

- [x] `NetworkServerConfig` 默认值（host/port/path/token）、`auth_enabled` 在空 token 时为 False（有断言）。
- [x] `_env_flag` 的真值表（1/true/yes/空/未设置）用 `patch.dict(os.environ)` 覆盖。
- [x] `_normalize_path` 的边界（带/不带前导斜杠、空串、重复斜杠）有断言。
- [x] `parse_args`：默认值来自环境变量；`--transport sse` 与 `--stateless-http` 同时给时
      `parser.error` → `SystemExit(2)`（断言退出码）。
- [x] `create_network_app` 返回 `(server, client, app)` 且路由已注册（`/healthz` 等）；
      healthz 成功/失败分支用 fake client 或本地 stub，**不依赖真实 mod**。
- [x] 8 个 layered 工具逐个调用一次（`asyncio.run(server.get_tool(name)).fn(...)`，
      参考 `test_crystal_sphere_tools.py:45-47`），断言关键键存在；
      知识写入全部落在 `tempfile` 目录（`STS2_AGENT_KNOWLEDGE_DIR` 或
      `patch("sts2_mcp.server.Sts2KnowledgeBase")`，必须在 `create_server` 之前设置）。
- [x] 测试结束后仓库 `agent_knowledge/` 无新增文件（`git status` 证据）。
- [x] `uv run --locked python -m unittest discover -s tests` 全绿。

## 范围外

- `run_network_server_async`（真实绑定端口、跑 uvicorn）不做单测，只在测试里断言其参数装配可被读取。
- `network_server.py` 无 client 注入点这一点**不改生产代码**去加注入（如确有必要，改测试的 patch 方式）。
- 不改任何工具的返回结构。
