# PR #196 实机问题修复 — 开发计划

来源：用户实机测试 PR #196（双 tab overlay + Jev 双层决策 + 会话持久化）后的反馈。
工作分支：在 `feature/play-session-persistence` 之上开 `fix/pr196-live-issues`，完成后推送到该 PR 分支（或按仓库规则走 dev）。

## 问题清单与根因（已确认）

### P0 — 双层决策模式未生效（最严重）
- **根因 A（已确认）**：`AgentRuntime` 构造函数里 `decider: BuildJevDecider()` 只在**首次构造**时调用一次。玩家启动游戏后才在设置里填 Jev API Key → `_decider` 永远为 null → `AgentLoop.PlayOnceAsync` 的双层分支（`dualLayerOn && _decider is { } decider`）永远不成立 → 始终走 LLM 路径。**修复**：把 decider 改为按 settings 惰性重建（每次 PlayOnce 或 settings 保存时检查 fingerprint 变化）。
- **根因 B（已确认）**：`TestJevConnectionAsync` 是占位 stub，从不真正 ping（返回"连接测试将随双层决策引擎一同提供"）。**修复**：实现真实 `IJevClient.PingAsync`（GET /v1/models）。
- **待确认 C**：战利品阶段"秒选第一张"——需确认是 JevOptionEnumerator 对 REWARD 屏枚举只产一个选项、还是 LLM 路径的 fallback、还是 CompanionPlayPolicy。子代理报告中。

### P0 — 自动游玩几回合后卡死、无错误提示
- 现象：loop 停住，UI 无错误、无下一步、思考不更新。
- 候选根因：
  - LLM 请求默认 10 分钟超时，期间 UI 只显示"正在请求模型…"——看起来像卡死。
  - `WaitUntilActionableAsync` 20s 超时返回 `WaitingForGame=true`，recovery 每 1s 重试，无限循环——若游戏停在某个不可操作状态，永远"等待游戏可操作"，无错误、无升级。
  - 异常被吞的路径待子代理确认。
- **修复方向**：① 状态栏显示当前阶段+已等待时长；② WaitingForGame 连续 N 分钟后升级为可见警告；③ LLM 请求加可配置超时+进度心跳；④ 所有 catch 路径保证 `_status` 被更新。

### P1 — 思考内容不显示 / 应放到对话区
- 现状：思考（Reasoning）显示在"思考"卡片（`_playThought`，截断 400 字符）；对话区只显示 `AssistantText`，而自动游玩时模型通常只调工具不产文本 → 对话区空。
- **修复**：把每回合的 reasoning + 动作决策作为对话消息追加到对话区（可折叠/截断），"思考"卡片移除或改为显示当前状态。

### P1 — 对话显示上限 bug
- 现状：`_history` 上限 80 条（超出丢弃最旧），UI 只画最后 60 条（`ChatTailLimit`）。
- 用户报告"超出之后就不再显示"——需确认是 80 条 cap 的感知问题还是真的停止渲染。
- **修复**：对话区改为可滚动完整历史（RichTextLabel 本身可滚动），移除 60 条绘制上限或大幅提高；history cap 提高并加"已省略 N 条"提示。

### P1 — 模型设置重构
- ⑦ 测试连接改为**逐模型**：每个模型卡片上一个测试按钮，测试内容 = 真实调用（ping + 一次带工具定义的简单决策请求，验证工具调用能力）。
- ⑧ 测试成功后在该模型卡片旁显示醒目标识（✓ 已验证 badge），不仅限于首次配置栏文字。
- ⑨ 合并主对话模型/游玩模型为一个"主模型"；视觉辅助模型保留在高级设置。
- ⑩ 移除"工具调用"打勾选项（不支持工具调用的模型无法用于游玩，测试时自动检测并标记）。
- ⑪ 思考强度移到对话/游玩侧（游玩页加思考强度选择，实时生效）。

### P1 — 移除"允许代打"选项
- 对话区的"允许代打"checkbox 多余（已有自动游玩按钮）。移除，对话保持只读；要动手就用自动游玩/单步。

