# 技术设计：标准大厅与队友 MCP

## 基线

本地 `main` 比远端落后，实施从包含 0.19.2 与最近重邀修复的 `origin/dev` 新建功能分支，不在 `main` 实施，不覆盖本地分支。当前已核对远端 `dev` 为 `049c2a0`，实施前再核对。

## 1. 建房兼容边界

- 在现有 `StartLocalFourPlayerHostAsync` 路径按**完整参数类型**选择游戏公开实例方法：先找 `(GameMode, bool)`，传 `GameMode.Standard, false`；没找到才查 `(GameMode)`，传 `GameMode.Standard`。不能按枚举顺序猜模式，不能因为失败而再次试另一个签名。找不到已知签名时在调用前报出明确的兼容信息，保留现有上层 debug fallback 和等待观测。
- 选择和调用参数构造收在小型、可单测的反射适配边界。保留游戏线程、四人 ENet、已有选角界面等待、队友入场、继续存档及重邀逻辑。没有公开 API 形状变化。
- 已最小复现：在 PowerShell 加载安装目录 `GodotSharp.dll`、`sts2.dll`，对 `NMultiplayerSubmenu.FastHost` 使用与旧分支相同的空数组执行 `MethodInfo.Invoke`；CLR 在进入 Godot 方法体之前抛出 `TargetParameterCountException: Parameter count mismatch.`。新版签名有两个参数；安装 DLL 与两份反编译签名可交叉核对。回归测试同时锁住方法选择与实参，包含一个同名但非兼容的重载。

## 2. 外部模式和可复制地址

- 沿用启动时实际采用的外部路由（子进程 `STS2_AGENT_ROLE=companion` 且 `STS2_AGENT_AUTOPLAY=0`），只给这个队友进程**本次会话**默认启用原生 `/mcp`。不要持久化这个默认覆盖，也不要改主窗口设置；队友运行中的 play/Jev 设置同步不能把它冲掉。已有本地 MCP 显式开关应仍能关闭本次会话的接口。自动游玩模式和普通主窗口继续遵循已有设置。
- 多人页使用启动后经过身份验证的 `CompanionConnection.Port` 和**实际运行的队友路由**给出 `http://127.0.0.1:<port>/mcp`，不硬编码端口或误用主窗地址；连接结束时不展示旧地址。复用现有复制按钮和中英本地化；说明把地址加入 DSH 的 Streamable HTTP MCP，外部客户端调用 `decide`/`act`，而「继续游玩」只启动内置模型。保留 HTTP 基础地址的兼容用法，不把队友令牌给外部客户端。
- 原生 MCP 和 Python sidecar 的工具列表及参数不变。两条路径继续走现有游戏动作层和 `CompanionActPolicy`，保留 loopback、Origin/Host、角色和单动作门禁。客户端每手主动发 `act`；游戏不会主动回调 DSH。若人工把内置模型启动，`/teammate/control` 仍按旧契约执行，外接工作流不调用它。

## 验证边界

实际游戏编译与 C#、MCP、文档门禁先跑。实机使用隔离用户和无 Key 的独立 Mod 设置；不操作现有存档、不启动付费模型、不覆盖玩家的 Mod 安装或全局 MCP 配置。只能完成协议直测时应明确说明 DSH 客户端是否连过，不能把一个测试替代另一个。
