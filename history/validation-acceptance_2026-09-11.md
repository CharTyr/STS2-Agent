# 主动发言闸门与端点契约验收（2026-09-11）

记录日期：2026-09-11。范围：v0.10.6 标签后主线未发布变更——主动发言音量上限与对局边界两处修复、`docs/api.md` 端点补登、状态页过期口径修正——在隔离游戏副本上的实机验收。全部证据目录：`build/validation-2026-09-11/`（已 gitignore）。

## 1. 本轮修复

| 提交 | 内容 | 离线证据 |
| --- | --- | --- |
| deafa23 | 主动发言的 6 条上限改为随自动游玩会话开始重新发放（`ProactiveChatSession.BeginSession`，`AgentRuntime.StartAutoPlay` 调用）；75 秒间隔仍是跨会话全局约束 | `ProactiveChat.NewPlaySessionResetsCap` |
| deafa23 | `StartAutoPlay` 重置 `CurrentRunBoundary`，使「暂停期间开始的新一局」属于新会话，而不是被判成对局标识变化 | `CurrentRun.FreshSessionAcceptsNewRun` |
| e4c5797 | `docs/api.md` 补登 `/session/control`、`/events/stream`、`/companion/control`、`/companion/message`，补齐 `/health` 字段与 `stop_kind` 取值表 | `scripts/check_verification_gates.py` 四闸门 |

C# 核心测试：**217 PASS / 0 FAIL**（本轮新增 2 项）。

## 2. 为什么改这两处

- **上限不重置**：`AgentRuntime` 全进程只持有一个 `ProactiveChatSession`，而 `Reset()` 的唯一调用点是设置页的「重置本会话统计」。也就是说说完 6 句之后，主动发言在本次进程内永久沉默，与「每会话 6 句」的字面语义不符。改为每次自动游玩会话开始时归还额度。
- **对局边界跨会话残留**：`CurrentRunBoundary` 的类注释写的是「scoped to one automatic session」，但它实际是 runtime 字段，只在自动游玩循环观察到主菜单时重置。若上一局在暂停状态下结束（例如自动游玩在 GAME_OVER 停止、玩家手动回到主菜单），旧边界会带着上一局的 run_id 留下；此时开始新一局再按「开始自动游玩」，循环第一次迭代就抛出「检测到对局标识变化」，而且只要停在局内就会一直失败。

实机复现（修复前的构建，证据 `gate-timeline.jsonl` 第一段）：

```
{"at": "2026-09-11T20:19:11", "kind": "setup_run", "screen": "EVENT"}
{"at": "2026-09-11T20:19:14", "kind": "setup_autoplay", "play_running": false, "phase": "paused"}
```

同时段 `GET /health` 读数：`play_running=false, stop_kind=run_end`。由于 `StopKindPolicy` 现在只把两条对局边界文案归为 `run_end`，该读数即证明停在边界判定上。

修复后的同一序列（新构建）：

```
{"at": "2026-09-11T20:21:38", "kind": "setup_autoplay", "play_running": true, "phase": "running"}
```

## 3. 主动发言闸门实机验收

方法：隔离副本 + 本地零成本 OpenAI 兼容桩（`build/validation-2026-09-10/stub-model-server.py`，逐请求记账），配置 `settings.proactive-on.json` / `settings.proactive-off.json`（`supportsTools=false`，走 JSON 兜底路径）；自动游玩用 `POST /session/control {"running":true}` 启动，战斗用控制台 `fight NIBBITS_WEAK` / `win` 制造 COMBAT↔MAP 转移。驱动器：`build/validation-2026-09-11/proactive-gate-driver.py`。

| 边界 | 结果 | 证据 |
| --- | --- | --- |
| 开关关闭的负例 | 自动游玩跑满 50 次请求、期间进入并打完战斗，**0 条**主动发言 | `off-ledger.jsonl`（20:14:09–20:14:58），同时段 `/state` 为 COMBAT 且 `play_running=true` |
| 战斗开始触发 | 触发，提示词为 `A fight just started…` | `on-ledger2.jsonl` 20:21:40、20:34:37 |
| 战斗结束触发 | 触发 6 次，提示词为 `The fight just ended…` | `on-ledger2.jsonl` 20:23:09 / 20:24:38 / 20:26:06 / 20:27:34 / 20:29:04 / 20:31:21 |
| 75 秒最小间隔 | 9 次发送间隔 88–137 秒；另做阻断测试：发送后 **12.2 秒**强制 COMBAT→MAP 与 MAP→COMBAT 转移，随后 63.4 秒内 0 条发送 | `gate-timeline.jsonl` 的 `block_*` 段（`refused=true`） |
| 每会话 6 条上限 | 满 6 条后，第 7 个时刻（84 秒后的真实转移，自动游玩仍在跑）0 条发送 | `gate-timeline.jsonl` 的 `cap_result`（`refused=true`） |
| 暂停/继续归还额度（本轮修复） | 暂停再继续自动游玩后，第 7 条被接受 | `gate-timeline.jsonl` 的 `reset_result`（`accepted=true`，20:31:21） |
| 只读路径 | 主动发言请求只走对话模型，`act` 不在其工具集合内 | 离线 `ProactiveChat.ReadOnlyCannotAct`；本轮到 20:34:37 为止共 641 次请求，主动发言请求与游玩请求可在 `last_user_head` 上直接区分 |