### P2 — 模型回复语言设置（中/英）
- 在设置或对话侧加"回复语言"选项，注入 system prompt（PlayPrompt / ChatAsync 的 system 消息）。

### P2 — 首次配置引导文案更新
- `FirstRunSetup` 与设置页"首次配置"段的文案按新双 tab UI 重写（游玩/设置 tab、主模型单一化、逐模型测试、Jev 配置位置）。

### P3 — agent loop 架构评估（pi agent）— 结论（2026-09-22）

- pi (earendil-works/pi) 是 **TypeScript/Node** 工具包（`pi-agent-core` / `pi-ai` / `pi-coding-agent`，npm 分发）。本 Mod 是游戏进程内的 C#/.NET 9 Godot 插件，**无法直接嵌入** pi；作为 sidecar 引入则要求玩家装 Node，违背"玩家零依赖"的产品目标（原生 MCP 面正是为了不装 Python 而存在）。
- **采纳其设计而非其代码**：pi-agent-core 的事件流模型（agent_start/turn_start/message_update/tool_execution_end/agent_end）、transformContext/convertToLlm 分离、beforeToolCall/afterToolCall 钩子、finishTurn 显式终局——对应到本轮修复：分阶段心跳（PlayPhases + StatusWithElapsed）≈ 事件流的最小可用版；异常类型保留 + NoteEvent ≈ 显式 error surfacing；GameThread 超时/取消 ≈ "所有等待都有 deadline 且可取消"。
- 完整事件流重构（把 `AutoPlayRecovery` 的隐式 `(StopReason, Delay)` 元组换成显式状态机、`CompleteWithToolsAsync` 抽成事件流内核）另立任务，不在本轮范围。

## 实机复测要点（给用户）

1. **必须用本分支重新构建的 DLL**（v0.15.0 及更早的 release 构建里根本没有双层决策代码）。构建：`scripts/build-mod.ps1 -Configuration Release`（已执行并部署到游戏 mods 目录），然后重启游戏。
2. 双层决策：设置页填 Jev Base URL + API Key → 点「测试 Jev 连接」（现在是真实 ping）→ 游玩页开「双层决策模式」→ 开始自动游玩。决策记录里 Jev 执行的动作为 `来源：jev` 并带置信度；Jev 面板显示最近选择与概率。
3. 战利品选牌：奖励屏遇到卡牌奖励时会**停下等决策**（不再自动拿第一张）；LLM/Jev 显式选牌或跳过后才继续。
4. 卡死排查：状态栏现在显示当前阶段+已等待秒数；连续等待超过 2 分钟会停机并给出可见原因。
5. 对话区：思考内容（勾选「显示思考内容」）与每个动作决策都以气泡进入对话流；对话上限 200 条，超出时顶部显示"已省略更早的 N 条"。
6. 模型设置：每张模型卡片有「测试」按钮（真实调用 + 工具调用检测），通过显示 ✅ 徽标；主模型一个即可（对话与游玩共用）；视觉模型在「显示高级选项」里；思考强度与回复语言在游玩页对话卡上直接调。


## 执行顺序

1. P0 双层决策修复（decider 惰性重建 + 真实 Jev ping + 战利品枚举确认）
2. P0 卡死修复（状态可见性 + 等待升级 + 错误 surfacing）
3. P1 思考进对话 + 对话上限修复
4. P1 模型设置重构（逐模型测试+badge、单一主模型、移除工具调用勾选、思考强度移位）
5. P1 移除允许代打
6. P2 语言设置
7. P2 首次配置文案
8. 构建 + 单测 + preflight，更新 PR #196

## 验证

- `dotnet run --project STS2AIAgent.Tests`（新增/更新单测：decider 重建、Jev ping、对话 cap、设置迁移）
- `powershell scripts/build-mod.ps1 -Configuration Release`（需先关游戏）
- preflight-release.ps1
- 实机验证由用户进行
