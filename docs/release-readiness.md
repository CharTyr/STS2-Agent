# 发布验收清单

- 更新时间：`2026-04-30`
- 目标版本：`v0.7.0`
- 目标：确认 `STS2 AI Agent` 已达到“多人主流程可发布、验证脚本可复跑、发布包可直接分发”的发布标准。

本次发布重点：

- 多人地图投票可由本地 AI 正确提交，不再丢失主机端首票。
- 多人休息点 `MEND` 已补齐目标元数据与 `target_index` 执行链路。
- 启动脚本、多人验证脚本、release 打包流程都已做成可重复执行的封版流程。

## 1. 静态门槛

以下命令必须通过：

```powershell
dotnet build "<repo-root>/STS2AIAgent/STS2AIAgent.csproj" -c Release
python -m py_compile "<repo-root>/mcp_server/src/sts2_mcp/client.py" "<repo-root>/mcp_server/src/sts2_mcp/server.py"
cd "<repo-root>/mcp_server"
uv run python -c "from sts2_mcp.server import create_server; create_server(); print('MCP_IMPORT_OK')"
```

也可以直接运行：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/preflight-release.ps1"
```

通过标准：

- Mod C# 项目可在 `Release` 配置下编译。
- MCP Python 源码可编译。
- MCP server 可成功导入并创建服务实例。
- `CHANGELOG.md` 与发布文档齐全，且版本信息已同步到 `0.7.0`。

## 2. 安装门槛

### Mod 安装

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/build-mod.ps1" -Configuration Release
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-mod-load.ps1" -DeepCheck
```

通过标准：

- `STS2AIAgent.dll` 已复制到游戏 `mods/` 目录。
- `STS2AIAgent.pck` 已复制到游戏 `mods/` 目录。
- 游戏启动后，`/health`、`/state`、`/actions/available` 都能正常返回。

### MCP 启动

```powershell
cd "<repo-root>/mcp_server"
uv sync
uv run sts2-mcp-server
```

通过标准：

- MCP server 可正常启动。
- MCP 客户端可成功调用 `health_check`。
- Mod 未启动时返回的错误信息可理解，不是异常崩溃。

## 3. 调试门槛

调试控制台必须保持“默认关闭，显式开启后才可用”：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1"
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-debug-console-gating.ps1" -EnableDebugActions
```

通过标准：

- 默认情况下，`run_console_command` 返回 `invalid_action`。
- 显式启用 `STS2_ENABLE_DEBUG_ACTIONS=1` 后，`run_console_command` 可用。
- MCP server 仅在启用对应环境变量时注册调试工具。

## 4. 单机主流程门槛

至少覆盖以下流程：

1. `MAIN_MENU -> open_character_select`
2. `select_character`
3. `embark`
4. 处理开局 `MODAL` / FTUE
5. `MAP -> choose_map_node`
6. `COMBAT -> play_card -> end_turn`
7. 战斗结束后进入奖励或回到地图

通过标准：

- `available_actions` 在各阶段暴露正确。
- 动作执行后状态转换稳定，不出现挂起请求。
- `state-invariants` 验证通过。

## 5. 多人主流程门槛

必须运行：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/test-multiplayer-lobby-flow.ps1"
```

必须覆盖：

1. Host 建房、Client 加入。
2. 双端选角与 ready。
3. 进入 run 后自动越过开场过场并到达地图。
4. 双端地图投票与投票状态回显。
5. 首场战斗中的出牌、回合推进、奖励结算。
6. 休息点 `MEND` 目标元数据暴露。
7. 未传 `target_index` 时返回 `invalid_target`。
8. 传入 `target_index` 后多人 `MEND` 在单次请求内完成。

通过标准：

- 脚本整体退出码为 `0`。
- Host 与 Client 都能推进到战斗后地图或休息点后的稳定状态。
- `map.local_vote`、`map.player_votes`、节点投票信息可见。
- `rest.options[*]` 对 `MEND` 暴露 `requires_target` 与有效目标索引。

## 6. 已知限制

以下限制已确认存在，但不阻塞 `v0.7.0` 作为发布版本：

- 在多人奖励结算后，Host 侧调试命令 `run_console_command: room RestSite` 仍可能因为游戏本体同步状态触发内部错误。
- 该问题只影响 debug 跳房验证，不影响正常 AI 接管下的多人主流程游玩。

## 7. 打包门槛

生成发布包：

```powershell
powershell -ExecutionPolicy Bypass -File "<repo-root>/scripts/package-release.ps1" -Configuration Release
```

发布目录与 zip 至少应包含：

- `mod/STS2AIAgent.dll`
- `mod/STS2AIAgent.pck`
- `mod/mod_id.json`
- `mcp_server/`
- `scripts/start-mcp-stdio.ps1`
- `scripts/start-mcp-network.ps1`
- `README.md`
- `CHANGELOG.md`
- `docs/release-readiness.md`

通过标准：

- release 目录生成成功。
- zip 生成成功。
- 根目录文档与脚本齐全，可直接交付给最终用户。
