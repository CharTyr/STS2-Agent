# 实施计划：游玩对话 session 持久化

## 前置

- 依赖子任务1（对话并入游玩页）与子任务2（PlayStrategy/StrategyStore）落地。
- 分支：`feature/play-session-persistence`。

## 步骤

### 1. 持久化层
- [ ] `Agent/PlaySessionRecord.cs` DTO。
- [ ] `Agent/PlaySessionStore.cs`：目录解析、原子写、.bak、损坏恢复、脱敏、容量裁剪。
- [ ] 单测：序列化/恢复/隔离/损坏/run_unknown/脱敏/裁剪。

### 2. 接线 AgentRuntime
- [ ] 持有 store；dirty 标记 + flush 时机（run 切换/暂停/隐藏/退出）。
- [ ] 恢复：进入有 session 的 run 时 Load → 对话/决策/策略各归各位。
- [ ] 单测：模型切换、run 切换隔离。

### 3. overlay 恢复显示
- [ ] 对话区从 record 重建。

## 验证

```powershell
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj
```

- [ ] 单测全绿。
- [ ] 实机：开 run 对话几句 → 退出 → 继续该 run → 对话/决策/策略恢复；切换模型上下文保留；新 run 全新 session。

## 回滚点

- 纯新增，revert 即可。
