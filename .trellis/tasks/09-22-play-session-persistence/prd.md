# 游玩对话 session 持久化（按 run 保存/恢复上下文）

## Goal

让一个游戏 run 存档对应一次对话 session：玩家可中途保存退出，下次「继续游戏」时恢复该 run 的对话与决策上下文；切换模型时上下文不丢。这里的「存档」指游戏内「继续游戏」的 run 存档，而非整个游戏存档。

## Requirements

### 现状（来自代码勘察）
- 游玩循环 `AgentLoop.PlayOnceAsync` 是**逐手无状态**的：每轮重建 system+playbook+策略+队伍上下文+vision+压缩记忆+最新 state。所谓「记忆」来自 `ContextCompaction.Format` 对 `DecisionLog` 的摘要（`_recentDecisions` 提供者），不是对话 transcript。
- 对话（`ChatAsync`）是另一条独立会话，历史只在 overlay 内存里。
- `DecisionLog` 已 best-effort 落盘（200 条内存 + 2MB 文件轮转），但**不按 run 恢复进循环记忆**。

### 需求
- 以 `run_id` 为键持久化一个 run 的 session：
  - 对话历史（用户消息 + 模型回复 + 决策/工具调用条目）。
  - 决策上下文（供 `ContextCompaction` 的近期决策序列）。
  - 双层模式下的当前 `PlayStrategy`。
- 「继续游戏」进入同一 run 时，恢复上述上下文：对话区重现历史、决策记忆接续、策略接续。
- 切换模型（conversation/play model）时保留并复用该 run 的上下文（上下文是与 run 绑定、与模型无关的）。
- 中途退出（关 overlay/关游戏）不丢：持久化是即时的或退出时 flush。
- 新 run（新存档）开启新 session，不串上下文。

## Acceptance Criteria

- [ ] 每个 run 的对话历史按 `run_id` 持久化到磁盘，重新进入该 run 时对话区恢复显示。
- [ ] 决策上下文（近期决策序列）按 run 恢复，`ContextCompaction` 在继续游玩时能基于历史决策工作。
- [ ] 双层模式下 `PlayStrategy` 按 run 保存与恢复。
- [ ] 切换模型后继续同一 run，上下文保留。
- [ ] 中途退出后再进入，上下文不丢。
- [ ] 新 run 开启全新 session。
- [ ] 持久化失败不影响游玩（best-effort，对齐 DecisionLog 的「诊断绝不阻断游玩」原则）。
- [ ] 敏感信息（API key）不写入 session 文件；沿用 `DiagnosticExport.Redact` 脱敏。
- [ ] 单测覆盖：session 序列化/反序列化、按 run 键恢复、模型切换场景、损坏文件恢复。

## Notes

- 依赖子任务1（对话并入游玩页的形态）落地后再做。
- 存储位置建议与 `SettingsStore` 同级目录下的 `sessions/` 子目录，每 run 一个文件（`{run_id}.json`），原子写 + .bak。
- 注意 `run_unknown` 占位 run 不落盘（对齐 DecisionLog 对 `run_unknown` 的归一）。
