# ADR 0002：两个 MCP 工具面，一份契约

状态：**已接受，暂不实施**（记录决定与触发条件）
日期：2026-09-17

> 后续补充（2026-09-21）：下表记录的是撰写当天的工具数。工具面此后继续增长——当前
> `guided` 档共 15 个工具，其中 14 个在两个面上同名同参数，`wait_for_event` 仍为 sidecar
> 专属。实时清单以 `scripts/test-mcp-tool-profile.ps1` 的 `ESSENTIAL_TOOLS` 为准，本文不再复述。

## 背景

这个 mod 有**两个 MCP 服务端**，实现同一个工具面：

| | 位置 | 运行形态 | 工具 |
| --- | --- | --- | --- |
| 原生 | `STS2AIAgent/Server/NativeMcpServer.cs`（952 行）+ `AgentTools.Mcp` 的 schema | mod 进程内，`/mcp` 端点 | 9 |
| Sidecar | `mcp_server/src/sts2_mcp/server.py`（1102 行） | 独立 Python 进程，经 HTTP 调 mod | 9 + `wait_for_event` / `run_console_command` + layered / full 档的 legacy 工具 |

九个同名工具——`health_check`、`get_game_state`、`get_raw_game_state`、
`get_available_actions`、`wait_until_actionable`、`get_game_data_item`、
`get_game_data_items`、`get_relevant_game_data`、`act`——在两处各写了一遍：工具名、
输入 schema、参数名、描述文本。

**这是 ADR 0001 那个问题的跨语言版本**，但有一点决定性的不同：ADR 0001 的两份实现在同一个类里，
可以合并成一次遍历。这两份不行。原生面的存在理由就是**不需要装 Python**——订阅工坊的玩家
打开游戏就能用 `/mcp`；sidecar 的存在理由是 FastMCP 的生态、tool profile 分档和不随游戏进程
生死的连接。两者都得留着。

所以问题不是「合并实现」，而是：**这份契约该有几个真源？**

今天是两个，靠 `mcp_server/tests/test_native_tool_alignment.py`（438 行）三向比对撑住：
Python 注册的工具名 ↔ `AgentTools.Mcp` ↔ `ExecuteToolAsync` 的 switch 分支（双向），
Python 的 inputSchema ↔ C# switch 实际解析的 `arguments` 键，
`docs/api.md` 的冻结参数列 ↔ full 档 legacy 工具的 schema。

这个测试**现在是绿的，豁免表是空的**（`_DOC_ARGUMENT_EXEMPTIONS` 为 `{}`），
唯一的例外是 `run_console_command` 被刻意排除在 legacy 工具之外——它只在
`STS2_ENABLE_DEBUG_ACTIONS` 为真时单独注册。也就是说：两边今天**真的**一致。

## 为什么现在不动

和 ADR 0001 记录过的理由是同一条，但结论相反。

ADR 0001 的重复是**净损失**：910 行回答同一个问题，没有任何一方带来能力，而且文档在教人维护
重复。这里不是——两个实现各自换来一件真东西（零依赖 / 生态与分档），重复的只有那层**声明**。

而声明层的重复已经被机械比对住了，比对是双向的，豁免表是空的。把它换成生成式的单一清单
（一份 JSON/schema，C# 与 Python 各自读或各自生成）要付的代价是具体的：多一个构建步骤、
多一个产物要打包、`AgentTools.cs` 的强类型 schema 变成运行时解析、以及——最重要的——
**这条改动会同时动到玩家唯一不装任何东西就能用的那个入口**。

v0.12.4 的教训是：改 agent 能看见的判断面，离线全绿不算数。这里没有一个现成的实机基线
能证明「两个 MCP 面在重构前后行为一致」，做之前得先有。

## 决定

**保留两个实现，但把契约的一致性当成契约来守，而不是当成巧合。**

具体地：

1. `test_native_tool_alignment.py` 是这条缝的守卫，它不是可选的。它的三向比对与**空豁免表**
   是本 ADR 依赖的事实；往豁免表里加一条，就是在记录一次真实的分歧，必须写明理由与期限。
2. 新增一个 MCP 工具时两边都要加——这一点与 ADR 0001 相反，且必须显式写在 `AGENTS.md` 的
   步骤里，因为同一个仓库里两条相反的规则最容易记混。
3. 不为这层重复再写局部补丁式的契约测试。ADR 0001 的教训是：给重复打补丁会让重复看起来
   被管住了。要么靠这一个总的比对，要么就消除它。

## 触发条件（满足任一条，就重新评估）

- `_DOC_ARGUMENT_EXEMPTIONS` 里出现第一条**非临时**的豁免——说明两边已经无法真正对齐；
- 同名工具在两侧的**行为**（而不只是签名）出现分歧，且被实机发现；
- 工具数量超过 15，或任一侧的 schema 声明超过 300 行；
- 出现第三个消费方（例如一个独立的 TypeScript 客户端）需要同一份 schema。

前三条里任何一条成立，本 ADR 的「重复是可接受的」前提就不成立了。

## 如果要做，怎么做

按 ADR 0001 验证过的顺序，不要跳步：

1. 先有实机基线——两个 MCP 面各自 `tools/list` 与逐工具调用的完整采样，落盘；
2. 再抽出单一声明源（工具名、描述、inputSchema），两侧从它产出；
3. 再回放对照基线，两面零分歧才算完成；
4. `test_native_tool_alignment.py` 随之改形——从「比对两份实现」变成「断言两侧都无处安放
   自己的工具名或参数」，与 `ActionSurface.*` 今天的写法一致。

## 相关

- [ADR 0001：动作面只保留一份判断](0001-single-action-surface.md)
- [架构约束](../../.trellis/spec/mod/architecture.md)
- `mcp_server/tests/test_native_tool_alignment.py`
