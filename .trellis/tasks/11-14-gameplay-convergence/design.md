# 兼容的首批游玩流程收敛

## 边界

保留游戏状态/动作执行层、公开 guided/layered/full 工具集合和所有参数。改变模型的默认使用指导与相关描述，复用已有 decide、act、wait_until_actionable、card_cid/then 和按屏注入。另修正已复现的 Python 计划响应分类：未知结果优先于通用 error 分类，避免把不确定执行说成明确拒绝；不改变实际动作的提交与停止逻辑。

## 数据流与行为

- 外部首次或丢失上下文：decide 返回同帧状态、动作描述及场景指导；已有足够新鲜状态时不额外获取相同状态。
- 后续：读取 act 的 state，整回合计划读 final_state；过渡/pending 用 wait_until_actionable，再直接使用其 state。跨屏仅按需补场景指导；保留 static unchanged 标记与 full_state 恢复说明。
- 游戏内：使用 AgentLoop 已注入的新鲜状态与当前屏指导；PlaySystem 的工具目录仅列实际公布的工具。不得因共享外部指导诱导模型调用未公布读取工具。
- 知识：先读当前载荷描述、glossary 与本局已知元数据；仅补决策关键缺口，优先针对实体/批量而非每步全场景查询。实时伤害/可用性来自状态，元数据只解释机制。
- 规划：一次 act 可在 then schema 可用的战斗中携带最多9个后续步骤；未知抽牌/选择不预编动作；保留每步新鲜校验、稳定卡牌身份和所有停止条件。

## Skill 结构

主 SKILL 聚焦正常游玩主循环和共享安全合同；连接/宿主插件配置、诊断和角色分工放已有 references 或外部专用段，避免进入嵌入合同。保留必要 screen routing、关键坑、错误语义和规则可达性，不要求为缩短而删除唯一安全事实。

## 兼容与风险

公开协议不变；无数据库或保存格式迁移。不能误把静态元数据用于推导当前合法性；不能把读取工具完全移除，compact 缺字段时仍有 raw fallback。描述和共享合同必须兼容游戏内与外部 MCP 的能力差异。无真实对局预算，不验证胜率或服务商行为。

## 代理

项目 agents 默认与 research = gpt-6-luna/max；implement/check = gpt-6.1-terra/max。派生接口已实时更新角色模型绑定。

## 独立检查后发现的必要修复

`play_tools._stop` 原本先检查 error，而真实客户端丢失响应的返回同时含 `status=outcome_unknown` 与 error，因此计划后续步骤被标成 rejected。应先检查 status；测试通过公开计划入口覆盖首步和后续步骤，断言停止原因、步骤状态和动作调用次数。既有明确 error 仍是 rejected。未知后续步骤的 final_state 可能较旧，保留指导中检查 plan.steps[].status 并刷新状态的防御规则。
