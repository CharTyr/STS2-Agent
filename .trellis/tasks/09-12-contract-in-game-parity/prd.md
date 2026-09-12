# 让内嵌的游玩契约在游戏内可满足

## 背景

`skills/sts2-mcp-player/SKILL.md` 第 48–141 行是共享契约块（`<!-- BEGIN SHARED PLAY CONTRACT -->` 到
`<!-- END SHARED PLAY CONTRACT -->`，内嵌正文 50–139 行）。它被 `STS2AIAgent/STS2AIAgent.csproj:32-33`
作为 EmbeddedResource 打进 mod，由 `PlayPrompt.cs:43-47` 提取、`PlayPrompt.BuildPlaySystem()`
（`PlayPrompt.cs:57-71`）拼进**游戏内自动游玩**的系统提示（`AgentLoop.cs:115-119` 每步重建）。

问题：契约里的第一条硬性要求**在游戏内无法满足**。

```markdown
SKILL.md:52  Call `health_check` once at session start.
SKILL.md:53  ... with `health_check` only at session start ...
```

而游戏内工具集是 `AgentTools.Play`（`STS2AIAgent/Agent/AgentTools.cs:70-78`）= `ReadOnly`（7 个）+ `act`，
**不含 `health_check`**；`health_check` 只在 `AgentTools.Mcp`（`:80-83`，`:82` 定义），
由原生 MCP 端点使用（`NativeMcpServer.cs:414/454`）。

后果（已核实）：模型发出该调用 → `AgentLoop` 的 switch 无匹配 → `AgentLoop.cs:547` 返回
`{"error":"Unknown tool 'health_check'"}`。不崩溃，但每次会话白白消耗一轮；
若 8 轮内始终没产出 `act`，以 `AgentLoop.cs:435-437` 的 round-limit 错误收场。
更糟的是「自相矛盾」：同一段契约在 `PlayPrompt.cs:62` 明确列出了游戏内可用的工具清单，
而它上面那条要求点名了一个不在清单里的工具。

第二处同类问题：`skills/sts2-mcp-player/references/screen-playbooks.md:93` 要求用 debug
`fight THE_ARCHITECT_EVENT_ENCOUNTER` 进场——游戏内既没有 console 工具，
`act` 也没有 `command` 参数位（`AgentTools.cs:7-26`、`AgentLoop.cs:605-610`），
`run_console_command` 不在 `available_actions` 里（`GameStateService.cs:2528-2810`），
`act` 会在 `AgentLoop.cs:593-603` 直接判非法。该 playbook 也被内嵌（`csproj:33`）。

## 目标

让内嵌给游戏内模型的文本**只说它能做到的事**，同时不削弱外部 MCP 客户端的契约
（`mcp_server` 侧的 guided 工具集**确实**有 `health_check`，`server.py:619`）。

## 验收标准

- [ ] 游戏内系统提示不再要求调用游戏内不存在的工具：`health_check` 的措辞从"必须调用"
      改为对 in-game 循环成立的说法（例如说明该检查由外部 MCP 客户端/orchestrator 负责），
      且**不删除** `health_check` 在 MCP 侧的存在与文档。
- [ ] 内嵌文本里的工具名集合 ⊆ `AgentTools.Play` 的设备名集合（新增一条可否证的测试）。
- [ ] playbooks 里的 debug-only 指令明确标注"仅外部 MCP / 需 `STS2_ENABLE_DEBUG_ACTIONS=1`"，
      不再以游戏内可达的语气出现。
- [ ] `McpPlayerSkillTests`（`STS2AIAgent.Tests/McpPlayerSkillTests.cs:7-36`，断言 SKILL.md 与
      `PlayPrompt.PlayContract` 一致）保持绿，或按其真实约束更新。
- [ ] `skills/sts2-mcp-player/SKILL.md:25`（MCP 侧工具列表）与 README 的相应表述（若也不实）
      一并核对；`README.md:183`/`README.zh-CN.md:183` 声称"内置工具与游戏内自动游玩一致并含 health_check"
      与 `AgentTools.cs:70-78` 不符，需修正或说明。
- [ ] 两个 marker 行保持存在且成对（`PlayPrompt.ExtractSharedContract` 依赖它们）。
- [ ] C# / MCP 全量测试、gates、preflight 通过。

## 范围外

- 不给 `AgentTools.Play` 增加 `health_check`（in-game 循环本身就是进程内的，
  "检查 mod 连接"语义不成立；这是设计判断，已在 design 中说明）。
- 不改契约里其它已验证可满足的要求（探子逐条核对过：contract 点名的动作名全部存在于
  `BuildAvailableActionNames`，compact 字段也都在）。
