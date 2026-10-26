## 实机测试问题修复批次（fix/pr196-live-issues 合入）

针对实机测试发现的问题，全部修复并合入本 PR。静态验证：C# 737 测试全绿、Python 416 测试全绿、preflight 全过、mod 已构建部署。

### 已确认并修复的根因

**双层决策未生效（严重）**
- 根因：Jev decider 在 AgentRuntime 构造时一次性构建，启动后才填 API Key 则永远为 null → 双层分支永不进入。已改为按配置指纹逐回合惰性重建。
- 「测试 Jev 连接」原为不发包的占位桩，现改为真实 GET /v1/models 验证。
- 决策日志现在把 Jev 执行的动作记为 source=jev（带置信度），Jev 面板显示最近选择与概率分布（原为死 UI）。
- StrategyPlanner 原为死代码（从未实例化），现已接线：换屏/换阶段或连续低置信度时后台刷新策略。

**战利品秒选第一张（严重）**
- 根因：resolve_rewards / collect_rewards_and_proceed 不带 option_index 时游戏侧静默取第一张牌；playbook 还明示优先调用宏动作。现改为：未携带选择时流程停在选牌界面（pending_card_choice=true，返回 pending），由模型显式 choose_reward_card / skip_reward_cards 决策。Jev 枚举器同步把 resolve_rewards 展开为每卡一个选项+显式跳过。ActIndexValidator 对 resolve_rewards 的索引校验改为对照 reward.cards 并放行 -1 跳过哨兵。

**打几回合卡死无提示（严重）**
- 根因 A：GameThread.InvokeAsync 无超时/取消——游戏线程不 pump 时回合永久挂起，暂停都打不断。已加超时/取消重载并接入状态读取与可操作等待。
- 根因 B：LLM 请求默认 10 分钟硬超时且期间状态栏冻结。现状态栏显示当前阶段（读状态/等可操作/请求 Jev/请求模型）+ 已等待秒数；LLM/Jev 单次请求超时可在设置里调。
- 根因 C：WaitingForGame 无限 1 秒自旋且 UI 称"这是正常等待"。现连续等待 120 次（约 2 分钟）停机并给出可见原因。
- 异常不再只剩 message：保留异常类型。

**UI 批次**
- 思考内容（勾选「显示思考内容」）与每个动作决策以气泡进入对话流；原「思考」卡片删除。真实 reasoning_content 不再被 act 参数里的一句话覆盖。
- 对话冻结修复：RefreshDynamic 全包 try/catch（此前任何一处异常都会让对话永久停更且异常被吞）、800ms tick 也刷对话、上限统一为 200 条并显示"已省略 N 条"、自动滚到底。
- 「允许代打」选项已删（对话只读；说「帮我打」仍可单次代打；要连续打用自动游玩/单步）。
- 模型设置：每张模型卡片有「测试」按钮（真实 ping + tool_choice=required 的工具调用探测），通过显示 ✅ 徽标；主模型合并为一个（对话与游玩共用），视觉模型移到高级选项；「工具调用」勾选删除（以探测结果为准）；思考强度与回复语言（中/英/跟随玩家）移到游玩页对话卡。
- 首次配置引导按双 tab UI 重写（不再指向已删除的「AI 队友」tab）。

### pi agent 评估结论
pi 是 TypeScript/Node 工具包，无法嵌入游戏进程；作为 sidecar 引入违背玩家零依赖目标。本轮采纳其事件流设计要点（分阶段心跳、显式 error surfacing、所有等待可取消有 deadline），完整事件流重构另立任务。详见 .trellis/tasks/09-22-pr196-live-fixes/plan.md。

### 实机复测注意
**必须用本分支重新构建的 DLL 并重启游戏**——v0.15.0 及更早的 release 构建里不含双层决策代码。复测清单见 plan.md 末尾。
