# 修复 companion health、SSE 生命周期与谓词拆分

## Goal

修复 companion 健康身份误判与角色错误可观测性，消除零 SSE 订阅者时的周期性完整状态构建和满队列静默丢事件，并在零行为变化前提下继续拆分 `GameStateService` 谓词层。

## Background

- v0.13.0 起 `/health.data.status` 来自兼容性探针，合法活跃状态为 `ready` 与 `degraded`；当前 companion 身份检查及 POSIX 启动等待仍只接受 `ready`。
- `Router.BuildHealthData()` 当前对 companion 也返回 host 双开状态机的默认状态文本，造成角色语义误导。
- `GameEventService.Start()` 当前无条件启动每 120ms 的完整状态构建；即使没有 `/events/stream` 订阅者也占用游戏线程。
- subscriber channel 当前使用 `DropOldest`，而发布端依赖 `TryWrite == false` 识别慢客户端，导致有语义事件可能静默丢失。
- 架构规范指定下一次机械拆分为 `GameStateService` 的 `Can*` / `Is*` predicate 层。

## Requirements

### R1 — Companion liveness/identity

- `CompanionHealth.IsExpectedProcess` 必须把 `ready` 与 `degraded` 都视为活跃兼容状态。
- 继续严格验证 `ok == true`、`service == "sts2-ai-agent"`、`instance_role == "companion"`、精确 `api_port` 与精确 `process_id`。
- 缺失、未知、非字符串 status、错误类型、错误 envelope 与 malformed JSON 必须拒绝。
- `scripts/lib-sts2.sh` 的启动健康等待采用相同的活跃状态集合，不弱化其进程与端口归属检查。

### R2 — Role-correct `/health`

- 审核并记录 common、host-only 与 companion 适用字段。
- 保持现有 key shape；companion 对 host-only 字段返回稳定的 `null`（不返回 host 默认文本或伪造状态）。
- host 继续返回真实 `companion_process_alive`、`companion_process_exited`、`companion`、`dual_status`、`dual_launch_outcome`、`team_control_status`。
- 两个角色继续返回自身 common 字段，包括 `instance_role`、`status`、`compatibility`、`api_port`、`process_id`、`play_running`、`play_phase`、`session_requests`、`state_build` 等。
- 更新 `docs/api.md` 与依赖 `/health` facts 的 gate/contract tests。

### R3 — Demand-driven event polling

- 服务启动不再等价于立即周期性构建 state；subscriber 为 0 时不周期调用 `BuildStatePayload`。
- 0→1 可靠唤醒唯一共享 poll loop；多个并发订阅者不能创建多个 poller。
- 1→0 后停止周期构建；其后新订阅者能恢复。
- 一个订阅者离开但仍有其它订阅者时继续 polling。
- `Stop()` 终止干净，旧 unsubscribe 不能影响新生命周期，shutdown 后不能被隐式重启，也不能复用 disposed token/source。
- 保持现有事件类型与首次状态语义；首个 subscriber 的第一份 state 仍产生 `session_started`，已有 snapshot 的后续 subscriber 收到 `stream_ready`。

### R4 — Explicit slow-subscriber failure

- 有界队列容量保持有限，producer 不等待慢 HTTP 客户端。
- 满队列时不能静默 DropOldest；非阻塞写入明确失败，慢 subscriber 从服务中移除且 channel 完成，使 SSE 客户端明确断开并通过重连/当前 state 对齐。
- 正常消费者事件顺序保持，未满不误断，一个慢 subscriber 不影响其它 subscriber。
- `event_id` 保持全局单调序号；断开/重连后的编号可显示中间存在 gap，不承诺 replay。
- 审核 Python `iter_events` / `wait_for_event`；只在服务端主动关闭会破坏现有 fallback/reconnect 契约时修改。

### R5 — Predicate partial relocation

- 按实际依赖将明确属于 availability/predicate 层的 `Can*`、`Is*` 及仅服务于该层的 helper 移到一个或少数 `GameStateService.*.cs` partial 文件。
- 这是机械 relocation：签名、方法体、方法顺序、判断顺序、异常/fallback、返回值保持原样。
- 同时被 raw builder 与 predicate 使用的 helper 留在 base 文件。
- 不修改 `EnumerateAvailableActions`、action availability、反射逻辑、payload 或 compact agent view 行为。
- 用逐行/成员证据证明从 base 删除的非空逻辑行原样出现在新 partial；同步下调 named budget 与架构实际行数，不提高总预算。

## Constraints

- 严格执行顺序：R1 → R2 → R3 → R4 → R5 → full verification。
- 每项先运行相关小范围测试；失败需记录真实错误、归因并修复本轮回归。
- 不修改已有未跟踪文件 `cstest.log`、`v.log`。
- 不伪造实机验证；本轮若仅离线验证，在文档和报告明确写 `offline verified / live validation pending`。
- 外部行为变化 R1–R4 检查 `docs/api.md`、`docs/live-validation-checklist.md`、`.trellis/spec` 与 `CHANGELOG.md` 的 `Unreleased`。
- R5 不制造产品 changelog 噪音。

## Acceptance Criteria

- [ ] ready companion accepted；degraded companion accepted。
- [ ] wrong pid/port/role/service、malformed/缺失/未知/非活跃 status 均 rejected。
- [ ] POSIX health wait 对 ready/degraded 一致，并有离线回归测试。
- [ ] host health 仍给出 host-only 状态；companion 对这些稳定 key 返回 `null`；common 字段均保持正确。
- [ ] 0 subscriber 时 0 次周期 state build；1 subscriber 启动；2 subscriber 仍单 loop；one leaves 继续；last leaves idle；后来订阅恢复；Stop 干净。
- [ ] 正常事件顺序保持；队列满明确关闭/移除慢 subscriber；无静默 DropOldest；其它 subscriber 不受影响。
- [ ] Python SSE 等待/回退测试证明服务端关闭契约可接受，或按必要范围修复。
- [ ] Predicate relocation 通过纯迁移证明、C# tests、source-shape 与 arch-facts，且 budget 只下调。
- [ ] `dotnet run --project STS2AIAgent.Tests\STS2AIAgent.Tests.csproj` 通过并报告测试总数。
- [ ] `mcp_server` 下 `uv run --locked python -m unittest discover -s tests -v` 通过并报告测试总数。
- [ ] `python scripts\check_verification_gates.py` 通过。
- [ ] `powershell -ExecutionPolicy Bypass -File scripts\preflight-release.ps1` 通过。
- [ ] 最终报告按 Task 1–5 列根因、文件、实现、测试、API/行为、验证、实机待办、commit hash，并列完整命令结果、git status、遗留问题与实机建议。

## Out of Scope

- 不把 `degraded` 改回 `ready`，不删除 compatibility 信息。
- 不重写整个 Router 或事件系统，不引入无限队列，不让 producer 等待消费者。
- 不重新设计 predicates、合并/重命名方法、改 LINQ/null/reflection/action/payload 行为。
- 不执行需要启动/控制真实游戏的验证，除非后续明确授权；离线结果不冒充实机结果。
