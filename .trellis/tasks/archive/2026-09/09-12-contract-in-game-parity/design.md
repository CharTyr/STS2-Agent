# 设计

## 关键区分：同一个文件服务两类读者

| 读者 | 拿到的文本 | 工具集 |
|---|---|---|
| 游戏内自动游玩 | `PlayPrompt.PlaySystem` = 共享契约块（50–139）+ playbooks | `AgentTools.Play`（无 health_check） |
| 外部 MCP 客户端 | 整份 SKILL.md（含块外的 Quick Start、决策日志等） | `mcp_server` 的 guided 工具集（**有** health_check，`server.py:619`） |
| 原生 MCP（mod 内建端点） | SKILL.md 作为 `instructions`（`NativeMcpServer.cs:374`） | `AgentTools.Mcp`（**有** health_check） |

所以修法必须是"让措辞对游戏内成立"，而不是"把 health_check 从文档里删掉"。

## 改法

1. `SKILL.md` 的 Quick Start：把「Call `health_check` once at session start.」改成对
   **orchestrator 层**成立的说法，并点明 in-game 循环没有该工具、不要尝试。建议形态
   （实现时按行文风格定稿，但必须满足验收标准 1 与 2）：
   - 一句说明：连接检查属于外部 MCP 客户端/orchestrator 的职责；
   - in-game 循环直接从 `get_game_state` 开始。
2. 新增测试（`STS2AIAgent.Tests/`，由主代理注册）：
   `McpPlayerSkillTests` 里已有"SKILL.md 共享块 ↔ PlayPrompt.PlayContract 一致"的断言，
   新测试在此之上加一条**工具名包含性**断言：
   - 解析 `AgentTools.Play`（含 `ReadOnly`）定义里的工具名字面量；
   - 解析共享契约块里以反引号包裹、形如工具名的候选（并以 `PlayPrompt.PlaySystem` 里出现的为准）；
   - 断言契约块提到的**可调用工具名** ⊆ Play 集合；`health_check` 这类只属于 MCP 的名字
     必须以"非调用语气"出现（实现时用"是否出现在 Call/Use 之类的祈使上下文"判定，
     或更稳的做法：把契约块里允许出现的工具名做成显式白名单并断言两者一致）。
   断言必须可否证：临时把 `health_check` 改回祈使句 → 变红。
3. `screen-playbooks.md:93` 的 debug 指令：标注"仅外部 MCP（需 `STS2_ENABLE_DEBUG_ACTIONS=1`）"，
   并给出游戏内可执行的替代（例如按正常流程打完该幕）。
4. README 两语版本第 183 行的工具清单：与 `AgentTools.cs` 对齐（去掉 health_check 或说明它只属 MCP）。

## 陷阱

- 契约块被 `PlayPrompt.ExtractSharedContract` 按 marker `IndexOf` 截取，**marker 行本身不能删**，
  也不能新增第二对同名 marker。
- `McpPlayerSkillTests` 断言的是"文件内容与内嵌契约一致"，改 SKILL.md 后必须复跑它。
- `skills/**` 的改动会进入 mod 构建（EmbeddedResource），所以改完要跑
  `dotnet build STS2AIAgent/STS2AIAgent.csproj` 确认内嵌仍成立。
