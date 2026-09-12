# 证据

## 交付

| 文件 | 改动 |
|---|---|
| `STS2AIAgent.Tests/ApiExceptionTests.cs`（新） | 3 条行为测试 |
| `STS2AIAgent.Tests/JsonHelperTests.cs`（新） | 2 条行为测试 |
| `STS2AIAgent.Tests/HttpServerPortPolicyTests.cs`（新） | 3 条源码契约测试 |
| `docs/api.md` | 屏幕枚举表补 6 行：`MULTIPLAYER_LOBBY`、`BUNDLE_SELECTION`、`CAPSTONE_SELECTION`、`CARDS_VIEW`、`UNLOCK`、`TIMELINE`（实测缺失数多于审计报告列的 3 个） |
| csproj + TestRunner | 已由主代理注册（编译项 + 8 条测试名） |

生产代码零改动。

## 验收

| 标准 | 证据 |
|---|---|
| 8 条测试按注册名通过 | `PASS ApiException.CarriesStatusAndCode / RetryableDefaultsToFalse / DetailsAreOptional`、`PASS JsonHelper.PascalCaseIndented / CaseInsensitiveRead`、`PASS HttpServerPort.ExplicitNeverDrifts / AutoIncrementFlagged / FallbackGate` |
| 可否证 | 把 `JsonHelper` 的 `WriteIndented` 改 false → `JsonHelper.PascalCaseIndented` 红；对 `HttpServer.cs` 的**副本**做三处变异（`PortWasAutoIncremented=false`、`allowIncrement=true`、环境变量名改错）→ 对应契约测试各自变红；仓库生产文件零残留 |
| 文档 | `git diff -- docs/api.md` 仅屏幕表 6 行新增；`<!-- BEGIN ACTION CONTRACT -->` 块零 diff，gate 报 `55 actions match` |

## 门禁

C# **PASS=311 FAIL=0**；`check_verification_gates.py` 通过；`check_release_package.py --source-root .` 通过；preflight 通过。

## 覆盖边界（明确写清，避免下轮重复评估）

- `HttpServer` 的三条测试是**源码文本契约**，不是运行时行为：`HttpServer.Start()` 依赖 `MegaCrit…Log` 与 `Router`，离线不可编译。运行时保障仍来自 `LoopbackListenerTests`（7 条）的间接覆盖。
- 屏幕名来自 `GameStateService.ResolveNonModalScreen` 的静态映射，未在真实游戏里逐个触发核对。
- 屏幕清单不是 gate 校验项，未来仍可能漂移（动作契约才是硬校验）。
