# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `skills/sts2-mcp-player/SKILL.md` | 共享契约块 Quick Start 的前两条改写：连接检查归外部 MCP 客户端/orchestrator，游戏内循环没有 `health_check`；游戏内从 `get_game_state -> get_available_actions -> act` 开始。marker 对未动 |
| `skills/sts2-mcp-player/references/screen-playbooks.md` | `:93` 的 debug `fight THE_ARCHITECT_EVENT_ENCOUNTER` 标注为 **external MCP only**（需 `STS2_ENABLE_DEBUG_ACTIONS=1`），并给出游戏内可达的替代（走事件自身非致命选项，绝不发 `PROCEED`） |
| `README.md`、`README.zh-CN.md` | `:183` 的工具清单改为「MCP 工具在内置自动游玩工具面之上多一个 `health_check`」，与 `AgentTools.cs` 对齐 |
| `STS2AIAgent.Tests/HealthCheckParityTests.cs` | 新增，3 条断言（Play 面不含 health_check / 内嵌提示只点名 Play 面工具 / MCP 面仍文档化 health_check 且 marker 成对） |
| `STS2AIAgent.Tests/TestRunner.cs` | 追加 3 行 `Parity.*` 注册 |

未触碰 `STS2AIAgent/Agent/**` 生产代码（`git status --porcelain -- STS2AIAgent/Agent/` 为空）。

## 验收

| 标准 | 证据 |
|---|---|
| 游戏内提示不再要求不存在的工具 | 契约块内 `health_check` 只剩一处，以「in-game play loop has no `health_check`」的非祈使语气出现；红/绿见下 |
| 工具名集合 ⊆ `AgentTools.Play` | `Parity.EmbeddedPromptUsesOnlyPlayTools`：对 `PlayPrompt.PlayContract` 与 `PlayPrompt.PlaySystem` 双份断言。把 SKILL.md:52 改回 `Call \`health_check\` once at session start.` → `FAIL ... the shared play contract instructs the in-game model to call a tool its play surface cannot dispatch: health_check`；还原后 PASS |
| playbooks 的 debug 指令标注 | `:93` 现文含 `external MCP only` 与 `STS2_ENABLE_DEBUG_ACTIONS=1` |
| `McpPlayerSkillTests` 保持绿 | `PASS Skill.McpPlayerContract`（仍断言 `ExtractSharedContract(skill) == PlayPrompt.PlayContract`） |
| SKILL.md:25 与 README 核对 | `:25` 是 MCP 侧 subagent 配置清单（含 `health_check`/`wait_for_event`），对 MCP 正确，保留；README 两语已改 |
| marker 成对 | `Parity.McpSurfaceKeepsHealthCheck` 断言 BEGIN/END 各恰好一次且先后有序 |
| 全量门禁 | C# 347 PASS / 0 FAIL；MCP 167 OK；6 gate 全绿；preflight exit 0 |

## 判定口径（实现选择，供复核）

「调用语气」的判定规则：同一句（按行 / `; ` / `. ` 切分）内出现调用类动词或 `->` 链，
且动词可被 `do not` / `never` / `instead of …` 之类否定形式豁免。
**没有**用「反引号标识符形状」判定——契约块里大量反引号是动作名（`choose_map_node`、
`collect_rewards_and_proceed` …），按形状判定会大面积误报。
候选工具名集合取自 `AgentTools.Mcp` 加显式外部专用名单（`health_check` / `wait_for_event` /
`run_console_command`）。

## 只能实机验证

- 游戏内模型是否真的不再发 `health_check`、从而每局少浪费一轮（离线只证提示文本不再要求它）。
- `PlayPrompt.PlaySystem` 每步重建注入在真实对局中的表现。

## 未动（MCP 侧正确表述）

`skills/sts2-mcp-player/README.md`、`references/remote-connection.md`、`references/debug-and-validation.md`
仍写 `health_check`——这些是 MCP 侧文档，平台确实有该工具，改动会引入新错误。

