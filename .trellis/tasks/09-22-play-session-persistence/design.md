# 设计：游玩对话 session 持久化

## 核心判断

「上下文」在当前架构里不是单一 transcript，而是三样东西的组合。持久化要覆盖这三样，恢复时各归各位：

1. **对话历史**：用户消息 + 模型回复 + 决策/工具调用条目（子任务1把决策渲染进对话流后，对话区就是决策流）。
2. **决策记忆**：`ContextCompaction` 消费的近期决策序列（`AgentLoop._recentDecisions` 提供者当前读 `AgentRuntime.RecentDecisions`，即 DecisionLog 内存快照）。
3. **策略状态**：双层模式下的 `PlayStrategy`（子任务2的 StrategyStore）。

## 新增类型

- `Agent/PlaySessionRecord.cs`：DTO `{run_id, character, updated_at, chat: ChatTurn[], decisions: DecisionLogEntry[], strategy: PlayStrategy?}`。System.Text.Json，可空字段向后兼容。
- `Agent/PlaySessionStore.cs`：持久化服务。
  - 目录：`SettingsStore` 同级 `sessions/`，每 run 一个 `{run_id}.json`。
  - 原子写（临时文件 + move）+ `.bak`；损坏时备份恢复并出 notice（复用 SettingsStore 的模式）。
  - `Load(runId)` / `Save(record)` / `Delete(runId)`；`run_unknown` 不落盘。
  - best-effort：任何 IO 失败不抛给游玩路径。
  - 脱敏：写入前经 `DiagnosticExport.Redact`；绝不写 API key。

## 接线

- `AgentRuntime` 持有 `PlaySessionStore`；`CurrentRunBoundary` 已能提供 run_id / run 切换信号。
- **保存**：对话每追加一条、决策每记录一条、策略每更新一次，标记 dirty；在 run 切换、暂停、overlay 隐藏、游戏退出时 flush（对齐现有 `FlushSettingsIfDirty` 的时机模式）。
- **恢复**：`AutoPlayLoopAsync` 检测到进入一个有 session 的 run（`boundary.Check` 给出 run_id）时，`Load(runId)` → 恢复对话到 overlay、把 decisions 喂给 `_recentDecisions` 提供者、把 strategy 写回 `StrategyStore`。
- **模型切换**：上下文与模型无关，天然保留；`ContextCompaction.ResolveWindow` 按新模型窗口重新计算压缩阈值即可（既有逻辑）。

## 边界

- 容量：chat 与 decisions 各设上限（如 decisions 沿用 200、chat 200 条），超出裁最旧；文件大小上限 + 轮转（对齐 DecisionLog 2MB）。
- 并发：所有读写走 store 内部锁；flush 在后台，不阻塞游戏线程。
- 多实例：companion 进程有自己的 run 与 session 文件（run_id 不同），不共享。

## 测试

- 单测：record 序列化/反序列化、store 原子写与损坏恢复、按 run 键隔离、run_unknown 不落盘、脱敏、容量裁剪、模型切换场景（上下文保留）。

## 兼容与回滚

- 纯新增持久化层；无既有格式变更。回滚 = revert（session 文件残留无害）。
