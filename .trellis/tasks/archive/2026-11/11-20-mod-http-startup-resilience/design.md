# 技术设计：先恢复监听，再考虑降级

1. 在 Windows 监听器启动出现已复现的 `HttpListenerException.NativeErrorCode == 6` 时，换用不依赖 HTTP.sys 的回环传输。正常 `HttpListener`、指定端口策略、端口冲突错误保持原样；后备监听失败时保留两次的诊断。不把系统错误 6 当成端口冲突并盲目增加端口。
2. 首选使用已经通过独立 EXE 探针的 Kestrel，不自写 HTTP 解析器。先验证依赖随 Mod 打包、与 Godot 进程的 .NET 9 装载兼容。游戏 runtimeconfig 仅声明其自带 `Microsoft.NETCore.App`（配置版本 9.0.7），游戏目录不含 ASP.NET Core DLL；因此主项目不能假定系统里已装 ASP.NET Core。任何依赖必须由 Mod 提供，并通过 GitHub ZIP、Workshop 内容布局、双开离线 Mod 迁移的产物检查；不触碰游戏安装里的运行时或要求玩家装依赖。若依赖与现有游戏宿主/打包链不能安全共存，先报告阻碍和替代方案，不盲目迁移或把降级当恢复。
3. 两个监听器适配到同一套 Router/NativeMcpServer 的请求和响应契约；不复制业务路由。监听成功才发布端口、实例文件和队友启动就绪。测试需同时约束本机绑定、Host/Origin、队友令牌、请求上限、SSE 与取消/关闭，并检查不影响 pinned port 和已发布 schema。
4. 启动最终失败时，普通主窗继续初始化 Agent、原生守卫和悬浮窗；MCP 有效状态、双开门禁、UI 与诊断以真实监听状态为准，保留已存设置。队友启动失败仍走 `CompanionStartupFile` 的 `failed` 路径，不能继续加入大厅。仅处理预期的传输启动错误，其他初始化异常继续上报。
