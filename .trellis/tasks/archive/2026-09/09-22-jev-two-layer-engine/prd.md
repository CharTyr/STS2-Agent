# Jev 双层决策：执行层 + LLM 规划层 + MCP parity

## Goal

为两种游玩模式引入双层决策模式：开启后由 Jev（TypeSafe System One 模型）通过 `POST /v1/systemone` 的 Choice 原语直接选择每一个具体操作，LLM 只在低频战略规划层根据游戏状态调整 Jev 的提问与策略。外部 MCP coding agent 通过新增 planner 工具充当同一规划层，保证与游戏内体验一致。初版仅适配 Jev。

## Requirements

### 执行层（Jev）
- 新增 `IActionDecider` 抽象：输入「同帧 state + available_actions 快照」与当前策略，输出一个具体动作决策（动作名 + card/target/option/x/y/tool 索引 + 置信度 + 概率 + 用量）。
- `JevExecutionDecider` 为默认实现：把快照展开为**具体可选动作枚举**（如「打出 打击→敌人1」「结束回合」「选择地图节点2」），作为 Jev Choice 的 criteria（≤255 项）；单次调用可混合一个 Score（危险度，供 UI 展示）。
- 新增 `JevClient`（`IJevClient`）：`POST {BaseUrl}/v1/systemone`，Bearer 认证，显式超时，429 退避并尊重 `retry-after`，错误分类对齐 `ModelRoleProbe.FailureKind`（4xx→config，408/429/5xx/超时→network）。
- Jev HTTP 调用在游戏线程之外；只把选中动作经 `IGameBridge.ActAsync` 送回游戏线程。

### 规划层（LLM）
- 新增 `PlayStrategy`（打法姿态 + 指令文本 + 选项提示）与线程安全 `StrategyStore`（含内置默认策略）。
- 新增 `StrategyPlanner`：低频触发（换屏/换 Act/Boss/低置信连续/N 手一次），用只读 LLM 根据 run 摘要 + 当前屏 + 最近决策 + 当前策略产出更新后的 `PlayStrategy`，写入 `StrategyStore`。
- 策略文本注入 Jev 的 Choice instructions（分面：goal / 当前屏 playbook / 策略 / 合法性提醒），照搬 typesafe-mario 的 instructions 结构。

### 置信度门控
- `confidence >= JevConfidenceThreshold` → 直接执行（复用 `ExecuteActAsync` 全管道：合法性、`ActIndexValidator`、游戏线程、未稳定重等、指纹）。
- 低于阈值或 Jev 调用失败 → 回退现有 LLM 逐手决策路径（不中断整局），并触发一次策略刷新。

### 接入点
- 在 `AgentLoop.PlayOnceAsync` 拿到 state 之后、组装 LLM prompt 之前分支（摸底报告 B1）。双层开启且当前模式启用时走 Jev 路径，否则走现有 LLM 路径。
- 单人（`/session/control`）与多人 companion（`/teammate/control`→`/companion/control`）共用同一 `AutoPlayLoopAsync`，双层逻辑对两者一致生效（按各自模式开关）。

### MCP parity（双面）
- 新增 planner 工具，C# 原生面（`AgentTools.Mcp` + `NativeMcpServer.Tools.cs`）与 Python sidecar（`server.py` + `client.py`）各加一遍：
  - `get_planner_briefing`：run 摘要 + 当前屏 + 当前策略 + 最近 Jev 决策与置信度趋势。
  - `update_play_strategy`：写入新策略（posture / instructions / hints）。
- 新增 HTTP 路由 `GET /strategy`、`POST /strategy`（IsLocal 限制，仿 `/session/control`）。
- `test_native_tool_alignment.py` 双向比对通过（豁免表保持为空）。

### 决策日志与预算
- `DecisionLogEntry` 加可空 `confidence` 字段（`WhenWritingNull` 天然向后兼容）；Jev 决策 `source="jev"`。
- Jev 每次调用 `RequestsSpent += 1`；usage 未知保持 null。
- 必须填 `StateFingerprint`（no-progress 守卫依赖）。

## Acceptance Criteria

- [ ] 双层开启后，单人与 companion 的自动游玩由 Jev 逐步决策并执行，决策日志出现 `source="jev"` 且带 confidence。
- [ ] 置信度低于阈值时回退 LLM 逐手决策，整局不中断。
- [ ] LLM 规划层按触发条件更新策略，策略文本进入 Jev 提问。
- [ ] 外部 MCP agent 可经 `get_planner_briefing` / `update_play_strategy` 读取简报与调整策略；双面工具对齐。
- [ ] Jev 调用有显式超时、429 退避、取消传播；不在游戏线程内 await HTTP。
- [ ] 预算/日志/no-progress 守卫对 Jev 路径同样生效。
- [ ] C# 单测覆盖：JevTypes 序列化、JevClient（fake handler）、选项枚举、置信度门控、策略存储、DecisionLog confidence 字段。
- [ ] Python 单测覆盖新工具（DummyClient）+ 对齐测试。
- [ ] `docs/api.md` 增加新路由说明；`CHANGELOG.md` Unreleased 记录。

## Notes

- 形状模板：`Multiplayer/CompanionPlayPolicy.DecideImmediate`（state+actions+参数 → 动作+索引）。
- 参考 typesafe-mario `policy.py`：Choice 的 criteria=动作→描述、instructions 分面、单次混合多 question。
- 约束详见决策链路摸底报告 C1-C5（游戏线程、取消、预算、日志、no-progress）。
- 在线多人当前仅本地双开形态；本任务不新增跨网络 AI 协议。
