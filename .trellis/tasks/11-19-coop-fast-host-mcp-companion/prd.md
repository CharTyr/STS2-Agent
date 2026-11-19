# 修复双开建房与外部 MCP 队友接入

## 目标

Slay the Spire 2 v0.113.1 的本机双开能正常组队。玩家不在 Mod 里填 Key 或验证模型，也能让 DSH 等外部 MCP 客户端操作第二个角色，真人继续操作主角色。

## 背景

- 反馈环境：Mod 0.19.2、游戏 v0.113.1。自动组队日志包含 `FastHost ENet lobby failed` 和 `Parameter count mismatch`。已用本机同版本游戏 DLL 在不启动游戏的情况下复现 `TargetParameterCountException: Parameter count mismatch.`：[现行调用](../../../STS2AIAgent/Game/GameActionService.cs)（`InvokeFastHostAsync`）对长度大于 1 的签名传空参数，但游戏的公开方法是 `FastHost(GameMode gameMode, bool isCustom)`。旧版声明为 `FastHost(GameMode gameMode)`。
- [现有「外部 Agent」队友模式](../../../README.zh-CN.md)（选项 C）已能跳过模型验证；[队友启动](../../../STS2AIAgent/Agent/AgentRuntime.cs)（`CompanionEntryAsync`）在进图后停在等待外部接管。当前邀请页仅显示 REST 地址，而无悬浮窗的第二窗口不会默认启用原生 MCP。
- [`POST /teammate/control`](../../../docs/api.md) 控制的是 Mod 内置自动游玩，不是外部 MCP 的开关。`running: true` 拒绝未验证模型符合既有契约。

## 要求与验收

| 编号 | 要求 | 可观察的验收结果 |
| --- | --- | --- |
| R1 | 修复本机标准双开的快速建房，保留旧版单参数兼容。 | 离线回归覆盖 `FastHost(GameMode)`、`FastHost(GameMode, bool)` 与不支持的签名。游戏 v0.113.1 实机邀请不再出现参数数量异常，队友加入标准 ENet 房间，双方 Ready 后进入同一局。 |
| R2 | 选「外部 Agent」后，让连接 MCP 的人能直接找到并复制队友实例的 MCP 地址。 | 主窗模型未验证且原生 MCP 未勾选时，外部模式队友仍能入队，展示实际队友端口的 `http://127.0.0.1:<port>/mcp`。`health_check` 返回 `instance_role=companion`，`decide` 读到队友当前局面，`act` 完成一次实际出牌。需要把 DSH 客户端连接实测与原生 MCP 协议直测分开记录。 |
| R3 | 保留角色隔离和内置自动游玩的语义。 | 外部出牌只改变队友手牌或资源，真人角色不被代打；队友 `play_phase=paused`、`session_requests=0`，主窗 MCP 开关不被改变。原本 `running:true` 仍要求已验证模型；普通自动游玩模式不因修复而默认启用 MCP。 |
| R4 | 补齐入口和说明。 | 多人页面明确提示把队友 MCP 地址填入 DSH，无须点「继续游玩」；README/API 说明自动游玩按钮和外部接管的区别，保留原生 MCP、现有 REST 及 Python sidecar 的使用方式。 |

## 范围外

不加新决策回调协议或新 MCP 工具，不改变公开 API 字段或调试门禁，不开放非本机访问，不改 DSH 本体，不自动发布、上传创意工坊或消耗模型配额。实机只使用隔离测试配置和存档，不关停正在玩的实例。完整通关不是验收前提。
