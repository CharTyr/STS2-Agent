# 验证记录

游戏 v0.113.1（release_info commit d09d48e5），隔离副本 `build/live-coop-1131/game`，
独立 `--clientId 3911261301`、独立 APPDATA 与设置文件，无模型 Key，未触碰正式安装与存档。

## 一、后备传输恢复 API（PASS，实机）

部署 DLL SHA256 `FA8A69821C7CB2755688B4C6BF58E1FBA5C0EF14F43F0ABF52986BBA99EF1CE3`。

- 日志：`HTTP.sys refused the request queue (句柄无效。); using the loopback socket transport.`
  然后 `Listening on http://127.0.0.1:18341/ via socket`，`Ready on http://127.0.0.1:18341/`。
- `GET /health` → 200 `{status: ready, mod_version: 0.19.3, instance_role: human, api_port: 18341}`。
- `GET /state` → 200，`screen=MAIN_MENU`。
- `POST /mcp/control {running:true}` → 200 `mcp_enabled=true`。
- 原生 MCP：`initialize` 200（`serverInfo.version=0.19.3`）、`tools/list` 返回 16 个工具。

## 二、双开与外部 MCP 出牌（PASS，实机）

- `POST /action invite_ai_teammate`（外部 Agent 路由）→ 队友在 18342 起来：
  `/health` 报 `instance_role=companion`、`status=ready`、`play_phase=paused`、`mcp_enabled=true`。
- 双方进入同一局（MAP → COMBAT），主窗与队友各自读到自己的 `combat.player`。
- 队友经**原生 MCP** `act {"action":"play_card","card_index":0,"target_index":0}` →
  `status=completed`、`stable=true`，`cards_played_this_turn` 0→1，能量 3→2。
- 隔离成立：队友 `play_phase=paused`、`session_requests=0`（未调用模型）；
  主窗 `cards_played_this_turn=0`（未被代打）。
- 说明：为进入可出牌的战斗，主窗座位由 HTTP 动作驱动（选角、确认 FTUE、开局），
  这是前置条件准备，不是自动游玩验收。

## 三、两种传输都失败时的降级（PASS，实机）

占用 `STS2_API_PORT=18351` 后启动，日志：

```
[WARN] [STS2AIAgent] Local API unavailable: The local HTTP API could not start on any transport.
[INFO] [STS2AIAgent] Ready without a local API
[INFO] [STS2AIAgent.Overlay] Installed
[INFO] Finished mod initialization for 'STS2 AI Agent' (STS2AIAgent).
```

- Mod 初始化完成、悬浮窗安装成功；该端口上确实没有 API（探测超时）。
- 代码路径上 MCP 不启用、设置页显示「本机 API 未启动」、邀请/续局在共享门禁与执行器双重拒绝。
  这三处的界面读数未逐一截图核对。

## 四、离线

- C# 全量：1172 通过 / 0 失败 / 2 跳过（含 6 条真实 socket 回归）。
- `python scripts/check_verification_gates.py`：12 项门禁全过。

## 未验证

- Host/Origin 与队友令牌在 socket 传输上的逐条拒绝用例只有单元与共享代码覆盖，
  未在实机上逐条重放。
- 工坊内容、GitHub 产物布局本次未重新打包实测（实机用的是本地 mods 目录的同源码 DLL）。
