# 验证记录（2026-11-19）

环境：游戏 v0.113.1（`release_info.json` commit d09d48e5），构建对应 DLL
`04CFF87677D6DD847A62AEB25D0ACA22802A4ACBEC37C062D3D949AC1212BA53`。

## 离线

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| `dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release`（对 v0.113.1 程序集） | PASS，0 警告 0 错误 | `build/live-coop-1131/build-mod.log` |
| FastHost 兼容回归（新签名 / 旧签名 / 拒绝未知重载 / 派发后不重试 / 接线） | 5/5 PASS；修复前同一用例以 `TargetParameterCountException: Parameter count mismatch.` 变红 | `STS2AIAgent.Tests/FastHostCompatibilityTests.cs`，红绿日志见会话记录 |
| C# 全量（`dotnet run --project STS2AIAgent.Tests`） | 1157 PASS / 9 FAIL / 2 SKIP；9 条失败与修复前红基线完全一致（设置存档与日志写入被环境拒绝、无有效句柄的 HttpListener），不是本改动引入 | `build/live-coop-1131/tests-mcp-red.log`、`tests-green.log` |
| Python MCP（`python -m unittest discover -s tests`） | 462 跑 1 失败：`test_posix_script_portability` 要求 `scripts/setup-cloud-dev.sh` source 共享库；该脚本属另一并行会话的未提交改动，本分支改动不涉及 | `build/live-coop-1131/python-tests-nosync.log` |
| `python scripts/check_verification_gates.py` | 全部通过（api-doc / api-facts / api-schema / arch-facts / doc-links / doc-marks / docs-tracked / lockfile / packaged-links / ps1-syntax / script-encoding）；`sh-syntax` 本机无可用 bash，跳过 | 会话输出 |

## 实机（隔离副本）

在 `build/live-coop-1131/game` 的完整隔离副本上启动 v0.113.1，用户真实安装与存档未改动
（`--force-steam off`、独立 `--clientId 3911261301`、`APPDATA` 指向隔离目录）。

- Mod 加载并完成初始化（DLL + PCK 均来自本次构建）。
- **BLOCKED**：该运行环境让 Mod 的本机 HTTP 监听启动失败——
  `System.Net.HttpListenerException (6): 句柄无效`，`LoopbackListener.Bind → HttpServer.Start`。
  这是既有 `HttpListener` 依赖在受限进程环境下的失败，发生在本改动之前、与 FastHost 无关；
  没有 `/health` 就没有 API，因此双开建房与队友 MCP 的实机验收**未执行**，不得记为 PASS。
- 结束方式：只关闭本次隔离测试进程，退出码 0。

## 待补的实机验收（需在正常桌面进程环境执行）

1. 主菜单邀请 AI 队友 → 不再出现 `Parameter count mismatch`，队友进入标准 ENet 大厅，双方 Ready 进同一局。
2. 「外部 Agent」模式、Mod 不填 Key：主窗口多人页出现 `http://127.0.0.1:<队友端口>/mcp`，
   `health_check` 报 `instance_role=companion`，`decide` 读到队友局面，`act` 实际出一张牌。
3. 队友 `play_phase=paused`、`session_requests=0`；主窗口 MCP 开关未被改动；真人角色不被代打。
4. DSH 客户端连接与 MCP 协议直测分别记录，不互相替代。
