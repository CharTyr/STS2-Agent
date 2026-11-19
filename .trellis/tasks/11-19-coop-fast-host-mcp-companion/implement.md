# 实施与验证计划

1. 审阅批准后从最新 `origin/dev` 建功能分支。确认未跟踪的 Trellis 计划不与目标分支冲突，保留用户未提交内容。激活本任务并确认当前任务指向本目录。
2. 先写离线回归：模拟旧签名、新签名和不支持的同名重载；覆盖选择标准非自定义模式且只 dispatch 一次。记录旧版实参复现和测试在修复前的失效信号。给外部启动路由与 MCP 开关联动补测试，验证只影响选中的队友实例，设置同步和重邀不串线。
3. 改建房的签名选择，接入现有游戏线程和有期限的选角等待。接线启用外部队友进程的原生 MCP；从实际队友连接显示和复制地址。不引入另一套出牌协议。
4. 维护中英本地化、README、API 说明、项目地图、`CHANGELOG.md` 的 `Unreleased`。必要时更新注册、编译链接、源文件预算和新增反射边界的可执行契约；不修改发布版本。
5. 运行 `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release`、`dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj -c Release`、`uv run --locked python -m unittest discover -s tests -v`（工作目录为 MCP sidecar）、`python scripts/check_verification_gates.py`。对不能通过的检查定位是否是基线或环境问题；不上调测试预算来掩盖回归。
6. 实机优先使用隔离部署：以 Windows v0.113.1 和无 Key 的测试设置启动主窗，外部模式邀请，确认 ENet 大厅、玩家信息和真实队友 MCP 地址。通过 MCP 查健康及局面，实际在队友手上执行并核对一次出牌，检查主角手牌独立、队友内部模型请求计数为零、主窗 MCP 保持原设定。确认测试 Mod 版本和 DLL SHA256；只有 DSH 真正连通时才记 DSH 实测通过。记录所有未运行或阻塞项，不自动重试未知结果。退出自己的测试进程，不重启用户实例。
7. 按问题分开查看差异：建房适配可用旧签名用例单独复核；外部接入回退不会影响建房；不改内置自动游玩认证或公开端点协议。保存离线和实机证据，报告未发布。
