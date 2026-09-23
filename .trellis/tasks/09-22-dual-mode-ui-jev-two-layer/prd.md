# 双模式 UI 重组与 Jev 双层决策模式

## Goal

把游戏内覆盖层从「功能散在 6 个 tab」重组为以**游玩**为中心的两大模式体验，并为两种游玩方式引入**双层决策模式**：Jev（TypeSafe System One 模型）负责实际操作游玩，LLM 只根据游戏状态做战略规划、为 Jev 调整游玩策略。初版仅适配 Jev。

## 背景与需求来源

用户核心诉求（2026-09-22 会话）：

1. 该 mod 本质上只提供两种游玩方式：
   - **模式一（单人）**：游戏内覆盖层让 LLM 操作游戏游玩；或通过 MCP 让任意支持 MCP 的第三方 coding agent 操作游戏游玩。两者体验应完全一致——覆盖层只是让不想额外开 coding agent 的玩家更方便地开始。
   - **模式二（多人）**：游戏内覆盖层、或 MCP 第三方 coding agent，作为一名**独立玩家**加入一场多人游戏（本地/在线），与其他玩家（可包括本机用户自己）一起游玩。
2. UI 易用性整改：直接整理为清晰的两大块模式。
3. 两个模式都增加**双层决策模式开关**：开启后由 Jev 实际操作游玩，LLM 只负责根据当前游戏情况、敌人、卡组、状态等一切可得信息进行决策，为 Jev 调整游玩策略。
4. 参考案例：https://github.com/fhshaik/typesafe-mario （Jev 直接选择手柄输入的 Policy 模式）。

## 用户在评审中拍板的关键决策

- **UI 结构**：一次只能启用单人或多人其中一种模式，做成一个**滑块开关**切换单/多人，不再各占一个标签页；**接入（MCP）放进设置页的子选项页**；只保留**游玩页**，页内放对话。
- **对话即决策流**：对话直接输出模型的决策信息、操作结果（本质是 LLM 工具调用信息、回复的返回），可选开启模型思考内容显示；用户可在对话内直接与 LLM 对话影响决策、暂停/继续游玩。
- **会话即存档**：一个游戏 run 存档 = 一次对话 session，可中途保存退出，下次继续游玩、切换模型时保留上下文（指游戏内「继续游戏」的 run 存档，非整个游戏存档）。
- **双层决策开启时**：对话 UI 下方额外一块 UI 展示 Jev 模型的输出内容。
- **低置信度处理**：回退 LLM 逐手决策（confidence-gated routing，符合 TypeSafe 官方模式）。
- **MCP 双层形态**：Jev 循环永远在 mod 内跑，外部 coding agent 只做规划层（通过新增 MCP 工具调策略），保证两种接入方式体验严格一致。
- **Trellis 流程**：建任务 + 完整规划产物（prd + design + implement）。

## 任务地图（子任务）

| 子任务 | 交付物 | 独立可验证 |
|---|---|---|
| `09-22-overlay-two-tab-reorg` | 覆盖层重组为 游玩/设置 两 tab；游玩页内嵌模式滑块、对话、决策日志、Jev 面板；接入并入设置子页 | 是（纯 UI + 设置，不依赖 Jev 引擎） |
| `09-22-jev-two-layer-engine` | JevClient / IActionDecider / 选项枚举 / 策略存储 / 置信度门控接入 AgentLoop；MCP 双面 planner 工具 | 是（引擎 + 测试，UI 只留开关挂点） |
| `09-22-play-session-persistence` | 按 run 保存/恢复对话与决策上下文；切换模型保留上下文 | 是（持久化层，依赖子任务1的对话形态） |

**实施顺序**：子任务1（UI 骨架）与子任务2（Jev 引擎）可并行；子任务3 依赖子任务1的对话形态落地后再做。最终集成评审在父任务完成。

## 跨子任务验收标准

- [ ] 覆盖层只剩 **游玩 / 设置** 两个顶级 tab；游玩页顶部有 单人⇄多人 模式滑块，同一时刻只有一种模式激活。
- [ ] 单人模式：覆盖层内 LLM 游玩与外部 MCP coding agent 游玩能力一致（同一套状态/动作/决策日志面）。
- [ ] 多人模式：AI 作为独立玩家加入本地/在线多人游戏（沿用本地双开 companion 形态），与其他玩家同场游玩。
- [ ] 两种模式各有**双层决策开关**；开启后 Jev 执行实际操作，LLM 仅做战略规划并调整 Jev 策略。
- [ ] 双层开启时，对话下方展示 Jev 输出（所选动作、置信度、概率分布）。
- [ ] Jev 置信度低于阈值时回退 LLM 逐手决策，不中断整局。
- [ ] 外部 MCP agent 在双层模式下通过 `get_planner_briefing` / `update_play_strategy` 工具充当规划层；C# 原生面与 Python sidecar 工具面对齐（`test_native_tool_alignment.py` 通过）。
- [ ] 一个 run 的对话/决策上下文可保存、退出后恢复、切换模型不丢上下文。
- [ ] 所有既有契约闸门通过（OverlayTabContractTests / LocalizationTests / SourceShapeContractTests / api-facts / GameTaskBounding 等），新增代码有对应单测。
- [ ] `CHANGELOG.md` 的 `## Unreleased` 段记录全部玩家可见改动。

## 约束

- 遵守 `.trellis/spec/mod/` 全部边界：`IGameBridge` 缝、`GameThread.InvokeAsync` 游戏线程边界、禁裸 await（GameTaskBounding）、文件行数 ratchet（新代码进新文件）、本地化走 `Loc.T` 且英文条目入 `Loc.Strings.Ui.cs`、设置持久化走 `SettingsStore` + `SettingsClone` + `EnsureValidShape`。
- Jev HTTP 调用绝不在游戏线程 lambda 内 await；必须有显式超时与取消传播。
- Jev 每次调用计入 `SessionBudgetGuard`；usage 未知时保持 null（不塞 0）。
- API 向后兼容：不删既有字段；新增字段可空。
- 初版仅适配 Jev（`jev-latest`），但执行层抽象（`IActionDecider`）不锁死供应商。

## Notes

- 参考实现已克隆分析：`typesafe-mario` 的 `Policy` 协议（`choose(snapshot, actions) -> Decision`）、`TypeSafePolicy`（单次调用混合 Choice+Noul+Score）、runner 的异步提交与 JSONL 决策记录。
- TypeSafe API：`POST https://api.typesafe.ai/v1/systemone`，Bearer 认证；questions 支持 choice（criteria=选项→描述，≤255 项）/ score（2-10 级）/ noul（是非 0-1）；Choice/Score 返 confidence；429 需退避并尊重 retry-after；输入计费 $0.042/Mtok、输出免费。