观察：循环在每次迭代开头观察情境，因此发送可能比屏幕转移滞后十几到二十几秒（`cycle` 读到的 `sends_after` 有时比实际早一拍）。上表的间隔结论按记账时间戳计算，不受该滞后影响。

## 4. 端点契约与文档

`docs/api.md` 此前只登记 `/health`、`/state`、`/actions/available`、`/action`，而生产代码还实现了 `POST /session/control`（悬浮窗「开始自动游玩」的同一入口，也是本轮验收唯一可编程启动方式）与 `POST /companion/control`、`POST /companion/message`、`GET /events/stream`。本轮补齐，并把 `/health` 的字段说明与 `stop_kind` 取值表写进契约；示例值同步到当前 `0.10.6` / `v0.111.0`。

同批修正的状态页口径：#82 修复后的选牌屏一行、宠物与球槽一行、停止原因分类一行；`docs/proactive-chat-review.md` 的「Not verified: real in-game behaviour」已过期，改为两轮实机结论加仍然未验的部分；`docs/mechanic-coverage-matrix.md` 的 Deck selection 行补上 #82 更正说明（保留历史快照标记）。

## 5. 环境与真实存档

- 隔离副本：`build/validation-2026-09-08/game/`，本轮部署的 DLL SHA256 前 16 位 `1276E878E525D6BE`；档案 `--clientId 2026091012`；`STS2_AGENT_SETTINGS_PATH` 指向本轮 `settings.proactive-*.json`。
- 真实存档保护快照复验（`build/validation-2026-09-10/protected-snapshot.json`，197 项）：**0 changed、0 missing**。真实 Steam 安装、真实存档、`%APPDATA%/STS2AIAgent` 配置均未被本轮改动的隔离副本触碰。
- 正式安装目录当前仍是 v0.10.6 的 `47CE0F90…`，本轮未重新部署到正式安装。

## 6. 未覆盖项
## 7. 第二轮：审计发现的缺陷与修复（同日稍后）

对 HTTP 服务层、自动游玩停止/恢复、悬浮窗文案与 MCP 契约做了四份只读审计，发现并修复以下真实缺陷。

