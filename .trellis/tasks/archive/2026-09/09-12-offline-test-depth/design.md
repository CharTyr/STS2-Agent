# 设计：能测什么、怎么测

## 1. `STS2AIAgent.Tests/ApiExceptionTests.cs`

编译 `..\STS2AIAgent\Server\ApiException.cs` 进测试工程，断言（对照 `ApiException.cs:5-20`）：

- `new ApiException(409, "invalid_action", "msg", details)` → `StatusCode==409`、`Code=="invalid_action"`、`Message=="msg"`；
- `Retryable` 默认 `false`，显式 `retryable: true` 时为 `true`；
- `Details` 允许 `null`（构造重载的实际签名以源码为准）；
- 是 `Exception` 的子类（`Assert.IsAssignableFrom<Exception>`）。

## 2. `STS2AIAgent.Tests/JsonHelperTests.cs`

编译 `..\STS2AIAgent\Server\JsonHelper.cs` 进测试工程：

- 自建 POCO（`PascalCase` 属性），序列化后文本包含 `"PascalName"` 与前缀 `{\n  ` —— 证明
  `PropertyNamingPolicy = null` + `WriteIndented = true`；
- 反序列化 `{"pascalname":...}`（全小写键）能取到值 —— 证明大小写不敏感；
- 断言必须否证：把 `WriteIndented` 改成 `false` 或用 camelCase 策略，测试要红。

若 `JsonHelper` 的方法签名是泛型 `Serialize<T>`/`Deserialize<T>`，按实际签名调用；
任何与源码不符的假设都改测试而不是改生产代码。

## 3. `STS2AIAgent.Tests/HttpServerPortPolicyTests.cs`（源码契约）

用 `AgentSourceFixture.Read("Server/HttpServer.cs")` + `MethodBody(...)`：

- `Start`/`StartAsync` 路径中存在 `PortWasAutoIncremented` 的写入，且写入条件含端口比较；
- `IsExplicitPortConfigured`（:173-179）读的是 `STS2_API_PORT` 环境变量；
- `AllowFallback`（:166-171）与 `ResolvePreferredPort`（:210-221）存在且被 `Start` 调用；
- 断言写成"删掉这一行即变红"的形式（例如断言 `WithoutWhitespace` 后的片段包含
  `PortWasAutoIncremented=started.Port!=preferredPort`）。

`LoopbackListener` 的真实端口行为已由 `LoopbackListenerTests`（7 条）覆盖，不重复。

## 4. `docs/api.md`

- 先 `rg -n 'BUNDLE_SELECTION|CAPSTONE_SELECTION|TIMELINE' docs/api.md` 核实；
- 缺则按仓库既有格式补进屏幕清单（保持原表格风格与字段顺序）；
- `<!-- BEGIN ACTION CONTRACT -->` 块内 55 个动作名逐字不动（有 gate 校验），改完跑 gate 确认。

## 债务记录（写进 research 与 evidence）

`HttpServer.cs` / `GameEventService.cs` / `UiFactory.cs` / `Router.cs` 当前**不可**离线编译，
原因分别是 Godot 节点实例化、`MegaCrit…Log`、以及 `Router` 拖入的游戏依赖闭包；
`GameThread.cs` 需先解耦日志才能拆出 `InvokeAsync` 契约。
