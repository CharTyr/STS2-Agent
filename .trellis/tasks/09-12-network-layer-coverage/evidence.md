# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `mcp_server/tests/test_network_server.py`（新） | 23 条用例 |
| `mcp_server/tests/test_layered_tools.py`（新） | 12 条用例 |

生产代码零改动（`network_server.py`/`server.py`/`pyproject.toml`/`uv.lock` 相对 HEAD 无差异）。

## 验收

| 标准 | 证据 |
|---|---|
| 配置默认值 + 空 token 不启用鉴权 | `NetworkServerConfigTests` 断言 host/port/transport/path/tool_profile/api_base_url/token/log_level 与 `auth_enabled` 两分支 |
| `_env_flag` 真值表 | `EnvFlagTests`：1/true/True/TRUE/`"  yes  "`/on/0/false/no/2/enabled/空串/纯空白；实测校准出一条真实行为——纯空白不回落默认值（显式非真值） |
| `_normalize_path` 边界 | `NormalizePathTests`：9 种输入（含 `/mcp/`、`//mcp`、多段、空串） |
| `parse_args` | `ParseArgsTests`：10 个字段取自环境变量；`sse`+`--stateless-http` → `ctx.exception.code == 2` |
| `create_network_app` + healthz 两分支 | `NetworkAppTests`：patch `Sts2Client` 注入假 client（不连真实 8080、不绑端口）；断言返回三元组、超时配置、`/`、`/healthz`、`/mcp` 路由；healthz 正常 200 / `Sts2ApiError` → 503（保留 code/retryable）/ 普通异常 → 500 |
| 8 个 layered 工具 | 每个工具直接调用一次；写入全部落在 `tempfile`（`patch.dict` 设 `STS2_AGENT_KNOWLEDGE_DIR`，在 `create_server` 之前）；断言`jaw_worm_x2+louse_x1` 去重排序、无 event 分支零写入 |
| 不碰 sts2 的两个工具 | `complete_combat_handoff`/`complete_event_handoff` 断言 `client.get_state_calls == 0` |
| 仓库不被写脏 | 跑完全套后 `agent_knowledge/` 仍只有 `run_logs/` |

## 门禁

`cd mcp_server; uv run --locked python -m unittest discover -s tests` → **159 tests OK**（本任务 +35）。

## 未证明（只能实机）

- `run_network_server_async` 未调用（真实绑端口 + uvicorn.serve），其参数装配只经 `NetworkServerConfig`/`parse_args` 间接覆盖。
- 真实传输层（端口监听、远程 MCP 客户端 over HTTP/SSE、bearer 鉴权握手）需实机端到端验证。
