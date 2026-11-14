# 首批游玩流程证据

- 日期：2026-11-14
- 范围：共享游玩合同、游戏内提示词及工具目录，离线只读。
- Luna 研究未交付可用结果，已中止；以下为主会话针对已知文件逐点核实，非代理研究结论。

## 已核实

1. `AgentLoop.Decision.cs:176-195` 已注入当前屏 playbook/strategy，`:260-276` 注入新鲜状态及 ActNowInstruction，实际 tools 为 PlayInGame 或 PlayInGameSingle。
2. `AgentTools.cs:215-225` 在游戏内移除 get_game_state/get_raw_game_state/get_available_actions；`PlayPrompt.BuildPlaySystem` 的文字工具目录仍列出这些外部读取工具。
3. `SKILL.md` 共享合同 Quick Start 要求 get_game_state 后 act，并把 game-data 作为优先步骤；但开头已描述 card_cid/then，后部已说明 decide 聚合状态与场景指导，以及 act 返回状态复用。
4. `AgentTools.cs:183-191` 的元数据工具描述未表达“已有语义不重复查”；`:220-228` 已有整回合计划并逐步新鲜校验，不需要新增规划器。
5. `McpPlayerSkillTests.cs:18-57` 保护共享合同嵌入与关键屏幕词条可达；`AgentLoopTests.cs:1214,1227` 等保护前缀和最后动作指令；现有测试不能用放松安全规则的方式绕过。

## 优化假设与最小方案

- 把初始外部读取收敛到 decide；普通后续直接复用 act/wait 状态。游戏内直接使用注入状态，只在未知字段/过渡时补充读工具。
- 先用状态、描述、glossary 和已保留元数据识别语义；只在决定受未知信息影响时查 game-data。静态知识不覆盖实时合法性或数值。
- 共享合同明确一次 act 可带最多9个后续 combat 步骤（实际 schema 提供 then 时），抽牌/选择未知时缩短计划；pending/unknown/拒绝不得盲目重放。
- 统一游戏内实际工具目录与文字目录；不删除公开工具或参数，不改变 action 执行与 no-replay 行为。

## 证据边界

减少提示词与机械读取指导可离线证明；请求量、延迟、胜率的实际收益必须后续真实模型对局测量，本轮不作此宣称。
