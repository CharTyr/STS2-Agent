# 证据：测试工程的编译边界

## 事实

- `STS2AIAgent.Tests.csproj` **没有任何** `<Reference>`/`<PackageReference>`：纯 BCL 工程，
  `Godot`/`MegaCrit.*` 符号全部不可用；`internal` 类型可直接测（源码编入同程序集）。
- 已有能力：`AgentSourceFixture`（读源码做契约断言，:7-40）；
  `McpServiceTests` 能用 `LoopbackListener.Start(49170, true)` 起真实 loopback 服务
  （:213 / `:272-322`），但驱动的是 `NativeMcpServer`，不是 `Router`。

## 逐文件结论

| 文件 | 行数 | Godot | 游戏程序集 | 结论 |
|---|---|---|---|---|
| `Server/ApiException.cs` | 21 | 无 | 无 | 可编译可测 |
| `Server/JsonHelper.cs` | 24 | 无 | 无 | 可编译可测 |
| `Server/HttpServer.cs` | 223 | 无直接 | `Log`（:40/55/94/106/115/124/156/195） | 不可编译（还引用 `Router`） |
| `Server/GameEventService.cs` | 417 | 间接 | `Log` + `GameThread`/`GameStatePayload` | 不可编译 |
| `Ui/UiFactory.cs` | 179 | 直接（工厂方法 new 节点 + `Godot.Key`） | 无 | 不可编译 |
| `Game/GameThread.cs` | 171 | 仅 `WaitForNextFrame*`（:139-170） | `Log`（:27/31/54/61/112） | `InvokeAsync` 契约本身无 Godot，但被 `Log` 挡住 |
| `Server/Router.cs` | 466 | — | `MegaCrit…Debug/Logging` + Game/Agent/Multiplayer 全栈 | 不可编译 |

`GameThread.InvokeAsync` 的四条契约（未初始化抛 `InvalidOperationException`；
当前线程==捕获线程时内联返回已完成 Task；跨线程经 `_syncContext.Post`；异常回传）
只有在把 `MegaCrit…Log` 换成可注入委托后才可离线测 —— 本轮不做，记录为债务。

## `HttpServer` 与 `LoopbackListener` 的职责边界

`LoopbackListener` 只做纯端口选择/绑定/错误分类（文件头注释 :6-8 明说"port selection independent
of Godot"），已被 7 条测试覆盖；`HttpServer` 负责环境变量优选端口、是否允许 fallback、生命周期、
自增标记与 companion 端口发布，绑定委托给 `LoopbackListener.Start`（:44-51）。
因此本轮只对 `HttpServer` 做源码契约断言。

## `docs/api.md` 屏幕清单

审计报告：屏幕表缺 `BUNDLE_SELECTION`/`CAPSTONE_SELECTION`/`TIMELINE`（历史上就没登记）。
实现前先核实；动作契约块（55 个动作名）由 `check_verification_gates.py` 校验，不得改动。
