# 检查记录

## 已核验的既有结果

- `build/gameplay-convergence-20261114/python-final.log`：Python unittest 共 457 项，通过。
- `build/gameplay-convergence-20261114/gates.log`：12 项静态验证闸门通过。后续受影响闸门的复跑证据另见 `verification.md`。
- `build/gameplay-convergence-20261114/csharp-final.log`：1148 项通过、2 项失败；`Skill.PlayFlowUsesFreshState`、三项 `Parity.*` 和 `NativeMcp.Initialize` 均通过。两项失败均为真实 HTTP 监听测试，报“句柄无效”；`build/live-2026-10-27/quality-recheck-notes.md` 与该目录 `csharp-recheck.log` 记录了同名测试和相同环境故障。现有结果不能标成 C# 全绿。
- 游戏内模型选择 `PlayInGame` 或 `PlayInGameSingle`；新提示词目录的名称与两套 schema 的名称由 `PlayFlowMatchesTheAdvertisedInGameTools` 比对。外部初始化改为 `McpInstructions` 并引导无新鲜状态的客户端使用 `decide`。
- 后续计划步骤报 `outcome_unknown` 时，两套实现均根据已收到的步骤结果生成 `final_state`，所以它可能早于不确定步骤。
- 已修复 Python 计划的未知结果误分类：真实 transport 丢失载荷同时有 `error` 与 `status=outcome_unknown`，现在 `_stop` 优先判断未知状态。通过真实 `Sts2Client` 与模拟 transport 覆盖首步和后续步骤、动作提交次数及一次对账；普通错误仍报告 `rejected`。修复前复现及新增回归失败见 `review-plan-unknown-repro.log`、`python-unknown-red.log`，修复后证据见 `verification.md`。
- 本轮共享合同、外部指南和规格已要求同时检查 `plan.stop_reason` 与所有 `plan.steps[].status`，识别任一未知结果后先取新鲜状态；不再只靠停止原因判断。游戏内 `act` 的描述与 `then` schema 也已改为只计划已知步骤，停止于未知抽牌或选择。
- 未映射屏幕的 C#/Python playbook 索引原先要求每次动作后重读；已改成复用当前动作/等待结果或下一次注入的状态。新增检查覆盖这两个索引、各个游戏内屏幕片段和实际工具 schema 描述。
- 离线聚焦复验：C# 9/9、Python 场景指导 8/8，通过；C# 编译成功但保留一条不属于本轮差异的既有可空性警告。`arch-facts`、`doc-links`、`packaged-links` 和 `git diff --check` 均通过。具体命令与日志见 `verification.md`。
- 工具名称和参数保持不变；动作提交与停止流程未改，仅修正 Python 计划停止原因的分类顺序。规格中的 `PlayWithModelAsync`、`PlayInGame`、`PlayInGameSingle`、`McpInstructions` 和三项测试符号均已与实际源码核对。
- 所有上述结果为离线证据，不代表实机或模型效果。

## 未修复

- 最新 Python 全套 460 项中有 4 项 `test_knowledge` 因当前 Python 临时目录回退到仓库内而失败；需要可由 Python 写入且不在 checkout 内的临时目录复验。其余 456 项通过，相关计划与传输聚焦测试均通过，见 `verification.md`。
- C# 全套两项真实监听器测试在当前环境失败；需要可用的监听环境再验证，不能判为全绿。