| 缺陷 | 触发条件与后果 | 修复 | 验证 |
| --- | --- | --- | --- |
| `POST /action` 无 body 上限、无 JSON 容错 | 非 JSON 或字段类型错误（如 `card_index` 传字符串）抛 `JsonException`，被通用 catch 接成 **500 `internal_error`**，而契约要求 400 `invalid_request`；chunked body 还会无界读取 | `Router.RequireBoundedBody` + `ReadJsonBodyAsync`，两条路由统一走这两个助手 | 实机探针：not-json / wrong-type / empty / missing 全部 HTTP 400 `invalid_request`（此前为 500） |
| `POST /session/control` 非 JSON body → 500 | 同上 | 同上 | 同上 |
| 停止原因靠中文关键词匹配 | 决策失败的包裹文案会拼上游戏原始错误串；错误串含「上限」时被报成 `budget`，界面建议「重置本会话统计」而不是查看局面 | `AutoPlayStoppedException` 带可选 `Kind`；预算守卫与对局边界显式声明类型；`StopKindPolicy.Resolve` 让显式类型优先 | 离线 `StopKind.ExplicitKindWins`；`StopKind.RetryIsNotRunEnd` |
| 预算关键词误判 | `Classify` 对整串做 `Contains("上限")`，普通失败文案可能命中 | 移除裸词匹配：预算停止只由 `SessionBudgetGuard` 的显式类型产生 | 同上（`Classify("手牌已达上限")` 现为 `failed`） |
| 循环内重置对局边界 | `AutoPlayLoopAsync` 每轮看到菜单屏幕就换新的 `CurrentRunBoundary` 再立刻 `Check`，`_enteredRun` 为 false，**离开对局永不触发停止** | 删除循环内重置，边界只在 `StartAutoPlay` 安装 | 实机：`save_and_quit` 回主菜单后 `play_running=false`、`stop_kind=run_end` |
| 悬浮窗写盘异常逃逸 | `PersistOverlayVisible` / `PersistOverlayPlacement` / `PersistChatAttachFlags` / `SetMcpEnabled` 直接调用会 rethrow 的 `SettingsStore.Save`，磁盘满或被占用时异常抛进 Godot 信号回调，开关显示与实际不一致 | 新增 `SaveSettingsQuietly`，捕获 `IOException` / `UnauthorizedAccessException` 并写入状态行 | 代码路径 + 既有设置页文案 |
| 「队友正在行动」为死分支 | `Phase` 只有 paused/running/stopping，running 被上一分支的 `PlayPhase == "running"` 抢先命中，玩家只会看到「正在请求模型」 | 该分支收窄为 `s.RequestingModel` | 离线 `Session.RunningBranchReachable` |
| 单人自动游玩显示「可以邀请 AI 队友」 | ready_to_invite 分支排在所有运行中分支之前且不判断 `PlayRunning` | 条件加 `!s.PlayRunning` | 同上 |
| MCP 动作超时短于 Mod 等待 | `_DEFAULT_ACTION_TIMEOUT = 30s`，而 `continue_game_over` 最多等 60s 原生存档 → 正常慢路径被当成响应丢失 | 默认提升到 75s | 离线 `test_action_timeout_covers_the_longest_server_wait` |
| 连接被拒报成 outcome_unknown | 拒绝连接意味着请求从未到达 Mod，却被报成「可能已完成，不要重放」 | 仅 `ConnectionRefusedError` / `gaierror` 归为 `connection_error`（可重试）；已发出后丢失仍为 uncertain | 离线 `test_refused_connection_is_retryable_connection_error`、`test_lost_response_after_send_stays_uncertain` |
| `status="failed"` 被当成功 | 契约允许该取值，客户端只看 `ok` | `_decode_action_success` 对该取值抛 `action_failed` | 离线 `test_failed_status_is_not_reported_as_success` |
| 文档缺口 | `/data/{collection}` 与 `/mcp` 从未登记；7 个错误码未登记；README 的超时环境变量名不存在 | 补 `GET /data/{collection}`、`POST /mcp` 章节与错误码表；README 改为 `STS2_API_READ_TIMEOUT` / `STS2_API_ACTION_TIMEOUT` / `STS2_API_MAX_RETRIES` | 四闸门通过 |
| 工具文案指向 compact 视图不存在的字段 | `act` 说明要求读 `requires_target` / `target_index_space` / `valid_target_indices`，但 `agent_view` 只有 `target` / `targets`（仅 `rest.options` 是三件套） | 文案改为按 compact 视图描述，并说明全量状态里的对应名字 | 对照 `GameStateService` 的 compact 构建代码 |

实机验证（隔离副本，DLL `315ED550…` 之后重建为最终构建）：

```
action: not json         HTTP 400 ok=False code=invalid_request
action: wrong type       HTTP 400 ok=False code=invalid_request
action: empty            HTTP 400 ok=False code=invalid_request
action: missing field    HTTP 400 ok=False code=invalid_request
action: unknown verb     HTTP 409 ok=False code=invalid_action
session: not json        HTTP 400 ok=False code=invalid_request
session: wrong type      HTTP 400 ok=False code=invalid_request
session: missing field   HTTP 400 ok=False code=invalid_request
data: unknown            HTTP 404 ok=False code=collection_not_found
```

对局边界实机序列（本轮修复的核心证据）：

1. `continue_run` → REWARD（在局内）
2. `POST /session/control {"running":true}` → `play_running=true`、`stop_kind=null`（此前该场景会立刻以 `run_end` 停止）
3. 在自动游玩进行中执行 `save_and_quit` → 回到 MAIN_MENU
4. 8 秒后：`play_running=false`、`stop_kind=run_end` —— 离开对局的停止判定仍然有效

测试计数：C# 核心 **219 PASS / 0 FAIL**（本轮新增 3 项）；MCP **53 项**通过（本轮新增 4 项）。

证据文件：`build/validation-2026-09-11/http-contract-probe.py`、`boundary-ledger.jsonl`、本轮 `/health` 读数。


- 真实模型连通与发言质量：本轮仍用本地桩，未调用真实上游、未消耗预算。
- 6 条上限的跨会话语义只在单进程内验证；未测「长时间多局游玩后是否仍会给出 6 条」的实际体感。
- 阻断测试依赖控制台 `fight` / `win` 制造转移，未覆盖玩家正常推进节奏下的间隔表现。
- `/companion/control` 与 `/companion/message` 本轮只按源码与既有契约测试登记，未新增实机调用证据（`/events/stream` 的实机帧捕获见第 4 节）。
- 第二轮修复中，「错误文案含关键词导致误分类」只在离线测试里构造，未在真实对局中制造该类错误文案。
- `/mcp` 原生 MCP 端点本轮未实机调用，沿用 2026-09-08 的 Origin 契约与实机探测结论。
