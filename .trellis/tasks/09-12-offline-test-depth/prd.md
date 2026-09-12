# 给零覆盖的 C# 核心件补离线测试，并补全文档里的屏幕清单

## 背景

`STS2AIAgent.Tests` 是纯 BCL 工程（无 `<Reference>`/`<PackageReference>`），只能编译不依赖
Godot / MegaCrit 的源码。本轮探子逐个文件核了依赖，得到明确结论：

| 文件 | 能否离线测试 | 依据 |
|---|---|---|
| `Server/ApiException.cs` | **能**（零依赖） | 只用 BCL，`internal` 无障碍（源码直接编入测试程序集） |
| `Server/JsonHelper.cs` | **能**（零依赖） | 只用 `System.Text.Json` |
| `Server/HttpServer.cs` | 不能编译 | `MegaCrit…Log` + 依赖 `Router.HandleAsync/WriteErrorAsync` → `Router` 拖出整个游戏依赖闭包 |
| `Server/GameEventService.cs` | 不能编译 | `Log` + `GameThread`/`GameStatePayload` |
| `Ui/UiFactory.cs` | 不能编译 | 整文件实例化 Godot 节点 |
| `Game/GameThread.cs` | 只有 `InvokeAsync` 契约无 Godot | 但该段仍调用 `MegaCrit…Log`（:27/31/54/61/112），拆文件需先换掉日志调用 |

因此本轮能真正落地的离线覆盖是 `ApiException` 与 `JsonHelper` 两个**行为测试**，
加上 `HttpServer` 端口策略的**源码契约测试**（弱断言，但能防住"显式端口被静默漂移"这类回归）。

另外：`docs/api.md` 的屏幕表据审计缺 `BUNDLE_SELECTION`/`CAPSTONE_SELECTION`/`TIMELINE`
（历史遗留，前一轮已给 `/state` 新增这些屏幕名）。

## 目标

1. `ApiException`：断言 `StatusCode`/`Code`/`Details`/`Retryable` 与 `Message` 传递。
2. `JsonHelper`：断言序列化保留 PascalCase、缩进开启、反序列化大小写不敏感。
3. `HttpServer`：源码契约断言"显式端口不漂移、自增时打标记、允许 fallback 的分支存在"。
4. `docs/api.md`：补全缺失屏幕名（先核实，确缺才改），并保持动作契约块（55 个动作名）不被触碰。
5. 把"哪些文件为什么不能离线测"写进 `research` 与 evidence，避免下轮重复评估。

## 验收标准

- [x] 两个新测试文件 + `STS2AIAgent.Tests.csproj` 的 `<Compile Include>` + `TestRunner.cs` 注册。
- [x] 断言可否证：临时改坏被测行为（例如把 `JsonHelper` 的 `WriteIndented` 关掉）测试必须变红。
- [x] `docs/api.md` 屏幕清单补齐，动作契约块逐字未动（`check_verification_gates.py` 会校验）。
- [x] 全量门禁通过（C# 单测 / MCP 单测 / gates / package / preflight）。

## 范围外

- **不做** `GameThread` 拆分重构：需要先把 `InvokeAsync` 路径里的 `MegaCrit…Log` 调用换成可注入的
  日志委托，属行为可见的改动，收益（4 条契约）不足以在本轮承担风险；作为已知债务记录。
- 不给 `Router` 的错误信封做端到端测试（`Router` 拖入游戏依赖闭包；`McpServiceTests` 起的
  loopback 服务只驱动 `NativeMcpServer`）。
- 不改任何生产行为，只加测试与文档。
