# 主动发言闸门与端点契约验收（2026-09-11）

记录日期：2026-09-11。范围：v0.10.6 标签后主线未发布变更——主动发言音量上限与对局边界两处修复、`docs/api.md` 端点补登、状态页过期口径修正——在隔离游戏副本上的实机验收。全部证据目录：`build/validation-2026-09-11/`（已 gitignore）。

## 1. 本轮修复

| 提交 | 内容 | 离线证据 |
| --- | --- | --- |
| 待提交 | 主动发言的 6 条上限改为随自动游玩会话开始重新发放（`ProactiveChatSession.BeginSession`，`AgentRuntime.StartAutoPlay` 调用）；75 秒间隔仍是跨会话全局约束 | `ProactiveChat.NewPlaySessionResetsCap` |
| 待提交 | `StartAutoPlay` 重置 `CurrentRunBoundary`，使「暂停期间开始的新一局」属于新会话，而不是被判成对局标识变化 | `CurrentRun.FreshSessionAcceptsNewRun` |
| 待提交 | `docs/api.md` 补登 `/session/control`、`/events/stream`、`/companion/control`、`/companion/message`，补齐 `/health` 字段与 `stop_kind` 取值表 | `scripts/check_verification_gates.py` 四闸门 |

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

- 真实模型连通与发言质量：本轮仍用本地桩，未调用真实上游、未消耗预算。
- 6 条上限的跨会话语义只在单进程内验证；未测「长时间多局游玩后是否仍会给出 6 条」的实际体感。
- 阻断测试依赖控制台 `fight` / `win` 制造转移，未覆盖玩家正常推进节奏下的间隔表现。
- `/events/stream` 与 `/companion/control` 本轮只按源码与既有契约测试登记，未新增实机调用证据。

